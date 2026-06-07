using System;
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
                TryActivate(doc);
                TryActivate(TryGetPropertyValue(doc, "Window"));
                TryActivate(TryGetPropertyValue(CadApp.DocumentManager, "MdiActiveDocument"));
                TryActivate(TryGetStaticPropertyValue(typeof(CadApp), "MainWindow"));
                TrySetForeground(TryGetPropertyValue(doc, "Window"));
                TrySetForeground(TryGetStaticPropertyValue(typeof(CadApp), "MainWindow"));
            }
            catch
            {
            }

            // Give nanoCAD a short chance to process the focus change before starting an editor prompt.
            Thread.Sleep(30);
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

        private static void TrySetForeground(object? target)
        {
            var handle = TryGetWindowHandle(target);
            if (handle != IntPtr.Zero)
            {
                try
                {
                    SetForegroundWindow(handle);
                }
                catch
                {
                }
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
    }
}
