using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SpecStudioParser.CadLib;

namespace SpecStudioParser.Views
{
    public partial class CadLibParameterPickerWindow : Window
    {
        public CadLibParameterPickerWindow()
        {
            InitializeComponent();
            DataContext ??= new CadLibParameterPickerViewModel();

            if (DataContext is CadLibParameterPickerViewModel viewModel)
            {
                viewModel.RequestClose += OnRequestClose;
            }
        }

        public CadLibParameterInfo? SelectedParameter { get; private set; }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnParameterClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is CadLibParameterInfo parameter && DataContext is CadLibParameterPickerViewModel viewModel)
            {
                viewModel.SelectParameter(parameter);
            }
        }

        private void OnRequestClose(CadLibParameterInfo? parameter)
        {
            SelectedParameter = parameter;
            Close(parameter);
        }
    }
}
