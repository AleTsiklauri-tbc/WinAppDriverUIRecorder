using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Resources;
using Recorder.App.Services;
using Recorder.Core.Model;

namespace Recorder.App.ViewModels;

/// <summary>One shortcut in the cheat sheet.</summary>
public sealed record CheatSheetRow(string Keys, string Action);

/// <summary>
/// The cheat sheet (spec 11.9): every shortcut, the global ones with the keys they have now. Opened with ? or F1, closed
/// with Esc. UI thread only.
/// </summary>
public sealed partial class CheatSheetViewModel : ObservableObject
{
    private readonly Shortcuts _shortcuts;
    private readonly SettingsDialog _settings;

    public CheatSheetViewModel(Shortcuts shortcuts, SettingsDialog settings)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        (_shortcuts, _settings) = (shortcuts, settings);
        shortcuts.Changed += (_, _) => OnPropertyChanged(nameof(Global));
    }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>Anywhere, while the recorder runs (registered with Windows), in the order of spec 11.9.</summary>
    public IReadOnlyList<CheatSheetRow> Global =>
    [
        .. new[] { ShortcutId.Record, ShortcutId.Pause, ShortcutId.Capture, ShortcutId.Assert }
            .Select(id => new CheatSheetRow(_shortcuts.Gesture(id).ToString(), Shortcuts.Name(id))),
        new("Esc", Strings.CheatSheet_CancelMode),
    ];

    /// <summary>In the recorder's own window.</summary>
    public IReadOnlyList<CheatSheetRow> App { get; } =
    [
        new("Ctrl+C", Strings.CheatSheet_CopyLine),
        new("Ctrl+Shift+C", Strings.CheatSheet_CopyClass),
        new("Ctrl+Z · Ctrl+Y", Strings.CheatSheet_UndoRedo),
        new("Del · F2 · Ctrl+D", Strings.CheatSheet_StepKeys),
        new("Alt+↑ · Alt+↓", Strings.CheatSheet_MoveStep),
        new("Enter", Strings.CheatSheet_EditStep),
        new("Ctrl+S · Ctrl+Shift+S · Ctrl+O", Strings.CheatSheet_Files),
        new("Ctrl+E", Strings.CheatSheet_Export),
        new("Ctrl+F", Strings.CheatSheet_Filter),
        new("F1", Strings.CheatSheet_Help),
    ];

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void ChangeShortcuts()
    {
        IsOpen = false;
        _settings.Show(SettingsSection.Shortcuts);
    }
}
