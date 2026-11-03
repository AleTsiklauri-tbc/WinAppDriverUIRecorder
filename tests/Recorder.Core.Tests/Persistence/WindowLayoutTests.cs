using Recorder.Core.Persistence;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Persistence;

public sealed class WindowLayoutTests
{
    // One 1920 x 1080 monitor.
    private static WindowLayout? Fit(WindowLayout layout) => layout.FitTo(0, 0, 1920, 1080);

    private static WindowLayout At(double left, double top, double width = 1200, double height = 760) =>
        new(left, top, width, height, Maximized: false, LeftPaneWidth: 300, CodePaneWidth: 360, CodePaneHeight: 280);

    [Fact]
    public void A_window_on_the_screen_comes_back_where_it_was()
    {
        var layout = At(100, 50);

        Fit(layout).ShouldBe(layout);
    }

    [Fact]
    public void A_window_partly_off_the_screen_stays_while_its_title_bar_can_be_dragged()
    {
        var layout = At(-900, 300);

        Fit(layout).ShouldBe(layout);
    }

    [Fact]
    public void A_window_larger_than_the_screen_is_cut_down()
    {
        var fitted = Fit(At(0, 0, 2560, 1440))!;

        (fitted.Width, fitted.Height).ShouldBe((1920d, 1080d));
        (fitted.Left, fitted.Top).ShouldBe((0d, 0d));
    }

    [Theory]
    [InlineData(2500, 100)]  // on a second monitor that was disconnected
    [InlineData(100, -400)]  // title bar above the screen
    [InlineData(100, 1060)]  // title bar below the screen
    [InlineData(-1150, 100)] // less than the visible part of the title bar on the left
    public void A_window_whose_title_bar_would_not_show_moves_to_the_middle(double left, double top)
    {
        var fitted = Fit(At(left, top))!;

        (fitted.Left, fitted.Top).ShouldBe((360d, 160d));
        (fitted.Width, fitted.Height).ShouldBe((1200d, 760d));
        fitted.LeftPaneWidth.ShouldBe(300);
    }

    [Theory]
    [InlineData(double.NaN, 0, 1200, 760)]
    [InlineData(0, 0, 0, 760)]
    [InlineData(0, 0, 1200, double.PositiveInfinity)]
    [InlineData(0, 0, -5, 760)]
    public void Nonsense_bounds_are_ignored(double left, double top, double width, double height)
    {
        Fit(At(left, top, width, height)).ShouldBeNull();
    }
}
