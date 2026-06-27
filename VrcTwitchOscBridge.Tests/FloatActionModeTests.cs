using VrcTwitchOscBridge.Models;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class FloatActionModeTests
{
    [Fact]
    public void Set_IsDefaultValue()
    {
        Assert.Equal(FloatActionMode.Set, default(FloatActionMode));
    }

    [Fact]
    public void HasTenMembersInExpectedOrder()
    {
        var expected = new[]
        {
            "Set", "Random", "Add", "Subtract", "AddSubtract",
            "Multiply", "Toggle", "Cycle", "Glitchy", "Pulse"
        };
        var actual = System.Enum.GetNames<FloatActionMode>();
        Assert.Equal(expected, actual);
    }
}
