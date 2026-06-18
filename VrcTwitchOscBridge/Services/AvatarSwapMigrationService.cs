using System.Collections.ObjectModel;
using System.Linq;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public static class AvatarSwapMigrationService
{
    public const int CurrentMigrationVersion = 5;

    public static void Migrate(AppSettings settings)
    {
        Migrate(settings, null);
    }

    internal static void Migrate(AppSettings settings, List<SettingsStore.PersistedAvatarSwapProfile>? persistedSwapProfiles)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.AvatarChangeToAvatarSwapMigrationVersion >= CurrentMigrationVersion)
        {
            return;
        }

        if (settings.AvatarChangeToAvatarSwapMigrationVersion < 3)
        {
            MigrateLegacyToV3(settings);
        }

        if (settings.AvatarChangeToAvatarSwapMigrationVersion < 4)
        {
            MigrateV3ToV4(settings, persistedSwapProfiles);
        }

        if (settings.AvatarChangeToAvatarSwapMigrationVersion < 5)
        {
            MigrateV4ToV5(settings);
        }

        settings.AvatarChangeToAvatarSwapMigrationVersion = CurrentMigrationVersion;
    }

    private static void MigrateLegacyToV3(AppSettings settings)
    {
        var masterProfile = settings.AvatarProfiles.FirstOrDefault(p => p.IsMasterProfile);
        if (masterProfile is not null && !string.IsNullOrWhiteSpace(masterProfile.AvatarId))
        {
            settings.MasterAvatarSwapReturnId = masterProfile.AvatarId;
            settings.MasterAvatarSwapReturnName = masterProfile.AvatarName;
        }

        foreach (var profile in settings.AvatarProfiles)
        {
            var rulesToMove = profile.ChannelPointRules
                .Where(r => r.ActionType == OscActionType.AvatarChange
                            && !string.IsNullOrWhiteSpace(r.AvatarChangeTargetId))
                .ToList();
            foreach (var rule in rulesToMove)
            {
                var swapProfile = FindOrCreateSwapProfile(settings, rule.AvatarChangeTargetId, rule.AvatarTargetName);
                MigrateInto(swapProfile.ChannelPointRules, rule, TriggerRuleSource.AvatarSet);
                profile.ChannelPointRules.Remove(rule);
            }
        }

        var globalRulesToMove = settings.GlobalOverrideRules
            .Where(r => r.ActionType == OscActionType.AvatarChange
                        && !string.IsNullOrWhiteSpace(r.AvatarChangeTargetId))
            .ToList();
        foreach (var rule in globalRulesToMove)
        {
            var swapProfile = FindOrCreateSwapProfile(settings, rule.AvatarChangeTargetId, rule.AvatarTargetName);
            MigrateInto(GetTargetCollectionForTrigger(swapProfile, rule), rule, TriggerRuleSource.GlobalOverride);
            settings.GlobalOverrideRules.Remove(rule);
        }

        foreach (var powerUp in settings.PowerUpRules)
        {
            var action = powerUp.ActionRule;
            if (action is null) continue;
            if (action.ActionType != OscActionType.AvatarChange) continue;
            if (string.IsNullOrWhiteSpace(action.AvatarChangeTargetId)) continue;

            var swapProfile = FindOrCreateSwapProfile(settings, action.AvatarChangeTargetId, action.AvatarTargetName);
            action.PowerUpId = powerUp.Id.ToString();
            MigrateInto(swapProfile.ChannelPointRules, action, TriggerRuleSource.PowerUp);
            action.ActionType = OscActionType.AvatarParameter;
        }

        foreach (var cash in settings.CashPaymentRules)
        {
            var action = cash.TriggerAction;
            if (action is null) continue;
            if (action.ActionType != OscActionType.AvatarChange) continue;
            if (string.IsNullOrWhiteSpace(action.AvatarChangeTargetId)) continue;

            var swapProfile = FindOrCreateSwapProfile(settings, action.AvatarChangeTargetId, action.AvatarTargetName);
            action.CashPaymentRuleId = cash.Id.ToString();
            MigrateInto(swapProfile.ChannelPointRules, action, TriggerRuleSource.CashPayment);
            action.ActionType = OscActionType.AvatarParameter;
        }

        foreach (var profile in settings.AvatarProfiles)
        {
            var rulesToMove = profile.ChannelPointRules
                .Where(r => r.ActionType == OscActionType.AvatarRoulet
                            && r.AvatarRouletAvatarIds.Count > 0)
                .ToList();
            foreach (var rule in rulesToMove)
            {
                var firstPoolId = rule.AvatarRouletAvatarIds.First();
                var swapProfile = FindOrCreateSwapProfile(settings, firstPoolId, firstPoolId);
                MigrateInto(swapProfile.ChannelPointRules, rule, TriggerRuleSource.AvatarSet);
                profile.ChannelPointRules.Remove(rule);
            }
        }

        var globalRouletteToMove = settings.GlobalOverrideRules
            .Where(r => r.ActionType == OscActionType.AvatarRoulet
                        && r.AvatarRouletAvatarIds.Count > 0)
            .ToList();
        foreach (var rule in globalRouletteToMove)
        {
            var firstPoolId = rule.AvatarRouletAvatarIds.First();
            var swapProfile = FindOrCreateSwapProfile(settings, firstPoolId, firstPoolId);
            MigrateInto(swapProfile.ChannelPointRules, rule, TriggerRuleSource.GlobalOverride);
            settings.GlobalOverrideRules.Remove(rule);
        }

        foreach (var powerUp in settings.PowerUpRules)
        {
            var action = powerUp.ActionRule;
            if (action is null) continue;
            if (action.ActionType != OscActionType.AvatarRoulet) continue;
            if (action.AvatarRouletAvatarIds.Count == 0) continue;

            var firstPoolId = action.AvatarRouletAvatarIds.First();
            var swapProfile = FindOrCreateSwapProfile(settings, firstPoolId, firstPoolId);
            action.PowerUpId = powerUp.Id.ToString();
            MigrateInto(swapProfile.ChannelPointRules, action, TriggerRuleSource.PowerUp);
            action.ActionType = OscActionType.AvatarParameter;
        }

        foreach (var cash in settings.CashPaymentRules)
        {
            var action = cash.TriggerAction;
            if (action is null) continue;
            if (action.ActionType != OscActionType.AvatarRoulet) continue;
            if (action.AvatarRouletAvatarIds.Count == 0) continue;

            var firstPoolId = action.AvatarRouletAvatarIds.First();
            var swapProfile = FindOrCreateSwapProfile(settings, firstPoolId, firstPoolId);
            action.CashPaymentRuleId = cash.Id.ToString();
            MigrateInto(swapProfile.ChannelPointRules, action, TriggerRuleSource.CashPayment);
            action.ActionType = OscActionType.AvatarParameter;
        }

        settings.AvatarChangeToAvatarSwapMigrationVersion = 3;
    }

    private static void MigrateV3ToV4(AppSettings settings, List<SettingsStore.PersistedAvatarSwapProfile>? persistedSwapProfiles)
    {
        settings.AvatarRouletteProfiles ??= new ObservableCollection<AvatarRouletteProfile>();

        if (persistedSwapProfiles is not null)
        {
            for (int i = 0; i < settings.AvatarSwapProfiles.Count && i < persistedSwapProfiles.Count; i++)
            {
                var live = settings.AvatarSwapProfiles[i];
                var persistedProfile = persistedSwapProfiles[i];

                foreach (var t in persistedProfile.BitsSubsRules ?? new List<SettingsStore.PersistedTriggerRule>())
                {
                    var rule = PersistedToRule(t);
                    if (rule.TriggerType == TwitchTriggerType.Bits)
                    {
                        live.BitsRules.Add(rule);
                    }
                    else if (rule.TriggerType == TwitchTriggerType.Subscriptions)
                    {
                        if (rule.IsGiftSubscription) rule.TriggerType = TwitchTriggerType.GiftSubscription;
                        live.SubsRules.Add(rule);
                    }
                    else
                    {
                        live.ChannelPointRules.Add(rule);
                    }
                }

                foreach (var t in persistedProfile.RouletteRules ?? new List<SettingsStore.PersistedTriggerRule>())
                {
                    var rule = PersistedToRule(t);
                    if (rule.ActionType != OscActionType.AvatarRoulet) continue;
                    var roulette = new AvatarRouletteProfile
                    {
                        Id = Guid.NewGuid(),
                        Name = string.IsNullOrWhiteSpace(live.TargetAvatarName) ? "Roulette" : live.TargetAvatarName + " Roulette",
                        IsEnabled = live.IsEnabled,
                    };
                    if (rule.AvatarRouletAvatarIds is not null)
                    {
                        for (int j = 0; j < rule.AvatarRouletAvatarIds.Count; j++)
                        {
                            var id = rule.AvatarRouletAvatarIds[j];
                            var name = j < (rule.AvatarRouletAvatarNames?.Count ?? 0)
                                ? rule.AvatarRouletAvatarNames![j]
                                : id;
                            roulette.Pool.Add(new RouletteAvatarEntry { AvatarId = id, AvatarName = name });
                        }
                    }
                    rule.AvatarRouletAvatarIds = null;
                    rule.AvatarRouletAvatarNames = null;
                    roulette.Triggers.Add(rule);
                    settings.AvatarRouletteProfiles.Add(roulette);
                }
            }
        }

        foreach (var profile in settings.AvatarSwapProfiles)
        {
            var keepers = new ObservableCollection<TriggerRule>();
            foreach (var rule in profile.ChannelPointRules)
            {
                if (rule.Source == TriggerRuleSource.CashPayment)
                {
                    var migrated = new CashPaymentRule
                    {
                        Name = rule.Name,
                        Provider = CashPaymentProvider.StreamElements,
                        MinimumAmount = (decimal)rule.MinimumAmount,
                        IsEnabled = true,
                        ActionKind = CashPaymentActionKind.TriggerAction,
                        TriggerAction = new TriggerRule
                        {
                            ActionType = rule.ActionType,
                            AvatarChangeTargetId = rule.AvatarChangeTargetId,
                            AvatarTargetName = rule.AvatarTargetName
                        }
                    };
                    profile.PaymentRules.Add(migrated);
                }
                else
                {
                    keepers.Add(rule);
                }
            }
            profile.ChannelPointRules.Clear();
            foreach (var k in keepers) profile.ChannelPointRules.Add(k);
        }

        settings.AvatarChangeToAvatarSwapMigrationVersion = 4;
    }

    private static TriggerRule PersistedToRule(SettingsStore.PersistedTriggerRule p) => SettingsStore.ToRule(p);

    private static void MigrateInto(
        ObservableCollection<TriggerRule> target,
        TriggerRule rule,
        TriggerRuleSource source)
    {
        if (target.Contains(rule)) return;
        rule.Source = source;
        target.Add(rule);
    }

    private static AvatarSwapProfile FindOrCreateSwapProfile(AppSettings settings, string targetAvatarId, string targetAvatarName)
    {
        var existing = settings.AvatarSwapProfiles.FirstOrDefault(p => p.TargetAvatarId == targetAvatarId);
        if (existing is not null)
        {
            return existing;
        }

        var profile = new AvatarSwapProfile
        {
            TargetAvatarId = targetAvatarId,
            TargetAvatarName = string.IsNullOrWhiteSpace(targetAvatarName) ? targetAvatarId : targetAvatarName
        };
        settings.AvatarSwapProfiles.Add(profile);
        return profile;
    }

    private static System.Collections.ObjectModel.ObservableCollection<TriggerRule> GetTargetCollectionForTrigger(
        AvatarSwapProfile profile, TriggerRule rule)
    {
        return rule.TriggerType switch
        {
            TwitchTriggerType.Bits => profile.BitsRules,
            TwitchTriggerType.Subscriptions or TwitchTriggerType.GiftSubscription => profile.SubsRules,
            _ => profile.ChannelPointRules,
        };
    }

    private static void MigrateV4ToV5(AppSettings settings)
    {
        // V4->V5: The data model for AvatarSwapProfile.PaymentRules changed
        // from ObservableCollection<TriggerRule> to ObservableCollection<CashPaymentRule>.
        // The actual conversion from legacy TriggerRule JSON to CashPaymentRule happens
        // during deserialization via CashPaymentRuleJsonConverter (registered in SettingsStore).
        // This step simply marks settings at version 4 as up-to-date once they've been
        // loaded through the converter.
    }
}
