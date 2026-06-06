using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SpecStudioParser.CadLib;

namespace SpecStudioParser.Views
{
    public partial class CadLibConnectionWindow : Window
    {
        public CadLibConnectionWindow()
        {
            InitializeComponent();

            if (DataContext == null)
            {
                DataContext = new CadLibConnectionViewModel();
            }

            if (DataContext is CadLibConnectionViewModel viewModel)
            {
                viewModel.RequestClose += OnRequestClose;
            }
        }

        public CadLibConnectionResult? Result { get; private set; }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnRequestClose(CadLibConnectionResult? result)
        {
            Result = result;
            Close(result);
        }
    }
}
