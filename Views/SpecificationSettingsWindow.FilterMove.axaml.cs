using Avalonia.Controls;
using Avalonia.Interactivity;
using SpecStudioParser.Models;
using SpecStudioParser.ViewModels;

namespace SpecStudioParser.Views
{
    public partial class SpecificationSettingsWindow
    {
        private void MoveRootFilterItemUpClick(object sender, RoutedEventArgs e)
        {
            MoveRootFilterItem(sender, -1);
        }

        private void MoveRootFilterItemDownClick(object sender, RoutedEventArgs e)
        {
            MoveRootFilterItem(sender, 1);
        }

        private void MoveRootFilterItem(object sender, int direction)
        {
            if (sender is not Button button ||
                button.DataContext is not FilterRootItem item ||
                DataContext is not MainWindowViewModel viewModel ||
                viewModel.SelectedDataset == null)
            {
                return;
            }

            var items = viewModel.SelectedDataset.RootFilterItems;
            var oldIndex = items.IndexOf(item);
            var newIndex = oldIndex + direction;

            if (oldIndex >= 0 && newIndex >= 0 && newIndex < items.Count)
            {
                items.Move(oldIndex, newIndex);
            }
        }
    }
}