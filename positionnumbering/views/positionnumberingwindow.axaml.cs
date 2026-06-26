using Avalonia.Controls;
using SpecStudioParser.PositionNumbering.ViewModels;

namespace SpecStudioParser.PositionNumbering.Views
{
    public partial class PositionNumberingWindow : Window
    {
        public PositionNumberingWindow()
        {
            InitializeComponent();
            DataContext = new PositionNumberingViewModel();
        }
    }
}
