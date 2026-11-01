using System;
using System.Drawing;
using System.Windows.Forms;

namespace Recorder.SampleApp;

/// <summary>
/// Stand-in for third-party owner-drawn controls (LMS uses DevExpress v3): one window handle,
/// items painted by hand and exposed only through MSAA, so items have no AutomationId.
/// </summary>
internal sealed class OwnerDrawnNavBar : Control
{
    private static readonly string[] Items = { "განაცხადები", "სესხები", "ოპერაციები" };
    private readonly Action<string> _setStatus;

    public OwnerDrawnNavBar(Action<string> setStatus)
    {
        _setStatus = setStatus;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        TabStop = true;
    }

    private int ItemHeight => Math.Max(1, Height / Items.Length);

    private Rectangle ItemBounds(int index) => new(0, index * ItemHeight, Width, ItemHeight);

    private void Activate(int index) => _setStatus(Items[index] + " opened");

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(SystemColors.ControlLight);
        for (var i = 0; i < Items.Length; i++)
        {
            var bounds = ItemBounds(i);
            e.Graphics.DrawRectangle(SystemPens.ControlDark, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, Items[i], Font, bounds, SystemColors.ControlText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var index = e.Y / ItemHeight;
        if (index >= 0 && index < Items.Length)
        {
            Activate(index);
        }
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new NavBarAccessibleObject(this);

    private sealed class NavBarAccessibleObject : ControlAccessibleObject
    {
        private readonly OwnerDrawnNavBar _owner;

        public NavBarAccessibleObject(OwnerDrawnNavBar owner)
            : base(owner) => _owner = owner;

        public override AccessibleRole Role => AccessibleRole.ToolBar;

        public override int GetChildCount() => Items.Length;

        public override AccessibleObject GetChild(int index) => new ItemAccessibleObject(_owner, this, index);

        public override AccessibleObject HitTest(int x, int y)
        {
            var client = _owner.PointToClient(new Point(x, y));
            for (var i = 0; i < Items.Length; i++)
            {
                if (_owner.ItemBounds(i).Contains(client))
                {
                    return GetChild(i);
                }
            }

            return base.HitTest(x, y);
        }
    }

    private sealed class ItemAccessibleObject : AccessibleObject
    {
        private readonly OwnerDrawnNavBar _owner;
        private readonly AccessibleObject _parent;
        private readonly int _index;

        public ItemAccessibleObject(OwnerDrawnNavBar owner, AccessibleObject parent, int index)
        {
            _owner = owner;
            _parent = parent;
            _index = index;
        }

        public override string Name => Items[_index];

        public override AccessibleRole Role => AccessibleRole.PushButton;

        public override AccessibleObject Parent => _parent;

        public override Rectangle Bounds => _owner.RectangleToScreen(_owner.ItemBounds(_index));

        public override AccessibleStates State => AccessibleStates.Focusable;

        public override string DefaultAction => "Press";

        public override void DoDefaultAction() => _owner.Activate(_index);
    }
}
