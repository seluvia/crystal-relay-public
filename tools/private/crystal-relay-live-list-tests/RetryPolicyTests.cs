using CrystalRelayLiveList.Services;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class RetryPolicyTests
{
    [Fact]
    public void FirstFailure_ReturnsBaseDelay()
    {
        var policy = new RetryPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(1), policy.NextDelay());
    }

    [Fact]
    public void RepeatedFailures_GrowExponentially()
    {
        var policy = new RetryPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(1), policy.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(2), policy.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(4), policy.NextDelay());
    }

    [Fact]
    public void DelayCapsAtMax()
    {
        var policy = new RetryPolicy(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(10), policy.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(20), policy.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(30), policy.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(30), policy.NextDelay());
    }

    [Fact]
    public void Reset_ReturnsToBaseDelay()
    {
        var policy = new RetryPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
        policy.NextDelay();
        policy.NextDelay();
        policy.Reset();
        Assert.Equal(TimeSpan.FromSeconds(1), policy.NextDelay());
    }
}
