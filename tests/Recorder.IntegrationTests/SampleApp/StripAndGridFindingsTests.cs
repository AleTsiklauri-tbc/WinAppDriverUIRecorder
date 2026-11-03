using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Microsoft.Extensions.Logging.Abstractions;
using Recorder.Core.Automation;
using Recorder.Core.Locators;
using Recorder.Core.Model;
using Recorder.Core.Recording;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.SampleApp;

/// <summary>
/// Phase 3 findings: what a click on a menu, a tool strip, a context menu or a grid cell resolves to (F-25 to F-27).
/// Menus are opened with ExpandCollapse and WM_CONTEXTMENU, so these tests do not move the mouse.
/// </summary>
public sealed class StripAndGridFindingsTests : IClassFixture<SampleAppFixture>, IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly SampleAppFixture _app;
    private readonly UiaService _uia;

    public StripAndGridFindingsTests(SampleAppFixture app)
    {
        _app = app;
        _uia = new UiaService(UiaOptions.Default, new ValuePatternReader(), TimeProvider.System, NullLogger<UiaService>.Instance);
    }

    public void Dispose() => _uia.Dispose();

    /// <summary>F-25: a hit test stops at the strip; the capture engine goes one level down, to the item under the point.</summary>
    [Fact]
    public async Task A_hit_test_over_a_menu_bar_or_tool_strip_stops_at_the_strip_and_the_capture_reaches_the_item()
    {
        Desktop.Raise(_app.MainWindowHandle);
        var file = MenuItem("File");
        var button = _app.ById("toolStrip").FindAllChildren(c => c.ByControlType(ControlType.Button))[0];

        HitTest(Desktop.CenterOf(file)).ShouldBe(ControlType.MenuBar);
        HitTest(Desktop.CenterOf(button)).ShouldBe(ControlType.ToolBar);
        var fileItem = await CaptureAsync(Desktop.CenterOf(file));
        var toolButton = await CaptureAsync(Desktop.CenterOf(button));

        (fileItem.ControlType, fileItem.Name, fileItem.Ancestors[0].AutomationId).ShouldBe(("MenuItem", "File", "menuMain"));
        (toolButton.ControlType, toolButton.Ancestors[0].AutomationId).ShouldBe(("Button", "toolStrip"));
        toolButton.Bounds.ShouldBe(Desktop.BoundsOf(button));
    }

    /// <summary>F-26: an item of an open submenu reports its parent items, not the drop-down window, as its ancestors.</summary>
    [Fact]
    public async Task An_item_of_an_open_submenu_has_its_parent_items_as_ancestors()
    {
        Desktop.Raise(_app.MainWindowHandle);
        var file = MenuItem("File");
        AutomationElement? export = null;
        var pdfPoint = default(PixelPoint);
        nint dropDown = 0;
        file.Patterns.ExpandCollapse.Pattern.Expand();
        try
        {
            export = MenuItem("Export");
            export.Patterns.ExpandCollapse.Pattern.Expand();
            pdfPoint = Desktop.CenterOf(MenuItem("PDF"));
            Wait.Until(() => HitTest(pdfPoint) == ControlType.ToolBar && WindowAt(pdfPoint) != _app.ById("toolStrip").Properties.NativeWindowHandle.Value, Timeout, "the PDF item on screen");
            dropDown = WindowAt(pdfPoint);

            var snapshot = await CaptureAsync(pdfPoint);

            (snapshot.ControlType, snapshot.Name).ShouldBe(("MenuItem", "PDF"));
            snapshot.Ancestors.Take(3).Select(a => string.IsNullOrEmpty(a.Name) ? a.AutomationId : a.Name).ShouldBe(["Export", "File", "menuMain"]);
            snapshot.IsDetachedPopup.ShouldBeFalse();
            StepTargets.MenuPathOf(snapshot).ShouldBe(["File", "Export", "PDF"]);
            StepTargets.OwnerIndex(snapshot).ShouldBe(1);
        }
        finally
        {
            export?.Patterns.ExpandCollapse.PatternOrDefault?.Collapse();
            file.Patterns.ExpandCollapse.PatternOrDefault?.Collapse();
            Wait.Until(() => dropDown == 0 || WindowAt(pdfPoint) != dropDown, Timeout, "the menu to close");
        }
    }

    /// <summary>F-27: a grid cell names its row and column through GridItem and its header through TableItem, in any language.</summary>
    [Fact]
    public async Task Grid_cells_report_their_row_column_and_header()
    {
        Desktop.Raise(_app.MainWindowHandle);
        var grid = _app.ById("dgvLoans");
        var status = Wait.For(() => grid.FindFirstDescendant(c => c.ByName("Status Row 2, Not sorted.")), Timeout, "the Status cell of row 2");
        var check = Wait.For(() => grid.FindFirstDescendant(c => c.ByName(" Row 2")), Timeout, "the check box cell of row 2");

        var cell = await CaptureAsync(Desktop.CenterOf(status));
        var box = await CaptureAsync(Desktop.CenterOf(check));

        cell.Cell.ShouldBe(new Core.Model.GridCell(2, 3, "Status"));
        (cell.Ancestors[0].ControlType, cell.Ancestors[1].AutomationId).ShouldBe(("Custom", "dgvLoans"));
        StepTargets.OwnerIndex(cell).ShouldBe(1);
        (box.Cell!.Row, box.Cell.Column).ShouldBe((2, 0));
        string.IsNullOrEmpty(box.Cell.ColumnHeader).ShouldBeTrue();
    }

    /// <summary>A context menu is a separate window owned by the main window, so a window session reaches it (as F-23).</summary>
    [Fact]
    public async Task A_context_menu_is_owned_by_the_main_window_and_its_items_are_reached_like_menu_items()
    {
        Desktop.Raise(_app.MainWindowHandle);
        var grid = _app.ById("dgvLoans");
        var bounds = Desktop.BoundsOf(grid);
        Click.PostContextMenu(grid, new PixelPoint(bounds.X + 200, bounds.Y + 80));
        ElementSnapshot? copy = null;
        var copyPoint = default(PixelPoint);
        try
        {
            copyPoint = Desktop.CenterOf(Wait.For(() => MenuItemOrNull("Copy"), Timeout, "the context menu"));
            Wait.Until(() => HitTest(copyPoint) == ControlType.ToolBar, Timeout, "the Copy item on screen");

            copy = await CaptureAsync(copyPoint);

            (copy.ControlType, copy.Name, copy.Ancestors[0].AutomationId).ShouldBe(("MenuItem", "Copy", "gridMenu"));
            copy.TopLevelWindow.Handle.ShouldBe(_app.MainWindowHandle);
            copy.IsDetachedPopup.ShouldBeFalse();
            copy.Scope.ShouldBe(LocatorScope.Window);
        }
        finally
        {
            var menuWindow = copy?.Ancestors[0].NativeWindowHandle ?? 0;
            Click.PostKey(menuWindow != 0 ? menuWindow : _app.MainWindowHandle, Desktop.VkEscape);
            Wait.Until(() => menuWindow == 0 || WindowAt(copyPoint) != menuWindow, Timeout, "the context menu to close");
        }
    }

    private AutomationElement MenuItem(string name) => Wait.For(() => MenuItemOrNull(name), Timeout, "the " + name + " menu item");

    private AutomationElement? MenuItemOrNull(string name) =>
        _app.Session.MainWindow.FindFirstDescendant(c => c.ByControlType(ControlType.MenuItem).And(c.ByName(name)));

    private ControlType HitTest(PixelPoint point) =>
        _app.Session.Automation.FromPoint(new System.Drawing.Point(point.X, point.Y)).Properties.ControlType.ValueOrDefault;

    // The window of the element a hit test finds: for a menu or tool strip, the strip's own window.
    private nint WindowAt(PixelPoint point) =>
        _app.Session.Automation.FromPoint(new System.Drawing.Point(point.X, point.Y)).Properties.NativeWindowHandle.ValueOrDefault;

    private async Task<ElementSnapshot> CaptureAsync(PixelPoint point)
    {
        var result = await _uia.CaptureAtAsync(point, TestContext.Current.CancellationToken);
        result.Status.ShouldBe(UiaStatus.Ok, "capturing at " + point);
        return result.Value!;
    }
}
