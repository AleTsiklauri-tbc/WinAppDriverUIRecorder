using System;
using System.Drawing;
using System.Windows.Forms;

namespace Recorder.SampleApp;

/// <summary>
/// Borderless popup with a tree, like LMS's "PopupTreeView": a separate top-level window owned by the frame.
/// </summary>
internal sealed class PopupTreeForm : Form
{
    private readonly Action<string> _onPicked;

    public PopupTreeForm(Action<string> onPicked)
    {
        _onPicked = onPicked;
        Name = "PopupTreeForm";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(260, 180);

        var tree = new TreeView { Name = "PopupTreeView", Dock = DockStyle.Fill, HideSelection = false };
        var consumer = tree.Nodes.Add("consumer", "სამომხმარებლო");
        consumer.Nodes.Add("car", "ავტომობილი");
        consumer.Nodes.Add("repair", "რემონტი");
        var business = tree.Nodes.Add("business", "ბიზნესი");
        business.Nodes.Add("workingCapital", "საბრუნავი კაპიტალი");

        tree.NodeMouseDoubleClick += (_, e) => Pick(e.Node);
        tree.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && tree.SelectedNode is not null)
            {
                Pick(tree.SelectedNode);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };
        Deactivate += (_, _) => Close();
        Controls.Add(tree);
    }

    private void Pick(TreeNode node)
    {
        _onPicked(node.Text);
        Close();
    }
}
