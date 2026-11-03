using System.Text.RegularExpressions;
using Recorder.Core.Locators;

namespace Recorder.Core.Model;

/// <summary>Identifies the form an element belongs to: the nearest Window (MDI child, dialog or main window).</summary>
/// <remarks>
/// A form without a usable AutomationId is known by its title while the recorder runs, so that its controls group together.
/// The title is shown, never stored: a recording keeps such a page numbered instead (<see cref="Numbered"/>, D-70).
/// </remarks>
public sealed partial record PageKey(string Id, string DisplayName)
{
    private const string TitlePrefix = "name:";
    private const string NumberPrefix = "window:";

    /// <summary>Known by its window title, which can show a customer.</summary>
    public bool IsTitled => Id.StartsWith(TitlePrefix, StringComparison.Ordinal);

    /// <summary>A titled page as a recording keeps it, until a capture tells which window it is.</summary>
    public bool IsNumbered => Id.StartsWith(NumberPrefix, StringComparison.Ordinal);

    /// <summary>The <paramref name="number"/>th titled page of a recording: "Window 1", "Window 2"…</summary>
    public static PageKey Numbered(int number)
    {
        var text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new PageKey(NumberPrefix + text, "Window " + text);
    }

    public static PageKey For(ElementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.ControlType == "Window")
        {
            return From(snapshot.AutomationId, snapshot.Name, snapshot.ClassName, snapshot.NativeWindowHandle);
        }

        var window = snapshot.Ancestors.FirstOrDefault(a => a.ControlType == "Window");
        return window is not null
            ? From(window.AutomationId, window.Name, window.ClassName, window.NativeWindowHandle)
            : From(snapshot.TopLevelWindow.AutomationId, snapshot.TopLevelWindow.Title, snapshot.TopLevelWindow.ClassName, snapshot.TopLevelWindow.Handle);
    }

    private static PageKey From(string? automationId, string? name, string? className, nint handle)
    {
        if (IdRules.IsUsable(automationId, handle))
        {
            return new PageKey("id:" + automationId, automationId!);
        }

        // An MDI frame's title ends with " - [<active child>]", which changes with the active child.
        var title = string.IsNullOrWhiteSpace(name) ? null : MdiSuffix().Replace(name, string.Empty).Trim();
        if (!string.IsNullOrEmpty(title))
        {
            return new PageKey(TitlePrefix + title, title);
        }

        return string.IsNullOrWhiteSpace(className) ? new PageKey("window", "Window") : new PageKey("class:" + className, className);
    }

    [GeneratedRegex(@"\s-\s\[.*\]\s*$")]
    private static partial Regex MdiSuffix();
}
