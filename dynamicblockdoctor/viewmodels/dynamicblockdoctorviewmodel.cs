using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpecStudioParser.DynamicBlockDoctor.Models;
using SpecStudioParser.DynamicBlockDoctor.Services;

namespace SpecStudioParser.DynamicBlockDoctor.ViewModels
{
    public partial class DynamicBlockDoctorViewModel : ObservableObject
    {
        private readonly BlockDiagnosticService _service = new();

        [ObservableProperty]
        private string _statusText = "Нажмите «Диагностика» для сканирования блоков.";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DiagnoseCommand))]
        [NotifyCanExecuteChangedFor(nameof(FreezeCommand))]
        private bool _isBusy;

        [ObservableProperty]
        private string _summaryText = "";

        [ObservableProperty]
        private int _errorCount;
        [ObservableProperty]
        private int _warningCount;
        [ObservableProperty]
        private int _totalBlocks;
        [ObservableProperty]
        private int _dynamicBlocks;

        private BlockDiagnosticReport? _selectedReport;
        public BlockDiagnosticReport? SelectedReport
        {
            get => _selectedReport;
            set
            {
                if (SetProperty(ref _selectedReport, value))
                {
                    SelectedIssues.Clear();
                    if (value != null)
                    {
                        foreach (var issue in value.Issues.OrderByDescending(i => i.Severity))
                            SelectedIssues.Add(issue);
                    }
                    FreezeCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<BlockDiagnosticReport> Reports { get; } = new();
        public ObservableCollection<BlockIssue> SelectedIssues { get; } = new();

        private DrawingDiagnosticSummary? _lastSummary;

        [RelayCommand(CanExecute = nameof(CanDiagnose))]
        public void Diagnose()
        {
            IsBusy = true;
            StatusText = "Диагностика блоков…";

            try
            {
                _lastSummary = _service.DiagnoseDrawing();

                Reports.Clear();
                // Сортируем: сначала ошибки, потом предупреждения
                foreach (var r in _lastSummary.Reports
                             .OrderByDescending(x => x.Issues.Count(i => i.Severity == BlockIssueSeverity.Error))
                             .ThenByDescending(x => x.Issues.Count(i => i.Severity == BlockIssueSeverity.Warning))
                             .ThenBy(x => x.BlockName))
                {
                    Reports.Add(r);
                }

                TotalBlocks = _lastSummary.TotalBlocks;
                DynamicBlocks = _lastSummary.DynamicBlocks;
                ErrorCount = _lastSummary.Errors;
                WarningCount = _lastSummary.Warnings;

                SummaryText = _lastSummary.Summary;
                StatusText = $"Готово. {Reports.Count} блоков проанализировано.";
            }
            catch (Exception ex)
            {
                StatusText = $"Ошибка: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanFreeze))]
        public void Freeze()
        {
            if (SelectedReport == null) return;
            if (!SelectedReport.IsDynamic)
            {
                StatusText = "Блок не динамический — заморозка не требуется.";
                return;
            }

            IsBusy = true;
            StatusText = $"Заморозка блока «{SelectedReport.BlockName}»…";

            try
            {
                string result = _service.FreezeDynamicBlock(SelectedReport.Handle);
                StatusText = result;

                // Пересканируем
                Diagnose();
            }
            catch (Exception ex)
            {
                StatusText = $"Ошибка заморозки: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        // При выборе отчёта — показываем его проблемы (обработано в setter SelectedReport)

        private bool CanDiagnose() => !IsBusy;
        private bool CanFreeze() => !IsBusy && SelectedReport != null && SelectedReport.IsDynamic;
    }
}
