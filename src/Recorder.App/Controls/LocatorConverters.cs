using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Recorder.App.Overlay;
using Recorder.App.Resources;
using Recorder.App.Services;
using Recorder.App.ViewModels;

namespace Recorder.App.Controls;

/// <summary>Locator quality → the overlay's colour, so badges and outlines always agree (spec 11.7).</summary>
public sealed class QualityBrushConverter : IValueConverter
{
    private static readonly Dictionary<LocatorQuality, SolidColorBrush> Brushes = Enum.GetValues<LocatorQuality>()
        .ToDictionary(quality => quality, quality => Frozen(HighlightPalette.ColorOf(LocatorText.Style(quality))));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Brushes[value is LocatorQuality quality ? quality : LocatorQuality.Unchecked];

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    internal static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>Locator quality → the overlay chip's icon (check, warning, cross, ellipsis while unknown).</summary>
public sealed class QualityGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is LocatorQuality quality ? HighlightPalette.GlyphOf(LocatorText.Style(quality)) ?? "\uE712" : "\uE712";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Stability meter: bar number (parameter 1–3) lit when the level reaches it.</summary>
public sealed class LevelOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int level && int.TryParse(parameter as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bar) && level >= bar ? 1.0 : 0.25;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Status bar dot for WinAppDriver: grey until checked, then green or red.</summary>
public sealed class ReachabilityBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Unknown = QualityBrushConverter.Frozen(Color.FromRgb(0x8A, 0x8A, 0x8A));
    private static readonly SolidColorBrush Reachable = QualityBrushConverter.Frozen(HighlightPalette.ColorOf(HighlightStyle.Unique));
    private static readonly SolidColorBrush Unreachable = QualityBrushConverter.Frozen(HighlightPalette.ColorOf(HighlightStyle.NoUnique));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        WinAppDriverReachability.Reachable => Reachable,
        WinAppDriverReachability.Unreachable => Unreachable,
        _ => Unknown,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ReachabilityTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        WinAppDriverReachability.Reachable => Strings.Status_WinAppDriver_Reachable,
        WinAppDriverReachability.Unreachable => Strings.Status_WinAppDriver_Unreachable,
        _ => Strings.Status_WinAppDriver_Unknown,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
