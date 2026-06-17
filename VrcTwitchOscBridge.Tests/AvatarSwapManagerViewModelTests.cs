using System.Collections.Generic;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSwapManagerViewModelTests
{
    [Fact]
    public void OpenSwapEditorCommand_SetsSelectedCardAndRaisesPropertyChanged()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile
        {
            TargetAvatarId = "avtr_a",
            TargetAvatarName = "Avatar A"
        };
        profile.ChannelPointRules.Add(new TriggerRule
        {
            Name = "Test Redeem",
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_a",
            AvatarTargetName = "Avatar A"
        });
        settings.AvatarSwapProfiles.Add(profile);

        var vm = new AvatarSwapManagerViewModel(settings);

        var propertyChanges = new List<string>();
        vm.PropertyChanged += (_, e) => propertyChanges.Add(e.PropertyName ?? string.Empty);

        var card = vm.SwapCards.Single();
        vm.OpenSwapEditorCommand.Execute(card);

        Assert.True(vm.IsSwapEditorOpen);
        Assert.Same(card, vm.SelectedSwapCard);
        Assert.Contains(nameof(AvatarSwapManagerViewModel.SelectedSwapCard), propertyChanges);
        Assert.Single(vm.ChannelPointRows);
    }

    [Fact]
    public void HasAnyRules_FalseWhenAllRuleCollectionsEmpty()
    {
        var profile = new AvatarSwapProfile();
        var vm = new AvatarSwapCardViewModel(profile, new AvatarImageService());

        Assert.False(vm.HasAnyRules);
    }

    [Fact]
    public void HasAnyRules_TrueWhenChannelPointRulesPresent()
    {
        var profile = new AvatarSwapProfile();
        profile.ChannelPointRules.Add(new TriggerRule());
        var vm = new AvatarSwapCardViewModel(profile, new AvatarImageService());

        Assert.True(vm.HasAnyRules);
    }

    [Fact]
    public void HasAnyRules_TrueWhenBitsRulesPresent()
    {
        var profile = new AvatarSwapProfile();
        profile.BitsRules.Add(new TriggerRule());
        var vm = new AvatarSwapCardViewModel(profile, new AvatarImageService());

        Assert.True(vm.HasAnyRules);
    }

    [Fact]
    public void HasAnyRules_TrueWhenSubsRulesPresent()
    {
        var profile = new AvatarSwapProfile();
        profile.SubsRules.Add(new TriggerRule());
        var vm = new AvatarSwapCardViewModel(profile, new AvatarImageService());

        Assert.True(vm.HasAnyRules);
    }

    [Fact]
    public void HasAnyRules_TrueWhenPaymentRulesPresent()
    {
        var profile = new AvatarSwapProfile();
        profile.PaymentRules.Add(new TriggerRule());
        var vm = new AvatarSwapCardViewModel(profile, new AvatarImageService());

        Assert.True(vm.HasAnyRules);
    }
}
