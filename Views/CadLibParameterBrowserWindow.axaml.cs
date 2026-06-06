using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SpecStudioParser.CadLib;

namespace SpecStudioParser.Views
{
    public partial class CadLibParameterBrowserWindow : Window
    {
        public CadLibParameterBrowserWindow()
        {
            InitializeComponent();
            DataContext ??= new CadLibParameterBrowserViewModel();

            if (DataContext is CadLibParameterBrowserViewModel viewModel)
            {
                viewModel.RequestClose += () => this.Close();
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
