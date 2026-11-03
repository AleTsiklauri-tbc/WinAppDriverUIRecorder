using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Resources;
using Recorder.App.Services;
using Recorder.Core.Automation;
using Recorder.Core.Locators;
using Recorder.Core.Model;
using Recorder.Core.Recording;

namespace Recorder.App.ViewModels;

/// <summary>
/// The center pane for one element (spec 11.2): header with the variable name, the locator card and other locators
/// (<see cref="LocatorsViewModel"/>), ancestors, and the Advanced section (absolute XPath, properties, patterns).
/// </summary>
public sealed partial class InspectorViewModel : ObservableObject
{
    private readonly IUiaService _uia;
    private readonly ILocatorService _locators;
    private readonly IClipboardService _clipboard;
    private readonly ToastService _toasts;
    private readonly ElementCatalog _catalog;
    private readonly EditHistory _history;
    private PageElement? _element;
    private int _xpathRequest;

    public InspectorViewModel(
        IUiaService uia, ILocatorService locators, IClipboardService clipboard, ToastService toasts, ElementCatalog catalog, LocatorsViewModel locatorCard,
        EditHistory history)
    {
        _uia = uia;
        _locators = locators;
        _clipboard = clipboard;
        _toasts = toasts;
        _catalog = catalog;
        _history = history;
        Locators = locatorCard;
    }

    public LocatorsViewModel Locators { get; }

    /// <summary>The variable name, editable once the element is captured (Enter keeps it).</summary>
    [ObservableProperty]
    public partial string? EditableName { get; set; }

    /// <summary>The user asked to capture the element being inspected (an ancestor, for example).</summary>
    public event EventHandler<ElementSnapshot>? CaptureRequested;

    public ObservableCollection<PropertyRow> Properties { get; } = [];

    public ObservableCollection<AncestorItem> Ancestors { get; } = [];

    public ObservableCollection<string> Patterns { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasElement), nameof(Title), nameof(PageText))]
    public partial ElementSnapshot? Snapshot { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CaptureCommand))]
    public partial bool IsCaptured { get; private set; }

    [ObservableProperty]
    public partial bool IsAdvancedExpanded { get; set; }

    [ObservableProperty]
    public partial string? AbsoluteXPath { get; private set; }

    [ObservableProperty]
    public partial bool IsXPathLoading { get; private set; }

    [ObservableProperty]
    public partial bool XPathFailed { get; private set; }

    public bool HasElement => Snapshot is not null;

    public string Title => Snapshot is null ? string.Empty : ElementLabel.For(Snapshot);

    public string PageText => Snapshot is null ? string.Empty : Text.Format(Strings.Inspector_Page, PageKey.For(Snapshot).DisplayName);

    public void Show(ElementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Show(snapshot, _catalog.Find(snapshot.SessionKey));
    }

    /// <summary>Shows a listed element, including one opened from a recording that has not been captured again yet.</summary>
    public void ShowElement(PageElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        Show(element.Snapshot, element);
    }

    private void Show(ElementSnapshot snapshot, PageElement? element)
    {
        if (_element is not null)
        {
            _element.Changed -= OnElementChanged;
        }

        Snapshot = snapshot;
        _element = element;
        IsCaptured = _element is not null;
        EditableName = _element?.VariableName;
        if (_element is not null)
        {
            _element.Changed += OnElementChanged;
        }

        Locators.Show(_element, snapshot);
        Replace(Properties, InspectorRows.Properties(snapshot));
        Replace(Ancestors, InspectorRows.Ancestors(snapshot));
        Replace(Patterns, snapshot.SupportedPatterns.Order(StringComparer.Ordinal));
        AbsoluteXPath = null;
        XPathFailed = false;
        _xpathRequest++;
        if (IsAdvancedExpanded)
        {
            LoadXPathCommand.Execute(null);
        }
    }

    /// <summary>Shows the element again after the catalog changed (captured, merged).</summary>
    public void RefreshCapturedState()
    {
        if (Snapshot is not null && Snapshot.SessionKey.Length > 0 && _catalog.Find(Snapshot.SessionKey) != _element)
        {
            Show(Snapshot);
        }
    }

    private void OnElementChanged(object? sender, EventArgs e)
    {
        if (_element is not null && EditableName != _element.VariableName)
        {
            EditableName = _element.VariableName;
        }
    }

    [RelayCommand]
    private void CommitName()
    {
        // Leaving the box without typing must not pin the suggested name (UserRenamed) or re-render everything.
        if (_element is not { } element || EditableName == element.VariableName)
        {
            return;
        }

        var (oldName, wasRenamed) = (element.VariableName, element.UserRenamed);
        if (!_catalog.Rename(element, EditableName ?? string.Empty))
        {
            EditableName = element.VariableName; // nothing usable typed: keep the old name
            return;
        }

        // Undoable (spec 11.6): the rename is already applied, so applying it again is a no-op.
        var newName = element.VariableName;
        _history.Execute(() => _catalog.RestoreName(element, newName, userRenamed: true), () => _catalog.RestoreName(element, oldName, wasRenamed));
    }

    partial void OnIsAdvancedExpandedChanged(bool value)
    {
        if (value && Snapshot is not null && AbsoluteXPath is null && !IsXPathLoading)
        {
            LoadXPathCommand.Execute(null);
        }
    }

    [RelayCommand]
    private async Task LoadXPathAsync()
    {
        if (Snapshot is null)
        {
            return;
        }

        var request = ++_xpathRequest;
        IsXPathLoading = true;
        XPathFailed = false;
        var result = await _locators.AbsolutePathAsync(Snapshot, CancellationToken.None).ConfigureAwait(true);
        if (request != _xpathRequest)
        {
            return; // another element was selected meanwhile
        }

        IsXPathLoading = false;
        AbsoluteXPath = result.IsOk ? XPathRenderer.Render(result.Value!) : null;
        XPathFailed = !result.IsOk;
    }

    [RelayCommand]
    private void CopyXPath()
    {
        if (AbsoluteXPath is not null)
        {
            Copy(AbsoluteXPath);
        }
    }

    [RelayCommand]
    private void CopyProperty(PropertyRow? row)
    {
        if (row is not null && row.Value != Strings.Inspector_NoValue)
        {
            Copy(row.Value);
        }
    }

    [RelayCommand]
    private async Task InspectAncestorAsync(AncestorItem? item)
    {
        if (item is null)
        {
            return;
        }

        var result = await _uia.RefreshAsync(item.SessionKey, CancellationToken.None).ConfigureAwait(true);
        if (result.IsOk)
        {
            Show(result.Value!);
        }
        else if (result.Status != UiaStatus.Cancelled)
        {
            _toasts.Show(Strings.Toast_ElementGone);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCapture))]
    private void Capture()
    {
        if (Snapshot is not null)
        {
            CaptureRequested?.Invoke(this, Snapshot);
        }
    }

    private bool CanCapture() => Snapshot is not null && !IsCaptured;

    private void Copy(string text) =>
        _toasts.Show(_clipboard.TrySetText(text) ? Strings.Toast_Copied : Strings.Error_ClipboardBusy);

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
