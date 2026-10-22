using System.Diagnostics;

namespace PCue.Services;

/// <summary>
/// LibVLC 3 only refreshes the media time roughly every 250 ms. Interpolates between
/// updates so fades and out points move smoothly instead of in steps.
/// </summary>
public sealed class PlaybackClock
{
    private const long MaxExtrapolationMs = 400;

    private long _lastRawMs = long.MinValue;
    private long _anchorMs;
    private long _anchorTimestamp;

    /// <summary>Call after resume, pause or seek so stale anchors are not extrapolated.</summary>
    public void Reset() => _lastRawMs = long.MinValue;

    public long Estimate(long rawMs)
    {
        var now = Stopwatch.GetTimestamp();
        if (rawMs != _lastRawMs)
        {
            _lastRawMs = rawMs;
            _anchorMs = rawMs;
            _anchorTimestamp = now;
            return rawMs;
        }

        var elapsed = (long)Stopwatch.GetElapsedTime(_anchorTimestamp, now).TotalMilliseconds;
        return _anchorMs + Math.Min(elapsed, MaxExtrapolationMs);
    }
}
