using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public sealed record FoomaInteractionImportResult(
    IReadOnlyList<UniversalTriggerRule> Triggers,
    int ImportedCount,
    int FusedCommandCount,
    int SkippedCount);

public static class FoomaInteractionConfigImporter
{
    public static async Task<FoomaInteractionImportResult> ImportAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var triggers = new List<UniversalTriggerRule>();
        var skipped = 0;

        if (root.TryGetProperty("Commands", out var commandsNode)
            && commandsNode.ValueKind == JsonValueKind.Object)
        {
            foreach (var commandProperty in commandsNode.EnumerateObject())
            {
                if (TryCreateCommandTrigger(commandProperty.Name, commandProperty.Value, out var trigger))
                {
                    triggers.Add(trigger);
                }
                else
                {
                    skipped++;
                }
            }
        }

        skipped += ImportArray(root, "Rewards", TryCreateRewardTrigger, triggers);
        skipped += ImportArray(root, "Bits", TryCreateBitsTrigger, triggers);
        skipped += ImportArray(root, "Subscriptions", TryCreateSubscriptionTrigger, triggers);
        skipped += ImportArray(root, "Follows", TryCreateFollowTrigger, triggers);

        var fusedTriggers = UniversalTriggerFusionService.FuseMatchingCommandFallbacks(triggers, out var fusedCommandCount);
        return new FoomaInteractionImportResult(fusedTriggers, fusedTriggers.Count, fusedCommandCount, skipped);
    }

    private static int ImportArray(
        JsonElement root,
        string propertyName,
        TryCreateTrigger createTrigger,
        List<UniversalTriggerRule> triggers)
    {
        var skipped = 0;
        if (!root.TryGetProperty(propertyName, out var node)
            || node.ValueKind != JsonValueKind.Array)
        {
            return skipped;
        }

        foreach (var item in node.EnumerateArray())
        {
            if (createTrigger(item, out var trigger))
            {
                triggers.Add(trigger);
            }
            else
            {
                skipped++;
            }
        }

        return skipped;
    }

    private static bool TryCreateCommandTrigger(
        string commandName,
        JsonElement node,
        out UniversalTriggerRule trigger)
    {
        trigger = CreateBaseTrigger(node, commandName);
        trigger.TriggerType = UniversalTriggerType.ChatCommand;
        trigger.CommandText = commandName;
        trigger.ChatCommandPermission = ResolveCommandPermission(node);
        return IsImportReady(trigger);
    }

    private static bool TryCreateRewardTrigger(JsonElement node, out UniversalTriggerRule trigger)
    {
        var rewardName = GetString(node, "Name");
        trigger = CreateBaseTrigger(node, string.IsNullOrWhiteSpace(rewardName) ? "Imported Reward" : rewardName);
        trigger.TriggerType = UniversalTriggerType.ChannelPointReward;
        trigger.RewardId = GetString(node, "Id");
        trigger.RewardTitle = rewardName;
        return IsImportReady(trigger);
    }

    private static bool TryCreateBitsTrigger(JsonElement node, out UniversalTriggerRule trigger)
    {
        var minimumBits = Math.Max(1, GetInt(node, "MinBits", 1));
        var maximumBits = Math.Max(minimumBits, GetInt(node, "MaxBits", minimumBits));
        trigger = CreateBaseTrigger(node, $"Bits {minimumBits}-{maximumBits}");
        trigger.TriggerType = UniversalTriggerType.Bits;
        trigger.MinimumBits = minimumBits;
        trigger.MaximumBits = maximumBits;
        return IsImportReady(trigger);
    }

    private static bool TryCreateSubscriptionTrigger(JsonElement node, out UniversalTriggerRule trigger)
    {
        var type = GetInt(node, "Type", 0);
        var tier = GetString(node, "Tiers");
        var label = type == 1 ? "Gift Subscription" : "Subscription";
        trigger = CreateBaseTrigger(node, string.IsNullOrWhiteSpace(tier) ? label : $"{label} Tier {tier}");
        trigger.TriggerType = type == 1 ? UniversalTriggerType.GiftSubscription : UniversalTriggerType.Subscription;
        trigger.SubscriptionTier = tier;
        trigger.MinimumMonths = GetInt(node, "MinMonths", -1);
        trigger.MaximumMonths = GetInt(node, "MaxMonths", -1);
        return IsImportReady(trigger);
    }

    private static bool TryCreateFollowTrigger(JsonElement node, out UniversalTriggerRule trigger)
    {
        trigger = CreateBaseTrigger(node, "Follow");
        trigger.TriggerType = UniversalTriggerType.Follow;
        return IsImportReady(trigger);
    }

    private static UniversalTriggerRule CreateBaseTrigger(JsonElement node, string name)
    {
        return new UniversalTriggerRule
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Imported Universal Trigger" : name.Trim(),
            IsEnabled = GetBool(node, "IsEnabled", true),
            ExecuteRandomAction = GetBool(node, "ExecuteRandomAction", false),
            GlobalDelaySeconds = GetDelaySeconds(node, "GlobalDelay"),
            UserDelaySeconds = GetDelaySeconds(node, "UserDelay"),
            ImportSource = "Fooma Twitch Interaction",
            Actions = new ObservableCollection<UniversalTriggerAction>(FlattenActions(node))
        };
    }

    private static List<UniversalTriggerAction> FlattenActions(JsonElement node)
    {
        var actions = new List<UniversalTriggerAction>();
        if (!node.TryGetProperty("Actions", out var actionsNode)
            || actionsNode.ValueKind != JsonValueKind.Object)
        {
            return actions;
        }

        foreach (var group in actionsNode.EnumerateObject())
        {
            if (group.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var actionNode in group.Value.EnumerateArray())
            {
                var address = GetString(actionNode, "Address");
                if (string.IsNullOrWhiteSpace(address)
                    || !actionNode.TryGetProperty("TargetValue", out var targetValue))
                {
                    continue;
                }

                var defaultValue = actionNode.TryGetProperty("DefaultValue", out var defaultValueNode)
                    ? defaultValueNode
                    : targetValue;
                var valueKind = ResolveValueKind(targetValue);
                actions.Add(new UniversalTriggerAction
                {
                    OscAddress = address,
                    ValueKind = valueKind,
                    TargetValue = JsonValueToText(targetValue, valueKind),
                    DefaultValue = JsonValueToText(defaultValue, ResolveValueKind(defaultValue)),
                    DurationSeconds = GetDouble(actionNode, "Duration", 0),
                    AddToQueue = GetBool(actionNode, "AddToQueue", true),
                    ImportGroupKey = group.Name
                });
            }
        }

        return actions;
    }

    private static bool IsImportReady(UniversalTriggerRule trigger)
    {
        if (trigger.Actions.Count == 0)
        {
            return false;
        }

        return trigger.TriggerType switch
        {
            UniversalTriggerType.ChatCommand => ChatCommandUtility.IsConfigured(trigger.CommandText),
            UniversalTriggerType.ChannelPointReward => !string.IsNullOrWhiteSpace(trigger.RewardId)
                || !string.IsNullOrWhiteSpace(trigger.RewardTitle),
            _ => true
        };
    }

    private static ChatCommandPermission ResolveCommandPermission(JsonElement node)
    {
        if (GetBool(node, "CanNormalViewerExecute", false)
            || GetBool(node, "CanSubscriberExecute", false)
            || GetBool(node, "CanVipExecute", false))
        {
            return ChatCommandPermission.Everyone;
        }

        if (GetBool(node, "CanModeratorExecute", false))
        {
            return ChatCommandPermission.Moderators;
        }

        return ChatCommandPermission.Broadcaster;
    }

    private static UniversalTriggerValueKind ResolveValueKind(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False => UniversalTriggerValueKind.Bool,
            JsonValueKind.Number when value.TryGetInt32(out _) => UniversalTriggerValueKind.Int,
            JsonValueKind.Number => UniversalTriggerValueKind.Float,
            _ => UniversalTriggerValueKind.String
        };
    }

    private static string JsonValueToText(JsonElement value, UniversalTriggerValueKind valueKind)
    {
        return valueKind switch
        {
            UniversalTriggerValueKind.Bool => value.ValueKind == JsonValueKind.True
                ? "True"
                : value.ValueKind == JsonValueKind.False
                    ? "False"
                    : GetStringValue(value),
            UniversalTriggerValueKind.Int when value.TryGetInt32(out var intValue) => intValue.ToString(CultureInfo.InvariantCulture),
            UniversalTriggerValueKind.Float when value.TryGetDouble(out var doubleValue) => doubleValue.ToString(CultureInfo.InvariantCulture),
            _ => GetStringValue(value)
        };
    }

    private static string GetStringValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "True",
        JsonValueKind.False => "False",
        _ => string.Empty
    };

    private static string GetString(JsonElement node, string propertyName)
    {
        return node.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static bool GetBool(JsonElement node, string propertyName, bool defaultValue)
    {
        if (!node.TryGetProperty(propertyName, out var value))
        {
            return defaultValue;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }

    private static int GetInt(JsonElement node, string propertyName, int defaultValue)
    {
        return node.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var intValue)
            ? intValue
            : defaultValue;
    }

    private static double GetDouble(JsonElement node, string propertyName, double defaultValue)
    {
        return node.TryGetProperty(propertyName, out var value) && value.TryGetDouble(out var doubleValue)
            ? doubleValue
            : defaultValue;
    }

    private static int GetDelaySeconds(JsonElement node, string propertyName)
    {
        var delayText = GetString(node, propertyName);
        if (string.IsNullOrWhiteSpace(delayText))
        {
            return 0;
        }

        return TimeSpan.TryParse(delayText, CultureInfo.InvariantCulture, out var delay)
            ? Math.Max(0, (int)Math.Round(delay.TotalSeconds))
            : 0;
    }

    private delegate bool TryCreateTrigger(JsonElement node, out UniversalTriggerRule trigger);
}
