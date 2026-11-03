using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Recorder.App.Resources;
using Recorder.Core.Automation;
using Recorder.Core.Model;
using Recorder.Core.Persistence;
using Recorder.Core.Recording;

namespace Recorder.App.Services;

/// <summary>
/// Save, Open and Open recent for *.wfrec.json recordings (spec 12, Ctrl+S / Ctrl+O), and putting back what autosave kept.
/// Never while recording: the file dialogs are modal (spec 11.1). UI thread only.
/// </summary>
public sealed partial class RecordingFiles
{
    private const int MaxRecent = 8;
    private readonly ElementCatalog _catalog;
    private readonly StepList _steps;
    private readonly EditHistory _history;
    private readonly TargetTracker _target;
    private readonly SettingsStore _settings;
    private readonly ToastService _toasts;
    private readonly TimeProvider _time;
    private readonly ILogger<RecordingFiles> _logger;
    private DateTimeOffset _createdAt;

    public RecordingFiles(
        ElementCatalog catalog, StepList steps, EditHistory history, TargetTracker target, SettingsStore settings, ToastService toasts, TimeProvider time,
        ILogger<RecordingFiles> logger)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(catalog);
        (_catalog, _steps, _history, _target, _settings, _toasts, _time, _logger) = (catalog, steps, history, target, settings, toasts, time, logger);
        _createdAt = time.GetLocalNow();
        steps.Changed += (_, _) => IsDirty = true;
        history.Changed += (_, _) => IsDirty = true;
        catalog.ElementAdded += (_, _) => IsDirty = true;
        catalog.PageRenamed += (_, _) => IsDirty = true;
    }

    /// <summary>The recording was saved under another name or another one was opened (<see cref="Path"/> changed).</summary>
    public event EventHandler? PathChanged;

    /// <summary>What is on screen is in a file now: it was just saved or opened.</summary>
    public event EventHandler? SavedOrOpened;

    /// <summary>The file the recording was last saved to or opened from.</summary>
    public string? Path { get; private set; }

    /// <summary>The recording's name: its file name without the extension ("Loan flow"); null until it is saved or opened.</summary>
    public string? Name => Path is { } path ? NameOf(path) : null;

    /// <summary>Something changed since the last save or open.</summary>
    public bool IsDirty { get; private set; }

    public bool Save(Window owner) => Path is { } path ? Write(path) : SaveAs(owner);

    public bool SaveAs(Window owner)
    {
        var dialog = new SaveFileDialog
        {
            Filter = Strings.File_Filter,
            DefaultExt = RecordingStore.Extension,
            AddExtension = true,
            FileName = Path is null ? Strings.File_DefaultName + RecordingStore.Extension : System.IO.Path.GetFileName(Path),
            InitialDirectory = Folder(),
        };
        return dialog.ShowDialog(owner) == true && Write(dialog.FileName);
    }

    public void Open(Window owner)
    {
        if (!MayDiscard(owner, Strings.Confirm_Discard))
        {
            return;
        }

        var dialog = new OpenFileDialog { Filter = Strings.File_Filter, InitialDirectory = Folder() };
        if (dialog.ShowDialog(owner) == true)
        {
            Read(dialog.FileName);
        }
    }

    /// <summary>Export ▾ Open recent (spec 12). A file that is gone leaves the list.</summary>
    public void OpenRecent(Window owner, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            Forget(path);
            _toasts.Show(Text.Format(Strings.Toast_RecentMissing, System.IO.Path.GetFileName(path)));
            return;
        }

        if (MayDiscard(owner, Strings.Confirm_Discard))
        {
            Read(path);
        }
    }

    /// <summary>Puts back a recording autosave kept (spec 12). It stays unsaved, and saves to the file it came from, if any.</summary>
    /// <returns>False when the user kept the unsaved recording on screen instead.</returns>
    public bool Restore(Window owner, RecordingDocument document, string? path)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!MayDiscard(owner, Strings.Confirm_DiscardForRestore))
        {
            return false;
        }

        Load(document, path);
        IsDirty = true;
        LogRestored(_logger, document.Steps.Count, document.Elements.Count);
        return true;
    }

    /// <summary>What is on screen, as it would be saved.</summary>
    public RecordingDocument Document() => RecordingDocument.From(_catalog, _steps, _target.State.ProcessName, _createdAt, _time.GetLocalNow());

    private bool Write(string path)
    {
        try
        {
            var document = Document();
            RecordingStore.Save(path, document);
            var renamed = path != Path;
            (Path, IsDirty) = (path, false);
            if (renamed)
            {
                PathChanged?.Invoke(this, EventArgs.Empty);
            }

            Remember(path);
            LogSaved(_logger, document.Steps.Count, document.Elements.Count);
            _toasts.Show(Text.Format(Strings.Toast_Saved, System.IO.Path.GetFileName(path)));
            SavedOrOpened?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFailed(_logger, "save", exception.GetType().Name, exception.HResult);
            _toasts.Show(Text.Format(Strings.Error_SaveFailed, exception.Message));
            return false;
        }
    }

    private void Read(string path)
    {
        try
        {
            var document = RecordingStore.Load(path);
            Load(document, path);
            IsDirty = false;
            Remember(path);
            LogOpened(_logger, document.Steps.Count, document.Elements.Count);
            _toasts.Show(Text.Format(Strings.Toast_Opened, System.IO.Path.GetFileName(path)));
            SavedOrOpened?.Invoke(this, EventArgs.Empty);
        }
        catch (RecordingFileException exception)
        {
            _toasts.Show(exception.Problem == RecordingFileProblem.NewerVersion ? Strings.Error_NewerRecording : Strings.Error_NotARecording);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFailed(_logger, "open", exception.GetType().Name, exception.HResult);
            _toasts.Show(Text.Format(Strings.Error_OpenFailed, exception.Message));
        }
    }

    private void Load(RecordingDocument document, string? path)
    {
        _catalog.Load(document.Elements, document.PageClassNames);
        _steps.Reset(document.Steps);
        _history.Clear();
        (Path, _createdAt) = (path, document.CreatedAt);
        PathChanged?.Invoke(this, EventArgs.Empty);
    }

    // Unsaved steps are only dropped when the user says so.
    private bool MayDiscard(Window owner, string question) =>
        !IsDirty || _steps.Count == 0
        || MessageBox.Show(owner, question, Strings.Confirm_Discard_Title, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;

    /// <summary>A recording file's name without the extension: "Loan flow" for "C:\Tests\Loan flow.wfrec.json".</summary>
    public static string NameOf(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        return name.EndsWith(RecordingStore.Extension, StringComparison.OrdinalIgnoreCase)
            ? name[..^RecordingStore.Extension.Length]
            : System.IO.Path.GetFileNameWithoutExtension(name);
    }

    private string Folder() =>
        _settings.Current.LastRecordingFolder is { } folder && Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    // The folder for the next dialog, and the top of Open recent.
    private void Remember(string path) =>
        _settings.Update(settings => settings with
        {
            LastRecordingFolder = System.IO.Path.GetDirectoryName(path),
            RecentRecordings = [path, .. settings.RecentRecordings.Where(p => !SamePath(p, path)).Take(MaxRecent - 1)],
        });

    private void Forget(string path) =>
        _settings.Update(settings => settings with { RecentRecordings = [.. settings.RecentRecordings.Where(p => !SamePath(p, path))] });

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // Counts and error types only: file names and paths (and the messages that quote them) can hold customer names.
    [LoggerMessage(EventId = 640, Level = LogLevel.Information, Message = "Recording saved ({Steps} steps, {Elements} elements)")]
    private static partial void LogSaved(ILogger logger, int steps, int elements);

    [LoggerMessage(EventId = 641, Level = LogLevel.Information, Message = "Recording opened ({Steps} steps, {Elements} elements)")]
    private static partial void LogOpened(ILogger logger, int steps, int elements);

    [LoggerMessage(EventId = 642, Level = LogLevel.Warning, Message = "Could not {Operation} a recording: {ErrorType} (HRESULT 0x{HResult:X8})")]
    private static partial void LogFailed(ILogger logger, string operation, string errorType, int hresult);

    [LoggerMessage(EventId = 643, Level = LogLevel.Information, Message = "Recording restored from autosave ({Steps} steps, {Elements} elements)")]
    private static partial void LogRestored(ILogger logger, int steps, int elements);
}
