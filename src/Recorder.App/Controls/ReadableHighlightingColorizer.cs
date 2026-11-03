using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;

namespace Recorder.App.Controls;

/// <summary>
/// Syntax colours that stay readable in light and dark themes. AvalonEdit's Java and C# colours are made for a white page
/// (black dots, dark blue keywords), so a colour with less than 4.5:1 contrast against the editor's background (spec 11.8)
/// is moved towards white, or black, until it has it.
/// </summary>
internal sealed class ReadableHighlightingColorizer(IHighlightingDefinition definition, Func<Color> background) : HighlightingColorizer(definition)
{
    private const double MinimumContrast = 4.5;
    private readonly Dictionary<Color, SolidColorBrush> _brushes = [];

    public static Color Readable(Color color, Color background)
    {
        var toward = Contrast(Colors.White, background) >= Contrast(Colors.Black, background) ? Colors.White : Colors.Black;
        for (var step = 0; step < 12 && Contrast(color, background) < MinimumContrast; step++)
        {
            color = Color.FromRgb(Mix(color.R, toward.R), Mix(color.G, toward.G), Mix(color.B, toward.B));
        }

        return color;
    }

    protected override void ApplyColorToElement(VisualLineElement element, HighlightingColor color)
    {
        base.ApplyColorToElement(element, color);
        if (color.Foreground?.GetColor(CurrentContext) is not { } foreground)
        {
            return;
        }

        var readable = Readable(foreground, background());
        if (readable != foreground)
        {
            element.TextRunProperties.SetForegroundBrush(Brush(readable));
        }
    }

    // WCAG 2.1 contrast ratio from relative luminance.
    private static double Contrast(Color a, Color b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color color) => (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static byte Mix(byte from, byte to) => (byte)Math.Round(from + ((to - from) * 0.2));

    private SolidColorBrush Brush(Color color)
    {
        if (!_brushes.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush(color);
            brush.Freeze();
            _brushes[color] = brush;
        }

        return brush;
    }
}
