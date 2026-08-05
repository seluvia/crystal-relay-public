using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarScaleSafetyRuntimeExecutionTests
{
    [Fact]
    public void BridgeCoordinatorClampAvatarScaleHeight_UsesSnapshotSafetyLimit()
    {
        var safety = new AvatarScaleSafetySettings
        {
            CurrentMaximumHeightMeters = 2
        };
        var rule = new AvatarScaleRule
        {
            Name = "Runtime Tall",
            AdvancedRangeEnabled = true,
            TargetHeightMeters = 50
        };
        var snapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(rule, safety);
        var method = typeof(BridgeCoordinator).GetMethod(
            "ClampAvatarScaleHeight",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var clamped = Assert.IsType<double>(method.Invoke(null, [snapshot, 50d]));

        Assert.Equal(2, clamped, precision: 3);
    }

    [Fact]
    public void BridgeCoordinatorActiveSafetyClampHelper_UsesActiveConfigurationRange()
    {
        var settings = new AppSettings();
        settings.AvatarScaleSafety.CurrentMaximumHeightMeters = 2.5;
        var configuration = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault());
        var coordinator = RuntimeHelpers.GetUninitializedObject(typeof(BridgeCoordinator));
        var activeConfigurationField = typeof(BridgeCoordinator).GetField(
            "activeConfiguration",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var method = typeof(BridgeCoordinator).GetMethod(
            "ClampAvatarScaleHeightToActiveSafety",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(activeConfigurationField);
        Assert.NotNull(method);
        activeConfigurationField.SetValue(coordinator, configuration);

        var clamped = Assert.IsType<double>(method.Invoke(coordinator, [50d]));

        Assert.Equal(2.5, clamped, precision: 3);
    }

    [Fact]
    public void BridgeCoordinatorDedicatedHeightSendClamp_PreservesNonAdvancedRuleSafeMaximum()
    {
        var settings = new AppSettings();
        settings.AvatarScaleSafety.CurrentMinimumHeightMeters = AvatarScaleRule.SafeMaximumHeightMeters + 50;
        settings.AvatarScaleSafety.CurrentMaximumHeightMeters = AvatarScaleRule.SafeMaximumHeightMeters + 100;
        var configuration = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault());
        var rule = BridgeRuntimeConfiguration.CreateManualTestSnapshot(
            new AvatarScaleRule
            {
                Name = "Normal Safe Max",
                AdvancedRangeEnabled = false,
                TargetHeightMeters = AvatarScaleRule.SafeMaximumHeightMeters
            },
            settings.AvatarScaleSafety);
        var coordinator = RuntimeHelpers.GetUninitializedObject(typeof(BridgeCoordinator));
        var activeConfigurationField = typeof(BridgeCoordinator).GetField(
            "activeConfiguration",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var method = typeof(BridgeCoordinator).GetMethod(
            "ClampAvatarScaleHeightForSend",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(activeConfigurationField);
        Assert.NotNull(method);
        activeConfigurationField.SetValue(coordinator, configuration);

        var clamped = Assert.IsType<double>(method.Invoke(coordinator, [AvatarScaleRule.SafeMaximumHeightMeters, rule]));

        Assert.False(rule.AdvancedRangeEnabled);
        Assert.Equal(AvatarScaleRule.SafeMaximumHeightMeters, clamped, precision: 3);
    }

    [Fact]
    public void BridgeCoordinatorEyeHeightRawValueClamp_OnlyClampsFloatEyeHeightValues()
    {
        var settings = new AppSettings();
        settings.AvatarScaleSafety.CurrentMaximumHeightMeters = 2.5;
        var configuration = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault());
        var coordinator = RuntimeHelpers.GetUninitializedObject(typeof(BridgeCoordinator));
        var activeConfigurationField = typeof(BridgeCoordinator).GetField(
            "activeConfiguration",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var method = typeof(BridgeCoordinator).GetMethod(
            "ClampAvatarEyeHeightRawValueIfNeeded",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(activeConfigurationField);
        Assert.NotNull(method);
        activeConfigurationField.SetValue(coordinator, configuration);

        Assert.Equal("2.5", method.Invoke(coordinator, ["/avatar/eyeheight", OscParameterType.Float, "50"]));
        Assert.Equal("50", method.Invoke(coordinator, ["/avatar/parameters/Height", OscParameterType.Float, "50"]));
        Assert.Equal("50", method.Invoke(coordinator, ["/avatar/eyeheight", OscParameterType.Int, "50"]));
        Assert.Equal("not-a-number", method.Invoke(coordinator, ["/avatar/eyeheight", OscParameterType.Float, "not-a-number"]));
    }

    [Fact]
    public void BridgeCoordinatorDirectAvatarHeightSend_ClampsBeforeOscPacketBuild()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var sendBody = GetMethodBody(source, "private async Task<bool> SendAvatarHeightValueAsync");
        var clampIndex = sendBody.IndexOf(
            "heightMeters = ClampAvatarScaleHeightForSend(heightMeters, rule);",
            StringComparison.Ordinal);
        var packetIndex = sendBody.IndexOf(
            "var floatValue = (float)heightMeters;",
            StringComparison.Ordinal);

        Assert.True(clampIndex >= 0, "SendAvatarHeightValueAsync should clamp heightMeters against active AvatarScaleSafety before sending.");
        Assert.True(packetIndex > clampIndex, "The active safety clamp must run before the OSC float value is built.");
        Assert.Contains("activeConfiguration?.AvatarScaleSafety", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BridgeCoordinatorDedicatedHeightSendPipeline_PassesOriginatingRuleToFinalClamp()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var normalizedSource = NormalizeWhitespace(source);
        var operationBody = GetMethodBody(source, "private async Task<bool> SendAvatarHeightForOperationAsync");
        var transitionBody = GetMethodBody(source, "private async Task<bool> SendAvatarHeightAsync");

        Assert.Contains("private async Task<bool> SendAvatarHeightForOperationAsync( ActiveAvatarScaleOperationTicket operation, double targetHeight, double smoothSeconds, CancellationToken cancellationToken, Action? afterFirstSuccessfulSend = null, AvatarScaleRuleSnapshot? rule = null, Func<bool>? shouldContinue = null)", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("private async Task<bool> SendAvatarHeightAsync( double targetHeight, double smoothSeconds, CancellationToken cancellationToken, Action? afterFirstSuccessfulSend = null, Func<bool>? shouldContinue = null, AvatarScaleRuleSnapshot? rule = null)", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("rule: rule", source, StringComparison.Ordinal);
        Assert.Contains("SendAvatarHeightAsync( targetHeight, smoothSeconds, cancellationToken, afterFirstSuccessfulSend, IsCurrent, rule)", NormalizeWhitespace(operationBody), StringComparison.Ordinal);
        Assert.Contains("SendAvatarHeightValueAsync( targetHeight, cancellationToken, rule, shouldContinue, afterFirstSuccessfulSend)", NormalizeWhitespace(transitionBody), StringComparison.Ordinal);
        Assert.Contains("SendAvatarHeightValueAsync( value, cancellationToken, rule, shouldContinue, afterFirstSuccessfulSend)", NormalizeWhitespace(transitionBody), StringComparison.Ordinal);
    }

    [Fact]
    public void BridgeCoordinatorUniversalEyeHeightPackets_UseSafetyAwarePacketWrappers()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var body = GetMethodBody(source, "private byte[] BuildUniversalOscPacket");

        Assert.Contains("BuildOscPacketForAddressWithAvatarScaleSafety", body, StringComparison.Ordinal);
        Assert.Contains("BuildAvatarParameterPacketWithAvatarScaleSafety", body, StringComparison.Ordinal);
        Assert.DoesNotContain("vrChatOscClient.BuildPacketForAddress", body, StringComparison.Ordinal);
        Assert.DoesNotContain("vrChatOscClient.BuildAvatarParameterPacket", body, StringComparison.Ordinal);
    }

    [Fact]
    public void BridgeCoordinatorAvatarParameterFloatPackets_UseSafetyAwarePacketWrappers()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var methodNames = new[]
        {
            "private async Task SendSingleFloatAvatarParameterValueAsync",
            "private async Task<ResolvedRuleAction> ResolveFloatActionAsync",
            "private void ScheduleFloatPulseRestore",
            "private ResolvedRuleAction ResolveGlitchyFloatSession",
            "private async Task<ResolvedRuleAction> ResolveSetTriggerActionAsync",
            "private SetTriggerRestoreResolution BuildSetTriggerRestoreResolution"
        };

        foreach (var methodName in methodNames)
        {
            var body = GetMethodBody(source, methodName);

            Assert.Contains("BuildAvatarParameterPacketWithAvatarScaleSafety", body, StringComparison.Ordinal);
            Assert.DoesNotContain("vrChatOscClient.BuildAvatarParameterPacket", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BridgeCoordinatorEyeHeightObservedValues_UseSafetyClampedValues()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var textBody = GetMethodBody(source, "private bool TryCreateObservedValueFromText");
        var existingBody = GetMethodBody(source, "private bool TryCreateObservedValueFromExisting");
        var singleFloatBody = GetMethodBody(source, "private async Task SendSingleFloatAvatarParameterValueAsync");

        Assert.Contains("ClampAvatarEyeHeightRawValueIfNeeded", textBody, StringComparison.Ordinal);
        Assert.Contains("ClampAvatarEyeHeightRawValueIfNeeded", existingBody, StringComparison.Ordinal);
        Assert.Contains("new OscObservedValue(address, OscParameterType.Float, (float)clampedValue)", singleFloatBody, StringComparison.Ordinal);
        Assert.DoesNotContain("new OscObservedValue(address, OscParameterType.Float, (float)resetValue)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BridgeCoordinatorTimedEyeHeightFloatActions_UseClampedSessionAndDisplayValues()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var timedBody = GetMethodBody(source, "private async Task ExecuteTimedFloatAvatarParameterRuleActionAsync");
        var resolveBody = GetMethodBody(source, "private async Task<ResolvedRuleAction> ResolveFloatActionAsync");
        var glitchyBody = GetMethodBody(source, "private ResolvedRuleAction ResolveGlitchyFloatSession");
        var glitchyLoopBody = GetMethodBody(source, "private async Task RunGlitchyLoopAsync");

        Assert.Contains("var clampedTargetValue = ClampAvatarEyeHeightValueIfNeeded(address, targetValue);", timedBody, StringComparison.Ordinal);
        Assert.Contains("var clampedResetValue = ClampAvatarEyeHeightValueIfNeeded(address, resetValue);", timedBody, StringComparison.Ordinal);
        Assert.Contains("clampedTargetValue", timedBody, StringComparison.Ordinal);
        Assert.Contains("clampedResetValue", timedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("session.CurrentValue = targetValue;", timedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("FloatValueModeConverter.ToOscText(targetValue)", timedBody, StringComparison.Ordinal);

        Assert.Contains("var clampedNextValue = ClampAvatarEyeHeightValueIfNeeded(address, nextValue);", resolveBody, StringComparison.Ordinal);
        Assert.Contains("var clampedResetValue = effectiveReset.HasValue", resolveBody, StringComparison.Ordinal);
        Assert.DoesNotContain("displayValue: FloatValueModeConverter.ToOscText(nextValue)", resolveBody, StringComparison.Ordinal);

        Assert.Contains("var clampedNextValue = ClampAvatarEyeHeightValueIfNeeded(address, nextValue);", glitchyBody, StringComparison.Ordinal);
        Assert.DoesNotContain("FloatValueModeConverter.ToOscText(nextValue)", glitchyBody, StringComparison.Ordinal);

        Assert.Contains("var clampedValue = ClampAvatarEyeHeightValueIfNeeded(session.Address, value);", glitchyLoopBody, StringComparison.Ordinal);
        Assert.Contains("session.CurrentValue = clampedValue;", glitchyLoopBody, StringComparison.Ordinal);
        Assert.DoesNotContain("session.CurrentValue = value;", glitchyLoopBody, StringComparison.Ordinal);
    }

    [Fact]
    public void BridgeCoordinatorActiveFloatBoostEyeHeightActions_UseClampedBoostValue()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var body = GetMethodBody(source, "private async Task ApplyActiveFloatBoostRewardAsync");
        var normalizedBody = NormalizeWhitespace(body);

        Assert.Contains("clampedBoostedValue = ClampAvatarEyeHeightValueIfNeeded(session.Address, boostedValue);", body, StringComparison.Ordinal);
        Assert.Contains("boostMaximumReached = IsAtOrAboveActiveFloatBoostMaximum(clampedBoostedValue, upperBound);", body, StringComparison.Ordinal);
        Assert.Contains("ComputeLimitState( session.Rule.Rule, clampedBoostedValue,", normalizedBody, StringComparison.Ordinal);
        Assert.Contains("session.CurrentValue, clampedBoostedValue, inSeconds,", normalizedBody, StringComparison.Ordinal);
        Assert.Contains("session.CurrentValue = clampedBoostedValue;", body, StringComparison.Ordinal);
        Assert.Contains("RememberAvatarParameterValue(rule, FloatValueModeConverter.ToOscText(clampedBoostedValue));", body, StringComparison.Ordinal);
        Assert.Contains("FloatValueModeConverter.ToOscText(clampedBoostedValue)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("session.CurrentValue = boostedValue;", body, StringComparison.Ordinal);
        Assert.DoesNotContain("FloatValueModeConverter.ToOscText(boostedValue)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void BridgeCoordinatorAvatarScaleCarryover_PreservesSourceRuleForHeightClamp()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var normalizedSource = NormalizeWhitespace(source);
        var handleBody = NormalizeWhitespace(GetMethodBody(source, "private async Task HandleAvatarScaleAvatarChangedAsync"));
        var snapshotBody = NormalizeWhitespace(GetMethodBody(source, "private AvatarScaleCarryoverSnapshot? TryCreateAvatarScaleCarryoverSnapshot"));

        Assert.Contains("TryCommitPendingAvatarScaleHeightRestores( pendingRestores, sequenceId, runtimeGeneration)", handleBody, StringComparison.Ordinal);
        Assert.Contains("SendAvatarHeightValueAsync( carryover.CarriedHeightMeters, cancellationToken, FindAvatarScaleRuleSnapshot(carryover.SourceRuleId),", handleBody, StringComparison.Ordinal);
        Assert.Contains("private sealed record PendingAvatarScaleHeightRestoreState( double RestoreHeightMeters, DateTimeOffset SourceActiveUntil, string SourceRuleName, Guid SourceRuleId);", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("private bool RecordPendingAvatarScaleHeightRestore( string avatarId, double restoreHeightMeters, DateTimeOffset activeUntil, string sourceRuleName, Guid sourceRuleId, long expectedAvatarChangeSequenceId, long expectedRuntimeGeneration)", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("new PendingAvatarScaleHeightRestoreState( activeCarryover.RestoreHeightMeters, activeCarryover.ActiveUntil, activeCarryover.SourceRuleName, activeCarryover.SourceRuleId)", snapshotBody, StringComparison.Ordinal);
        Assert.Contains("new PendingAvatarScaleHeightRestoreState( previousRestoreHeight.Value, latestSession.ActiveUntil, latestSession.RuleName, latestSession.RuleId)", snapshotBody, StringComparison.Ordinal);
        Assert.Contains("return new AvatarScaleCarryoverSnapshot( activeSequence.Rule?.Id ?? Guid.Empty, Guid.Empty, activeSequence.SequenceId,", snapshotBody, StringComparison.Ordinal);
    }

    [Fact]
    public void BridgeCoordinatorPendingAvatarScaleRestore_UsesSourceRuleAwareClamp()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var body = NormalizeWhitespace(GetMethodBody(source, "private async Task RestorePendingAvatarScaleHeightForCurrentAvatarAsync"));

        Assert.Contains("SendAvatarHeightForOperationAsync( operation, pendingRestore.RestoreHeightMeters, 0, cancellationToken, rule: FindAvatarScaleRuleSnapshot(pendingRestore.SourceRuleId), shouldContinue: () => IsAvatarScaleRuntimeGenerationCurrent(runtimeGeneration))", body, StringComparison.Ordinal);
    }

    [Fact]
    public void BridgeCoordinatorFindAvatarScaleRuleSnapshot_IncludesPowerUpScaleActions()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var body = NormalizeWhitespace(GetMethodBody(source, "private AvatarScaleRuleSnapshot? FindAvatarScaleRuleSnapshot"));

        Assert.Contains("activeConfiguration.PowerUpRules", body, StringComparison.Ordinal);
        Assert.Contains(".Select(rule => rule.ScaleAction)", body, StringComparison.Ordinal);
        Assert.Contains("rule?.Id == ruleId", body, StringComparison.Ordinal);
    }

    [Fact]
    public void BridgeCoordinatorPausedDevAvatarScaleResume_UsesSnapshotRuleForHeightClamp()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var body = NormalizeWhitespace(GetMethodBody(source, "private async Task ResumePausedAvatarScaleTimerAfterDevAsync"));

        Assert.Contains("SendAvatarHeightForOperationAsync( operation, snapshot.CarriedHeightMeters, 0, cancellationToken, shouldContinue: () => IsAvatarScaleRuntimeGenerationCurrent(runtimeGeneration), rule: snapshot.Rule)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowViewModelManualScaleTests_PassSettingsAvatarScaleSafety()
    {
        var source = NormalizeWhitespace(File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "ViewModels", "MainWindowViewModel.cs")));

        Assert.Contains(
            "BridgeRuntimeConfiguration.CreateManualTestSnapshot(ruleToTest, Settings.AvatarScaleSafety)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "BridgeRuntimeConfiguration.CreateManualTestSnapshot(SelectedCashPaymentRule, Settings.AvatarScaleSafety)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "BridgeRuntimeConfiguration.CreateManualTestSnapshot(SelectedPowerUpRule, MasterAvatarProfile, Settings.AvatarScaleSafety)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowViewModelAvatarScaleSafetyChanges_AreWiredToSaveRefreshAndSync()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "ViewModels", "MainWindowViewModel.cs"));
        var normalizedSource = NormalizeWhitespace(source);
        var handlerBody = GetMethodBody(source, "private void AvatarScaleSafetyChanged");

        Assert.Contains("appSettings.AvatarScaleSafety.PropertyChanged += AvatarScaleSafetyChanged;", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("appSettings.AvatarScaleSafety.PropertyChanged -= AvatarScaleSafetyChanged;", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("QueueSave();", handlerBody, StringComparison.Ordinal);
        Assert.Contains("QueueBridgeRefresh();", handlerBody, StringComparison.Ordinal);
        Assert.Contains("RaisePropertyChanged(nameof(AvatarScaleRuntimeStatusText));", handlerBody, StringComparison.Ordinal);
        Assert.Contains("RaisePropertyChanged(nameof(AvatarScaleSets));", handlerBody, StringComparison.Ordinal);
        Assert.Contains("RaisePropertyChanged(nameof(AvatarScaleRules));", handlerBody, StringComparison.Ordinal);
        Assert.Contains("QueueManagedRewardSync();", handlerBody, StringComparison.Ordinal);
    }

    private static string GetMethodBody(string source, string methodSignatureStart)
    {
        var methodStart = source.IndexOf(methodSignatureStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Could not find method signature starting with '{methodSignatureStart}'.");

        var bodyStart = source.IndexOf('{', methodStart);
        Assert.True(bodyStart >= 0, $"Could not find method body for '{methodSignatureStart}'.");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(bodyStart, index - bodyStart + 1);
                }
            }
        }

        throw new InvalidOperationException($"Could not find method body end for '{methodSignatureStart}'.");
    }

    private static string NormalizeWhitespace(string source) => Regex.Replace(source, @"\s+", " ");

    private static string FindSourceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find source file.", Path.Combine(relativeParts));
    }
}
