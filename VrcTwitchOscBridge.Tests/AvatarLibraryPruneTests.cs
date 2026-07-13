using VrcTwitchOscBridge.Models;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarLibraryPruneTests
{
    [Fact]
    public void PruneMissingEntries_RemovesEntriesNotInCurrentAvatarList()
    {
        var library = new AvatarLibrary();
        library.Entries.Add(new AvatarLibraryEntry { AvatarId = "avd_11111" });
        library.Entries.Add(new AvatarLibraryEntry { AvatarId = "avd_22222" });
        library.Entries.Add(new AvatarLibraryEntry { AvatarId = "avd_33333" });

        var currentAvatars = new[]
        {
            new VrChatAvatarSummary("avd_11111", "Cutie", "VRChat", false, null),
            new VrChatAvatarSummary("avd_22222", "Other", "VRChat", false, null)
        };

        library.PruneMissingEntries(currentAvatars);

        Assert.Contains(library.Entries, e => e.AvatarId == "avd_11111");
        Assert.Contains(library.Entries, e => e.AvatarId == "avd_22222");
        Assert.DoesNotContain(library.Entries, e => e.AvatarId == "avd_33333");
    }

    [Fact]
    public void PruneMissingEntries_KeepsAllWhenAllPresent()
    {
        var library = new AvatarLibrary();
        library.Entries.Add(new AvatarLibraryEntry { AvatarId = "avd_11111" });

        var currentAvatars = new[]
        {
            new VrChatAvatarSummary("avd_11111", "Cutie", "VRChat", false, null)
        };

        library.PruneMissingEntries(currentAvatars);

        Assert.Single(library.Entries);
    }

    [Fact]
    public void PruneMissingEntries_HandlesEmptyLibrary()
    {
        var library = new AvatarLibrary();
        var currentAvatars = new[]
        {
            new VrChatAvatarSummary("avd_11111", "Cutie", "VRChat", false, null)
        };

        library.PruneMissingEntries(currentAvatars);

        Assert.Empty(library.Entries);
    }

    [Fact]
    public void PruneMissingEntries_HandlesEmptyAvatarList_KeepsEntries()
    {
        var library = new AvatarLibrary();
        library.Entries.Add(new AvatarLibraryEntry { AvatarId = "avd_11111" });

        library.PruneMissingEntries(Array.Empty<VrChatAvatarSummary>());

        Assert.Single(library.Entries);
    }
}
