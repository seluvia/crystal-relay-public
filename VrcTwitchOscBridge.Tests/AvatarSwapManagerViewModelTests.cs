using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.UserControls;
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
        profile.PaymentRules.Add(new CashPaymentRule());
        var vm = new AvatarSwapCardViewModel(profile, new AvatarImageService());

        Assert.True(vm.HasAnyRules);
    }

    [Fact]
    public void CardViewModel_RuleCountUpdatesLiveWhenRuleAdded()
    {
        var profile = new AvatarSwapProfile();
        var vm = new AvatarSwapCardViewModel(profile, new AvatarImageService());
        Assert.Equal("0", vm.RuleCountText);

        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        profile.ChannelPointRules.Add(new TriggerRule { TriggerType = TwitchTriggerType.ChannelPoints });

        Assert.Equal("1", vm.RuleCountText);
        Assert.Contains(nameof(AvatarSwapCardViewModel.RuleCountText), changed);
        Assert.Contains(nameof(AvatarSwapCardViewModel.HasAnyRules), changed);
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

    [Fact]
    public void AddPaymentRuleCommand_CreatesCashPaymentRuleNotTriggerRule()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Avatar A" };
        settings.AvatarSwapProfiles.Add(profile);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());

        vm.AddPaymentRuleCommand.Execute(null);

        Assert.Single(profile.PaymentRules);
        Assert.IsType<CashPaymentRule>(profile.PaymentRules[0]);
        var rule = (CashPaymentRule)profile.PaymentRules[0];
        Assert.Equal("New Cash Payment Swap", rule.Name);
        Assert.Equal(CashPaymentProvider.StreamElements, rule.Provider);
        Assert.True(rule.IsEnabled);
        Assert.Equal(CashPaymentActionKind.TriggerAction, rule.ActionKind);
        Assert.NotNull(rule.TriggerAction);
        Assert.Equal(OscActionType.AvatarChange, rule.TriggerAction.ActionType);
        Assert.Equal("avtr_a", rule.TriggerAction.AvatarChangeTargetId);
        Assert.Single(vm.PaymentRows);
        Assert.IsType<InlinePaymentRuleRowViewModel>(vm.PaymentRows[0]);
    }

    [Fact]
    public void SelectedRule_SetsRightPaneContent()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Avatar A" };
        profile.ChannelPointRules.Add(new TriggerRule { TriggerType = TwitchTriggerType.ChannelPoints, Name = "Test" });
        settings.AvatarSwapProfiles.Add(profile);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());
        Assert.IsType<RuleListPaneViewModel>(vm.RightPaneContent);

        var row = vm.ChannelPointRows.Single();
        row.EditCommand.Execute(null);

        Assert.Same(row, vm.SelectedRule);
        Assert.Same(row, vm.RightPaneContent);
    }

    [Fact]
    public void BackToListCommand_RestoresRuleListViewForSwapEditor()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Avatar A" };
        profile.ChannelPointRules.Add(new TriggerRule { TriggerType = TwitchTriggerType.ChannelPoints, Name = "Test" });
        settings.AvatarSwapProfiles.Add(profile);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());
        var row = vm.ChannelPointRows.Single();
        row.EditCommand.Execute(null);
        Assert.NotNull(vm.SelectedRule);
        Assert.IsNotType<RuleListPaneViewModel>(vm.RightPaneContent);

        vm.BackToListCommand.Execute(null);

        Assert.Null(vm.SelectedRule);
        var pane = Assert.IsType<RuleListPaneViewModel>(vm.RightPaneContent);
        Assert.Equal(RuleListPaneKind.Swap, pane.Kind);
    }

    [Fact]
    public void BackToListCommand_RestoresRuleListViewForRouletteEditor()
    {
        var settings = new AppSettings();
        var roulette = new AvatarRouletteProfile { Name = "My Roulette" };
        roulette.Triggers.Add(new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarChange,
            Name = "Test"
        });
        settings.AvatarRouletteProfiles.Add(roulette);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenRouletteEditorCommand.Execute(vm.RouletteCards.Single());
        var row = vm.RouletteChannelPointRows.Single();
        row.EditCommand.Execute(null);
        Assert.NotNull(vm.SelectedRule);
        Assert.IsNotType<RuleListPaneViewModel>(vm.RightPaneContent);

        vm.BackToListCommand.Execute(null);

        Assert.Null(vm.SelectedRule);
        var pane = Assert.IsType<RuleListPaneViewModel>(vm.RightPaneContent);
        Assert.Equal(RuleListPaneKind.Roulette, pane.Kind);
    }

    [Fact]
    public void DeleteRule_RemovesTypedRuleFromCollection()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Avatar A" };
        settings.AvatarSwapProfiles.Add(profile);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());

        vm.AddBitsRuleCommand.Execute(null);
        var bitsRow = vm.BitsRows.Single();

        vm.DeleteRuleCommand.Execute(bitsRow);

        Assert.Empty(profile.BitsRules);
        Assert.Empty(vm.BitsRows);
    }

    [Fact]
    public void AddBitsRuleCommand_CreatesTypedInlineBitsRuleRowViewModel()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Avatar A" };
        settings.AvatarSwapProfiles.Add(profile);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());

        vm.AddBitsRuleCommand.Execute(null);

        Assert.Single(profile.BitsRules);
        Assert.Single(vm.BitsRows);
        Assert.IsType<InlineBitsRuleRowViewModel>(vm.BitsRows[0]);
    }

    [Fact]
    public void AddSubsRuleCommand_CreatesTypedInlineSubsRuleRowViewModel()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Avatar A" };
        settings.AvatarSwapProfiles.Add(profile);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());

        vm.AddSubsRuleCommand.Execute(null);

        Assert.Single(profile.SubsRules);
        Assert.Single(vm.SubsRows);
        Assert.IsType<InlineSubsRuleRowViewModel>(vm.SubsRows[0]);
    }

    [Fact]
    public void AddChannelPointRuleCommand_CreatesTypedInlineChannelPointRuleRowViewModel()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Avatar A" };
        settings.AvatarSwapProfiles.Add(profile);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());

        vm.AddChannelPointRuleCommand.Execute(null);

        Assert.Single(profile.ChannelPointRules);
        Assert.Single(vm.ChannelPointRows);
        Assert.IsType<InlineChannelPointRuleRowViewModel>(vm.ChannelPointRows[0]);
    }

    [Fact]
    public void AddBitsRuleCommand_NewRuleHasBitsKeywordEnabledFalse()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Avatar A" };
        settings.AvatarSwapProfiles.Add(profile);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());

        vm.AddBitsRuleCommand.Execute(null);

        var rule = profile.BitsRules.Single();
        Assert.False(rule.BitsKeywordEnabled);
    }

    [Fact]
    public void AddSubsRuleCommand_NewRuleHasAllTierTogglesEnabled()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Avatar A" };
        settings.AvatarSwapProfiles.Add(profile);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());

        vm.AddSubsRuleCommand.Execute(null);

        var rule = profile.SubsRules.Single();
        Assert.True(rule.SubscriptionTier1Enabled);
        Assert.True(rule.SubscriptionTier2Enabled);
        Assert.True(rule.SubscriptionTier3Enabled);
    }

    [Fact]
    public void OpenSwapEditor_SetsRuleListPaneKindToSwap()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Avatar A" };
        settings.AvatarSwapProfiles.Add(profile);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());

        var pane = Assert.IsType<RuleListPaneViewModel>(vm.RightPaneContent);
        Assert.Equal(RuleListPaneKind.Swap, pane.Kind);
    }

    [Fact]
    public void OpenRouletteEditor_SetsRuleListPaneKindToRoulette()
    {
        var settings = new AppSettings();
        var roulette = new AvatarRouletteProfile { Name = "My Roulette" };
        settings.AvatarRouletteProfiles.Add(roulette);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenRouletteEditorCommand.Execute(vm.RouletteCards.Single());

        var pane = Assert.IsType<RuleListPaneViewModel>(vm.RightPaneContent);
        Assert.Equal(RuleListPaneKind.Roulette, pane.Kind);
    }

    [Fact]
    public void OpenRouletteEditor_ClearsSelectedSwapCard()
    {
        var settings = new AppSettings();
        settings.AvatarSwapProfiles.Add(new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Avatar A" });
        settings.AvatarRouletteProfiles.Add(new AvatarRouletteProfile { Name = "My Roulette" });

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());
        Assert.NotNull(vm.SelectedSwapCard);

        vm.OpenRouletteEditorCommand.Execute(vm.RouletteCards.Single());

        Assert.Null(vm.SelectedSwapCard);
        Assert.NotNull(vm.SelectedRouletteCard);
    }

    [Fact]
    public void SetRoulettePoolSelection_ReplacesPoolWithSelectedAvatarDetails()
    {
        var settings = new AppSettings();
        var roulette = new AvatarRouletteProfile { Name = "My Roulette" };
        roulette.Pool.Add(new RouletteAvatarEntry { AvatarId = "old", AvatarName = "Old" });
        settings.AvatarRouletteProfiles.Add(roulette);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenRouletteEditorCommand.Execute(vm.RouletteCards.Single());

        vm.SetRoulettePoolSelection(
            new[] { "avtr_b", "avtr_a", "avtr_b", "" },
            new[]
            {
                new VrChatAvatarSummary("avtr_a", "Avatar A", "VRChat", false, "thumb-a"),
                new VrChatAvatarSummary("avtr_b", "Avatar B", "VRChat", false, "thumb-b")
            });

        Assert.Collection(
            roulette.Pool,
            entry =>
            {
                Assert.Equal("avtr_b", entry.AvatarId);
                Assert.Equal("Avatar B", entry.AvatarName);
                Assert.Equal("thumb-b", entry.ThumbnailUrl);
            },
            entry =>
            {
                Assert.Equal("avtr_a", entry.AvatarId);
                Assert.Equal("Avatar A", entry.AvatarName);
                Assert.Equal("thumb-a", entry.ThumbnailUrl);
            });
    }

    [Fact]
    public void SetRoulettePoolSelection_BuildsImageRowsForSelectedAvatars()
    {
        var settings = new AppSettings();
        var roulette = new AvatarRouletteProfile { Name = "My Roulette" };
        settings.AvatarRouletteProfiles.Add(roulette);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenRouletteEditorCommand.Execute(vm.RouletteCards.Single());

        vm.SetRoulettePoolSelection(
            new[] { "avtr_a" },
            new[] { new VrChatAvatarSummary("avtr_a", "Avatar A", "VRChat", false, "thumb-a") });

        var row = Assert.Single(vm.RoulettePoolRows);
        Assert.Equal("Avatar A", row.AvatarName);
        Assert.Equal("thumb-a", row.ThumbnailUrl);
        Assert.True(row.HasImage);
        Assert.NotNull(row.Image);
    }

    [Fact]
    public void AddAdvancedTriggerCommand_ForRouletteCreatesAvatarRouletRuleAndOpensEditor()
    {
        var settings = new AppSettings();
        var roulette = new AvatarRouletteProfile { Name = "My Roulette" };
        settings.AvatarRouletteProfiles.Add(roulette);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenRouletteEditorCommand.Execute(vm.RouletteCards.Single());

        vm.AddAdvancedTriggerCommand.Execute("ChatCommand");

        var rule = Assert.Single(roulette.Triggers);
        Assert.Equal(TwitchTriggerType.ChatCommand, rule.TriggerType);
        Assert.Equal(OscActionType.AvatarRoulet, rule.ActionType);
        Assert.True(rule.ChatCommandEnabled);

        var row = Assert.IsType<InlineRouletteRuleRowViewModel>(Assert.Single(vm.RouletteAdvancedRows));
        Assert.Same(rule, row.Rule);
        Assert.Same(row, vm.SelectedRule);
        Assert.Same(row, vm.RightPaneContent);
    }

    [Fact]
    public async Task ManagedRouletteReward_UsesNewUiProfilePoolAndProfileEnabledState()
    {
        await using var vm = new MainWindowViewModel();
        vm.Settings.MasterAvatarSwapReturnId = "avtr_return";

        var roulette = new AvatarRouletteProfile { Name = "Managed Roulette" };
        roulette.Pool.Add(new RouletteAvatarEntry
        {
            AvatarId = "avtr_pool",
            AvatarName = "Pool Avatar"
        });
        var rule = new TriggerRule
        {
            Name = "Roulette Reward",
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarRoulet,
            RewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            ChannelPointRewardTitle = "Roulette Reward",
            ChannelPointRewardCost = 100
        };
        roulette.Triggers.Add(rule);

        var method = typeof(MainWindowViewModel).GetMethod(
            "CreateManagedRewardTargetForRouletteRule",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);

        var target = method.Invoke(vm, new object[]
        {
            roulette,
            rule,
            "avtr_return",
            false,
            true,
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>()
        });
        Assert.NotNull(target);

        var desiredEnabled = target.GetType().GetProperty("DesiredEnabled");
        Assert.NotNull(desiredEnabled);
        Assert.True((bool)desiredEnabled.GetValue(target)!);

        roulette.IsEnabled = false;
        var disabledTarget = method.Invoke(vm, new object[]
        {
            roulette,
            rule,
            "avtr_return",
            false,
            true,
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>()
        });
        Assert.NotNull(disabledTarget);
        Assert.False((bool)desiredEnabled.GetValue(disabledTarget)!);
    }

    [Fact]
    public void RuleListPaneViewModel_StoresKindAndTitle()
    {
        var pane = new RuleListPaneViewModel(RuleListPaneKind.Swap, "Avatar Name");
        Assert.Equal(RuleListPaneKind.Swap, pane.Kind);
        Assert.Equal("Avatar Name", pane.Title);
    }

    private sealed class StubTwitchRewardSource : ITwitchRewardSource
    {
        public ObservableCollection<TwitchRewardOption> RewardOptions { get; } = new();
        public ObservableCollection<TwitchPowerUpOption> PowerUpOptions { get; } = new();
        public ICommand RefreshTwitchRewardsCommand { get; } = new RelayCommand(() => { });
        public ICommand UnlinkTwitchRewardCommand { get; } = new RelayCommand(p => { });
    }
}
