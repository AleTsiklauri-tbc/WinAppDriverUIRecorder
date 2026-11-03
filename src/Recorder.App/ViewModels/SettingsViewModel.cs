using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Resources;
using Recorder.App.Services;
using Recorder.Core.CodeGen;
using Recorder.Core.Model;
using Recorder.Core.Persistence;

namespace Recorder.App.ViewModels;

/// <summary>
/// The Settings window (spec 12): General, Shortcuts, Recording, Code generation, WinAppDriver and Advanced. Every change is
/// saved and applied at once; there is nothing to confirm. UI thread only.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _settings;
    private readonly CodeProfiles _profiles;
    private readonly WinAppDriverService _winAppDriver;
    private readonly AppPaths _paths;

    public SettingsViewModel(SettingsStore settings, Shortcuts shortcuts, CodeProfiles profiles, WinAppDriverService winAppDriver, AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(winAppDriver);
        (_settings, _profiles, _winAppDriver, _paths) = (settings, profiles, winAppDriver, paths);
        ShortcutRows = [.. Enum.GetValues<ShortcutId>().Select(id => new ShortcutRowViewModel(id, shortcuts))];
        Address = winAppDriver.Address;
        UiaTimeoutText = (settings.Current.UiaTimeoutMilliseconds / 1000.0).ToString("0.#", CultureInfo.CurrentCulture);
        profiles.Changed += OnProfilesChanged;
    }

    [ObservableProperty]
    public partial SettingsSection Section { get; set; }

    // General

    public bool MinimizeOnRecord
    {
        get => _settings.Current.MinimizeOnRecord;
        set => Change(settings => settings with { MinimizeOnRecord = value });
    }

    public bool CodePaneRight
    {
        get => _settings.Current.CodePanePosition == CodePanePosition.Right;
        set => Choose(value, settings => settings with { CodePanePosition = CodePanePosition.Right }, nameof(CodePaneBottom));
    }

    public bool CodePaneBottom
    {
        get => _settings.Current.CodePanePosition == CodePanePosition.Bottom;
        set => Choose(value, settings => settings with { CodePanePosition = CodePanePosition.Bottom }, nameof(CodePaneRight));
    }

    public bool ThemeSystem
    {
        get => _settings.Current.Theme == ThemeChoice.System;
        set => Choose(value, settings => settings with { Theme = ThemeChoice.System }, nameof(ThemeLight), nameof(ThemeDark));
    }

    public bool ThemeLight
    {
        get => _settings.Current.Theme == ThemeChoice.Light;
        set => Choose(value, settings => settings with { Theme = ThemeChoice.Light }, nameof(ThemeSystem), nameof(ThemeDark));
    }

    public bool ThemeDark
    {
        get => _settings.Current.Theme == ThemeChoice.Dark;
        set => Choose(value, settings => settings with { Theme = ThemeChoice.Dark }, nameof(ThemeSystem), nameof(ThemeLight));
    }

    // Shortcuts

    public IReadOnlyList<ShortcutRowViewModel> ShortcutRows { get; }

    // Recording

    public bool RecordSpecialKeys
    {
        get => _settings.Current.RecordSpecialKeys;
        set => Change(settings => settings with { RecordSpecialKeys = value });
    }

    public bool MaskTypedValues
    {
        get => _settings.Current.MaskTypedValues;
        set => Change(settings => settings with { MaskTypedValues = value });
    }

    public bool RecordScrolls
    {
        get => _settings.Current.RecordScrolls;
        set => Change(settings => settings with { RecordScrolls = value });
    }

    // Code generation

    public IReadOnlyList<CodeProfile> Profiles => _profiles.Set.Profiles;

    public CodeProfile SelectedProfile
    {
        get => _profiles.Active;
        set
        {
            if (value is not null)
            {
                _profiles.Select(value.Id);
            }
        }
    }

    // WinAppDriver

    /// <summary>As typed; saved on Enter or when the box is left (<see cref="CommitAddressCommand"/>).</summary>
    [ObservableProperty]
    public partial string Address { get; set; }

    [ObservableProperty]
    public partial string? AddressProblem { get; private set; }

    public bool AllowRemote
    {
        get => _settings.Current.AllowRemoteWinAppDriver;
        set
        {
            Change(settings => settings with { AllowRemoteWinAppDriver = value });
            CommitAddress();
        }
    }

    // Advanced

    public bool DetailedLogs
    {
        get => _settings.Current.DetailedLogs;
        set => Change(settings => settings with { DetailedLogs = value });
    }

    [ObservableProperty]
    public partial string UiaTimeoutText { get; set; }

    [ObservableProperty]
    public partial string? UiaTimeoutProblem { get; private set; }

    /// <summary>The window closed. A view model is made for each opening, so it stops following the profiles.</summary>
    public void Detach() => _profiles.Changed -= OnProfilesChanged;

    [RelayCommand]
    private void CommitAddress() => AddressProblem = _winAppDriver.TrySetAddress(Address);

    [RelayCommand]
    private void CommitUiaTimeout()
    {
        if (double.TryParse(UiaTimeoutText, NumberStyles.Float, CultureInfo.CurrentCulture, out var seconds) && seconds is >= 1 and <= 10)
        {
            UiaTimeoutProblem = null;
            Change(settings => settings with { UiaTimeoutMilliseconds = (int)Math.Round(seconds * 1000) });
        }
        else
        {
            UiaTimeoutProblem = Strings.Settings_UiaTimeout_Invalid;
        }
    }

    [RelayCommand]
    private void OpenProfilesFolder() => _profiles.OpenFolder();

    [RelayCommand]
    private void DuplicateProfile() => _profiles.Duplicate();

    [RelayCommand]
    private void OpenLogsFolder() => Explorer.Open(_paths.LogsDirectory);

    private void OnProfilesChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Profiles));
        OnPropertyChanged(nameof(SelectedProfile));
    }

    private void Change(Func<AppSettings, AppSettings> change, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        _settings.Update(change);
        OnPropertyChanged(property);
    }

    // One of a group of radio buttons: only its selection is a change.
    private void Choose(bool selected, Func<AppSettings, AppSettings> change, params string[] others)
    {
        if (!selected)
        {
            return;
        }

        _settings.Update(change);
        foreach (var other in others)
        {
            OnPropertyChanged(other);
        }
    }
}

