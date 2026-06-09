using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public sealed class ActivityResumeService : IActivityResumeService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private static string SnapshotPath => Path.Combine(AppDataPaths.SecureFolder, "activity-resume.json");

    private readonly object stateGate = new();
    private ActivityResumeSnapshot? pendingSnapshot;

    public async Task LoadPendingAsync()
    {
        lock (stateGate)
        {
            pendingSnapshot = null;
        }

        if (!File.Exists(SnapshotPath))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(SnapshotPath);
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
                File.Delete(SnapshotPath);
            }
        }
        catch (Exception)
        {
            try
            {
                File.Delete(SnapshotPath);
            }
            catch
            {
            }
        }
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

    public Task RecordActivityStartedAsync(ResumeActivity activity, string avatarId)
    {
        lock (stateGate)
        {
            pendingSnapshot ??= new ActivityResumeSnapshot();
            pendingSnapshot.CurrentAvatarId = avatarId?.Trim() ?? string.Empty;
            pendingSnapshot.Activities.RemoveAll(a => a.RuleId == activity.RuleId);
            pendingSnapshot.Activities.Add(activity);
        }

        return CommitAsync();
    }

    public Task RecordActivityEndedAsync(Guid ruleId)
    {
        lock (stateGate)
        {
            if (pendingSnapshot is null)
            {
                return Task.CompletedTask;
            }

            pendingSnapshot.Activities.RemoveAll(a => a.RuleId == ruleId);
        }

        return CommitAsync();
    }

    public Task ClearAllAsync()
    {
        lock (stateGate)
        {
            pendingSnapshot = null;
        }

        try
        {
            if (File.Exists(SnapshotPath))
            {
                File.Delete(SnapshotPath);
            }
        }
        catch
        {
        }

        return Task.CompletedTask;
    }

    public async Task CommitAsync()
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
                if (File.Exists(SnapshotPath))
                {
                    File.Delete(SnapshotPath);
                }
                return;
            }

            var tempPath = SnapshotPath + ".tmp";
            var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, SnapshotPath, overwrite: true);
        }
        catch
        {
        }
    }

    public Task DeleteStaleFileIfPresentAsync()
    {
        try
        {
            if (File.Exists(SnapshotPath))
            {
                File.Delete(SnapshotPath);
                DebugLogService.Write("Deleted stale activity resume file from previous clean shutdown.");
            }
        }
        catch
        {
        }

        return Task.CompletedTask;
    }
}