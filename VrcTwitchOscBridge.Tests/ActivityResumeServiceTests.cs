using System.IO;
using System.Reflection;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class ActivityResumeServiceTests
{
    [Fact]
    public async Task StaleCleanupAndNewerSameRuleCommitLeaveTheNewActivityOnDisk()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "CrystalRelayActivityResumeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        var snapshotPath = Path.Combine(testDirectory, "activity-resume.json");
        var ruleId = Guid.NewGuid();

        try
        {
            await using (var previousService = new ActivityResumeService(snapshotPath))
            {
                await previousService.RecordActivityStartedAsync(
                    new ResumeActivity
                    {
                        Type = ResumeActivityType.AvatarScale,
                        RuleId = ruleId,
                        CurrentValue = 1.25
                    },
                    "avtr_test");
            }

            await using var service = new ActivityResumeService(snapshotPath);
            var writerGate = (SemaphoreSlim)typeof(ActivityResumeService)
                .GetField("writerGate", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(service)!;
            await writerGate.WaitAsync();

            var cleanupTask = service.DeleteStaleFileIfPresentAsync();
            var newerActivity = new ResumeActivity
            {
                Type = ResumeActivityType.AvatarScale,
                RuleId = ruleId,
                CurrentValue = 2.5
            };
            var latestActivity = new ResumeActivity
            {
                Type = ResumeActivityType.AvatarScale,
                RuleId = ruleId,
                CurrentValue = 2.75
            };
            var startTask = service.RecordActivityStartedAsync(newerActivity, "avtr_test");
            var latestStartTask = service.RecordActivityStartedAsync(latestActivity, "avtr_test");
            writerGate.Release();

            await Task.WhenAll(cleanupTask, startTask, latestStartTask);

            await using var loadedService = new ActivityResumeService(snapshotPath);
            await loadedService.LoadPendingAsync();
            var persisted = Assert.Single(loadedService.GetPendingActivities());
            Assert.Equal(ruleId, persisted.RuleId);
            Assert.Equal(2.75, persisted.CurrentValue);
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
    public async Task OlderSameRuleCompletionDoesNotRemoveNewerReplacement()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "CrystalRelayActivityResumeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        var snapshotPath = Path.Combine(testDirectory, "activity-resume.json");
        var ruleId = Guid.NewGuid();
        var oldActivity = new ResumeActivity
        {
            Type = ResumeActivityType.AvatarScale,
            RuleId = ruleId,
            CurrentValue = 1.25
        };
        var newerActivity = new ResumeActivity
        {
            Type = ResumeActivityType.AvatarScale,
            RuleId = ruleId,
            CurrentValue = 2.5
        };

        try
        {
            await using (var service = new ActivityResumeService(snapshotPath))
            {
                await service.RecordActivityStartedAsync(oldActivity, "avtr_test");
                await service.RecordActivityStartedAsync(newerActivity, "avtr_test");

                var completionMethod = typeof(ActivityResumeService).GetMethod(
                    nameof(ActivityResumeService.RecordActivityEndedAsync),
                    BindingFlags.Instance | BindingFlags.Public,
                    binder: null,
                    [typeof(Guid), typeof(ResumeActivity)],
                    modifiers: null);
                Assert.NotNull(completionMethod);

                var completionTask = Assert.IsAssignableFrom<Task>(
                    completionMethod!.Invoke(service, [ruleId, oldActivity]));
                await completionTask;
            }

            await using var reloadedService = new ActivityResumeService(snapshotPath);
            await reloadedService.LoadPendingAsync();
            var pending = Assert.Single(reloadedService.GetPendingActivities());
            Assert.Equal(ruleId, pending.RuleId);
            Assert.Equal(2.5, pending.CurrentValue);
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
    public async Task DisposeAsyncIsIdempotentAndRejectsLaterWritesSafely()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "CrystalRelayActivityResumeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        var snapshotPath = Path.Combine(testDirectory, "activity-resume.json");
        var service = new ActivityResumeService(snapshotPath);

        try
        {
            await service.DisposeAsync();
            await service.DisposeAsync();
            await service.RecordActivityStartedAsync(
                new ResumeActivity { Type = ResumeActivityType.Movement, RuleId = Guid.NewGuid() },
                "avtr_test");
        }
        finally
        {
            await service.DisposeAsync();
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }
}
