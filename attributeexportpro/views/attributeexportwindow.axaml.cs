using Avalonia.Controls;
using SpecStudioParser.AttributeExportPro.ViewModels;

namespace SpecStudioParser.AttributeExportPro.Views
{
    public partial class AttributeExportWindow : Window
    {
        public AttributeExportWindow()
        {
            InitializeComponent();
            DataContext = new AttributeExportViewModel();
        }
    }
}
}
