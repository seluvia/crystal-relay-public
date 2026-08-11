using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
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
    public void DeleteSwapCommand_ReportsRemovedChannelPointRulesForRetirement()
    {
        var settings = new AppSettings();
        var removedRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            RewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            ChannelPointRewardId = "removed-swap-reward"
        };
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Avatar A" };
        profile.ChannelPointRules.Add(removedRule);
        settings.AvatarSwapProfiles.Add(profile);
        var retiredRules = new List<TriggerRule>();
        var vm = new AvatarSwapManagerViewModel(
            settings,
            new StubTwitchRewardSource(),
            onChannelPointRulesRemoved: rules => retiredRules.AddRange(rules));

        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());
        vm.DeleteSwapCommand.Execute(null);

        Assert.Single(retiredRules);
        Assert.Same(removedRule, retiredRules[0]);
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
    public void ChannelPointRewardCostChange_NotifiesSettingsChangedForManagedRewardSync()
    {
        var settings = new AppSettings();
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ChannelPointRewardCost = 100
        };
        var profile = new AvatarSwapProfile();
        profile.ChannelPointRules.Add(rule);
        settings.AvatarSwapProfiles.Add(profile);
        var settingsChangedCount = 0;
        var managedRewardSyncCount = 0;

        _ = new AvatarSwapManagerViewModel(
            settings,
            new StubTwitchRewardSource(),
            onSettingsChanged: () => settingsChangedCount++,
            onManagedRewardSyncRequested: () => managedRewardSyncCount++);

        rule.ChannelPointRewardCost = 250;

        Assert.Equal(1, settingsChangedCount);
        Assert.Equal(1, managedRewardSyncCount);
    }

    [Fact]
    public void StandaloneAdvancedCommandEdit_RefreshesSettingsWithoutManagedRewardSync()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_target" };
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChatCommand,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target",
            ChatCommandEnabled = true,
            ChatCommandText = "!swap"
        };
        profile.AdvancedRules.Add(rule);
        settings.AvatarSwapProfiles.Add(profile);
        var settingsChangedCount = 0;
        var managedRewardSyncCount = 0;
        var vm = new AvatarSwapManagerViewModel(
            settings,
            new StubTwitchRewardSource(),
            onSettingsChanged: () => settingsChangedCount++,
            onManagedRewardSyncRequested: () => managedRewardSyncCount++);

        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());
        rule.ChatCommandText = "!new-swap";
        vm.SaveSwapEditorCommand.Execute(null);

        Assert.True(settingsChangedCount > 0);
        Assert.Equal(0, managedRewardSyncCount);
    }

    [Fact]
    public void SaveSwapEditor_ChannelPointProfileChangeRequestsManagedRewardSync()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_target" };
        profile.ChannelPointRules.Add(new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target",
            ChannelPointRewardTitle = "Swap"
        });
        settings.AvatarSwapProfiles.Add(profile);
        var managedRewardSyncCount = 0;
        var vm = new AvatarSwapManagerViewModel(
            settings,
            new StubTwitchRewardSource(),
            onManagedRewardSyncRequested: () => managedRewardSyncCount++);

        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());
        profile.IsEnabled = false;
        vm.SaveSwapEditorCommand.Execute(null);

        Assert.Equal(1, managedRewardSyncCount);
    }

    [Fact]
    public void SaveRouletteEditor_ChannelPointProfileChangeRequestsManagedRewardSync()
    {
        var settings = new AppSettings();
        var roulette = new AvatarRouletteProfile { Name = "Roulette" };
        roulette.Triggers.Add(new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarRoulet,
            AvatarRouletAvatarIds = { "avtr_target" },
            ChannelPointRewardTitle = "Roulette"
        });
        settings.AvatarRouletteProfiles.Add(roulette);
        var managedRewardSyncCount = 0;
        var vm = new AvatarSwapManagerViewModel(
            settings,
            new StubTwitchRewardSource(),
            onManagedRewardSyncRequested: () => managedRewardSyncCount++);

        vm.OpenRouletteEditorCommand.Execute(vm.RouletteCards.Single());
        roulette.IsEnabled = false;
        vm.SaveRouletteEditorCommand.Execute(null);

        Assert.Equal(1, managedRewardSyncCount);
    }

    [Fact]
    public async Task NormalizeChatCommandFallbackRules_DisablesDuplicateAdvancedAvatarSwapCommand()
    {
        await using var vm = new MainWindowViewModel();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_target" };
        var channelPointRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ChatCommandEnabled = true,
            ChatCommandText = "!swap"
        };
        var advancedRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChatCommand,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target",
            ChatCommandEnabled = true,
            ChatCommandText = "swap"
        };
        profile.ChannelPointRules.Add(channelPointRule);
        profile.AdvancedRules.Add(advancedRule);
        vm.Settings.AvatarSwapProfiles.Add(profile);

        var normalizeMethod = typeof(MainWindowViewModel).GetMethod(
            "NormalizeChatCommandFallbackRules",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(normalizeMethod);

        Assert.True(Assert.IsType<bool>(normalizeMethod!.Invoke(vm, null)));
        Assert.False(advancedRule.ChatCommandEnabled);
        Assert.Equal(string.Empty, advancedRule.ChatCommandText);
    }

    [Fact]
    public void PowerUpLinkChange_NotifiesSettingsChangedForRuntimeRefresh()
    {
        var settings = new AppSettings();
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.PowerUp,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target"
        };
        var profile = new AvatarSwapProfile();
        profile.PowerUpRules.Add(rule);
        settings.AvatarSwapProfiles.Add(profile);
        var settingsChangedCount = 0;

        _ = new AvatarSwapManagerViewModel(
            settings,
            new StubTwitchRewardSource(),
            onSettingsChanged: () => settingsChangedCount++);

        rule.PowerUpId = "power-up-1";

        Assert.Equal(1, settingsChangedCount);
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
                new VrChatAvatarSummary(
                    Id: "avtr_a", Name: "Avatar A", AuthorName: "", ThumbnailUrl: "thumb-a",
                    IsCurrentAvatar: false, IsUploaded: false, IsFavorited: false, IsLicensed: false,
                    Platform: "", StyleTags: Array.Empty<string>(), ContentTags: Array.Empty<string>(),
                    FavoriteGroupName: null),
                new VrChatAvatarSummary(
                    Id: "avtr_b", Name: "Avatar B", AuthorName: "", ThumbnailUrl: "thumb-b",
                    IsCurrentAvatar: false, IsUploaded: false, IsFavorited: false, IsLicensed: false,
                    Platform: "", StyleTags: Array.Empty<string>(), ContentTags: Array.Empty<string>(),
                    FavoriteGroupName: null)
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
            new[]
            {
                new VrChatAvatarSummary(
                    Id: "avtr_a", Name: "Avatar A", AuthorName: "", ThumbnailUrl: "thumb-a",
                    IsCurrentAvatar: false, IsUploaded: false, IsFavorited: false, IsLicensed: false,
                    Platform: "", StyleTags: Array.Empty<string>(), ContentTags: Array.Empty<string>(),
                    FavoriteGroupName: null)
            });

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
    public void AddAdvancedTriggerCommand_ForSwapCreatesStandaloneChatCommand()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile
        {
            TargetAvatarId = "avtr_target",
            TargetAvatarName = "Target"
        };
        settings.AvatarSwapProfiles.Add(profile);
        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());

        vm.AddAdvancedTriggerCommand.Execute("ChatCommand");

        var rule = Assert.Single(profile.AdvancedRules);
        Assert.Empty(profile.ChannelPointRules);
        Assert.Equal(TwitchTriggerType.ChatCommand, rule.TriggerType);
        Assert.Equal(OscActionType.AvatarChange, rule.ActionType);
        Assert.Equal("!swap", rule.ChatCommandText);
        Assert.True(rule.ChatCommandEnabled);
        var row = Assert.IsType<InlineRouletteRuleRowViewModel>(Assert.Single(vm.AdvancedRows));
        Assert.Same(rule, row.Rule);
        Assert.Same(row, vm.SelectedRule);
    }

    [Fact]
    public void DeleteRule_RemovesStandaloneAdvancedTriggerWithoutRewardRetirement()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_target" };
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChatCommand,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target",
            ChatCommandEnabled = true,
            ChatCommandText = "!swap"
        };
        profile.AdvancedRules.Add(rule);
        settings.AvatarSwapProfiles.Add(profile);
        var retired = new List<TriggerRule>();
        var vm = new AvatarSwapManagerViewModel(
            settings,
            new StubTwitchRewardSource(),
            onChannelPointRulesRemoved: rules => retired.AddRange(rules));
        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());

        var row = Assert.Single(vm.AdvancedRows);
        vm.DeleteRuleCommand.Execute(row);

        Assert.Empty(profile.AdvancedRules);
        Assert.Empty(retired);
    }

    [Fact]
    public void ExternalAdvancedRuleChanges_RefreshSelectedRowsWithoutDuplicates()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_target" };
        settings.AvatarSwapProfiles.Add(profile);
        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());

        var firstRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChatCommand,
            ActionType = OscActionType.AvatarChange,
            ChatCommandEnabled = true,
            ChatCommandText = "!first"
        };
        var secondRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.Follow,
            ActionType = OscActionType.AvatarChange
        };

        profile.AdvancedRules.Add(firstRule);
        var selectedRowBeforeRefresh = Assert.Single(vm.AdvancedRows, row => ReferenceEquals(row.Rule, firstRule));
        selectedRowBeforeRefresh.EditCommand.Execute(null);

        profile.AdvancedRules.Add(secondRule);

        Assert.Equal(2, vm.AdvancedRows.Count);
        Assert.Same(firstRule, vm.AdvancedRows[0].Rule);
        Assert.Same(secondRule, vm.AdvancedRows[1].Rule);

        var selectedReplacement = Assert.IsType<InlineRouletteRuleRowViewModel>(vm.SelectedRule);
        Assert.Same(firstRule, selectedReplacement.Rule);
        Assert.Same(selectedReplacement, vm.RightPaneContent);

        profile.AdvancedRules.Remove(firstRule);

        var row = Assert.Single(vm.AdvancedRows);
        Assert.Same(secondRule, row.Rule);

        profile.AdvancedRules.Clear();

        Assert.Empty(vm.AdvancedRows);
    }

    [Fact]
    public void ClearingUnselectedAdvancedRules_UnsubscribesRemovedRules()
    {
        var settings = new AppSettings();
        var selectedProfile = new AvatarSwapProfile { TargetAvatarId = "avtr_selected" };
        var unselectedProfile = new AvatarSwapProfile { TargetAvatarId = "avtr_unselected" };
        settings.AvatarSwapProfiles.Add(selectedProfile);
        settings.AvatarSwapProfiles.Add(unselectedProfile);
        var settingsChangedCount = 0;
        var vm = new AvatarSwapManagerViewModel(
            settings,
            new StubTwitchRewardSource(),
            onSettingsChanged: () => settingsChangedCount++);
        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single(card => card.Profile == selectedProfile));

        var removedRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChatCommand,
            ActionType = OscActionType.AvatarChange,
            ChatCommandEnabled = true,
            ChatCommandText = "!removed"
        };
        unselectedProfile.AdvancedRules.Add(removedRule);
        unselectedProfile.AdvancedRules.Clear();

        settingsChangedCount = 0;
        removedRule.ChatCommandText = "!stale";

        Assert.Equal(0, settingsChangedCount);
    }

    [Fact]
    public void RemovingSelectedAdvancedRule_ClearsEditorAndRestoresSwapRuleList()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_target", TargetAvatarName = "Target" };
        var rule = new TriggerRule
        {
            Name = "Selected Advanced Rule",
            TriggerType = TwitchTriggerType.ChatCommand,
            ActionType = OscActionType.AvatarChange,
            ChatCommandEnabled = true,
            ChatCommandText = "!swap"
        };
        profile.AdvancedRules.Add(rule);
        settings.AvatarSwapProfiles.Add(profile);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());
        var selectedRow = Assert.Single(vm.AdvancedRows);
        selectedRow.EditCommand.Execute(null);

        profile.AdvancedRules.Remove(rule);

        Assert.Null(vm.SelectedRule);
        var pane = Assert.IsType<RuleListPaneViewModel>(vm.RightPaneContent);
        Assert.Equal(RuleListPaneKind.Swap, pane.Kind);
    }

    [Fact]
    public void ClearingSelectedAdvancedRules_ClearsEditorAndRestoresSwapRuleList()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_target", TargetAvatarName = "Target" };
        var rule = new TriggerRule
        {
            Name = "Selected Advanced Rule",
            TriggerType = TwitchTriggerType.ChatCommand,
            ActionType = OscActionType.AvatarChange,
            ChatCommandEnabled = true,
            ChatCommandText = "!swap"
        };
        profile.AdvancedRules.Add(rule);
        settings.AvatarSwapProfiles.Add(profile);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());
        var selectedRow = Assert.Single(vm.AdvancedRows);
        selectedRow.EditCommand.Execute(null);

        profile.AdvancedRules.Clear();

        Assert.Null(vm.SelectedRule);
        var pane = Assert.IsType<RuleListPaneViewModel>(vm.RightPaneContent);
        Assert.Equal(RuleListPaneKind.Swap, pane.Kind);
    }

    [Fact]
    public void RebuildingAdvancedRows_DetachesStaleRowsAndKeepsCurrentRowsSynchronized()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_target" };
        var firstRule = new TriggerRule
        {
            Name = "First Rule",
            TriggerType = TwitchTriggerType.ChatCommand,
            ActionType = OscActionType.AvatarChange
        };
        profile.AdvancedRules.Add(firstRule);
        settings.AvatarSwapProfiles.Add(profile);

        var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
        vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());
        var staleRow = Assert.Single(vm.AdvancedRows);
        var initialSummary = staleRow.Summary;

        profile.AdvancedRules.Add(new TriggerRule
        {
            Name = "Second Rule",
            TriggerType = TwitchTriggerType.Follow,
            ActionType = OscActionType.AvatarChange
        });

        var currentRow = Assert.Single(vm.AdvancedRows, row => ReferenceEquals(row.Rule, firstRule));
        firstRule.Name = "Updated First Rule";

        Assert.Equal(initialSummary, staleRow.Summary);
        Assert.Contains("Updated First Rule", currentRow.Summary);
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
    public async Task LinkedRouletteReward_OwnershipProtectsSavedIdFromStaleCleanup()
    {
        await using var vm = new MainWindowViewModel();
        var roulette = new AvatarRouletteProfile { Name = "Linked Roulette" };
        var rule = new TriggerRule
        {
            Name = "Linked Roulette Reward",
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarRoulet,
            RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            ChannelPointRewardId = "linked-roulette-reward",
            ChannelPointRewardTitle = "Linked Roulette Reward",
            ChannelPointRewardCost = 100
        };
        roulette.Triggers.Add(rule);
        vm.Settings.AvatarRouletteProfiles.Add(roulette);

        var ownershipEntrySource = InvokeManagedRewardOwnershipEntries(vm);
        var ownershipEntries = AsObjects(ownershipEntrySource);
        var ownershipEntry = Assert.Single(
            ownershipEntries,
            entry => GetPropertyValue<Guid>(entry, "Id") == rule.Id);

        Assert.Equal(rule.ChannelPointRewardId, GetPropertyValue<string>(ownershipEntry, "RewardId"));
        Assert.Equal(rule.ChannelPointRewardTitle, GetPropertyValue<string>(ownershipEntry, "RewardTitle"));
        Assert.Equal(
            TwitchRewardSyncMode.LinkExisting,
            GetPropertyValue<TwitchRewardSyncMode>(ownershipEntry, "RewardSyncMode"));

        var claimedRewardIds = ownershipEntries
            .Select(entry => GetPropertyValue<string>(entry, "RewardId").Trim())
            .Where(rewardId => !string.IsNullOrWhiteSpace(rewardId))
            .ToHashSet(StringComparer.Ordinal);
        var catalog = CreateManagedRewardCatalog(new TwitchApiClient.CustomRewardResponse
        {
            Id = rule.ChannelPointRewardId,
            Title = ManagedRewardPresentation.BuildTitle(rule.ChannelPointRewardTitle, includePrefix: true),
            Cost = rule.ChannelPointRewardCost,
            IsEnabled = false
        });
        var getStaleRewards = GetInstanceMethod(catalog.GetType(), "GetStaleRewards");
        var staleRewards = AsObjects(getStaleRewards.Invoke(
            catalog,
            new object[]
            {
                claimedRewardIds,
                Array.Empty<string>(),
                new[] { rule.ChannelPointRewardId },
                Array.Empty<string>()
            }));

        Assert.Empty(staleRewards);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetireManagedRewards_PreservesDeletionConsent(bool deleteWhenInactive)
    {
        await using var vm = new MainWindowViewModel();
        var rule = new TriggerRule
        {
            RewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            ChannelPointRewardId = "retired-reward-id",
            DeleteManagedRewardWhenInactive = deleteWhenInactive
        };

        var retireMethod = typeof(MainWindowViewModel)
            .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(method => method.Name == "RetireManagedRewards"
                && method.GetParameters() is [{ ParameterType: { IsGenericType: true } parameterType }]
                && parameterType.GetGenericArguments()[0] == typeof(TriggerRule));
        retireMethod.Invoke(vm, new object[] { new[] { rule } });

        var retiredField = typeof(MainWindowViewModel).GetField(
            "retiredManagedRewardIds",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(retiredField);
        var retiredEntries = Assert.IsAssignableFrom<System.Collections.IEnumerable>(retiredField.GetValue(vm));
        var retiredEntry = Assert.Single(retiredEntries.Cast<object>());
        var retiredRecord = retiredEntry.GetType().GetProperty("Value")?.GetValue(retiredEntry) ?? retiredEntry;
        var consentProperty = retiredRecord.GetType().GetProperty("DeleteWhenInactive");
        Assert.NotNull(consentProperty);
        Assert.Equal(deleteWhenInactive, consentProperty.GetValue(retiredRecord));
    }

    [Fact]
    public async Task ManagedRewardSync_ExactIdConflictWithLinkedOwnerClearsManagedIdWithoutMutation()
    {
        await using var vm = new MainWindowViewModel();
        var handler = InstallRecordingTwitchHandler(vm);
        var linkedRule = CreateConfiguredUniversalRewardRule(
            "shared-reward-id",
            "Linked Reward",
            TwitchRewardSyncMode.LinkExisting);
        var managedRule = CreateConfiguredUniversalRewardRule(
            "shared-reward-id",
            "Managed Reward",
            TwitchRewardSyncMode.CreateOrManage);
        var target = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "CreateManagedRewardTargetForUniversalTrigger").Invoke(
                vm,
                new object[] { managedRule, true, string.Empty });
        Assert.NotNull(target);

        var result = await SynchronizeManagedRewardsAsync(
            vm,
            [target],
            CreateManagedRewardCatalog(new TwitchApiClient.CustomRewardResponse
            {
                Id = "shared-reward-id",
                Title = "Linked Reward",
                Cost = 100,
                IsEnabled = true,
                BackgroundColor = ManagedRewardPresentation.ReadyBackgroundColor
            }),
            InvokeManagedRewardOwnershipEntries(vm, [linkedRule, managedRule]));

        Assert.True(result.Changed);
        Assert.Empty(managedRule.RewardId);
        Assert.Equal("shared-reward-id", linkedRule.RewardId);
        Assert.Equal(0, result.Creates);
        Assert.Equal(0, result.Updates);
        Assert.Equal(0, result.Deletes);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UniversalManagedReward_IsNotMaterializedWhenCurrentAvatarLacksRequiredParameter()
    {
        await using var vm = new MainWindowViewModel();
        var rule = CreateConfiguredUniversalRewardRule(
            string.Empty,
            "Missing Avatar Parameter",
            TwitchRewardSyncMode.CreateOrManage);
        rule.Actions.Clear();
        rule.Actions.Add(new UniversalTriggerAction
        {
            OscAddress = "/avatar/parameters/Missing",
            TargetValue = "true"
        });

        var target = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "CreateManagedRewardTargetForUniversalTrigger").Invoke(
                vm,
                new object[] { rule, true, "avtr_missing_parameter" });

        Assert.Null(target);
    }

    [Fact]
    public async Task UniversalManagedReward_RequiresEveryAvatarParameterForVisibility()
    {
        await using var vm = new MainWindowViewModel();
        var rule = CreateConfiguredUniversalRewardRule(
            string.Empty,
            "Multiple Avatar Parameters",
            TwitchRewardSyncMode.CreateOrManage);
        rule.Actions.Clear();
        rule.Actions.Add(new UniversalTriggerAction
        {
            OscAddress = "/avatar/parameters/Present",
            TargetValue = "true"
        });
        rule.Actions.Add(new UniversalTriggerAction
        {
            OscAddress = "/avatar/parameters/Missing",
            TargetValue = "true"
        });

        var cacheField = typeof(MainWindowViewModel).GetField(
            "cachedVrChatParametersByAvatarId",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(cacheField);
        var cache = cacheField.GetValue(vm);
        var indexer = cache!.GetType().GetProperty("Item");
        Assert.NotNull(indexer);
        indexer.SetValue(
            cache,
            new List<VrChatOscParameterSummary>
            {
                new("/avatar/parameters/Present", "Present", OscParameterType.Bool)
            },
            new object[] { "avtr_partial_parameters" });

        var ready = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "IsUniversalTriggerReadyForCurrentAvatarJson").Invoke(
                vm,
                new object[] { rule, "avtr_partial_parameters" });

        Assert.False(Assert.IsType<bool>(ready));
    }

    [Fact]
    public async Task UniversalManagedReward_DoesNotReplaceStaleIdWhenCurrentAvatarLacksRequiredParameter()
    {
        await using var vm = new MainWindowViewModel();
        var handler = InstallRecordingTwitchHandler(vm);
        var rule = CreateConfiguredUniversalRewardRule(
            "stale-universal-reward",
            "Missing Avatar Parameter",
            TwitchRewardSyncMode.CreateOrManage);
        rule.Actions.Clear();
        rule.Actions.Add(new UniversalTriggerAction
        {
            OscAddress = "/avatar/parameters/Missing",
            TargetValue = "true"
        });

        var target = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "CreateManagedRewardTargetForUniversalTrigger").Invoke(
                vm,
                new object[] { rule, true, "avtr_missing_parameter" });
        Assert.NotNull(target);

        var result = await SynchronizeManagedRewardsAsync(
            vm,
            [target],
            CreateManagedRewardCatalog(),
            InvokeManagedRewardOwnershipEntries(vm, [rule]),
            allowInactiveRewardDeletion: false);

        Assert.False(result.Changed);
        Assert.Equal(0, result.Creates);
        Assert.Empty(handler.Requests);
        Assert.Equal("stale-universal-reward", rule.RewardId);
    }

    [Fact]
    public async Task UniversalManagedReward_DeletesOptedInDisabledRewardDuringSettingsEdit()
    {
        await using var vm = new MainWindowViewModel();
        var handler = InstallRecordingTwitchHandler(vm);
        var rule = CreateConfiguredUniversalRewardRule(
            "disabled-universal-reward",
            "Disabled Universal Reward",
            TwitchRewardSyncMode.CreateOrManage);
        rule.IsEnabled = false;
        rule.DeleteManagedRewardWhenInactive = true;
        var target = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "CreateManagedRewardTargetForUniversalTrigger").Invoke(
                vm,
                new object[] { rule, true, string.Empty });
        Assert.NotNull(target);

        var result = await SynchronizeManagedRewardsAsync(
            vm,
            [target],
            CreateManagedRewardCatalog(new TwitchApiClient.CustomRewardResponse
            {
                Id = rule.RewardId,
                Title = ManagedRewardPresentation.BuildTitle(rule.RewardTitle, includePrefix: true),
                Cost = rule.RewardCost,
                IsEnabled = true,
                BackgroundColor = ManagedRewardPresentation.ReadyBackgroundColor
            }),
            InvokeManagedRewardOwnershipEntries(vm, [rule]),
            allowInactiveRewardDeletion: false);

        Assert.True(result.Changed);
        Assert.Equal(1, result.Deletes);
        Assert.Equal(0, result.Updates);
        Assert.Empty(rule.RewardId);
        Assert.Contains(handler.Requests, request => request.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task SharedSetTriggerOwnership_ProtectsDivergentPersistedChildMirrorId()
    {
        await using var vm = new MainWindowViewModel();
        var profile = new AvatarTriggerProfile
        {
            AvatarId = "avtr_test",
            UseSharedNumberedOutfitReward = true,
            SetTriggerMasterRewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            SetTriggerMasterRewardId = "set-trigger-master-id",
            SetTriggerMasterRewardTitle = "Set Trigger Master"
        };
        var firstChild = CreateSetTriggerMasterChild("set-trigger-master-id", choiceNumber: 1);
        var divergentChild = CreateSetTriggerMasterChild("divergent-child-id", choiceNumber: 2);
        profile.ChannelPointRules.Add(firstChild);
        profile.ChannelPointRules.Add(divergentChild);
        vm.Settings.AvatarProfiles.Add(profile);

        var ownershipEntries = AsObjects(InvokeManagedRewardOwnershipEntries(vm));

        Assert.Contains(
            ownershipEntries,
            entry => GetPropertyValue<string>(entry, "RewardId") == "divergent-child-id");
    }

    [Fact]
    public async Task SharedSetTriggerManagedMaster_DoesNotReuseLinkedChildId()
    {
        await using var vm = new MainWindowViewModel();
        var profile = new AvatarTriggerProfile
        {
            AvatarId = "avtr_test",
            UseSharedNumberedOutfitReward = true,
            SetTriggerMasterRewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            SetTriggerMasterRewardId = "linked-child-id",
            SetTriggerMasterRewardTitle = "Set Trigger Master"
        };
        var linkedChild = CreateSetTriggerMasterChild("linked-child-id", choiceNumber: 1);
        profile.ChannelPointRules.Add(linkedChild);
        vm.Settings.AvatarProfiles.Add(profile);

        var target = CreateSharedAvatarSetTarget(vm, profile, linkedChild);
        var knownIds = Assert.IsAssignableFrom<IEnumerable<string>>(GetInstanceMethod(
                typeof(MainWindowViewModel),
                "BuildKnownManagedRewardIds").Invoke(
                vm,
                new object[] { InvokeManagedRewardOwnershipEntries(vm) }));

        Assert.Empty(GetPropertyValue<string>(target, "RewardId"));
        Assert.DoesNotContain("linked-child-id", knownIds);
    }

    [Fact]
    public async Task ManualVrcPrefixReward_IsNotAnAdoptionOrRecycleCandidate()
    {
        await using var vm = new MainWindowViewModel();
        var catalog = CreateManagedRewardCatalog(new TwitchApiClient.CustomRewardResponse
        {
            Id = "manual-vrc-reward-id",
            Title = "VRC: Manual Reward",
            Cost = 100,
            IsEnabled = false
        });

        var ownershipIndex = CreateNestedInstance(
            "ManagedRewardRuleOwnershipIndex",
            InvokeManagedRewardOwnershipEntries(vm));
        var findAdoptable = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "FindAdoptableExistingManagedReward");
        var adopted = findAdoptable.Invoke(
            vm,
            new object[]
            {
                Guid.NewGuid(),
                "Manual Reward",
                catalog,
                ownershipIndex,
                Array.Empty<string>()
            });

        var findReclaimable = GetInstanceMethod(catalog.GetType(), "FindFirstCapReclaimCandidate");
        var reclaimed = findReclaimable.Invoke(
            catalog,
            new object[]
            {
                new[] { "manual-vrc-reward-id" },
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>()
            });

        Assert.Null(adopted);
        Assert.Null(reclaimed);
    }

    [Fact]
    public async Task CooldownColorChange_DoesNotPatchLinkedReward()
    {
        await using var vm = new MainWindowViewModel();
        var rule = CreateConfiguredUniversalRewardRule(
            "linked-cooldown-id",
            "Linked Cooldown Reward",
            TwitchRewardSyncMode.LinkExisting);
        vm.Settings.UniversalTriggers.Add(rule);

        var ownershipMethod = typeof(MainWindowViewModel).GetMethod(
            "IsCreateOrManageRewardOwner",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(ownershipMethod);

        Assert.False(Assert.IsType<bool>(ownershipMethod.Invoke(vm, new object[] { rule.Id })));
    }

    [Fact]
    public async Task CooldownColorPatch_RevalidatesModeAndRewardId()
    {
        await using var vm = new MainWindowViewModel();
        var rule = CreateConfiguredUniversalRewardRule(
            "managed-cooldown-id",
            "Managed Cooldown Reward",
            TwitchRewardSyncMode.CreateOrManage);
        vm.Settings.UniversalTriggers.Add(rule);

        var authorizationMethod = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "IsManagedRewardColorPatchStillAuthorized");

        Assert.True(Assert.IsType<bool>(authorizationMethod.Invoke(
            vm,
            new object[] { rule.Id, "managed-cooldown-id" })));

        rule.RewardSyncMode = TwitchRewardSyncMode.LinkExisting;
        Assert.False(Assert.IsType<bool>(authorizationMethod.Invoke(
            vm,
            new object[] { rule.Id, "managed-cooldown-id" })));

        rule.RewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
        rule.RewardId = "different-managed-id";
        Assert.False(Assert.IsType<bool>(authorizationMethod.Invoke(
            vm,
            new object[] { rule.Id, "managed-cooldown-id" })));
    }

    [Fact]
    public async Task CapacityRecycle_FailedPatchKeepsOldOwnerAndNewTargetUnchanged()
    {
        await using var vm = new MainWindowViewModel();
        var handler = InstallRecordingTwitchHandler(vm);
        var oldRule = CreateConfiguredUniversalRewardRule(
            "recyclable-reward-id",
            "Old Managed Reward",
            TwitchRewardSyncMode.CreateOrManage);
        vm.Settings.UniversalTriggers.Add(oldRule);
        var target = CreateManagedRewardSyncTarget(
            string.Empty,
            "New Managed Reward",
            TwitchRewardSyncMode.CreateOrManage,
            protectFromCapReclaim: true);
        var targets = CreateTypedArray([target]);
        var candidate = new TwitchApiClient.CustomRewardResponse
        {
            Id = oldRule.RewardId,
            Title = "VRC: Old Managed Reward",
            Cost = 100,
            IsEnabled = false,
            BackgroundColor = ManagedRewardPresentation.ReadyBackgroundColor
        };

        await Assert.ThrowsAnyAsync<Exception>(() => TryRecycleManagedRewardForCapacityAsync(
            vm,
            target,
            targets,
            CreateManagedRewardCatalog(candidate),
            CreateManagedRewardCatalog(candidate),
            Array.Empty<string>(),
            InvokeManagedRewardOwnershipEntries(vm, [oldRule])));

        Assert.Equal("recyclable-reward-id", oldRule.RewardId);
        Assert.Empty(GetPropertyValue<string>(target, "RewardId"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task UniversalEditorCancel_RestoresAllPersistedRuleAndActionFields()
    {
        await using var vm = new MainWindowViewModel();
        var rule = CreateConfiguredUniversalRewardRule(
            "original-reward-id",
            "Original Reward",
            TwitchRewardSyncMode.CreateOrManage);
        var originalAction = new UniversalTriggerAction
        {
            Id = Guid.NewGuid(),
            OscAddress = "/avatar/parameters/Original",
            ValueKind = UniversalTriggerValueKind.Float,
            TargetValue = "0.5",
            DefaultValue = "0.1",
            DurationSeconds = 2.5,
            AddToQueue = false,
            ImportGroupKey = "original-group"
        };
        rule.Actions.Clear();
        rule.Actions.Add(originalAction);
        rule.IsEnabled = false;
        rule.Name = "Original Name";
        rule.ChatCommandEnabled = true;
        rule.CommandText = "!original";
        rule.ChatCommandPermission = ChatCommandPermission.Moderators;
        rule.RewardDescription = "Original description";
        rule.RewardCost = 250;
        rule.RewardCooldownSeconds = 30;
        rule.ManagedRewardReadyColor = "#123456";
        rule.ManagedRewardCooldownColor = "#654321";
        rule.DeleteManagedRewardWhenInactive = true;
        rule.MinimumBits = 10;
        rule.MaximumBits = 500;
        rule.SubscriptionTier = "1000";
        rule.MinimumMonths = 2;
        rule.MaximumMonths = 8;
        rule.GlobalDelaySeconds = 4;
        rule.UserDelaySeconds = 6;
        rule.ExecuteRandomAction = true;
        rule.ImportSource = "original-source";
        rule.ImportIdentity = "original-identity";
        vm.Settings.UniversalTriggers.Add(rule);

        using var manager = new UniversalTriggersManagerViewModel(vm.Settings, vm);
        manager.OpenEditorCommand.Execute(rule);

        rule.IsEnabled = true;
        rule.Name = "Changed Name";
        rule.TriggerType = UniversalTriggerType.Bits;
        rule.ChatCommandEnabled = false;
        rule.CommandText = "!changed";
        rule.ChatCommandPermission = ChatCommandPermission.Everyone;
        rule.RewardId = "changed-reward-id";
        rule.RewardTitle = "Changed Reward";
        rule.RewardDescription = "Changed description";
        rule.RewardCost = 999;
        rule.RewardCooldownSeconds = 1;
        rule.RewardSyncMode = TwitchRewardSyncMode.LinkExisting;
        rule.ManagedRewardReadyColor = "#ABCDEF";
        rule.ManagedRewardCooldownColor = "#FEDCBA";
        rule.DeleteManagedRewardWhenInactive = false;
        rule.MinimumBits = 100;
        rule.MaximumBits = 200;
        rule.SubscriptionTier = "3000";
        rule.MinimumMonths = 9;
        rule.MaximumMonths = 12;
        rule.GlobalDelaySeconds = 20;
        rule.UserDelaySeconds = 40;
        rule.ExecuteRandomAction = false;
        rule.ImportSource = "changed-source";
        rule.ImportIdentity = "changed-identity";
        rule.Actions.Clear();
        rule.Actions.Add(new UniversalTriggerAction
        {
            Id = Guid.NewGuid(),
            OscAddress = "/avatar/parameters/Changed",
            TargetValue = "1"
        });

        manager.CloseEditorCommand.Execute(null);

        Assert.False(rule.IsEnabled);
        Assert.Equal("Original Name", rule.Name);
        Assert.Equal(UniversalTriggerType.ChannelPointReward, rule.TriggerType);
        Assert.True(rule.ChatCommandEnabled);
        Assert.Equal("!original", rule.CommandText);
        Assert.Equal(ChatCommandPermission.Moderators, rule.ChatCommandPermission);
        Assert.Equal("original-reward-id", rule.RewardId);
        Assert.Equal("Original Reward", rule.RewardTitle);
        Assert.Equal("Original description", rule.RewardDescription);
        Assert.Equal(250, rule.RewardCost);
        Assert.Equal(30, rule.RewardCooldownSeconds);
        Assert.Equal(TwitchRewardSyncMode.CreateOrManage, rule.RewardSyncMode);
        Assert.Equal("#123456", rule.ManagedRewardReadyColor);
        Assert.Equal("#654321", rule.ManagedRewardCooldownColor);
        Assert.True(rule.DeleteManagedRewardWhenInactive);
        Assert.Equal(10, rule.MinimumBits);
        Assert.Equal(500, rule.MaximumBits);
        Assert.Equal("1000", rule.SubscriptionTier);
        Assert.Equal(2, rule.MinimumMonths);
        Assert.Equal(8, rule.MaximumMonths);
        Assert.Equal(4, rule.GlobalDelaySeconds);
        Assert.Equal(6, rule.UserDelaySeconds);
        Assert.True(rule.ExecuteRandomAction);
        Assert.Equal("original-source", rule.ImportSource);
        Assert.Equal("original-identity", rule.ImportIdentity);

        var restoredAction = Assert.Single(rule.Actions);
        Assert.Equal(originalAction.Id, restoredAction.Id);
        Assert.Equal(originalAction.OscAddress, restoredAction.OscAddress);
        Assert.Equal(originalAction.ValueKind, restoredAction.ValueKind);
        Assert.Equal(originalAction.TargetValue, restoredAction.TargetValue);
        Assert.Equal(originalAction.DefaultValue, restoredAction.DefaultValue);
        Assert.Equal(originalAction.DurationSeconds, restoredAction.DurationSeconds);
        Assert.Equal(originalAction.AddToQueue, restoredAction.AddToQueue);
        Assert.Equal(originalAction.ImportGroupKey, restoredAction.ImportGroupKey);
    }

    [Fact]
    public async Task UniversalEditorCancel_RestoresRuleWithNoActions()
    {
        await using var vm = new MainWindowViewModel();
        var rule = new UniversalTriggerRule
        {
            Name = "Original Empty Rule",
            TriggerType = UniversalTriggerType.ChatCommand,
            CommandText = "!original"
        };
        vm.Settings.UniversalTriggers.Add(rule);

        using var manager = new UniversalTriggersManagerViewModel(vm.Settings, vm);
        manager.OpenEditorCommand.Execute(rule);
        rule.Name = "Changed Empty Rule";
        rule.CommandText = "!changed";
        manager.CloseEditorCommand.Execute(null);

        Assert.Equal("Original Empty Rule", rule.Name);
        Assert.Equal("!original", rule.CommandText);
        Assert.Empty(rule.Actions);
    }

    [Fact]
    public async Task RewardFireSale_WardrobeManagedCostsAreDiscountedButLinkedCostsAreNot()
    {
        await using var vm = new MainWindowViewModel();
        var fireSale = vm.Settings.RewardFireSale;
        fireSale.IsEnabled = true;
        fireSale.SaleMode = RewardFireSaleMode.Permanent;
        fireSale.IsSaleActive = true;
        fireSale.ActiveDiscountPercent = 25;

        var profile = new AvatarTriggerProfile
        {
            AvatarId = "avtr_wardrobe",
            UseWardrobeMode = true,
            UseWardrobeMasterReward = true,
            WardrobeMasterRewardCost = 300,
            WardrobeMasterRewardTitle = "Wardrobe Master"
        };
        var managedOutfit = new WardrobeOutfit
        {
            TwitchRewardTitle = "Managed Outfit",
            TwitchRewardCost = "200",
            TwitchRewardSyncMode = TwitchRewardSyncMode.CreateOrManage
        };
        var linkedOutfit = new WardrobeOutfit
        {
            TwitchRewardTitle = "Linked Outfit",
            TwitchRewardCost = "200",
            TwitchRewardSyncMode = TwitchRewardSyncMode.LinkExisting
        };

        var outfitMethod = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "CreateManagedRewardTargetForWardrobeOutfit");
        var masterMethod = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "CreateManagedRewardTargetForWardrobeMasterReward");
        var managedTarget = outfitMethod.Invoke(vm, new object[] { profile, managedOutfit, true });
        var linkedTarget = outfitMethod.Invoke(vm, new object[] { profile, linkedOutfit, true });
        var masterTarget = masterMethod.Invoke(vm, new object[] { profile, true });

        Assert.Equal(150, GetPropertyValue<int>(managedTarget!, "RewardCost"));
        Assert.Equal(200, GetPropertyValue<int>(linkedTarget!, "RewardCost"));
        Assert.Equal(225, GetPropertyValue<int>(masterTarget!, "RewardCost"));
    }

    [Fact]
    public async Task RewardFireSale_FundingRewardUsesCooldownColorWhileCoolingDown()
    {
        await using var vm = new MainWindowViewModel();
        var fireSale = vm.Settings.RewardFireSale;
        fireSale.IsEnabled = true;
        fireSale.FundingRewardEnabled = true;
        fireSale.FundingRewardId = "funding-reward-id";
        fireSale.FundingRewardTitle = "Funding Reward";
        fireSale.FundingRewardCooldownSeconds = 30;
        fireSale.FundingRewardCooldownColor = "#123456";
        SetPrivateField(
            vm,
            "rewardFireSaleFundingRewardCooldownUntil",
            (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(1));

        var target = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "CreateManagedRewardTargetForRewardFireSaleFundingReward").Invoke(
                vm,
                new object[] { true });

        Assert.Equal("#123456", GetPropertyValue<string>(target!, "BackgroundColor"));
    }

    [Fact]
    public async Task ManagedRewardSync_DoesNotAdoptRawSameTitleUserReward()
    {
        await using var vm = new MainWindowViewModel();
        var handler = InstallRecordingTwitchHandler(vm);
        var rule = CreateConfiguredUniversalRewardRule(
            string.Empty,
            "User Reward Title",
            TwitchRewardSyncMode.CreateOrManage);
        var target = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "CreateManagedRewardTargetForUniversalTrigger").Invoke(
                vm,
                new object[] { rule, true, string.Empty });
        Assert.NotNull(target);

        var result = await SynchronizeManagedRewardsAsync(
            vm,
            [target],
            CreateManagedRewardCatalog(new TwitchApiClient.CustomRewardResponse
            {
                Id = "user-created-reward-id",
                Title = "User Reward Title",
                Cost = 100,
                IsEnabled = true,
                BackgroundColor = ManagedRewardPresentation.ReadyBackgroundColor
            }),
            InvokeManagedRewardOwnershipEntries(vm));

        Assert.False(result.Changed);
        Assert.Empty(rule.RewardId);
        Assert.Empty(result.ClaimedRewardIds);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnlinkTwitchReward_ClearsPersistedTriggerRuleIdAndPreservesTitle()
    {
        await using var vm = new MainWindowViewModel();
        var rule = new TriggerRule
        {
            RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            ChannelPointRewardId = "linked-reward-id",
            ChannelPointRewardTitle = "Linked Reward Title"
        };

        vm.UnlinkTwitchRewardCommand.Execute(rule);

        Assert.Empty(rule.ChannelPointRewardId);
        Assert.Equal("Linked Reward Title", rule.ChannelPointRewardTitle);
    }

    [Fact]
    public async Task UnlinkTwitchReward_ClearsIdsAndPreservesTitlesForEverySupportedTarget()
    {
        await using var vm = new MainWindowViewModel();
        var universalTrigger = new UniversalTriggerRule
        {
            RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            RewardId = "universal-id",
            RewardTitle = "Universal Title"
        };
        var scaleRule = new AvatarScaleRule
        {
            RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            RewardId = "scale-id",
            RewardTitle = "Scale Title"
        };
        var scaleMaster = new AvatarScaleMasterRewardSettings
        {
            RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            RewardId = "scale-master-id",
            RewardTitle = "Scale Master Title"
        };
        var setTriggerProfile = new AvatarTriggerProfile
        {
            UseSharedNumberedOutfitReward = true,
            SetTriggerMasterRewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            SetTriggerMasterRewardId = "set-master-id",
            SetTriggerMasterRewardTitle = "Set Master Title"
        };
        var firstSetTriggerRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.SetTrigger,
            SharedRewardChoiceEnabled = true,
            SharedRewardChoiceNumber = 1,
            ChannelPointRewardId = "set-master-id",
            ChannelPointRewardTitle = "Set Master Title"
        };
        var secondSetTriggerRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.SetTrigger,
            SharedRewardChoiceEnabled = true,
            SharedRewardChoiceNumber = 2,
            ChannelPointRewardId = "stale-mirrored-id",
            ChannelPointRewardTitle = "Set Master Title"
        };
        var unrelatedRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarParameter,
            ChannelPointRewardId = "unrelated-id",
            ChannelPointRewardTitle = "Unrelated Title"
        };
        setTriggerProfile.ChannelPointRules.Add(firstSetTriggerRule);
        setTriggerProfile.ChannelPointRules.Add(secondSetTriggerRule);
        setTriggerProfile.ChannelPointRules.Add(unrelatedRule);
        var outfit = new WardrobeOutfit
        {
            TwitchRewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            TwitchRewardId = "outfit-id",
            TwitchRewardTitle = "Outfit Title"
        };

        vm.UnlinkTwitchRewardCommand.Execute(universalTrigger);
        vm.UnlinkTwitchRewardCommand.Execute(scaleRule);
        vm.UnlinkTwitchRewardCommand.Execute(scaleMaster);
        vm.UnlinkTwitchRewardCommand.Execute(setTriggerProfile);
        vm.UnlinkTwitchRewardCommand.Execute(outfit);

        Assert.Empty(universalTrigger.RewardId);
        Assert.Equal("Universal Title", universalTrigger.RewardTitle);
        Assert.Empty(scaleRule.RewardId);
        Assert.Equal("Scale Title", scaleRule.RewardTitle);
        Assert.Empty(scaleMaster.RewardId);
        Assert.Equal("Scale Master Title", scaleMaster.RewardTitle);
        Assert.Empty(setTriggerProfile.SetTriggerMasterRewardId);
        Assert.Equal("Set Master Title", setTriggerProfile.SetTriggerMasterRewardTitle);
        Assert.Empty(firstSetTriggerRule.ChannelPointRewardId);
        Assert.Empty(secondSetTriggerRule.ChannelPointRewardId);
        Assert.Equal("Set Master Title", firstSetTriggerRule.ChannelPointRewardTitle);
        Assert.Equal("Set Master Title", secondSetTriggerRule.ChannelPointRewardTitle);
        Assert.Equal("unrelated-id", unrelatedRule.ChannelPointRewardId);
        Assert.Empty(outfit.TwitchRewardId);
        Assert.Equal("Outfit Title", outfit.TwitchRewardTitle);
    }

    [Fact]
    public async Task SetTriggerMasterUnlink_WiredBlankTitleChildrenCannotRestoreMasterId()
    {
        await using var vm = new MainWindowViewModel();
        const string oldRewardId = "set-trigger-master-id";
        const string masterTitle = "Set Trigger Master";
        var profile = new AvatarTriggerProfile
        {
            AvatarId = "avtr_test",
            UseSharedNumberedOutfitReward = true,
            SetTriggerMasterRewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            SetTriggerMasterRewardId = oldRewardId,
            SetTriggerMasterRewardTitle = masterTitle
        };
        var firstChild = CreateSetTriggerMasterChild(oldRewardId, choiceNumber: 1);
        var secondChild = CreateSetTriggerMasterChild(oldRewardId, choiceNumber: 2);
        var unrelatedRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarParameter,
            ChannelPointRewardId = "unrelated-id",
            ChannelPointRewardTitle = "Unrelated Reward"
        };
        profile.ChannelPointRules.Add(firstChild);
        profile.ChannelPointRules.Add(secondChild);
        profile.ChannelPointRules.Add(unrelatedRule);
        vm.Settings.AvatarProfiles.Add(profile);
        InvokeInstanceMethod(vm, "WireAvatarProfile", profile);
        Assert.False(profile.IsMasterProfile);

        vm.UnlinkTwitchRewardCommand.Execute(profile);

        Assert.Empty(firstChild.ChannelPointRewardId);
        Assert.Empty(secondChild.ChannelPointRewardId);
        Assert.Equal("unrelated-id", unrelatedRule.ChannelPointRewardId);
        Assert.Empty(profile.SetTriggerMasterRewardId);
        Assert.Equal(masterTitle, profile.SetTriggerMasterRewardTitle);

        var refreshedConfiguration = BridgeRuntimeConfiguration.FromSettings(
            vm.Settings,
            RuntimeConfig.CreateDefault(),
            null);
        var staleMatches = GetRuntimeRewardCandidates(
            refreshedConfiguration.Rules,
            oldRewardId,
            masterTitle);

        Assert.Empty(staleMatches);
    }

    [Fact]
    public void CapReclaimProtectedRewardIds_KeepLinkedAndManagedIdsWithoutEmptyLinkedReservation()
    {
        var targets = CreateTypedArray(
        [
            CreateManagedRewardSyncTarget(
                string.Empty,
                "Retained Empty Linked Title",
                TwitchRewardSyncMode.LinkExisting,
                protectFromCapReclaim: true),
            CreateManagedRewardSyncTarget(
                "linked-id",
                "Linked Title",
                TwitchRewardSyncMode.LinkExisting,
                protectFromCapReclaim: false),
            CreateManagedRewardSyncTarget(
                "managed-id",
                "Managed Title",
                TwitchRewardSyncMode.CreateOrManage,
                protectFromCapReclaim: true),
            CreateManagedRewardSyncTarget(
                "unprotected-managed-id",
                "Unprotected Managed Title",
                TwitchRewardSyncMode.CreateOrManage,
                protectFromCapReclaim: false)
        ]);

        var protectedRewardIds = Assert.IsAssignableFrom<IEnumerable<string>>(
            InvokeStaticMethod("BuildManagedRewardCapReclaimProtectedRewardIds", targets));

        Assert.Equal(
            ["linked-id", "managed-id"],
            protectedRewardIds.OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public async Task OmittedLinkedWardrobeOwnership_ProtectsCapReclaimWithoutProtectingManagedIds()
    {
        await using var vm = new MainWindowViewModel();
        var blankAvatarProfile = new AvatarTriggerProfile
        {
            AvatarId = string.Empty,
            UseWardrobeMode = true,
            UseWardrobeMasterReward = true,
            WardrobeMasterRewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            WardrobeMasterRewardId = "linked-master-blank-avatar",
            WardrobeMasterRewardTitle = "Linked Master Blank Avatar"
        };
        var linkedOutfit = new WardrobeOutfit
        {
            TwitchRewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            TwitchRewardId = "linked-outfit-blank-avatar",
            TwitchRewardTitle = "Linked Outfit Blank Avatar"
        };
        var managedOutfit = new WardrobeOutfit
        {
            TwitchRewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            TwitchRewardId = "managed-outfit-remains-recyclable",
            TwitchRewardTitle = "Managed Outfit Remains Recyclable"
        };
        blankAvatarProfile.WardrobeOutfits.Add(linkedOutfit);
        blankAvatarProfile.WardrobeOutfits.Add(managedOutfit);
        vm.Settings.AvatarProfiles.Add(blankAvatarProfile);

        var blankTitleMasterProfile = new AvatarTriggerProfile
        {
            AvatarId = "avtr_valid",
            UseWardrobeMode = true,
            UseWardrobeMasterReward = true,
            WardrobeMasterRewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            WardrobeMasterRewardId = "linked-master-blank-title",
            WardrobeMasterRewardTitle = string.Empty
        };
        vm.Settings.AvatarProfiles.Add(blankTitleMasterProfile);
        var createOutfitTarget = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "CreateManagedRewardTargetForWardrobeOutfit");
        var createMasterTarget = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "CreateManagedRewardTargetForWardrobeMasterReward");

        Assert.Null(createOutfitTarget.Invoke(vm, new object[] { blankAvatarProfile, linkedOutfit, true }));
        Assert.Null(createOutfitTarget.Invoke(vm, new object[] { blankAvatarProfile, managedOutfit, true }));
        Assert.Null(createMasterTarget.Invoke(vm, new object[] { blankAvatarProfile, true }));
        Assert.Null(createMasterTarget.Invoke(vm, new object[] { blankTitleMasterProfile, true }));

        var ownershipEntries = InvokeManagedRewardOwnershipEntries(vm);
        var targets = CreateTypedArray(
        [
            CreateManagedRewardSyncTarget(
                "unprotected-materialized-managed-id",
                "Unprotected Materialized Managed",
                TwitchRewardSyncMode.CreateOrManage,
                protectFromCapReclaim: false)
        ]);
        var protectedRewardIds = Assert.IsAssignableFrom<IEnumerable<string>>(
                InvokeStaticMethod(
                    "BuildManagedRewardCapReclaimProtectedRewardIdsForSync",
                    targets,
                    ownershipEntries))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(linkedOutfit.TwitchRewardId, protectedRewardIds);
        Assert.Contains(blankAvatarProfile.WardrobeMasterRewardId, protectedRewardIds);
        Assert.Contains(blankTitleMasterProfile.WardrobeMasterRewardId, protectedRewardIds);
        Assert.DoesNotContain(managedOutfit.TwitchRewardId, protectedRewardIds);
        Assert.DoesNotContain("unprotected-materialized-managed-id", protectedRewardIds);
    }

    [Fact]
    public async Task OmittedLinkedWardrobeOwnership_CannotBeRecycledAtCapacity()
    {
        await using var vm = new MainWindowViewModel();
        var handler = InstallRecordingTwitchHandler(vm);
        var profile = new AvatarTriggerProfile { AvatarId = string.Empty };
        var linkedOutfit = new WardrobeOutfit
        {
            TwitchRewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            TwitchRewardId = "omitted-linked-wardrobe-id",
            TwitchRewardTitle = "Omitted Linked Wardrobe"
        };
        profile.WardrobeOutfits.Add(linkedOutfit);
        vm.Settings.AvatarProfiles.Add(profile);
        var ownershipEntries = InvokeManagedRewardOwnershipEntries(vm);
        var target = CreateManagedRewardSyncTarget(
            string.Empty,
            "Needed Managed Reward",
            TwitchRewardSyncMode.CreateOrManage,
            protectFromCapReclaim: true);
        var targets = CreateTypedArray([target]);
        var protectedRewardIds = Assert.IsAssignableFrom<IEnumerable<string>>(
                InvokeStaticMethod(
                    "BuildManagedRewardCapReclaimProtectedRewardIdsForSync",
                    targets,
                    ownershipEntries))
            .ToHashSet(StringComparer.Ordinal);
        var linkedReward = new TwitchApiClient.CustomRewardResponse
        {
            Id = linkedOutfit.TwitchRewardId,
            Title = "VRC: Omitted Linked Wardrobe",
            Cost = 900,
            IsEnabled = false,
            BackgroundColor = "#123456",
            Prompt = "Linked viewer reward",
            IsUserInputRequired = true
        };
        var rewardCatalog = CreateManagedRewardCatalog(linkedReward);
        var manageableCatalog = CreateManagedRewardCatalog(linkedReward);

        var result = await TryRecycleManagedRewardForCapacityAsync(
            vm,
            target,
            targets,
            rewardCatalog,
            manageableCatalog,
            protectedRewardIds,
            ownershipEntries);

        Assert.False(result.Changed);
        Assert.Equal(0, result.Creates);
        Assert.Equal(0, result.Updates);
        Assert.Equal(0, result.Deletes);
        Assert.Empty(handler.Requests);
        Assert.Empty(GetPropertyValue<string>(target, "RewardId"));
        Assert.Equal("VRC: Omitted Linked Wardrobe", linkedReward.Title);
        Assert.Equal(900, linkedReward.Cost);
        Assert.False(linkedReward.IsEnabled);
        Assert.Equal("#123456", linkedReward.BackgroundColor);
        Assert.Equal("Linked viewer reward", linkedReward.Prompt);
        Assert.True(linkedReward.IsUserInputRequired);
        Assert.Equal("omitted-linked-wardrobe-id", linkedOutfit.TwitchRewardId);
    }

    [Fact]
    public void CapReclaimProtectedTitleKeys_EmptyLinkedIdDoesNotReserveRetainedTitle()
    {
        var targets = CreateTypedArray(
        [
            CreateManagedRewardSyncTarget(
                string.Empty,
                "Retained Empty Linked Title",
                TwitchRewardSyncMode.LinkExisting,
                protectFromCapReclaim: true),
            CreateManagedRewardSyncTarget(
                "linked-id",
                "Linked Title",
                TwitchRewardSyncMode.LinkExisting,
                protectFromCapReclaim: false),
            CreateManagedRewardSyncTarget(
                "managed-id",
                "Managed Title",
                TwitchRewardSyncMode.CreateOrManage,
                protectFromCapReclaim: true),
            CreateManagedRewardSyncTarget(
                "unprotected-managed-id",
                "Unprotected Managed Title",
                TwitchRewardSyncMode.CreateOrManage,
                protectFromCapReclaim: false)
        ]);

        var protectedTitleKeys = Assert.IsAssignableFrom<IEnumerable<string>>(
            InvokeStaticMethod("BuildManagedRewardCapReclaimProtectedTitleKeys", targets));

        Assert.Equal(
            [
                ManagedRewardPresentation.NormalizeTitleIdentityKey("Linked Title"),
                ManagedRewardPresentation.NormalizeTitleIdentityKey("Managed Title")
            ],
            protectedTitleKeys.OrderBy(value => value, StringComparer.Ordinal));
        Assert.DoesNotContain(
            ManagedRewardPresentation.NormalizeTitleIdentityKey("Retained Empty Linked Title"),
            protectedTitleKeys);
    }

    [Theory]
    [InlineData(TwitchRewardSyncMode.CreateOrManage, "managed-id", "", true)]
    [InlineData(TwitchRewardSyncMode.CreateOrManage, "", "Managed Title", true)]
    [InlineData(TwitchRewardSyncMode.LinkExisting, "linked-id", "", true)]
    [InlineData(TwitchRewardSyncMode.LinkExisting, "", "Retained Linked Title", false)]
    public async Task AvatarScaleMasterSyncTarget_UsesModeAwareRewardIdentity(
        TwitchRewardSyncMode rewardSyncMode,
        string rewardId,
        string rewardTitle,
        bool expectedDesiredEnabled)
    {
        await using var vm = new MainWindowViewModel();
        var master = new AvatarScaleMasterRewardSettings
        {
            IsEnabled = true,
            RewardSyncMode = rewardSyncMode,
            RewardId = rewardId,
            RewardTitle = rewardTitle
        };
        var target = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "CreateManagedRewardTargetForAvatarScaleMasterReward").Invoke(
                vm,
                new object[] { master, true, false, false });
        Assert.NotNull(target);

        Assert.Equal(expectedDesiredEnabled, GetPropertyValue<bool>(target, "DesiredEnabled"));
    }

    [Theory]
    [InlineData("universal", true)]
    [InlineData("universal", false)]
    [InlineData("avatar-scale", true)]
    [InlineData("avatar-scale", false)]
    public async Task ManagedIdOnlySync_ExactCatalogIdAdoptsTitleBeforeDeleteDecision(
        string family,
        bool useManagedRewardTitlePrefix)
    {
        await using var vm = new MainWindowViewModel();
        vm.Settings.UseManagedRewardTitlePrefix = useManagedRewardTitlePrefix;
        var handler = InstallRecordingTwitchHandler(vm);
        var fixture = CreateManagedIdOnlyTargetFixture(vm, family);
        const string canonicalTitle = "Catalog Adopted Title";
        var exactReward = new TwitchApiClient.CustomRewardResponse
        {
            Id = fixture.RewardId,
            Title = ManagedRewardPresentation.BuildTitle(canonicalTitle, useManagedRewardTitlePrefix),
            Cost = 100,
            IsEnabled = true,
            IsGlobalCooldownEnabled = false,
            GlobalCooldownSeconds = 0,
            BackgroundColor = ManagedRewardPresentation.ReadyBackgroundColor,
            Prompt = "Managed by Crystal Relay.",
            IsUserInputRequired = false
        };
        var result = await SynchronizeManagedRewardAsync(
            vm,
            fixture.Target,
            CreateManagedRewardCatalog(exactReward));

        Assert.True(result.Changed);
        Assert.Equal(fixture.RewardId, fixture.GetRewardId());
        Assert.Equal(canonicalTitle, fixture.GetRewardTitle());
        Assert.Equal(canonicalTitle, GetPropertyValue<string>(fixture.Target, "RewardTitle"));
        Assert.Equal(
            ManagedRewardPresentation.BuildTitle(canonicalTitle, useManagedRewardTitlePrefix),
            exactReward.Title);
        Assert.Contains(exactReward.Id, result.ClaimedRewardIds);
        Assert.Equal(0, result.Creates);
        Assert.Equal(0, result.Updates);
        Assert.Equal(0, result.Deletes);
        Assert.Empty(handler.Requests);
        Assert.True(exactReward.IsEnabled);
    }

    [Fact]
    public void ManagedRewardOwnershipIndex_UpdateRewardTitleMovesNormalizedOwnership()
    {
        var ownerId = Guid.NewGuid();
        var observerId = Guid.NewGuid();
        var ownershipEntry = CreateManagedRewardOwnershipEntry(
            ownerId,
            string.Empty,
            "  VRC: Previous   Title  ",
            TwitchRewardSyncMode.CreateOrManage);
        var ownershipIndex = CreateNestedInstance(
            "ManagedRewardRuleOwnershipIndex",
            CreateTypedArray([ownershipEntry]));
        var updateMethod = ownershipIndex.GetType().GetMethod(
            "UpdateRewardTitle",
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(updateMethod);

        updateMethod.Invoke(ownershipIndex, new object[]
        {
            ownerId,
            "previous title",
            " VRC: Adopted   Title "
        });

        var isOwnedByAnotherRule = GetInstanceMethod(
            ownershipIndex.GetType(),
            "IsOwnedByAnotherRule");
        var previousReward = new TwitchApiClient.CustomRewardResponse
        {
            Id = "previous-id",
            Title = "VRC: Previous Title"
        };
        var adoptedReward = new TwitchApiClient.CustomRewardResponse
        {
            Id = "adopted-id",
            Title = "adopted title"
        };

        Assert.False(Assert.IsType<bool>(isOwnedByAnotherRule.Invoke(
            ownershipIndex,
            new object[] { observerId, previousReward })));
        Assert.True(Assert.IsType<bool>(isOwnedByAnotherRule.Invoke(
            ownershipIndex,
            new object[] { observerId, adoptedReward })));
        Assert.False(Assert.IsType<bool>(isOwnedByAnotherRule.Invoke(
            ownershipIndex,
            new object[] { ownerId, adoptedReward })));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task ManagedIdOnlySync_AdoptedTitleClaimsOwnershipBeforeNextTitleOnlyTarget(
        bool useManagedRewardTitlePrefix,
        bool titleOnlyTargetFirst)
    {
        await using var vm = new MainWindowViewModel();
        vm.Settings.UseManagedRewardTitlePrefix = useManagedRewardTitlePrefix;
        var handler = InstallRecordingTwitchHandler(vm);
        const string canonicalTitle = "Shared Ownership Title";
        var exactRule = CreateConfiguredUniversalRewardRule(
            "exact-owned-id",
            string.Empty,
            TwitchRewardSyncMode.CreateOrManage);
        var titleOnlyRule = CreateConfiguredUniversalRewardRule(
            string.Empty,
            canonicalTitle,
            TwitchRewardSyncMode.CreateOrManage);
        var createTargetMethod = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "CreateManagedRewardTargetForUniversalTrigger");
        var exactTarget = createTargetMethod.Invoke(
            vm,
            new object[] { exactRule, true, string.Empty });
        var titleOnlyTarget = createTargetMethod.Invoke(
            vm,
            new object[] { titleOnlyRule, true, string.Empty });
        Assert.NotNull(exactTarget);
        Assert.NotNull(titleOnlyTarget);
        var exactReward = new TwitchApiClient.CustomRewardResponse
        {
            Id = exactRule.RewardId,
            Title = ManagedRewardPresentation.BuildTitle(canonicalTitle, useManagedRewardTitlePrefix),
            Cost = 100,
            IsEnabled = true,
            IsGlobalCooldownEnabled = false,
            GlobalCooldownSeconds = 0,
            BackgroundColor = ManagedRewardPresentation.ReadyBackgroundColor,
            Prompt = "Managed by Crystal Relay.",
            IsUserInputRequired = false
        };
        var duplicateReward = new TwitchApiClient.CustomRewardResponse
        {
            Id = "duplicate-unowned-id",
            Title = ManagedRewardPresentation.BuildTitle(canonicalTitle, !useManagedRewardTitlePrefix),
            Cost = 100,
            IsEnabled = true,
            IsGlobalCooldownEnabled = false,
            GlobalCooldownSeconds = 0,
            BackgroundColor = ManagedRewardPresentation.ReadyBackgroundColor,
            Prompt = "Managed by Crystal Relay.",
            IsUserInputRequired = false
        };
        var orderedTargets = titleOnlyTargetFirst
            ? new[] { titleOnlyTarget, exactTarget }
            : new[] { exactTarget, titleOnlyTarget };
        var result = await SynchronizeManagedRewardsAsync(
            vm,
            orderedTargets,
            CreateManagedRewardCatalog(exactReward, duplicateReward),
            InvokeManagedRewardOwnershipEntries(vm, [exactRule, titleOnlyRule]));

        Assert.True(result.Changed);
        Assert.Equal(canonicalTitle, exactRule.RewardTitle);
        Assert.Equal(canonicalTitle, GetPropertyValue<string>(exactTarget, "RewardTitle"));
        Assert.Equal("exact-owned-id", exactRule.RewardId);
        Assert.Empty(titleOnlyRule.RewardId);
        Assert.Empty(GetPropertyValue<string>(titleOnlyTarget, "RewardId"));
        Assert.Contains(exactReward.Id, result.ClaimedRewardIds);
        Assert.DoesNotContain(duplicateReward.Id, result.ClaimedRewardIds);
        Assert.Equal(0, result.Creates);
        Assert.Equal(0, result.Updates);
        Assert.Equal(0, result.Deletes);
        Assert.Empty(handler.Requests);
        Assert.Equal(
            ManagedRewardPresentation.BuildTitle(canonicalTitle, useManagedRewardTitlePrefix),
            exactReward.Title);
        Assert.Equal(
            ManagedRewardPresentation.BuildTitle(canonicalTitle, !useManagedRewardTitlePrefix),
            duplicateReward.Title);
    }

    [Fact]
    public async Task ManagedMasterTargets_SameProfileUseDistinctStableOwnerIdentitiesAndCallbacks()
    {
        await using var vm = new MainWindowViewModel();
        var fixture = CreateSameProfileMasterTargetFixture(
            vm,
            setTriggerRewardId: "set-master-id",
            wardrobeRewardId: "wardrobe-master-id",
            setTriggerTitle: "Set Master",
            wardrobeTitle: "Wardrobe Master");
        var profileId = fixture.Profile.Id;
        var setTriggerOwnerId = GetPropertyValue<Guid>(fixture.SetTriggerTarget, "Id");
        var wardrobeOwnerId = GetPropertyValue<Guid>(fixture.WardrobeTarget, "Id");
        var repeatedSetTriggerTarget = CreateSharedAvatarSetTarget(vm, fixture.Profile, fixture.SetTriggerRule);
        var repeatedWardrobeTarget = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "CreateManagedRewardTargetForWardrobeMasterReward").Invoke(
                vm,
                new object[] { fixture.Profile, true });
        Assert.NotNull(repeatedWardrobeTarget);

        Assert.NotEqual(profileId, setTriggerOwnerId);
        Assert.NotEqual(profileId, wardrobeOwnerId);
        Assert.NotEqual(setTriggerOwnerId, wardrobeOwnerId);
        Assert.Equal(setTriggerOwnerId, GetPropertyValue<Guid>(repeatedSetTriggerTarget, "Id"));
        Assert.Equal(wardrobeOwnerId, GetPropertyValue<Guid>(repeatedWardrobeTarget, "Id"));

        var ownershipEntries = AsObjects(InvokeManagedRewardOwnershipEntries(vm));
        Assert.Equal(
            setTriggerOwnerId,
            GetPropertyValue<Guid>(Assert.Single(
                ownershipEntries,
                entry => GetPropertyValue<string>(entry, "RewardId") == "set-master-id"), "Id"));
        Assert.Equal(
            wardrobeOwnerId,
            GetPropertyValue<Guid>(Assert.Single(
                ownershipEntries,
                entry => GetPropertyValue<string>(entry, "RewardId") == "wardrobe-master-id"), "Id"));

        GetPropertyValue<Action<string>>(fixture.SetTriggerTarget, "ApplyRewardId")("updated-set-id");
        GetPropertyValue<Action<string>>(fixture.SetTriggerTarget, "ApplyRewardTitle")("Updated Set Title");
        Assert.Equal("updated-set-id", fixture.Profile.SetTriggerMasterRewardId);
        Assert.Equal("updated-set-id", fixture.SetTriggerRule.ChannelPointRewardId);
        Assert.Equal("Updated Set Title", fixture.Profile.SetTriggerMasterRewardTitle);
        Assert.Equal("Updated Set Title", fixture.SetTriggerRule.ChannelPointRewardTitle);
        Assert.Equal("wardrobe-master-id", fixture.Profile.WardrobeMasterRewardId);
        Assert.Equal("Wardrobe Master", fixture.Profile.WardrobeMasterRewardTitle);

        GetPropertyValue<Action<string>>(fixture.WardrobeTarget, "ApplyRewardId")("updated-wardrobe-id");
        GetPropertyValue<Action<string>>(fixture.WardrobeTarget, "ApplyRewardTitle")("Updated Wardrobe Title");
        Assert.Equal("updated-wardrobe-id", fixture.Profile.WardrobeMasterRewardId);
        Assert.Equal("Updated Wardrobe Title", fixture.Profile.WardrobeMasterRewardTitle);
        Assert.Equal("updated-set-id", fixture.Profile.SetTriggerMasterRewardId);
        Assert.Equal("Updated Set Title", fixture.Profile.SetTriggerMasterRewardTitle);
        Assert.Equal(profileId, fixture.Profile.Id);
    }

    [Fact]
    public async Task ManagedMasterOwnership_SameNormalizedTitlesCannotClaimBothRewards()
    {
        await using var vm = new MainWindowViewModel();
        vm.Settings.UseManagedRewardTitlePrefix = true;
        var handler = InstallRecordingTwitchHandler(vm);
        const string canonicalTitle = "Shared Master Identity";
        var fixture = CreateSameProfileMasterTargetFixture(
            vm,
            setTriggerRewardId: "set-master-exact-id",
            wardrobeRewardId: string.Empty,
            setTriggerTitle: canonicalTitle,
            wardrobeTitle: $"VRC: {canonicalTitle}");
        var exactReward = CreateRewardMatchingTarget(
            fixture.SetTriggerTarget,
            fixture.Profile.SetTriggerMasterRewardId,
            ManagedRewardPresentation.BuildTitle(canonicalTitle, includePrefix: true));
        var duplicateReward = new TwitchApiClient.CustomRewardResponse
        {
            Id = "duplicate-master-id",
            Title = canonicalTitle,
            Cost = 999,
            IsEnabled = false,
            BackgroundColor = "#123456",
            Prompt = "Duplicate",
            IsUserInputRequired = false
        };
        var unrelatedReward = new TwitchApiClient.CustomRewardResponse
        {
            Id = "unrelated-master-id",
            Title = "VRC: Unrelated Master",
            Cost = 777,
            IsEnabled = false,
            BackgroundColor = "#654321",
            Prompt = "Unrelated",
            IsUserInputRequired = true
        };
        var result = await SynchronizeManagedRewardsAsync(
            vm,
            [fixture.WardrobeTarget, fixture.SetTriggerTarget],
            CreateManagedRewardCatalog(exactReward, duplicateReward, unrelatedReward),
            InvokeManagedRewardOwnershipEntries(vm));

        Assert.False(result.Changed);
        Assert.Equal("set-master-exact-id", fixture.Profile.SetTriggerMasterRewardId);
        Assert.Equal("set-master-exact-id", fixture.SetTriggerRule.ChannelPointRewardId);
        Assert.Empty(fixture.Profile.WardrobeMasterRewardId);
        Assert.Contains(exactReward.Id, result.ClaimedRewardIds);
        Assert.DoesNotContain(duplicateReward.Id, result.ClaimedRewardIds);
        Assert.DoesNotContain(unrelatedReward.Id, result.ClaimedRewardIds);
        Assert.Equal(0, result.Creates);
        Assert.Equal(0, result.Updates);
        Assert.Equal(0, result.Deletes);
        Assert.Empty(handler.Requests);
        Assert.Equal(canonicalTitle, duplicateReward.Title);
        Assert.Equal(999, duplicateReward.Cost);
        Assert.False(duplicateReward.IsEnabled);
        Assert.Equal("#123456", duplicateReward.BackgroundColor);
        Assert.Equal("VRC: Unrelated Master", unrelatedReward.Title);
        Assert.Equal(777, unrelatedReward.Cost);
        Assert.False(unrelatedReward.IsEnabled);
        Assert.Equal("#654321", unrelatedReward.BackgroundColor);
        Assert.Equal("Unrelated", unrelatedReward.Prompt);
        Assert.True(unrelatedReward.IsUserInputRequired);
    }

    [Theory]
    [InlineData(TwitchRewardSyncMode.CreateOrManage, TwitchRewardSyncMode.CreateOrManage, true, false, true)]
    [InlineData(TwitchRewardSyncMode.LinkExisting, TwitchRewardSyncMode.CreateOrManage, true, false, true)]
    [InlineData(TwitchRewardSyncMode.CreateOrManage, TwitchRewardSyncMode.LinkExisting, false, true, true)]
    [InlineData(TwitchRewardSyncMode.LinkExisting, TwitchRewardSyncMode.LinkExisting, true, true, false)]
    public async Task PersistedMasterIdCollision_RepairsByLinkedPriorityWithoutTwitchMutation(
        TwitchRewardSyncMode setTriggerMode,
        TwitchRewardSyncMode wardrobeMode,
        bool expectSetTriggerId,
        bool expectWardrobeId,
        bool expectedChanged)
    {
        await using var vm = new MainWindowViewModel();
        var handler = InstallRecordingTwitchHandler(vm);
        const string sharedRewardId = "persisted-shared-master-id";
        const string setTriggerTitle = "Persisted Set Master";
        const string wardrobeTitle = "Persisted Wardrobe Master";
        var fixture = CreateSameProfileMasterTargetFixture(
            vm,
            sharedRewardId,
            sharedRewardId,
            setTriggerTitle,
            wardrobeTitle);
        fixture.Profile.SetTriggerMasterRewardSyncMode = setTriggerMode;
        fixture.SetTriggerRule.RewardSyncMode = setTriggerMode;
        fixture.Profile.WardrobeMasterRewardSyncMode = wardrobeMode;
        var unrelatedRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarParameter,
            RewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            ChannelPointRewardId = sharedRewardId,
            ChannelPointRewardTitle = "Unrelated Rule"
        };
        fixture.Profile.ChannelPointRules.Add(unrelatedRule);
        var repairMethod = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "RepairConflictingProfileMasterRewardIds");

        var changed = Assert.IsType<bool>(repairMethod.Invoke(vm, Array.Empty<object>()));

        Assert.Equal(expectedChanged, changed);
        Assert.Equal(expectSetTriggerId ? sharedRewardId : string.Empty, fixture.Profile.SetTriggerMasterRewardId);
        Assert.Equal(expectSetTriggerId ? sharedRewardId : string.Empty, fixture.SetTriggerRule.ChannelPointRewardId);
        Assert.Equal(expectWardrobeId ? sharedRewardId : string.Empty, fixture.Profile.WardrobeMasterRewardId);
        Assert.Equal(setTriggerTitle, fixture.Profile.SetTriggerMasterRewardTitle);
        Assert.Equal(setTriggerTitle, fixture.SetTriggerRule.ChannelPointRewardTitle);
        Assert.Equal(wardrobeTitle, fixture.Profile.WardrobeMasterRewardTitle);
        Assert.Equal(sharedRewardId, unrelatedRule.ChannelPointRewardId);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PersistedMasterIdCollision_SetTriggerLossPreservesDisabledSharedModeChildIds()
    {
        await using var vm = new MainWindowViewModel();
        const string sharedRewardId = "disabled-set-master-collision";
        var fixture = CreateSameProfileMasterTargetFixture(
            vm,
            sharedRewardId,
            sharedRewardId,
            "Disabled Set Master",
            "Linked Wardrobe Master");
        fixture.Profile.SetTriggerMasterRewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
        fixture.SetTriggerRule.RewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
        fixture.Profile.WardrobeMasterRewardSyncMode = TwitchRewardSyncMode.LinkExisting;
        fixture.Profile.UseSharedNumberedOutfitReward = false;

        var changed = Assert.IsType<bool>(GetInstanceMethod(
            typeof(MainWindowViewModel),
            "RepairConflictingProfileMasterRewardIds").Invoke(vm, Array.Empty<object>()));

        Assert.True(changed);
        Assert.Empty(fixture.Profile.SetTriggerMasterRewardId);
        Assert.Equal(sharedRewardId, fixture.SetTriggerRule.ChannelPointRewardId);
        Assert.Equal(sharedRewardId, fixture.Profile.WardrobeMasterRewardId);
        Assert.Equal("Disabled Set Master", fixture.Profile.SetTriggerMasterRewardTitle);
        Assert.Equal("Disabled Set Master", fixture.SetTriggerRule.ChannelPointRewardTitle);
        Assert.Equal("Linked Wardrobe Master", fixture.Profile.WardrobeMasterRewardTitle);
    }

    [Fact]
    public async Task PersistedMasterIdCollision_DisabledSharedModePreservesIndependentSetTriggerReward()
    {
        await using var vm = new MainWindowViewModel();
        var handler = InstallRecordingTwitchHandler(vm);
        const string sharedRewardId = "disabled-shared-mode-master-collision";
        const string independentRewardId = "independent-set-trigger-reward-id";
        const string setTriggerTitle = "Configured Set Trigger Master";
        const string wardrobeTitle = "Configured Linked Wardrobe Master";
        const string independentRewardTitle = "Independent Set Trigger Reward";
        var profile = new AvatarTriggerProfile
        {
            AvatarId = "avtr_test",
            UseSharedNumberedOutfitReward = false,
            SetTriggerMasterRewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            SetTriggerMasterRewardId = sharedRewardId,
            SetTriggerMasterRewardTitle = setTriggerTitle,
            UseWardrobeMode = true,
            UseWardrobeMasterReward = true,
            WardrobeMasterRewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            WardrobeMasterRewardId = sharedRewardId,
            WardrobeMasterRewardTitle = wardrobeTitle
        };
        var independentChild = CreateSetTriggerMasterChild(independentRewardId, choiceNumber: 1);
        independentChild.ChannelPointRewardTitle = independentRewardTitle;
        independentChild.RewardSyncMode = TwitchRewardSyncMode.LinkExisting;
        var unrelatedRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarParameter,
            RewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            ChannelPointRewardId = "unrelated-rule-id",
            ChannelPointRewardTitle = "Unrelated Rule Title"
        };
        profile.ChannelPointRules.Add(independentChild);
        profile.ChannelPointRules.Add(unrelatedRule);
        vm.Settings.AvatarProfiles.Add(profile);
        InvokeInstanceMethod(vm, "WireAvatarProfile", profile);

        var changed = Assert.IsType<bool>(GetInstanceMethod(
            typeof(MainWindowViewModel),
            "RepairConflictingProfileMasterRewardIds").Invoke(vm, Array.Empty<object>()));

        Assert.True(changed);
        Assert.Empty(profile.SetTriggerMasterRewardId);
        Assert.Equal(sharedRewardId, profile.WardrobeMasterRewardId);
        Assert.Equal(setTriggerTitle, profile.SetTriggerMasterRewardTitle);
        Assert.Equal(wardrobeTitle, profile.WardrobeMasterRewardTitle);
        Assert.Equal(independentRewardId, independentChild.ChannelPointRewardId);
        Assert.Equal(independentRewardTitle, independentChild.ChannelPointRewardTitle);
        Assert.Equal(TwitchRewardSyncMode.LinkExisting, independentChild.RewardSyncMode);
        Assert.Equal("unrelated-rule-id", unrelatedRule.ChannelPointRewardId);
        Assert.Equal("Unrelated Rule Title", unrelatedRule.ChannelPointRewardTitle);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PersistedMasterIdCollision_WiredMirrorsCannotRestoreClearedSetMasterId()
    {
        await using var vm = new MainWindowViewModel();
        const string sharedRewardId = "wired-set-master-collision";
        var fixture = CreateSameProfileMasterTargetFixture(
            vm,
            sharedRewardId,
            sharedRewardId,
            "Wired Set Master",
            "Linked Wardrobe Master");
        fixture.Profile.SetTriggerMasterRewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
        fixture.SetTriggerRule.RewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
        fixture.Profile.WardrobeMasterRewardSyncMode = TwitchRewardSyncMode.LinkExisting;
        InvokeInstanceMethod(vm, "WireAvatarProfile", fixture.Profile);

        var changed = Assert.IsType<bool>(GetInstanceMethod(
            typeof(MainWindowViewModel),
            "RepairConflictingProfileMasterRewardIds").Invoke(vm, Array.Empty<object>()));

        Assert.True(changed);
        Assert.Empty(fixture.Profile.SetTriggerMasterRewardId);
        Assert.Empty(fixture.SetTriggerRule.ChannelPointRewardId);
        Assert.Equal(sharedRewardId, fixture.Profile.WardrobeMasterRewardId);
        Assert.Equal("Wired Set Master", fixture.Profile.SetTriggerMasterRewardTitle);
        Assert.Equal("Wired Set Master", fixture.SetTriggerRule.ChannelPointRewardTitle);
        Assert.Equal("Linked Wardrobe Master", fixture.Profile.WardrobeMasterRewardTitle);
    }

    [Fact]
    public async Task PersistedMasterIdCollision_WiredBlankTitleSiblingMirrorsCannotRestoreClearedSetMasterId()
    {
        await using var vm = new MainWindowViewModel();
        const string sharedRewardId = "wired-blank-title-sibling-collision";
        const string setTriggerTitle = "Configured Set Trigger Master";
        const string wardrobeTitle = "Configured Linked Wardrobe Master";
        var profile = new AvatarTriggerProfile
        {
            AvatarId = "avtr_test",
            UseSharedNumberedOutfitReward = true,
            SetTriggerMasterRewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            SetTriggerMasterRewardId = sharedRewardId,
            SetTriggerMasterRewardTitle = setTriggerTitle,
            UseWardrobeMode = true,
            UseWardrobeMasterReward = true,
            WardrobeMasterRewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            WardrobeMasterRewardId = sharedRewardId,
            WardrobeMasterRewardTitle = wardrobeTitle
        };
        var firstChild = CreateSetTriggerMasterChild(sharedRewardId, choiceNumber: 1);
        var secondChild = CreateSetTriggerMasterChild(sharedRewardId, choiceNumber: 2);
        firstChild.RewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
        secondChild.RewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
        var unrelatedRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarParameter,
            RewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            ChannelPointRewardId = "unrelated-rule-id",
            ChannelPointRewardTitle = "Unrelated Rule Title"
        };
        profile.ChannelPointRules.Add(firstChild);
        profile.ChannelPointRules.Add(secondChild);
        profile.ChannelPointRules.Add(unrelatedRule);
        vm.Settings.AvatarProfiles.Add(profile);
        InvokeInstanceMethod(vm, "WireAvatarProfile", profile);

        var changed = Assert.IsType<bool>(GetInstanceMethod(
            typeof(MainWindowViewModel),
            "RepairConflictingProfileMasterRewardIds").Invoke(vm, Array.Empty<object>()));

        Assert.True(changed);
        Assert.Empty(profile.SetTriggerMasterRewardId);
        Assert.Empty(firstChild.ChannelPointRewardId);
        Assert.Empty(secondChild.ChannelPointRewardId);
        Assert.Equal(sharedRewardId, profile.WardrobeMasterRewardId);
        Assert.Equal(setTriggerTitle, profile.SetTriggerMasterRewardTitle);
        Assert.Equal(wardrobeTitle, profile.WardrobeMasterRewardTitle);
        Assert.Empty(firstChild.ChannelPointRewardTitle);
        Assert.Empty(secondChild.ChannelPointRewardTitle);
        Assert.Equal("unrelated-rule-id", unrelatedRule.ChannelPointRewardId);
        Assert.Equal("Unrelated Rule Title", unrelatedRule.ChannelPointRewardTitle);
    }

    [Fact]
    public async Task PersistedMasterIdCollision_WiredDivergentSharedMirrorClearsAllSetTriggerIdsWhenLinkedWardrobeWins()
    {
        await using var vm = new MainWindowViewModel();
        var handler = InstallRecordingTwitchHandler(vm);
        const string sharedRewardId = "wired-divergent-set-master-collision";
        const string staleSharedChildRewardId = "stale-set-trigger-child-id";
        const string setTriggerTitle = "Configured Set Trigger Master";
        const string wardrobeTitle = "Configured Linked Wardrobe Master";
        var profile = new AvatarTriggerProfile
        {
            AvatarId = "avtr_test",
            UseSharedNumberedOutfitReward = true,
            SetTriggerMasterRewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            SetTriggerMasterRewardId = sharedRewardId,
            SetTriggerMasterRewardTitle = setTriggerTitle,
            UseWardrobeMode = true,
            UseWardrobeMasterReward = true,
            WardrobeMasterRewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            WardrobeMasterRewardId = sharedRewardId,
            WardrobeMasterRewardTitle = wardrobeTitle
        };
        var matchingChild = CreateSetTriggerMasterChild(sharedRewardId, choiceNumber: 1);
        var staleChild = CreateSetTriggerMasterChild(staleSharedChildRewardId, choiceNumber: 2);
        matchingChild.RewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
        staleChild.RewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
        var unrelatedRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarParameter,
            RewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            ChannelPointRewardId = "unrelated-rule-id",
            ChannelPointRewardTitle = "Unrelated Rule Title"
        };
        profile.ChannelPointRules.Add(matchingChild);
        profile.ChannelPointRules.Add(staleChild);
        profile.ChannelPointRules.Add(unrelatedRule);
        vm.Settings.AvatarProfiles.Add(profile);
        InvokeInstanceMethod(vm, "WireAvatarProfile", profile);

        var changed = Assert.IsType<bool>(GetInstanceMethod(
            typeof(MainWindowViewModel),
            "RepairConflictingProfileMasterRewardIds").Invoke(vm, Array.Empty<object>()));

        Assert.True(changed);
        Assert.Empty(profile.SetTriggerMasterRewardId);
        Assert.Empty(matchingChild.ChannelPointRewardId);
        Assert.Empty(staleChild.ChannelPointRewardId);
        Assert.Equal(sharedRewardId, profile.WardrobeMasterRewardId);
        Assert.Equal(setTriggerTitle, profile.SetTriggerMasterRewardTitle);
        Assert.Equal(wardrobeTitle, profile.WardrobeMasterRewardTitle);
        Assert.Empty(matchingChild.ChannelPointRewardTitle);
        Assert.Empty(staleChild.ChannelPointRewardTitle);
        Assert.Equal("unrelated-rule-id", unrelatedRule.ChannelPointRewardId);
        Assert.Equal("Unrelated Rule Title", unrelatedRule.ChannelPointRewardTitle);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ManagedIdOnlySync_MissingExactIdDoesNotClearOrMutateUnrelatedReward()
    {
        await using var vm = new MainWindowViewModel();
        var handler = InstallRecordingTwitchHandler(vm);
        var target = CreateManagedRewardSyncTarget(
            "missing-stable-id",
            string.Empty,
            TwitchRewardSyncMode.CreateOrManage,
            protectFromCapReclaim: true,
            deleteWhenInactive: true);
        var unrelatedReward = new TwitchApiClient.CustomRewardResponse
        {
            Id = "unrelated-id",
            Title = "VRC: Unrelated Reward",
            Cost = 999,
            IsEnabled = true,
            BackgroundColor = "#123456",
            Prompt = "Unrelated",
            IsUserInputRequired = true
        };
        var result = await SynchronizeManagedRewardAsync(
            vm,
            target,
            CreateManagedRewardCatalog(unrelatedReward));

        Assert.False(result.Changed);
        Assert.Equal("missing-stable-id", GetPropertyValue<string>(target, "RewardId"));
        Assert.Empty(GetPropertyValue<string>(target, "RewardTitle"));
        Assert.Empty(result.ClaimedRewardIds);
        Assert.Equal(0, result.Creates);
        Assert.Equal(0, result.Updates);
        Assert.Equal(0, result.Deletes);
        Assert.Empty(handler.Requests);
        Assert.Equal("VRC: Unrelated Reward", unrelatedReward.Title);
        Assert.Equal(999, unrelatedReward.Cost);
        Assert.True(unrelatedReward.IsEnabled);
        Assert.Equal("#123456", unrelatedReward.BackgroundColor);
        Assert.Equal("Unrelated", unrelatedReward.Prompt);
        Assert.True(unrelatedReward.IsUserInputRequired);
    }

    [Fact]
    public async Task UniversalRewardReadiness_EmptyLinkedIdNeedsFixWhileManagedTitleStaysActive()
    {
        await using var parent = new MainWindowViewModel();
        var unlinkedRule = CreateConfiguredUniversalRewardRule(
            "linked-id",
            "Retained Linked Title",
            TwitchRewardSyncMode.LinkExisting);
        var managedRule = CreateConfiguredUniversalRewardRule(
            string.Empty,
            "Managed Title",
            TwitchRewardSyncMode.CreateOrManage);
        parent.Settings.UniversalTriggers.Add(unlinkedRule);
        parent.Settings.UniversalTriggers.Add(managedRule);
        var manager = new UniversalTriggersManagerViewModel(parent.Settings, parent);

        var cards = manager.RewardSection.Cast<UniversalTriggerCardViewModel>().ToArray();
        Assert.True(unlinkedRule.IsConfigured);
        Assert.Equal(
            UniversalTriggerCardStatus.Ready,
            Assert.Single(cards, card => ReferenceEquals(card.Rule, unlinkedRule)).Status);
        manager.FilterMode = UniversalTriggerFilterMode.Active;
        Assert.Equal(2, manager.RewardSection.Cast<UniversalTriggerCardViewModel>().Count());

        parent.UnlinkTwitchRewardCommand.Execute(unlinkedRule);

        Assert.Empty(unlinkedRule.RewardId);
        Assert.Equal("Retained Linked Title", unlinkedRule.RewardTitle);
        Assert.False(unlinkedRule.IsTriggerFilterConfigured);
        Assert.False(unlinkedRule.IsConfigured);
        Assert.Equal(
            UniversalTriggerCardStatus.Warn,
            Assert.Single(cards, card => ReferenceEquals(card.Rule, unlinkedRule)).Status);
        Assert.True(managedRule.IsConfigured);
        Assert.Equal(
            UniversalTriggerCardStatus.Ready,
            Assert.Single(cards, card => ReferenceEquals(card.Rule, managedRule)).Status);
        Assert.Equal(1, manager.CountActive);
        Assert.Equal(1, manager.CountNeedsFix);
        Assert.Equal([managedRule], manager.RewardSection.Cast<UniversalTriggerCardViewModel>().Select(card => card.Rule));
        manager.FilterMode = UniversalTriggerFilterMode.NeedsFix;
        Assert.Equal([unlinkedRule], manager.RewardSection.Cast<UniversalTriggerCardViewModel>().Select(card => card.Rule));
    }

    [Fact]
    public async Task UniversalRewardReadiness_EmptyLinkedIdWithCommandFallbackStaysActiveAfterUnlink()
    {
        await using var parent = new MainWindowViewModel();
        var rule = CreateConfiguredUniversalRewardRule(
            "linked-id",
            "Retained Linked Title",
            TwitchRewardSyncMode.LinkExisting);
        rule.ChatCommandEnabled = true;
        rule.CommandText = "!fused";
        parent.Settings.UniversalTriggers.Add(rule);
        var manager = new UniversalTriggersManagerViewModel(parent.Settings, parent);
        var card = Assert.Single(manager.RewardSection.Cast<UniversalTriggerCardViewModel>());
        manager.FilterMode = UniversalTriggerFilterMode.Active;

        parent.UnlinkTwitchRewardCommand.Execute(rule);

        Assert.Empty(rule.RewardId);
        Assert.True(rule.IsTriggerFilterConfigured);
        Assert.True(rule.IsConfigured);
        Assert.Equal(UniversalTriggerCardStatus.Ready, card.Status);
        Assert.Equal(1, manager.CountActive);
        Assert.Equal(0, manager.CountNeedsFix);
        Assert.Same(rule, Assert.Single(manager.RewardSection.Cast<UniversalTriggerCardViewModel>()).Rule);
        Assert.Contains(
            BridgeRuntimeConfiguration.FromSettings(parent.Settings, RuntimeConfig.CreateDefault(), null).UniversalTriggers,
            snapshot => snapshot.Id == rule.Id);
    }

    [Fact]
    public async Task UniversalRewardReadiness_CreateOrManageIdOnlyStaysActive()
    {
        await using var parent = new MainWindowViewModel();
        var rule = CreateConfiguredUniversalRewardRule(
            "managed-id",
            string.Empty,
            TwitchRewardSyncMode.CreateOrManage);
        parent.Settings.UniversalTriggers.Add(rule);
        var manager = new UniversalTriggersManagerViewModel(parent.Settings, parent);

        Assert.True(rule.IsTriggerFilterConfigured);
        Assert.True(rule.IsConfigured);
        Assert.Equal(
            UniversalTriggerCardStatus.Ready,
            Assert.Single(manager.RewardSection.Cast<UniversalTriggerCardViewModel>()).Status);
        Assert.Equal(1, manager.CountActive);
        Assert.Equal(0, manager.CountNeedsFix);
    }

    [Fact]
    public async Task UniversalManagerDispose_DetachesRulesAndCollectionWithoutChangingSavedRules()
    {
        await using var parent = new MainWindowViewModel();
        var originalRule = CreateConfiguredUniversalRewardRule(
            "managed-id",
            "Managed Title",
            TwitchRewardSyncMode.CreateOrManage);
        parent.Settings.UniversalTriggers.Add(originalRule);
        var manager = new UniversalTriggersManagerViewModel(parent.Settings, parent);
        var originalCard = Assert.Single(manager.RewardSection.Cast<UniversalTriggerCardViewModel>());
        var disposable = Assert.IsAssignableFrom<IDisposable>(manager);

        disposable.Dispose();
        disposable.Dispose();
        var managerChanges = 0;
        var cardChanges = 0;
        manager.PropertyChanged += (_, _) => managerChanges++;
        originalCard.PropertyChanged += (_, _) => cardChanges++;

        originalRule.IsEnabled = false;
        var addedAfterClose = CreateConfiguredUniversalRewardRule(
            "second-id",
            "Second Rule",
            TwitchRewardSyncMode.CreateOrManage);
        parent.Settings.UniversalTriggers.Add(addedAfterClose);

        Assert.Equal(0, managerChanges);
        Assert.Equal(0, cardChanges);
        Assert.Empty(manager.RewardSection.Cast<UniversalTriggerCardViewModel>());
        Assert.Equal([originalRule, addedAfterClose], parent.Settings.UniversalTriggers);

        var reopened = new UniversalTriggersManagerViewModel(parent.Settings, parent);
        Assert.Equal(2, reopened.RewardSection.Cast<UniversalTriggerCardViewModel>().Count());
        Assert.IsAssignableFrom<IDisposable>(reopened).Dispose();
    }

    [Fact]
    public async Task UniversalManagerDispose_ReleasesSettingsAndRuleRoots()
    {
        await using var parent = new MainWindowViewModel();
        var rule = CreateConfiguredUniversalRewardRule(
            "managed-id",
            "Managed Title",
            TwitchRewardSyncMode.CreateOrManage);
        parent.Settings.UniversalTriggers.Add(rule);

        var managerReference = CreateDisposedUniversalManagerWeakReference(parent.Settings, parent);
        for (var attempt = 0; attempt < 3 && managerReference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(managerReference.IsAlive);
        Assert.Same(rule, Assert.Single(parent.Settings.UniversalTriggers));
    }

    [Fact]
    public async Task UniversalEditor_OpenSelectionSynchronizesRewardTitle()
    {
        await using var parent = new MainWindowViewModel();
        var rule = CreateConfiguredUniversalRewardRule(
            string.Empty,
            "Original Title",
            TwitchRewardSyncMode.LinkExisting);
        parent.Settings.UniversalTriggers.Add(rule);
        using var manager = new UniversalTriggersManagerViewModel(parent.Settings, parent);

        manager.OpenEditorCommand.Execute(rule);
        manager.AvailableTwitchRewards.Add(new TwitchApiClient.CustomRewardResponse
        {
            Id = "catalog-id",
            Title = "Catalog Title"
        });
        rule.RewardId = "catalog-id";

        Assert.Equal("Catalog Title", rule.RewardTitle);
    }

    [Fact]
    public async Task UniversalEditor_CloseDetachesRewardTitleSynchronization()
    {
        await using var parent = new MainWindowViewModel();
        var rule = CreateConfiguredUniversalRewardRule(
            string.Empty,
            "Original Title",
            TwitchRewardSyncMode.LinkExisting);
        parent.Settings.UniversalTriggers.Add(rule);
        using var manager = new UniversalTriggersManagerViewModel(parent.Settings, parent);
        manager.OpenEditorCommand.Execute(rule);
        manager.AvailableTwitchRewards.Add(new TwitchApiClient.CustomRewardResponse
        {
            Id = "catalog-id",
            Title = "Catalog Title"
        });

        manager.CloseEditorCommand.Execute(null);
        rule.RewardTitle = "After Close";
        rule.RewardId = "catalog-id";

        Assert.Equal("After Close", rule.RewardTitle);
        Assert.Equal(0, CountSelectedTriggerHandlers(rule, manager));
    }

    [Fact]
    public async Task UniversalEditor_SelectionClearAndReopenAttachesExactlyOnce()
    {
        await using var parent = new MainWindowViewModel();
        var rule = CreateConfiguredUniversalRewardRule(
            string.Empty,
            "Original Title",
            TwitchRewardSyncMode.LinkExisting);
        parent.Settings.UniversalTriggers.Add(rule);
        using var manager = new UniversalTriggersManagerViewModel(parent.Settings, parent);

        manager.OpenEditorCommand.Execute(rule);
        manager.SelectedTrigger = null;
        manager.OpenEditorCommand.Execute(rule);

        Assert.Equal(1, CountSelectedTriggerHandlers(rule, manager));
        manager.SelectedTrigger = null;
        rule.RewardTitle = "After Clear";
        manager.AvailableTwitchRewards.Add(new TwitchApiClient.CustomRewardResponse
        {
            Id = "catalog-id",
            Title = "Catalog Title"
        });
        rule.RewardId = "catalog-id";
        Assert.Equal("After Clear", rule.RewardTitle);
    }

    [Fact]
    public async Task UniversalEditor_DisposeAfterOpenReleasesSelectedRuleRoot()
    {
        await using var parent = new MainWindowViewModel();
        var rule = CreateConfiguredUniversalRewardRule(
            string.Empty,
            "Original Title",
            TwitchRewardSyncMode.LinkExisting);
        parent.Settings.UniversalTriggers.Add(rule);

        var managerReference = CreateDisposedOpenEditorManagerWeakReference(parent.Settings, parent, rule);
        for (var attempt = 0; attempt < 3 && managerReference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(managerReference.IsAlive);
        Assert.Same(rule, Assert.Single(parent.Settings.UniversalTriggers));
    }

    [Fact]
    public async Task UnlinkWardrobeMasterReward_ClearsIdAndPreservesTitle()
    {
        await using var vm = new MainWindowViewModel();
        var profile = new AvatarTriggerProfile
        {
            WardrobeMasterRewardId = "wardrobe-master-id",
            WardrobeMasterRewardTitle = "Wardrobe Master Title"
        };

        vm.UnlinkWardrobeMasterRewardCommand.Execute(profile);

        Assert.Empty(profile.WardrobeMasterRewardId);
        Assert.Equal("Wardrobe Master Title", profile.WardrobeMasterRewardTitle);
    }

    [Fact]
    public async Task UnlinkedRouletteReward_DoesNotReadoptCatalogRewardByTitle()
    {
        await using var vm = new MainWindowViewModel();
        var rule = new TriggerRule
        {
            Name = "Linked Roulette Reward",
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarRoulet,
            RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            ChannelPointRewardId = "linked-reward-id",
            ChannelPointRewardTitle = "Linked Reward Title"
        };
        vm.UnlinkTwitchRewardCommand.Execute(rule);
        Assert.Equal("Linked Reward Title", rule.ChannelPointRewardTitle);

        var target = CreateLinkedRouletteTarget(vm, rule);

        var rewardCatalog = CreateManagedRewardCatalog(new TwitchApiClient.CustomRewardResponse
        {
            Id = "different-reward-id",
            Title = "Linked Reward Title",
            Cost = 100,
            IsEnabled = true
        });
        var claimedRewardIds = new HashSet<string>(StringComparer.Ordinal);

        Assert.False(await SynchronizeLinkedRewardAsync(vm, target, rewardCatalog, claimedRewardIds));
        Assert.Empty(rule.ChannelPointRewardId);
        Assert.Equal("Linked Reward Title", rule.ChannelPointRewardTitle);
        Assert.Empty(claimedRewardIds);
    }

    [Fact]
    public async Task LinkedRewardSync_EmptyIdDoesNotAdoptMatchingCatalogTitle()
    {
        await using var vm = new MainWindowViewModel();
        var rule = new TriggerRule
        {
            Name = "Unlinked Roulette Reward",
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarRoulet,
            RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            ChannelPointRewardTitle = "Retained Reward Title"
        };
        var target = CreateLinkedRouletteTarget(vm, rule);
        var rewardCatalog = CreateManagedRewardCatalog(new TwitchApiClient.CustomRewardResponse
        {
            Id = "catalog-reward-id",
            Title = "Retained Reward Title",
            Cost = 100,
            IsEnabled = true
        });
        var claimedRewardIds = new HashSet<string>(StringComparer.Ordinal);

        Assert.False(await SynchronizeLinkedRewardAsync(vm, target, rewardCatalog, claimedRewardIds));
        Assert.Empty(rule.ChannelPointRewardId);
        Assert.Empty(claimedRewardIds);
    }

    [Theory]
    [InlineData("standard")]
    [InlineData("universal")]
    [InlineData("avatar-scale")]
    [InlineData("avatar-scale-master")]
    [InlineData("avatar-set-shared")]
    [InlineData("set-trigger-master")]
    [InlineData("wardrobe-outfit")]
    [InlineData("wardrobe-master")]
    public async Task LinkedRewardTargetFamily_EmptyIdDoesNotAdoptMatchingCatalogTitle(string family)
    {
        await using var vm = new MainWindowViewModel();
        var fixture = CreateEmptyIdLinkedTargetFixture(vm, family);
        var rewardCatalog = CreateManagedRewardCatalog(new TwitchApiClient.CustomRewardResponse
        {
            Id = $"catalog-{family}",
            Title = fixture.RewardTitle,
            Cost = 100,
            IsEnabled = true
        });
        var claimedRewardIds = new HashSet<string>(StringComparer.Ordinal);

        Assert.True(GetPropertyValue<bool>(fixture.Target, "UsesLinkedExistingReward"));
        Assert.Empty(GetPropertyValue<string>(fixture.Target, "RewardId"));
        Assert.False(await SynchronizeLinkedRewardAsync(vm, fixture.Target, rewardCatalog, claimedRewardIds));
        Assert.Empty(fixture.GetRewardId());
        Assert.Empty(claimedRewardIds);
    }

    [Fact]
    public async Task LinkedRouletteReward_MissingConfiguredIdDoesNotFallBackToTitle()
    {
        await using var vm = new MainWindowViewModel();
        var rule = new TriggerRule
        {
            Name = "Linked Roulette Reward",
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarRoulet,
            RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            ChannelPointRewardId = "configured-reward-id",
            ChannelPointRewardTitle = "Stale Reward Title"
        };
        var target = CreateLinkedRouletteTarget(vm, rule);
        var catalogReward = new TwitchApiClient.CustomRewardResponse
        {
            Id = "different-reward-id",
            Title = "Stale Reward Title",
            Cost = 999,
            IsEnabled = false
        };
        var rewardCatalog = CreateManagedRewardCatalog(catalogReward);
        var claimedRewardIds = new HashSet<string>(StringComparer.Ordinal);

        Assert.False(await SynchronizeLinkedRewardAsync(vm, target, rewardCatalog, claimedRewardIds));
        Assert.Equal("configured-reward-id", rule.ChannelPointRewardId);
        Assert.Empty(claimedRewardIds);
        Assert.Equal("Stale Reward Title", catalogReward.Title);
        Assert.Equal(999, catalogReward.Cost);
        Assert.False(catalogReward.IsEnabled);
    }

    [Fact]
    public async Task RouletteRewardProjection_IncludesLinkedListenOnlyAndManagedTargetsWithoutMutation()
    {
        await using var vm = new MainWindowViewModel();
        vm.Settings.MasterAvatarSwapReturnId = "avtr_return";

        var roulette = new AvatarRouletteProfile { Name = "Mixed Roulette" };
        roulette.Pool.Add(new RouletteAvatarEntry
        {
            AvatarId = "avtr_pool",
            AvatarName = "Pool Avatar"
        });
        var linkedRule = new TriggerRule
        {
            Name = "Linked Roulette Reward",
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarRoulet,
            RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            ChannelPointRewardId = "linked-roulette-reward",
            ChannelPointRewardTitle = "Linked Roulette Reward",
            ChannelPointRewardCost = 100,
            CooldownSeconds = 15,
            DeleteManagedRewardWhenInactive = true
        };
        var managedRule = new TriggerRule
        {
            Name = "Managed Roulette Reward",
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarRoulet,
            RewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            ChannelPointRewardId = "managed-roulette-reward",
            ChannelPointRewardTitle = "Managed Roulette Reward",
            ChannelPointRewardCost = 200
        };
        var chatRule = new TriggerRule
        {
            Name = "Chat Roulette",
            TriggerType = TwitchTriggerType.ChatCommand,
            ActionType = OscActionType.AvatarRoulet,
            RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            ChannelPointRewardId = "chat-rule-must-not-project"
        };
        roulette.Triggers.Add(linkedRule);
        roulette.Triggers.Add(managedRule);
        roulette.Triggers.Add(chatRule);
        vm.Settings.AvatarRouletteProfiles.Add(roulette);

        var projectionMethod = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "CreateManagedRewardTargetsForRouletteRules");
        var projectedTargets = AsObjects(projectionMethod.Invoke(vm, new object[]
        {
            "avtr_return",
            false,
            true,
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>()
        }));
        var linkedTarget = Assert.Single(
            projectedTargets,
            target => GetPropertyValue<Guid>(target, "Id") == linkedRule.Id);
        var managedTarget = Assert.Single(
            projectedTargets,
            target => GetPropertyValue<Guid>(target, "Id") == managedRule.Id);

        Assert.DoesNotContain(
            projectedTargets,
            target => GetPropertyValue<Guid>(target, "Id") == chatRule.Id);
        Assert.True(GetPropertyValue<bool>(linkedTarget, "UsesLinkedExistingReward"));
        Assert.Equal(
            TwitchRewardSyncMode.LinkExisting,
            GetPropertyValue<TwitchRewardSyncMode>(linkedTarget, "RewardSyncMode"));
        Assert.False(GetPropertyValue<bool>(managedTarget, "UsesLinkedExistingReward"));
        Assert.Equal(
            TwitchRewardSyncMode.CreateOrManage,
            GetPropertyValue<TwitchRewardSyncMode>(managedTarget, "RewardSyncMode"));
        Assert.True(GetPropertyValue<bool>(managedTarget, "DesiredEnabled"));

        var typedTargets = CreateTypedArray(projectedTargets);
        var protectedRewardIds = InvokeStaticMethod(
            "BuildManagedRewardCapReclaimProtectedRewardIds",
            typedTargets);
        var protectedTitleKeys = InvokeStaticMethod(
            "BuildManagedRewardCapReclaimProtectedTitleKeys",
            typedTargets);
        Assert.Contains(
            linkedRule.ChannelPointRewardId,
            Assert.IsAssignableFrom<IEnumerable<string>>(protectedRewardIds));
        Assert.Contains(
            ManagedRewardPresentation.NormalizeTitleIdentityKey(linkedRule.ChannelPointRewardTitle),
            Assert.IsAssignableFrom<IEnumerable<string>>(protectedTitleKeys));

        var existingReward = new TwitchApiClient.CustomRewardResponse
        {
            Id = linkedRule.ChannelPointRewardId,
            Title = "Viewer-Owned Roulette",
            Cost = 999,
            IsEnabled = false,
            IsGlobalCooldownEnabled = true,
            GlobalCooldownSeconds = 600,
            BackgroundColor = "#123456",
            Prompt = "Viewer controlled",
            IsUserInputRequired = true
        };
        var rewardCatalog = CreateManagedRewardCatalog(existingReward);
        var ownershipEntrySource = InvokeManagedRewardOwnershipEntries(vm);
        var ownershipIndex = CreateNestedInstance(
            "ManagedRewardRuleOwnershipIndex",
            ownershipEntrySource);
        var apiCallCounter = CreateNestedInstance("ManagedRewardApiCallCounter");
        var claimedRewardIds = new HashSet<string>(StringComparer.Ordinal);
        var synchronizeMethod = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "SynchronizeManagedRewardForTargetAsync");
        var synchronizeTask = Assert.IsType<Task<bool>>(synchronizeMethod.Invoke(vm, new object[]
        {
            linkedTarget,
            typedTargets,
            rewardCatalog,
            claimedRewardIds,
            Array.Empty<string>(),
            protectedRewardIds,
            protectedTitleKeys,
            CreateCompletedFactory(rewardCatalog),
            ownershipIndex,
            apiCallCounter,
            true,
            true,
            MainWindowViewModel.ManagedRewardSyncReason.SettingsEdit,
            Array.Empty<string>(),
            CancellationToken.None
        }));

        Assert.False(await synchronizeTask);
        Assert.Contains(existingReward.Id, claimedRewardIds);
        Assert.Equal(0, GetPropertyValue<int>(apiCallCounter, "Creates"));
        Assert.Equal(0, GetPropertyValue<int>(apiCallCounter, "Updates"));
        Assert.Equal(0, GetPropertyValue<int>(apiCallCounter, "Deletes"));
        Assert.Equal("Viewer-Owned Roulette", existingReward.Title);
        Assert.Equal(999, existingReward.Cost);
        Assert.False(existingReward.IsEnabled);
        Assert.Equal(600, existingReward.GlobalCooldownSeconds);
        Assert.Equal("#123456", existingReward.BackgroundColor);
        Assert.Equal("Viewer controlled", existingReward.Prompt);
        Assert.True(existingReward.IsUserInputRequired);
        Assert.Equal("linked-roulette-reward", linkedRule.ChannelPointRewardId);
        Assert.Equal("Linked Roulette Reward", linkedRule.ChannelPointRewardTitle);
        Assert.Equal(100, linkedRule.ChannelPointRewardCost);
    }

    [Fact]
    public async Task AvatarSwapManagedRewardOwnership_ExcludesAdvancedCommandRulesButKeepsChannelPointRules()
    {
        await using var vm = new MainWindowViewModel();
        var advancedRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChatCommand,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target",
            ChatCommandEnabled = true,
            ChatCommandText = "!swap",
            ChannelPointRewardId = "advanced-command-must-not-own-reward"
        };
        var channelPointRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target",
            ChannelPointRewardId = "channel-point-swap-reward"
        };
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_target" };
        profile.ChannelPointRules.Add(advancedRule);
        vm.Settings.AvatarSwapProfiles.Add(profile);

        var ownershipEntries = AsObjects(InvokeManagedRewardOwnershipEntries(vm));
        Assert.DoesNotContain(
            ownershipEntries,
            entry => GetPropertyValue<Guid>(entry, "Id") == advancedRule.Id);

        profile.ChannelPointRules.Add(channelPointRule);

        ownershipEntries = AsObjects(InvokeManagedRewardOwnershipEntries(vm));
        Assert.Contains(
            ownershipEntries,
            entry => GetPropertyValue<Guid>(entry, "Id") == channelPointRule.Id);
    }

    [Fact]
    public async Task AvatarSwapCooldownColorEligibility_ExcludesMisfiledCommandRulesButKeepsChannelPointRules()
    {
        await using var vm = new MainWindowViewModel();
        var misfiledCommandRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChatCommand,
            ActionType = OscActionType.AvatarChange,
            RewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            AvatarChangeTargetId = "avtr_target",
            ChannelPointRewardId = "misfiled-command-reward",
            ManagedRewardReadyColor = "#102030",
            ManagedRewardCooldownColor = "#405060"
        };
        var channelPointRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarChange,
            RewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            AvatarChangeTargetId = "avtr_target",
            ChannelPointRewardId = "channel-point-swap-reward",
            ManagedRewardReadyColor = "#203040",
            ManagedRewardCooldownColor = "#506070"
        };
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_target" };
        profile.ChannelPointRules.Add(misfiledCommandRule);
        profile.ChannelPointRules.Add(channelPointRule);
        vm.Settings.AvatarSwapProfiles.Add(profile);

        var ownershipMethod = GetInstanceMethod(typeof(MainWindowViewModel), "IsCreateOrManageRewardOwner");
        var rewardIdMethod = GetInstanceMethod(typeof(MainWindowViewModel), "ResolveManagedRewardIdForRule");
        var colorMethod = GetInstanceMethod(typeof(MainWindowViewModel), "ResolveConfiguredRewardColor");

        Assert.False(Assert.IsType<bool>(ownershipMethod.Invoke(vm, new object[] { misfiledCommandRule.Id })));
        Assert.Null(rewardIdMethod.Invoke(vm, new object[] { misfiledCommandRule.Id }));
        Assert.Null(colorMethod.Invoke(vm, new object[] { misfiledCommandRule.Id, false }));
        Assert.Null(colorMethod.Invoke(vm, new object[] { misfiledCommandRule.Id, true }));

        Assert.True(Assert.IsType<bool>(ownershipMethod.Invoke(vm, new object[] { channelPointRule.Id })));
        Assert.Equal(channelPointRule.ChannelPointRewardId, rewardIdMethod.Invoke(vm, new object[] { channelPointRule.Id }));
        Assert.Equal(channelPointRule.ManagedRewardReadyColor, colorMethod.Invoke(vm, new object[] { channelPointRule.Id, false }));
        Assert.Equal(channelPointRule.ManagedRewardCooldownColor, colorMethod.Invoke(vm, new object[] { channelPointRule.Id, true }));
    }

    [Fact]
    public void RuleListPaneViewModel_StoresKindAndTitle()
    {
        var pane = new RuleListPaneViewModel(RuleListPaneKind.Swap, "Avatar Name");
        Assert.Equal(RuleListPaneKind.Swap, pane.Kind);
        Assert.Equal("Avatar Name", pane.Title);
    }

    private static object InvokeManagedRewardOwnershipEntries(
        MainWindowViewModel vm,
        IReadOnlyCollection<UniversalTriggerRule>? managedUniversalTriggers = null)
    {
        var method = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "EnumerateManagedRewardOwnershipEntries");
        return method.Invoke(vm, new object?[]
        {
            Array.Empty<TriggerRule>(),
            managedUniversalTriggers ?? Array.Empty<UniversalTriggerRule>(),
            Array.Empty<AvatarScaleRule>(),
            null
        })!;
    }

    private static TriggerRule CreateSetTriggerMasterChild(string rewardId, int choiceNumber)
    {
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.SetTrigger,
            RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            ChannelPointRewardId = rewardId,
            ChannelPointRewardTitle = string.Empty,
            SharedRewardChoiceEnabled = true,
            SharedRewardChoiceNumber = choiceNumber
        };
        rule.SetTriggerActions.Add(new SetTriggerAction
        {
            ParameterName = "/avatar/parameters/Test",
            ParameterType = OscParameterType.Bool,
            ParameterValue = "true"
        });
        return rule;
    }

    private static UniversalTriggerRule CreateConfiguredUniversalRewardRule(
        string rewardId,
        string rewardTitle,
        TwitchRewardSyncMode rewardSyncMode)
    {
        var rule = new UniversalTriggerRule
        {
            TriggerType = UniversalTriggerType.ChannelPointReward,
            RewardId = rewardId,
            RewardTitle = rewardTitle,
            RewardSyncMode = rewardSyncMode
        };
        rule.Actions.Add(new UniversalTriggerAction
        {
            OscAddress = "/test",
            TargetValue = "true"
        });
        return rule;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CreateDisposedUniversalManagerWeakReference(
        AppSettings settings,
        MainWindowViewModel parent)
    {
        var manager = new UniversalTriggersManagerViewModel(settings, parent);
        Assert.IsAssignableFrom<IDisposable>(manager).Dispose();
        return new WeakReference(manager);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CreateDisposedOpenEditorManagerWeakReference(
        AppSettings settings,
        MainWindowViewModel parent,
        UniversalTriggerRule rule)
    {
        var manager = new UniversalTriggersManagerViewModel(settings, parent);
        manager.OpenEditorCommand.Execute(rule);
        manager.Dispose();
        return new WeakReference(manager);
    }

    private static int CountSelectedTriggerHandlers(
        UniversalTriggerRule rule,
        UniversalTriggersManagerViewModel manager)
    {
        var propertyChangedField = typeof(ObservableObject).GetField(
            "PropertyChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(propertyChangedField);
        var handlers = propertyChangedField.GetValue(rule) as MulticastDelegate;
        return handlers?.GetInvocationList().Count(handler =>
            ReferenceEquals(handler.Target, manager)
            && string.Equals(
                handler.Method.Name,
                "OnSelectedTriggerPropertyChanged",
                StringComparison.Ordinal)) ?? 0;
    }

    private static TriggerRuleSnapshot[] GetRuntimeRewardCandidates(
        IReadOnlyList<TriggerRuleSnapshot> rules,
        string rewardId,
        string rewardTitle)
    {
        var indexType = typeof(BridgeCoordinator).GetNestedType(
            "RuntimeRuleIndex",
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(indexType);
        var createMethod = indexType.GetMethod(
            "Create",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(createMethod);
        var index = createMethod.Invoke(null, new object[] { rules });
        Assert.NotNull(index);
        var getCandidatesMethod = indexType.GetMethod(
            "GetChannelPointCandidates",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(getCandidatesMethod);
        var candidates = getCandidatesMethod.Invoke(index, new object[] { rewardId, rewardTitle });
        return Assert.IsAssignableFrom<IEnumerable<TriggerRuleSnapshot>>(candidates).ToArray();
    }

    private static object CreateManagedRewardSyncTarget(
        string rewardId,
        string rewardTitle,
        TwitchRewardSyncMode rewardSyncMode,
        bool protectFromCapReclaim,
        bool deleteWhenInactive = false)
    {
        return CreateNestedInstance(
            "ManagedRewardSyncTarget",
            Guid.NewGuid(),
            rewardTitle,
            rewardId,
            rewardTitle,
            100,
            rewardSyncMode,
            0,
            ManagedRewardPresentation.ReadyBackgroundColor,
            string.Empty,
            false,
            true,
            false,
            deleteWhenInactive,
            protectFromCapReclaim,
            (Action<string>)(_ => { }),
            (Action<string>)(_ => { }));
    }

    private static object CreateLinkedRouletteTarget(MainWindowViewModel vm, TriggerRule rule)
    {
        var roulette = new AvatarRouletteProfile { Name = "Linked Roulette" };
        roulette.Pool.Add(new RouletteAvatarEntry
        {
            AvatarId = "avtr_pool",
            AvatarName = "Pool Avatar"
        });
        roulette.Triggers.Add(rule);
        vm.Settings.AvatarRouletteProfiles.Add(roulette);

        var createTargetMethod = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "CreateManagedRewardTargetForRouletteRule");
        var target = createTargetMethod.Invoke(vm, new object[]
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
        return target;
    }

    private static ManagedIdOnlyTargetFixture CreateManagedIdOnlyTargetFixture(
        MainWindowViewModel vm,
        string family)
    {
        const string rewardId = "managed-id-only";
        switch (family)
        {
            case "universal":
            {
                var rule = CreateConfiguredUniversalRewardRule(
                    rewardId,
                    string.Empty,
                    TwitchRewardSyncMode.CreateOrManage);
                rule.DeleteManagedRewardWhenInactive = true;
                var target = GetInstanceMethod(
                    typeof(MainWindowViewModel),
                    "CreateManagedRewardTargetForUniversalTrigger").Invoke(
                        vm,
                        new object[] { rule, true, string.Empty });
                Assert.NotNull(target);
                return new ManagedIdOnlyTargetFixture(
                    target,
                    rewardId,
                    () => rule.RewardId,
                    () => rule.RewardTitle);
            }
            case "avatar-scale":
            {
                var rule = new AvatarScaleRule
                {
                    TriggerType = AvatarScaleTriggerType.ChannelPointReward,
                    RewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
                    RewardId = rewardId,
                    RewardTitle = string.Empty,
                    DeleteManagedRewardWhenInactive = true
                };
                var target = GetInstanceMethod(
                    typeof(MainWindowViewModel),
                    "CreateManagedRewardTargetForAvatarScaleRule").Invoke(vm, new object[]
                    {
                        rule,
                        true,
                        Array.Empty<Guid>(),
                        Array.Empty<Guid>(),
                        Array.Empty<Guid>(),
                        Array.Empty<Guid>(),
                        false,
                        false,
                        true
                    });
                Assert.NotNull(target);
                return new ManagedIdOnlyTargetFixture(
                    target,
                    rewardId,
                    () => rule.RewardId,
                    () => rule.RewardTitle);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown managed ID-only family.");
        }
    }

    private static SameProfileMasterTargetFixture CreateSameProfileMasterTargetFixture(
        MainWindowViewModel vm,
        string setTriggerRewardId,
        string wardrobeRewardId,
        string setTriggerTitle,
        string wardrobeTitle)
    {
        var profile = new AvatarTriggerProfile
        {
            AvatarId = "avtr_test",
            UseSharedNumberedOutfitReward = true,
            SetTriggerMasterRewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            SetTriggerMasterRewardId = setTriggerRewardId,
            SetTriggerMasterRewardTitle = setTriggerTitle,
            UseWardrobeMode = true,
            UseWardrobeMasterReward = true,
            WardrobeMasterRewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            WardrobeMasterRewardId = wardrobeRewardId,
            WardrobeMasterRewardTitle = wardrobeTitle
        };
        var setTriggerRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.SetTrigger,
            RewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            ChannelPointRewardId = setTriggerRewardId,
            ChannelPointRewardTitle = setTriggerTitle,
            SharedRewardChoiceEnabled = true,
            SharedRewardChoiceNumber = 1
        };
        setTriggerRule.SetTriggerActions.Add(new SetTriggerAction
        {
            ParameterName = "/avatar/parameters/Test",
            ParameterType = OscParameterType.Bool,
            ParameterValue = "true"
        });
        profile.ChannelPointRules.Add(setTriggerRule);
        vm.Settings.AvatarProfiles.Add(profile);

        var setTriggerTarget = CreateSharedAvatarSetTarget(vm, profile, setTriggerRule);
        var wardrobeTarget = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "CreateManagedRewardTargetForWardrobeMasterReward").Invoke(
                vm,
                new object[] { profile, true });
        Assert.NotNull(wardrobeTarget);
        return new SameProfileMasterTargetFixture(
            profile,
            setTriggerRule,
            setTriggerTarget,
            wardrobeTarget);
    }

    private static LinkedTargetFixture CreateEmptyIdLinkedTargetFixture(
        MainWindowViewModel vm,
        string family)
    {
        var rewardTitle = $"Retained {family} Title";
        var emptyRuleIdCollections = new object[]
        {
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>()
        };

        switch (family)
        {
            case "standard":
            {
                var profile = new AvatarTriggerProfile { AvatarId = "avtr_test" };
                var rule = new TriggerRule
                {
                    TriggerType = TwitchTriggerType.ChannelPoints,
                    RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
                    ChannelPointRewardTitle = rewardTitle
                };
                var target = GetInstanceMethod(
                    typeof(MainWindowViewModel),
                    "CreateManagedRewardTargetForRule").Invoke(vm, new object?[]
                    {
                        profile,
                        rule,
                        "avtr_test",
                        false,
                        true,
                        emptyRuleIdCollections[0],
                        emptyRuleIdCollections[1],
                        emptyRuleIdCollections[2],
                        emptyRuleIdCollections[3]
                    });
                Assert.NotNull(target);
                return new LinkedTargetFixture(target, () => rule.ChannelPointRewardId, rewardTitle);
            }
            case "universal":
            {
                var trigger = new UniversalTriggerRule
                {
                    TriggerType = UniversalTriggerType.ChannelPointReward,
                    RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
                    RewardTitle = rewardTitle
                };
                trigger.Actions.Add(new UniversalTriggerAction
                {
                    OscAddress = "/avatar/parameters/Test",
                    TargetValue = "1"
                });
                var target = GetInstanceMethod(
                    typeof(MainWindowViewModel),
                    "CreateManagedRewardTargetForUniversalTrigger").Invoke(
                        vm,
                        new object[] { trigger, true, "avtr_test" });
                Assert.NotNull(target);
                return new LinkedTargetFixture(target, () => trigger.RewardId, rewardTitle);
            }
            case "avatar-scale":
            {
                var rule = new AvatarScaleRule
                {
                    TriggerType = AvatarScaleTriggerType.ChannelPointReward,
                    RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
                    RewardTitle = rewardTitle
                };
                var target = GetInstanceMethod(
                    typeof(MainWindowViewModel),
                    "CreateManagedRewardTargetForAvatarScaleRule").Invoke(vm, new object[]
                    {
                        rule,
                        true,
                        Array.Empty<Guid>(),
                        Array.Empty<Guid>(),
                        Array.Empty<Guid>(),
                        Array.Empty<Guid>(),
                        false,
                        false,
                        true
                    });
                Assert.NotNull(target);
                return new LinkedTargetFixture(target, () => rule.RewardId, rewardTitle);
            }
            case "avatar-scale-master":
            {
                var master = new AvatarScaleMasterRewardSettings
                {
                    RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
                    RewardTitle = rewardTitle
                };
                var target = GetInstanceMethod(
                    typeof(MainWindowViewModel),
                    "CreateManagedRewardTargetForAvatarScaleMasterReward").Invoke(
                        vm,
                        new object[] { master, true, false, false });
                Assert.NotNull(target);
                return new LinkedTargetFixture(target, () => master.RewardId, rewardTitle);
            }
            case "avatar-set-shared":
            {
                var profile = new AvatarTriggerProfile { AvatarId = "avtr_test" };
                var rule = new TriggerRule
                {
                    TriggerType = TwitchTriggerType.ChannelPoints,
                    ActionType = OscActionType.AvatarParameter,
                    RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
                    ChannelPointRewardTitle = rewardTitle,
                    SharedRewardChoiceEnabled = true,
                    SharedRewardChoiceNumber = 1
                };
                profile.ChannelPointRules.Add(rule);
                var target = CreateSharedAvatarSetTarget(vm, profile, rule);
                return new LinkedTargetFixture(target, () => rule.ChannelPointRewardId, rewardTitle);
            }
            case "set-trigger-master":
            {
                var profile = new AvatarTriggerProfile
                {
                    AvatarId = "avtr_test",
                    UseSharedNumberedOutfitReward = true,
                    SetTriggerMasterRewardSyncMode = TwitchRewardSyncMode.LinkExisting,
                    SetTriggerMasterRewardTitle = rewardTitle
                };
                var rule = new TriggerRule
                {
                    TriggerType = TwitchTriggerType.ChannelPoints,
                    ActionType = OscActionType.SetTrigger,
                    SharedRewardChoiceEnabled = true,
                    SharedRewardChoiceNumber = 1,
                    ChannelPointRewardTitle = rewardTitle
                };
                rule.SetTriggerActions.Add(new SetTriggerAction
                {
                    ParameterName = "/avatar/parameters/Test",
                    ParameterType = OscParameterType.Bool,
                    ParameterValue = "true"
                });
                profile.ChannelPointRules.Add(rule);
                var target = CreateSharedAvatarSetTarget(vm, profile, rule);
                return new LinkedTargetFixture(target, () => profile.SetTriggerMasterRewardId, rewardTitle);
            }
            case "wardrobe-outfit":
            {
                var profile = new AvatarTriggerProfile { AvatarId = "avtr_test" };
                var outfit = new WardrobeOutfit
                {
                    TwitchRewardSyncMode = TwitchRewardSyncMode.LinkExisting,
                    TwitchRewardTitle = rewardTitle
                };
                var target = GetInstanceMethod(
                    typeof(MainWindowViewModel),
                    "CreateManagedRewardTargetForWardrobeOutfit").Invoke(
                        vm,
                        new object[] { profile, outfit, true });
                Assert.NotNull(target);
                return new LinkedTargetFixture(target, () => outfit.TwitchRewardId, rewardTitle);
            }
            case "wardrobe-master":
            {
                var profile = new AvatarTriggerProfile
                {
                    AvatarId = "avtr_test",
                    WardrobeMasterRewardSyncMode = TwitchRewardSyncMode.LinkExisting,
                    WardrobeMasterRewardTitle = rewardTitle
                };
                var target = GetInstanceMethod(
                    typeof(MainWindowViewModel),
                    "CreateManagedRewardTargetForWardrobeMasterReward").Invoke(
                        vm,
                        new object[] { profile, true });
                Assert.NotNull(target);
                return new LinkedTargetFixture(target, () => profile.WardrobeMasterRewardId, rewardTitle);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown linked target family.");
        }
    }

    private static object CreateSharedAvatarSetTarget(
        MainWindowViewModel vm,
        AvatarTriggerProfile profile,
        TriggerRule rule)
    {
        var enumerateMethod = typeof(MainWindowViewModel).GetMethod(
            "EnumerateSharedAvatarSetRewardGroups",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(enumerateMethod);
        var group = Assert.Single(AsObjects(enumerateMethod.Invoke(null, new object[] { profile })));
        var target = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "CreateManagedRewardTargetForSharedAvatarSetRewardGroup").Invoke(vm, new object[]
            {
                profile,
                group,
                "avtr_test",
                false,
                true,
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                Array.Empty<Guid>()
            });
        Assert.NotNull(target);
        Assert.Equal(rule.Id, GetPropertyValue<TriggerRule>(group, "Owner").Id);
        return target;
    }

    private static Task<bool> SynchronizeLinkedRewardAsync(
        MainWindowViewModel vm,
        object target,
        object rewardCatalog,
        HashSet<string> claimedRewardIds)
    {
        var synchronizeMethod = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "SynchronizeLinkedExistingRewardForTargetAsync");
        return Assert.IsType<Task<bool>>(synchronizeMethod.Invoke(vm, new object[]
        {
            target,
            null!,
            rewardCatalog,
            claimedRewardIds,
            CancellationToken.None
        }));
    }

    private static async Task<ManagedSyncResult> SynchronizeManagedRewardAsync(
        MainWindowViewModel vm,
        object target,
        object rewardCatalog)
    {
        return await SynchronizeManagedRewardsAsync(
            vm,
            [target],
            rewardCatalog,
            InvokeManagedRewardOwnershipEntries(vm));
    }

    private static async Task<ManagedSyncResult> SynchronizeManagedRewardsAsync(
        MainWindowViewModel vm,
        IReadOnlyList<object> targets,
        object rewardCatalog,
        object ownershipEntries,
        bool allowInactiveRewardDeletion = true,
        MainWindowViewModel.ManagedRewardSyncReason reason = MainWindowViewModel.ManagedRewardSyncReason.SettingsEdit)
    {
        var allTargets = CreateTypedArray(targets);
        var ownershipIndex = CreateNestedInstance(
            "ManagedRewardRuleOwnershipIndex",
            ownershipEntries);
        var knownManagedRewardIds = AsObjects(ownershipEntries)
            .Select(entry => GetPropertyValue<string>(entry, "RewardId").Trim())
            .Where(rewardId => !string.IsNullOrWhiteSpace(rewardId))
            .ToHashSet(StringComparer.Ordinal);
        var apiCallCounter = CreateNestedInstance("ManagedRewardApiCallCounter");
        var claimedRewardIds = new HashSet<string>(StringComparer.Ordinal);
        var preHydrateMethod = typeof(MainWindowViewModel).GetMethod(
            "PreHydrateManagedRewardTargetTitles",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(preHydrateMethod);
        var changed = Assert.IsType<bool>(preHydrateMethod.Invoke(
            null,
            new object[] { allTargets, rewardCatalog, ownershipIndex }));
        var synchronizeMethod = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "SynchronizeManagedRewardForTargetAsync");
        foreach (var target in targets)
        {
            var synchronizeTask = Assert.IsAssignableFrom<Task<bool>>(synchronizeMethod.Invoke(vm, new object[]
            {
                target,
                allTargets,
                rewardCatalog,
                claimedRewardIds,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                CreateCompletedFactory(rewardCatalog),
                ownershipIndex,
                apiCallCounter,
                allowInactiveRewardDeletion,
                true,
                reason,
                knownManagedRewardIds,
                CancellationToken.None
            }));
            changed |= await synchronizeTask;
        }

        return new ManagedSyncResult(
            changed,
            claimedRewardIds,
            GetPropertyValue<int>(apiCallCounter, "Creates"),
            GetPropertyValue<int>(apiCallCounter, "Updates"),
            GetPropertyValue<int>(apiCallCounter, "Deletes"));
    }

    private static async Task<ManagedSyncResult> TryRecycleManagedRewardForCapacityAsync(
        MainWindowViewModel vm,
        object target,
        Array allTargets,
        object rewardCatalog,
        object manageableCatalog,
        IReadOnlyCollection<string> protectedRewardIds,
        object ownershipEntries)
    {
        var ownershipIndex = CreateNestedInstance(
            "ManagedRewardRuleOwnershipIndex",
            ownershipEntries);
        var knownManagedRewardIds = AsObjects(ownershipEntries)
            .Select(entry => GetPropertyValue<string>(entry, "RewardId").Trim())
            .Where(rewardId => !string.IsNullOrWhiteSpace(rewardId))
            .ToHashSet(StringComparer.Ordinal);
        var apiCallCounter = CreateNestedInstance("ManagedRewardApiCallCounter");
        var claimedRewardIds = new HashSet<string>(StringComparer.Ordinal);
        var recycleMethod = GetInstanceMethod(
            typeof(MainWindowViewModel),
            "TryRecycleManagedRewardForCapacityAsync");
        var recycleTask = Assert.IsAssignableFrom<Task>(recycleMethod.Invoke(vm, new object[]
        {
            target,
            allTargets,
            rewardCatalog,
            claimedRewardIds,
            protectedRewardIds,
            Array.Empty<string>(),
            CreateCompletedFactory(manageableCatalog),
            ownershipIndex,
            knownManagedRewardIds,
            apiCallCounter,
            "VRC: Needed Managed Reward",
            100,
            true,
            0,
            ManagedRewardPresentation.ReadyBackgroundColor,
            "Managed by Crystal Relay.",
            false,
            CancellationToken.None
        }));
        await recycleTask;
        var result = recycleTask.GetType().GetProperty("Result")?.GetValue(recycleTask);
        Assert.NotNull(result);
        var changedField = result.GetType().GetField("Item2");
        Assert.NotNull(changedField);

        return new ManagedSyncResult(
            Assert.IsType<bool>(changedField.GetValue(result)),
            claimedRewardIds,
            GetPropertyValue<int>(apiCallCounter, "Creates"),
            GetPropertyValue<int>(apiCallCounter, "Updates"),
            GetPropertyValue<int>(apiCallCounter, "Deletes"));
    }

    private static RecordingTwitchHandler InstallRecordingTwitchHandler(MainWindowViewModel vm)
    {
        var handler = new RecordingTwitchHandler();
        var twitchApiClient = new TwitchApiClient();
        var apiHttpClientField = typeof(TwitchApiClient).GetField(
            "httpClient",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(apiHttpClientField);
        var originalHttpClient = Assert.IsType<HttpClient>(apiHttpClientField.GetValue(twitchApiClient));
        apiHttpClientField.SetValue(twitchApiClient, new HttpClient(handler));
        originalHttpClient.Dispose();

        var vmApiClientField = typeof(MainWindowViewModel).GetField(
            "twitchApiClient",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(vmApiClientField);
        var originalApiClient = Assert.IsType<TwitchApiClient>(vmApiClientField.GetValue(vm));
        vmApiClientField.SetValue(vm, twitchApiClient);
        originalApiClient.Dispose();
        return handler;
    }

    private static object CreateManagedRewardCatalog(params TwitchApiClient.CustomRewardResponse[] rewards) =>
        CreateNestedInstance("ManagedRewardSyncCatalog", new object[] { rewards });

    private static TwitchApiClient.CustomRewardResponse CreateRewardMatchingTarget(
        object target,
        string rewardId,
        string rewardTitle)
    {
        var cooldownSeconds = GetPropertyValue<int>(target, "CooldownSeconds");
        return new TwitchApiClient.CustomRewardResponse
        {
            Id = rewardId,
            Title = rewardTitle,
            Cost = GetPropertyValue<int>(target, "RewardCost"),
            IsEnabled = GetPropertyValue<bool>(target, "DesiredEnabled"),
            IsGlobalCooldownEnabled = cooldownSeconds > 0,
            GlobalCooldownSeconds = cooldownSeconds,
            BackgroundColor = GetPropertyValue<string>(target, "BackgroundColor"),
            Prompt = GetPropertyValue<string>(target, "Prompt"),
            IsUserInputRequired = GetPropertyValue<bool>(target, "RequireUserInput")
        };
    }

    private static object CreateNestedInstance(string typeName, params object[] arguments)
    {
        var nestedType = typeof(MainWindowViewModel).GetNestedType(
            typeName,
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(nestedType);
        var constructor = Assert.Single(nestedType.GetConstructors(
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic));
        return constructor.Invoke(arguments);
    }

    private static object CreateManagedRewardOwnershipEntry(
        Guid id,
        string rewardId,
        string rewardTitle,
        TwitchRewardSyncMode rewardSyncMode)
    {
        var nestedType = typeof(MainWindowViewModel).GetNestedType(
            "ManagedRewardOwnershipEntry",
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(nestedType);
        var constructor = Assert.Single(
            nestedType.GetConstructors(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic),
            candidate => candidate.GetParameters().Length == 4);
        return constructor.Invoke(new object[] { id, rewardId, rewardTitle, rewardSyncMode });
    }

    private static System.Reflection.MethodInfo GetInstanceMethod(Type type, string methodName)
    {
        var method = type.GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method;
    }

    private static object InvokeStaticMethod(string methodName, params object[] arguments)
    {
        var method = typeof(MainWindowViewModel).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method.Invoke(null, arguments)!;
    }

    private static void InvokeInstanceMethod(MainWindowViewModel instance, string methodName, params object[] arguments) =>
        GetInstanceMethod(typeof(MainWindowViewModel), methodName).Invoke(instance, arguments);

    private static object[] AsObjects(object? source) =>
        Assert.IsAssignableFrom<System.Collections.IEnumerable>(source).Cast<object>().ToArray();

    private static Array CreateTypedArray(IReadOnlyList<object> values)
    {
        var elementType = Assert.Single(values.Select(value => value.GetType()).Distinct());
        var result = Array.CreateInstance(elementType, values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            result.SetValue(values[index], index);
        }

        return result;
    }

    private static T GetPropertyValue<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(
            propertyName,
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(property);
        return Assert.IsType<T>(property.GetValue(instance));
    }

    private static void SetPrivateField<T>(object instance, string fieldName, T value)
    {
        var field = instance.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(instance, value);
    }

    private static object CreateCompletedFactory(object value)
    {
        var method = typeof(AvatarSwapManagerViewModelTests).GetMethod(
            nameof(CreateCompletedFactoryCore),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method.MakeGenericMethod(value.GetType()).Invoke(null, new[] { value })!;
    }

    private static Func<CancellationToken, Task<T>> CreateCompletedFactoryCore<T>(T value) =>
        _ => Task.FromResult(value);

    private sealed record LinkedTargetFixture(
        object Target,
        Func<string> GetRewardId,
        string RewardTitle);

    private sealed record ManagedIdOnlyTargetFixture(
        object Target,
        string RewardId,
        Func<string> GetRewardId,
        Func<string> GetRewardTitle);

    private sealed record SameProfileMasterTargetFixture(
        AvatarTriggerProfile Profile,
        TriggerRule SetTriggerRule,
        object SetTriggerTarget,
        object WardrobeTarget);

    private sealed record ManagedSyncResult(
        bool Changed,
        HashSet<string> ClaimedRewardIds,
        int Creates,
        int Updates,
        int Deletes);

    private sealed class RecordingTwitchHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Uri)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri?.ToString() ?? string.Empty));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class StubTwitchRewardSource : ITwitchRewardSource
    {
        public ObservableCollection<TwitchRewardOption> RewardOptions { get; } = new();
        public ObservableCollection<TwitchPowerUpOption> PowerUpOptions { get; } = new();
        public ICommand RefreshTwitchRewardsCommand { get; } = new RelayCommand(() => { });
        public ICommand UnlinkTwitchRewardCommand { get; } = new RelayCommand(p => { });
    }
}
