using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SpecStudioParser.CadLib;
using System;

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
                viewModel.RequestCopy += CopyToClipboard;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private async void CopyToClipboard(string text)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(text) && Clipboard != null)
                {
                    await Clipboard.SetTextAsync(text);
                }
            }
            catch
            {
            }
        }
    }
}
