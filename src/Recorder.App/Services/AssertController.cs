using System.Windows;
using System.Windows.Threading;
using Recorder.App.Overlay;
using Recorder.App.Resources;
using Recorder.App.ViewModels;
using Recorder.App.Views;
using Recorder.Core.Automation;
using Recorder.Core.Model;
using Recorder.Core.Recording;
using static Recorder.App.Interop.NativeMethods;

namespace Recorder.App.Services;

/// <summary>
/// Assert mode (spec 9.6): the hovered control is outlined, the next click on the target is kept from it and opens the
/// assertion picker next to the cursor; the chosen assertion becomes a step. Esc cancels. UI thread only.
/// </summary>
public sealed class AssertController
{
    private readonly HotkeyService _hotkeys;
    private readonly ToastService _toasts;
    private readonly HoverHighlighter _highlighter;
    private readonly Dispatcher _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    private RecordingSession? _session;

    public AssertController(HotkeyService hotkeys, ToastService toasts, ILocatorService locators, ElementCatalog catalog, Overlays overlays)
    {
        ArgumentNullException.ThrowIfNull(overlays);
        (_hotkeys, _toasts) = (hotkeys, toasts);
        _highlighter = new HoverHighlighter(locators, catalog, () => overlays.Hover);
    }

    /// <summary>Assert mode started or ended.</summary>
    public event EventHandler? ModeChanged;

    public bool IsArmed => _session is not null;

    public void Arm(RecordingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (IsArmed || session.Status != RecordingStatus.Recording)
        {
            return;
        }

        _session = session;
        session.ArmAssert();
        session.HoverUpdated += OnHoverUpdated;
        _hotkeys.Register(HotkeyId.CancelMode, HotkeyGesture.Escape);
        ModeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Cancel()
    {
        _session?.CancelAssert();
        Disarm();
    }

    /// <summary>The click of Assert mode landed (raised by the session; called here on the UI thread).</summary>
    public void OnAssertRequested(RecordingSession session, AssertTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Disarm();
        if (target.Element is not { } element)
        {
            _toasts.Show(Strings.Toast_AssertNothing);
            return;
        }

        var picker = new AssertPickerViewModel(element, AssertionChoices.For(element, target.State));
        var window = new AssertPickerWindow(picker, target.Point);
        window.Closed += (_, _) =>
        {
            if (picker.Chosen is { } choice && session.Status == RecordingStatus.Recording)
            {
                session.AddAssertion(choice.Kind, element, choice.Text, choice.State);
                _toasts.Show(Text.Format(Strings.Toast_AssertionAdded, AssertPickerViewModel.Label(choice)));
            }

            // Keyboard focus goes back to the app being recorded, so the user carries on where they were.
            if (IsWindow(element.Host.Handle))
            {
                SetForegroundWindow(element.Host.Handle);
            }
        };
        window.Show();
        window.Activate();
    }

    private void Disarm()
    {
        if (_session is null)
        {
            return;
        }

        _session.HoverUpdated -= OnHoverUpdated;
        _session = null;
        _hotkeys.Unregister(HotkeyId.CancelMode);
        _highlighter.Hide();
        ModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnHoverUpdated(object? sender, HoverUpdate update) => _dispatcher.BeginInvoke(() =>
    {
        if (!IsArmed)
        {
            return;
        }

        if (update is { Kind: HoverKind.Element, Element: { } element })
        {
            _highlighter.Show(element);
        }
        else
        {
            _highlighter.Hide();
        }
    });
}
