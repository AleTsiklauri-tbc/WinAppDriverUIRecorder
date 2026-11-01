using System.Globalization;

namespace Recorder.Core.Model;

/// <summary>A screen point in physical pixels.</summary>
public readonly record struct PixelPoint(int X, int Y)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{X},{Y}");
}

/// <summary>
/// A screen rectangle in physical pixels. UI Automation and Win32 use physical pixels for a
/// per-monitor-V2 process, so no conversion is needed until something is drawn with WPF.
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public PixelPoint Center => new(X + (Width / 2), Y + (Height / 2));

    public bool Contains(PixelPoint point) => point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    public static PixelRect FromEdges(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{X},{Y} {Width}×{Height}");
}
