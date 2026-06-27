using System;
using VrcTwitchOscBridge.Services.Support;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class SupportOverrideCapClampTests
{
    [Fact]
    public void ClampWithProfileCapEnabled_At1800_AddsRequested()
    {
        var requested = TimeSpan.FromSeconds(34);
        var existing = TimeSpan.FromSeconds(1750);
        var result = SupportOverrideCapMath.ClampAddedDuration(
            capEnabled: true,
            capSeconds: 1800,
            requestedDuration: requested,
            existingRemainingDuration: existing);
        Assert.Equal(TimeSpan.FromSeconds(34), result);
    }

    [Fact]
    public void ClampWithProfileCapEnabled_ClampsToRemainingCapacity()
    {
        var requested = TimeSpan.FromSeconds(34);
        var existing = TimeSpan.FromSeconds(1790);
        var result = SupportOverrideCapMath.ClampAddedDuration(
            capEnabled: true,
            capSeconds: 1800,
            requestedDuration: requested,
            existingRemainingDuration: existing);
        Assert.Equal(TimeSpan.FromSeconds(10), result);
    }

    [Fact]
    public void ClampWithProfileCapDisabled_NoClamp()
    {
        var requested = TimeSpan.FromSeconds(1000);
        var existing = TimeSpan.FromSeconds(500);
        var result = SupportOverrideCapMath.ClampAddedDuration(
            capEnabled: false,
            capSeconds: 1800,
            requestedDuration: requested,
            existingRemainingDuration: existing);
        Assert.Equal(TimeSpan.FromSeconds(1000), result);
    }

    [Fact]
    public void ClampWithZeroRequested_ReturnsZero()
    {
        var result = SupportOverrideCapMath.ClampAddedDuration(
            capEnabled: true,
            capSeconds: 1800,
            requestedDuration: TimeSpan.Zero,
            existingRemainingDuration: TimeSpan.FromSeconds(1750));
        Assert.Equal(TimeSpan.Zero, result);
    }
}
