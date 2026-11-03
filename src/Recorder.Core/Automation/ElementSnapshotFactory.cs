using System.Drawing;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Identifiers;
using FlaUI.UIA3;
using Recorder.Core.Interop;
using Recorder.Core.Model;
using Recorder.Core.Naming;

namespace Recorder.Core.Automation;

/// <summary>An element read from UI Automation, with the live elements needed to inspect it again later.</summary>
internal sealed record CapturedElement(AutomationElement Element, ElementSnapshot Snapshot, IReadOnlyList<AutomationElement> AncestorElements);

/// <summary>
/// Builds <see cref="ElementSnapshot"/>s. All properties come from cache requests, so an element costs one
/// cross-process call and each ancestor one more. Worker thread only.
/// </summary>
internal sealed class ElementSnapshotFactory
{
    // Menu bars, tool strips and drop-down menus answer a hit test with the strip itself, never the item (F-25).
    private static readonly HashSet<string> Strips = new(StringComparer.Ordinal) { "MenuBar", "Menu", "ToolBar" };
    private const int MaxStripDepth = 3;

    private readonly UIA3Automation _automation;
    private readonly IValueReader _valueReader;
    private readonly TimeProvider _time;
    private readonly ITreeWalker _rawWalker;
    private readonly CacheRequest _elementRequest;
    private readonly CacheRequest _ancestorRequest;
    private readonly CacheRequest _siblingsRequest;
    private readonly CacheRequest _processRequest;
    private readonly PatternId[] _patterns;
    private readonly PropertyId _gridRow;
    private readonly PropertyId _gridColumn;

    public ElementSnapshotFactory(UIA3Automation automation, IValueReader valueReader, TimeProvider time)
    {
        _automation = automation;
        _valueReader = valueReader;
        _time = time;
        _rawWalker = automation.TreeWalkerFactory.GetRawViewWalker();
        _patterns = automation.PatternLibrary.AllForCurrentFramework;
        _gridRow = automation.PropertyLibrary.GridItem.Row;
        _gridColumn = automation.PropertyLibrary.GridItem.Column;

        var p = automation.PropertyLibrary.Element;
        PropertyId[] common = NonNull(p.ControlType, p.AutomationId, p.Name, p.ClassName, p.FrameworkId, p.BoundingRectangle, p.NativeWindowHandle, p.RuntimeId, p.ProcessId);
        _ancestorRequest = Request(common);
        _siblingsRequest = Request(NonNull(p.ControlType, p.Name, p.BoundingRectangle, p.IsOffscreen));
        _siblingsRequest.TreeScope = TreeScope.Children;
        _processRequest = Request(NonNull(p.ProcessId));

        // ValuePattern.Value is deliberately not cached: it would be read even for password fields.
        _elementRequest = Request([.. common, .. NonNull(p.IsPassword, p.IsEnabled, p.IsOffscreen, p.LabeledBy, _gridRow, _gridColumn), .. NonNull([.. _patterns.Select(x => x.AvailabilityProperty)])]);
    }

    /// <summary>The element under a screen point; null when there is none or it belongs to <paramref name="ignoredProcessId"/>.</summary>
    /// <param name="findLabel">Also look for a nearby label (7.8): one more call, worth it for a capture but not for hovering.</param>
    public CapturedElement? AtPoint(PixelPoint point, int ignoredProcessId, bool findLabel, CancellationToken cancellationToken)
    {
        AutomationElement? element;
        using (_elementRequest.Activate())
        {
            element = _automation.FromPoint(new Point(point.X, point.Y));
            if (element is null || element.Properties.ProcessId.ValueOrDefault == ignoredProcessId)
            {
                return null;
            }
        }

        return Build(ItemInStrip(element, point, cancellationToken), findLabel, cancellationToken);
    }

    /// <summary>Reads a previously seen element again (for example an ancestor the user clicked); null if it is gone.</summary>
    public CapturedElement? Refresh(AutomationElement element, CancellationToken cancellationToken)
    {
        AutomationElement? updated;
        using (_elementRequest.Activate())
        {
            updated = element.FrameworkAutomationElement.GetUpdatedCache();
        }

        return updated is null ? null : Build(updated, findLabel: true, cancellationToken);
    }

    /// <summary>The element that received keyboard focus, if <paramref name="accept"/> takes its process; otherwise null.</summary>
    public CapturedElement? Focused(AutomationElement element, Func<int, bool> accept, CancellationToken cancellationToken)
    {
        int processId;
        using (_processRequest.Activate())
        {
            processId = element.FrameworkAutomationElement.GetUpdatedCache()?.Properties.ProcessId.ValueOrDefault ?? 0;
        }

        return processId != 0 && accept(processId) ? Refresh(element, cancellationToken) : null;
    }

