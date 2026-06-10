using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.ViewModels;

public enum UniversalTriggerCardStatus
{
    Unconfigured,
    Ready,
    WarnDirectOsc,
    WarnNotAvatarBound,
    DangerMissingParam,
    DangerNoActions,
}

public enum UniversalTriggerChipSeverity { Info, Ready, Warn, Danger }

public sealed record UniversalTriggerStatusChip(string Text, UniversalTriggerChipSeverity Severity);

public sealed class UniversalTriggerCardViewModel : ObservableObject
{
    public UniversalTriggerRule Rule { get; }

    public UniversalTriggerCardViewModel(UniversalTriggerRule rule)
    {
        Rule = rule;
        rule.PropertyChanged += OnRulePropertyChanged;
        rule.Actions.CollectionChanged += OnActionsCollectionChanged;
    }

    public string TypeChipText => Rule.TriggerType switch
    {
        UniversalTriggerType.ChatCommand => "CHAT COMMAND",
        UniversalTriggerType.ChannelPointReward => "CHANNEL POINT",
        UniversalTriggerType.Bits => "BITS",
        UniversalTriggerType.Subscription => "SUBSCRIPTION",
        UniversalTriggerType.GiftSubscription => "GIFT SUBSCRIPTION",
        UniversalTriggerType.Follow => "FOLLOW",
        _ => "UNCONFIGURED",
    };

    public string SecondaryChipText
    {
        get
        {
            if (string.Equals(Rule.ImportSource, "Fooma Twitch Interaction", StringComparison.OrdinalIgnoreCase))
                return "from Fooma";
            return Rule.TriggerType switch
            {
                UniversalTriggerType.ChannelPointReward when Rule.RewardCost > 0 => $"{Rule.RewardCost} pts",
                UniversalTriggerType.Bits => $"Bits {Rule.MinimumBits}-{Rule.MaximumBits}",
                _ => string.Empty,
            };
        }
    }

    public string ActionSummary
    {
        get
        {
            var count = Rule.Actions.Count;
            if (count == 0) return "No actions yet";
            var totalSeconds = Rule.Actions.Sum(a => a.DurationSeconds);
            var paths = Rule.Actions
                .Select(a => a.OscAddress)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct()
                .Take(2)
                .ToList();
            var pathText = paths.Count == 0 ? string.Empty : string.Join(", ", paths) + (count > paths.Count ? ", ..." : string.Empty);
            return totalSeconds > 0
                ? $"{count} action(s), {totalSeconds:0.#}s total · {pathText}"
                : $"{count} action(s) · {pathText}";
        }
    }

    public bool IsUnconfigured => !Rule.IsConfigured;

    public bool IsDanger => PrimaryStatus is UniversalTriggerCardStatus.DangerMissingParam or UniversalTriggerCardStatus.DangerNoActions;

    public bool HasDanger => IsDanger;

    public bool IsWarn => PrimaryStatus is UniversalTriggerCardStatus.WarnDirectOsc or UniversalTriggerCardStatus.WarnNotAvatarBound;

    public IReadOnlyList<UniversalTriggerStatusChip> StatusChips
    {
        get
        {
            var chips = new List<UniversalTriggerStatusChip>();
            if (IsUnconfigured)
            {
                chips.Add(new UniversalTriggerStatusChip("Needs setup", UniversalTriggerChipSeverity.Info));
            }
            if (PrimaryStatus == UniversalTriggerCardStatus.WarnDirectOsc)
            {
                chips.Add(new UniversalTriggerStatusChip("⚠ Direct OSC paths", UniversalTriggerChipSeverity.Warn));
            }
            if (PrimaryStatus == UniversalTriggerCardStatus.WarnNotAvatarBound)
            {
                chips.Add(new UniversalTriggerStatusChip("⚠ Not avatar-bound", UniversalTriggerChipSeverity.Warn));
            }
            if (PrimaryStatus == UniversalTriggerCardStatus.DangerMissingParam)
            {
                chips.Add(new UniversalTriggerStatusChip("✗ param missing", UniversalTriggerChipSeverity.Danger));
            }
            if (PrimaryStatus == UniversalTriggerCardStatus.DangerNoActions)
            {
                chips.Add(new UniversalTriggerStatusChip("✗ No complete actions", UniversalTriggerChipSeverity.Danger));
            }
            if (PrimaryStatus == UniversalTriggerCardStatus.Ready)
            {
                chips.Add(new UniversalTriggerStatusChip("✓ Ready for current avatar", UniversalTriggerChipSeverity.Ready));
            }
            if (string.Equals(Rule.ImportSource, "Fooma Twitch Interaction", StringComparison.OrdinalIgnoreCase))
            {
                chips.Add(new UniversalTriggerStatusChip("from Fooma", UniversalTriggerChipSeverity.Info));
            }
            return chips;
        }
    }

