namespace CrystalRelayLiveList.Services;

public sealed class RetryPolicy
{
    private readonly TimeSpan baseDelay;
    private readonly TimeSpan maxDelay;
    private int failures;

    public RetryPolicy(TimeSpan baseDelay, TimeSpan maxDelay)
    {
        this.baseDelay = baseDelay;
        this.maxDelay = maxDelay;
    }

    public TimeSpan NextDelay()
    {
        var delay = TimeSpan.FromTicks(baseDelay.Ticks * (1L << Math.Min(failures, 16)));
        if (delay > maxDelay)
        {
            delay = maxDelay;
        }
        failures++;
        return delay;
    }

    public void Reset() => failures = 0;
}
