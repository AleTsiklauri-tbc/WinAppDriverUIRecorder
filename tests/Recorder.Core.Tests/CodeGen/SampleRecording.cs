using Recorder.Core.Locators;
using Recorder.Core.Model;

namespace Recorder.Core.Tests.CodeGen;

/// <summary>
/// Recordings of the SampleApp for code generation: its controls with the locators the recorder finds for them, and either
/// the Phase 3 acceptance flow or a flow with every step kind.
/// </summary>
internal sealed class SampleRecording
{
    private static readonly AncestorInfo MainForm = Window("frmMain", "Recorder Sample - [Loan]", "WindowsForms10.Window.8.app.0.141b42a_r8_ad1", 1001);
    private static readonly AncestorInfo MdiClient = new(
        "Pane", "853106", null, "MDIClient", "WinForm", new PixelRect(0, 0, 800, 600), 853106, [42, 853106]);

    private static readonly AncestorInfo LoanForm = Window("frmLoan", "Loan", "WindowsForms10.Window.8.app.0.141b42a_r8_ad1", 2001);

    // The message box has no AutomationId: its page is keyed by its title, which must never reach the code (D-61).
    private static readonly AncestorInfo MessageBox = Window(null, "Recorder Sample", "#32770", 3001);
    private static readonly AncestorInfo MenuBar = new("MenuBar", "menuMain", null, "WindowsForms10.Window.8.app.0.141b42a_r8_ad1", "WinForm", new PixelRect(0, 0, 800, 24), 1002, [42, 1002]);

    private readonly ElementCatalog _catalog = new();
    private readonly List<RecordedStep> _steps = [];
    private int _nextHandle = 5001;

    private SampleRecording()
    {
        AncestorInfo[] inLoan = [LoanForm, MdiClient, MainForm];
        Amount = Add("Edit", "txtAmount", "Amount", inLoan, Id("txtAmount"));
        Password = Add("Edit", "txtPassword", "Password", inLoan, Id("txtPassword"), isPassword: true);
        Currency = Add("ComboBox", "cmbCurrency", "Currency", inLoan, Id("cmbCurrency"));
        Insured = Add("CheckBox", "chkInsured", "Insured", inLoan, Id("chkInsured"));
        File = Add("MenuItem", null, "File", [MenuBar, MainForm], new LocatorCandidate(
            LocatorStrategy.Name, LocatorSpec.ForAttribute(AttributeName.Name, "File"), "File", LocatorScope.Window, Stability.Medium, 120, 1, 1, true,
            [LocatorNote.UiText], new LocatorExplanation(LocatorKind.Name)));
        ShowMessage = Add("Button", "btnShowMessage", "Show message", inLoan, Id("btnShowMessage"));
        Ok = Add("Button", "2", "OK", [MessageBox, MainForm], Id("2"));
        LoansGrid = Add("Table", "dgvLoans", "DataGridView", inLoan, Id("dgvLoans"));
    }

    public PageElement Amount { get; }

    public PageElement Password { get; }

    public PageElement Currency { get; }

    public PageElement Insured { get; }

    public PageElement File { get; }

    public PageElement ShowMessage { get; }

    public PageElement Ok { get; }

    public PageElement LoansGrid { get; }

    public ElementCatalog Catalog => _catalog;

    public IReadOnlyList<RecordedStep> Steps => _steps;

    /// <summary>Spec 14, Phase 3: type amount → type password → select "GEL" → tick → File › Export › PDF → Show message → OK → row 2, "Status".</summary>
    public static SampleRecording AcceptanceFlow()
    {
        var recording = new SampleRecording();
        recording.AddAcceptanceSteps();
        return recording;
    }

    /// <summary>The acceptance flow, then every other step kind and variant (spec 13.2: snapshot tests cover every step kind).</summary>
    public static SampleRecording EveryStepKind()
    {
        var r = new SampleRecording();
        r.AddAcceptanceSteps();
        AncestorInfo[] inLoan = [LoanForm, MdiClient, MainForm];
        var fixedRate = r.Add("RadioButton", "rbFixed", "Fixed rate", inLoan, Id("rbFixed"));
        var schedule = r.Add("TabItem", "tabSchedule", "Schedule", inLoan, Id("tabSchedule"));
        var status = r.Add("Text", "lblStatus", "Ready", inLoan, Id("lblStatus"));
        var save = r.Add("Button", "btnSave", "Save", inLoan, Id("btnSave"));
        var clients = r.Add("Window", "frmClients", "Clients - John Example", [], Id("frmClients"));

        // dgvLoans: the cells are clicked inside the grid. "Grid" is the grid LMSAutomationDemo's GridSteps works on: its text
        // cells go through GridSteps, a check box cell does not (D-83).
        var clientGrid = r.Add("Table", "Grid", "DataGridView", inLoan, Id("Grid"));
        r.Step(StepKind.DoubleClick, r.LoansGrid, row: 0, column: 1, header: "Client", cellType: "Edit");
        r.Step(StepKind.RightClick, r.LoansGrid, row: 1, column: 0, header: null, cellType: "CheckBox");
        r.Step(StepKind.GridCellClick, r.LoansGrid, row: 4, column: 3, header: "Status", cellType: "Edit");
        r.Step(StepKind.GridCellClick, clientGrid, row: 0, column: 2, header: "Client", cellType: "Edit");
        r.Step(StepKind.DoubleClick, clientGrid, row: 0, column: 2, header: "Client", cellType: "Edit");
        r.Step(StepKind.GridCellClick, clientGrid, row: 1, column: 0, header: "Select", cellType: "CheckBox");
        r.Step(StepKind.DoubleClick, r.Amount);
        r.Step(StepKind.RightClick, r.Amount);
        r.Step(StepKind.ClearText, r.Amount);
        r.Step(StepKind.SetText, r.Amount, text: "7 500,00 \"GEL\"");
        r.Step(StepKind.SetChecked, r.Insured, state: false);
        r.Step(StepKind.SetChecked, r.Insured, state: null);
        r.Step(StepKind.SetChecked, fixedRate, state: true);
        r.Step(StepKind.SelectTab, schedule);
        r.Step(StepKind.PressKeys, r.Password, text: "Enter");
        r.Step(StepKind.PressKeys, null, text: "Ctrl+S");
        r.Step(StepKind.SwitchWindow, clients);
        r.Step(StepKind.AssertVisible, status);
        r.Step(StepKind.AssertEnabled, save, state: false);
        r.Step(StepKind.AssertText, status, text: "Ready");
        r.Step(StepKind.AssertValue, r.Amount, text: "7 500,00 \"GEL\"");
        r.Step(StepKind.AssertChecked, r.Insured, state: false);
        r._steps.Add(new RecordedStep { Kind = StepKind.Comment, Comment = "Check the totals\nthen save */" });
        r._steps.Add(new RecordedStep { Kind = StepKind.Comment, Target = r.LoansGrid.Id, Scroll = -3 });
        r.Step(StepKind.Click, save);
        return r;
    }

