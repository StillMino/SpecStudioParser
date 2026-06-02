using System;
using System.Linq;
using HostMgd.EditorInput;
using HostMgd.ApplicationServices;
using Teigha.Runtime;
using SpecStudioParser.Views;
using SpecStudioParser.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform; // Для работы с IPlatformHandle
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CadApp = HostMgd.ApplicationServices.Application;

[assembly: CommandClass(typeof(SpecStudioParser.Commands.NanoCadCommands))]

namespace SpecStudioParser.Commands
{
    public static class NanoCadCommands
    {
        private static MainWindow? _currentWindow;
        private static bool _isAvaloniaInitialized = false;

        public static AppBuilder BuildAvaloniaApp()
        {
            var builder = AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();

            builder.AfterSetup(b =>
            {
                if (b.Instance != null && (b.Instance.Styles == null || b.Instance.Styles.Count == 0))
                {
                    var theme = new FluentTheme();
                    b.Instance.Styles.Add(theme);
                    b.Instance.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
                }
            });

            return builder;
        }

        [CommandMethod("SPEC_SHOW", CommandFlags.Session)]
        public static void ShowSpecStudioParser()
        {
            try
            {
                if (_currentWindow != null)
                {
                    _currentWindow.Activate();
                    return;
                }

                if (!_isAvaloniaInitialized)
                {
                    BuildAvaloniaApp().SetupWithoutStarting();
                    _isAvaloniaInitialized = true;
                }

                IntPtr nanoCadHwnd = CadApp.MainWindow.Handle;

                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        _currentWindow = new MainWindow();
                        var viewModel = new MainWindowViewModel();
                        _currentWindow.DataContext = viewModel;

                        _currentWindow.Closed += (s, e) => { _currentWindow = null; };

                        // Показываем окно стандартным образом
                        _currentWindow.Show();

                        // ЖЕЛЕЗНАЯ ПРИВЯЗКА К НАКАДУ ЧЕРЕЗ WIN32 API ПОСЛЕ ОТОБРАЖЕНИЯ ОКНА
                        // Это избавляет от поиска внутренних скрытых классов Avalonia
                        if (nanoCadHwnd != IntPtr.Zero)
                        {
                            var platformHandle = _currentWindow.TryGetPlatformHandle();
                            if (platformHandle != null && platformHandle.Handle != IntPtr.Zero)
                            {
                                IntPtr avaloniaHwnd = platformHandle.Handle;
                                NativeWin32.SetWindowLongPtr(avaloniaHwnd, NativeWin32.GWLP_HWNDPARENT, nanoCadHwnd);
                            }
                        }
                    }
                    catch (System.Exception innerEx)
                    {
                        LogToNanoCadConsole($"\n[SpecStudio UI Error]: Ошибка внутри потока интерфейса: {innerEx.Message}\n");
                    }
                });

                LogToNanoCadConsole("\n[SpecStudio]: Конструктор спецификаций успешно запущен.\n");
            }
            catch (System.Exception ex)
            {
                LogToNanoCadConsole($"\n[SpecStudio Error]: Критическая ошибка инициализации окна: {ex.Message}\n");
            }
        }

        [CommandMethod("SPEC_SCAN", CommandFlags.Modal)]
        public static void ExecuteFastScan()
        {
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            try
            {
                ed.WriteMessage("\n[SpecStudio]: Запуск фонового анализа чертежа...");
                var viewModel = _currentWindow?.DataContext as MainWindowViewModel ?? new MainWindowViewModel();
                viewModel.ScanAllCommand.Execute(null);
                ed.WriteMessage($"\n[SpecStudio]: Сканирование завершено. {viewModel.ConnectionStatus}\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[SpecStudio Error]: Ошибка фонового сканирования: {ex.Message}\n");
            }
        }

        private static void LogToNanoCadConsole(string message)
        {
            try
            {
                CadApp.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage(message);
            }
            catch { }
        }
    }

    /// <summary>
    /// Легковесный вспомогательный класс для работы с Win32 API напрямую.
    /// Гарантирует корректное поведение Z-Order окна поверх nanoCAD.
    /// </summary>
    internal static class NativeWin32
    {
        public const int GWLP_HWNDPARENT = -8;

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            else
                return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }
    }
}