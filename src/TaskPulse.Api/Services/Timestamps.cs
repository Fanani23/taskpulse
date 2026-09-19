namespace TaskPulse.Api.Services;

internal static class Timestamps
{
    private const long TicksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;

    public static DateTimeOffset ToMicroseconds(DateTimeOffset value)
        => new(value.UtcTicks - value.UtcTicks % TicksPerMicrosecond, TimeSpan.Zero);
}
