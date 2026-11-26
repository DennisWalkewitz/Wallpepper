namespace Wallpepper.Desktop;

/// <summary>
/// The truth about rendering: a display that should draw but has presented nothing for a few seconds is stalled.
/// Compiling a shader or rebuilding the device does not count. Recovery is asked for with a growing delay.
/// </summary>
internal sealed class StallWatchdog
{
    private const double StallSeconds = 5;
    private const double FirstRecoveryDelaySeconds = 5;
    private const double MaxRecoveryDelaySeconds = 300;

    private sealed class Seen
    {
        public int Frames { get; set; }
        public double ProgressAt { get; set; }
    }

    private readonly Dictionary<DisplaySlot, Seen> _seen = new Dictionary<DisplaySlot, Seen>();
    private double _nextRecoveryAt;
    private double _recoveryDelay = FirstRecoveryDelaySeconds;

    /// <summary>Looks at one display. Returns true when it is stalled.</summary>
    /// <param name="expectFrames">false while the display has a good reason to present nothing</param>
    public bool Check(DisplaySlot slot, double now, bool expectFrames)
    {
        if (!_seen.TryGetValue(slot, out var seen))
        {
            seen = new Seen { ProgressAt = now };
            _seen[slot] = seen;
        }
        int frames = slot.Target.Stats.Frames;
        if (!expectFrames || frames != seen.Frames)
        {
            seen.ProgressAt = now;
        }
        seen.Frames = frames;
        return now - seen.ProgressAt > StallSeconds;
    }

    /// <summary>Called once per look at all displays. Returns true when the windows and the device should be rebuilt now.</summary>
    public bool ShouldRecover(bool anyStalled, double now, IReadOnlyList<DisplaySlot> slots)
    {
        foreach (var gone in _seen.Keys.Where(slot => !slots.Contains(slot)).ToList())
        {
            _seen.Remove(gone);
        }
        if (!anyStalled)
        {
            _recoveryDelay = FirstRecoveryDelaySeconds;
            _nextRecoveryAt = 0;
            return false;
        }
        if (now < _nextRecoveryAt)
        {
            return false;
        }
        // rebuild, then leave it more and more time before trying again
        _nextRecoveryAt = now + _recoveryDelay;
        _recoveryDelay = Math.Min(MaxRecoveryDelaySeconds, _recoveryDelay * 2);
        foreach (var seen in _seen.Values)
        {
            seen.ProgressAt = now;
        }
        return true;
    }
}
