using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Resources;
using Recorder.App.Services;
using Recorder.Core.CodeGen;
using Recorder.Core.Model;
using Recorder.Core.Naming;
using Recorder.Core.Recording;

namespace Recorder.App.ViewModels;

public enum CodeTab
{
    Pages,
    Steps,
    Test,
    Element,
}

/// <summary>
/// The code pane (spec 10.4): page objects, Steps classes and the test, generated with the active profile 150 ms after the
/// elements, steps, names or chosen locators change, and the selected element's line. A template error is shown instead of
/// the code (spec 10.1). Page class names are edited here (spec 10.2), undoably. UI thread only.
/// </summary>
public sealed partial class CodeViewModel : ObservableObject
{
    private readonly CodeProfiles _profiles;
    private readonly ElementCatalog _catalog;
    private readonly StepList _steps;
    private readonly RecordingFiles _recordingFiles;
    private readonly LocatorsViewModel _locators;
    private readonly CodeFiles _files;
    private readonly IClipboardService _clipboard;
    private readonly ToastService _toasts;
    private readonly Shortcuts _shortcuts;
    private readonly EditHistory _history;
    private readonly RecordingViewModel _recording;
    private readonly RecordingChangeWatcher _changes;
    private GeneratedCode _code = GeneratedCode.Empty;
    private string? _followClass;
    private bool _syncing;
    private bool _isShown = true;
    private bool _stale;

