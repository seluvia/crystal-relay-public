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

    [Fact]
    public void GroupFilterOptions_IncludesAll_Ungrouped_AndGroupsInSortOrder()
    {
        var library = new AvatarLibrary();
        library.Groups.Add(new AvatarGroup { Id = "g2", Name = "Public", SortOrder = 1 });
        library.Groups.Add(new AvatarGroup { Id = "g1", Name = "Cuties", SortOrder = 0 });

        var options = AvatarLibraryFilterOptionsBuilder.BuildGroupOptions(library);

        Assert.Equal(4, options.Count); // All, Ungrouped, Cuties, Public
        Assert.Null(options[0].Id);
        Assert.Equal("All", options[0].Display);
        Assert.Equal("ungrouped", options[1].Id);
        Assert.Equal("Ungrouped", options[1].Display);
        Assert.Equal("g1", options[2].Id);
        Assert.Equal("Cuties", options[2].Display);
        Assert.Equal("g2", options[3].Id);
        Assert.Equal("Public", options[3].Display);
    }

    [Fact]
    public void TagFilterOptions_IncludesAll_AndTagsInNameOrder()
    {
        var library = new AvatarLibrary();
        library.Tags.Add(new AvatarTag { Id = "t2", Name = "Fav", ColorHex = "#F472B6" });
        library.Tags.Add(new AvatarTag { Id = "t1", Name = "Mini", ColorHex = "#A855F7" });

        var options = AvatarLibraryFilterOptionsBuilder.BuildTagOptions(library);

        Assert.Equal(3, options.Count); // All, Fav, Mini (alphabetical by Name)
        Assert.Null(options[0].Id);
        Assert.Equal("All", options[0].Display);
        Assert.Equal("t2", options[1].Id); // Fav sorts before Mini
        Assert.Equal("Fav", options[1].Display);
        Assert.Equal("t1", options[2].Id);
        Assert.Equal("Mini", options[2].Display);
    }
}
