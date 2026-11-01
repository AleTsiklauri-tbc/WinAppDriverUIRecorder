using System.Drawing;
using System.Windows.Forms;

namespace Recorder.SampleApp;

/// <summary>
/// MDI frame like LMS: menu, toolbar and status bar live on the frame; content lives in MDI children.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly ToolStripStatusLabel _status = new() { Name = "lblStatus", Text = "Ready" };
    private WebForm? _webForm;

    public MainForm()
    {
        Name = "frmMain";
        Text = "Recorder Sample";
        IsMdiContainer = true;
        ClientSize = new Size(1200, 760);
        StartPosition = FormStartPosition.CenterScreen;

        var menu = BuildMenu();
        var statusStrip = new StatusStrip { Name = "statusStrip" };
        statusStrip.Items.Add(_status);

        Controls.Add(BuildToolStrip());
        Controls.Add(menu);
        Controls.Add(statusStrip);
        MainMenuStrip = menu;
        Load += (_, _) => OpenChildren();
    }

    internal void SetStatus(string text) => _status.Text = text;

    private MenuStrip BuildMenu()
    {
        var pdf = new ToolStripMenuItem("PDF", null, (_, _) => SetStatus("Exported to PDF")) { Name = "mnuExportPdf" };
        var excel = new ToolStripMenuItem("Excel", null, (_, _) => SetStatus("Exported to Excel")) { Name = "mnuExportExcel" };
        var export = new ToolStripMenuItem("&Export") { Name = "mnuExport" };
        export.DropDownItems.AddRange(new ToolStripItem[] { pdf, excel });

        var exit = new ToolStripMenuItem("E&xit", null, (_, _) => Close()) { Name = "mnuExit" };
        var file = new ToolStripMenuItem("&File") { Name = "mnuFile" };
        file.DropDownItems.AddRange(new ToolStripItem[] { export, new ToolStripSeparator(), exit });

        var webForm = new ToolStripMenuItem("Web form", null, (_, _) => OpenWebForm()) { Name = "mnuWebForm" };
        var window = new ToolStripMenuItem("&Window") { Name = "mnuWindow" };
        window.DropDownItems.Add(webForm);

        var menu = new MenuStrip { Name = "menuMain", MdiWindowListItem = window };
        menu.Items.AddRange(new ToolStripItem[] { file, window });
        return menu;
    }

    // LMS toolbar buttons are image-only and unnamed; today's tests reach them by index.
    private ToolStrip BuildToolStrip()
    {
        var strip = new ToolStrip { Name = "toolStrip" };
        strip.Items.Add(ImageButton("Save", Color.SeaGreen));
        strip.Items.Add(ImageButton("New", Color.SteelBlue));
        strip.Items.Add(ImageButton("Edit", Color.DarkOrange));
        strip.Items.Add(ImageButton("Delete", Color.Firebrick));
        return strip;
    }

    private ToolStripButton ImageButton(string toolTip, Color color)
    {
        var button = new ToolStripButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            Image = Swatch(color),
            ToolTipText = toolTip,
        };
        button.Click += (_, _) => SetStatus(toolTip + " clicked");
        return button;
    }

    private static Bitmap Swatch(Color color)
    {
        var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        using var brush = new SolidBrush(color);
        graphics.FillRectangle(brush, 2, 2, 12, 12);
        return bitmap;
    }

    private void OpenChildren()
    {
        new ClientsForm(SetStatus) { MdiParent = this }.Show();
        new LoanForm(SetStatus) { MdiParent = this, WindowState = FormWindowState.Maximized }.Show();
    }

    private void OpenWebForm()
    {
        if (_webForm is null || _webForm.IsDisposed)
        {
            _webForm = new WebForm { MdiParent = this, WindowState = FormWindowState.Maximized };
            _webForm.Show();
        }

        _webForm.Activate();
    }
}
