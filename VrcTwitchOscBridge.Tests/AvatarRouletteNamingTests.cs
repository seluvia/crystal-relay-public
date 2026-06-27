using System.ComponentModel;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarRouletteNamingTests
{
    [Fact]
    public void Profile_Name_Setter_RaisesPropertyChanged()
    {
        var profile = new AvatarRouletteProfile { Name = "New Roulette" };

        var fired = new System.Collections.Generic.List<string?>();
        ((INotifyPropertyChanged)profile).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        profile.Name = "Lucky Wheel";

        Assert.Contains(nameof(AvatarRouletteProfile.Name), fired);
        Assert.Equal("Lucky Wheel", profile.Name);
    }

    [Fact]
    public void CardViewModel_RaisesName_WhenProfileNameChanges()
    {
        var profile = new AvatarRouletteProfile { Name = "New Roulette" };
        var card = new AvatarRouletteCardViewModel(profile);

        var fired = new System.Collections.Generic.List<string?>();
        ((INotifyPropertyChanged)card).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        profile.Name = "Crazy Wheel";

        Assert.Contains(nameof(AvatarRouletteCardViewModel.Name), fired);
        Assert.Equal("Crazy Wheel", card.Name);
    }

    [Fact]
    public void CardViewModel_ExposesFirstFourPoolPreviewRows()
    {
        var profile = new AvatarRouletteProfile { Name = "Preview Roulette" };
        for (var i = 1; i <= 5; i++)
        {
            profile.Pool.Add(new RouletteAvatarEntry
            {
                AvatarId = $"avtr_{i}",
                AvatarName = $"Avatar {i}",
                ThumbnailUrl = $"thumb-{i}"
            });
        }

        var card = new AvatarRouletteCardViewModel(profile, new AvatarImageService());

        Assert.Equal(4, card.PreviewRows.Count);
        Assert.Equal(new[] { "avtr_1", "avtr_2", "avtr_3", "avtr_4" }, card.PreviewRows.Select(row => row.AvatarId).ToArray());
        Assert.All(card.PreviewRows, row => Assert.True(row.HasImage));
    }
}
