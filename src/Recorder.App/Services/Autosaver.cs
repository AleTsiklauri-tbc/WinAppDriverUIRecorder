using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using Recorder.App.Resources;
using Recorder.Core.Model;
using Recorder.Core.Persistence;

namespace Recorder.App.Services;

/// <summary>
/// Spec 12, never lose work: the recording is kept 2 s after the last change, so a crash or a close without saving loses
/// nothing, and at start a recording an earlier session left unsaved is offered back ("Restore your unsaved recording from
/// 14:32?"). UI thread only.
/// </summary>
public sealed partial class Autosaver
{
    private const string BannerId = "autosave";
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(2);

    private readonly AutosaveService _store;
    private readonly RecordingFiles _files;
    private readonly ElementCatalog _catalog;
    private readonly StepList _steps;
    private readonly RecordingCoordinator _recording;
    private readonly BannerService _banners;
    private readonly ToastService _toasts;
    private readonly ILogger<Autosaver> _logger;
    private RecordingChangeWatcher? _watcher;
    private bool _failing;

    public Autosaver(
        AutosaveService store, RecordingFiles files, ElementCatalog catalog, StepList steps, RecordingCoordinator recording, BannerService banners,
        ToastService toasts, ILogger<Autosaver> logger)
    {
        ArgumentNullException.ThrowIfNull(files);
        (_store, _files, _catalog, _steps, _recording, _banners, _toasts, _logger) = (store, files, catalog, steps, recording, banners, toasts, logger);
        files.SavedOrOpened += (_, _) =>
        {
            _watcher?.Cancel();
            _store.Clear();
        };
    }

    /// <summary>At start, once the main window shows: keeps changes from now on and offers what an earlier session left.</summary>
    public void Start()
    {
        _watcher ??= new RecordingChangeWatcher(_catalog, _steps, _files, Delay, Keep);
        try
        {
            if (_store.FindLeftover() is { } leftover)
            {
                Offer(leftover);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFailed(_logger, "look for", exception.GetType().Name, exception.HResult);
        }
    }

    /// <summary>The app is closing: the last changes are kept now, or nothing is kept when nothing is unsaved.</summary>
    public void Flush()
    {
        _watcher?.Cancel();
        Keep();
    }

    private void Keep()
    {
        if (!_files.IsDirty || (_catalog.Count == 0 && _steps.Count == 0))
        {
            _store.Clear();
            return;
        }

        try
        {
            _store.Save(_files.Document(), _files.Path);
            _failing = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Once per run of failures: autosave tries again at the next change.
            if (!_failing)
            {
                _failing = true;
                LogFailed(_logger, "keep", exception.GetType().Name, exception.HResult);
            }
        }
    }

    private void Offer(AutosaveEntry leftover)
    {
        var savedAt = leftover.SavedAt.ToLocalTime();
        var when = savedAt.ToString(savedAt.Date == DateTime.Today ? "t" : "g", CultureInfo.CurrentCulture);
        _banners.Show(
            BannerId, Text.Format(Strings.Banner_Restore, when), isWarning: false, Strings.Banner_Restore_Action, () => Restore(leftover, when),
            dismissed: () => AutosaveService.Delete(leftover));
    }

    private void Restore(AutosaveEntry leftover, string when)
    {
        if (_recording.IsActive)
        {
            _toasts.Show(Strings.Toast_RestoreWhileRecording);
            return;
        }

        if (Application.Current?.MainWindow is not { } owner)
        {
            return;
        }

        try
        {
            if (_files.Restore(owner, AutosaveService.Load(leftover), leftover.SourcePath))
            {
                _banners.Remove(BannerId);
                AutosaveService.Delete(leftover);
                _toasts.Show(Text.Format(Strings.Toast_Restored, when));
            }
        }
        catch (RecordingFileException exception)
        {
            _toasts.Show(Text.Format(Strings.Error_RestoreFailed, exception.Problem == RecordingFileProblem.NewerVersion ? Strings.Error_NewerRecording : Strings.Error_AutosaveDamaged));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFailed(_logger, "restore", exception.GetType().Name, exception.HResult);
            _toasts.Show(Text.Format(Strings.Error_RestoreFailed, exception.Message));
        }
    }

    // Error types only: paths can hold customer names (AGENTS.md).
    [LoggerMessage(EventId = 920, Level = LogLevel.Warning, Message = "Autosave could not {Operation} a recording: {ErrorType} (HRESULT 0x{HResult:X8})")]
    private static partial void LogFailed(ILogger logger, string operation, string errorType, int hresult);
}
