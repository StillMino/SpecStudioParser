using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SpecStudioParser.DesignTools.ViewModels
{
    /// <summary>Converts IsExpanded bool to chevron glyph.</summary>
    public class ExpandChevronConverter : IValueConverter
    {
        public static readonly ExpandChevronConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is true ? "▼" : "▶";

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Converts IsStub bool to opacity (stub = 0.55, real = 1.0).</summary>
    public class StubOpacityConverter : IValueConverter
    {
        public static readonly StubOpacityConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is true ? 0.55 : 1.0;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
