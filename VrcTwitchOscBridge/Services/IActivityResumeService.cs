using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public interface IActivityResumeService
{
    Task LoadPendingAsync();
    bool HasPendingResume { get; }
    bool IsPendingForAvatar(string avatarId);
    IReadOnlyList<ResumeActivity> GetPendingActivities();
    Task RemoveExpiredActivitiesAsync();
    Task RecordActivityStartedAsync(ResumeActivity activity, string avatarId);
    Task RecordActivityEndedAsync(Guid ruleId, ResumeActivity? expectedActivity = null);
    Task RemoveActivityAsync(ResumeActivity activity);
    Task ClearAllAsync();
    Task CommitAsync();
    Task DeleteStaleFileIfPresentAsync();
}
