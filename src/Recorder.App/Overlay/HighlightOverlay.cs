using System.Windows.Media;
using Recorder.Core.Model;

namespace Recorder.App.Overlay;

/// <summary>How a highlighted element is drawn: colour and icon by locator quality (spec 8.4), so it is never colour alone.</summary>
public enum HighlightStyle
{
    /// <summary>Under the cursor, quality not known yet (or not checked).</summary>
    Neutral,

    /// <summary>The best locator is unique and High: green, check mark.</summary>
    Unique,

    /// <summary>The best unique locator is Medium or Low (text, position): amber, warning sign.</summary>
    Medium,

    /// <summary>No unique locator: red, cross.</summary>
    NoUnique,

    /// <summary>Already captured: dashed blue, tag.</summary>
    Captured,
}

/// <summary>Overlay colours and chip icons. The app's badges use the same colours (Tokens.xaml, spec 11.7).</summary>
internal static class HighlightPalette
{
    public static Color ColorOf(HighlightStyle style) => style switch
    {
        HighlightStyle.Unique => Color.FromRgb(0x10, 0x7C, 0x10),
        HighlightStyle.Medium => Color.FromRgb(0x9D, 0x5D, 0x00),
        HighlightStyle.NoUnique => Color.FromRgb(0xC4, 0x2B, 0x1C),
        HighlightStyle.Captured => Color.FromRgb(0x00, 0x78, 0xD4),
        _ => Color.FromRgb(0x6B, 0x69, 0xD6),
    };

    public static string? GlyphOf(HighlightStyle style) => style switch
    {
        HighlightStyle.Unique => "\uE73E",
        HighlightStyle.Medium => "\uE7BA",
        HighlightStyle.NoUnique => "\uE711",
        HighlightStyle.Captured => "\uE8EC",
        _ => null,
    };
}

/// <summary>
/// Outlines one element on screen: four thin click-through edge windows around it and a label chip next to it,
/// flipped to stay on its monitor. It is the only thing that draws, and it leaves nothing behind when hidden.
/// UI thread only.
/// </summary>
public sealed class HighlightOverlay : IDisposable
{
    private const int DashLength = 6;

    private readonly LayeredWindow[] _edges = [new(), new(), new(), new()];
    private readonly LayeredWindow _chip = new();
    private (string Text, HighlightStyle Style, double Scale) _chipKey;
    private ChipBitmap? _chipBitmap;

    public bool IsVisible => _chip.IsVisible || _edges.Any(edge => edge.IsVisible);

    /// <summary>The overlay's window handles (top, bottom, left, right edge, then the chip), for tests.</summary>
    public IReadOnlyList<nint> WindowHandles => [.. _edges.Select(edge => edge.Handle), _chip.Handle];

    public void Show(PixelRect bounds, string label, HighlightStyle style)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (bounds.IsEmpty)
        {
            Hide();
            return;
        }

        var monitor = MonitorArea.For(bounds);
        var thickness = Math.Max(1, (int)Math.Round(2 * monitor.Scale));
        var color = HighlightPalette.ColorOf(style);
        var dashed = style == HighlightStyle.Captured;
        var outer = new PixelRect(bounds.X - thickness, bounds.Y - thickness, bounds.Width + (2 * thickness), bounds.Height + (2 * thickness));

        ShowEdge(_edges[0], new PixelRect(outer.X, outer.Y, outer.Width, thickness), color, dashed);
        ShowEdge(_edges[1], new PixelRect(outer.X, outer.Bottom - thickness, outer.Width, thickness), color, dashed);
        ShowEdge(_edges[2], new PixelRect(outer.X, outer.Y + thickness, thickness, outer.Height - (2 * thickness)), color, dashed);
        ShowEdge(_edges[3], new PixelRect(outer.Right - thickness, outer.Y + thickness, thickness, outer.Height - (2 * thickness)), color, dashed);

        var chip = ChipFor(label, style, color, monitor.Scale);
        _chip.Show(PlaceChip(outer, bounds, thickness, chip, monitor), chip.Pixels);
    }

    public void Hide()
    {
        foreach (var edge in _edges)
        {
            edge.Hide();
        }

        _chip.Hide();
    }

    public void Dispose()
    {
        foreach (var edge in _edges)
        {
            edge.Dispose();
        }

        _chip.Dispose();
    }

    /// <summary>Above the element; below it if there is no room above; inside its top edge as a last resort.</summary>
    internal static PixelRect PlaceChip(PixelRect outer, PixelRect bounds, int thickness, ChipBitmap chip, MonitorArea monitor)
    {
        var gap = thickness;
        var area = monitor.WorkArea;
        var y = outer.Y - gap - chip.Height;
        if (y < area.Y)
        {
            y = outer.Bottom + gap;
            if (y + chip.Height > area.Bottom)
            {
                y = bounds.Y + thickness;
            }
        }

        var x = Math.Clamp(outer.X, area.X, Math.Max(area.X, area.Right - chip.Width));
        return new PixelRect(x, y, chip.Width, chip.Height);
    }

    private static void ShowEdge(LayeredWindow window, PixelRect rect, Color color, bool dashed)
    {
        if (rect.IsEmpty)
        {
            window.Hide();
            return;
        }

        var horizontal = rect.Width >= rect.Height;
        var pixels = new byte[rect.Width * rect.Height * 4];
        for (var y = 0; y < rect.Height; y++)
        {
            for (var x = 0; x < rect.Width; x++)
            {
                var along = horizontal ? x : y;
                if (dashed && (along / DashLength) % 2 == 1)
                {
                    continue; // transparent gap
                }

                var i = ((y * rect.Width) + x) * 4;
                pixels[i] = color.B;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.R;
                pixels[i + 3] = 255;
            }
        }

        window.Show(rect, pixels);
    }

    private ChipBitmap ChipFor(string label, HighlightStyle style, Color color, double scale)
    {
        var key = (label, style, scale);
        if (_chipBitmap is null || _chipKey != key)
        {
            _chipBitmap = ChipRenderer.Render(label, HighlightPalette.GlyphOf(style), color, scale);
            _chipKey = key;
        }

        return _chipBitmap;
    }
}
