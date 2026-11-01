using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Recorder.App.Hosting;

/// <summary>Lifecycle log lines; the first thing support needs when reading a log file.</summary>
internal static partial class AppLog
{
    public static readonly string AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    private static readonly string Os = RuntimeInformation.OSDescription;
    private static readonly string Runtime = RuntimeInformation.FrameworkDescription;

    public static void Started(ILogger logger) => LogStarted(logger, AppVersion, Os, Runtime);

    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "WinForms Recorder {Version} started on {Os}, {Runtime}")]
    private static partial void LogStarted(ILogger logger, string version, string os, string runtime);

    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "WinForms Recorder stopped")]
    public static partial void Stopped(ILogger logger);
}
