using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Architecture;

/// <summary>
/// Every log message has its own EventId, so a line in a user's log points at one place in the code. Each service has a
/// range (ARCHITECTURE.md §3, Hosting); two services once picked the same numbers.
/// </summary>
public partial class LogEventIdTests
{
    [Fact]
    public void No_two_log_messages_share_an_EventId()
    {
        var root = RepoRoot.Find();
        var messages = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase) && !file.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => EventIdRegex().Matches(File.ReadAllText(file)).Select(match => (Id: int.Parse(match.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture), File: Path.GetRelativePath(root, file))))
            .ToList();

        messages.Count.ShouldBeGreaterThan(30, "the scan found the [LoggerMessage] attributes");
        messages.GroupBy(message => message.Id)
            .Where(group => group.Count() > 1)
            .Select(group => $"EventId {group.Key}: {string.Join(", ", group.Select(message => message.File))}")
            .ShouldBeEmpty();
    }

    [GeneratedRegex(@"\[LoggerMessage\(EventId\s*=\s*(?<id>\d+)")]
    private static partial Regex EventIdRegex();
}
