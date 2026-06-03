using SpecStudioParser.ViewModels;
using System;
using System.IO;
using System.Linq;

namespace SpecStudioParser.Services
{
    public static class ProfileImportService
    {
        public static bool ImportXmlWithHostDialog(MainWindowViewModel viewModel)
        {
            try
            {
                var dialog = new HostMgd.Windows.OpenFileDialog(
                    "Импорт XML Model Studio",
                    string.Empty,
                    "xml",
                    "SpecStudioImportXml",
                    HostMgd.Windows.OpenFileDialog.OpenFileDialogFlags.NoUrls);

                if (dialog.ShowModal() != true)
                {
                    viewModel.ConnectionStatus = "Импорт XML отменён.";
                    return false;
                }

                var sourcePath = dialog.Filename;
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    viewModel.ConnectionStatus = "Ошибка импорта XML: файл не выбран или не найден.";
                    return false;
                }

                viewModel.ActiveProfile = MscsXmlService.LoadFromMscsXml(sourcePath);
                viewModel.SelectedXmlFile = ProfileStorageService.CopyProfileIntoStorage(sourcePath, viewModel.RootProfilesPath);

                viewModel.RefreshAvailableXmlFilesList();

                if (viewModel.ActiveProfile.Datasets.Any())
                {
                    viewModel.SelectedDataset = viewModel.ActiveProfile.Datasets.First();
                }

                viewModel.IsProfileLoaded = true;
                viewModel.OnColumnsStructureChanged?.Invoke();
                viewModel.ConnectionStatus = $"XML импортирован: {viewModel.SelectedXmlFile}";
                return true;
            }
            catch (Exception ex)
            {
                viewModel.ConnectionStatus = $"Ошибка импорта XML: {ex.Message}";
                return false;
            }
        }
    }
}