using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SpecStudioParser.Models;
using SpecStudioParser.Services;
using SpecStudioParser.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpecStudioParser.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel? _subscribedViewModel;
        private DataGrid? _analyzerGrid;
        private DataGrid? _specGrid;

        public MainWindow()
        {
            InitializeComponent();

            // ХАК ДЛЯ nanoCAD: возвращает окно поверх пространства САПР при потере фокуса.
            this.Deactivated += (s, e) =>
            {
                if (this.IsVisible)
                {
                    this.Topmost = false;
                    this.Topmost = true;
                }
            };

            _analyzerGrid = this.FindControl<DataGrid>("AnalyzerGrid");
            _specGrid = this.FindControl<DataGrid>("SpecGrid");

            if (_analyzerGrid != null)
            {
                _analyzerGrid.SelectionChanged += AnalyzerGridSelectionChanged;
            }

            if (_specGrid != null)
            {
                _specGrid.SelectionChanged += SpecGridSelectionChanged;
            }

            DataContextChanged += MainWindowDataContextChanged;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void MainWindowDataContextChanged(object? sender, EventArgs e)
        {
            if (_subscribedViewModel != null)
            {
                _subscribedViewModel.OnColumnsStructureChanged -= RebuildDataGridColumns;
                _subscribedViewModel = null;
            }

            if (DataContext is MainWindowViewModel vm)
            {
                _subscribedViewModel = vm;
                _subscribedViewModel.OnColumnsStructureChanged += RebuildDataGridColumns;
                RebuildDataGridColumns();
            }
        }

        private void AnalyzerGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm || _analyzerGrid == null) return;
            vm.OnAnalyzerGridSelectionChanged(ExtractHandlesFromSelection(_analyzerGrid));
        }

        private void SpecGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm || _specGrid == null) return;
            vm.OnSpecGridSelectionChanged(ExtractHandlesFromSelection(_specGrid));
        }

        private static List<string> ExtractHandlesFromSelection(DataGrid grid)
        {
            if (grid.SelectedItems == null) return new List<string>();

            return grid.SelectedItems
                .Cast<object>()
                .Select(ExtractHandle)
                .Where(handle => !string.IsNullOrWhiteSpace(handle))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string ExtractHandle(object? selectedItem)
        {
            if (selectedItem is DwgObject dwgObject)
            {
                return dwgObject.Handle;
            }

            if (selectedItem is Dictionary<string, object> objectDictionary &&
                objectDictionary.TryGetValue("__Handle", out object? objectHandle))
            {
                return objectHandle?.ToString() ?? string.Empty;
            }

            if (selectedItem is Dictionary<string, string> stringDictionary &&
                stringDictionary.TryGetValue("__Handle", out string? stringHandle))
            {
                return stringHandle ?? string.Empty;
            }

            return string.Empty;
        }

        private void ApplyProfileFolderClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            try
            {
                ProfileStorageService.EnsureProfilesFolder(viewModel.RootProfilesPath);
                viewModel.RefreshAvailableXmlFilesList();
                viewModel.ConnectionStatus = $"Папка профилей применена: {viewModel.RootProfilesPath}";
            }
            catch (Exception ex)
            {
                viewModel.ConnectionStatus = $"Ошибка применения папки профилей: {ex.Message}";
            }
        }

        private void ImportXmlClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                ProfileImportService.ImportXmlWithHostDialog(viewModel);
                RebuildDataGridColumns();
            }
        }

        private void RebuildDataGridColumns()
        {
            if (DataContext is not MainWindowViewModel vm) return;

            var specGrid = this.FindControl<DataGrid>("SpecGrid");
            if (specGrid == null) return;

            specGrid.Columns.Clear();
            if (vm.ActiveProfile?.Datasets == null) return;

            var addedColumnCaptions = new List<string>();

            foreach (var dataset in vm.ActiveProfile.Datasets)
            {
                if (dataset.Columns == null) continue;

                foreach (var col in dataset.Columns.Where(c => c.Visible == 1))
                {
                    if (!addedColumnCaptions.Contains(col.Caption))
                    {
                        addedColumnCaptions.Add(col.Caption);

                        var dataGridColumn = new DataGridTextColumn
                        {
                            Header = col.Caption,
                            Binding = new Binding($"[{col.Caption}]") { Mode = BindingMode.OneWay },
                            IsReadOnly = true
                        };

                        specGrid.Columns.Add(dataGridColumn);
                    }
                }
            }
        }
    }
}