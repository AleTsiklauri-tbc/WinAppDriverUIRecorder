using FlaUI.Core.Definitions;

namespace Recorder.Core.Automation;

/// <summary>Control type names exactly as WinAppDriver writes them as XPath tags.</summary>
internal static class ControlTypeNames
{
    /// <summary>
    /// An element that reports no control type (DataGridView rows) gets UI Automation's default, Custom, which is the
    /// tag WinAppDriver uses (F-6); FlaUI reports it as Unknown.
    /// </summary>
    public static string Of(ControlType controlType) =>
        controlType == ControlType.Unknown ? nameof(ControlType.Custom) : controlType.ToString();
}
