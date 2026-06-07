using Avalonia.Controls;
using Avalonia.Interactivity;
using SpecStudioParser.CadLib;
using SpecStudioParser.Models;
using SpecStudioParser.Services;
using SpecStudioParser.ViewModels;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;

namespace SpecStudioParser.Views
{
    // ИСПРАВЛЕНО: Класс должен строго соответствовать имени окна настроек
    public partial class SpecificationSettingsWindow : Window
    {
        public SpecificationSettingsWindow()
        {
            InitializeComponent();

            Opened += (s, e) => BringSettingsWindowToFront();
            Activated += (s, e) => BringSettingsWindowToFront();

            // ЖЕЛЕЗНЫЙ ХАК ДЛЯ nanoCAD: предотвращает уход окна настроек за пространство САПР
            this.Deactivated += (s, e) =>
            {
                if (this.IsVisible)
                {
                    BringSettingsWindowToFront();
                }
            };
        }

        private void BringSettingsWindowToFront()
        {
            if (!IsVisible) return;

            Topmost = true;
            Activate();
            Focus();
        }

        protected override void OnDataContextChanged(System.EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.SelectedDataset?.EnsureRootFilterItems();

                // Безопасно подписываемся на ручной вызов перестройки структуры колонок из ViewModel
                viewModel.OnColumnsStructureChanged -= RebuildDataGridColumns;
                viewModel.OnColumnsStructureChanged += RebuildDataGridColumns;

                // Фикс стрелочек: Подписка на динамическое изменение коллекции в памяти
                if (viewModel.SelectedDataset?.Columns != null)
                {
                    viewModel.SelectedDataset.Columns.CollectionChanged -= OnColumnsCollectionChanged;
                    viewModel.SelectedDataset.Columns.CollectionChanged += OnColumnsCollectionChanged;
                }
            }
        }

