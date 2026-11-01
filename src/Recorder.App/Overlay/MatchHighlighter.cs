using System.Windows.Threading;
using Recorder.Core.Model;

namespace Recorder.App.Overlay;

/// <summary>"Highlight matches" (spec 11.2): outlines every element a locator finds for 1.5 s. UI thread only.</summary>
public sealed class MatchHighlighter : IDisposable
{
    /// <summary>More outlines than this would hide the screen rather than explain it.</summary>
    public const int MaxShown = 20;

    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(1500);
    private readonly List<HighlightOverlay> _overlays = [];
    private readonly DispatcherTimer _timer;

    public MatchHighlighter()
    {
        _timer = new DispatcherTimer(Duration, DispatcherPriority.Normal, (_, _) => Hide(), Dispatcher.CurrentDispatcher) { IsEnabled = false };
    }

    public bool IsShowing => _overlays.Any(overlay => overlay.IsVisible);

    /// <summary>The overlays' window handles, for tests.</summary>
    public IEnumerable<nint> WindowHandles => _overlays.SelectMany(overlay => overlay.WindowHandles);

    public void Show(IReadOnlyList<(PixelRect Bounds, string Label)> matches, HighlightStyle style)
    {
        ArgumentNullException.ThrowIfNull(matches);
        Hide();
        for (var i = 0; i < matches.Count && i < MaxShown; i++)
        {
            if (i == _overlays.Count)
            {
                _overlays.Add(new HighlightOverlay());
            }

            _overlays[i].Show(matches[i].Bounds, matches[i].Label, style);
        }

        _timer.Start();
    }

    public void Hide()
    {
        _timer.Stop();
        foreach (var overlay in _overlays)
        {
            overlay.Hide();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        foreach (var overlay in _overlays)
        {
            overlay.Dispose();
        }

        _overlays.Clear();
    }
}
