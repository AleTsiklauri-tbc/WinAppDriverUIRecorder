using Recorder.Core.Model;
using Recorder.Core.Recording;

namespace Recorder.Core.Tests.Recording;

/// <summary>SampleApp-like snapshots for the recording rules: the Loan form inside the MDI frame (window 1001).</summary>
internal static class Elements
{
    public const nint MainWindow = 1001;

    public static readonly AncestorInfo Main = TestSnapshots.MainForm;
    public static readonly AncestorInfo Loan = TestSnapshots.LoanForm;
    public static readonly AncestorInfo CurrencyCombo = TestSnapshots.Ancestor("ComboBox", "cmbCurrency", "Currency", 3003);
    public static readonly AncestorInfo DropDownList = TestSnapshots.Ancestor("List", "1000", "Currency", 3010);
    public static readonly AncestorInfo MenuBar = TestSnapshots.Ancestor("MenuBar", "menuMain", null, 1100);
    public static readonly AncestorInfo FileMenu = TestSnapshots.Ancestor("MenuItem", null, "File", 1101);
    public static readonly AncestorInfo ExportMenu = TestSnapshots.Ancestor("MenuItem", null, "Export", 1102);
    public static readonly AncestorInfo LoansGrid = TestSnapshots.Ancestor("Table", "dgvLoans", "DataGridView", 3020);
    public static readonly AncestorInfo Row2 = TestSnapshots.Ancestor("Custom", null, "Row 2", 3021);
    public static readonly AncestorInfo TabControl = TestSnapshots.Ancestor("Tab", "tabMain", null, 3031);
    public static readonly AncestorInfo MessageBoxWindow = TestSnapshots.Ancestor("Window", null, "Information", 5000);
    public static readonly AncestorInfo ReportWindowAncestor = TestSnapshots.Ancestor("Window", "frmReport", "Report", 6000);

    private static readonly AncestorInfo[] InLoan = [Loan, TestSnapshots.MdiClient, Main];

    public static ElementSnapshot Amount { get; } = Make("Edit", "txtAmount", "Amount", 3001, InLoan, ["Value"]);

    public static ElementSnapshot Password { get; } = Make("Edit", "txtPassword", "Password", 3002, InLoan, ["Value"], password: true);

    public static ElementSnapshot Currency { get; } = Make("ComboBox", "cmbCurrency", "Currency", 3003, InLoan, ["Value", "ExpandCollapse"]);

    public static ElementSnapshot Gel { get; } = Make("ListItem", null, "GEL", 3011, [DropDownList, CurrencyCombo, .. InLoan], ["SelectionItem"]);

    public static ElementSnapshot Insured { get; } = Make("CheckBox", "chkInsured", "Insured", 3004, InLoan, ["Toggle"]);

    public static ElementSnapshot FileItem { get; } = Make("MenuItem", null, "File", 1101, [MenuBar, Main], ["ExpandCollapse"]);

    public static ElementSnapshot ExportItem { get; } = Make("MenuItem", null, "Export", 1102, [FileMenu, MenuBar, Main], ["ExpandCollapse"]);

    public static ElementSnapshot PdfItem { get; } = Make("MenuItem", null, "PDF", 1103, [ExportMenu, FileMenu, MenuBar, Main], ["Invoke"]);

    public static ElementSnapshot ShowMessage { get; } = Make("Button", "btnShowMessage", "Show message", 3040, InLoan, ["Invoke"]);

    public static ElementSnapshot SaveCustomer { get; } = Make("Button", "btnSaveCustomer", "Save", 3041, InLoan, ["Invoke"]);

    public static ElementSnapshot MessageOk { get; } = Make("Button", "2", "OK", 5001, [MessageBoxWindow, Main], ["Invoke"]);

    public static ElementSnapshot Grid { get; } = Make("Table", "dgvLoans", "DataGridView", 3020, InLoan, ["Grid", "Table"]);

    public static ElementSnapshot StatusCell { get; } =
        Make("Edit", "4228372289", "Status Row 2, Not sorted.", 3022, [Row2, LoansGrid, .. InLoan], ["Value", "GridItem", "TableItem"], cell: new GridCell(2, 3, "Status"));

    public static ElementSnapshot CheckCell { get; } =
        Make("CheckBox", "4254444047", " Row 2", 3023, [Row2, LoansGrid, .. InLoan], ["Toggle", "GridItem", "TableItem"], cell: new GridCell(2, 0, string.Empty));

    public static ElementSnapshot RowHeader { get; } = Make("Custom", null, "Row 2", 3021, [LoansGrid, .. InLoan], []);

    public static ElementSnapshot GeneralTab { get; } = Make("TabItem", null, "General", 3030, [TabControl, .. InLoan], ["SelectionItem"]);

    /// <summary>A button in a separate top-level window that the main window does not own (needs SwitchWindow).</summary>
    public static ElementSnapshot PrintButton { get; } = Make("Button", "btnPrint", "Print", 6001, [ReportWindowAncestor], ["Invoke"]);

    public static ElementSnapshot ReportWindow { get; } = Make("Window", "frmReport", "Report", 6000, [], ["Window"]);

    public static ElementSnapshot MainWindowElement { get; } = Make("Window", "frmMain", "Recorder Sample", 1001, [], ["Window"]);

    public static ElementSnapshot Make(
        string controlType, string? automationId, string? name, nint key, IReadOnlyList<AncestorInfo> ancestors, string[] patterns,
        string? value = null, bool password = false, GridCell? cell = null)
    {
        // Every key gets its own spot on the screen, so a point finds exactly one element.
        var top = ancestors.Count > 0 ? ancestors[^1] : null;
        var bounds = new PixelRect((int)(key % 1000) * 100, (int)(key / 1000) * 100, 80, 20);
        return new ElementSnapshot(
            controlType, automationId, name, "WindowsForms10.Window.8.app.0.141b42a_r8_ad1", "WinForm", password, IsEnabled: true, IsOffscreen: false,
            bounds, 4242, key, new WindowInfo(top?.NativeWindowHandle ?? key, top?.Name ?? name, top?.AutomationId ?? automationId, top?.ClassName),
            ancestors, patterns, value, LabelText: null, [42, (int)key], DateTimeOffset.UnixEpoch, Cell: cell);
    }

    public static ClickInput Click(ElementSnapshot element, ElementSnapshot? owner = null, uint time = 1000, PointerButton button = PointerButton.Left, ElementSnapshot? window = null) =>
        new(button, element, owner, window, element.Bounds.Center, time, DateTimeOffset.UnixEpoch);

    public static ValueInput Value(ElementSnapshot element, string? before, string? after, bool secret = false) =>
        new(element, null, before, after, secret, DateTimeOffset.UnixEpoch);
}

/// <summary>Applies step changes the way the app does, so tests can look at the resulting list.</summary>
internal sealed class DraftList
{
    private readonly List<StepDraft> _steps = [];

    public IReadOnlyList<StepDraft> Steps => _steps;

    public IReadOnlyList<StepKind> Kinds => [.. _steps.Select(step => step.Kind)];

    public DraftList Apply(IReadOnlyList<StepChange> changes)
    {
        foreach (var change in changes)
        {
            var index = change is StepReplaced replaced ? _steps.FindIndex(step => step.Id == replaced.ReplacedId) : -1;
            if (index >= 0)
            {
                _steps[index] = change.Step;
            }
            else
            {
                _steps.Add(change.Step);
            }
        }

        return this;
    }
}
