using Recorder.Core.Locators;

namespace Recorder.Core.Model;

/// <summary>Short human labels for elements, used in the overlay chip, lists and toasts.</summary>
public static class ElementLabel
{
    private const int MaxNameLength = 40;

    /// <summary>"Edit · txtAmount", "Button · “ძებნა”" or just "Pane".</summary>
    public static string For(ElementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return For(snapshot.ControlType, snapshot.AutomationId, snapshot.Name, snapshot.NativeWindowHandle);
    }

    public static string For(AncestorInfo ancestor)
    {
        ArgumentNullException.ThrowIfNull(ancestor);
        return For(ancestor.ControlType, ancestor.AutomationId, ancestor.Name, ancestor.NativeWindowHandle);
    }

    /// <summary>The most human part of the label: the id, else the quoted name, else nothing.</summary>
    /// <remarks>Name is visible UI text (for inputs, the label next to them), never the typed value.</remarks>
    public static string? Identity(string? automationId, string? name, nint nativeWindowHandle)
    {
        if (IdRules.IsUsable(automationId, nativeWindowHandle))
        {
            return automationId;
        }

        var cleanName = Clean(name);
        return cleanName is null ? null : "“" + cleanName + "”";
    }

    private static string For(string controlType, string? automationId, string? name, nint handle)
    {
        var identity = Identity(automationId, name, handle);
        return identity is null ? controlType : controlType + " · " + identity;
    }

    private static string? Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var singleLine = name.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= MaxNameLength ? singleLine : string.Concat(singleLine.AsSpan(0, MaxNameLength - 1), "…");
    }
}
