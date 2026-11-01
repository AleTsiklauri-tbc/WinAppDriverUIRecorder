using System.Globalization;
using Recorder.App.Resources;
using Recorder.Core.Automation;
using Recorder.Core.Model;

namespace Recorder.App.ViewModels;

public sealed record PropertyRow(string Name, string Value);

public sealed record AncestorItem(string Label, string SessionKey);

/// <summary>Turns a snapshot into the Inspector's property table and ancestor breadcrumb.</summary>
internal static class InspectorRows
{
    public static IEnumerable<PropertyRow> Properties(ElementSnapshot s)
    {
        yield return Row(Strings.Prop_AutomationId, s.AutomationId);
        yield return Row(Strings.Prop_Name, s.Name);
        yield return Row(Strings.Prop_ClassName, s.ClassName);
        yield return Row(Strings.Prop_ControlType, s.ControlType);
        yield return Row(Strings.Prop_FrameworkId, s.FrameworkId);
        yield return Row(Strings.Prop_Value, s.IsPassword ? Strings.Inspector_PasswordValue : s.Value);
        yield return Row(Strings.Prop_LabelText, s.LabelText);
        yield return Row(Strings.Prop_IsEnabled, YesNo(s.IsEnabled));
        yield return Row(Strings.Prop_IsOffscreen, YesNo(s.IsOffscreen));
        yield return Row(Strings.Prop_IsPassword, YesNo(s.IsPassword));
        yield return Row(Strings.Prop_Bounds, s.Bounds.ToString());
        yield return Row(Strings.Prop_Process, ProcessText(s.ProcessId));
        yield return Row(Strings.Prop_Handle, HandleText(s.NativeWindowHandle));
        yield return Row(Strings.Prop_Window, s.TopLevelWindow.AutomationId is { Length: > 0 } id ? id : s.TopLevelWindow.Title);
        yield return Row(Strings.Prop_RuntimeId, RuntimeKey.Of(s.RuntimeIdForSessionOnly));
    }

    /// <summary>Root first, ending with the element's parent.</summary>
    public static IEnumerable<AncestorItem> Ancestors(ElementSnapshot snapshot) =>
        snapshot.Ancestors.Reverse().Select(a => new AncestorItem(ElementLabel.For(a), RuntimeKey.Of(a.RuntimeId)));

    private static PropertyRow Row(string name, string? value) =>
        new(name, string.IsNullOrEmpty(value) ? Strings.Inspector_NoValue : value);

    private static string YesNo(bool value) => value ? Strings.Value_Yes : Strings.Value_No;

    private static string HandleText(nint handle) =>
        handle == 0 ? Strings.Inspector_NoValue : string.Create(CultureInfo.InvariantCulture, $"0x{(long)handle:X} ({(long)handle})");

    private static string ProcessText(int processId)
    {
        var name = WindowEnumerator.ProcessName(processId);
        return name is null
            ? processId.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{name} ({processId})");
    }
}
