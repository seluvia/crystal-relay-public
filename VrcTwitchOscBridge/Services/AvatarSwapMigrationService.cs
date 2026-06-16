using System.Linq;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public static class AvatarSwapMigrationService
{
    public const int CurrentMigrationVersion = 1;

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

        if (masterProfile is not null)
        {
            foreach (var rule in masterProfile.ChannelPointRules
                         .Where(r => r.ActionType == OscActionType.AvatarChange
                                     && !string.IsNullOrWhiteSpace(r.AvatarChangeTargetId))
                         .ToList())
            {
                var profile = FindOrCreateProfile(settings, rule.AvatarChangeTargetId, rule.AvatarTargetName);
                profile.ChannelPointRules.Add(rule);
            }
        }

        foreach (var rule in settings.GlobalOverrideRules
                     .Where(r => r.ActionType == OscActionType.AvatarChange
                                 && !string.IsNullOrWhiteSpace(r.AvatarChangeTargetId))
                     .ToList())
        {
            var profile = FindOrCreateProfile(settings, rule.AvatarChangeTargetId, rule.AvatarTargetName);
            profile.BitsSubsRules.Add(rule);
        }

        settings.AvatarChangeToAvatarSwapMigrationVersion = CurrentMigrationVersion;
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
