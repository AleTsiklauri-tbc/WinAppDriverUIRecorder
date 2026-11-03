using FlaUI.Core.Definitions;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.SampleApp;

/// <summary>
/// Spike tests for spec 7.1: what UI Automation (UIA3) really exposes for .NET Framework 4.8 WinForms.
/// Results are recorded in docs/ARCHITECTURE.md, section "Findings".
/// </summary>
public class ControlFindingsTests : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _app;

    public ControlFindingsTests(SampleAppFixture app) => _app = app;

    [Theory]
    [InlineData("txtAmount", ControlType.Edit)]
    [InlineData("cmbCurrency", ControlType.ComboBox)]
    [InlineData("chkInsured", ControlType.CheckBox)]
    [InlineData("rbFixed", ControlType.RadioButton)]
    [InlineData("btnSaveCustomer", ControlType.Button)]
    [InlineData("frmLoan", ControlType.Window)]
    public void Control_Name_becomes_the_AutomationId(string controlName, ControlType expectedType)
    {
        var element = _app.ById(controlName);

        element.ControlType.ShouldBe(expectedType);
        element.Properties.FrameworkId.Value.ShouldBe("WinForm");
    }

    [Theory]
    [InlineData("txtAmount", "WindowsForms10.EDIT")]
    [InlineData("btnShowMessage", "WindowsForms10.BUTTON")]
    [InlineData("chkInsured", "WindowsForms10.BUTTON")]
    [InlineData("rbFixed", "WindowsForms10.BUTTON")]
    [InlineData("lblAmount", "WindowsForms10.STATIC")]
    [InlineData("cmbCurrency", "WindowsForms10.COMBOBOX")]
    [InlineData("valuesCheckedListBox", "WindowsForms10.LISTBOX")]
    [InlineData("tabMain", "WindowsForms10.SysTabControl32")]
    [InlineData("pnlCustomer", "WindowsForms10.Window.8")]
    [InlineData("dgvLoans", "WindowsForms10.Window.8")]
    public void Class_names_are_WindowsForms10_plus_win32_class_plus_a_build_specific_suffix(string controlName, string stablePrefix)
    {
        var className = _app.ById(controlName).Properties.ClassName.Value;

        className.ShouldStartWith(stablePrefix + ".app.0.");
        className.ShouldMatch(@"^WindowsForms10\.[\w.]+\.app\.0\.[0-9a-f]+_r\d+_ad\d+$");
    }

    [Fact]
    public void An_unnamed_control_gets_its_window_handle_as_AutomationId()
    {
        var unnamed = Wait.For(
            () => _app.Loan.FindFirstChild(c => c.ByControlType(ControlType.Edit).And(c.ByName("თანხა"))),
            TimeSpan.FromSeconds(10),
            "the unnamed TextBox");

        var handle = unnamed.Properties.NativeWindowHandle.Value.ToInt64();
        unnamed.Properties.AutomationId.Value.ShouldBe(handle.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Edit_controls_take_their_Name_from_the_preceding_label_and_LabeledBy_is_empty()
    {
        var amount = _app.ById("txtAmount");

        amount.Properties.Name.Value.ShouldBe("Amount");
        amount.Properties.LabeledBy.ValueOrDefault.ShouldBeNull();
    }

    [Fact]
    public void The_password_box_reports_IsPassword() =>
        _app.ById("txtPassword").Properties.IsPassword.Value.ShouldBeTrue();

    [Fact]
    public void DataGridView_is_a_Table_with_zero_based_rows_and_header_based_cell_names()
    {
        var grid = _app.ById("dgvLoans");
        grid.ControlType.ShouldBe(ControlType.Table);
        grid.Properties.Name.Value.ShouldBe("DataGridView");

        var rowNames = grid.FindAllChildren().Select(row => row.Properties.Name.ValueOrDefault).ToList();
        rowNames.ShouldBe(["Top Row", "Row 0", "Row 1", "Row 2", "Row 3", "Row 4"]);

        var status = grid.FindFirstDescendant(c => c.ByName("Status Row 2, Not sorted."));
        status.ShouldNotBeNull();
        status.ControlType.ShouldBe(ControlType.Edit);
        status.Patterns.Value.Pattern.Value.Value.ShouldBe("Overdue");

        // Non-sortable columns (checkbox columns by default) get no ", Not sorted." suffix.
        var check = grid.FindFirstDescendant(c => c.ByName(" Row 2"));
        check.ShouldNotBeNull();
        check.ControlType.ShouldBe(ControlType.CheckBox);
    }

    [Fact]
    public void Image_only_toolbar_buttons_have_neither_Name_nor_AutomationId()
    {
        var items = _app.ById("toolStrip").FindAllChildren();

        items[0].ControlType.ShouldBe(ControlType.Thumb);
        items[0].Properties.Name.Value.ShouldBe("Grip");
        items.Skip(1).Count().ShouldBe(4);
        items.Skip(1).ShouldAllBe(b => b.ControlType == ControlType.Button
            && string.IsNullOrEmpty(b.Properties.Name.ValueOrDefault)
            && string.IsNullOrEmpty(b.Properties.AutomationId.ValueOrDefault));
    }

    [Fact]
    public void Tab_items_expose_their_text_but_not_the_TabPage_name()
    {
        var tabs = _app.ById("tabMain").FindAllChildren(c => c.ByControlType(ControlType.TabItem));

        tabs.Select(t => t.Properties.Name.Value).ShouldBe(["General", "გრაფიკი"]);
        tabs.ShouldAllBe(t => string.IsNullOrEmpty(t.Properties.AutomationId.ValueOrDefault));

        // F-37: nor a RuntimeId, so the locator service can't find a capture of one in the tree.
        tabs.ShouldAllBe(t => (t.Properties.RuntimeId.ValueOrDefault ?? Array.Empty<int>()).Length == 0);
    }

    [Fact]
    public void Owner_drawn_items_are_reached_through_MSAA_and_have_no_AutomationId()
    {
        var navBar = _app.ById("navBar");
        navBar.ControlType.ShouldBe(ControlType.ToolBar);

        var items = navBar.FindAllChildren();
        items.Select(i => i.Properties.Name.Value).ShouldBe(["განაცხადები", "სესხები", "ოპერაციები"]);
        items.ShouldAllBe(i => i.ControlType == ControlType.Button && string.IsNullOrEmpty(i.Properties.AutomationId.ValueOrDefault));

        // F-37: nor a RuntimeId.
        items.ShouldAllBe(i => (i.Properties.RuntimeId.ValueOrDefault ?? Array.Empty<int>()).Length == 0);
    }

    [Fact]
    public void Menu_items_have_no_AutomationId_and_open_submenus_nest_under_the_main_window()
    {
        var main = _app.Session.MainWindow;
        var file = main.FindFirstDescendant(c => c.ByControlType(ControlType.MenuItem).And(c.ByName("File")));
        file.ShouldNotBeNull();

        // MSAA-backed items do not support the AutomationId property at all (reading .Value throws).
        file.Properties.AutomationId.IsSupported.ShouldBeFalse();

        file.Patterns.ExpandCollapse.Pattern.Expand();
        try
        {
            var export = Wait.For(() => main.FindFirstDescendant(c => c.ByName("Export")), TimeSpan.FromSeconds(5), "File › Export");
            export.ControlType.ShouldBe(ControlType.MenuItem);
        }
        finally
        {
            file.Patterns.ExpandCollapse.Pattern.Collapse();
        }
    }
}
