using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSwapMigrationServiceV6Tests
{
    [Fact]
    public void CurrentMigrationVersion_IsAtLeast6()
    {
        Assert.True(AvatarSwapMigrationService.CurrentMigrationVersion >= 6);
    }
}
