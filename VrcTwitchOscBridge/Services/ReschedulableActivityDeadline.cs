namespace VrcTwitchOscBridge.Services;

internal sealed class ReschedulableActivityDeadline
{
    private static readonly Func<TimeSpan, CancellationToken, Task> ProductionDelay =
        static (remaining, cancellationToken) => Task.Delay(remaining, cancellationToken);
    private readonly object gate = new();
    private readonly Func<TimeSpan, CancellationToken, Task> delayFactory;
    private DateTimeOffset deadline;
    private TaskCompletionSource wake = CreateWakeSource();

    public ReschedulableActivityDeadline(DateTimeOffset deadline)
        : this(deadline, ProductionDelay)
    {
    }

    internal ReschedulableActivityDeadline(
        DateTimeOffset deadline,
        Func<TimeSpan, CancellationToken, Task> delayFactory)
    {
        this.deadline = deadline;
        this.delayFactory = delayFactory ?? throw new ArgumentNullException(nameof(delayFactory));
    }

    public DateTimeOffset Deadline
    {
        get
        {
            lock (gate)
            {
                return deadline;
            }
        }
    }

    public void Extend(TimeSpan extension)
    {
        if (extension <= TimeSpan.Zero)
        {
            return;
        }

        TaskCompletionSource previous;
        lock (gate)
        {
            deadline = deadline.Add(extension);
            previous = wake;
            wake = CreateWakeSource();
        }

        previous.TrySetResult();
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            DateTimeOffset currentDeadline;
            Task wakeTask;
            lock (gate)
            {
                currentDeadline = deadline;
                wakeTask = wake.Task;
            }

            var remaining = currentDeadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            var delay = delayFactory(remaining, cancellationToken);
            var completed = await Task.WhenAny(delay, wakeTask).ConfigureAwait(false);
            if (ReferenceEquals(completed, delay))
            {
                await delay.ConfigureAwait(false);
                lock (gate)
                {
                    if (deadline <= DateTimeOffset.UtcNow)
                    {
                        return;
                    }
                }
            }
        }
    }

    private static TaskCompletionSource CreateWakeSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
