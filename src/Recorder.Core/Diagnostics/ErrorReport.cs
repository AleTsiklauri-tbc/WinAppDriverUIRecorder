using System.Globalization;

namespace Recorder.Core.Diagnostics;

/// <summary>
/// An unexpected error as presented to the user: a plain-language message on screen,
/// and technical details that only leave the app when the user clicks "Copy details".
/// </summary>
public sealed record ErrorReport(string UserMessage, string Details)
{
    public static ErrorReport Create(Exception exception, string userMessage, DateTimeOffset occurredAt, string appVersion)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);

        var details = string.Join(
            Environment.NewLine,
            "WinForms Recorder " + appVersion,
            "Time: " + occurredAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            "OS: " + Environment.OSVersion.VersionString,
            exception.ToString());

        return new ErrorReport(userMessage, details);
    }
}
