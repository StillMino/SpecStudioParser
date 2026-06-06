using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SpecStudioParser.CadLib;

namespace SpecStudioParser.Views
{
    public partial class CadLibParameterPickerWindow : Window
    {
        public CadLibParameterPickerWindow() : this(new CadLibParameterPickerOptions())
        {
        }

        public CadLibParameterPickerWindow(CadLibParameterPickerOptions options)
        {
            InitializeComponent();
            DataContext = new CadLibParameterPickerViewModel(options);
            Title = options.Title;

            if (DataContext is CadLibParameterPickerViewModel viewModel)
            {
                viewModel.RequestClose += OnRequestClose;
            }
        }

        public CadLibParameterPickerResult? Result { get; private set; }
        public CadLibParameterInfo? SelectedParameter => Result?.SingleParameter;

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

        private void OnRequestClose(CadLibParameterPickerResult? result)
        {
            Result = result;
            Close(result);
        }
    }
}
