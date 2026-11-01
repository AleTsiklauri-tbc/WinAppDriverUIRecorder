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
    private readonly UIA3Automation _automation;
    private readonly IValueReader _valueReader;
    private readonly TimeProvider _time;
    private readonly ITreeWalker _rawWalker;
    private readonly CacheRequest _elementRequest;
    private readonly CacheRequest _ancestorRequest;
    private readonly CacheRequest _siblingsRequest;
    private readonly PatternId[] _patterns;

    public ElementSnapshotFactory(UIA3Automation automation, IValueReader valueReader, TimeProvider time)
    {
        _automation = automation;
        _valueReader = valueReader;
        _time = time;
        _rawWalker = automation.TreeWalkerFactory.GetRawViewWalker();
        _patterns = automation.PatternLibrary.AllForCurrentFramework;

        var p = automation.PropertyLibrary.Element;
        PropertyId[] common = NonNull(p.ControlType, p.AutomationId, p.Name, p.ClassName, p.FrameworkId, p.BoundingRectangle, p.NativeWindowHandle, p.RuntimeId, p.ProcessId);
        _ancestorRequest = Request(common);
        _siblingsRequest = Request(NonNull(p.ControlType, p.Name, p.BoundingRectangle, p.IsOffscreen));
        _siblingsRequest.TreeScope = TreeScope.Children;

        // ValuePattern.Value is deliberately not cached: it would be read even for password fields.
        _elementRequest = Request([.. common, .. NonNull(p.IsPassword, p.IsEnabled, p.IsOffscreen, p.LabeledBy), .. NonNull([.. _patterns.Select(x => x.AvailabilityProperty)])]);
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

        return Build(element, findLabel, cancellationToken);
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

    private CapturedElement Build(AutomationElement element, bool findLabel, CancellationToken cancellationToken)
    {
        string controlType;
        string? automationId, name, className, frameworkId;
        bool isPassword, isEnabled, isOffscreen, hasValuePattern;
        PixelRect bounds;
        int processId;
        nint handle;
        int[] runtimeId;
        AutomationElement? labeledBy;
        List<string> patterns;
        using (_elementRequest.Activate())
        {
            var props = element.Properties;
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
                    && element.FrameworkAutomationElement.TryGetPropertyValue<bool>(availability, out var available)
                    && available)
                .Select(pattern => pattern.Name)
                .ToList();
            hasValuePattern = patterns.Contains(_automation.PatternLibrary.ValuePattern.Name);
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

        var snapshot = new ElementSnapshot(
            controlType, automationId, name, className, frameworkId, isPassword, isEnabled, isOffscreen, bounds, processId, handle,
            window, ancestors, patterns, value, labelText, runtimeId, _time.GetLocalNow(), host, isDetachedPopup);
        return new CapturedElement(element, snapshot, ancestorElements);
    }

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

    private static PixelRect ToPixels(Rectangle rectangle) =>
        rectangle.IsEmpty ? default : new PixelRect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

    // FlaUI exposes properties a framework does not support as null.
    private static PropertyId[] NonNull(params PropertyId?[] properties) => [.. properties.OfType<PropertyId>()];

    private static CacheRequest Request(IEnumerable<PropertyId> properties)
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
