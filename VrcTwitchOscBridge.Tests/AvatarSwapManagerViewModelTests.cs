using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using VrcTwitchOscBridge.Infrastructure;
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

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());

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
    public void AddSwapCommand_DoesNotCreateDuplicateCard()
    {
        var settings = new AppSettings();
        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());

        vm.AddSwapCommand.Execute(null);

        Assert.Single(settings.AvatarSwapProfiles);
        Assert.Single(vm.SwapCards);
    }

    [Fact]
    public void Selection_RaisesCanExecuteChangedForSelectionDependentCommands()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Avatar A" };
        settings.AvatarSwapProfiles.Add(profile);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());

        var changedCommands = new List<string>();
        vm.DeleteSwapCommand.CanExecuteChanged += (_, _) => changedCommands.Add(nameof(vm.DeleteSwapCommand));
        vm.AddChannelPointRuleCommand.CanExecuteChanged += (_, _) => changedCommands.Add(nameof(vm.AddChannelPointRuleCommand));
        vm.AddBitsRuleCommand.CanExecuteChanged += (_, _) => changedCommands.Add(nameof(vm.AddBitsRuleCommand));
        vm.AddSubsRuleCommand.CanExecuteChanged += (_, _) => changedCommands.Add(nameof(vm.AddSubsRuleCommand));
        vm.AddPaymentRuleCommand.CanExecuteChanged += (_, _) => changedCommands.Add(nameof(vm.AddPaymentRuleCommand));

        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());

        Assert.Contains(nameof(vm.DeleteSwapCommand), changedCommands);
        Assert.Contains(nameof(vm.AddChannelPointRuleCommand), changedCommands);
        Assert.Contains(nameof(vm.AddBitsRuleCommand), changedCommands);
        Assert.Contains(nameof(vm.AddSubsRuleCommand), changedCommands);
        Assert.Contains(nameof(vm.AddPaymentRuleCommand), changedCommands);
    }

    [Fact]
    public void DeleteSwapCommand_RemovesProfileAndCard()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Avatar A" };
        settings.AvatarSwapProfiles.Add(profile);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        var card = vm.SwapCards.Single();
        vm.OpenSwapEditorCommand.Execute(card);

        vm.DeleteSwapCommand.Execute(null);

        Assert.Empty(settings.AvatarSwapProfiles);
        Assert.Empty(vm.SwapCards);
        Assert.False(vm.IsSwapEditorOpen);
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

    [Fact]
    public void Constructor_ForwardsTwitchRewardSourceProperties()
    {
        var settings = new AppSettings();
        var source = new StubTwitchRewardSource();
        var option = TwitchRewardOption.Placeholder("test-reward");
        source.RewardOptions.Add(option);

        var vm = new AvatarSwapManagerViewModel(settings, source);

        Assert.Same(source.RewardOptions, vm.TwitchRewardOptions);
        Assert.Same(source.RefreshTwitchRewardsCommand, vm.RefreshTwitchRewardsCommand);
        Assert.Same(source.UnlinkTwitchRewardCommand, vm.UnlinkTwitchRewardCommand);
        Assert.Contains(option, vm.TwitchRewardOptions);
    }

    [Fact]
    public void Constructor_PropagatesRewardOptionsCollectionChanges()
    {
        var settings = new AppSettings();
        var source = new StubTwitchRewardSource();
        var vm = new AvatarSwapManagerViewModel(settings, source);

        var option = TwitchRewardOption.Placeholder("late-add");
        source.RewardOptions.Add(option);

        Assert.Contains(option, vm.TwitchRewardOptions);
    }

    private sealed class StubTwitchRewardSource : ITwitchRewardSource
    {
        public ObservableCollection<TwitchRewardOption> RewardOptions { get; } = new();
        public ICommand RefreshTwitchRewardsCommand { get; } = new RelayCommand(() => { });
        public ICommand UnlinkTwitchRewardCommand { get; } = new RelayCommand(p => { });
    }
}
