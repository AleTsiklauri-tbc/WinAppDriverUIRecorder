using Recorder.Core.Model;

namespace Recorder.Core.Locators;

/// <summary>
/// One element of a fetched UI tree, with its attributes as WinAppDriver reports them: the control type is the XPath
/// tag, and a missing AutomationId, Name or ClassName is an empty string.
/// </summary>
public sealed class UiNode
{
    private readonly List<UiNode> _children = [];

    public UiNode(
        string controlType,
        string? automationId = null,
        string? name = null,
        string? className = null,
        string? frameworkId = null,
        nint nativeWindowHandle = 0,
        PixelRect bounds = default,
        bool isOffscreen = false,
        string? runtimeKey = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(controlType);
        ControlType = controlType;
        AutomationId = automationId ?? string.Empty;
        Name = name ?? string.Empty;
        ClassName = className ?? string.Empty;
        FrameworkId = frameworkId;
        NativeWindowHandle = nativeWindowHandle;
        Bounds = bounds;
        IsOffscreen = isOffscreen;
        RuntimeKey = runtimeKey ?? string.Empty;
    }

    public string ControlType { get; }

    public string AutomationId { get; }

    public string Name { get; }

    public string ClassName { get; }

    public string? FrameworkId { get; }

    public nint NativeWindowHandle { get; }

    public PixelRect Bounds { get; }

    public bool IsOffscreen { get; }

    /// <summary>The runtime id as text. The same element can sit at two places in the tree (an open menu, F-21).</summary>
    public string RuntimeKey { get; }

    public UiNode? Parent { get; private set; }

    public IReadOnlyList<UiNode> Children => _children;

    /// <summary>Position in document order (pre-order), 0 for the root. Set when the tree is built.</summary>
    public int Order { get; internal set; }

    /// <summary>Inside a title bar, where WinAppDriver reports different AutomationIds than UI Automation (F-22).</summary>
    public bool IsNonClient => Ancestors().Any(ancestor => ancestor.ControlType == "TitleBar");

    public UiNode Add(UiNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Parent is not null)
        {
            throw new InvalidOperationException("The node already has a parent.");
        }

        child.Parent = this;
        _children.Add(child);
        return this;
    }

    public UiNode AddRange(params UiNode[] children)
    {
        ArgumentNullException.ThrowIfNull(children);
        foreach (var child in children)
        {
            Add(child);
        }

        return this;
    }

    public string Attribute(AttributeName attribute) => attribute switch
    {
        AttributeName.AutomationId => AutomationId,
        AttributeName.Name => Name,
        AttributeName.ClassName => ClassName,
        _ => throw new ArgumentOutOfRangeException(nameof(attribute)),
    };

    /// <summary>Parent first, root last.</summary>
    public IEnumerable<UiNode> Ancestors()
    {
        for (var ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            yield return ancestor;
        }
    }

    public bool IsDescendantOf(UiNode node) => Ancestors().Contains(node);

    public override string ToString() => $"{ControlType} '{AutomationId}' '{Name}'";
}

/// <summary>A UI tree in document order: the tree WinAppDriver searches for one session (its root and everything below).</summary>
public sealed class UiTree
{
    private readonly List<UiNode> _nodes = [];
    private readonly Dictionary<string, List<UiNode>> _byKey = new(StringComparer.Ordinal);

    public UiTree(UiNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.Parent is not null)
        {
            throw new ArgumentException("The root must not have a parent.", nameof(root));
        }

        Root = root;
        var stack = new Stack<UiNode>();
        stack.Push(root);
        while (stack.TryPop(out var node))
        {
            node.Order = _nodes.Count;
            _nodes.Add(node);
            if (node.RuntimeKey.Length > 0)
            {
                if (!_byKey.TryGetValue(node.RuntimeKey, out var sameElement))
                {
                    _byKey[node.RuntimeKey] = sameElement = [];
                }

                sameElement.Add(node);
            }

            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(node.Children[i]);
            }
        }
    }

    public UiNode Root { get; }

    /// <summary>All nodes in document order.</summary>
    public IReadOnlyList<UiNode> Nodes => _nodes;

    public int Count => _nodes.Count;

    /// <summary>The first place the element with this runtime id appears, or null.</summary>
    public UiNode? Find(string runtimeKey) => _byKey.TryGetValue(runtimeKey, out var nodes) ? nodes[0] : null;
}
