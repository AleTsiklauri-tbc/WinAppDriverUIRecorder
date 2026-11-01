using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Recorder.Core.Persistence;

/// <summary>User settings stored in %APPDATA%\WinFormsRecorder\settings.json.</summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Process name of the last chosen target, e.g. "LMS.exe". The recorder re-attaches to it automatically.</summary>
    public string? LastTargetProcessName { get; init; }

    public string CaptureHotkey { get; init; } = "Ctrl+Shift+X";

    /// <summary>How long one UI Automation call may take before the target counts as busy.</summary>
    public int UiaTimeoutMilliseconds { get; init; } = 2000;

    /// <summary>WinAppDriver for "Verify with WinAppDriver" (spec 7.7). Only this machine unless <see cref="AllowRemoteWinAppDriver"/>.</summary>
    public string WinAppDriverUrl { get; init; } = "http://127.0.0.1:4723";

    /// <summary>"" or a prefix such as "/wd/hub".</summary>
    public string WinAppDriverPathPrefix { get; init; } = string.Empty;

    public bool AllowRemoteWinAppDriver { get; init; }
}

/// <summary>
/// Loads and saves <see cref="AppSettings"/>. A damaged file is set aside and defaults are used. A file
/// written by a newer version is left untouched, and changes are kept in memory only.
/// </summary>
public sealed partial class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly ILogger<SettingsStore> _logger;
    private readonly Lock _gate = new();
    private bool _readOnly;

    public SettingsStore(AppPaths paths, ILogger<SettingsStore> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _path = paths.SettingsFile;
        _logger = logger;
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_gate)
        {
            Current = change(Current) with { SchemaVersion = AppSettings.CurrentSchemaVersion };
            if (!_readOnly)
            {
                Save(Current);
            }

            return Current;
        }
    }

    private AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new AppSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings();
            if (settings.SchemaVersion > AppSettings.CurrentSchemaVersion)
            {
                _readOnly = true;
                LogNewerSchema(_logger, settings.SchemaVersion, AppSettings.CurrentSchemaVersion);
                return new AppSettings();
            }

            return settings;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            var backup = _path + ".damaged-" + DateTime.Now.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
            TryMove(_path, backup);
            LogDamaged(_logger, exception, backup);
            return new AppSettings();
        }
    }

    private void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }

    private static void TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: true);
        }
        catch (IOException)
        {
            // The defaults are used either way.
        }
    }

    [LoggerMessage(EventId = 300, Level = LogLevel.Warning, Message = "settings.json could not be read; it was moved to {Backup} and defaults are used")]
    private static partial void LogDamaged(ILogger logger, Exception exception, string backup);

    [LoggerMessage(EventId = 301, Level = LogLevel.Warning, Message = "settings.json has schema version {FileVersion} (newer than {SupportedVersion}); it is left unchanged and defaults are used")]
    private static partial void LogNewerSchema(ILogger logger, int fileVersion, int supportedVersion);
}