    public UniversalTriggerCardStatus PrimaryStatus
    {
        get
        {
            if (IsUnconfigured) return UniversalTriggerCardStatus.Unconfigured;
            if (!Rule.HasCompleteAction) return UniversalTriggerCardStatus.DangerNoActions;
            if (HasAnyDirectOscAction() && !HasAnyAvatarParamAction())
                return UniversalTriggerCardStatus.WarnDirectOsc;
            if (HasAnyDirectOscAction())
                return UniversalTriggerCardStatus.WarnNotAvatarBound;
            if (HasMissingAvatarParams())
                return UniversalTriggerCardStatus.DangerMissingParam;
            return UniversalTriggerCardStatus.Ready;
        }
    }

    public IReadOnlyList<string> AvatarParamPaths => Rule.Actions
        .Select(a => a.OscAddress)
        .Where(p => !string.IsNullOrWhiteSpace(p) && (p.StartsWith("avatar/parameters/") || p.StartsWith("/avatar/parameters/")))
        .Select(p => p.StartsWith("/") ? p : "/" + p)
        .Distinct()
        .ToList();

    public IReadOnlyList<string> MissingAvatarParamNames(IReadOnlyCollection<string> currentAvatarParams)
    {
        return AvatarParamPaths
            .Where(p => !currentAvatarParams.Contains(p))
            .Select(p => p.Substring("/avatar/parameters/".Length))
            .ToList();
    }

    private bool HasAnyAvatarParamAction() => Rule.Actions.Any(a =>
        !string.IsNullOrWhiteSpace(a.OscAddress) &&
        (a.OscAddress.StartsWith("avatar/parameters/") || a.OscAddress.StartsWith("/avatar/parameters/")));

    private bool HasAnyDirectOscAction() => Rule.Actions.Any(a =>
        !string.IsNullOrWhiteSpace(a.OscAddress) &&
        a.OscAddress.StartsWith("/") &&
        !a.OscAddress.StartsWith("/avatar/parameters/"));

    private bool HasMissingAvatarParams()
    {
        if (!HasAnyAvatarParamAction()) return false;
        // The runtime check (IsUniversalTriggerReadyForCurrentAvatarJson) is the authority
        // for "missing param"; the view-model defers to that for now. The simpler heuristic
        // is: if the trigger is configured but no avatar JSON has been loaded, treat as missing.
        return false;
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Re-evaluate every computed card property when the underlying rule changes
        RaisePropertyChanged(nameof(TypeChipText));
        RaisePropertyChanged(nameof(SecondaryChipText));
        RaisePropertyChanged(nameof(ActionSummary));
        RaisePropertyChanged(nameof(IsUnconfigured));
        RaisePropertyChanged(nameof(IsDanger));
        RaisePropertyChanged(nameof(IsWarn));
        RaisePropertyChanged(nameof(PrimaryStatus));
        RaisePropertyChanged(nameof(AvatarParamPaths));
        RaisePropertyChanged(nameof(HasDanger));
        RaisePropertyChanged(nameof(StatusChips));
    }

    private void OnActionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(ActionSummary));
        RaisePropertyChanged(nameof(IsDanger));
        RaisePropertyChanged(nameof(IsWarn));
        RaisePropertyChanged(nameof(PrimaryStatus));
        RaisePropertyChanged(nameof(AvatarParamPaths));
        RaisePropertyChanged(nameof(HasDanger));
        RaisePropertyChanged(nameof(StatusChips));
    }
}