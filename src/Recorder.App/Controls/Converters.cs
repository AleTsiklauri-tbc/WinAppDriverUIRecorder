using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Recorder.App.Controls;

/// <summary>Visible when the value is false (or null, empty or zero when bound to objects, strings or counts).</summary>
public sealed class InverseVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        IsSet(value) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static bool IsSet(object? value) => value switch
    {
        null => false,
        bool flag => flag,
        string text => text.Length > 0,
        int count => count > 0,
        _ => true,
    };
}

/// <summary>Visible when the value is true, non-null, a non-empty string or a positive count.</summary>
public sealed class VisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        InverseVisibilityConverter.IsSet(value) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
