using System.Linq;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public static class AvatarSwapMigrationService
{
    public const int CurrentMigrationVersion = 3;

    public static void Migrate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.AvatarChangeToAvatarSwapMigrationVersion >= CurrentMigrationVersion)
        {
            return;
        }

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
            MigrateInto(swapProfile.BitsSubsRules, rule, TriggerRuleSource.GlobalOverride);
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
                MigrateInto(swapProfile.RouletteRules, rule, TriggerRuleSource.AvatarSet);
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
            MigrateInto(swapProfile.RouletteRules, rule, TriggerRuleSource.GlobalOverride);
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
            MigrateInto(swapProfile.RouletteRules, action, TriggerRuleSource.PowerUp);
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
            MigrateInto(swapProfile.RouletteRules, action, TriggerRuleSource.CashPayment);
            action.ActionType = OscActionType.AvatarParameter;
        }

        settings.AvatarChangeToAvatarSwapMigrationVersion = CurrentMigrationVersion;
    }

    private static void MigrateInto(
        System.Collections.ObjectModel.ObservableCollection<TriggerRule> target,
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
}
