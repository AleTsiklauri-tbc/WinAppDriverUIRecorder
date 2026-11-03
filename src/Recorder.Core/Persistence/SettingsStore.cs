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

    /// <summary>Start or stop recording (spec 11.9). Registered while the recorder runs.</summary>
    public string RecordHotkey { get; init; } = "F9";

    /// <summary>Pause or resume. Registered only while recording, so F10 keeps working in other apps otherwise.</summary>
    public string PauseHotkey { get; init; } = "F10";

    /// <summary>Assert mode. Registered only while recording.</summary>
    public string AssertHotkey { get; init; } = "Ctrl+Shift+A";

    /// <summary>Spec 9.4: record Enter, Tab, Esc, F1–F12, Delete, arrows and Ctrl or Alt chords.</summary>
    public bool RecordSpecialKeys { get; init; } = true;

    /// <summary>Spec 9.3: every typed value becomes a test-data key instead of its text.</summary>
    public bool MaskTypedValues { get; init; }

    /// <summary>Spec 9.5: record wheel scrolls as comments.</summary>
    public bool RecordScrolls { get; init; }

    /// <summary>Spec 11.4: minimize the main window while recording; the mini-bar stays on top.</summary>
    public bool MinimizeOnRecord { get; init; } = true;

    /// <summary>Where the user last dragged the recording mini-bar (device-independent pixels); null for top center.</summary>
    public double? MiniBarLeft { get; init; }

    public double? MiniBarTop { get; init; }

    /// <summary>The folder of the last recording saved or opened.</summary>
    public string? LastRecordingFolder { get; init; }

    /// <summary>The code profile the code pane uses (spec 10.1): a folder name, built-in or under the profiles folder.</summary>
    public string ActiveProfile { get; init; } = "java-appium-pom";

    /// <summary>The folder code was last saved, exported or merged in.</summary>
    public string? LastCodeFolder { get; init; }

    /// <summary>Light, dark, or as Windows is set (spec 12, General).</summary>
    public ThemeChoice Theme { get; init; } = ThemeChoice.System;

    /// <summary>Right of the Inspector or below it (spec 11.2).</summary>
    public CodePanePosition CodePanePosition { get; init; } = CodePanePosition.Right;

    /// <summary>Detailed logs add timings and statuses; they never hold names, values or titles (spec 12, Advanced).</summary>
    public bool DetailedLogs { get; init; }

    /// <summary>The main window's size, position and pane splits when it was last closed (spec 11.2).</summary>
    public WindowLayout? Window { get; init; }

    /// <summary>Recordings saved or opened lately, most recent first (spec 12).</summary>
    public IReadOnlyList<string> RecentRecordings { get; init; } = [];
}

public enum ThemeChoice
{
    System,
    Light,
    Dark,
}

public enum CodePanePosition
{
    Right,
    Bottom,
}

/// <summary>Device-independent pixels. The bounds are the normal (not maximized) ones.</summary>
/// <param name="CodePaneWidth">The code pane's width on the right.</param>
/// <param name="CodePaneHeight">The code pane's height below the Inspector.</param>
public sealed record WindowLayout(
    double Left, double Top, double Width, double Height, bool Maximized, double LeftPaneWidth, double CodePaneWidth, double CodePaneHeight)
{
    private const double TitleBarHeight = 32;
    private const double VisibleTitleWidth = 100;
    private const double Slack = 8;

    /// <summary>
    /// The layout to restore on a desktop with the given bounds (the virtual screen): no larger than the desktop, and
    /// centered on it when the title bar would not show, e.g. after a monitor was disconnected. Null for bounds that make no sense.
    /// </summary>
    public WindowLayout? FitTo(double left, double top, double width, double height)
    {
        if (!(double.IsFinite(Left) && double.IsFinite(Top) && Width > 0 && Height > 0 && double.IsFinite(Width) && double.IsFinite(Height)))
        {
            return null;
        }

        var fitted = this with { Width = Math.Min(Width, width), Height = Math.Min(Height, height) };
        var titleShows = Top >= top - Slack && Top + TitleBarHeight <= top + height
            && Left + fitted.Width >= left + VisibleTitleWidth && Left <= left + width - VisibleTitleWidth;
        return titleShows ? fitted : fitted with { Left = left + ((width - fitted.Width) / 2), Top = top + ((height - fitted.Height) / 2) };
    }
}

/// <summary>
/// Reads an enum by name and falls back to its default for a name it does not know, so a value written by a newer version
/// (the schema only grows) never makes settings.json unreadable.
/// </summary>
internal sealed class LenientEnumConverter<T> : JsonConverter<T>
    where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var value) && Enum.IsDefined(value))
        {
            return value;
        }

        reader.Skip();
        return default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
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
        Converters = { new LenientEnumConverter<ThemeChoice>(), new LenientEnumConverter<CodePanePosition>() },
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

    /// <summary>Settings changed (raised after they are saved, on the thread that changed them): the new settings.</summary>
    public event EventHandler<AppSettings>? Changed;

    public AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        AppSettings updated;
        lock (_gate)
        {
            updated = Current = change(Current) with { SchemaVersion = AppSettings.CurrentSchemaVersion };
            if (!_readOnly)
            {
                Save(Current);
            }
        }

        Changed?.Invoke(this, updated);
        return updated;
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
