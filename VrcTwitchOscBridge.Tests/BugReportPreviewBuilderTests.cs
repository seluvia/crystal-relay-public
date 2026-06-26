using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class BugReportPreviewBuilderTests
{
    [Fact]
    public void Build_IncludesAllUserFields()
    {
        var result = BugReportPreviewBuilder.Build(
            title: "Test bug",
            category: "connection",
            severity: "normal",
            whatHappened: "Something broke",
            expectedBehavior: "It should work",
            stepsToReproduce: "Click the button",
            contactName: "tester",
            appVersion: "3.1.9",
            snapshot: "Crystal Relay Status Snapshot\nVRChat: Connected",
            activityLogSection: null,
            debugLogSection: null,
            crashLogSection: null);

        Assert.Contains("## Bug Report", result);
        Assert.Contains("**Category:** connection", result);
        Assert.Contains("**Severity:** normal", result);
        Assert.Contains("**App version:** 3.1.9", result);
        Assert.Contains("**Contact:** tester", result);
        Assert.Contains("## What happened", result);
        Assert.Contains("Something broke", result);
        Assert.Contains("## Expected behavior", result);
        Assert.Contains("It should work", result);
        Assert.Contains("## Steps to reproduce", result);
        Assert.Contains("Click the button", result);
        Assert.Contains("## Live status snapshot", result);
        Assert.Contains("Crystal Relay Status Snapshot", result);
    }

    [Fact]
    public void Build_NullSections_ShowNotIncluded()
    {
        var result = BugReportPreviewBuilder.Build(
            title: "Test bug",
            category: "other",
            severity: "low",
            whatHappened: "Something broke",
            expectedBehavior: "It should work",
            stepsToReproduce: "Click the button",
            contactName: string.Empty,
            appVersion: "3.1.9",
            snapshot: "snapshot text",
            activityLogSection: null,
            debugLogSection: null,
            crashLogSection: null);

        Assert.Contains("## Activity log", result);
        Assert.Contains("Not included.", result);
        Assert.Contains("## Debug logs", result);
        Assert.Contains("## Crash log", result);
    }

    [Fact]
    public void Build_PopulatedSections_ShowContent()
    {
        var result = BugReportPreviewBuilder.Build(
            title: "Test bug",
            category: "crash",
            severity: "crash",
            whatHappened: "App crashed",
            expectedBehavior: "No crash",
            stepsToReproduce: "Do the thing",
            contactName: string.Empty,
            appVersion: "3.1.9",
            snapshot: "snapshot",
            activityLogSection: "line1\nline2",
            debugLogSection: "debug1",
            crashLogSection: "crash details here");

        Assert.Contains("line1", result);
        Assert.Contains("line2", result);
        Assert.Contains("debug1", result);
        Assert.Contains("crash details here", result);
    }

    [Fact]
    public void Build_EmptyContact_ShowsNotProvided()
    {
        var result = BugReportPreviewBuilder.Build(
            title: "Test bug",
            category: "other",
            severity: "normal",
            whatHappened: "Something broke",
            expectedBehavior: "It should work",
            stepsToReproduce: "Click the button",
            contactName: string.Empty,
            appVersion: "3.1.9",
            snapshot: "snapshot",
            activityLogSection: null,
            debugLogSection: null,
            crashLogSection: null);

        Assert.Contains("**Contact:** Not provided", result);
    }

    [Fact]
    public void Build_SanitizesUserEnteredFields()
    {
        var result = BugReportPreviewBuilder.Build(
            title: "Token leak",
            category: "other",
            severity: "normal",
            whatHappened: "access_token=super-secret and C:\\Users\\secretuser\\crystal-relay\\settings.json",
            expectedBehavior: "Bearer abc123secret should not be shown",
            stepsToReproduce: "Open the app, use ABCD-EFGH when it asks for a code, use the feature.",
            contactName: "C:\\Users\\secretuser\\contact.txt",
            appVersion: "3.1.9",
            snapshot: "snapshot",
            activityLogSection: null,
            debugLogSection: null,
            crashLogSection: null);

        Assert.DoesNotContain("super-secret", result);
        Assert.DoesNotContain("abc123secret", result);
        Assert.DoesNotContain("secretuser", result);
        Assert.DoesNotContain("ABCD-EFGH", result);
        Assert.Contains("[redacted]", result);
        Assert.Contains("<user>", result);
    }
}
