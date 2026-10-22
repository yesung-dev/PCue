using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PCue.Converters;

/// <summary>true → Visible. ConverterParameter "Invert" flips it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter is "Invert")
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}

/// <summary>null / empty string → Collapsed. ConverterParameter "Invert" flips it.</summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value is string s ? s.Length > 0 : value is not null;
        if (parameter is "Invert")
            hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}

/// <summary>
/// [markerMs, maximum, sliderWidth] → Canvas.Left on the seek slider.
/// Must match the SeekSlider template: 8px side margin + 18px thumb.
/// </summary>
public sealed class TimelineMarkerConverter : IMultiValueConverter
{
    private const double Inset = 8 + 18 / 2.0;
    private const double MarkerHalfWidth = 1;

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 3
            || values[0] is not double ms
            || values[1] is not double max
            || values[2] is not double width
            || ms < 0 || max <= 0 || width <= Inset * 2)
            return -100.0;

        var ratio = Math.Clamp(ms / max, 0, 1);
        return Inset + ratio * (width - Inset * 2) - MarkerHalfWidth;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