    public CodeViewModel(
        CodeProfiles profiles, ElementCatalog catalog, StepList steps, RecordingFiles recordingFiles, LocatorsViewModel locators, CodeFiles files,
        IClipboardService clipboard, ToastService toasts, EditHistory history, RecordingViewModel recording, Shortcuts shortcuts)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(locators);
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(shortcuts);
        (_profiles, _catalog, _steps, _recordingFiles, _locators, _files) = (profiles, catalog, steps, recordingFiles, locators, files);
        (_clipboard, _toasts, _history, _recording, _shortcuts) = (clipboard, toasts, history, recording, shortcuts);
        shortcuts.Changed += (_, _) => ShowFile();
        _changes = new RecordingChangeWatcher(catalog, steps, recordingFiles, TimeSpan.FromMilliseconds(150), Generate);
        profiles.Changed += (_, _) => OnProfilesChanged();
        locators.PropertyChanged += OnLocatorsChanged;
        recording.PropertyChanged += (_, _) => NotifyCommands();
        OnProfilesChanged();
    }

    public IReadOnlyList<CodeProfile> Profiles => _profiles.Set.Profiles;

    /// <summary>Bound two-way to the profile picker; choosing one regenerates everything with it.</summary>
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

    public string Highlighting => _profiles.Active.Settings.Highlighting;

    public bool HasStepsLayer => _profiles.Active.Settings.HasStepsLayer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPagesTab), nameof(HasFilePicker))]
    public partial CodeTab SelectedTab { get; set; }

    /// <summary>Class names of the tab's files (Pages, Steps).</summary>
    public ObservableCollection<string> FileNames { get; } = [];

    [ObservableProperty]
    public partial string? SelectedFileName { get; set; }

    public bool HasFilePicker => SelectedTab is CodeTab.Pages or CodeTab.Steps && FileNames.Count > 1;

    public bool IsPagesTab => SelectedTab == CodeTab.Pages;

    [ObservableProperty]
    public partial string Code { get; private set; } = string.Empty;

    /// <summary>A template error, e.g. "page.sbn line 12: …" (spec 10.1).</summary>
    [ObservableProperty]
    public partial string? Error { get; private set; }

    /// <summary>What to do to see code here, when there is none.</summary>
    [ObservableProperty]
    public partial string? Placeholder { get; private set; }

    /// <summary>The page class shown, editable on the Pages tab.</summary>
    [ObservableProperty]
    public partial string? EditableClassName { get; set; }

    private GeneratedFile? Current =>
        SelectedTab is CodeTab.Element ? null : _code.Of(Kind(SelectedTab)).FirstOrDefault(file => file.ClassName == SelectedFileName);

    partial void OnSelectedTabChanged(CodeTab value) => Show();

    partial void OnSelectedFileNameChanged(string? value)
    {
        if (!_syncing)
        {
            ShowFile();
        }
    }

    /// <summary>Ctrl+Shift+C: the class shown, or the element's line on the Element tab (spec 11.9).</summary>
    [RelayCommand(CanExecute = nameof(HasCode))]
    private void Copy() => _toasts.Show(_clipboard.TrySetText(Code) ? Strings.Toast_Copied : Strings.Error_ClipboardBusy);

    [RelayCommand(CanExecute = nameof(CanWriteCurrent))]
    private void SaveAs()
    {
        if (Owner() is { } owner && Current is { } file)
        {
            _files.SaveAs(owner, file, _profiles.Active);
        }
    }

    [RelayCommand(CanExecute = nameof(CanWriteCurrent))]
    private void Merge()
    {
        if (Owner() is { } owner && Current is { } file)
        {
            _files.Merge(owner, file, _profiles.Active);
        }
    }

    /// <summary>Ctrl+E: every class into a folder.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Export()
    {
        if (Owner() is { } owner)
        {
            _files.Export(owner, [.. _code.Files]);
        }
    }

    [RelayCommand]
    private void EditProfile() => _profiles.OpenEditableCopy();

    /// <summary>Renames the page shown (Enter, or leaving the box), undoably (spec 11.6).</summary>
    [RelayCommand]
    private void CommitClassName()
    {
        if (Current is not { Kind: CodeFileKind.Page, Page: { } key } file || _catalog.Pages.FirstOrDefault(p => p.Key == key) is not { } page)
        {
            return;
        }

        var typed = EditableClassName?.Trim();
        var name = VariableNamer.ClassName(typed);
        if (name is null || name == file.ClassName)
        {
            if (!string.IsNullOrEmpty(typed) && name is null)
            {
                _toasts.Show(Strings.Code_ClassName_Invalid);
            }

            EditableClassName = file.ClassName;
            return;
        }

        var old = page.ClassName;
        _followClass = name;
        _history.Execute(() => _catalog.RenamePage(page, name), () => _catalog.RenamePage(page, old));
    }

    private static CodeFileKind Kind(CodeTab tab) => tab switch
    {
        CodeTab.Pages => CodeFileKind.Page,
        CodeTab.Steps => CodeFileKind.Steps,
        _ => CodeFileKind.Test,
    };

    private static Window? Owner() => Application.Current?.MainWindow;

    private bool HasCode() => Code.Length > 0 && Error is null;

    private bool CanWriteCurrent() => _recording.IsIdle && Current is { Error: null };

    private bool CanExport() => _recording.IsIdle && _code.Files.Count > 0 && _code.Files.All(file => file.Error is null);

    private void OnLocatorsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (SelectedTab == CodeTab.Element && e.PropertyName is nameof(LocatorsViewModel.JavaLine) or nameof(LocatorsViewModel.CodeLineError))
        {
            Show();
        }
    }

    private void OnProfilesChanged()
    {
        OnPropertyChanged(nameof(Profiles));
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(Highlighting));
        OnPropertyChanged(nameof(HasStepsLayer));
        if (!HasStepsLayer && SelectedTab == CodeTab.Steps)
        {
            SelectedTab = CodeTab.Pages;
        }

        Generate();
    }

    /// <summary>
    /// False while the main window is minimized, as it is while recording: the code is generated once it shows again instead of
    /// after every recorded step.
    /// </summary>
    public bool IsShown
    {
        get => _isShown;
        set
        {
            _isShown = value;
            if (value && _stale)
            {
                Generate();
            }
        }
    }

    // The test is named after the recording's file ("Loan flow.wfrec.json" → LoanFlowTest).
    private void Generate()
    {
        _changes.Cancel();
        _stale = !_isShown;
        if (_stale)
        {
            return;
        }

        _code = _profiles.Generator.Generate(_catalog, _steps.Items, _recordingFiles.Name ?? Strings.Code_DefaultTestName);
        Show();
    }

    // Lists the tab's classes, keeping the one shown (or the one just renamed), then shows it.
    private void Show()
    {
        List<string> names = SelectedTab == CodeTab.Element ? [] : [.. _code.Of(Kind(SelectedTab)).Select(file => file.ClassName)];
        var keep = _followClass is { } followed && names.Contains(followed) ? followed : SelectedFileName;
        _syncing = true;
        if (!FileNames.SequenceEqual(names))
        {
            FileNames.Clear();
            names.ForEach(FileNames.Add);
        }

        SelectedFileName = keep is not null && names.Contains(keep) ? keep : names.FirstOrDefault();
        _syncing = false;
        if (_followClass is not null && names.Contains(_followClass))
        {
            _followClass = null;
        }

        OnPropertyChanged(nameof(HasFilePicker));
        ShowFile();
    }

    private void ShowFile()
    {
        if (SelectedTab == CodeTab.Element)
        {
            (Code, Error) = (_locators.JavaLine ?? string.Empty, _locators.CodeLineError);
            Placeholder = Code.Length == 0 && Error is null ? Strings.Code_Element_Empty : null;
        }
        else
        {
            var file = Current;
            (Code, Error) = (file?.Text ?? string.Empty, _code.Error ?? file?.Error);
            Placeholder = file is not null || Error is not null ? null : SelectedTab switch
            {
                CodeTab.Pages => _shortcuts.Format(Strings.Code_Pages_Empty),
                CodeTab.Steps => _shortcuts.Format(Strings.Code_Steps_Empty),
                _ => _shortcuts.Format(Strings.Code_Test_Empty),
            };
            EditableClassName = file?.ClassName;
        }

        NotifyCommands();
    }

    private void NotifyCommands()
    {
        CopyCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        MergeCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }
}
