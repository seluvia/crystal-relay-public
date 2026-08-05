using System.IO;
using System.Text.Json;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public sealed class ActivityResumeService : IActivityResumeService, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private static string SnapshotPath => Path.Combine(AppDataPaths.SecureFolder, "activity-resume.json");

    private readonly object stateGate = new();
    private readonly string snapshotPath;
    private readonly SemaphoreSlim writerGate = new(1, 1);
    private readonly object writerLifecycleGate = new();
    private TaskCompletionSource writerUsersDrained = CreateCompletedTaskSource();
    private Task? disposeTask;
    private int writerUserCount;
    private bool writerDisposalStarted;
    private ActivityResumeSnapshot? pendingSnapshot;

    public ActivityResumeService()
        : this(null)
    {
    }

    internal ActivityResumeService(string? snapshotPath)
    {
        this.snapshotPath = string.IsNullOrWhiteSpace(snapshotPath)
            ? SnapshotPath
            : snapshotPath;
    }

    public async Task LoadPendingAsync()
    {
        await RunWriterAsync(async () =>
        {
            lock (stateGate)
            {
                pendingSnapshot = null;
            }

            if (!File.Exists(snapshotPath))
            {
                return;
            }

            try
            {
                var json = await File.ReadAllTextAsync(snapshotPath);
                var snapshot = JsonSerializer.Deserialize<ActivityResumeSnapshot>(json, SerializerOptions);
                if (snapshot is not null && snapshot.Version == 1)
                {
                    lock (stateGate)
                    {
                        pendingSnapshot = snapshot;
                    }
                }
                else
                {
                    File.Delete(snapshotPath);
                }
            }
            catch (Exception)
            {
                try
                {
                    File.Delete(snapshotPath);
                }
                catch
                {
                }
            }
        });
    }

    public bool HasPendingResume
    {
        get
        {
            lock (stateGate)
            {
                return pendingSnapshot?.Activities.Count > 0;
            }
        }
    }

    public bool IsPendingForAvatar(string avatarId)
    {
        var normalized = avatarId?.Trim() ?? string.Empty;
        lock (stateGate)
        {
            if (pendingSnapshot is null)
            {
                return false;
            }

            return string.Equals(pendingSnapshot.CurrentAvatarId, normalized, StringComparison.Ordinal);
        }
    }

    public IReadOnlyList<ResumeActivity> GetPendingActivities()
    {
        lock (stateGate)
        {
            return pendingSnapshot?.Activities.ToList() ?? (IReadOnlyList<ResumeActivity>)Array.Empty<ResumeActivity>();
        }
    }

    public async Task RemoveExpiredActivitiesAsync()
    {
        await RunWriterAsync(async () =>
        {
            var removedAny = false;
            lock (stateGate)
            {
                if (pendingSnapshot is not null)
                {
                    removedAny = pendingSnapshot.Activities.RemoveAll(
                        activity => activity.ExpiresAt is { } expiresAt
                            && expiresAt <= DateTimeOffset.UtcNow) > 0;
                }
            }

            if (removedAny)
            {
                await CommitLockedAsync();
            }
        });
    }

    public async Task RecordActivityStartedAsync(ResumeActivity activity, string avatarId)
    {
        await RunWriterAsync(async () =>
        {
            lock (stateGate)
            {
                pendingSnapshot ??= new ActivityResumeSnapshot();
                pendingSnapshot.CurrentAvatarId = avatarId?.Trim() ?? string.Empty;
                pendingSnapshot.Activities.RemoveAll(a => a.RuleId == activity.RuleId);
                pendingSnapshot.Activities.Add(activity);
            }

            await CommitLockedAsync();
        });
    }

    public async Task RecordActivityEndedAsync(Guid ruleId, ResumeActivity? expectedActivity = null)
    {
        await RunWriterAsync(async () =>
        {
            lock (stateGate)
            {
                if (pendingSnapshot is null)
                {
                    return;
                }

                if (expectedActivity is null)
                {
                    pendingSnapshot.Activities.RemoveAll(a => a.RuleId == ruleId);
                }
                else
                {
                    var activityIndex = pendingSnapshot.Activities.FindIndex(
                        existing => ReferenceEquals(existing, expectedActivity));
                    if (activityIndex >= 0)
                    {
                        pendingSnapshot.Activities.RemoveAt(activityIndex);
                    }
                }
            }

            await CommitLockedAsync();
        });
    }

    public async Task RemoveActivityAsync(ResumeActivity activity)
    {
        await RunWriterAsync(async () =>
        {
            lock (stateGate)
            {
                if (pendingSnapshot is null)
                {
                    return;
                }

                var activityIndex = pendingSnapshot.Activities.FindIndex(
                    existing => ReferenceEquals(existing, activity));
                if (activityIndex < 0)
                {
                    return;
                }

                pendingSnapshot.Activities.RemoveAt(activityIndex);
            }

            await CommitLockedAsync();
        });
    }

    public async Task ClearAllAsync()
    {
        await RunWriterAsync(() =>
        {
            lock (stateGate)
            {
                pendingSnapshot = null;
            }

            TryDeleteSnapshotFile();
            return Task.CompletedTask;
        });
    }

    public async Task CommitAsync()
    {
        await RunWriterAsync(CommitLockedAsync);
    }

    public async Task DeleteStaleFileIfPresentAsync()
    {
        await RunWriterAsync(() =>
        {
            lock (stateGate)
            {
                // An activity that was admitted before this cleanup owns the file now.
                if (pendingSnapshot is not null)
                {
                    return Task.CompletedTask;
                }
            }

            if (File.Exists(snapshotPath))
            {
                try
                {
                    File.Delete(snapshotPath);
                    DebugLogService.Write("Deleted stale activity resume file from previous clean shutdown.");
                }
                catch
                {
                }
            }

            return Task.CompletedTask;
        });
    }

    public ValueTask DisposeAsync()
    {
        lock (writerLifecycleGate)
        {
            if (disposeTask is null)
            {
                writerDisposalStarted = true;
                disposeTask = DisposeWriterAfterDrainAsync(writerUsersDrained.Task);
            }

            return new ValueTask(disposeTask);
        }
    }

    private async Task DisposeWriterAfterDrainAsync(Task drainTask)
    {
        await drainTask.ConfigureAwait(false);
        writerGate.Dispose();
    }

    private async Task RunWriterAsync(Func<Task> action)
    {
        if (!TryEnterWriterUser())
        {
            return;
        }

        var gateAcquired = false;
        try
        {
            await writerGate.WaitAsync().ConfigureAwait(false);
            gateAcquired = true;
            await action().ConfigureAwait(false);
        }
        finally
        {
            if (gateAcquired)
            {
                writerGate.Release();
            }

            ExitWriterUser();
        }
    }

    private bool TryEnterWriterUser()
    {
        lock (writerLifecycleGate)
        {
            if (writerDisposalStarted)
            {
                return false;
            }

            if (writerUserCount++ == 0)
            {
                writerUsersDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            return true;
        }
    }

    private void ExitWriterUser()
    {
        lock (writerLifecycleGate)
        {
            if (--writerUserCount == 0)
            {
                writerUsersDrained.TrySetResult();
            }
        }
    }

    private async Task CommitLockedAsync()
    {
        ActivityResumeSnapshot? snapshot;
        lock (stateGate)
        {
            snapshot = pendingSnapshot is null ? null : new ActivityResumeSnapshot
            {
                Version = pendingSnapshot.Version,
                SavedAt = DateTimeOffset.UtcNow,
                CurrentAvatarId = pendingSnapshot.CurrentAvatarId,
                Activities = pendingSnapshot.Activities.ToList()
            };
        }

        try
        {
            if (snapshot is null || snapshot.Activities.Count == 0)
            {
                TryDeleteSnapshotFile();
                return;
            }

            var tempPath = snapshotPath + ".tmp";
            var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, snapshotPath, overwrite: true);
        }
        catch
        {
        }
    }

    private void TryDeleteSnapshotFile()
    {
        try
        {
            if (File.Exists(snapshotPath))
            {
                File.Delete(snapshotPath);
            }
        }
        catch
        {
        }
    }

    private static TaskCompletionSource CreateCompletedTaskSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.TrySetResult();
        return source;
    }
}
