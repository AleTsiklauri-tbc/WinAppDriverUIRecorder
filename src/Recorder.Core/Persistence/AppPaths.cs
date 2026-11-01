namespace Recorder.Core.Persistence;

/// <summary>
/// Every location the recorder writes to. Settings and profiles roam with the user;
/// logs and autosaves stay on this machine. Nothing is written anywhere else.
/// </summary>
public sealed class AppPaths
{
    public const string ProductFolderName = "WinFormsRecorder";

    /// <summary>Environment variable that redirects all data into one folder (tests, portable use).</summary>
    public const string HomeOverrideVariable = "WINFORMSRECORDER_HOME";

    public AppPaths(string roamingRoot, string localRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roamingRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(localRoot);
        RoamingRoot = roamingRoot;
        LocalRoot = localRoot;
    }

    /// <summary>%APPDATA%\WinFormsRecorder</summary>
    public string RoamingRoot { get; }

    /// <summary>%LOCALAPPDATA%\WinFormsRecorder</summary>
    public string LocalRoot { get; }

    public string SettingsFile => Path.Combine(RoamingRoot, "settings.json");

    public string ProfilesDirectory => Path.Combine(RoamingRoot, "profiles");

    public string LogsDirectory => Path.Combine(LocalRoot, "logs");

    public string AutosaveDirectory => Path.Combine(LocalRoot, "autosave");

    public static AppPaths ForCurrentUser() =>
        Resolve(Environment.GetEnvironmentVariable, Environment.GetFolderPath);

    public static AppPaths Resolve(
        Func<string, string?> getEnvironmentVariable,
        Func<Environment.SpecialFolder, string> getFolderPath)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(getFolderPath);

        var home = getEnvironmentVariable(HomeOverrideVariable);
        if (!string.IsNullOrWhiteSpace(home))
        {
            var root = Path.GetFullPath(home);
            return new AppPaths(Path.Combine(root, "roaming"), Path.Combine(root, "local"));
        }

        return new AppPaths(
            Path.Combine(getFolderPath(Environment.SpecialFolder.ApplicationData), ProductFolderName),
            Path.Combine(getFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductFolderName));
    }
}
