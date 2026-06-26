using Avalonia.Controls;
using SpecStudioParser.DynamicBlockDoctor.ViewModels;

namespace SpecStudioParser.DynamicBlockDoctor.Views
{
    public partial class DynamicBlockDoctorWindow : Window
    {
        public DynamicBlockDoctorWindow()
        {
            InitializeComponent();
            DataContext = new DynamicBlockDoctorViewModel();
        }
    }
}
