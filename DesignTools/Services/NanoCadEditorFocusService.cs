using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using CadApp = HostMgd.ApplicationServices.Application;

namespace SpecStudioParser.DesignTools.Services
{
    /// <summary>
    /// Modeless Avalonia palette keeps keyboard/mouse focus. Before Editor.GetSelection/GetPoint
    /// nanoCAD needs focus returned to the document area, otherwise the drawing can appear frozen
    /// until the user clicks in the workspace.
    /// </summary>
    internal static class NanoCadEditorFocusService
    {
        public static void PrepareForEditorInput()
        {
            try
            {
                var doc = CadApp.DocumentManager.MdiActiveDocument;
                var docWindow = TryGetPropertyValue(doc, "Window");
                var mainWindow = TryGetStaticPropertyValue(typeof(CadApp), "MainWindow");

                TryActivate(doc);
                TryActivate(docWindow);
                TryActivate(mainWindow);

                TryFocusHandle(TryGetWindowHandle(docWindow));
                TryFocusHandle(TryGetWindowHandle(mainWindow));
                TryFocusHandle(Process.GetCurrentProcess().MainWindowHandle);
            }
            catch
            {
            }

            // Give nanoCAD a short chance to process the focus change before starting an editor prompt.
            Thread.Sleep(80);
        }

        private static void TryActivate(object? target)
        {
            if (target == null)
            {
                return;
            }

            foreach (var methodName in new[] { "Focus", "SetFocus", "Activate", "BringToFront" })
            {
                try
                {
                    var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes);
                    method?.Invoke(target, Array.Empty<object>());
                }
                catch
                {
                }
            }
        }

        private static object? TryGetPropertyValue(object? target, string propertyName)
        {
            if (target == null)
            {
                return null;
            }

            try
            {
                return target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);
            }
            catch
            {
                return null;
            }
        }

        private static object? TryGetStaticPropertyValue(Type type, string propertyName)
        {
            try
            {
                return type.GetProperty(propertyName, BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        private static void TryFocusHandle(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                ShowWindow(handle, ShowWindowCommand.Restore);
                BringWindowToTop(handle);

                var foreground = GetForegroundWindow();
                var currentThread = GetCurrentThreadId();
                var foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, IntPtr.Zero);
                var targetThread = GetWindowThreadProcessId(handle, IntPtr.Zero);

                if (foregroundThread != 0 && foregroundThread != currentThread)
                {
                    AttachThreadInput(currentThread, foregroundThread, true);
                }

                if (targetThread != 0 && targetThread != currentThread)
                {
                    AttachThreadInput(currentThread, targetThread, true);
                }

                SetForegroundWindow(handle);
                SetActiveWindow(handle);
                SetFocus(handle);

                if (targetThread != 0 && targetThread != currentThread)
                {
                    AttachThreadInput(currentThread, targetThread, false);
                }

                if (foregroundThread != 0 && foregroundThread != currentThread)
                {
                    AttachThreadInput(currentThread, foregroundThread, false);
                }
            }
            catch
            {
            }
        }

        private static IntPtr TryGetWindowHandle(object? target)
        {
            if (target == null)
            {
                return IntPtr.Zero;
            }

            foreach (var propertyName in new[] { "Handle", "Hwnd", "HWND", "WindowHandle" })
            {
                try
                {
                    var value = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);
                    if (value is IntPtr ptr)
                    {
                        return ptr;
                    }

                    if (value is long longValue && longValue != 0)
                    {
                        return new IntPtr(longValue);
                    }

                    if (value is int intValue && intValue != 0)
                    {
                        return new IntPtr(intValue);
                    }
                }
                catch
                {
                }
            }

            return IntPtr.Zero;
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, ShowWindowCommand nCmdShow);

        private enum ShowWindowCommand
        {
            Restore = 9
        }
    }
}
