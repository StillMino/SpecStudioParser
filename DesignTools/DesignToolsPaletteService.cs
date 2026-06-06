using Avalonia.Threading;
using HostMgd.ApplicationServices;
using HostMgdAvalonia.Windows;
using SpecStudioParser.DesignTools.ViewModels;
using SpecStudioParser.DesignTools.Views;
using System;
using CadApp = HostMgd.ApplicationServices.Application;

namespace SpecStudioParser.DesignTools
{
    public sealed class DesignToolsPaletteService
    {
        private PaletteSet? _paletteSet;
        private DesignToolsWindow? _window;
        private DesignToolsViewModel? _viewModel;

        public void Show()
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    EnsurePalette();
                    if (_paletteSet != null)
                    {
                        _paletteSet.Visible = true;
                    }
                }
                catch (Exception ex)
                {
                    WriteMessage($"\n[DesignTools]: Не удалось открыть панель: {ex.Message}\n");
                }
            });
        }

        private void EnsurePalette()
        {
            if (_paletteSet != null)
            {
                _viewModel?.RefreshContextCommand.Execute(null);
                return;
            }

            _viewModel = new DesignToolsViewModel();
            _window = new DesignToolsWindow
            {
                DataContext = _viewModel
            };

            _paletteSet = new PaletteSet("Инструменты проектировщика");
            _paletteSet.Add("Функции", _window);
        }

        private static void WriteMessage(string message)
        {
            try
            {
                CadApp.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage(message);
            }
            catch
            {
            }
        }
    }
}
