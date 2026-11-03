using System.Globalization;

namespace Recorder.Core.Persistence;

/// <summary>A recording an earlier session kept but never saved: it crashed, or was closed without saving.</summary>
/// <param name="File">The kept recording, a normal *.wfrec.json that Open can read too.</param>
/// <param name="SourcePath">The file the recording was opened from or last saved to; null if it never had one.</param>
public sealed record AutosaveEntry(string File, string? SourcePath, DateTimeOffset SavedAt);

/// <summary>
/// Spec 12: the recording is kept in %LOCALAPPDATA%\WinFormsRecorder\autosave\ as the user works (the app waits for a pause),
/// so a crash or a close without saving loses nothing. Each session keeps its own file, next to a lock file that Windows
/// deletes when the session ends, however it ends: a recording without a live lock was left by an earlier session.
/// </summary>
public sealed class AutosaveService : IDisposable
{
    private const string LockExtension = ".lock";
    private const string SourceExtension = ".source";
    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);

    private readonly string _folder;
    private readonly string _session;
    private readonly string _file;
    private readonly TimeProvider _time;
    private FileStream? _lock;

    public AutosaveService(AppPaths paths, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(time);
        (_folder, _time) = (paths.AutosaveDirectory, time);
        var name = time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];
        _session = Path.Combine(_folder, name);
        _file = _session + RecordingStore.Extension;
    }

    /// <summary>
    /// The newest recording an earlier session left unsaved, or null. On the way, recordings whose file was saved later (the
    /// work was kept after all) and recordings older than 30 days are deleted.
    /// </summary>
    public AutosaveEntry? FindLeftover()
    {
        if (!Directory.Exists(_folder))
        {
            return null;
        }

        AutosaveEntry? newest = null;
        foreach (var file in Directory.EnumerateFiles(_folder, "*" + RecordingStore.Extension))
        {
            if (string.Equals(file, _file, StringComparison.OrdinalIgnoreCase) || IsLive(SessionOf(file)) || Read(file) is not { } entry)
            {
                continue;
            }

            if (IsStale(entry))
            {
                Delete(entry);
            }
            else if (newest is null || entry.SavedAt > newest.SavedAt)
            {
                newest = entry;
            }
        }

        return newest;
    }

    /// <exception cref="RecordingFileException">The file is damaged or from a newer version.</exception>
    /// <exception cref="IOException">The file can't be read.</exception>
    public static RecordingDocument Load(AutosaveEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return RecordingStore.Load(entry.File);
    }

    /// <summary>Forgets a recording an earlier session left: it was restored, or the user doesn't want it.</summary>
    public static void Delete(AutosaveEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var session = SessionOf(entry.File);
        TryDelete(entry.File);
        TryDelete(session + SourceExtension);
        TryDelete(session + LockExtension);
    }

    /// <summary>Keeps the recording as it is now, in place of what this session kept before.</summary>
    /// <param name="sourcePath">The file it was opened from or last saved to, so that a restored recording saves there again.</param>
    public void Save(RecordingDocument document, string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        Directory.CreateDirectory(_folder);
        _lock ??= new FileStream(_session + LockExtension, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        RecordingStore.Save(_file, document);
        if (sourcePath is null)
        {
            TryDelete(_session + SourceExtension);
        }
        else
        {
            File.WriteAllText(_session + SourceExtension, sourcePath);
        }
    }

    /// <summary>Nothing is unsaved any more (the recording was saved, opened or emptied): this session's file goes.</summary>
    public void Clear()
    {
        TryDelete(_file);
        TryDelete(_session + SourceExtension);
        _lock?.Dispose();
        _lock = null;
    }

    public void Dispose()
    {
        _lock?.Dispose();
        _lock = null;
    }

    private static string SessionOf(string file) => file[..^RecordingStore.Extension.Length];

    // A session that is still running holds its lock file open, so the file can't be deleted. Without a running session the
    // lock is gone already (or left behind by a file system that ignores delete-on-close, and deleted here).
    private static bool IsLive(string session)
    {
        try
        {
            File.Delete(session + LockExtension);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    // Null for a file that can't be read now; it is tried again at the next start.
    private static AutosaveEntry? Read(string file)
    {
        try
        {
            var source = SessionOf(file) + SourceExtension;
            var sourcePath = File.Exists(source) ? File.ReadAllText(source).Trim() : null;
            return new AutosaveEntry(file, string.IsNullOrEmpty(sourcePath) ? null : sourcePath, File.GetLastWriteTimeUtc(file));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Spec 12: offered only when it is newer than the last explicit save of its file.
    private bool IsStale(AutosaveEntry entry) =>
        entry.SavedAt < _time.GetUtcNow() - KeepFor
        || (entry.SourcePath is { } source && File.Exists(source) && File.GetLastWriteTimeUtc(source) >= entry.SavedAt.UtcDateTime);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Left for the next start to try again.
        }
        catch (UnauthorizedAccessException)
        {
            // Likewise.
        }
    }
}
