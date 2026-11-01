using Recorder.Core.Diagnostics;
using Recorder.Core.Persistence;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Diagnostics;

public class LogOptionsTests
{
    private readonly LogOptions _options = LogOptions.For(new AppPaths(@"C:\roaming\WinFormsRecorder", @"C:\local\WinFormsRecorder"));

    [Fact]
    public void Logs_go_to_the_local_logs_folder_only() =>
        _options.FilePathPattern.ShouldBe(@"C:\local\WinFormsRecorder\logs\recorder-.log");

    [Fact]
    public void Logs_are_kept_for_seven_days() =>
        _options.RetainFor.ShouldBe(TimeSpan.FromDays(7));

    [Fact]
    public void A_single_log_file_is_capped_so_a_noisy_session_cannot_fill_the_disk() =>
        _options.FileSizeLimitBytes.ShouldBe(50L * 1024 * 1024);
}
