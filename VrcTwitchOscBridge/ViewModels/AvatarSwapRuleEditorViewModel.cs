using System.Collections.Generic;
using System.Linq;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class AvatarSwapRuleEditorViewModel : ObservableObject
{
    public AvatarSwapRuleEditorViewModel(TriggerRule rule)
    {
        Rule = rule;
        OriginalSnapshot = Clone(rule);
        Name = rule.Name;
        Cost = rule.ChannelPointRewardCost;
        CooldownSeconds = rule.CooldownSeconds;
        ActiveTimeSeconds = rule.DurationSeconds;
        TriggerType = rule.TriggerType;
        MinimumAmount = rule.MinimumAmount;
        ActionType = rule.ActionType;
        PermanentAvatarChange = rule.PermanentAvatarChange;
        CooldownOnlyAvatarChange = rule.CooldownOnlyAvatarChange;
        DeleteManagedRewardWhenInactive = rule.DeleteManagedRewardWhenInactive;
        RewardSyncMode = rule.RewardSyncMode;
        RoulettePool = rule.AvatarRouletAvatarIds?.ToList() ?? new List<string>();
        TargetAvatarId = rule.AvatarChangeTargetId;
        TargetAvatarName = rule.AvatarTargetName;
        ReturnAvatarId = rule.AvatarChangeResetId;
        ReturnAvatarName = rule.ResetAvatarName;
    }

    public TriggerRule Rule { get; }

    public TriggerRule? OriginalSnapshot { get; }

    public bool IsDirty => OriginalSnapshot is null
        || Name != OriginalSnapshot.Name
        || Cost != OriginalSnapshot.ChannelPointRewardCost
        || CooldownSeconds != OriginalSnapshot.CooldownSeconds
        || ActiveTimeSeconds != OriginalSnapshot.DurationSeconds
        || TriggerType != OriginalSnapshot.TriggerType
        || MinimumAmount != OriginalSnapshot.MinimumAmount
        || ActionType != OriginalSnapshot.ActionType
        || PermanentAvatarChange != OriginalSnapshot.PermanentAvatarChange
        || CooldownOnlyAvatarChange != OriginalSnapshot.CooldownOnlyAvatarChange
        || DeleteManagedRewardWhenInactive != OriginalSnapshot.DeleteManagedRewardWhenInactive
        || RewardSyncMode != OriginalSnapshot.RewardSyncMode
        || !RoulettePool.SequenceEqual(OriginalSnapshot.AvatarRouletAvatarIds ?? Enumerable.Empty<string>())
        || TargetAvatarId != OriginalSnapshot.AvatarChangeTargetId
        || TargetAvatarName != OriginalSnapshot.AvatarTargetName
        || ReturnAvatarId != OriginalSnapshot.AvatarChangeResetId
        || ReturnAvatarName != OriginalSnapshot.ResetAvatarName;

    public string Name { get; set; }
    public int Cost { get; set; }
    public int CooldownSeconds { get; set; }
    public double ActiveTimeSeconds { get; set; }
    public TwitchTriggerType TriggerType { get; set; }
    public int MinimumAmount { get; set; }
    public OscActionType ActionType { get; set; }
    public bool PermanentAvatarChange { get; set; }
    public bool CooldownOnlyAvatarChange { get; set; }
    public bool DeleteManagedRewardWhenInactive { get; set; }
    public TwitchRewardSyncMode RewardSyncMode { get; set; }
    public IReadOnlyList<string> RoulettePool { get; set; }
    public string? TargetAvatarId { get; set; }
    public string? TargetAvatarName { get; set; }
    public string? ReturnAvatarId { get; set; }
    public string? ReturnAvatarName { get; set; }

    public void Save()
    {
        Rule.Name = Name;
        Rule.ChannelPointRewardCost = Cost;
        Rule.CooldownSeconds = CooldownSeconds;
        Rule.DurationSeconds = ActiveTimeSeconds;
        Rule.TriggerType = TriggerType;
        Rule.MinimumAmount = MinimumAmount;
        Rule.ActionType = ActionType;
        Rule.PermanentAvatarChange = PermanentAvatarChange;
        Rule.CooldownOnlyAvatarChange = CooldownOnlyAvatarChange;
        Rule.DeleteManagedRewardWhenInactive = DeleteManagedRewardWhenInactive;
        Rule.RewardSyncMode = RewardSyncMode;
        Rule.AvatarRouletAvatarIds = new System.Collections.ObjectModel.ObservableCollection<string>(RoulettePool);
        Rule.AvatarChangeTargetId = TargetAvatarId ?? string.Empty;
        Rule.AvatarTargetName = TargetAvatarName ?? string.Empty;
        Rule.AvatarChangeResetId = ReturnAvatarId ?? string.Empty;
        Rule.ResetAvatarName = ReturnAvatarName ?? string.Empty;
    }

    private static TriggerRule Clone(TriggerRule r) => new()
    {
        Name = r.Name,
        ChannelPointRewardCost = r.ChannelPointRewardCost,
        CooldownSeconds = r.CooldownSeconds,
        DurationSeconds = r.DurationSeconds,
        TriggerType = r.TriggerType,
        MinimumAmount = r.MinimumAmount,
        ActionType = r.ActionType,
        PermanentAvatarChange = r.PermanentAvatarChange,
        ReturnToPreviousAvatar = r.ReturnToPreviousAvatar,
        CooldownOnlyAvatarChange = r.CooldownOnlyAvatarChange,
        DeleteManagedRewardWhenInactive = r.DeleteManagedRewardWhenInactive,
        RewardSyncMode = r.RewardSyncMode,
        AvatarRouletAvatarIds = r.AvatarRouletAvatarIds?.ToList() is { } ids
            ? new System.Collections.ObjectModel.ObservableCollection<string>(ids)
            : new System.Collections.ObjectModel.ObservableCollection<string>(),
        AvatarChangeTargetId = r.AvatarChangeTargetId,
        AvatarTargetName = r.AvatarTargetName,
        AvatarChangeResetId = r.AvatarChangeResetId,
        ResetAvatarName = r.ResetAvatarName
    };
}
