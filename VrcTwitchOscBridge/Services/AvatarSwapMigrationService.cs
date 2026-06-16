using System.Linq;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public static class AvatarSwapMigrationService
{
    public const int CurrentMigrationVersion = 2;

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
            var isMaster = profile.IsMasterProfile;
            foreach (var rule in profile.ChannelPointRules
                         .Where(r => r.ActionType == OscActionType.AvatarChange
                                     && !string.IsNullOrWhiteSpace(r.AvatarChangeTargetId))
                         .ToList())
            {
                var swapProfile = FindOrCreateProfile(settings, rule.AvatarChangeTargetId, rule.AvatarTargetName);
                if (isMaster)
                {
                    MigrateInto(swapProfile.ChannelPointRules, rule, TriggerRuleSource.AvatarSet);
                }
                else
                {
                    MigrateInto(swapProfile.ChannelPointRules, rule, TriggerRuleSource.AvatarSet);
                }
            }
        }

        foreach (var rule in settings.GlobalOverrideRules
                     .Where(r => r.ActionType == OscActionType.AvatarChange
                                 && !string.IsNullOrWhiteSpace(r.AvatarChangeTargetId))
                     .ToList())
        {
            var swapProfile = FindOrCreateProfile(settings, rule.AvatarChangeTargetId, rule.AvatarTargetName);
            MigrateInto(swapProfile.BitsSubsRules, rule, TriggerRuleSource.GlobalOverride);
        }

        foreach (var powerUp in settings.PowerUpRules)
        {
            var action = powerUp.ActionRule;
            if (action is null) continue;
            if (action.ActionType != OscActionType.AvatarChange) continue;
            if (string.IsNullOrWhiteSpace(action.AvatarChangeTargetId)) continue;

            var swapProfile = FindOrCreateProfile(settings, action.AvatarChangeTargetId, action.AvatarTargetName);
            MigrateInto(swapProfile.ChannelPointRules, action, TriggerRuleSource.PowerUp);
        }

        foreach (var cash in settings.CashPaymentRules)
        {
            var action = cash.TriggerAction;
            if (action is null) continue;
            if (action.ActionType != OscActionType.AvatarChange) continue;
            if (string.IsNullOrWhiteSpace(action.AvatarChangeTargetId)) continue;

            var swapProfile = FindOrCreateProfile(settings, action.AvatarChangeTargetId, action.AvatarTargetName);
            MigrateInto(swapProfile.ChannelPointRules, action, TriggerRuleSource.CashPayment);
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

    private static AvatarSwapProfile FindOrCreateProfile(AppSettings settings, string targetAvatarId, string targetAvatarName)
    {
        var existing = settings.AvatarSwapProfiles.FirstOrDefault(p =>
            string.Equals(p.TargetAvatarId, targetAvatarId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var profile = new AvatarSwapProfile
        {
            TargetAvatarId = targetAvatarId,
            TargetAvatarName = targetAvatarName
        };
        settings.AvatarSwapProfiles.Add(profile);
        return profile;
    }
}
