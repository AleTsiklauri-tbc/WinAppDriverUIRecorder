using Recorder.Core.Model;

namespace Recorder.Core.Naming;

/// <summary>A sibling that might be the label of an input.</summary>
public sealed record LabelCandidate(string ControlType, string? Name, PixelRect Bounds, bool IsOffscreen);

/// <summary>
/// Finds the visible label of an input that has no LabeledBy (spec 7.8): the nearest Text in the same parent, left of
/// it on the same row or directly above it, within 250 px at 100 % scaling. Used for names and descriptions only,
/// never for locators.
/// </summary>
public static class LabelFinder
{
    private const double ReachAt100Percent = 250;

    /// <summary>Controls that show no caption of their own, so a nearby label says what they are.</summary>
    public static bool NeedsLabel(string controlType) =>
        controlType is "Edit" or "ComboBox" or "List" or "Spinner" or "Slider" or "Table" or "DataGrid" or "Tree" or "Document"
            or "Calendar" or "Custom";

    public static string? Find(PixelRect target, IEnumerable<LabelCandidate> siblings, double scale)
    {
        ArgumentNullException.ThrowIfNull(siblings);
        var reach = ReachAt100Percent * Math.Max(scale, 0.5);
        var tolerance = 4 * Math.Max(scale, 0.5);
        string? best = null;
        var bestDistance = double.MaxValue;
        foreach (var label in siblings)
        {
            if (label.ControlType != "Text" || string.IsNullOrWhiteSpace(label.Name) || label.IsOffscreen || label.Bounds.IsEmpty || label.Bounds == target)
            {
                continue;
            }

            var box = label.Bounds;
            double? distance = null;
            var verticalOverlap = Math.Min(box.Bottom, target.Bottom) - Math.Max(box.Y, target.Y);
            if (box.Right <= target.X + tolerance && verticalOverlap >= 0.5 * Math.Min(box.Height, target.Height))
            {
                distance = Math.Max(0, target.X - box.Right);
            }
            else if (box.Bottom <= target.Y + tolerance && box.X < target.Right && box.Right > target.X - tolerance)
            {
                distance = Math.Max(0, target.Y - box.Bottom);
            }

            if (distance is { } d && d <= reach && d < bestDistance)
            {
                best = label.Name!.Trim();
                bestDistance = d;
            }
        }

        return best;
    }
}
