using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Recorder.App.Overlay;

internal sealed record ChipBitmap(byte[] Pixels, int Width, int Height);

/// <summary>Renders the label chip ("✓ Edit · txtAmount") with WPF text rendering at a monitor's DPI.</summary>
internal static class ChipRenderer
{
    private const double FontSize = 12;
    private const double GlyphSize = 11;
    private const double PaddingX = 7;
    private const double PaddingY = 3;
    private const double Gap = 5;
    private const double Radius = 4;

    private static readonly Typeface TextFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface GlyphFace = new(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static ChipBitmap Render(string text, string? glyph, Color background, double scale)
    {
        var label = Formatted(text, TextFace, FontSize, scale);
        var icon = glyph is null ? null : Formatted(glyph, GlyphFace, GlyphSize, scale);
        var iconWidth = icon is null ? 0 : icon.WidthIncludingTrailingWhitespace + Gap;
        var width = PaddingX + iconWidth + label.WidthIncludingTrailingWhitespace + PaddingX;
        var height = PaddingY + Math.Max(label.Height, icon?.Height ?? 0) + PaddingY;

        var visual = new DrawingVisual();
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale); // ClearType fringes on a transparent window
        using (var context = visual.RenderOpen())
        {
            context.DrawRoundedRectangle(Frozen(background), null, new Rect(0, 0, width, height), Radius, Radius);
            if (icon is not null)
            {
                context.DrawText(icon, new Point(PaddingX, (height - icon.Height) / 2));
            }

            context.DrawText(label, new Point(PaddingX + iconWidth, (height - label.Height) / 2));
        }

        var pixelWidth = (int)Math.Ceiling(width * scale);
        var pixelHeight = (int)Math.Ceiling(height * scale);
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[pixelWidth * pixelHeight * 4];
        bitmap.CopyPixels(pixels, pixelWidth * 4, 0);
        return new ChipBitmap(pixels, pixelWidth, pixelHeight);
    }

    private static FormattedText Formatted(string text, Typeface typeface, double size, double scale) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, Brushes.White, scale);

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
