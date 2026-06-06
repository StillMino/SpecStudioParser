using Avalonia.Controls;
using System;
using System.Threading.Tasks;
using SpecStudioParser.Views;

namespace SpecStudioParser.CadLib
{
    public static class CadLibParameterPickerService
    {
        public static Task<CadLibParameterPickerResult?> PickSingleAsync(Window owner, string title = "Выбор параметра CADLib", string hint = "")
        {
            return PickAsync(owner, new CadLibParameterPickerOptions
            {
                Mode = CadLibParameterPickerMode.Single,
                Title = title,
                Hint = hint,
                CloseAfterSingleSelection = false
            });
        }

        public static Task<CadLibParameterPickerResult?> PickMultipleAsync(Window owner, string title = "Выбор параметров CADLib", string hint = "")
        {
            return PickAsync(owner, new CadLibParameterPickerOptions
            {
                Mode = CadLibParameterPickerMode.Multiple,
                Title = title,
                Hint = hint
            });
        }

        public static async Task<CadLibParameterPickerResult?> PickAsync(Window owner, CadLibParameterPickerOptions options)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            var window = new CadLibParameterPickerWindow(options);
            await window.ShowDialog(owner);
            return window.Result;
        }
    }
}
