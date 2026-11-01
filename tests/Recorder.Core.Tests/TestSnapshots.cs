using Recorder.Core.Model;

namespace Recorder.Core.Tests;

/// <summary>Builds snapshots for tests: a txtAmount Edit inside frmLoan inside frmMain unless told otherwise.</summary>
internal static class TestSnapshots
{
    public static readonly AncestorInfo MdiClient = Ancestor("Pane", "853106", null, handle: 853106);
    public static readonly AncestorInfo LoanForm = Ancestor("Window", "frmLoan", "Loan", handle: 2001);
    public static readonly AncestorInfo MainForm = Ancestor("Window", "frmMain", "Recorder Sample - [Loan]", handle: 1001);

    public static ElementSnapshot Edit(
        string? automationId = "txtAmount",
        string? name = "Amount",
        nint handle = 3001,
        int[]? runtimeId = null,
        IReadOnlyList<AncestorInfo>? ancestors = null,
        string controlType = "Edit",
        int processId = 4242)
    {
        ancestors ??= [LoanForm, MdiClient, MainForm];
        var top = ancestors.Count > 0 ? ancestors[^1] : null;
        return new ElementSnapshot(
            controlType, automationId, name, "WindowsForms10.EDIT.app.0.141b42a_r8_ad1", "WinForm",
            IsPassword: false, IsEnabled: true, IsOffscreen: false, new PixelRect(10, 20, 160, 24), processId, handle,
            new WindowInfo(top?.NativeWindowHandle ?? handle, top?.Name, top?.AutomationId, top?.ClassName),
            ancestors, ["Value"], Value: null, LabelText: null, runtimeId ?? [42, (int)handle], DateTimeOffset.UnixEpoch);
    }

    public static AncestorInfo Ancestor(string controlType, string? automationId, string? name, nint handle = 0) =>
        new(controlType, automationId, name, "WindowsForms10.Window.8.app.0.141b42a_r8_ad1", "WinForm", new PixelRect(0, 0, 800, 600), handle, [42, (int)handle]);
}
