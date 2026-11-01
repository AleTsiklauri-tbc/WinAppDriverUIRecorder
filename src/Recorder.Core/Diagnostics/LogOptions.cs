using Recorder.Core.Persistence;

namespace Recorder.Core.Diagnostics;

/// <summary>
/// The only log destination: a local rolling file, one per day, deleted after <see cref="RetainFor"/>.
/// </summary>
public sealed record LogOptions(
    string FilePathPattern,
    TimeSpan RetainFor,
    long FileSizeLimitBytes,
    string OutputTemplate)
{
    public const string FileNamePattern = "recorder-.log";

    public static LogOptions For(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new LogOptions(
            Path.Combine(paths.LogsDirectory, FileNamePattern),
            TimeSpan.FromDays(7),
            50L * 1024 * 1024,
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
    }
}
