using Recorder.App.Overlay;
using Recorder.App.ViewModels;
using Recorder.Core.Automation;
using Recorder.Core.Model;

namespace Recorder.App.Services;

/// <summary>
/// The Pick-mode highlight (spec 8.4): the outline appears at once in neutral purple, then takes the colour of the
/// element's best locator when the uniqueness check answers (green unique, amber text or position, red none).
/// Already captured elements are dashed blue. UI thread only.
/// </summary>
internal sealed class HoverHighlighter(ILocatorService locators, ElementCatalog catalog, Func<HighlightOverlay> overlay)
{
    private int _generation;

    public void Show(ElementSnapshot element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var generation = ++_generation;
        var label = ElementLabel.For(element);
        if (catalog.Contains(element.SessionKey))
        {
            overlay().Show(element.Bounds, label, HighlightStyle.Captured);
            return;
        }

        overlay().Show(element.Bounds, label, HighlightStyle.Neutral);
        _ = ColourAsync(element, label, generation);
    }

    public void Hide()
    {
        _generation++;
        overlay().Hide();
    }

    private async Task ColourAsync(ElementSnapshot element, string label, int generation)
    {
        var result = await locators.LocateAsync(element, LocateMode.Hover, CancellationToken.None).ConfigureAwait(true);
        if (generation != _generation || result.Value is not { } report)
        {
            return; // the cursor moved on, or the check was replaced by a newer one
        }

        var style = LocatorText.Style(LocatorText.Quality(report.Best));
        overlay().Show(element.Bounds, label + LocatorText.ChipSuffix(report), style);
    }
}
