using System.Drawing;
using System.Windows.Forms;

namespace Recorder.SampleApp;

/// <summary>A UserControl placed without a Name; only its inner TextBox is named.</summary>
internal sealed class AddressControl : UserControl
{
    public AddressControl()
    {
        Size = new Size(278, 30);
        Controls.Add(new Label { Text = "Street", Location = new Point(0, 7), AutoSize = true });
        Controls.Add(new TextBox { Name = "txtStreet", Location = new Point(108, 4), Width = 170 });
    }
}
