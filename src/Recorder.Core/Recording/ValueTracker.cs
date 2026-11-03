using Recorder.Core.Automation;
using Recorder.Core.Model;

namespace Recorder.Core.Recording;

/// <summary>
/// The input that has keyboard focus and its value when the user started on it (spec 9.3). Typed text is never captured:
/// the value is compared when focus leaves, and before any click or recorded key. A password field is never read; typing
/// into one (or, without the keyboard hook, one visit) becomes a secret value (D-50). Used by the session loop only.
/// </summary>
internal sealed class ValueTracker(IUiaService uia, Func<bool> keysActive)
{
    private ElementSnapshot? _focused;
    private string? _baseline;
    private bool _secretTyped;
    private bool _secretRecorded;
    private volatile bool _passwordFocused;

    public ElementSnapshot? Focused => _focused;

    /// <summary>Read by the keyboard hook callback.</summary>
    public bool IsPasswordFocused => _passwordFocused;

    public bool IsFocused(ElementSnapshot? element) =>
        element is not null && element.SessionKey.Length > 0 && element.SessionKey == _focused?.SessionKey;

    public void OnSecretTyped()
    {
        if (_focused is { IsPassword: true })
        {
            _secretTyped = true;
        }
    }

    /// <summary>
    /// Focus moved to <paramref name="element"/> (null: to another app or the recorder). The value before typing comes from the
    /// click that focused it when there was one, which was read before the click (D-51); otherwise from the focus snapshot.
    /// </summary>
    /// <param name="recording">False while paused: a visit then never counts as typing into a password field.</param>
    public void Focus(ElementSnapshot? element, ElementSnapshot? clicked, bool recording)
    {
        _focused = element;
        _passwordFocused = element?.IsPassword ?? false;
        _baseline = clicked is not null && element is not null && clicked.SessionKey == element.SessionKey ? clicked.Value : element?.Value;
        _secretTyped = false;
        _secretRecorded = !recording;
    }

    /// <summary>The focused input's change since the last check, or null; the baseline moves on.</summary>
    public async Task<ValueInput?> FlushAsync(DateTimeOffset at, CancellationToken token)
    {
        if (_focused is not { } element)
        {
            return null;
        }

        if (element.IsPassword)
        {
            // Never read. Without the keyboard hook there is no way to know whether something was typed, so one visit counts.
            if (!_secretTyped && (keysActive() || _secretRecorded))
            {
                return null;
            }

            _secretTyped = false;
            _secretRecorded = true;
            return new ValueInput(element, null, null, null, IsSecret: true, at);
        }

        if (!StepTargets.IsValueInput(element))
        {
            return null;
        }

        var state = await uia.ReadStateAsync(element.SessionKey, token).ConfigureAwait(false);
        if (!state.IsOk || state.Value!.Value == _baseline)
        {
            return null;
        }

        var before = _baseline;
        _baseline = state.Value.Value;
        return new ValueInput(element, null, before, _baseline, IsSecret: false, at);
    }

    /// <summary>After a pause: what changed meanwhile is not recorded.</summary>
    public async Task RebaselineAsync(CancellationToken token)
    {
        if (_focused is { IsPassword: false } focused && StepTargets.IsValueInput(focused))
        {
            var state = await uia.ReadStateAsync(focused.SessionKey, token).ConfigureAwait(false);
            _baseline = state.IsOk ? state.Value!.Value : _baseline;
        }

        _secretTyped = false;
        _secretRecorded = true;
    }
}