    /// <summary>Raw-view ancestors, nearest first, excluding the desktop, each with its key properties cached.</summary>
    public List<AutomationElement> AncestorsOf(AutomationElement element, CancellationToken cancellationToken)
    {
        var chain = new List<AutomationElement>();
        using (_ancestorRequest.Activate())
        {
            for (var parent = _rawWalker.GetParent(element); parent is not null; parent = _rawWalker.GetParent(parent))
            {
                cancellationToken.ThrowIfCancellationRequested();
                chain.Add(parent);
            }
        }

        // The last element reached is the desktop root.
        if (chain.Count > 0)
        {
            chain.RemoveAt(chain.Count - 1);
        }

        return chain;
    }

    /// <summary>Activates the ancestor cache request so cached ancestor properties can be read.</summary>
    public IDisposable ReadAncestorCache() => _ancestorRequest.Activate();

    // The item of a menu or tool strip under the point is the strip's child whose bounds contain it (F-25).
    private AutomationElement ItemInStrip(AutomationElement element, PixelPoint point, CancellationToken cancellationToken)
    {
        var current = element;
        for (var depth = 0; depth < MaxStripDepth; depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (_siblingsRequest.Activate())
            {
                if (!IsStrip(current))
                {
                    break;
                }

                var item = current.FrameworkAutomationElement.GetUpdatedCache()?.CachedChildren
                    .FirstOrDefault(child => !child.Properties.IsOffscreen.ValueOrDefault && ToPixels(child.Properties.BoundingRectangle.ValueOrDefault).Contains(point));
                if (item is null)
                {
                    break;
                }

                current = item;
            }

            using (_elementRequest.Activate())
            {
                current = current.FrameworkAutomationElement.GetUpdatedCache() ?? current;
            }
        }

        return current;
    }

    // Called with a cache request active whose properties include ControlType.
    private static bool IsStrip(AutomationElement element) => Strips.Contains(ControlTypeNames.Of(element.Properties.ControlType.ValueOrDefault));

