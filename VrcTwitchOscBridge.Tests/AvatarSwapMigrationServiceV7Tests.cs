using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSwapMigrationServiceV7Tests
{
    [Fact]
    public void CurrentMigrationVersion_IsAtLeast7()
    {
        Assert.True(AvatarSwapMigrationService.CurrentMigrationVersion >= 7);
    }
}
