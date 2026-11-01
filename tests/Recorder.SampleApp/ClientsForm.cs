using System;
using System.Drawing;
using System.Windows.Forms;

namespace Recorder.SampleApp;

/// <summary>Second MDI child, so elements can be grouped by their nearest form.</summary>
internal sealed class ClientsForm : Form
{
    public ClientsForm(Action<string> setStatus)
    {
        Name = "frmClients";
        Text = "Clients";
        ClientSize = new Size(600, 300);

        Controls.Add(new Label { Name = "lblClientNo", Text = "Client No", Location = new Point(12, 15), AutoSize = true });
        Controls.Add(new TextBox { Name = "clientNoEditBox", Location = new Point(100, 12), Width = 160 });

        var search = new Button { Name = "btnSearch", Text = "Search", Location = new Point(270, 10), Size = new Size(90, 26) };
        search.Click += (_, _) => setStatus("Clients searched");
        Controls.Add(search);

        var accounts = new ListView { Name = "listView", View = View.Details, FullRowSelect = true, Location = new Point(12, 50), Size = new Size(560, 200) };
        accounts.Columns.Add("Account", 300);
        accounts.Columns.Add("Currency", 100);
        accounts.Items.Add(new ListViewItem(new[] { "ACC-0001", "GEL" }));
        accounts.Items.Add(new ListViewItem(new[] { "ACC-0002", "USD" }));
        accounts.Items.Add(new ListViewItem(new[] { "ACC-0003", "EUR" }));
        Controls.Add(accounts);
    }
}