    private CapturedElement Build(AutomationElement element, bool findLabel, CancellationToken cancellationToken)
    {
        string controlType;
        string? automationId, name, className, frameworkId;
        bool isPassword, isEnabled, isOffscreen, hasValuePattern, isGridItem;
        PixelRect bounds;
        int processId, gridRow = 0, gridColumn = 0;
        nint handle;
        int[] runtimeId;
        AutomationElement? labeledBy;
        List<string> patterns;
        using (_elementRequest.Activate())
        {
            var props = element.Properties;
            var framework = element.FrameworkAutomationElement;
            controlType = ControlTypeNames.Of(props.ControlType.ValueOrDefault);
            automationId = props.AutomationId.ValueOrDefault;
            name = props.Name.ValueOrDefault;
            className = props.ClassName.ValueOrDefault;
            frameworkId = props.FrameworkId.ValueOrDefault;
            isPassword = props.IsPassword.ValueOrDefault;
            isEnabled = props.IsEnabled.ValueOrDefault;
            isOffscreen = props.IsOffscreen.ValueOrDefault;
            bounds = ToPixels(props.BoundingRectangle.ValueOrDefault);
            processId = props.ProcessId.ValueOrDefault;
            handle = props.NativeWindowHandle.ValueOrDefault;
            runtimeId = props.RuntimeId.ValueOrDefault ?? [];
            labeledBy = props.LabeledBy.ValueOrDefault;
            patterns = _patterns
                .Where(pattern => pattern.AvailabilityProperty is { } availability
                    && framework.TryGetPropertyValue<bool>(availability, out var available)
                    && available)
                .Select(pattern => pattern.Name)
                .ToList();
            hasValuePattern = patterns.Contains(_automation.PatternLibrary.ValuePattern.Name);
            isGridItem = patterns.Contains(_automation.PatternLibrary.GridItemPattern.Name)
                && framework.TryGetPropertyValue(_gridRow, out gridRow)
                && framework.TryGetPropertyValue(_gridColumn, out gridColumn);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var ancestorElements = AncestorsOf(element, cancellationToken);
        List<AncestorInfo> ancestors;
        using (ReadAncestorCache())
        {
            ancestors = ancestorElements.Select(ToAncestorInfo).ToList();
        }

        var top = ancestors.Count > 0 ? ancestors[^1] : null;
        var window = top is null
            ? new WindowInfo(handle, name, automationId, className)
            : new WindowInfo(top.NativeWindowHandle, top.Name, top.AutomationId, top.ClassName);
        var (host, isDetachedPopup) = HostOf(handle, automationId, ancestors, window);

        // Live reads, outside the cache scope. The value of a password field is never requested.
        var value = !isPassword && hasValuePattern ? _valueReader.ReadValue(element) : null;
        var labelText = labeledBy?.Properties.Name.ValueOrDefault;
        if (string.IsNullOrWhiteSpace(labelText) && findLabel && LabelFinder.NeedsLabel(controlType) && ancestorElements.Count > 0)
        {
            labelText = NearbyLabel(ancestorElements[0], bounds);
        }

        var cell = isGridItem ? new Model.GridCell(gridRow, gridColumn, ColumnHeaderOf(element)) : null;
        var snapshot = new ElementSnapshot(
            controlType, automationId, name, className, frameworkId, isPassword, isEnabled, isOffscreen, bounds, processId, handle,
            window, ancestors, patterns, value, labelText, runtimeId, _time.GetLocalNow(), host, isDetachedPopup, cell);
        return new CapturedElement(element, snapshot, ancestorElements);
    }

    // The header of a grid cell's column (TableItem pattern): language-neutral, unlike the cell's generated Name (F-27).
    private static string? ColumnHeaderOf(AutomationElement cell) =>
        cell.Patterns.TableItem.PatternOrDefault?.ColumnHeaderItems.ValueOrDefault is { Length: > 0 } headers
            ? headers[0].Properties.Name.ValueOrDefault
            : null;

    // The Win32 top-level window around the element. An owned dialog or menu is a separate top-level window although
    // UI Automation nests it under its owner (F-7); an unowned pop-up is out of reach of a window session (F-23).
    private static (WindowInfo? Host, bool IsDetachedPopup) HostOf(nint handle, string? automationId, List<AncestorInfo> ancestors, WindowInfo top)
    {
        var nearest = handle != 0 ? handle : ancestors.Select(a => a.NativeWindowHandle).FirstOrDefault(h => h != 0);
        var root = Win32.RootOf(nearest);
        if (root == 0 || root == top.Handle)
        {
            return (null, false);
        }

        var id = root == handle ? automationId : ancestors.FirstOrDefault(a => a.NativeWindowHandle == root)?.AutomationId;
        var host = new WindowInfo(root, Win32.WindowText(root), id, Win32.ClassName(root));
        return (host, !Win32.IsOwnedBy(root, top.Handle));
    }

    // The text next to an input that has no LabeledBy (7.8): one cached read of the parent's children.
    private string? NearbyLabel(AutomationElement parent, PixelRect bounds)
    {
        using (_siblingsRequest.Activate())
        {
            var updated = parent.FrameworkAutomationElement.GetUpdatedCache();
            if (updated is null)
            {
                return null;
            }

            var siblings = updated.CachedChildren.Select(child => new LabelCandidate(
                ControlTypeNames.Of(child.Properties.ControlType.ValueOrDefault),
                child.Properties.Name.ValueOrDefault,
                ToPixels(child.Properties.BoundingRectangle.ValueOrDefault),
                child.Properties.IsOffscreen.ValueOrDefault));
            return LabelFinder.Find(bounds, siblings.ToList(), Win32.ScaleAt(bounds));
        }
    }

    internal static AncestorInfo ToAncestorInfo(AutomationElement element)
    {
        var props = element.Properties;
        return new AncestorInfo(
            ControlTypeNames.Of(props.ControlType.ValueOrDefault),
            props.AutomationId.ValueOrDefault,
            props.Name.ValueOrDefault,
            props.ClassName.ValueOrDefault,
            props.FrameworkId.ValueOrDefault,
            ToPixels(props.BoundingRectangle.ValueOrDefault),
            props.NativeWindowHandle.ValueOrDefault,
            props.RuntimeId.ValueOrDefault ?? []);
    }

    internal static PixelRect ToPixels(Rectangle rectangle) =>
        rectangle.IsEmpty ? default : new PixelRect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

    // FlaUI exposes properties a framework does not support as null.
    internal static PropertyId[] NonNull(params PropertyId?[] properties) => [.. properties.OfType<PropertyId>()];

    internal static CacheRequest Request(IEnumerable<PropertyId> properties)
    {
        var request = new CacheRequest
        {
            TreeScope = TreeScope.Element,
            TreeFilter = TrueCondition.Default,
            AutomationElementMode = AutomationElementMode.Full,
        };
        foreach (var property in properties)
        {
            request.Add(property);
        }

        return request;
    }
}
