using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using SpecStudioParser.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpecStudioParser.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // ХАК ДЛЯ nanoCAD
            this.Deactivated += (s, e) =>
            {
                if (this.IsVisible)
                {
                    this.Topmost = false;
                    this.Topmost = true;
                }
            };

            DataContextChanged += (s, e) =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.OnColumnsStructureChanged -= () => RebuildDataGridColumns(vm);
                    vm.OnColumnsStructureChanged += () => RebuildDataGridColumns(vm);

                    // Обработка кликов в AnalyzerGrid через чистый string
                    var analyzerGrid = this.FindControl<DataGrid>("AnalyzerGrid");
                    if (analyzerGrid != null)
                    {
                        analyzerGrid.SelectionChanged += (sender, args) =>
                        {
                            if (analyzerGrid.SelectedItems != null)
                            {
                                var selectedStrings = analyzerGrid.SelectedItems
                                    .Cast<object>()
                                    .Select(x => x?.ToString() ?? string.Empty)
                                    .Where(s => !string.IsNullOrEmpty(s))
                                    .ToList();

                                vm.OnAnalyzerGridSelectionChanged(selectedStrings);
                            }
                        };
                    }

                    // Обработка кликов в SpecGrid через чистый string
                    var specGrid = this.FindControl<DataGrid>("SpecGrid");
                    if (specGrid != null)
                    {
                        specGrid.SelectionChanged += (sender, args) =>
                        {
                            if (specGrid.SelectedItems != null)
                            {
                                var selectedStrings = specGrid.SelectedItems
                                    .Cast<object>()
                                    .Select(x => x?.ToString() ?? string.Empty)
                                    .Where(s => !string.IsNullOrEmpty(s))
                                    .ToList();

                                vm.OnSpecGridSelectionChanged(selectedStrings);
                            }
                        };
                    }

                    RebuildDataGridColumns(vm);
                }
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void RebuildDataGridColumns(MainWindowViewModel vm)
        {
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