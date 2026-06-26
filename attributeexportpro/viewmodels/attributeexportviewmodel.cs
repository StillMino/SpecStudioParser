using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpecStudioParser.AttributeExportPro.Models;
using SpecStudioParser.AttributeExportPro.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CadApp = HostMgd.ApplicationServices.Application;

namespace SpecStudioParser.AttributeExportPro.ViewModels
{
    public partial class AttributeExportViewModel : ObservableObject
    {
        private readonly AttributeScannerService _scanner = new();
        private readonly ExportWriter _writer = new();

        [ObservableProperty]
        private string _statusText = "Готов к работе. Нажмите «Сканировать чертёж».";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
        [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
        private bool _isBusy;

        [ObservableProperty]
        private string? _selectedBlockName;

        public ObservableCollection<string> FormatOptions { get; } = new() { "Excel (.xls)", "CSV (.csv)" };

        private string _selectedFormatOption = "Excel (.xls)";
        public string SelectedFormatOption
        {
            get => _selectedFormatOption;
            set
            {
                _selectedFormatOption = value;
                SelectedFormat = value.StartsWith("CSV") ? ExportFormat.Csv : ExportFormat.Xlsx;
                OnPropertyChanged();
            }
        }

        [ObservableProperty]
        private ExportFormat _selectedFormat = ExportFormat.Xlsx;

        [ObservableProperty]
        private bool _groupEnabled = true;
        [ObservableProperty]
        private bool _includeHeaderEnabled = true;
        [ObservableProperty]
        private bool _sortEnabled = true;

        private ExportData? _lastScanData;

        public ObservableCollection<string> BlockNames { get; } = new();
        public ObservableCollection<ExportColumnMapping> Columns { get; } = new();
        public ObservableCollection<CheckableTag> AvailableTags { get; } = new();

        [RelayCommand(CanExecute = nameof(CanScan))]
        public void Scan()
        {
            IsBusy = true;
            StatusText = "Сканирование чертежа…";

            try
            {
                var data = _scanner.ScanBlocks(string.IsNullOrWhiteSpace(SelectedBlockName) ? null : SelectedBlockName);
                _lastScanData = data;

                BlockNames.Clear();
                foreach (var n in data.BlockNames)
                    BlockNames.Add(n);

                AvailableTags.Clear();
                foreach (var tag in data.AllAttributeTags)
                    AvailableTags.Add(new CheckableTag { Tag = tag, IsSelected = true });

                // Авто-генерация колонок при первом сканировании
                if (Columns.Count == 0 && data.Rows.Count > 0)
                {
                    var autoCols = ExportWriter.AutoGenerateColumns(data);
                    foreach (var c in autoCols)
                        Columns.Add(c);
                }

                StatusText = $"Найдено блоков: {data.Rows.Count}, уникальных имён: {data.BlockNames.Count}. " +
                             $"Тегов атрибутов: {data.AllAttributeTags.Count}.";
            }
            catch (Exception ex)
            {
                StatusText = $"Ошибка сканирования: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanExport))]
        public async void Export()
        {
            if (_lastScanData == null) return;

            IsBusy = true;
            StatusText = "Формирование файла…";

            try
            {
                // Фильтруем колонки по AvailableTags (чекбоксы)
                var profile = new AttributeExportProfile
                {
                    Name = "Export",
                    TargetBlockName = SelectedBlockName,
                    Format = SelectedFormat,
                    GroupByBlockName = GroupEnabled,
                    IncludeHeader = IncludeHeaderEnabled,
                    SortByName = SortEnabled,
                    Columns = Columns.ToList()
                };

                // Если все теги сняты — используем авто-генерацию
                if (profile.Columns.Count == 0)
                {
                    profile.Columns = ExportWriter.AutoGenerateColumns(_lastScanData);
                }

                // Фильтруем данные по выбранным тегам
                var selectedTags = AvailableTags.Where(t => t.IsSelected).Select(t => t.Tag).ToHashSet();
                if (selectedTags.Count > 0)
                {
                    profile.Columns = profile.Columns
                        .Where(c => !c.SourceAttribute.StartsWith("$") || c.SourceAttribute is "$BLOCK_NAME" or "$LAYER")
                        .Concat(profile.Columns.Where(c => selectedTags.Contains(c.SourceAttribute) || !c.SourceAttribute.StartsWith("DYN.")))
                        .Distinct()
                        .ToList();
                }

                string ext = SelectedFormat == ExportFormat.Xlsx ? ".xls" : ".csv";

                var topLevel = TopLevel.GetTopLevel(ApplicationLifetimeUtils.GetMainWindow());
                string defaultName = $"attribute_export_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
                string defaultDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                string? filePath;
                if (topLevel != null && topLevel.StorageProvider.CanSave)
                {
                    var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = "Сохранить выгрузку атрибутов",
                        DefaultExtension = ext.TrimStart('.'),
                        SuggestedFileName = defaultName,
                        SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(new Uri(defaultDir))
                    });
                    filePath = file?.Path.LocalPath;
                }
                else
                {
                    filePath = Path.Combine(defaultDir, defaultName);
                }

                if (string.IsNullOrEmpty(filePath))
                {
                    StatusText = "Экспорт отменён.";
                    return;
                }

                if (SelectedFormat == ExportFormat.Csv)
                {
                    _writer.WriteCsv(_lastScanData, profile, filePath);
                }
                else
                {
                    _writer.WriteXlsxAsXml(_lastScanData, profile, filePath);
                }

                StatusText = $"Сохранено: {filePath} (строк: {_lastScanData.Rows.Count})";

                CadApp.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage(
                    $"\n[AttributeExportPro]: Экспорт завершён — {filePath}\n");
            }
            catch (Exception ex)
            {
                StatusText = $"Ошибка экспорта: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanScan() => !IsBusy;
        private bool CanExport() => !IsBusy && _lastScanData != null && _lastScanData.Rows.Count > 0;
    }

    public partial class CheckableTag : ObservableObject
    {
        [ObservableProperty] private string _tag = "";
        [ObservableProperty] private bool _isSelected = true;
    }

    /// <summary>
    /// Утилита для получения главного окна (безопасно для контекста nanoCAD).
    /// </summary>
    internal static class ApplicationLifetimeUtils
    {
        public static Window? GetMainWindow()
        {
            try
            {
                return Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;
            }
            catch { return null; }
        }
    }
}
