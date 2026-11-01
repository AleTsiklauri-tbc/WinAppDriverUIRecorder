using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Identifiers;
using FlaUI.UIA3;
using Recorder.Core.Interop;
using Recorder.Core.Locators;
using Recorder.Core.Model;

namespace Recorder.Core.Automation;

/// <summary>
/// Reads the raw-view tree of one window in a single cached call, in the shape a WinAppDriver session on that window
/// searches (F-20): the window and everything below it, minus the contents of unowned pop-up windows that UI
/// Automation nests under their control but WinAppDriver does not (F-23). Worker thread only.
/// </summary>
internal sealed class TreeFetcher
{
    private readonly UIA3Automation _automation;
    private readonly CacheRequest _request;

    public TreeFetcher(UIA3Automation automation)
    {
        _automation = automation;
        var p = automation.PropertyLibrary.Element;
        _request = new CacheRequest
        {
            TreeScope = TreeScope.Subtree,
            TreeFilter = TrueCondition.Default,
            AutomationElementMode = AutomationElementMode.None,
        };
        PropertyId?[] properties = [p.ControlType, p.AutomationId, p.Name, p.ClassName, p.FrameworkId, p.RuntimeId, p.NativeWindowHandle, p.BoundingRectangle, p.IsOffscreen];
        foreach (var property in properties.OfType<PropertyId>())
        {
            _request.Add(property);
        }
    }

    public UiTree Fetch(nint rootWindow, CancellationToken cancellationToken)
    {
        using (_request.Activate())
        {
            var root = _automation.FromHandle(rootWindow);
            cancellationToken.ThrowIfCancellationRequested();
            return new UiTree(Convert(root, rootWindow));
        }
    }

    private static UiNode Convert(AutomationElement element, nint rootWindow)
    {
        var node = ToNode(element);
        foreach (var child in element.CachedChildren)
        {
            var handle = child.Properties.NativeWindowHandle.ValueOrDefault;
            if (handle != 0 && handle != rootWindow && Win32.IsTopLevel(handle) && !Win32.IsOwnedBy(handle, rootWindow))
            {
                continue;
            }

            node.Add(Convert(child, rootWindow));
        }

        return node;
    }

    private static UiNode ToNode(AutomationElement element)
    {
        var props = element.Properties;
        var rectangle = props.BoundingRectangle.ValueOrDefault;
        return new UiNode(
            ControlTypeNames.Of(props.ControlType.ValueOrDefault),
            props.AutomationId.ValueOrDefault,
            props.Name.ValueOrDefault,
            props.ClassName.ValueOrDefault,
            props.FrameworkId.ValueOrDefault,
            props.NativeWindowHandle.ValueOrDefault,
            rectangle.IsEmpty ? default : new PixelRect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height),
            props.IsOffscreen.ValueOrDefault,
            RuntimeKey.Of(props.RuntimeId.ValueOrDefault ?? []));
    }
}
