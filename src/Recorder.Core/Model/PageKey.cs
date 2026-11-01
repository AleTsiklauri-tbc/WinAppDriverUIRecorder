using System.Text.RegularExpressions;
using Recorder.Core.Locators;

namespace Recorder.Core.Model;

/// <summary>Identifies the form an element belongs to: the nearest Window (MDI child, dialog or main window).</summary>
public sealed partial record PageKey(string Id, string DisplayName)
{
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
            return new PageKey("name:" + title, title);
        }

        return string.IsNullOrWhiteSpace(className) ? new PageKey("window", "Window") : new PageKey("class:" + className, className);
    }

    [GeneratedRegex(@"\s-\s\[.*\]\s*$")]
    private static partial Regex MdiSuffix();
}
