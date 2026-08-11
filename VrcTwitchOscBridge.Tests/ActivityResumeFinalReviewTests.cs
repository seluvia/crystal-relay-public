using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class ActivityResumeFinalReviewTests
{
    [Fact]
    public async Task RemoveActivityAsync_RemovesOnlyThePersistedActivityInstance()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "CrystalRelayActivityResumeTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        var snapshotPath = Path.Combine(testDirectory, "activity-resume.json");
        var ruleId = Guid.NewGuid();

        try
        {
            var snapshot = new ActivityResumeSnapshot
            {
                CurrentAvatarId = "avtr_test",
                Activities =
                [
                    new ResumeActivity
                    {
                        Type = ResumeActivityType.Movement,
                        RuleId = ruleId,
                        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                        Payload = new Dictionary<string, object>
                        {
                            ["identity"] = "expired"
                        }
                    },
                    new ResumeActivity
                    {
                        Type = ResumeActivityType.Movement,
                        RuleId = ruleId,
                        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
                        Payload = new Dictionary<string, object>
                        {
                            ["identity"] = "replacement"
                        }
                    }
                ]
            };
            await File.WriteAllTextAsync(snapshotPath, JsonSerializer.Serialize(snapshot));

            await using (var service = new ActivityResumeService(snapshotPath))
            {
                await service.LoadPendingAsync();
                var loaded = service.GetPendingActivities();
                var expired = Assert.Single(loaded, activity =>
                    activity.Payload["identity"]?.ToString() == "expired");
                var replacement = Assert.Single(loaded, activity =>
                    activity.Payload["identity"]?.ToString() == "replacement");

                await service.RemoveActivityAsync(expired);

                var remaining = Assert.Single(service.GetPendingActivities());
                Assert.Same(replacement, remaining);
            }

            await using var reloadedService = new ActivityResumeService(snapshotPath);
            await reloadedService.LoadPendingAsync();
            var persistedRemaining = Assert.Single(reloadedService.GetPendingActivities());
            Assert.Equal("replacement", persistedRemaining.Payload["identity"]?.ToString());
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ResumePendingActivitiesSingleFlight_SkipsExpiredActivitiesBeforeDispatch()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var resumeBody = GetMethodBody(source, "private async Task ResumePendingActivitiesSingleFlightAsync");
        var normalizedBody = NormalizeWhitespace(resumeBody);

        Assert.Contains("RemoveExpiredActivitiesAsync()", normalizedBody, StringComparison.Ordinal);
        Assert.Contains("all saved activities have expired", normalizedBody, StringComparison.Ordinal);

        var removalIndex = normalizedBody.IndexOf("RemoveExpiredActivitiesAsync()", StringComparison.Ordinal);
        var avatarCheckIndex = normalizedBody.IndexOf("IsPendingForAvatar", removalIndex, StringComparison.Ordinal);
        var dispatchIndex = normalizedBody.IndexOf("ResumeActivityAsync(", removalIndex, StringComparison.Ordinal);

        Assert.True(removalIndex >= 0);
        Assert.True(avatarCheckIndex > removalIndex);
        Assert.True(dispatchIndex > avatarCheckIndex);
    }

    [Fact]
    public void ResumePendingActivitiesCarriesExpectedAvatarThroughDispatch()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var singleFlight = NormalizeWhitespace(GetMethodBody(source, "private async Task ResumePendingActivitiesSingleFlightAsync("));
        var activityDispatch = NormalizeWhitespace(GetMethodBody(source, "private async Task ResumeActivityAsync("));
        var scaleCore = NormalizeWhitespace(GetMethodBody(source, "private async Task ResumeActivityCoreAsync("));

        Assert.Contains("currentAvatarId", singleFlight, StringComparison.Ordinal);
        Assert.Contains("expectedAvatarId", activityDispatch, StringComparison.Ordinal);
        Assert.Contains("IsResumeAvatarCurrent", scaleCore, StringComparison.Ordinal);
    }

    [Fact]
    public void ResumePendingActivitiesClaimsAvatarScopedAttemptAfterEligibilityCheck()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var resumeBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task ResumePendingActivitiesSingleFlightAsync("));

        var pendingActivitiesIndex = resumeBody.IndexOf("var pendingActivities", StringComparison.Ordinal);
        var activityCleanupIndex = resumeBody.IndexOf(
            "await activityResumeService.RemoveActivityAsync(activity)",
            pendingActivitiesIndex,
            StringComparison.Ordinal);
        var avatarEligibilityIndex = resumeBody.IndexOf(
            "IsResumeAvatarCurrent(currentAvatarId)",
            activityCleanupIndex,
            StringComparison.Ordinal);
        var attemptClaimIndex = resumeBody.IndexOf(
            "if (!TryClaimResumeAttempt())",
            avatarEligibilityIndex,
            StringComparison.Ordinal);

        Assert.True(pendingActivitiesIndex >= 0);
        Assert.True(activityCleanupIndex > pendingActivitiesIndex);
        Assert.True(avatarEligibilityIndex > pendingActivitiesIndex);
        Assert.True(avatarEligibilityIndex > activityCleanupIndex);
        Assert.True(attemptClaimIndex > avatarEligibilityIndex);
        Assert.Contains("TryMarkActivityResumeAttempted(", resumeBody, StringComparison.Ordinal);
        Assert.Contains("expectedAvatarId", GetMethodDeclaration(
            source,
            "private bool TryMarkActivityResumeAttempted("), StringComparison.Ordinal);
    }

    [Fact]
    public void ScheduleReset_CarriesActivityIdentityIntoNormalCompletionCleanup()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var normalizedSource = NormalizeWhitespace(source);
        var scheduleBody = NormalizeWhitespace(GetMethodBody(source, "private void ScheduleReset("));
        var pendingResetDeclaration = NormalizeWhitespace(GetMethodBody(source, "private sealed record PendingResetState("));

        Assert.Contains("ResumeActivity? activityResumeEntry", GetMethodDeclaration(source, "private void ScheduleReset("), StringComparison.Ordinal);
        Assert.Contains("ResumeActivity? ActivityResumeEntry", pendingResetDeclaration, StringComparison.Ordinal);
        Assert.Contains("new PendingResetState(", scheduleBody, StringComparison.Ordinal);

        var pendingResetConstructionIndex = scheduleBody.IndexOf("new PendingResetState(", StringComparison.Ordinal);
        Assert.True(
            scheduleBody.IndexOf("activityResumeEntry", pendingResetConstructionIndex, StringComparison.Ordinal)
                > pendingResetConstructionIndex);
        Assert.Contains("pendingReset.ActivityResumeEntry", scheduleBody, StringComparison.Ordinal);
        AssertIdentityRemovalUsesPersistedActivity(
            scheduleBody,
            "pendingReset.RuleId",
            "pendingReset.ActivityResumeEntry");
        Assert.Contains("ActivityResumeEntry", normalizedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ScheduleJumpPulseReset_RemovesTheExactActivityIdentityOnCompletion()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var scheduleDeclaration = GetMethodDeclaration(source, "private void ScheduleJumpPulseReset(");
        var scheduleBody = NormalizeWhitespace(GetMethodBody(source, "private void ScheduleJumpPulseReset("));

        Assert.Contains("ResumeActivity? activityResumeEntry", scheduleDeclaration, StringComparison.Ordinal);
        Assert.Contains("activityResumeEntry", scheduleBody, StringComparison.Ordinal);
        AssertIdentityRemovalUsesPersistedActivity(
            scheduleBody,
            "rule.Id",
            "activityResumeEntry");
    }

    [Fact]
    public void AvatarChangeResume_UsesPersistedTargetDataForAnExactAction()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var normalizedSource = NormalizeWhitespace(source);
        var resumeBody = GetMethodBody(source, "private async Task ResumeActivityCoreAsync");
        var avatarChangeCase = NormalizeWhitespace(
            GetBlockStartingAt(resumeBody, "case ResumeActivityType.AvatarChange:"));
        var actionExecutionBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task ExecuteRuleActionAsync("));
        Assert.Contains(
            "private static string? GetResumeActivityPayloadString(",
            source,
            StringComparison.Ordinal);
        var payloadReaderBody = NormalizeWhitespace(
            GetMethodBody(source, "private static string? GetResumeActivityPayloadString("));

        Assert.Contains("[\"avatarTargetId\"]", actionExecutionBody, StringComparison.Ordinal);
        Assert.Contains("[\"avatarTargetName\"]", actionExecutionBody, StringComparison.Ordinal);
        Assert.Contains("GetResumeActivityPayloadString( activity.Payload, \"avatarTargetId\"", avatarChangeCase, StringComparison.Ordinal);
        Assert.Contains("GetResumeActivityPayloadString( activity.Payload, \"avatarTargetName\"", avatarChangeCase, StringComparison.Ordinal);
        Assert.Contains("value is JsonElement", payloadReaderBody, StringComparison.Ordinal);
        Assert.Contains("ValueKind.String", payloadReaderBody, StringComparison.Ordinal);
        Assert.Contains("GetString()", payloadReaderBody, StringComparison.Ordinal);
        Assert.Contains("new ResolvedRuleAction(", avatarChangeCase, StringComparison.Ordinal);
        Assert.Contains("resumeAction: resumeAction", avatarChangeCase, StringComparison.Ordinal);
        Assert.Contains("resumeActivityEntry: activity", avatarChangeCase, StringComparison.Ordinal);
        Assert.Contains("resumeAction", actionExecutionBody, StringComparison.Ordinal);
        var suppliedActionIndex = actionExecutionBody.IndexOf("resumeAction", StringComparison.Ordinal);
        var freshResolutionIndex = actionExecutionBody.IndexOf("ResolveActionAsync(", StringComparison.Ordinal);
        Assert.True(suppliedActionIndex >= 0);
        Assert.True(freshResolutionIndex > suppliedActionIndex);
        Assert.DoesNotContain("ResolveRouletteProfileAction", avatarChangeCase, StringComparison.Ordinal);
        Assert.Contains("JsonElement", normalizedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AvatarScaleHeightMismatchResume_CarriesOriginalActivityIntoRestoreContinuation()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var normalizedSource = NormalizeWhitespace(source);
        var resumeBody = GetMethodBody(source, "private async Task ResumeActivityCoreAsync");
        var avatarScaleCase = GetBlockStartingAt(resumeBody, "case ResumeActivityType.AvatarScale:");
        var mismatchBranchStart = avatarScaleCase.IndexOf(
            "else",
            avatarScaleCase.IndexOf("heightMatches", StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.True(mismatchBranchStart >= 0);
        var mismatchBranch = NormalizeWhitespace(GetBlockStartingAt(avatarScaleCase, "else", mismatchBranchStart));
        var executeScaleDeclaration = GetMethodDeclaration(source, "private async Task<bool> ExecuteAvatarScaleRuleAsync(");
        var executeScaleBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task<bool> ExecuteAvatarScaleRuleAsync("));
        var continuationDeclaration = GetMethodDeclaration(source, "private Task<bool> CommitAvatarScaleContinuationWithActivityAsync(");
        var continuationBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task<bool> CommitAvatarScaleContinuationCoreAsync("));

        Assert.Contains("activityResumeEntry: activity", mismatchBranch, StringComparison.Ordinal);
        Assert.Contains("ResumeActivity? activityResumeEntry", executeScaleDeclaration, StringComparison.Ordinal);
        Assert.Contains("activityResumeEntry: activityResumeEntry", executeScaleBody, StringComparison.Ordinal);
        Assert.Contains("ResumeActivity? activityResumeEntry", continuationDeclaration, StringComparison.Ordinal);
        Assert.Contains("activityResumeEntry: recordedActivityResumeEntry", continuationBody, StringComparison.Ordinal);
    }

    [Fact]
    public void AvatarScaleHeightMismatchResume_ReplaysPersistedTargetInsteadOfRecomputingIt()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var resumeBody = GetMethodBody(source, "private async Task ResumeActivityCoreAsync");
        var avatarScaleCase = NormalizeWhitespace(
            GetBlockStartingAt(resumeBody, "case ResumeActivityType.AvatarScale:"));
        var executeDeclaration = GetMethodDeclaration(
            source,
            "private async Task<bool> ExecuteAvatarScaleRuleAsync(");
        var executeBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task<bool> ExecuteAvatarScaleRuleAsync("));

        Assert.Contains("double? resumeTargetHeight", executeDeclaration, StringComparison.Ordinal);
        Assert.Contains("resumeTargetHeight: savedHeight", avatarScaleCase, StringComparison.Ordinal);
        Assert.Contains(
            "resumeTargetHeight ?? ResolveAvatarScaleTargetHeight",
            executeBody,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AvatarScaleResume_FencesInnerScaleWriteAndContinuationAgainstAvatarChanges()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var executeDeclaration = GetMethodDeclaration(
            source,
            "private async Task<bool> ExecuteAvatarScaleRuleAsync(");
        var executeBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task<bool> ExecuteAvatarScaleRuleAsync("));
        var continuationDeclaration = GetMethodDeclaration(
            source,
            "private async Task<bool> CommitAvatarScaleContinuationCoreAsync(");
        var continuationBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task<bool> CommitAvatarScaleContinuationCoreAsync("));
        var operationSendDeclaration = GetMethodDeclaration(
            source,
            "private async Task<bool> SendAvatarHeightForOperationAsync(");
        var operationSendBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task<bool> SendAvatarHeightForOperationAsync("));
        var valueSendBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task<bool> SendAvatarHeightValueAsync("));

        Assert.Contains("expectedResumeAvatarId", executeDeclaration, StringComparison.Ordinal);
        Assert.Contains("expectedResumeAvatarId", executeBody, StringComparison.Ordinal);
        Assert.Contains("IsResumeAvatarCurrent", executeBody, StringComparison.Ordinal);
        Assert.Contains("expectedResumeAvatarId", continuationDeclaration, StringComparison.Ordinal);
        Assert.Contains("IsResumeAvatarCurrent", continuationBody, StringComparison.Ordinal);
        Assert.Contains("CommitAvatarScaleContinuationWithActivityForResumeAsync", executeBody, StringComparison.Ordinal);
        Assert.Contains("string? expectedResumeAvatarId", operationSendDeclaration, StringComparison.Ordinal);
        Assert.Contains("IsResumeAvatarCurrent", operationSendBody, StringComparison.Ordinal);
        Assert.Contains("shouldContinue?.Invoke()", valueSendBody, StringComparison.Ordinal);
    }

    [Fact]
    public void AvatarScaleResume_BudgetsRestoreAndInitialTransitionsInsidePersistedLifetime()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var normalizedSource = NormalizeWhitespace(source);
        var resumeBody = NormalizeWhitespace(GetMethodBody(source, "private async Task ResumeActivityCoreAsync"));
        var durationHelperBody = NormalizeWhitespace(
            GetMethodBody(source, "private static double GetResumeAvatarScaleActiveTimeSeconds("));

        Assert.Contains(
            "GetResumeAvatarScaleActiveTimeSeconds( activity, rule, includeInitialTransition: !heightMatches)",
            resumeBody,
            StringComparison.Ordinal);
        Assert.Contains("GetResumeActivityRemainingSeconds(activity, 0", durationHelperBody, StringComparison.Ordinal);
        Assert.Contains("RestoreMode", durationHelperBody, StringComparison.Ordinal);
        Assert.Contains("includeInitialTransition", durationHelperBody, StringComparison.Ordinal);
        Assert.Contains("Math.Max(0", normalizedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AvatarScaleResume_OnlyUsesPersistedTargetWhenOneWasSaved()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var resumeBody = NormalizeWhitespace(GetMethodBody(source, "private async Task ResumeActivityCoreAsync"));
        var avatarScaleCase = GetBlockStartingAt(resumeBody, "case ResumeActivityType.AvatarScale:");

        Assert.Contains("var savedHeight = activity.CurrentValue", avatarScaleCase, StringComparison.Ordinal);
        Assert.Contains("activity.CurrentValue.HasValue", avatarScaleCase, StringComparison.Ordinal);
    }

    [Fact]
    public void ResumeActivity_MissingRulesAreRemovedAsStale()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var resumeBody = GetMethodBody(source, "private async Task ResumeActivityCoreAsync");

        foreach (var activityType in new[]
                 {
                     "ResumeActivityType.AvatarScale:",
                     "ResumeActivityType.Movement:",
                     "ResumeActivityType.AvatarChange:"
                 })
        {
            var activityCase = NormalizeWhitespace(GetBlockStartingAt(resumeBody, $"case {activityType}"));
            var missingRuleIndex = activityCase.IndexOf("if (rule is null)", StringComparison.Ordinal);
            var removalIndex = activityCase.IndexOf("RemoveActivityAsync(activity)", missingRuleIndex, StringComparison.Ordinal);

            Assert.True(missingRuleIndex >= 0);
            Assert.True(removalIndex > missingRuleIndex);
        }
    }

    [Fact]
    public void AvatarScaleResume_FindsConfiguredPowerUpAndCashScaleActions()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var resumeBody = GetMethodBody(source, "private async Task ResumeActivityCoreAsync");
        var avatarScaleCase = NormalizeWhitespace(
            GetBlockStartingAt(resumeBody, "case ResumeActivityType.AvatarScale:"));

        Assert.Contains("FindAvatarScaleRuleSnapshot(activity.RuleId)", avatarScaleCase, StringComparison.Ordinal);
    }

    [Fact]
    public void PendingAvatarScopedResetCleanup_IsTrackedForShutdown()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var methodBody = NormalizeWhitespace(
            GetMethodBody(source, "private void ResumePendingAvatarScopedResetsForCurrentAvatar("));

        Assert.Contains("StartTrackedMovementCleanup", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchingAvatarScaleResume_UsesPersistedLifetimeForCarryoverWindow()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var resumeBody = NormalizeWhitespace(GetMethodBody(source, "private async Task ResumeActivityCoreAsync"));
        var avatarScaleCase = NormalizeWhitespace(
            GetBlockStartingAt(resumeBody, "case ResumeActivityType.AvatarScale:"));

        Assert.Contains("GetResumeActivityRemainingSeconds(activity, 0)", avatarScaleCase, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAvatarScaleEffectDurationSeconds(rule)", avatarScaleCase, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtendActiveActivityTimers_PersistsExtendedResumeExpiryThroughWriter()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var body = NormalizeWhitespace(GetMethodBody(source, "private void ExtendActiveActivityTimers"));
        var normalizedSource = NormalizeWhitespace(source);

        Assert.Contains("ActivityResumeEntry", body, StringComparison.Ordinal);
        Assert.Contains("ExpiresAt", body, StringComparison.Ordinal);
        Assert.Contains("CommitExtendedActivityResumeAsync", body, StringComparison.Ordinal);
        Assert.Contains("CommitAsync", normalizedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ResumeActivityCore_UsesPersistedRemainingDurationForTimedActivities()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var resumeBody = NormalizeWhitespace(GetMethodBody(source, "private async Task ResumeActivityCoreAsync"));
        var avatarScaleCase = NormalizeWhitespace(
            GetBlockStartingAt(resumeBody, "case ResumeActivityType.AvatarScale:"));
        var movementCase = NormalizeWhitespace(
            GetBlockStartingAt(resumeBody, "case ResumeActivityType.Movement:"));
        var avatarChangeCase = NormalizeWhitespace(
            GetBlockStartingAt(resumeBody, "case ResumeActivityType.AvatarChange:"));
        var durationHelperBody = NormalizeWhitespace(GetResumeDurationHelperBody(source));

        Assert.Contains("ExpiresAt", durationHelperBody, StringComparison.Ordinal);
        Assert.Contains("DateTimeOffset.UtcNow", durationHelperBody, StringComparison.Ordinal);
        Assert.Matches(
            @"\b\w*(?:Resume|Activity)\w*(?:Remaining|Duration|Seconds)\w*\(\s*activity(?:\.ExpiresAt)?\b",
            resumeBody);
        Assert.Matches(@"\bwith\s*\{[^}]*ActiveTimeSeconds\s*=", avatarScaleCase);
        Assert.Matches(@"\bwith\s*\{[^}]*DurationSeconds\s*=", movementCase);
        Assert.Matches(@"\bwith\s*\{[^}]*DurationSeconds\s*=", avatarChangeCase);
    }

    [Fact]
    public void GlitchyAvatarScale_NormalActivityPersistsTheExactRestoreActivity()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var glitchyScaleBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task<bool> ExecuteGlitchyAvatarScaleRuleAsync("));
        var restoreBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task RunAvatarScaleRestoreSequenceAsync("));

        Assert.Contains("new ResumeActivity", glitchyScaleBody, StringComparison.Ordinal);
        Assert.Contains("Type = ResumeActivityType.AvatarScale", glitchyScaleBody, StringComparison.Ordinal);
        Assert.Contains("ExpiresAt", glitchyScaleBody, StringComparison.Ordinal);
        Assert.Contains("RecordActivityStartedAsync", glitchyScaleBody, StringComparison.Ordinal);
        Assert.Contains("activityResumeEntry:", glitchyScaleBody, StringComparison.Ordinal);
        AssertIdentityRemovalUsesPersistedActivity(
            restoreBody,
            "completedRuleId",
            "completedActivity");
    }

    [Fact]
    public void GlitchyMovement_NormalActivityPersistsAndCompletesTheExactActivity()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var executeBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task ExecuteGlitchyMovementRuleActionAsync("));
        var runDeclaration = NormalizeWhitespace(
            GetMethodDeclaration(source, "private async Task RunGlitchyMovementSequenceAsync("));
        var runBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task RunGlitchyMovementSequenceAsync("));

        Assert.Contains("new ResumeActivity", executeBody, StringComparison.Ordinal);
        Assert.Contains("Type = ResumeActivityType.Movement", executeBody, StringComparison.Ordinal);
        Assert.Contains("ExpiresAt", executeBody, StringComparison.Ordinal);
        Assert.Contains("movementDirection", executeBody, StringComparison.Ordinal);
        Assert.Contains("RecordActivityStartedAsync", executeBody, StringComparison.Ordinal);
        Assert.Contains("activityResumeEntry:", executeBody, StringComparison.Ordinal);
        Assert.Contains("ResumeActivity? activityResumeEntry", runDeclaration, StringComparison.Ordinal);
        AssertIdentityRemovalUsesPersistedActivity(
            runBody,
            "rule.Id",
            "activityResumeEntry");
    }

    [Fact]
    public void AvatarScaleRestoreReplacementAndCancellation_RemoveOnlyTheExactResumeEntry()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var scheduleBody = NormalizeWhitespace(
            GetMethodBody(source, "private bool ScheduleAvatarScaleRestoreSequence("));
        var cancelBody = NormalizeWhitespace(
            GetMethodBody(source, "private void CancelAvatarScaleRestoreSequenceForCurrentAvatar("));

        Assert.Matches(@"(?:previous|old)\w*ActivityResumeEntry", scheduleBody);
        Assert.Contains(
            "RemoveAvatarScaleActivityResumeEntryAsync( previousActivityResumeEntry",
            scheduleBody,
            StringComparison.Ordinal);
        Assert.Contains("ActivityResumeEntry", cancelBody, StringComparison.Ordinal);
        Assert.Contains("RemoveAvatarScaleActivityResumeEntryAsync", cancelBody, StringComparison.Ordinal);
    }

    [Fact]
    public void AvatarChangeResumeWithoutTarget_SkipsInsteadOfResolvingFreshAvatarRoulette()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var resumeBody = GetMethodBody(source, "private async Task ResumeActivityCoreAsync");
        var avatarChangeCase = NormalizeWhitespace(
            GetBlockStartingAt(resumeBody, "case ResumeActivityType.AvatarChange:"));
        const string missingTargetGuard = "if (string.IsNullOrWhiteSpace(resumeTargetId))";

        var guardIndex = avatarChangeCase.IndexOf(missingTargetGuard, StringComparison.Ordinal);
        var executeIndex = avatarChangeCase.IndexOf("ExecuteRuleActionAsync(", StringComparison.Ordinal);
        Assert.True(guardIndex >= 0);
        Assert.True(executeIndex > guardIndex);

        var guardBody = GetBlockStartingAt(avatarChangeCase, missingTargetGuard);
        Assert.True(
            guardBody.Contains("return;", StringComparison.Ordinal)
                || guardBody.Contains("RemoveActivityAsync(activity)", StringComparison.Ordinal));
    }

    [Fact]
    public void MovementResume_UsesThePersistedMovementDirection()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var resumeBody = GetMethodBody(source, "private async Task ResumeActivityCoreAsync");
        var movementCase = NormalizeWhitespace(
            GetBlockStartingAt(resumeBody, "case ResumeActivityType.Movement:"));

        Assert.Contains(
            "GetResumeActivityPayloadString( activity.Payload, \"movementDirection\"",
            movementCase,
            StringComparison.Ordinal);
        Assert.Matches(@"Enum\.TryParse<\s*PlayerMovementDirection\s*>", movementCase);
        Assert.Matches(@"\bwith\s*\{[^}]*MovementDirection\s*=", movementCase);
        Assert.Contains("executionRule", movementCase, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ExecuteRuleActionAsync( rule,",
            movementCase,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StopAsync_ReleasesMovementInputsBeforeClearingRuntimeState()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var stopBody = NormalizeWhitespace(GetMethodBody(source, "private async Task StopCoreAsync()"));
        var resetHelperBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task ResetRuntimeEffectsBeforeOscShutdownAsync"));

        var resetHelperIndex = stopBody.IndexOf(
            "ResetRuntimeEffectsBeforeOscShutdownAsync()",
            StringComparison.Ordinal);
        var resetIndex = resetHelperBody.IndexOf("ResetPendingRulesAsync()", StringComparison.Ordinal);
        var stopOscIndex = stopBody.IndexOf("StopOscRouterSafelyAsync()", StringComparison.Ordinal);
        var clearIndex = stopBody.IndexOf("ClearRuntimeState()", StringComparison.Ordinal);

        Assert.True(resetHelperIndex >= 0);
        Assert.True(resetIndex >= 0);
        Assert.DoesNotContain("ResetPendingRulesAsync()", stopBody, StringComparison.Ordinal);
        Assert.True(stopOscIndex > resetHelperIndex);
        Assert.True(clearIndex > resetHelperIndex);
    }

    [Fact]
    public void MovementCleanupPaths_AreTrackedAndJoinedDuringShutdown()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var normalizedSource = NormalizeWhitespace(source);
        var stopBody = NormalizeWhitespace(GetMethodBody(source, "private async Task StopCoreAsync()"));
        var resetHelperBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task ResetRuntimeEffectsBeforeOscShutdownAsync"));

        Assert.Contains("StartTrackedMovementCleanup", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("ResetRuntimeEffectsBeforeOscShutdownAsync()", stopBody, StringComparison.Ordinal);
        Assert.Contains("WaitForActiveMovementCleanupTasksAsync", resetHelperBody, StringComparison.Ordinal);
        Assert.Contains("RunQuickMovementTestAsync", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("ScheduleReset", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("ScheduleJumpPulseReset", normalizedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistentEffectShutdown_DrainsWorkersBeforePendingMovementCleanup()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var resetBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task ResetRuntimeEffectsBeforeOscShutdownAsync"));

        var persistentDrainIndex = resetBody.IndexOf(
            "WaitForPersistentEffectTasksAsync",
            StringComparison.Ordinal);
        var scaleWriteDrainIndex = resetBody.IndexOf(
            "WaitForAvatarScaleWriteUsersAsync",
            StringComparison.Ordinal);
        var pendingResetIndex = resetBody.IndexOf(
            "ResetPendingRulesAsync()",
            StringComparison.Ordinal);
        var movementCleanupIndex = resetBody.LastIndexOf(
            "WaitForActiveMovementCleanupTasksAsync()",
            StringComparison.Ordinal);

        Assert.True(persistentDrainIndex >= 0);
        Assert.True(scaleWriteDrainIndex > persistentDrainIndex);
        Assert.True(pendingResetIndex > scaleWriteDrainIndex);
        Assert.True(movementCleanupIndex > pendingResetIndex);
    }

    [Fact]
    public void PersistentEffectShutdown_DoesNotPromoteStateOnlyNotificationsToResetWorkers()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var resetBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task ResetRuntimeEffectsBeforeOscShutdownAsync"));

        Assert.DoesNotContain("ScheduleAvatarScaleEffectStateNotification", resetBody, StringComparison.Ordinal);
        Assert.DoesNotContain("ScheduleCooldownStateNotification", resetBody, StringComparison.Ordinal);
        Assert.DoesNotContain("ScheduleLockoutStateNotification", resetBody, StringComparison.Ordinal);
    }

    [Fact]
    public void QueuedAvatarSwitchDrain_UsesWorkerCancellationForTransitionGateWait()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var normalizedSource = NormalizeWhitespace(source);
        var methodIndex = normalizedSource.IndexOf(
            "private void EnsureQueuedAvatarSwitchDrain()",
            StringComparison.Ordinal);
        var cancellationWaitIndex = normalizedSource.IndexOf(
            "TryEnterTimedSupporterOverrideTransitionAsync(cancellationToken)",
            methodIndex,
            StringComparison.Ordinal);
        var uncancellableWaitIndex = normalizedSource.IndexOf(
            "TryEnterTimedSupporterOverrideTransitionAsync(CancellationToken.None)",
            methodIndex,
            StringComparison.Ordinal);

        Assert.True(methodIndex >= 0);
        Assert.True(cancellationWaitIndex > methodIndex);
        Assert.True(uncancellableWaitIndex < 0 || uncancellableWaitIndex < cancellationWaitIndex);
    }

    [Fact]
    public void PersistentEffectCatchLogs_SanitizeWorkerExceptionMessages()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));

        foreach (var methodSignature in new[]
        {
            "private void ScheduleActiveFloatRedeemCompletion(",
            "private bool ScheduleActiveFloatRedeemCompletionAfterGracePeriod(",
            "private void ScheduleFloatPulseRestore(",
            "private async Task RunGlitchyLoopAsync(",
            "private async Task RunAvatarScaleRestoreSequenceAsync(",
            "private async Task RunSupporterGrowthScaleSessionAsync(",
            "private void EnsureQueuedRuleDrain(",
            "private void EnsureQueuedLaneDrain(",
            "private void EnsureQueuedAvatarSwitchDrain(",
            "private void EnsureQueuedAvatarScaleOperationDrain()"
        })
        {
            var body = NormalizeWhitespace(GetMethodBody(source, methodSignature));
            if (body.Contains("catch (Exception ex)", StringComparison.Ordinal))
            {
                Assert.Contains(
                    "SensitiveTextSanitizer.Sanitize(ex.Message)",
                    body,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void TimedActivity_IsPersistedBeforeItsResetCleanupCanStart()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var actionBody = NormalizeWhitespace(GetMethodBody(source, "private async Task ExecuteRuleActionAsync("));
        var persistIndex = actionBody.IndexOf("RecordActivityStartedAsync", StringComparison.Ordinal);
        var resetIndex = actionBody.IndexOf("ScheduleReset(", StringComparison.Ordinal);
        var jumpResetIndex = actionBody.IndexOf("ScheduleJumpPulseReset(", StringComparison.Ordinal);

        Assert.True(persistIndex >= 0);
        Assert.True(resetIndex > persistIndex || jumpResetIndex > persistIndex);
    }

    [Fact]
    public void AvatarScaleRestore_PersistsActivityBeforeSchedulingItsTimer()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var continuationBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task<bool> CommitAvatarScaleContinuationCoreAsync("));
        var persistIndex = continuationBody.IndexOf("RecordActivityStartedAsync", StringComparison.Ordinal);
        var scheduleIndex = continuationBody.IndexOf("ScheduleAvatarScaleRestoreSequence", StringComparison.Ordinal);

        Assert.True(persistIndex >= 0);
        Assert.True(scheduleIndex > persistIndex);
    }

    private static string GetResumeDurationHelperBody(string source)
    {
        foreach (Match match in Regex.Matches(source, @"private static [^{;]+\b\w+\s*\("))
        {
            if (!Regex.IsMatch(
                    match.Value,
                    "Resume|Remaining|Duration|Expires",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                continue;
            }

            var bodyStart = source.IndexOf('{', match.Index);
            if (bodyStart < 0)
            {
                continue;
            }

            var body = GetBalancedBlock(source, bodyStart);
            if (body.Contains("ExpiresAt", StringComparison.Ordinal)
                && body.Contains("DateTimeOffset.UtcNow", StringComparison.Ordinal))
            {
                return body;
            }
        }

        Assert.Fail("Could not find a static resume-duration helper that derives remaining time from ExpiresAt.");
        return string.Empty;
    }

    private static void AssertIdentityRemovalUsesPersistedActivity(
        string source,
        string ruleIdExpression,
        string activityExpression)
    {
        var recordEndedCall = $"RecordActivityEndedAsync( {ruleIdExpression}, {activityExpression}";
        var removeCall = $"RemoveActivityAsync( {activityExpression}";
        Assert.True(
            source.Contains(recordEndedCall, StringComparison.Ordinal)
                || source.Contains(removeCall, StringComparison.Ordinal),
            $"Expected exact activity cleanup using {activityExpression}.");
    }

    private static string GetMethodDeclaration(string source, string methodSignatureStart)
    {
        var methodStart = source.IndexOf(methodSignatureStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Could not find method signature starting with '{methodSignatureStart}'.");

        var bodyStart = source.IndexOf('{', methodStart);
        Assert.True(bodyStart >= 0, $"Could not find method body for '{methodSignatureStart}'.");
        return source[methodStart..bodyStart];
    }

    private static string GetMethodBody(string source, string methodSignatureStart)
    {
        var methodStart = source.IndexOf(methodSignatureStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Could not find method signature starting with '{methodSignatureStart}'.");

        var bodyStart = source.IndexOf('{', methodStart);
        Assert.True(bodyStart >= 0, $"Could not find method body for '{methodSignatureStart}'.");
        return GetBalancedBlock(source, bodyStart);
    }

    private static string GetBlockStartingAt(string source, string marker, int searchStart = 0)
    {
        var markerStart = source.IndexOf(marker, searchStart, StringComparison.Ordinal);
        Assert.True(markerStart >= 0, $"Could not find block marker '{marker}'.");

        var bodyStart = source.IndexOf('{', markerStart);
        Assert.True(bodyStart >= 0, $"Could not find block body for '{marker}'.");
        return GetBalancedBlock(source, bodyStart);
    }

    private static string GetBalancedBlock(string source, int bodyStart)
    {
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
                    return source[bodyStart..(index + 1)];
                }
            }
        }

        throw new InvalidOperationException("Could not find the end of a source block.");
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
