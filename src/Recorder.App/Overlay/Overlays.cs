namespace Recorder.App.Overlay;

/// <summary>
/// The recorder's on-screen highlights: the Pick/flash outline and the "Highlight matches" outlines. Windows must be
/// destroyed by the thread that created them, so this is disposed on the UI thread when the main window closes.
/// </summary>
public sealed class Overlays : IDisposable
{
    private HighlightOverlay? _hover;
    private MatchHighlighter? _matches;
    private bool _disposed;

    public HighlightOverlay Hover
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _hover ??= new HighlightOverlay();
        }
    }

    public MatchHighlighter Matches
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _matches ??= new MatchHighlighter();
        }
    }

    /// <summary>The hover outline if it was ever shown, for tests and shutdown.</summary>
    public HighlightOverlay? HoverIfCreated => _hover;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hover?.Dispose();
        _matches?.Dispose();
    }
}
