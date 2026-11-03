using Recorder.Core.Automation;
using Recorder.Core.Model;

namespace Recorder.Core.Recording;

/// <param name="Changed">Looked up after the press, so it may not be what was clicked (spec 9.2).</param>
internal readonly record struct ResolvedElement(UiaStatus Status, ElementSnapshot? Element, bool Changed);

/// <summary>
/// Finds the element of a press (spec 9.2, D-44): the hover cache when it is fresh and contains the point, otherwise the
/// point, looked up at once. A busy target is asked again until it answers, so a click on a frozen app is never lost (F-16).
/// </summary>
internal sealed class ClickResolver(IUiaService uia, IRecordingTarget target, HoverTracker hover, RecordingOptions options, TimeProvider time, CancellationToken stopping)
{
    /// <summary>Called on the hook dispatcher thread, before the click changes the screen.</summary>
    public Task<ResolvedElement> ResolveAsync(PixelPoint point)
    {
        var cached = hover.Latest;
        return cached is not null && cached.Element.Bounds.Contains(point) && time.GetElapsedTime(cached.Timestamp) < options.HoverFreshness
            ? Task.FromResult(new ResolvedElement(UiaStatus.Ok, cached.Element, Changed: false))
            : ResolveAtAsync(point);
    }

    private async Task<ResolvedElement> ResolveAtAsync(PixelPoint point)
    {
        var started = time.GetTimestamp();
        var waited = false;
        while (true)
        {
            var result = await uia.CaptureAtAsync(point, stopping).ConfigureAwait(false);
            if (result.IsOk)
            {
                var element = result.Value!;
                return target.Owns(element)
                    ? new ResolvedElement(UiaStatus.Ok, element, waited || !element.Bounds.Contains(point))
                    : new ResolvedElement(UiaStatus.NothingThere, null, false);
            }

            if (result.Status != UiaStatus.Busy || time.GetElapsedTime(started) >= options.BusyRetryFor)
            {
                return new ResolvedElement(result.Status, null, false);
            }

            waited = true;
            await Task.Delay(options.BusyRetryInterval, time, stopping).ConfigureAwait(false);
        }
    }
}
