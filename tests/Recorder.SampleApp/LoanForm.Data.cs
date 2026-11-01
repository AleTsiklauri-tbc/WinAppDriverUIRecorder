using System.Drawing;
using System.Windows.Forms;

namespace Recorder.SampleApp;

internal sealed partial class LoanForm
{
    private void AddDataControls()
    {
        Controls.Add(BuildGrid());
        Controls.Add(BuildTabs());

        AddLabel("lblAttributes", "Attributes", 320, 342);
        var attributes = new CheckedListBox { Name = "valuesCheckedListBox", Location = new Point(320, 362), Size = new Size(300, 80), CheckOnClick = true };
        attributes.Items.AddRange(new object[] { "ფიზიკური პირი", "იურიდიული პირი", "VIP" });
        Controls.Add(attributes);
    }

    // 5 rows x 4 columns. The first column is a checkbox with an empty header, as in LMS grids.
    private DataGridView BuildGrid()
    {
        var grid = new DataGridView
        {
            Name = "dgvLoans",
            Location = new Point(320, 12),
            Size = new Size(660, 180),
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            ContextMenuStrip = BuildGridMenu(),
        };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "colSelect", HeaderText = string.Empty, Width = 32 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colId", HeaderText = "ID", ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colAmount", HeaderText = "Amount", ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colStatus", HeaderText = "Status", ReadOnly = true });

        grid.Rows.Add(false, "1001", "5000.00", "Active");
        grid.Rows.Add(false, "1002", "1250.50", "Closed");
        grid.Rows.Add(true, "1003", "300.00", "Overdue");
        grid.Rows.Add(false, "1004", "78000.00", "Active");
        grid.Rows.Add(false, "1005", "15.75", "New");
        return grid;
    }

    private ContextMenuStrip BuildGridMenu()
    {
        var menu = new ContextMenuStrip { Name = "gridMenu" };
        menu.Items.Add(new ToolStripMenuItem("Copy", null, (_, _) => _setStatus("Row copied")) { Name = "mnuGridCopy" });
        menu.Items.Add(new ToolStripMenuItem("Refresh", null, (_, _) => _setStatus("Grid refreshed")) { Name = "mnuGridRefresh" });
        return menu;
    }

    private static TabControl BuildTabs()
    {
        var tabs = new TabControl { Name = "tabMain", Location = new Point(320, 205), Size = new Size(660, 120) };

        var general = new TabPage("General") { Name = "tabGeneral" };
        general.Controls.Add(new CheckBox { Name = "chkAutoRenew", Text = "Auto renew", Location = new Point(12, 12), AutoSize = true });

        // Georgian tab text ("Schedule"); the TextBox keeps a designer-default name on purpose.
        var schedule = new TabPage("გრაფიკი") { Name = "tabSchedule" };
        schedule.Controls.Add(new Label { Text = "Payment day", Location = new Point(12, 15), AutoSize = true });
        schedule.Controls.Add(new TextBox { Name = "textBox12", Location = new Point(110, 12), Width = 80 });

        tabs.TabPages.AddRange(new[] { general, schedule });
        return tabs;
    }
}
