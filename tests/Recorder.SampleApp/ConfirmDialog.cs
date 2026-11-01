using System.Drawing;
using System.Windows.Forms;

namespace Recorder.SampleApp;

/// <summary>Modal dialog owned by the frame, named like LMS dialogs (e.g. ApplicationsDialog).</summary>
internal sealed class ConfirmDialog : Form
{
    public ConfirmDialog()
    {
        Name = "ConfirmDialog";
        Text = "Confirm";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(320, 140);

        var ok = new Button { Name = "OkButton", Text = "OK", DialogResult = DialogResult.OK, Location = new Point(144, 100), Size = new Size(75, 26) };
        var cancel = new Button { Name = "btnCancel", Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(228, 100), Size = new Size(75, 26) };

        Controls.Add(new Label { Name = "lblComment", Text = "Comment", Location = new Point(12, 15), AutoSize = true });
        Controls.Add(new TextBox { Name = "txtComment", Location = new Point(90, 12), Width = 210 });
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
