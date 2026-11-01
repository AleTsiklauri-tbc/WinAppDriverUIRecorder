using Recorder.Core.Model;

namespace Recorder.Core.Automation;

/// <summary>
/// Decides when the cursor has settled long enough to look up the element under it.
/// Pure logic: fed with (position, time) samples, so it can be tested without a real cursor or clock.
/// </summary>
public sealed class HoverDebouncer(TimeSpan debounce)
{
    private PixelPoint? _pending;
    private TimeSpan _pendingSince;
    private PixelPoint? _resolved;

    /// <summary>Returns the point to resolve once the cursor has rested there for the debounce time, else null.</summary>
    public PixelPoint? Sample(PixelPoint position, TimeSpan now)
    {
        if (_pending != position)
        {
            // A new position: anything resolved before may have changed by the time the cursor comes back.
            _pending = position;
            _pendingSince = now;
            _resolved = null;
            return null;
        }

        if (now - _pendingSince < debounce || _resolved == position)
        {
            return null;
        }

        _resolved = position;
        return position;
    }

    /// <summary>When the cursor first arrived at the last resolved point (start of the hover latency).</summary>
    public TimeSpan SettledSince => _pendingSince;

    /// <summary>True once the current position has been handed out for a lookup.</summary>
    public bool HasResolved => _resolved is not null;

    /// <summary>Forgets the last resolved point, so the same position is resolved again (a refresh).</summary>
    public void Reset() => _resolved = null;
}
