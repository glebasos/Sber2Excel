using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Sber2Excel.Converters;

/// <summary>Converts bool to green (true) or red (false) brush for amount coloring.</summary>
public class BoolToColorConverter : IValueConverter
{
    public static readonly BoolToColorConverter Instance = new();

    private static readonly IBrush GreenBrush = new SolidColorBrush(Color.Parse("#1A7F37"));
    private static readonly IBrush RedBrush = new SolidColorBrush(Color.Parse("#CF1322"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? GreenBrush : RedBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
