using System;
using System.ComponentModel;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class MovementRedeemCardViewModel : ObservableObject
{
    private readonly TriggerRule rule;

    public MovementRedeemCardViewModel(TriggerRule rule, Action<MovementRedeemCardViewModel>? testAction)
    {
        this.rule = rule ?? throw new ArgumentNullException(nameof(rule));
        this.testAction = testAction;
        UpdateFromRule();
    }

    public Guid Id => rule.Id;

    public string Name
    {
        get => rule.Name;
        set
        {
            if (rule.Name != value)
            {
                rule.Name = value;
                RaisePropertyChanged();
            }
        }
    }

    public PlayerMovementDirection MovementDirection => rule.MovementDirection;

    public bool IsVrOnly => MovementTypeClassifier.IsVrOnly(rule.MovementDirection);

    public bool IsAxisType => MovementTypeClassifier.IsAxisType(rule.MovementDirection);

    public string? BehaviorTooltip => MovementTypeClassifier.GetBehaviorTooltip(rule.MovementDirection);

    public double DurationSeconds
    {
        get => rule.DurationSeconds;
        set
        {
            var clamped = Math.Max(1, value);
            if (Math.Abs(rule.DurationSeconds - clamped) > 0.01)
            {
                rule.DurationSeconds = clamped;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(DurationText));
            }
        }
    }

    public int CooldownSeconds
    {
        get => rule.CooldownSeconds;
        set
        {
            var clamped = Math.Max(0, value);
            if (rule.CooldownSeconds != clamped)
            {
                rule.CooldownSeconds = clamped;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(CooldownText));
            }
        }
    }

    public float? FloatValue
    {
        get => rule.FloatValue;
        set
        {
            if (rule.FloatValue != value)
            {
                rule.FloatValue = value;
                RaisePropertyChanged();
            }
        }
    }

    public bool IsEnabled
    {
        get => rule.IsEnabled;
        set
        {
            if (rule.IsEnabled != value)
            {
                rule.IsEnabled = value;
                RaisePropertyChanged();
            }
        }
    }

    public string DurationText => $"{DurationSeconds:F1}s";

    public string CooldownText => CooldownSeconds > 0 ? $"{CooldownSeconds:F0}s cooldown" : "No cooldown";

    public string DurationWithCooldownText => CooldownSeconds > 0
        ? $"{DurationSeconds:F1}s / {CooldownSeconds:F0}s"
        : $"{DurationSeconds:F1}s";

    public string DirectionDisplayName => GetDisplayName(rule.MovementDirection);

    public string DisplayName
    {
        get
        {
            if (rule.TriggerType == TwitchTriggerType.ChannelPoints)
                return string.IsNullOrWhiteSpace(rule.ChannelPointRewardTitle)
                    ? rule.HasConfiguredChatCommand
                        ? rule.ChatCommandText.Trim()
                        : "New Movement Rule"
                    : rule.ChannelPointRewardTitle.Trim();

            if (rule.HasConfiguredChatCommand)
                return rule.ChatCommandText.Trim();

            var dirName = GetDisplayName(rule.MovementDirection);
            return rule.TriggerType switch
            {
                TwitchTriggerType.Bits => $"Bits {dirName}",
                TwitchTriggerType.Subscriptions => $"Subs {dirName}",
                TwitchTriggerType.Follow => $"Follow {dirName}",
                _ => dirName
            };
        }
    }

    public bool HasChannelPointTrigger => !string.IsNullOrEmpty(rule.ChannelPointRewardId);
    public bool HasChatCommandTrigger => rule.HasConfiguredChatCommand;
    public bool HasBitsTrigger => rule.TriggerType == TwitchTriggerType.Bits;
    public bool HasSubsTrigger => rule.TriggerType == TwitchTriggerType.Subscriptions;
    public bool HasFollowTrigger => rule.TriggerType == TwitchTriggerType.Follow;

    public TriggerRule GetRule() => rule;

    private readonly Action<MovementRedeemCardViewModel>? testAction;

    public string TestButtonText => "Test";

    public void Test()
    {
        testAction?.Invoke(this);
    }

    private void UpdateFromRule()
    {
        RaisePropertyChanged(nameof(Name));
        RaisePropertyChanged(nameof(MovementDirection));
        RaisePropertyChanged(nameof(IsVrOnly));
        RaisePropertyChanged(nameof(IsAxisType));
        RaisePropertyChanged(nameof(BehaviorTooltip));
        RaisePropertyChanged(nameof(DurationSeconds));
        RaisePropertyChanged(nameof(CooldownSeconds));
        RaisePropertyChanged(nameof(FloatValue));
        RaisePropertyChanged(nameof(IsEnabled));
        RaisePropertyChanged(nameof(DurationText));
        RaisePropertyChanged(nameof(CooldownText));
        RaisePropertyChanged(nameof(DirectionDisplayName));
        RaisePropertyChanged(nameof(HasChannelPointTrigger));
        RaisePropertyChanged(nameof(HasChatCommandTrigger));
        RaisePropertyChanged(nameof(HasBitsTrigger));
        RaisePropertyChanged(nameof(HasSubsTrigger));
        RaisePropertyChanged(nameof(HasFollowTrigger));
        RaisePropertyChanged(nameof(DurationWithCooldownText));
        RaisePropertyChanged(nameof(DisplayName));
    }

    internal static string GetDisplayName(PlayerMovementDirection direction) => direction switch
    {
        PlayerMovementDirection.Forward => "Move Forward",
        PlayerMovementDirection.Backward => "Move Backward",
        PlayerMovementDirection.Left => "Strafe Left",
        PlayerMovementDirection.Right => "Strafe Right",
        PlayerMovementDirection.Jump => "Jump",
        PlayerMovementDirection.Run => "Run / Sprint",
        PlayerMovementDirection.SpinLeft => "Spin Left",
        PlayerMovementDirection.SpinRight => "Spin Right",
        PlayerMovementDirection.RandomMovement => "Random Movement",
        PlayerMovementDirection.GlitchyMovement => "Glitchy Movement",
        PlayerMovementDirection.LookHorizontal => "Look Horizontal (Axis)",
        PlayerMovementDirection.LookLeft => "Look Left",
        PlayerMovementDirection.LookRight => "Look Right",
        PlayerMovementDirection.ComfortLeft => "Snap Turn Left (VR)",
        PlayerMovementDirection.ComfortRight => "Snap Turn Right (VR)",
        PlayerMovementDirection.GrabLeft => "Grab (Left Hand)",
        PlayerMovementDirection.GrabRight => "Grab (Right Hand)",
        PlayerMovementDirection.UseLeft => "Use (Left Hand)",
        PlayerMovementDirection.UseRight => "Use (Right Hand)",
        PlayerMovementDirection.DropLeft => "Drop (Left Hand)",
        PlayerMovementDirection.DropRight => "Drop (Right Hand)",
        PlayerMovementDirection.MoveHoldFB => "Move Held F/B",
        PlayerMovementDirection.Vertical => "Move Vertical (Axis)",
        PlayerMovementDirection.Horizontal => "Move Horizontal (Axis)",
        PlayerMovementDirection.UseAxisRight => "Use (Axis, Right Hand)",
        PlayerMovementDirection.GrabAxisRight => "Grab (Axis, Right Hand)",
        PlayerMovementDirection.SpinHoldCwCcw => "Spin Held CW/CCW",
        PlayerMovementDirection.SpinHoldUD => "Spin Held Up/Down",
        PlayerMovementDirection.SpinHoldLR => "Spin Held Left/Right",
        PlayerMovementDirection.QuickMenuToggleLeft => "Quick Menu (Left)",
        PlayerMovementDirection.QuickMenuToggleRight => "Quick Menu (Right)",
        PlayerMovementDirection.PanicButton => "Safe Mode (Panic)",
        PlayerMovementDirection.Voice => "Voice Toggle",
        _ => direction.ToString(),
    };
}
