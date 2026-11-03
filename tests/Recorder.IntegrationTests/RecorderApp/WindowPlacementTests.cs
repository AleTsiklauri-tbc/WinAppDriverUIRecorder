using FlaUI.Core.Definitions;
using Recorder.Core.Model;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.RecorderApp;

/// <summary>
/// Spec 11.2 on two monitors, which may have different scales (D-76): the main window comes back on the monitor it was
/// closed on, at the same size, maximized or not. Moves the recorder's own window without the mouse; skipped with one monitor.
/// </summary>
public sealed class WindowPlacementTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly string _home = RecorderLauncher.NewDataHome();
    private AppSession? _session;

    public void Dispose()
    {
        _session?.Dispose();
        RecorderLauncher.DeleteDataHome(_home, nameof(WindowPlacementTests));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void The_window_comes_back_on_the_monitor_it_was_closed_on(int number, bool maximized)
    {
        var monitors = Monitors.All();
        Assert.SkipWhen(monitors.Count < 2, "Needs two monitors.");
        var monitor = monitors[number - 1];

        // A size of its own (1100 × 700 DIPs), so that the default size can't pass by chance.
        var window = Launch();
        Monitors.MoveOnto(window, monitor);
        Monitors.Resize(window, (int)(1100 * monitor.Scale), (int)(700 * monitor.Scale));
        var normal = Monitors.BoundsOf(window);
        if (maximized)
        {
            SetState(WindowVisualState.Maximized);
            Wait.Until(() => Monitors.IsMaximized(window), Timeout, "the window to be maximized");
        }

        _session!.CloseAndWaitForExit(Timeout).ShouldBe(0);
        _session.Dispose();
        _session = null;
        window = Launch();

        Wait.Until(
            () => Monitors.Of(Monitors.BoundsOf(window)) == monitor && Monitors.IsMaximized(window) == maximized,
            Timeout,
            () => $"the window on {monitor}{(maximized ? ", maximized" : string.Empty)}; it is at {Monitors.BoundsOf(window)} on {Monitors.Of(Monitors.BoundsOf(window))}");
        if (maximized)
        {
            SetState(WindowVisualState.Normal);
        }

        Wait.Until(() => IsNear(Monitors.BoundsOf(window), normal), Timeout, () => $"the window at {normal}; it is at {Monitors.BoundsOf(window)}");
    }

    // Positions go through device-independent pixels: a pixel either way is the same place.
    private static bool IsNear(PixelRect actual, PixelRect expected) =>
        Math.Abs(actual.X - expected.X) <= 2 && Math.Abs(actual.Y - expected.Y) <= 2
        && Math.Abs(actual.Right - expected.Right) <= 2 && Math.Abs(actual.Bottom - expected.Bottom) <= 2;

    private nint Launch()
    {
        _session = RecorderLauncher.Launch(_home);
        Wait.ForDescendant(_session.MainWindow, "Status.State");
        return _session.MainWindow.Properties.NativeWindowHandle.Value;
    }

    private void SetState(WindowVisualState state) => _session!.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(state);
}