        private void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Если элемент внутри ObservableCollection переместили кнопками Выше/Ниже (Move), обновляем UI
            if (e.Action == NotifyCollectionChangedAction.Move)
            {
                RebuildDataGridColumns();
            }
        }

        /// <summary>
        /// Принудительно заставляет Avalonia DataGrid перерисовать строки в новом порядке
        /// </summary>
        private void RebuildDataGridColumns()
        {
            if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedDataset == null)
                return;

            // Находим таблицу FieldsGrid на форме настроек
            var fieldsGrid = this.FindControl<DataGrid>("FieldsGrid");
            if (fieldsGrid == null) return;

            // Быстрое переприсвоение ItemsSource сбрасывает визуальный кэш отображения строк
            var currentSource = fieldsGrid.ItemsSource;
            fieldsGrid.ItemsSource = null;
            fieldsGrid.ItemsSource = currentSource;
        }

        private void ImportXmlClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                ProfileImportService.ImportXmlWithHostDialog(viewModel);
                viewModel.SelectedDataset?.EnsureRootFilterItems();
                RebuildDataGridColumns();
            }
        }

        private async void AddRootConditionClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedDataset == null)
            {
                return;
            }

            var parameters = await PickFilterParametersAsync("Выбор CADLib параметров для корневых условий");
            if (parameters.Count == 0)
            {
                return;
            }

            AddRootConditions(viewModel.SelectedDataset, parameters);
        }

        private void AddFilterGroupClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel && viewModel.SelectedDataset != null)
            {
                viewModel.SelectedDataset.AddChildFilterGroup();
            }
        }

        private void GroupSelectedRootFilterItemsClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel && viewModel.SelectedDataset != null)
            {
                viewModel.SelectedDataset.GroupSelectedRootFilterItems();
            }
        }

        private void RunFilterDiagnosticsClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel && viewModel.SelectedDataset != null)
            {
                viewModel.SelectedDataset.RunFilterIntegrityDiagnostics();
            }
        }

        private async void AddConditionToGroupClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button ||
                button.DataContext is not FilterConditionGroup group ||
                DataContext is not MainWindowViewModel viewModel ||
                viewModel.SelectedDataset == null)
            {
                return;
            }

            var parameters = await PickFilterParametersAsync("Выбор CADLib параметров для условий группы");
            if (parameters.Count == 0)
            {
                return;
            }

            AddConditionsToGroup(viewModel.SelectedDataset, group, parameters);
        }

        private void AddNestedGroupToGroupClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is FilterConditionGroup group)
            {
                group.AddGroup();
            }
        }

        private async Task<IReadOnlyList<CadLibParameterInfo>> PickFilterParametersAsync(string title)
        {
            var result = await CadLibParameterPickerService.PickMultipleAsync(
                this,
                title,
                "Выберите один или несколько параметров. Для каждого выбранного параметра будет создано отдельное условие.");

            return result?.SelectedParameters ?? [];
        }

        private static void AddRootConditions(DatasetConfig dataset, IReadOnlyList<CadLibParameterInfo> parameters)
        {
            dataset.EnsureRootFilterItems();
            foreach (var parameter in parameters.Where(HasSystemName))
            {
                var condition = CreateConditionFromParameter(parameter, GetLastRootItemJoinWithNext(dataset));
                dataset.RootFilterItems.Add(FilterRootItem.FromCondition(condition));
            }

            dataset.RunFilterIntegrityDiagnostics();
        }

        private static void AddConditionsToGroup(DatasetConfig dataset, FilterConditionGroup group, IReadOnlyList<CadLibParameterInfo> parameters)
        {
            group.EnsureItems();
            foreach (var parameter in parameters.Where(HasSystemName))
            {
                var condition = CreateConditionFromParameter(parameter, GetLastGroupItemJoinWithNext(group));
                group.Items.Add(FilterGroupItem.FromCondition(condition));
            }

            dataset.RunFilterIntegrityDiagnostics();
        }

        private static FilterConditionItem CreateConditionFromParameter(CadLibParameterInfo parameter, string joinWithNext)
        {
            return new FilterConditionItem
            {
                Attribute = parameter.SystemName,
                JoinWithNext = joinWithNext
            };
        }

        private static bool HasSystemName(CadLibParameterInfo parameter)
        {
            return !string.IsNullOrWhiteSpace(parameter.SystemName);
        }

        private static string GetLastRootItemJoinWithNext(DatasetConfig dataset)
        {
            var lastItem = dataset.RootFilterItems.LastOrDefault();
            if (lastItem?.Condition != null)
            {
                return lastItem.Condition.JoinWithNext;
            }

            if (lastItem?.Group != null)
            {
                return lastItem.Group.JoinWithNext;
            }

            return "and";
        }

        private static string GetLastGroupItemJoinWithNext(FilterConditionGroup group)
        {
            var lastItem = group.Items.LastOrDefault();
            if (lastItem?.Condition != null)
            {
                return lastItem.Condition.JoinWithNext;
            }

            if (lastItem?.Group != null)
            {
                return lastItem.Group.JoinWithNext;
            }

            return "and";
        }

        private void RemoveFilterConditionClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button ||
                button.DataContext is not FilterConditionItem condition ||
                DataContext is not MainWindowViewModel viewModel ||
                viewModel.SelectedDataset == null)
            {
                return;
            }

            viewModel.SelectedDataset.RemoveFilterCondition(condition);
        }

        private void RemoveRootFilterItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.DataContext is FilterRootItem item &&
                DataContext is MainWindowViewModel viewModel &&
                viewModel.SelectedDataset != null)
            {
                viewModel.SelectedDataset.RemoveRootFilterItem(item);
            }
        }

        private void RemoveNestedFilterGroupClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.DataContext is FilterConditionGroup group &&
                DataContext is MainWindowViewModel viewModel &&
                viewModel.SelectedDataset != null)
            {
                viewModel.SelectedDataset.RootFilterGroup.RemoveGroup(group);
            }
        }

        private void DissolveNestedFilterGroupClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.DataContext is FilterConditionGroup group &&
                DataContext is MainWindowViewModel viewModel &&
                viewModel.SelectedDataset != null)
            {
                viewModel.SelectedDataset.DissolveNestedFilterGroup(group);
            }
        }

        private void PromoteNestedFilterGroupClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.DataContext is FilterConditionGroup group &&
                DataContext is MainWindowViewModel viewModel &&
                viewModel.SelectedDataset != null)
            {
                viewModel.SelectedDataset.PromoteNestedFilterGroup(group);
            }
        }

        private void GroupSelectedNestedFilterItemsClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.DataContext is FilterConditionGroup group &&
                DataContext is MainWindowViewModel viewModel &&
                viewModel.SelectedDataset != null)
            {
                viewModel.SelectedDataset.GroupSelectedNestedFilterItems(group);
            }
        }

        private void MoveNestedFilterGroupItemUpClick(object sender, RoutedEventArgs e)
        {
            MoveNestedFilterGroupItem(sender, -1);
        }

        private void MoveNestedFilterGroupItemDownClick(object sender, RoutedEventArgs e)
        {
            MoveNestedFilterGroupItem(sender, 1);
        }

        private void MoveNestedFilterGroupItem(object sender, int direction)
        {
            if (sender is not Button button ||
                button.DataContext is not FilterGroupItem item ||
                DataContext is not MainWindowViewModel viewModel ||
                viewModel.SelectedDataset == null)
            {
                return;
            }

            viewModel.SelectedDataset.MoveNestedFilterGroupItem(item, direction);
        }

        private void RemoveFilterGroupClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.DataContext is FilterConditionGroup group &&
                DataContext is MainWindowViewModel viewModel &&
                viewModel.SelectedDataset != null)
            {
                FilterRootItem? rootItem = null;
                foreach (var item in viewModel.SelectedDataset.RootFilterItems)
                {
                    if (item.Group == group)
                    {
                        rootItem = item;
                        break;
                    }
                }

                if (rootItem != null)
                {
                    viewModel.SelectedDataset.RemoveRootFilterItem(rootItem);
                }
                else
                {
                    viewModel.SelectedDataset.RootFilterGroup.RemoveGroup(group);
                }
            }
        }

        private void CloseWindowClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
