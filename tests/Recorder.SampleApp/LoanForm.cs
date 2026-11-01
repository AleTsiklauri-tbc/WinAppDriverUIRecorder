using System;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace Recorder.SampleApp;

/// <summary>
/// The main work surface (spec 13.1). Control names mirror what the locator engine must handle:
/// named, unnamed, password, Georgian text, dynamic text and duplicate visible text.
/// </summary>
internal sealed partial class LoanForm : Form
{
    private readonly Action<string> _setStatus;
    private readonly System.Windows.Forms.Timer _clockTimer = new() { Interval = 1000 };
    private readonly Label _clock = new() { AutoSize = true, Location = new Point(130, 274) };
    private readonly TextBox _purpose = new() { Name = "purposeEditBox", Location = new Point(120, 400), Width = 140, ReadOnly = true };

    public LoanForm(Action<string> setStatus)
    {
        _setStatus = setStatus;
        Name = "frmLoan";
        Text = "Loan";
        ClientSize = new Size(1000, 640);
        AutoScroll = true;

        AddInputs();
        AddButtons();
        AddPanels();
        AddDataControls();

        // Unnamed label whose text changes every second: its Name must be treated as dynamic.
        UpdateClock();
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        Controls.Add(_clock);
        FormClosed += (_, _) => _clockTimer.Dispose();
    }

    private void UpdateClock() => _clock.Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private void AddInputs()
    {
        AddLabel("lblAmount", "Amount", 12, 15);
        Controls.Add(new TextBox { Name = "txtAmount", Location = new Point(120, 12), Width = 160 });

        // Georgian label next to an unnamed TextBox: the label is the only human-readable hint.
        AddLabel(string.Empty, "თანხა", 12, 45);
        Controls.Add(new TextBox { Location = new Point(120, 42), Width = 160 });

        AddLabel("lblPassword", "Password", 12, 75);
        Controls.Add(new TextBox { Name = "txtPassword", Location = new Point(120, 72), Width = 160, UseSystemPasswordChar = true });

        AddLabel("lblCurrency", "Currency", 12, 105);
        var currency = new ComboBox { Name = "cmbCurrency", Location = new Point(120, 102), Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
        currency.Items.AddRange(new object[] { "GEL", "USD", "EUR" });
        Controls.Add(currency);

        Controls.Add(new CheckBox { Name = "chkInsured", Text = "Insured", Location = new Point(120, 132), AutoSize = true });

        var rateType = new GroupBox { Name = "grpRateType", Text = "Rate type", Location = new Point(12, 165), Size = new Size(268, 56) };
        rateType.Controls.Add(new RadioButton { Name = "rbFixed", Text = "Fixed", Location = new Point(12, 24), AutoSize = true, Checked = true });
        rateType.Controls.Add(new RadioButton { Name = "rbFloating", Text = "Floating", Location = new Point(120, 24), AutoSize = true });
        Controls.Add(rateType);
    }

    private void AddButtons()
    {
        AddButton("btnShowMessage", "Show message", 12, 232, 100, (_, _) =>
            MessageBox.Show(MdiParent ?? this, "Saved successfully.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information));
        AddButton("btnOpenDialog", "Open dialog…", 118, 232, 100, (_, _) =>
        {
            using var dialog = new ConfirmDialog();
            dialog.ShowDialog(MdiParent ?? this);
        });

        // Blocks the UI thread on purpose: the recorder must stay responsive while the target hangs.
        AddButton("btnFreeze", "Freeze 5 s", 224, 232, 80, (_, _) => Thread.Sleep(5000));

        // Georgian "Search" with no Name: only the visible text identifies it.
        AddButton(string.Empty, "ძებნა", 12, 268, 100, (_, _) => _setStatus("Search clicked"));
    }

    private void AddPanels()
    {
        // Same visible text "Save" twice: the Name locator is ambiguous and needs refinement.
        Controls.Add(SavePanel("pnlCustomer", "btnSaveCustomer", 12, "Customer saved"));
        Controls.Add(SavePanel("pnlGuarantor", "btnSaveGuarantor", 154, "Guarantor saved"));

        Controls.Add(new AddressControl { Location = new Point(12, 360) });

        AddLabel("lblPurpose", "Purpose", 12, 403);
        Controls.Add(_purpose);
        AddButton("btnPurpose", "…", 262, 399, 28, (_, _) => ShowPurposePopup());

        Controls.Add(new OwnerDrawnNavBar(_setStatus) { Name = "navBar", Location = new Point(12, 440), Size = new Size(278, 96) });
    }

    private Panel SavePanel(string panelName, string buttonName, int left, string status)
    {
        var panel = new Panel { Name = panelName, Location = new Point(left, 306), Size = new Size(136, 44), BorderStyle = BorderStyle.FixedSingle };
        var save = new Button { Name = buttonName, Text = "Save", Location = new Point(8, 8), Size = new Size(118, 26) };
        save.Click += (_, _) => _setStatus(status);
        panel.Controls.Add(save);
        return panel;
    }

    private void ShowPurposePopup()
    {
        var popup = new PopupTreeForm(picked => _purpose.Text = picked)
        {
            Location = _purpose.PointToScreen(new Point(0, _purpose.Height)),
        };
        popup.Show(MdiParent ?? this);
    }

    private void AddLabel(string name, string text, int left, int top) =>
        Controls.Add(new Label { Name = name, Text = text, Location = new Point(left, top), AutoSize = true });

    private void AddButton(string name, string text, int left, int top, int width, EventHandler onClick)
    {
        var button = new Button { Name = name, Text = text, Location = new Point(left, top), Size = new Size(width, 28) };
        button.Click += onClick;
        Controls.Add(button);
    }
}