    /// <summary>
    /// A recording much larger than a real test (for timings): <paramref name="pages"/> forms with
    /// <paramref name="elementsPerPage"/> text boxes each, and <paramref name="steps"/> steps typing into them in turn.
    /// </summary>
    public static SampleRecording Large(int pages, int elementsPerPage, int steps)
    {
        var r = new SampleRecording();
        var elements = new List<PageElement>();
        for (var page = 0; page < pages; page++)
        {
            var form = Window($"frmPage{page}", $"Page {page}", "WindowsForms10.Window.8.app.0.141b42a_r8_ad1", 10_000 + page);
            for (var i = 0; i < elementsPerPage; i++)
            {
                elements.Add(r.Add("Edit", $"txtField{page}_{i}", $"Field {i}", [form, MdiClient, MainForm], Id($"txtField{page}_{i}")));
            }
        }

        for (var i = 0; i < steps; i++)
        {
            r.Step(StepKind.SetText, elements[i % elements.Count], text: i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return r;
    }

    private static AncestorInfo Window(string? automationId, string title, string className, nint handle) =>
        new("Window", automationId, title, className, "WinForm", new PixelRect(0, 0, 800, 600), handle, [42, (int)handle]);

    internal static LocatorCandidate Id(string automationId) =>
        new(LocatorStrategy.AccessibilityId, LocatorSpec.ForAttribute(AttributeName.AutomationId, automationId), automationId, LocatorScope.Window,
            Stability.High, 150, 1, 1, true, [], new LocatorExplanation(LocatorKind.Id));

    private void AddAcceptanceSteps()
    {
        Step(StepKind.SetText, Amount, text: "5000");
        _steps.Add(new RecordedStep { Kind = StepKind.SetText, Target = Password.Id, TestDataKey = Password.VariableName });
        Step(StepKind.SelectOption, Currency, text: "GEL");
        Step(StepKind.SetChecked, Insured, state: true);
        _steps.Add(new RecordedStep { Kind = StepKind.MenuPath, Target = File.Id, MenuItems = ["File", "Export", "PDF"] });
        Step(StepKind.Click, ShowMessage);
        Step(StepKind.Click, Ok);
        Step(StepKind.GridCellClick, LoansGrid, row: 2, column: 3, header: "Status", cellType: "Edit");
    }

    private void Step(
        StepKind kind, PageElement? target, string? text = null, bool? state = null, int? row = null, int? column = null, string? header = null,
        string? cellType = null) =>
        _steps.Add(new RecordedStep
        {
            Kind = kind, Target = target?.Id, Text = text, State = state, RowIndex = row, ColumnIndex = column, ColumnHeader = header,
            CellControlType = cellType, At = DateTimeOffset.UnixEpoch,
        });

    private PageElement Add(string controlType, string? automationId, string? name, AncestorInfo[] ancestors, LocatorCandidate locator, bool isPassword = false)
    {
        var handle = _nextHandle++;
        var top = ancestors.Length > 0 ? ancestors[^1] : null;
        var snapshot = new ElementSnapshot(
            controlType, automationId, name, "WindowsForms10.Window.8.app.0.141b42a_r8_ad1", "WinForm", isPassword, IsEnabled: true, IsOffscreen: false,
            new PixelRect(10, 10, 100, 20), 4242, handle, new WindowInfo(top?.NativeWindowHandle ?? handle, top?.Name ?? name, top?.AutomationId ?? automationId, top?.ClassName),
            ancestors, [], Value: null, LabelText: null, [42, (int)handle], DateTimeOffset.UnixEpoch);
        var (element, _) = _catalog.Add(snapshot);
        return _catalog.AttachLocators(element, new LocatorReport([locator], 0, true, LocatorScope.Window, 120, TimeSpan.Zero));
    }
}
