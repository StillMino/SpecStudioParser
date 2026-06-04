using Avalonia.Controls;
using Avalonia.Interactivity;
using SpecStudioParser.Models;
using SpecStudioParser.Services;
using SpecStudioParser.ViewModels;
using System.Collections.Specialized;

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

        private void AddRootConditionClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel && viewModel.SelectedDataset != null)
            {
                viewModel.SelectedDataset.AddRootFilterCondition();
            }
        }

        private void AddFilterGroupClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel && viewModel.SelectedDataset != null)
            {
                viewModel.SelectedDataset.AddChildFilterGroup();
            }
        }

        private void AddConditionToGroupClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is FilterConditionGroup group)
            {
                group.Conditions.Add(new FilterConditionItem());
            }
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
                    viewModel.SelectedDataset.RootFilterGroup.Groups.Remove(group);
                }
            }
        }

        private void CloseWindowClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}