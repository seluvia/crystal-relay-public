using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.ViewModels;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarLibraryFilterTests
{
    [Fact]
    public void ResolveGroupFilter_All_ReturnsNull()
    {
        var option = new FilterOption(null, "All");
        Assert.Null(option.Id);
    }

    [Fact]
    public void ResolveGroupFilter_Ungrouped_ReturnsSentinel()
    {
        var option = new FilterOption("ungrouped", "Ungrouped");
        Assert.Equal("ungrouped", option.Id);
    }

    [Fact]
    public void ResolveGroupFilter_RealGroup_ReturnsGroupId()
    {
        var option = new FilterOption("grp_123", "Cuties");
        Assert.Equal("grp_123", option.Id);
    }
}
