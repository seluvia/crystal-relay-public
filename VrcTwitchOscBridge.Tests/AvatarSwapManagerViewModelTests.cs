using System.Collections.Generic;
using VrcTwitchOscBridge.Models;
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
}