/// <summary>One changeable global shortcut in Settings: its keys, why new keys were refused, and its default.</summary>
public sealed partial class ShortcutRowViewModel : ObservableObject
{
    private readonly Shortcuts _shortcuts;

    public ShortcutRowViewModel(ShortcutId id, Shortcuts shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        (Id, _shortcuts) = (id, shortcuts);
        Keys = shortcuts.Gesture(id).ToString();
    }

    public ShortcutId Id { get; }

    public string Name => Shortcuts.Name(Id);

    public string AutomationId => "Settings.Shortcut." + Id;

    public string ResetAutomationId => AutomationId + ".Reset";

    public string ResetTip => Text.Format(Strings.Settings_Shortcut_Reset_Tip, ShortcutRules.Defaults[Id]);

    [ObservableProperty]
    public partial string Keys { get; private set; }

    /// <summary>Why the keys last pressed were not taken; null when they were.</summary>
    [ObservableProperty]
    public partial string? Problem { get; private set; }

    /// <summary>Keys pressed in the shortcut box; null for a key no shortcut can use.</summary>
    public void Press(HotkeyGesture? gesture)
    {
        Problem = gesture is null ? Strings.Shortcut_KeyNotSupported : _shortcuts.Set(Id, gesture);
        Keys = _shortcuts.Gesture(Id).ToString();
    }

    [RelayCommand]
    private void Reset() => Press(ShortcutRules.Defaults[Id]);
}
