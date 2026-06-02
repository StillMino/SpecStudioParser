using Avalonia.Controls;
using Avalonia.Interactivity;
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

            // ЖЕЛЕЗНЫЙ ХАК ДЛЯ nanoCAD: предотвращает уход окна настроек за пространство САПР
            this.Deactivated += (s, e) =>
            {
                if (this.IsVisible)
                {
                    this.Topmost = false;
                    this.Topmost = true; // Сброс и повторный вызов заставляют Win32 поднять окно наверх
                }
            };
        }

        protected override void OnDataContextChanged(System.EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is MainWindowViewModel viewModel)
            {
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

        private void CloseWindowClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}