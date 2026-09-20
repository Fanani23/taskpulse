namespace TaskPulse.Realtime.Infrastructure;

// Fixed one-minute window counter for one connection. Not thread-safe on purpose: a connection's messages are
// handled one at a time by its receive loop.
public sealed class RateGate
{
    private long _windowStart;
    private int _count;

    public RateGateVerdict Hit(int limit, TimeProvider? clock = null)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow().ToUnixTimeMilliseconds();
        if (now - _windowStart >= 60_000)
        {
            _windowStart = now;
            _count = 0;
        }

        _count++;
        if (_count <= limit)
        {
            return RateGateVerdict.Allowed;
        }

        return _count > limit * 2 ? RateGateVerdict.Abusive : RateGateVerdict.Limited;
    }
}

public enum RateGateVerdict
{
    Allowed,
    Limited,
    Abusive,
}
