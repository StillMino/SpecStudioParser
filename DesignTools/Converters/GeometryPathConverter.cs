using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace SpecStudioParser.DesignTools.Converters
{
    public sealed class GeometryPathConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string path || string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return StreamGeometry.Parse(path);
            }
            catch
            {
                return null;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
