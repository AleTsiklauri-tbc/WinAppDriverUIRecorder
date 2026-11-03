using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Recorder.App.Resources;
using Recorder.Core.CodeGen;
using Recorder.Core.Locators;
using Recorder.Core.Model;
using Recorder.Core.Persistence;

namespace Recorder.App.Services;

/// <summary>
/// The code profiles (spec 10.1): built in, plus the user's folders under %APPDATA%\WinFormsRecorder\profiles, read again
/// when a file there changes (hot reload). The active one is kept in settings and names newly captured elements.
/// UI thread only; <see cref="Changed"/> is raised on it.
/// </summary>
public sealed partial class CodeProfiles : IDisposable
{
    private readonly AppPaths _paths;
    private readonly SettingsStore _settings;
    private readonly ElementCatalog _catalog;
    private readonly BannerService _banners;
    private readonly ILogger<CodeProfiles> _logger;
    private readonly DispatcherTimer _reload;
    private FileSystemWatcher? _watcher;

    public CodeProfiles(AppPaths paths, SettingsStore settings, ElementCatalog catalog, BannerService banners, ILogger<CodeProfiles> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalog);
        (_paths, _settings, _catalog, _banners, _logger) = (paths, settings, catalog, banners, logger);

        // An editor saves a file in several writes: read once things are quiet.
        _reload = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => Reload(), Dispatcher.CurrentDispatcher);
        _reload.Stop();
        Set = ProfileLoader.Load(paths.ProfilesDirectory);
        (Active, Generator) = Activate(Set.Find(settings.Current.ActiveProfile));
        ReportProblems();
        Watch();
    }

    /// <summary>The profiles were read again, or another one became active.</summary>
    public event EventHandler? Changed;

    public ProfileSet Set { get; private set; }

    public CodeProfile Active { get; private set; }

    public CodeGenerator Generator { get; private set; }

    /// <summary>"Java" or "C#": how the code is called in labels such as "Copy Java line".</summary>
    public string LanguageName => Active.Settings.Highlighting;

    /// <summary>One locator as the active profile's snippet (snippet.sbn), or why the template failed (spec 10.1).</summary>
    public (string? Line, string? Error) Snippet(string variableName, ElementSnapshot snapshot, LocatorCandidate candidate)
    {
        try
        {
            return (Generator.Snippet(variableName, snapshot, candidate), null);
        }
        catch (TemplateException exception)
        {
            return (null, exception.Message);
        }
    }

    public void Select(string id)
    {
        if (id == Active.Id)
        {
            return;
        }

        _settings.Update(settings => settings with { ActiveProfile = id });
        (Active, Generator) = Activate(Set.Find(id));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Copies the active profile into the profiles folder unless it is already there, and opens the folder in Explorer.
    /// Edits there take effect as soon as they are saved.
    /// </summary>
    public void OpenEditableCopy()
    {
        var folder = Path.Combine(_paths.ProfilesDirectory, Active.Id);
        if (!Directory.Exists(folder))
        {
            ProfileLoader.Export(Active, folder);
            LogExported(_logger, Active.Id);
        }

        Explorer.Open(folder);
    }

    /// <summary>
    /// Copies the active profile under a new name ("Java · TestNG · Appium 7 (copy)", folder java-appium-pom-copy), makes the
    /// copy active and opens its folder: the way to change a profile without touching the original (spec 10.1).
    /// </summary>
    public void Duplicate()
    {
        var id = Active.Id + "-copy";
        for (var n = 2; Directory.Exists(Path.Combine(_paths.ProfilesDirectory, id)) || Set.Profiles.Any(p => p.Id == id); n++)
        {
            id = Active.Id + "-copy" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var folder = Path.Combine(_paths.ProfilesDirectory, id);
        ProfileLoader.Copy(Active, folder, Text.Format(Strings.Profile_CopyName, Active.DisplayName));
        LogExported(_logger, id);
        Set = ProfileLoader.Load(_paths.ProfilesDirectory);
        _settings.Update(settings => settings with { ActiveProfile = id });
        (Active, Generator) = Activate(Set.Find(id));
        ReportProblems();
        Changed?.Invoke(this, EventArgs.Empty);
        Explorer.Open(folder);
    }

    /// <summary>Opens the folder that holds the user's profiles.</summary>
    public void OpenFolder() => Explorer.Open(_paths.ProfilesDirectory);

    // Idempotent (D-27); may run on another thread at shutdown, so the timer is left alone.
    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private (CodeProfile Profile, CodeGenerator Generator) Activate(CodeProfile profile)
    {
        _catalog.Naming = profile.Settings.Naming();
        return (profile, new CodeGenerator(profile));
    }

    private void Reload()
    {
        _reload.Stop();
        Set = ProfileLoader.Load(_paths.ProfilesDirectory);
        (Active, Generator) = Activate(Set.Find(Active.Id));
        LogReloaded(_logger, Set.Profiles.Count, Set.Problems.Count);
        ReportProblems();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // A profile folder that can't be read is a lasting condition: a banner until it is fixed (spec 11.5).
    private void ReportProblems()
    {
        if (Set.Problems is [var first, ..])
        {
            _banners.Show("profiles", Text.Format(Strings.Banner_ProfileProblem, first.ProfileId, first.File, first.Message), isWarning: true);
        }
        else
        {
            _banners.Remove("profiles");
        }
    }

    private void Watch()
    {
        try
        {
            Directory.CreateDirectory(_paths.ProfilesDirectory);
            _watcher = new FileSystemWatcher(_paths.ProfilesDirectory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            FileSystemEventHandler changed = (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => { _reload.Stop(); _reload.Start(); });
            _watcher.Changed += changed;
            _watcher.Created += changed;
            _watcher.Deleted += changed;
            _watcher.Renamed += (sender, e) => changed(sender, e);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Profiles still load; they are just not read again on their own.
            LogWatchFailed(_logger, exception.GetType().Name);
        }
    }

    [LoggerMessage(EventId = 900, Level = LogLevel.Information, Message = "Code profiles read again: {Profiles} profiles, {Problems} problems")]
    private static partial void LogReloaded(ILogger logger, int profiles, int problems);

    [LoggerMessage(EventId = 901, Level = LogLevel.Information, Message = "Copied the {Profile} profile to the profiles folder for editing")]
    private static partial void LogExported(ILogger logger, string profile);

    [LoggerMessage(EventId = 902, Level = LogLevel.Warning, Message = "The profiles folder cannot be watched ({ErrorType}); edits take effect after a restart")]
    private static partial void LogWatchFailed(ILogger logger, string errorType);
}
