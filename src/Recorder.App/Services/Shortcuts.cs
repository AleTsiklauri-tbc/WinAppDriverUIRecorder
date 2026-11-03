using System.ComponentModel;
using Recorder.App.Resources;
using Recorder.Core.Model;
using Recorder.Core.Persistence;

namespace Recorder.App.Services;

/// <summary>
/// The global shortcuts (spec 11.9: all configurable). Holds each one's keys, changes one after checking it (and saves it),
/// and puts the current keys into UI text: Strings.resx writes {capture}, {record}, {pause} and {assert} where a shortcut
/// goes, and XAML reads such a text through the indexer, <c>{Binding [FirstRun_Step2], Source={StaticResource Shortcuts}}</c>.
/// UI thread only.
/// </summary>
public sealed class Shortcuts : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs IndexerChanged = new("Item[]");
    private readonly SettingsStore _settings;
    private readonly HotkeyService _hotkeys;
    private readonly BannerService _banners;
    private readonly SettingsDialog _dialog;
    private readonly Dictionary<ShortcutId, HotkeyGesture> _gestures = [];

    public Shortcuts(SettingsStore settings, HotkeyService hotkeys, BannerService banners, SettingsDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(settings);
        (_settings, _hotkeys, _banners, _dialog) = (settings, hotkeys, banners, dialog);
        var current = settings.Current;
        _gestures[ShortcutId.Capture] = Read(current.CaptureHotkey, ShortcutId.Capture);
        _gestures[ShortcutId.Record] = Read(current.RecordHotkey, ShortcutId.Record);
        _gestures[ShortcutId.Pause] = Read(current.PauseHotkey, ShortcutId.Pause);
        _gestures[ShortcutId.Assert] = Read(current.AssertHotkey, ShortcutId.Assert);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>A shortcut has new keys: whoever registered it registers it again.</summary>
    public event EventHandler<ShortcutId>? Changed;

    public IReadOnlyDictionary<ShortcutId, HotkeyGesture> Current => _gestures;

    /// <summary>A Strings.resx text with the current shortcuts in it.</summary>
    public string this[string key] => Format(Strings.ResourceManager.GetString(key, Strings.Culture) ?? key);

    public HotkeyGesture Gesture(ShortcutId id) => _gestures[id];

    /// <summary>Puts the current shortcuts into a text that names them as {capture}, {record}, {pause} or {assert}.</summary>
    public string Format(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("{capture}", _gestures[ShortcutId.Capture].ToString(), StringComparison.Ordinal)
            .Replace("{record}", _gestures[ShortcutId.Record].ToString(), StringComparison.Ordinal)
            .Replace("{pause}", _gestures[ShortcutId.Pause].ToString(), StringComparison.Ordinal)
            .Replace("{assert}", _gestures[ShortcutId.Assert].ToString(), StringComparison.Ordinal);
    }

    /// <summary>What a shortcut does, for messages and the cheat sheet.</summary>
    public static string Name(ShortcutId id) => id switch
    {
        ShortcutId.Capture => Strings.Shortcut_Name_Capture,
        ShortcutId.Record => Strings.Shortcut_Name_Record,
        ShortcutId.Pause => Strings.Shortcut_Name_Pause,
        _ => Strings.Shortcut_Name_Assert,
    };

    /// <summary>Gives a shortcut new keys, unless they can't be used.</summary>
    /// <returns>Why the keys can't be used, or null when they were set (and saved).</returns>
    public string? Set(ShortcutId id, HotkeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        if (ShortcutRules.Same(gesture, _gestures[id]))
        {
            return null;
        }

        var (problem, other) = ShortcutRules.Check(id, gesture, _gestures);
        var reason = problem switch
        {
            ShortcutProblem.NeedsModifier => Strings.Shortcut_NeedsModifier,
            ShortcutProblem.Reserved => Text.Format(Strings.Shortcut_Reserved, gesture),
            ShortcutProblem.SameAs => Text.Format(Strings.Shortcut_SameAs, gesture, Name(other!.Value)),
            _ => _hotkeys.IsFree(gesture) ? null : Text.Format(Strings.Shortcut_Taken, gesture),
        };
        if (reason is not null)
        {
            return reason;
        }

        _gestures[id] = gesture;
        var text = gesture.ToString();
        _settings.Update(settings => id switch
        {
            ShortcutId.Capture => settings with { CaptureHotkey = text },
            ShortcutId.Record => settings with { RecordHotkey = text },
            ShortcutId.Pause => settings with { PauseHotkey = text },
            _ => settings with { AssertHotkey = text },
        });
        PropertyChanged?.Invoke(this, IndexerChanged);
        Changed?.Invoke(this, id);
        return null;
    }

    /// <summary>
    /// Registers a shortcut with Windows. When another app holds it, a banner says what to use instead and offers to choose
    /// another one (D-23); the banner goes once the shortcut works.
    /// </summary>
    public bool Register(ShortcutId id)
    {
        var gesture = _gestures[id];
        if (_hotkeys.Register(HotkeyIdOf(id), gesture))
        {
            _banners.Remove(BannerId(id));
            return true;
        }

        var message = id switch
        {
            ShortcutId.Capture => Strings.Banner_CaptureShortcutTaken,
            ShortcutId.Record => Strings.Banner_RecordShortcutTaken,
            ShortcutId.Pause => Strings.Banner_PauseShortcutTaken,
            _ => Strings.Banner_AssertShortcutTaken,
        };
        _banners.Show(BannerId(id), Text.Format(message, gesture), isWarning: true, Strings.Banner_ChooseShortcut, () => _dialog.Show(SettingsSection.Shortcuts));
        return false;
    }

    public void Unregister(ShortcutId id)
    {
        _hotkeys.Unregister(HotkeyIdOf(id));
        _banners.Remove(BannerId(id));
    }

    private static HotkeyId HotkeyIdOf(ShortcutId id) => id switch
    {
        ShortcutId.Capture => HotkeyId.Capture,
        ShortcutId.Record => HotkeyId.Record,
        ShortcutId.Pause => HotkeyId.Pause,
        _ => HotkeyId.Assert,
    };

    private static string BannerId(ShortcutId id) => "shortcut-" + id;

    // A shortcut settings.json spells wrongly falls back to its default.
    private static HotkeyGesture Read(string text, ShortcutId id) =>
        HotkeyGesture.TryParse(text, out var gesture) ? gesture : ShortcutRules.Defaults[id];
}
