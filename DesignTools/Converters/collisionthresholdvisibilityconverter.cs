using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SpecStudioParser.DesignTools.Converters
{
    public class CollisionThresholdVisibilityConverter : IValueConverter
    {
        public static readonly CollisionThresholdVisibilityConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string id)
                return id == "collision-cleanup";
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
