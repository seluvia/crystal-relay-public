using System.Collections.ObjectModel;
using System.Globalization;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

internal static class UniversalTriggerFusionService
{
    public static int FuseMatchingCommandFallbacks(IList<UniversalTriggerRule> triggers)
    {
        if (triggers.Count == 0)
        {
            return 0;
        }

        var commandTriggers = triggers
            .Where(trigger => trigger.TriggerType == UniversalTriggerType.ChatCommand
                && ChatCommandUtility.IsConfigured(trigger.CommandText))
            .ToArray();
        var rewardTriggers = triggers
            .Where(trigger => trigger.TriggerType == UniversalTriggerType.ChannelPointReward)
            .ToArray();
        if (commandTriggers.Length == 0 || rewardTriggers.Length == 0)
        {
            return 0;
        }

        var consumedCommandIds = new HashSet<Guid>();
        var commandsToRemove = new List<UniversalTriggerRule>();
        foreach (var rewardTrigger in rewardTriggers)
        {
            var rewardSignature = BuildTriggerSignature(rewardTrigger);
            if (string.IsNullOrWhiteSpace(rewardSignature))
            {
                continue;
            }

            var matchingCommand = commandTriggers
                .Where(command => !consumedCommandIds.Contains(command.Id)
                    && string.Equals(BuildTriggerSignature(command), rewardSignature, StringComparison.Ordinal))
                .OrderByDescending(command => NamesMatch(command.CommandText, rewardTrigger.RewardTitle))
                .FirstOrDefault();
            if (matchingCommand is null)
            {
                continue;
            }

            var commandText = ChatCommandUtility.Normalize(matchingCommand.CommandText);
            var rewardCommandText = ChatCommandUtility.Normalize(rewardTrigger.CommandText);
            if (rewardTrigger.ChatCommandEnabled
                && ChatCommandUtility.IsConfigured(rewardCommandText)
                && !string.Equals(rewardCommandText, commandText, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rewardTrigger.ChatCommandEnabled = true;
            rewardTrigger.CommandText = commandText;
            rewardTrigger.ChatCommandPermission = matchingCommand.ChatCommandPermission;
            consumedCommandIds.Add(matchingCommand.Id);
            commandsToRemove.Add(matchingCommand);
        }

        foreach (var commandTrigger in commandsToRemove)
        {
            triggers.Remove(commandTrigger);
        }

        return commandsToRemove.Count;
    }

    public static IReadOnlyList<UniversalTriggerRule> FuseMatchingCommandFallbacks(
        IReadOnlyList<UniversalTriggerRule> triggers,
        out int fusedCount)
    {
        var mutableTriggers = new ObservableCollection<UniversalTriggerRule>(triggers);
        fusedCount = FuseMatchingCommandFallbacks(mutableTriggers);
        return mutableTriggers.ToArray();
    }

    private static string BuildTriggerSignature(UniversalTriggerRule trigger)
    {
        if (trigger.Actions.Count == 0)
        {
            return string.Empty;
        }

        var actionParts = trigger.Actions
            .Where(action => !string.IsNullOrWhiteSpace(action.OscAddress)
                && !string.IsNullOrWhiteSpace(action.TargetValue)
                && (action.DurationSeconds <= 0 || !string.IsNullOrWhiteSpace(action.DefaultValue)))
            .Select(BuildActionSignature)
            .OrderBy(part => part, StringComparer.Ordinal)
            .ToArray();
        if (actionParts.Length == 0)
        {
            return string.Empty;
        }

        return $"{trigger.ExecuteRandomAction}|{string.Join(";", actionParts)}";
    }

    private static string BuildActionSignature(UniversalTriggerAction action)
    {
        return string.Join(
            "|",
            action.OscAddress.Trim(),
            action.ValueKind,
            NormalizeValue(action.TargetValue, action.ValueKind),
            NormalizeValue(action.DefaultValue, action.ValueKind),
            Math.Max(0, action.DurationSeconds).ToString("G17", CultureInfo.InvariantCulture),
            action.AddToQueue);
    }

    private static string NormalizeValue(string value, UniversalTriggerValueKind valueKind)
    {
        var trimmedValue = value?.Trim() ?? string.Empty;
        return valueKind switch
        {
            UniversalTriggerValueKind.Bool when bool.TryParse(trimmedValue, out var boolValue) => boolValue.ToString(CultureInfo.InvariantCulture),
            UniversalTriggerValueKind.Int when int.TryParse(trimmedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue) => intValue.ToString(CultureInfo.InvariantCulture),
            UniversalTriggerValueKind.Float when double.TryParse(trimmedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue) => doubleValue.ToString("G17", CultureInfo.InvariantCulture),
            _ => trimmedValue
        };
    }

    private static bool NamesMatch(string commandText, string rewardTitle)
    {
        var normalizedCommand = ChatCommandUtility.Normalize(commandText).TrimStart('!');
        var normalizedReward = ManagedRewardPresentation.StripPrefix(rewardTitle).Trim();
        return !string.IsNullOrWhiteSpace(normalizedCommand)
            && !string.IsNullOrWhiteSpace(normalizedReward)
            && string.Equals(normalizedCommand, normalizedReward, StringComparison.OrdinalIgnoreCase);
    }
}
