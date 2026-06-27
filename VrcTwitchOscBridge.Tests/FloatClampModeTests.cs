using VrcTwitchOscBridge.Models;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class FloatClampModeTests
{
    [Fact]
    public void HasThreeMembersInExpectedOrder()
    {
        var actual = System.Enum.GetNames<FloatClampMode>();
        Assert.Equal(new[] { "None", "ZeroToOne", "MinToMax" }, actual);
    }

    [Fact]
    public void Values_AreDistinctNonNegativeInts()
    {
        var values = System.Enum.GetValues<FloatClampMode>();
        Assert.Equal(3, values.Length);
        Assert.Equal(0, (int)FloatClampMode.None);
        Assert.Equal(1, (int)FloatClampMode.ZeroToOne);
        Assert.Equal(2, (int)FloatClampMode.MinToMax);
    }
}
