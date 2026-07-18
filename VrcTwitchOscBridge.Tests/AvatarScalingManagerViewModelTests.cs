using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarScalingManagerViewModelTests
{
    [Fact]
    public void Constructor_BuildsRewardCardsFromScaleSets()
    {
        var settings = new AppSettings();
        var set = new AvatarScaleSet { Name = "Default Scale Set" };
        set.ScaleRules.Add(new AvatarScaleRule
        {
            Name = "Grow Big",
            TriggerType = AvatarScaleTriggerType.ChannelPointReward,
            RewardTitle = "Grow Big"
        });
        settings.AvatarScaleSets.Add(set);

        var vm = new AvatarScalingManagerViewModel(settings, null);

        var card = Assert.Single(vm.TwitchRewardCards);
        Assert.Equal(AvatarScalingSourceKind.TwitchReward, card.Kind);
        Assert.Equal("Grow Big", card.Title);
        Assert.Contains("Current max height allowed: 100m", card.SafetySummary);
    }

    [Fact]
    public void Constructor_GroupsTwitchRewardCardsByScaleSet()
    {
        var settings = new AppSettings();
        var firstSet = new AvatarScaleSet { Name = "Default Scale Set" };
        firstSet.ScaleRules.Add(new AvatarScaleRule
        {
            Name = "Grow Big",
            TriggerType = AvatarScaleTriggerType.ChannelPointReward,
            RewardTitle = "Grow Big"
        });
        var secondSet = new AvatarScaleSet { Name = "Chaos Scale Set" };
        secondSet.ScaleRules.Add(new AvatarScaleRule
        {
            Name = "Random Size",
            TriggerType = AvatarScaleTriggerType.ChatCommand,
            CommandText = "!randomsize"
        });
        settings.AvatarScaleSets.Add(firstSet);
        settings.AvatarScaleSets.Add(secondSet);

        var vm = new AvatarScalingManagerViewModel(settings, null);

        Assert.Collection(
            vm.TwitchScaleSetGroups,
            group =>
            {
                Assert.Same(firstSet, group.ScaleSet);
                Assert.Equal("Default Scale Set", group.Title);
                Assert.Single(group.Cards);
            },
            group =>
            {
                Assert.Same(secondSet, group.ScaleSet);
                Assert.Equal("Chaos Scale Set", group.Title);
                Assert.Single(group.Cards);
            });
    }

    [Fact]
    public void ScaleSetGroup_CountTextIsLocalized()
    {
        try
        {
            LocalizationService.Initialize(AppLanguage.Spanish);
            var settings = new AppSettings();
            var set = new AvatarScaleSet { Name = "Default Scale Set" };
            set.ScaleRules.Add(new AvatarScaleRule
            {
                Name = "Grow Big",
                TriggerType = AvatarScaleTriggerType.ChannelPointReward,
                RewardTitle = "Grow Big"
            });
            set.ScaleRules.Add(new AvatarScaleRule
            {
                Name = "Shrink Small",
                TriggerType = AvatarScaleTriggerType.ChatCommand,
                CommandText = "!small"
            });
            settings.AvatarScaleSets.Add(set);

            var vm = new AvatarScalingManagerViewModel(settings, null);

            Assert.Equal("2 recompensas", vm.TwitchScaleSetGroups.Single().CountText);
        }
        finally
        {
            LocalizationService.Initialize(AppLanguage.English);
        }
    }

    [Fact]
    public void Constructor_LeavesEditorEmptyUntilUserClicksEdit()
    {
        var settings = new AppSettings();
        var set = new AvatarScaleSet { Name = "Default Scale Set" };
        var rule = new AvatarScaleRule
        {
            Name = "Grow Big",
            TriggerType = AvatarScaleTriggerType.ChannelPointReward,
            RewardTitle = "Grow Big"
        };
        set.ScaleRules.Add(rule);
        settings.AvatarScaleSets.Add(set);

        var vm = new AvatarScalingManagerViewModel(settings, null);

        Assert.False(vm.IsEditorOpen);
        Assert.Null(vm.SelectedAvatarScaleRule);
        Assert.Null(vm.SelectedCard);
        Assert.False(vm.HasSelectedCard);
        Assert.True(vm.HasNoSelectedCard);
    }

    [Fact]
    public void Constructor_ShowsOnlyAvatarScalingCashAndPowerUpCards()
    {
        var settings = new AppSettings();
        settings.CashPaymentRules.Add(new CashPaymentRule { Name = "Tip Scale", ActionKind = CashPaymentActionKind.AvatarScaling });
        settings.CashPaymentRules.Add(new CashPaymentRule { Name = "Tip OSC", ActionKind = CashPaymentActionKind.TriggerAction });
        settings.PowerUpRules.Add(new PowerUpRule { Name = "Power Scale", ActionKind = PowerUpActionKind.AvatarScaling });
        settings.PowerUpRules.Add(new PowerUpRule { Name = "Power OSC", ActionKind = PowerUpActionKind.TriggerAction });

        var vm = new AvatarScalingManagerViewModel(settings, null);

        Assert.Single(vm.CashPaymentCards);
        Assert.Equal("Tip Scale", vm.CashPaymentCards.Single().Title);
        Assert.Single(vm.PowerUpCards);
        Assert.Equal("Power Scale", vm.PowerUpCards.Single().Title);
    }

    [Fact]
    public async Task OpenEditorCommand_DoesNotExposeCashOrPowerUpScaleActionsAsChildRewardRules()
    {
        await using var parent = new MainWindowViewModel();
        var staleRule = new AvatarScaleRule { Name = "Old Grow", RewardTitle = "Old Grow" };
        parent.SelectedAvatarScaleRule = staleRule;
        parent.Settings.CashPaymentRules.Add(new CashPaymentRule
        {
            Name = "Tip Scale",
            ActionKind = CashPaymentActionKind.AvatarScaling
        });
        parent.Settings.PowerUpRules.Add(new PowerUpRule
        {
            Name = "Power Scale",
            ActionKind = PowerUpActionKind.AvatarScaling
        });
        using var vm = new AvatarScalingManagerViewModel(parent.Settings, parent);

        vm.OpenEditorCommand.Execute(vm.CashPaymentCards.Single());

        Assert.Same(vm.CashPaymentCards.Single(), vm.SelectedCard);
        Assert.Null(vm.SelectedAvatarScaleRule);
        Assert.Null(parent.SelectedAvatarScaleRule);

        parent.SelectedAvatarScaleRule = staleRule;
        vm.OpenEditorCommand.Execute(vm.PowerUpCards.Single());

        Assert.Same(vm.PowerUpCards.Single(), vm.SelectedCard);
        Assert.Null(vm.SelectedAvatarScaleRule);
        Assert.Null(parent.SelectedAvatarScaleRule);
    }

    [Fact]
    public void OpenEditorCommand_SelectsCardAndOpensSidePanel()
    {
        var settings = new AppSettings();
        var set = new AvatarScaleSet();
        set.ScaleRules.Add(new AvatarScaleRule { Name = "Grow Big", RewardTitle = "Grow Big" });
        settings.AvatarScaleSets.Add(set);
        var vm = new AvatarScalingManagerViewModel(settings, null);
        var card = vm.TwitchRewardCards.Single();

        vm.OpenEditorCommand.Execute(card);

        Assert.True(vm.IsEditorOpen);
        Assert.Same(card, vm.SelectedCard);
        Assert.Same(card.ScaleRule, vm.SelectedAvatarScaleRule);
    }

    [Fact]
    public async Task OpenEditorCommand_WithParentMainWindow_SyncsSelectedScaleRuleAndOwnerSet()
    {
        await using var parent = new MainWindowViewModel();
        var staleSet = new AvatarScaleSet { Name = "Old Set" };
        var staleRule = new AvatarScaleRule { Name = "Old Grow", RewardTitle = "Old Grow" };
        staleSet.ScaleRules.Add(staleRule);
        var targetSet = new AvatarScaleSet { Name = "Target Set" };
        var targetRule = new AvatarScaleRule { Name = "Grow Big", RewardTitle = "Grow Big" };
        targetSet.ScaleRules.Add(targetRule);
        parent.Settings.AvatarScaleSets.Add(staleSet);
        parent.Settings.AvatarScaleSets.Add(targetSet);
        parent.SelectedAvatarScaleSet = staleSet;
        parent.SelectedAvatarScaleRule = staleRule;
        using var vm = new AvatarScalingManagerViewModel(parent.Settings, parent);
        var card = vm.TwitchRewardCards.Single(card => ReferenceEquals(card.ScaleRule, targetRule));

        vm.OpenEditorCommand.Execute(card);

        Assert.Same(targetRule, parent.SelectedAvatarScaleRule);
        Assert.Same(targetSet, parent.SelectedAvatarScaleSet);
    }

    [Fact]
    public async Task OpenEditorCommand_EnablesScaleSetCommandsOnlyForScaleSetOwnedCardsAndClearsStaleParentSet()
    {
        await using var parent = new MainWindowViewModel();
        var staleSet = new AvatarScaleSet { Name = "Old Set" };
        staleSet.ScaleRules.Add(new AvatarScaleRule { Name = "Old Grow", RewardTitle = "Old Grow" });
        var targetSet = new AvatarScaleSet { Name = "Target Set" };
        var targetRule = new AvatarScaleRule { Name = "Grow Big", RewardTitle = "Grow Big" };
        targetSet.ScaleRules.Add(targetRule);
        var cashRule = new CashPaymentRule { Name = "Tip Scale", ActionKind = CashPaymentActionKind.AvatarScaling };
        var powerRule = new PowerUpRule { Name = "Power Scale", ActionKind = PowerUpActionKind.AvatarScaling };
        parent.Settings.AvatarScaleSets.Add(staleSet);
        parent.Settings.AvatarScaleSets.Add(targetSet);
        parent.Settings.CashPaymentRules.Add(cashRule);
        parent.Settings.PowerUpRules.Add(powerRule);
        parent.SelectedAvatarScaleSet = staleSet;
        using var vm = new AvatarScalingManagerViewModel(parent.Settings, parent);

        vm.OpenEditorCommand.Execute(vm.TwitchRewardCards.Single(card => ReferenceEquals(card.ScaleRule, targetRule)));

        Assert.True(GetRequiredBoolProperty(vm, "SelectedCardUsesScaleSetCommands"));
        Assert.Same(targetSet, parent.SelectedAvatarScaleSet);

        vm.OpenEditorCommand.Execute(vm.CashPaymentCards.Single());

        Assert.False(GetRequiredBoolProperty(vm, "SelectedCardUsesScaleSetCommands"));
        Assert.Null(parent.SelectedAvatarScaleSet);

        parent.SelectedAvatarScaleSet = staleSet;
        vm.OpenEditorCommand.Execute(vm.PowerUpCards.Single());

        Assert.False(GetRequiredBoolProperty(vm, "SelectedCardUsesScaleSetCommands"));
        Assert.Null(parent.SelectedAvatarScaleSet);

        parent.SelectedAvatarScaleSet = staleSet;
        vm.OpenEditorCommand.Execute(vm.MasterRewardCard);

        Assert.False(GetRequiredBoolProperty(vm, "SelectedCardUsesScaleSetCommands"));
        Assert.Null(parent.SelectedAvatarScaleSet);
    }

    [Fact]
    public async Task Constructor_WithParentMainWindow_ExposesEditorCommands()
    {
        await using var parent = new MainWindowViewModel();
        using var vm = new AvatarScalingManagerViewModel(parent.Settings, parent);

        Assert.Same(parent.AddAvatarScaleSetCommand, vm.AddAvatarScaleSetCommand);
        Assert.Same(parent.RemoveSelectedAvatarScaleSetCommand, vm.RemoveSelectedAvatarScaleSetCommand);
        Assert.Same(parent.AddAvatarScaleRuleCommand, vm.AddAvatarScaleRuleCommand);
        Assert.Same(parent.AddRewardGrowthCommand, vm.AddRewardGrowthCommand);
        Assert.Same(parent.RemoveSelectedAvatarScaleRuleCommand, vm.RemoveSelectedAvatarScaleRuleCommand);
        Assert.Same(parent.TestSelectedAvatarScaleRuleCommand, vm.TestSelectedAvatarScaleRuleCommand);
        Assert.Same(parent.OpenAvatarScaleRuleLockoutPickerCommand, vm.OpenAvatarScaleRuleLockoutPickerCommand);
        Assert.Same(parent.RefreshTwitchRewardsCommand, vm.RefreshTwitchRewardsCommand);
        Assert.Same(parent.UnlinkTwitchRewardCommand, vm.UnlinkTwitchRewardCommand);
    }

    [Fact]
    public async Task Constructor_WithParentMainWindow_ExposesEditorOptionLists()
    {
        await using var parent = new MainWindowViewModel();
        using var vm = new AvatarScalingManagerViewModel(parent.Settings, parent);

        Assert.Same(parent.AvatarScaleModes, vm.AvatarScaleModes);
        Assert.Same(parent.AvatarScalePresets, vm.AvatarScalePresets);
        Assert.Same(parent.AvatarScaleRestoreModes, vm.AvatarScaleRestoreModes);
        Assert.Same(parent.AvatarScaleSubscriptionTierOptions, vm.AvatarScaleSubscriptionTierOptions);
        Assert.Same(parent.RewardSyncModeOptions, vm.RewardSyncModeOptions);
        Assert.Same(parent.RewardOptions, vm.RewardOptions);
        Assert.Equal(parent.AvailableAvatarScaleTriggerTypesForSelectedRule, vm.AvailableAvatarScaleTriggerTypesForSelectedRule);
    }

    [Fact]
    public async Task Constructor_WithParentMainWindow_ExposesChatCommandPermissionOptions()
    {
        await using var parent = new MainWindowViewModel();
        using var vm = new AvatarScalingManagerViewModel(parent.Settings, parent);
        var property = typeof(AvatarScalingManagerViewModel).GetProperty("ChatCommandPermissionOptions");

        Assert.NotNull(property);
        var options = Assert.IsAssignableFrom<IReadOnlyList<ChatCommandPermissionOption>>(property!.GetValue(vm));
        Assert.Same(parent.ChatCommandPermissionOptions, options);
    }

    [Fact]
    public async Task Manager_ReportsSelectedScaleSetStateForListCreateActions()
    {
        await using var parent = new MainWindowViewModel();
        using var vm = new AvatarScalingManagerViewModel(parent.Settings, parent);
        var property = typeof(AvatarScalingManagerViewModel).GetProperty("HasSelectedAvatarScaleSet");
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Assert.NotNull(property);
        Assert.False(Assert.IsType<bool>(property!.GetValue(vm)));

        parent.SelectedAvatarScaleSet = new AvatarScaleSet { Name = "New Set" };

        Assert.True(Assert.IsType<bool>(property.GetValue(vm)));
        Assert.Contains("HasSelectedAvatarScaleSet", changes);
    }

    [Fact]
    public void PassThroughs_AreEmptyOrNull_WhenParentMainWindowIsMissing()
    {
        using var vm = new AvatarScalingManagerViewModel(new AppSettings(), null);

        Assert.Null(vm.AddAvatarScaleSetCommand);
        Assert.Null(vm.RemoveSelectedAvatarScaleSetCommand);
        Assert.Null(vm.AddAvatarScaleRuleCommand);
        Assert.Null(vm.AddRewardGrowthCommand);
        Assert.Null(vm.RemoveSelectedAvatarScaleRuleCommand);
        Assert.Null(vm.TestSelectedAvatarScaleRuleCommand);
        Assert.Null(vm.OpenAvatarScaleRuleLockoutPickerCommand);
        Assert.Null(vm.RefreshTwitchRewardsCommand);
        Assert.Null(vm.UnlinkTwitchRewardCommand);
        Assert.Empty(vm.AvailableAvatarScaleTriggerTypesForSelectedRule);
        Assert.Empty(vm.AvatarScaleModes);
        Assert.Empty(vm.AvatarScalePresets);
        Assert.Empty(vm.AvatarScaleRestoreModes);
        Assert.Empty(vm.AvatarScaleSubscriptionTierOptions);
        Assert.Empty(vm.RewardSyncModeOptions);
        Assert.Empty(vm.RewardOptions);
    }

    [Fact]
    public async Task Manager_RaisesLockoutSummary_WhenParentLockoutSummaryChanges()
    {
        await using var parent = new MainWindowViewModel();
        var set = new AvatarScaleSet { Name = "Scale Set" };
        var firstRule = new AvatarScaleRule { Name = "First Scale", RewardTitle = "First Scale" };
        var secondRule = new AvatarScaleRule { Name = "Second Scale", RewardTitle = "Second Scale" };
        secondRule.TemporarilyDisabledScaleRuleIds.Add(firstRule.Id);
        set.ScaleRules.Add(firstRule);
        set.ScaleRules.Add(secondRule);
        parent.Settings.AvatarScaleSets.Add(set);
        parent.SelectedAvatarScaleSet = set;
        parent.SelectedAvatarScaleRule = firstRule;
        using var vm = new AvatarScalingManagerViewModel(parent.Settings, parent);
        vm.OpenEditorCommand.Execute(vm.TwitchRewardCards.Single(card => ReferenceEquals(card.ScaleRule, firstRule)));
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        parent.SelectedAvatarScaleRule = secondRule;

        Assert.Contains(nameof(AvatarScalingManagerViewModel.AvatarScaleRuleLockoutSummaryText), changes);
        Assert.Contains("First Scale", vm.AvatarScaleRuleLockoutSummaryText);
    }

    [Fact]
    public void OpenAdvancedSafetyCommand_TogglesAdvancedSafetyPanel()
    {
        using var vm = new AvatarScalingManagerViewModel(new AppSettings(), null);
        var command = Assert.IsAssignableFrom<ICommand>(GetRequiredPropertyValue(vm, "OpenAdvancedSafetyCommand"));

        Assert.False(GetRequiredBoolProperty(vm, "IsAdvancedSafetyOpen"));

        command.Execute(null);

        Assert.True(GetRequiredBoolProperty(vm, "IsAdvancedSafetyOpen"));

        command.Execute(null);

        Assert.False(GetRequiredBoolProperty(vm, "IsAdvancedSafetyOpen"));
    }

    [Fact]
    public void Card_RaisesSafetySummary_WhenSafetyChanges()
    {
        var safety = new AvatarScaleSafetySettings();
        var card = new AvatarScalingSourceCardViewModel(
            AvatarScalingSourceKind.TwitchReward,
            safety,
            scaleRule: new AvatarScaleRule { RewardTitle = "Grow Big" });
        var changes = new List<string?>();
        card.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        safety.CurrentMaximumHeightMeters = 25;

        Assert.Contains(nameof(AvatarScalingSourceCardViewModel.SafetySummary), changes);
        Assert.Contains("25m", card.SafetySummary);
    }

    [Fact]
    public void Manager_RaisesCurrentMaxHeightAllowedText_WhenSafetyChanges()
    {
        var settings = new AppSettings();
        var vm = new AvatarScalingManagerViewModel(settings, null);
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        settings.AvatarScaleSafety.CurrentMaximumHeightMeters = 25;

        Assert.Contains(nameof(AvatarScalingManagerViewModel.CurrentMaxHeightAllowedText), changes);
        Assert.Equal("25m", vm.CurrentMaxHeightAllowedText);
    }

    [Fact]
    public void Card_RaisesTitleStatusAndActionSummary_WhenScaleRuleChanges()
    {
        var rule = new AvatarScaleRule
        {
            RewardTitle = "Grow Big",
            ScaleMode = AvatarScaleMode.SetHeight,
            TargetHeightMeters = 1.6
        };
        var card = new AvatarScalingSourceCardViewModel(
            AvatarScalingSourceKind.TwitchReward,
            new AvatarScaleSafetySettings(),
            scaleRule: rule);
        var changes = new List<string?>();
        card.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        rule.RewardTitle = "Grow Bigger";
        rule.TargetHeightMeters = 2.4;
        rule.IsEnabled = false;

        Assert.Contains(nameof(AvatarScalingSourceCardViewModel.Title), changes);
        Assert.Contains(nameof(AvatarScalingSourceCardViewModel.ActionSummary), changes);
        Assert.Contains(nameof(AvatarScalingSourceCardViewModel.Status), changes);
        Assert.Contains(nameof(AvatarScalingSourceCardViewModel.StatusText), changes);
        Assert.Equal("Grow Bigger", card.Title);
        Assert.Equal("Set 2.4m", card.ActionSummary);
        Assert.Equal(AvatarScalingCardStatus.Disabled, card.Status);
    }

    [Fact]
    public void MasterRewardCard_ReportsDisabled_WhenMasterRewardIsDisabled()
    {
        var settings = new AppSettings();
        settings.AvatarScaleMasterReward.IsEnabled = false;

        var vm = new AvatarScalingManagerViewModel(settings, null);

        Assert.Equal(AvatarScalingCardStatus.Disabled, vm.MasterRewardCard.Status);
        Assert.Equal("Disabled", vm.MasterRewardCard.StatusText);
    }

    [Fact]
    public void RefreshCards_ClearsSelectedCard_WhenSelectedRuleWasRemoved()
    {
        var settings = new AppSettings();
        var set = new AvatarScaleSet();
        set.ScaleRules.Add(new AvatarScaleRule { Name = "Grow Big", RewardTitle = "Grow Big" });
        settings.AvatarScaleSets.Add(set);
        var vm = new AvatarScalingManagerViewModel(settings, null);
        var card = vm.TwitchRewardCards.Single();
        vm.OpenEditorCommand.Execute(card);

        set.ScaleRules.Clear();
        vm.RefreshCards();

        Assert.Empty(vm.TwitchRewardCards);
        Assert.False(vm.IsEditorOpen);
        Assert.Null(vm.SelectedCard);
        Assert.Null(vm.SelectedAvatarScaleRule);
    }

    [Fact]
    public void ManagerCards_Update_WhenTopLevelSourceCollectionChanges()
    {
        var settings = new AppSettings();
        var vm = new AvatarScalingManagerViewModel(settings, null);

        settings.CashPaymentRules.Add(new CashPaymentRule
        {
            Name = "Tip Scale",
            ActionKind = CashPaymentActionKind.AvatarScaling
        });

        var card = Assert.Single(vm.CashPaymentCards);
        Assert.Equal("Tip Scale", card.Title);
    }

    [Fact]
    public void CashPaymentCards_Update_WhenRuleActionKindChanges()
    {
        var settings = new AppSettings();
        var rule = new CashPaymentRule
        {
            Name = "Tip Scale",
            ActionKind = CashPaymentActionKind.TriggerAction
        };
        settings.CashPaymentRules.Add(rule);
        var vm = new AvatarScalingManagerViewModel(settings, null);

        Assert.Empty(vm.CashPaymentCards);

        rule.ActionKind = CashPaymentActionKind.AvatarScaling;

        var card = Assert.Single(vm.CashPaymentCards);
        Assert.Same(rule, card.CashPaymentRule);

        rule.ActionKind = CashPaymentActionKind.TriggerAction;

        Assert.Empty(vm.CashPaymentCards);
    }

    [Fact]
    public void PowerUpCards_Update_WhenRuleActionKindChanges()
    {
        var settings = new AppSettings();
        var rule = new PowerUpRule
        {
            Name = "Power Scale",
            ActionKind = PowerUpActionKind.TriggerAction
        };
        settings.PowerUpRules.Add(rule);
        var vm = new AvatarScalingManagerViewModel(settings, null);

        Assert.Empty(vm.PowerUpCards);

        rule.ActionKind = PowerUpActionKind.AvatarScaling;

        var card = Assert.Single(vm.PowerUpCards);
        Assert.Same(rule, card.PowerUpRule);

        rule.ActionKind = PowerUpActionKind.TriggerAction;

        Assert.Empty(vm.PowerUpCards);
    }

    [Fact]
    public void ScaleRule_ChangingTriggerType_RebuildsGrouping()
    {
        var settings = new AppSettings();
        var set = new AvatarScaleSet();
        var rule = new AvatarScaleRule
        {
            Name = "Grow Big",
            TriggerType = AvatarScaleTriggerType.ChannelPointReward,
            RewardTitle = "Grow Big"
        };
        set.ScaleRules.Add(rule);
        settings.AvatarScaleSets.Add(set);
        var vm = new AvatarScalingManagerViewModel(settings, null);

        Assert.Single(vm.TwitchRewardCards);
        Assert.Empty(vm.SupporterGrowthCards);

        rule.TriggerType = AvatarScaleTriggerType.SupporterGrowth;

        Assert.Empty(vm.TwitchRewardCards);
        var card = Assert.Single(vm.SupporterGrowthCards);
        Assert.Same(rule, card.ScaleRule);
        Assert.Equal(AvatarScalingSourceKind.SupporterGrowth, card.Kind);
    }

    [Fact]
    public void ScaleSet_ReplacingScaleRulesCollection_RebuildsCardsFromNewRules()
    {
        var settings = new AppSettings();
        var set = new AvatarScaleSet();
        set.ScaleRules.Add(new AvatarScaleRule
        {
            Name = "Old Grow",
            TriggerType = AvatarScaleTriggerType.ChannelPointReward,
            RewardTitle = "Old Grow"
        });
        settings.AvatarScaleSets.Add(set);
        var vm = new AvatarScalingManagerViewModel(settings, null);

        set.ScaleRules =
        [
            new AvatarScaleRule
            {
                Name = "Supporters Grow",
                TriggerType = AvatarScaleTriggerType.SupporterGrowth
            }
        ];

        Assert.Empty(vm.TwitchRewardCards);
        var card = Assert.Single(vm.SupporterGrowthCards);
        Assert.Equal("Supporters Grow", card.Title);
    }

    [Fact]
    public void Dispose_UnsubscribesReplacedScaleRulesCollection()
    {
        var settings = new AppSettings();
        var set = new AvatarScaleSet();
        var oldRules = set.ScaleRules;
        oldRules.Add(new AvatarScaleRule
        {
            Name = "Old Grow",
            TriggerType = AvatarScaleTriggerType.ChannelPointReward,
            RewardTitle = "Old Grow"
        });
        settings.AvatarScaleSets.Add(set);
        var vm = new AvatarScalingManagerViewModel(settings, null);
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        set.ScaleRules =
        [
            new AvatarScaleRule
            {
                Name = "New Grow",
                TriggerType = AvatarScaleTriggerType.ChannelPointReward,
                RewardTitle = "New Grow"
            }
        ];
        changes.Clear();

        vm.Dispose();
        oldRules.Add(new AvatarScaleRule
        {
            Name = "Leaked Grow",
            TriggerType = AvatarScaleTriggerType.ChannelPointReward,
            RewardTitle = "Leaked Grow"
        });

        Assert.Empty(changes);
    }

    [Fact]
    public void CashAndPowerUpCards_ReportDisabled_WhenNestedScaleActionIsDisabled()
    {
        var settings = new AppSettings();
        var cashRule = new CashPaymentRule
        {
            Name = "Tip Scale",
            ActionKind = CashPaymentActionKind.AvatarScaling
        };
        var powerRule = new PowerUpRule
        {
            Name = "Power Scale",
            ActionKind = PowerUpActionKind.AvatarScaling
        };
        cashRule.ScaleAction.IsEnabled = false;
        powerRule.ScaleAction.IsEnabled = false;
        settings.CashPaymentRules.Add(cashRule);
        settings.PowerUpRules.Add(powerRule);

        var vm = new AvatarScalingManagerViewModel(settings, null);

        Assert.Equal(AvatarScalingCardStatus.Disabled, vm.CashPaymentCards.Single().Status);
        Assert.Equal(AvatarScalingCardStatus.Disabled, vm.PowerUpCards.Single().Status);
    }

    [Theory]
    [InlineData(AvatarScaleTriggerType.Bits)]
    [InlineData(AvatarScaleTriggerType.Subscription)]
    [InlineData(AvatarScaleTriggerType.GiftSubscription)]
    [InlineData(AvatarScaleTriggerType.Follow)]
    public void EventScaleRules_AreNotLabeledAsTwitchRewardCards(AvatarScaleTriggerType triggerType)
    {
        var settings = new AppSettings();
        var set = new AvatarScaleSet();
        set.ScaleRules.Add(new AvatarScaleRule
        {
            Name = "Event Scale",
            TriggerType = triggerType
        });
        settings.AvatarScaleSets.Add(set);

        var vm = new AvatarScalingManagerViewModel(settings, null);

        var card = Assert.Single(vm.TwitchRewardCards);
        Assert.NotEqual(AvatarScalingSourceKind.TwitchReward, card.Kind);
        Assert.Equal("Twitch Event", card.SourcePill);
    }

    [Fact]
    public void SupporterGrowthCard_UsesSupporterGrowthActionSummary()
    {
        var settings = new AppSettings();
        var set = new AvatarScaleSet();
        var rule = new AvatarScaleRule
        {
            Name = "Supporters Grow",
            TriggerType = AvatarScaleTriggerType.SupporterGrowth,
            SupporterGrowthTier1HeightMeters = 0.2,
            SupporterGrowthTier2HeightMeters = 0.4,
            SupporterGrowthTier3HeightMeters = 0.6
        };
        set.ScaleRules.Add(rule);
        settings.AvatarScaleSets.Add(set);

        var vm = new AvatarScalingManagerViewModel(settings, null);

        var card = Assert.Single(vm.SupporterGrowthCards);
        Assert.Equal("Supporter growth +0.2/+0.4/+0.6m", card.ActionSummary);
    }

    [Fact]
    public async Task DeleteCardCommand_RemovesTwitchRewardScaleRule()
    {
        await using var parent = new MainWindowViewModel();
        var set = new AvatarScaleSet { Name = "Scale Set" };
        var rule = new AvatarScaleRule { Name = "Grow Big", RewardTitle = "Grow Big" };
        set.ScaleRules.Add(rule);
        parent.Settings.AvatarScaleSets.Add(set);
        using var vm = new AvatarScalingManagerViewModel(parent.Settings, parent);

        var card = Assert.Single(vm.TwitchRewardCards);
        Assert.Same(rule, card.ScaleRule);

        vm.DeleteCardCommand.Execute(card);

        Assert.Empty(set.ScaleRules);
        Assert.Empty(vm.TwitchRewardCards);
    }

    [Fact]
    public async Task DeleteCardCommand_RemovesSupporterGrowthRule()
    {
        await using var parent = new MainWindowViewModel();
        var set = new AvatarScaleSet { Name = "Scale Set" };
        var rule = new AvatarScaleRule
        {
            Name = "Supporters Grow",
            TriggerType = AvatarScaleTriggerType.SupporterGrowth
        };
        set.ScaleRules.Add(rule);
        parent.Settings.AvatarScaleSets.Add(set);
        using var vm = new AvatarScalingManagerViewModel(parent.Settings, parent);

        var card = Assert.Single(vm.SupporterGrowthCards);
        Assert.Same(rule, card.ScaleRule);

        vm.DeleteCardCommand.Execute(card);

        Assert.Empty(set.ScaleRules);
        Assert.Empty(vm.SupporterGrowthCards);
    }

    [Fact]
    public async Task DeleteCardCommand_RemovesCashPaymentRule()
    {
        await using var parent = new MainWindowViewModel();
        var rule = new CashPaymentRule
        {
            Name = "Tip Scale",
            ActionKind = CashPaymentActionKind.AvatarScaling
        };
        parent.Settings.CashPaymentRules.Add(rule);
        using var vm = new AvatarScalingManagerViewModel(parent.Settings, parent);

        var card = Assert.Single(vm.CashPaymentCards);
        Assert.Same(rule, card.CashPaymentRule);

        vm.DeleteCardCommand.Execute(card);

        Assert.Empty(parent.Settings.CashPaymentRules);
        Assert.Empty(vm.CashPaymentCards);
    }

    [Fact]
    public async Task DeleteCardCommand_RemovesPowerUpRule()
    {
        await using var parent = new MainWindowViewModel();
        var rule = new PowerUpRule
        {
            Name = "Power Scale",
            ActionKind = PowerUpActionKind.AvatarScaling
        };
        parent.Settings.PowerUpRules.Add(rule);
        using var vm = new AvatarScalingManagerViewModel(parent.Settings, parent);

        var card = Assert.Single(vm.PowerUpCards);
        Assert.Same(rule, card.PowerUpRule);

        vm.DeleteCardCommand.Execute(card);

        Assert.Empty(parent.Settings.PowerUpRules);
        Assert.Empty(vm.PowerUpCards);
    }

    [Fact]
    public async Task DeleteCardCommand_DoesNotRemoveMasterReward()
    {
        await using var parent = new MainWindowViewModel();
        using var vm = new AvatarScalingManagerViewModel(parent.Settings, parent);

        Assert.Equal(AvatarScalingSourceKind.MasterReward, vm.MasterRewardCard.Kind);

        vm.DeleteCardCommand.Execute(vm.MasterRewardCard);

        Assert.NotNull(vm.MasterRewardCard);
        Assert.True(parent.Settings.AvatarScaleMasterReward.IsEnabled == vm.MasterRewardCard.MasterReward?.IsEnabled);
    }

    [Fact]
    public async Task DeleteCardCommand_IgnoresNullParameter()
    {
        await using var parent = new MainWindowViewModel();
        using var vm = new AvatarScalingManagerViewModel(parent.Settings, parent);

        vm.DeleteCardCommand.Execute(null);
    }

    [Fact]
    public async Task AddAvatarScalingCashPaymentRuleCommand_CreatesRuleWithAvatarScalingActionKind()
    {
        await using var parent = new MainWindowViewModel();

        Assert.Empty(parent.Settings.CashPaymentRules);

        parent.AddAvatarScalingCashPaymentRuleCommand.Execute(null);

        var rule = Assert.Single(parent.Settings.CashPaymentRules);
        Assert.Equal(CashPaymentActionKind.AvatarScaling, rule.ActionKind);
        Assert.True(rule.UsesAvatarScaling);
    }

    [Fact]
    public async Task AddAvatarScalingPowerUpRuleCommand_CreatesRuleWithAvatarScalingActionKind()
    {
        await using var parent = new MainWindowViewModel();

        Assert.Empty(parent.Settings.PowerUpRules);

        parent.AddAvatarScalingPowerUpRuleCommand.Execute(null);

        var rule = Assert.Single(parent.Settings.PowerUpRules);
        Assert.Equal(PowerUpActionKind.AvatarScaling, rule.ActionKind);
        Assert.True(rule.UsesAvatarScaling);
        Assert.Same(rule, parent.SelectedPowerUpRule);
    }

    private static bool GetRequiredBoolProperty(object target, string propertyName)
    {
        return Assert.IsType<bool>(GetRequiredPropertyValue(target, propertyName));
    }

    private static object? GetRequiredPropertyValue(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return property!.GetValue(target);
    }

    [Fact]
    public async Task Constructor_WithParentMainWindow_ExposesAvatarScalingCashAndPowerUpAddCommands()
    {
        await using var parent = new MainWindowViewModel();
        using var vm = new AvatarScalingManagerViewModel(parent.Settings, parent);

        Assert.Same(parent.AddAvatarScalingCashPaymentRuleCommand, vm.AddAvatarScalingCashPaymentRuleCommand);
        Assert.Same(parent.AddAvatarScalingPowerUpRuleCommand, vm.AddAvatarScalingPowerUpRuleCommand);
    }

    [Fact]
    public void PassThroughs_AvatarScalingCashAndPowerUpAddCommands_NullWhenParentMissing()
    {
        using var vm = new AvatarScalingManagerViewModel(new AppSettings(), null);

        Assert.Null(vm.AddAvatarScalingCashPaymentRuleCommand);
        Assert.Null(vm.AddAvatarScalingPowerUpRuleCommand);
    }
}
