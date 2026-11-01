using Recorder.Core.Locators;

namespace Recorder.Core.Tests.Locators;

/// <summary>
/// A small tree shaped like the SampleApp as WinAppDriver sees it (F-20): unnamed controls carry their window handle
/// as AutomationId, the MDI client has no id, grid rows are Custom, and title bars hold non-client buttons.
/// </summary>
internal static class TestTrees
{
    public const int ProcessId = 4242;
    private const string Edit = "WindowsForms10.EDIT.app.0.141b42a_r8_ad1";
    private const string Static = "WindowsForms10.STATIC.app.0.141b42a_r8_ad1";
    private const string ButtonClass = "WindowsForms10.BUTTON.app.0.141b42a_r8_ad1";
    private const string Generic = "WindowsForms10.Window.8.app.0.141b42a_r8_ad1";

    public static UiTree SampleApp()
    {
        var root = Hwnd("Window", 1001, "frmMain", "Recorder Sample - [Loan]", Generic).AddRange(
            TitleBar("main"),
            Node("MenuBar", "menuMain", null, Generic, "menu").AddRange(Node("MenuItem", null, "File", null, "menu.file")),
            Hwnd("Pane", 1002, null, null, "WindowsForms10.MDICLIENT.app.0.141b42a_r8_ad1").Add(
                Hwnd("Window", 2001, "frmLoan", "Loan", Generic).AddRange(
                    TitleBar("loan"),
                    Hwnd("Text", 2002, "lblAmount", "Amount", Static),
                    Hwnd("Edit", 2003, "txtAmount", "Amount", Edit),
                    Hwnd("Text", 2004, null, "თანხა", Static),
                    Hwnd("Edit", 2005, null, "თანხა", Edit),
                    Hwnd("Group", 2006, "grpRateType", "Rate type", ButtonClass).AddRange(
                        Hwnd("RadioButton", 2007, "rbFixed", "Fixed", ButtonClass),
                        Hwnd("RadioButton", 2008, "rbFloating", "Floating", ButtonClass)),
                    Hwnd("Pane", 2009, "pnlCustomer", null, Generic).Add(Hwnd("Button", 2010, "btnSaveCustomer", "Save", ButtonClass)),
                    Hwnd("Pane", 2011, "pnlGuarantor", null, Generic).Add(Hwnd("Button", 2012, "btnSaveGuarantor", "Save", ButtonClass)),
                    Hwnd("Button", 2013, null, "ძებნა", ButtonClass),
                    Hwnd("Text", 2014, null, "16:59:02", Static),
                    Hwnd("Pane", 2015, null, null, Generic).AddRange(
                        Hwnd("Text", 2016, null, "Street", Static),
                        Hwnd("Edit", 2017, "txtStreet", "Street", Edit)),
                    Hwnd("Edit", 2018, "row_48213", null, Edit),
                    Hwnd("Pane", 2019, null, null, "DevExpress.XtraEditors.PanelControl"),
                    Hwnd("Table", 2020, "dgvLoans", "DataGridView", Generic).AddRange(
                        Row(0, "4247891719"),
                        Row(1, "4257712780")))),
            Hwnd("Window", 3001, null, "Information", "#32770").AddRange(
                Hwnd("Text", 3002, "65535", "Saved successfully.", "Static"),
                Hwnd("Button", 3003, "2", "OK", "Button")));
        return new UiTree(root);
    }

    /// <summary>An open menu: the same items appear under the menu item and in the owned drop-down window (F-21).</summary>
    public static UiTree OpenMenu()
    {
        var root = Hwnd("Window", 1001, "frmMain", "Main", Generic).AddRange(
            Node("ToolBar", null, "FileDropDown", null, "drop").Add(Node("MenuItem", null, "Export", null, "menu.export")),
            Node("MenuBar", "menuMain", null, Generic, "bar").Add(
                Node("MenuItem", null, "File", null, "menu.file").Add(Node("MenuItem", null, "Export", null, "menu.export"))));
        return new UiTree(root);
    }

    public static UiNode Find(this UiTree tree, Func<UiNode, bool> match) => tree.Nodes.First(match);

    public static UiNode ById(this UiTree tree, string automationId) => tree.Find(node => node.AutomationId == automationId);

    public static UiNode ByName(this UiTree tree, string controlType, string name) =>
        tree.Find(node => node.ControlType == controlType && node.Name == name);

    private static UiNode Row(int index, string headerId)
    {
        var row = Node("Custom", null, $"Row {index}", null, $"row.{index}");
        return row.AddRange(
            Node("Header", headerId, $"Row {index}", null, $"row.{index}.header"),
            Node("Edit", (77 + index).ToString(System.Globalization.CultureInfo.InvariantCulture), $"Status Row {index}, Not sorted.", null, $"row.{index}.status"));
    }

    private static UiNode TitleBar(string window) =>
        Node("TitleBar", "TitleBar", null, null, window + ".titlebar").AddRange(
            Node("MenuBar", "SystemMenuBar", "System", null, window + ".system"),
            Node("Button", "Close", "Close", null, window + ".close"));

    private static UiNode Hwnd(string controlType, int handle, string? automationId, string? name, string? className) =>
        new(controlType, automationId ?? handle.ToString(System.Globalization.CultureInfo.InvariantCulture), name, className, "WinForm", handle,
            runtimeKey: "42." + handle.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static UiNode Node(string controlType, string? automationId, string? name, string? className, string key) =>
        new(controlType, automationId, name, className, "WinForm", runtimeKey: key);
}
