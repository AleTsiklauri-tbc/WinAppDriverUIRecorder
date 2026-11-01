using Recorder.Core.Automation;
using Recorder.Core.Model;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Automation;

public class HoverDebouncerTests
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(40);
    private static readonly PixelPoint A = new(100, 100);
    private static readonly PixelPoint B = new(300, 120);

    private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    [Fact]
    public void A_point_is_resolved_once_the_cursor_rested_for_the_debounce_time()
    {
        var debouncer = new HoverDebouncer(Debounce);

        debouncer.Sample(A, Ms(0)).ShouldBeNull();
        debouncer.Sample(A, Ms(25)).ShouldBeNull();
        debouncer.Sample(A, Ms(50)).ShouldBe(A);
        debouncer.SettledSince.ShouldBe(Ms(0));
    }

    [Fact]
    public void A_resolved_point_is_not_resolved_again_while_the_cursor_stays()
    {
        var debouncer = new HoverDebouncer(Debounce);
        debouncer.Sample(A, Ms(0));
        debouncer.Sample(A, Ms(50));

        debouncer.Sample(A, Ms(75)).ShouldBeNull();
        debouncer.Sample(A, Ms(500)).ShouldBeNull();
    }

    [Fact]
    public void Moving_restarts_the_wait()
    {
        var debouncer = new HoverDebouncer(Debounce);
        debouncer.Sample(A, Ms(0));
        debouncer.Sample(B, Ms(25)).ShouldBeNull();
        debouncer.Sample(B, Ms(50)).ShouldBeNull();

        debouncer.Sample(B, Ms(75)).ShouldBe(B);
        debouncer.SettledSince.ShouldBe(Ms(25));
    }

    [Fact]
    public void Returning_to_a_previous_point_resolves_it_again()
    {
        var debouncer = new HoverDebouncer(Debounce);
        debouncer.Sample(A, Ms(0));
        debouncer.Sample(A, Ms(50)).ShouldBe(A);
        debouncer.Sample(B, Ms(75));
        debouncer.Sample(A, Ms(100));

        debouncer.Sample(A, Ms(150)).ShouldBe(A);
    }

    [Fact]
    public void Reset_resolves_the_current_point_again()
    {
        var debouncer = new HoverDebouncer(Debounce);
        debouncer.Sample(A, Ms(0));
        debouncer.Sample(A, Ms(50));

        debouncer.Reset();

        debouncer.Sample(A, Ms(75)).ShouldBe(A);
    }
}
