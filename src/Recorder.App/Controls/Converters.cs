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

/// <summary>True when the value is set (as for <see cref="VisibilityConverter"/>): enables a box only when it has something to edit.</summary>
public sealed class IsSetConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => InverseVisibilityConverter.IsSet(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>An enum value as a tab index and back: tabs listed in the order of the enum's values.</summary>
public sealed class EnumToIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Enum ? System.Convert.ToInt32(value, CultureInfo.InvariantCulture) : -1;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index && index >= 0 && targetType.IsEnum ? Enum.ToObject(targetType, index) : Binding.DoNothing;
}
