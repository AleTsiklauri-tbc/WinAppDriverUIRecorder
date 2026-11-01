using Recorder.Core.Diagnostics;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Diagnostics;

public class ErrorReportTests
{
    private static Exception Thrown()
    {
        try
        {
            throw new InvalidOperationException("outer failure", new TimeoutException("inner cause"));
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    [Fact]
    public void The_on_screen_message_is_the_plain_message_only()
    {
        var report = ErrorReport.Create(Thrown(), "Something went wrong.", DateTimeOffset.Now, "1.2.3");

        report.UserMessage.ShouldBe("Something went wrong.");
    }

    [Fact]
    public void Details_carry_everything_needed_to_diagnose_the_problem()
    {
        var at = new DateTimeOffset(2026, 10, 29, 14, 32, 5, TimeSpan.FromHours(4));

        var report = ErrorReport.Create(Thrown(), "Something went wrong.", at, "1.2.3");

        report.Details.ShouldContain("WinForms Recorder 1.2.3");
        report.Details.ShouldContain("2026-10-29 14:32:05 +04:00");
        report.Details.ShouldContain("System.InvalidOperationException: outer failure");
        report.Details.ShouldContain("System.TimeoutException: inner cause");
        report.Details.ShouldContain(nameof(Thrown));
    }

    [Fact]
    public void A_user_message_is_required() =>
        Should.Throw<ArgumentException>(() => ErrorReport.Create(Thrown(), " ", DateTimeOffset.Now, "1.0.0"));
}
