using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class ReschedulableActivityDeadlineTests
{
    [Fact]
    public async Task Extend_WakesExistingWaiter_AndWaitsForNewDeadline()
    {
        var deadline = new ReschedulableActivityDeadline(DateTimeOffset.UtcNow.AddMilliseconds(80));
        var wait = deadline.WaitAsync(CancellationToken.None);

        await Task.Delay(35);
        deadline.Extend(TimeSpan.FromMilliseconds(160));
        await Task.Delay(70);

        Assert.False(wait.IsCompleted);
        await wait.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CancellationStopsWaiter()
    {
        using var cancellation = new CancellationTokenSource();
        var deadline = new ReschedulableActivityDeadline(DateTimeOffset.UtcNow.AddSeconds(10));
        var wait = deadline.WaitAsync(cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }

    [Fact]
    public async Task WaitAsync_WhenDelayWinnerRacesExtension_WaitsForExtendedDeadline()
    {
        var oldDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var extendedDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayCall = 0;
        var deadline = new ReschedulableActivityDeadline(
            DateTimeOffset.UtcNow.AddSeconds(30),
            (_, _) => Interlocked.Increment(ref delayCall) == 1
                ? oldDelay.Task
                : extendedDelay.Task);
        var wait = deadline.WaitAsync(CancellationToken.None);

        oldDelay.TrySetResult();
        deadline.Extend(TimeSpan.FromSeconds(30));

        await Task.Yield();
        Assert.False(wait.IsCompleted);
        extendedDelay.TrySetResult();
        await wait;
    }

    [Theory]
    [InlineData("RunAvatarScaleRestoreSequenceAsync", "sequence.ActivityDeadline.WaitAsync")]
    [InlineData("ScheduleActiveFloatRedeemCompletion", "session.ActivityDeadline.WaitAsync")]
    [InlineData("RunSupporterGrowthScaleSessionAsync", "supporterState.ActivityDeadline.WaitAsync")]
    [InlineData("ScheduleTimedSupporterOverrideCompletion", "activeState.ActivityDeadline.WaitAsync")]
    [InlineData("ScheduleReset", "pendingReset.ActivityDeadline.WaitAsync")]
    [InlineData("EnsureQueuedLaneDrain", "activeLane.ActivityDeadline.WaitAsync")]
    public void EveryExtendedActivityWorkerWaitsOnItsReschedulableDeadline(
        string methodName,
        string expectedWait)
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var body = NormalizeWhitespace(GetMethodBody(source, methodName));

        Assert.Contains(expectedWait, body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RunAvatarScaleRestoreSequenceAsync", "ActiveUntil")]
    [InlineData("ScheduleActiveFloatRedeemCompletion", "ActivityDeadline.Deadline")]
    [InlineData("RunSupporterGrowthScaleSessionAsync", "ActivityDeadline.Deadline")]
    [InlineData("ScheduleReset", "ActivityDeadline.Deadline")]
    [InlineData("CompleteTimedSupporterOverrideCoreAsync", "ActivityDeadline.Deadline")]
    public void CompletionWorkersClaimAuthoritativeDeadline(string methodName, string expectedCheck)
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var body = NormalizeWhitespace(GetMethodBody(source, methodName));

        Assert.Contains(expectedCheck, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtendedDeadlineCompletesExactlyOnceAfterExtension()
    {
        var deadline = new ReschedulableActivityDeadline(DateTimeOffset.UtcNow.AddMilliseconds(70));
        var completionCount = 0;
        var completion = Task.Run(async () =>
        {
            await deadline.WaitAsync(CancellationToken.None);
            Interlocked.Increment(ref completionCount);
        });

        await Task.Delay(25);
        deadline.Extend(TimeSpan.FromMilliseconds(140));
        await Task.Delay(65);
        Assert.False(completion.IsCompleted);

        await completion.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, completionCount);
    }

    [Fact]
    public async Task GraceAwareWait_ExtensionDuringGrace_DoesNotCompleteUntilExtendedDeadline()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var deadline = new ReschedulableActivityDeadline(startedAt.AddMilliseconds(80));
        var graceEndsAt = startedAt.AddMilliseconds(450);
        var graceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completionCount = 0;
        var completedAt = DateTimeOffset.MinValue;
        var completion = Task.Run(async () =>
        {
            await InvokeGraceAwareWaitAsync(
                deadline,
                () =>
                {
                    graceStarted.TrySetResult();
                    return graceEndsAt - DateTimeOffset.UtcNow;
                },
                () => DateTimeOffset.UtcNow < graceEndsAt,
                CancellationToken.None);
            completedAt = DateTimeOffset.UtcNow;
            Interlocked.Increment(ref completionCount);
        });

        await graceStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(50);
        Assert.True(DateTimeOffset.UtcNow < graceEndsAt, "The extension must occur during avatar grace.");

        deadline.Extend(TimeSpan.FromMilliseconds(1200));
        var extendedDeadline = deadline.Deadline;

        await Task.Delay(600);
        Assert.False(completion.IsCompleted);

        await completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, completionCount);
        Assert.True(
            completedAt >= extendedDeadline - TimeSpan.FromMilliseconds(100),
            $"Completion occurred before the extended deadline: completed at {completedAt:O}, extended deadline {extendedDeadline:O}.");
    }

    [Fact]
    public void ScheduleReset_RefreshesCurrentRecordAfterDeadlineWake()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var body = NormalizeWhitespace(GetMethodBody(source, "ScheduleReset"));
        const string wait = "await pendingReset.ActivityDeadline.WaitAsync(pendingReset.Cancellation.Token);";
        const string refresh = "TryGetCurrentPendingReset(rule.Id, cancellation, out pendingReset)";

        var waitIndex = body.IndexOf(wait, StringComparison.Ordinal);
        var refreshIndex = body.IndexOf(refresh, waitIndex, StringComparison.Ordinal);
        var cleanupRecordIndex = body.IndexOf("releasedPendingReset = currentPendingReset", waitIndex, StringComparison.Ordinal);

        Assert.True(waitIndex >= 0, "ScheduleReset must wait on the pending reset deadline.");
        Assert.True(refreshIndex > waitIndex,
            "ScheduleReset must refresh the same-cancellation pending reset after the deadline wait.");
        Assert.True(cleanupRecordIndex > waitIndex,
            "ScheduleReset cleanup must claim the current same-cancellation pending reset record.");
        Assert.Contains("ReferenceEquals(currentPendingReset.Cancellation, cancellation)", body, StringComparison.Ordinal);
        Assert.False(body.Contains("ReferenceEquals(currentPendingReset, pendingReset)", StringComparison.Ordinal),
            "Completion cleanup must not reject an extension-only replacement by record identity.");
    }

    [Fact]
    public void ScheduleReset_SetTriggerObservationKeepsCurrentPendingResetState()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var body = NormalizeWhitespace(GetMethodBody(source, "ScheduleReset"));
        var observationIndex = body.IndexOf("ResolveSetTriggerDiffRestoreAsync", StringComparison.Ordinal);
        var stateCheckIndex = body.IndexOf(
            "ReferenceEquals(currentPendingReset.Cancellation, cancellation)",
            observationIndex,
            StringComparison.Ordinal);
        var packetMergeIndex = body.IndexOf("pendingReset = currentPendingReset with", stateCheckIndex, StringComparison.Ordinal);

        Assert.True(observationIndex >= 0, "ScheduleReset must retain Set Trigger observation handling.");
        Assert.True(stateCheckIndex > observationIndex,
            "Set Trigger restore resolution must reacquire the current same-cancellation reset state.");
        Assert.True(packetMergeIndex > stateCheckIndex,
            "Set Trigger restore packets must be merged onto the current pending reset record.");
    }

    [Theory]
    [InlineData(
        "ScheduleActiveFloatRedeemCompletionAfterGracePeriod",
        "session.ActivityDeadline",
        "completionCancellation.Token")]
    [InlineData(
        "ScheduleTimedSupporterOverrideCompletionAfterGracePeriod",
        "activeState.ActivityDeadline",
        "completionToken")]
    public void GraceCompletion_UsesSharedAuthoritativeDeadlineWait(
        string methodName,
        string expectedDeadline,
        string expectedCancellationToken)
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var body = NormalizeWhitespace(GetMethodBody(source, methodName));

        Assert.Contains("WaitForActivityDeadlineAndAvatarChangeGraceAsync", body, StringComparison.Ordinal);
        Assert.Contains(expectedDeadline, body, StringComparison.Ordinal);
        Assert.Contains(expectedCancellationToken, body, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedGraceCompletionWait_RechecksAuthoritativeDeadlineAfterGrace()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var body = NormalizeWhitespace(GetMethodBody(
            source,
            "WaitForActivityDeadlineAndAvatarChangeGraceAsync"));
        var graceDelayIndex = body.IndexOf(
            "await Task.Delay(graceRemaining, cancellationToken);",
            StringComparison.Ordinal);
        var deadlineCheckIndex = body.IndexOf(
            "activityDeadline.Deadline > DateTimeOffset.UtcNow",
            graceDelayIndex,
            StringComparison.Ordinal);
        var rewaitIndex = body.IndexOf(
            "await activityDeadline.WaitAsync(cancellationToken);",
            deadlineCheckIndex,
            StringComparison.Ordinal);
        var retryIndex = body.IndexOf("continue;", deadlineCheckIndex, StringComparison.Ordinal);

        Assert.Contains("await activityDeadline.WaitAsync(cancellationToken);", body, StringComparison.Ordinal);
        Assert.True(graceDelayIndex >= 0, "Shared grace waiting must await the avatar-change grace period.");
        Assert.True(
            deadlineCheckIndex > graceDelayIndex,
            "Shared grace waiting must inspect the authoritative deadline after grace waiting.");
        Assert.True(
            rewaitIndex > deadlineCheckIndex,
            "A future authoritative deadline must be awaited again before completion.");
        Assert.True(
            retryIndex > deadlineCheckIndex,
            "A future authoritative deadline must send the worker back to deadline waiting.");
    }

    [Fact]
    public void SupporterGrowthScaleSession_CleansStaleHandoffInsideFinally()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var body = NormalizeWhitespace(GetMethodBody(source, "RunSupporterGrowthScaleSessionAsync"));
        var guardIndex = body.IndexOf(
            "if (!avatarScaleSupporterGrowthStates.TryGetValue",
            StringComparison.Ordinal);
        var tryIndex = body.IndexOf("try", StringComparison.Ordinal);
        var finallyIndex = body.LastIndexOf("finally", StringComparison.Ordinal);
        var disposeIndex = body.IndexOf("sessionCancellation.Dispose();", finallyIndex, StringComparison.Ordinal);

        Assert.True(guardIndex >= 0, "Supporter growth handoff must validate its state identity.");
        Assert.True(tryIndex >= 0 && tryIndex < guardIndex,
            "Supporter growth state validation must remain inside cleanup ownership.");
        Assert.True(finallyIndex > guardIndex && disposeIndex > finallyIndex,
            "Stale supporter growth handoffs must dispose their session cancellation in finally.");
    }

    private static string GetMethodBody(string source, string methodSignatureStart)
    {
        var declaration = Regex.Match(
            source,
            $"(?m)^\\s*(?:private|public|protected|internal)\\b[^{{;]*\\b{Regex.Escape(methodSignatureStart)}\\s*\\(",
            RegexOptions.CultureInvariant);
        var methodStart = declaration.Success ? declaration.Index : -1;
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

    private static Task InvokeGraceAwareWaitAsync(
        ReschedulableActivityDeadline activityDeadline,
        Func<TimeSpan> getGraceRemaining,
        Func<bool> isInGracePeriod,
        CancellationToken cancellationToken)
    {
        var method = typeof(BridgeCoordinator).GetMethod(
            "WaitForActivityDeadlineAndAvatarChangeGraceAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var invocation = method!.Invoke(
            null,
            new object?[]
            {
                activityDeadline,
                getGraceRemaining,
                isInGracePeriod,
                cancellationToken
            });
        return Assert.IsAssignableFrom<Task>(invocation);
    }

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
