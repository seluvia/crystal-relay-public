# Bug Report System Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enrich Crystal Relay's in-app bug report system with a live status snapshot, granular log toggles, a privacy preview window, category + severity fields, a crash-triggered prompt on next launch, and raised payload caps.

**Architecture:** Incremental enrichment of the existing pipeline (WPF dialog -> BugReportService -> Cloudflare worker -> GitHub issue). New `BugReportSnapshotService` builds a lean status snapshot from ViewModel public properties. New `BugReportPreviewBuilder` + `BugReportPreviewWindow` show the exact sanitized text before sending. The worker maps category to GitHub labels and renders per-section diagnostics. A crash-prompt flow fires on startup when `latest-crash.txt` is newer than a seen-marker.

**Tech Stack:** C# / .NET 10 / WPF + XAML, Cloudflare Workers (JavaScript), xUnit for tests, JSON localization files.

**Spec:** `docs/superpowers/specs/2026-06-25-bug-report-redesign-design.md`

---

## File structure

### New files
- `VrcTwitchOscBridge/Services/BugReportSnapshotService.cs` — builds the lean live-status snapshot string from a data record.
- `VrcTwitchOscBridge/Services/BugReportPreviewBuilder.cs` — mirrors the worker body template; renders preview text client-side.
- `VrcTwitchOscBridge/BugReportPreviewWindow.xaml` — themed read-only preview modal.
- `VrcTwitchOscBridge/BugReportPreviewWindow.xaml.cs` — preview window code-behind.
- `VrcTwitchOscBridge.Tests/BugReportSnapshotServiceTests.cs` — unit tests for snapshot building.
- `VrcTwitchOscBridge.Tests/BugReportPreviewBuilderTests.cs` — unit tests for preview rendering.
- `VrcTwitchOscBridge.Tests/BugReportServiceDiagnosticSectionTests.cs` — unit tests for the per-section diagnostic builders.

### Modified files
- `VrcTwitchOscBridge/Services/BugReportService.cs` — raise caps; split `BuildSanitizedDiagnostics` into 3 section builders; extend payload/submission records.
- `VrcTwitchOscBridge/BugReportWindow.xaml` — add category/severity combos, diagnostics panel with 3 toggles, preview button; bump window size.
- `VrcTwitchOscBridge/BugReportWindow.xaml.cs` — new constructor params, new properties, preview handler, crash-log visibility.
- `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs` — extend `OpenBugReportAsync`; add crash-prompt + seen-marker logic; build snapshot.
- `VrcTwitchOscBridge/App.xaml.cs` — add crash-check call after `MainWindow.Show()`.
- `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` — add entries for new files.
- `cloudflare/bug-report-worker/src/index.js` — raise caps; add category->label map; rewrite `buildGitHubIssue` body; extend `validatePayload`.
- `cloudflare/bug-report-worker/README.md` — note new category/severity fields and label requirements.
- `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json` — new source keys.
- All non-English `.extra.json` localization files (13 files: de-DE, es-ES, fr-FR, it-IT, ja-JP, ko-KR, pl-PL, pt-BR, ru-RU, sv-SE, th-TH, zh-CN, zh-TW) — translations.

---

## Task 1: BugReportSnapshotService + tests

**Files:**
- Create: `VrcTwitchOscBridge/Services/BugReportSnapshotService.cs`
- Create: `VrcTwitchOscBridge.Tests/BugReportSnapshotServiceTests.cs`
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` (add compile entries)

- [ ] **Step 1: Write the failing test**

Create `VrcTwitchOscBridge.Tests/BugReportSnapshotServiceTests.cs`:

```csharp
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class BugReportSnapshotServiceTests
{
    [Fact]
    public void Build_IncludesAllExpectedLines()
    {
        var data = new BugReportSnapshotData(
            IsBroadcasterConnected: true,
            IsBotConnected: false,
            IsVrChatConnected: true,
            OscStatusDetail: "VRChat is connected through OSCQuery.",
            CurrentAvatarName: "Ryo Adoption",
            CurrentAvatarId: "avtr_abc123def456",
            CurrentAvatarHeightMeters: 1.62,
            SelectedTheme: AppTheme.VoidCrystal,
            ThemeDisplayName: "Void Crystal",
            AppVersion: "3.1.9");

        var result = BugReportSnapshotService.Build(data);

        Assert.Contains("Crystal Relay Status Snapshot", result);
        Assert.Contains("Twitch broadcaster: Connected", result);
        Assert.Contains("Twitch bot: Disconnected", result);
        Assert.Contains("VRChat: Connected", result);
        Assert.Contains("OSC: VRChat is connected through OSCQuery.", result);
        Assert.Contains("Current avatar: Ryo Adoption", result);
        Assert.Contains("Eye height: 1.62 m", result);
        Assert.Contains("Theme: Void Crystal", result);
        Assert.Contains("App version: 3.1.9", result);
    }

    [Fact]
    public void Build_TruncatesLongAvatarId()
    {
        var longId = "avtr_" + new string('x', 30);
        var data = new BugReportSnapshotData(
            IsBroadcasterConnected: false,
            IsBotConnected: false,
            IsVrChatConnected: false,
            OscStatusDetail: string.Empty,
            CurrentAvatarName: "Test",
            CurrentAvatarId: longId,
            CurrentAvatarHeightMeters: 1.0,
            SelectedTheme: AppTheme.VoidCrystal,
            ThemeDisplayName: "Void Crystal",
            AppVersion: "3.1.9");

        var result = BugReportSnapshotService.Build(data);

        Assert.Contains("...", result);
        Assert.DoesNotContain(longId, result);
    }

    [Fact]
    public void Build_BlankAvatarName_ShowsUnknown()
    {
        var data = new BugReportSnapshotData(
            IsBroadcasterConnected: false,
            IsBotConnected: false,
            IsVrChatConnected: false,
            OscStatusDetail: string.Empty,
            CurrentAvatarName: string.Empty,
            CurrentAvatarId: string.Empty,
            CurrentAvatarHeightMeters: 0,
            SelectedTheme: AppTheme.VoidCrystal,
            ThemeDisplayName: "Void Crystal",
            AppVersion: "3.1.9");

        var result = BugReportSnapshotService.Build(data);

        Assert.Contains("Current avatar: Unknown", result);
    }

    [Fact]
    public void Build_SanitizesAvatarNameContainingPath()
    {
        var data = new BugReportSnapshotData(
            IsBroadcasterConnected: false,
            IsBotConnected: false,
            IsVrChatConnected: false,
            OscStatusDetail: string.Empty,
            CurrentAvatarName: "C:\\Users\\secret\\avatar",
            CurrentAvatarId: "avtr_123",
            CurrentAvatarHeightMeters: 1.0,
            SelectedTheme: AppTheme.VoidCrystal,
            ThemeDisplayName: "Void Crystal",
            AppVersion: "3.1.9");

        var result = BugReportSnapshotService.Build(data);

        Assert.DoesNotContain("secret", result);
        Assert.Contains("<user>", result);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "<repo>\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~BugReportSnapshotServiceTests"`
Expected: FAIL — `BugReportSnapshotService` not found / `BugReportSnapshotData` not found.

- [ ] **Step 3: Write minimal implementation**

Create `VrcTwitchOscBridge/Services/BugReportSnapshotService.cs`:

```csharp
using System.Globalization;
using System.Text;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

internal sealed record BugReportSnapshotData(
    bool IsBroadcasterConnected,
    bool IsBotConnected,
    bool IsVrChatConnected,
    string OscStatusDetail,
    string CurrentAvatarName,
    string CurrentAvatarId,
    double CurrentAvatarHeightMeters,
    AppTheme SelectedTheme,
    string ThemeDisplayName,
    string AppVersion);

internal static class BugReportSnapshotService
{
    private const int MaxAvatarIdDisplayLength = 20;

    public static string Build(BugReportSnapshotData data)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Crystal Relay Status Snapshot");
        builder.AppendLine($"Twitch broadcaster: {FormatBool(data.IsBroadcasterConnected)}");
        builder.AppendLine($"Twitch bot: {FormatBool(data.IsBotConnected)}");
        builder.AppendLine($"VRChat: {FormatBool(data.IsVrChatConnected)}");
        builder.AppendLine($"OSC: {string.IsNullOrWhiteSpace(data.OscStatusDetail) ? "Not available" : data.OscStatusDetail}");

        var avatarName = string.IsNullOrWhiteSpace(data.CurrentAvatarName) ? "Unknown" : data.CurrentAvatarName;
        var avatarId = TruncateAvatarId(data.CurrentAvatarId);
        builder.AppendLine($"Current avatar: {avatarName} ({avatarId})");

        builder.AppendLine($"Eye height: {data.CurrentAvatarHeightMeters.ToString("0.##", CultureInfo.InvariantCulture)} m");
        builder.AppendLine($"Theme: {data.ThemeDisplayName}");
        builder.AppendLine($"App version: {data.AppVersion}");

        return SensitiveTextSanitizer.Sanitize(builder.ToString().TrimEnd());
    }

    private static string FormatBool(bool value) => value ? "Connected" : "Disconnected";

    private static string TruncateAvatarId(string avatarId)
    {
        if (string.IsNullOrWhiteSpace(avatarId))
        {
            return "Unknown";
        }

        return avatarId.Length <= MaxAvatarIdDisplayLength
            ? avatarId
            : avatarId[..MaxAvatarIdDisplayLength] + "...";
    }
}
```

- [ ] **Step 4: Add compile entries to csproj**

In `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`, add after line 209 (`<Compile Include="Services\BugReportService.cs" />`):

```xml
    <Compile Include="Services\BugReportSnapshotService.cs" />
```

And add after the existing test compile entries in `VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj` (inside `<ItemGroup>` with other compile items, or just rely on default globbing if the test project uses default items — it does NOT have `EnableDefaultCompileItems=false`, so SDK-style default globbing picks up `.cs` files automatically. No csproj edit needed for the test file).

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test "<repo>\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~BugReportSnapshotServiceTests"`
Expected: PASS — 4 tests.

- [ ] **Step 6: Build the app project to verify no compile errors**

Run: `dotnet build "<repo>\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```powershell
cd "<repo>"
git add VrcTwitchOscBridge/Services/BugReportSnapshotService.cs VrcTwitchOscBridge.Tests/BugReportSnapshotServiceTests.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "Add BugReportSnapshotService with lean live-status snapshot"
```

---

## Task 2: BugReportPreviewBuilder + tests

**Files:**
- Create: `VrcTwitchOscBridge/Services/BugReportPreviewBuilder.cs`
- Create: `VrcTwitchOscBridge.Tests/BugReportPreviewBuilderTests.cs`
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` (add compile entry)

- [ ] **Step 1: Write the failing test**

Create `VrcTwitchOscBridge.Tests/BugReportPreviewBuilderTests.cs`:

```csharp
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
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "<repo>\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~BugReportPreviewBuilderTests"`
Expected: FAIL — `BugReportPreviewBuilder` not found.

- [ ] **Step 3: Write minimal implementation**

Create `VrcTwitchOscBridge/Services/BugReportPreviewBuilder.cs`:

```csharp
using System.Text;

namespace VrcTwitchOscBridge.Services;

internal static class BugReportPreviewBuilder
{
    public static string Build(
        string title,
        string category,
        string severity,
        string whatHappened,
        string expectedBehavior,
        string stepsToReproduce,
        string contactName,
        string appVersion,
        string snapshot,
        string? activityLogSection,
        string? debugLogSection,
        string? crashLogSection)
    {
        var contact = string.IsNullOrWhiteSpace(contactName) ? "Not provided" : contactName;
        var appVer = string.IsNullOrWhiteSpace(appVersion) ? "Unknown" : appVersion;

        var builder = new StringBuilder();
        builder.AppendLine("## Bug Report");
        builder.AppendLine();
        builder.AppendLine($"**Category:** {category}");
        builder.AppendLine($"**Severity:** {severity}");
        builder.AppendLine($"**App version:** {appVer}");
        builder.AppendLine($"**Contact:** {contact}");
        builder.AppendLine();
        builder.AppendLine("## What happened");
        builder.AppendLine();
        builder.AppendLine(whatHappened);
        builder.AppendLine();
        builder.AppendLine("## Expected behavior");
        builder.AppendLine();
        builder.AppendLine(expectedBehavior);
        builder.AppendLine();
        builder.AppendLine("## Steps to reproduce");
        builder.AppendLine();
        builder.AppendLine(stepsToReproduce);
        builder.AppendLine();
        builder.AppendLine("## Live status snapshot");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(snapshot);
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Activity log");
        builder.AppendLine();
        AppendSection(builder, activityLogSection);
        builder.AppendLine();
        builder.AppendLine("## Debug logs");
        builder.AppendLine();
        AppendSection(builder, debugLogSection);
        builder.AppendLine();
        builder.AppendLine("## Crash log");
        builder.AppendLine();
        AppendSection(builder, crashLogSection);

        return builder.ToString().TrimEnd();
    }

    private static void AppendSection(StringBuilder builder, string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            builder.AppendLine("Not included.");
            return;
        }

        builder.AppendLine("```text");
        builder.AppendLine(section);
        builder.AppendLine("```");
    }
}
```

- [ ] **Step 4: Add compile entry to csproj**

In `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`, add after the `BugReportSnapshotService` line added in Task 1:

```xml
    <Compile Include="Services\BugReportPreviewBuilder.cs" />
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test "<repo>\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~BugReportPreviewBuilderTests"`
Expected: PASS — 4 tests.

- [ ] **Step 6: Build the app project**

Run: `dotnet build "<repo>\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```powershell
cd "<repo>"
git add VrcTwitchOscBridge/Services/BugReportPreviewBuilder.cs VrcTwitchOscBridge.Tests/BugReportPreviewBuilderTests.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "Add BugReportPreviewBuilder for client-side preview rendering"
```

---

## Task 3: BugReportService — raise caps and split diagnostic builders

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BugReportService.cs`
- Create: `VrcTwitchOscBridge.Tests/BugReportServiceDiagnosticSectionTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `VrcTwitchOscBridge.Tests/BugReportServiceDiagnosticSectionTests.cs`:

```csharp
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class BugReportServiceDiagnosticSectionTests
{
    [Fact]
    public void BuildActivityLogSection_IncludesHeaderAndEntries()
    {
        var service = new BugReportService();
        var entries = new[] { "Started bridge", "Twitch connected", "VRChat connected" };

        var result = service.BuildActivityLogSection(entries);

        Assert.Contains("Recent Activity Log", result);
        Assert.Contains("Started bridge", result);
        Assert.Contains("Twitch connected", result);
        Assert.Contains("VRChat connected", result);
    }

    [Fact]
    public void BuildActivityLogSection_EmptyEntries_ReturnsEmpty()
    {
        var service = new BugReportService();

        var result = service.BuildActivityLogSection([]);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void BuildDebugLogSection_ReturnsHeaderOrEmpty()
    {
        var service = new BugReportService();

        var result = service.BuildDebugLogSection();

        // Debug logs may or may not exist on the test machine, but the method
        // should never throw and should return a string.
        Assert.True(result == string.Empty || result.Contains("Recent Debug Logs") || result.Contains("Could not read"));
    }

    [Fact]
    public void BuildCrashLogSection_WhenNoCrashLog_ReturnsEmpty()
    {
        var service = new BugReportService();

        var result = service.BuildCrashLogSection();

        // No crash log on a clean test machine — should return empty, not throw.
        Assert.True(result == string.Empty || result.Contains("Crash Log") || result.Contains("Could not read"));
    }

    [Fact]
    public void BuildActivityLogSection_SanitizesUserPaths()
    {
        var service = new BugReportService();
        var entries = new[] { "Loaded C:\\Users\\secretuser\\config.json" };

        var result = service.BuildActivityLogSection(entries);

        Assert.DoesNotContain("secretuser", result);
        Assert.Contains("<user>", result);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "<repo>\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~BugReportServiceDiagnosticSectionTests"`
Expected: FAIL — `BuildActivityLogSection` not found.

- [ ] **Step 3: Modify BugReportService.cs — raise caps**

In `VrcTwitchOscBridge/Services/BugReportService.cs`, change these constants (lines 19-22):

Replace:
```csharp
    private const int MaxDiagnosticLogLength = 11 * 1024;
    private const int MaxPayloadLength = 20 * 1024;
    private const int MaxActivityLogLines = 80;
    private const int MaxDebugLogLines = 160;
```

With:
```csharp
    private const int MaxDiagnosticLogLength = 40 * 1024;
    private const int MaxPayloadLength = 56 * 1024;
    private const int MaxActivityLogLines = 200;
    private const int MaxDebugLogLines = 400;
```

- [ ] **Step 4: Modify BugReportService.cs — replace BuildSanitizedDiagnostics with three section builders**

Replace the entire `BuildSanitizedDiagnostics` method (lines 123-172) with three new public methods:

```csharp
    public string BuildActivityLogSection(IEnumerable<string> activityLogEntries)
    {
        var recentEntries = activityLogEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Take(MaxActivityLogLines)
            .Reverse()
            .ToArray();
        if (recentEntries.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Recent Activity Log");
        builder.AppendLine(new string('-', 40));
        foreach (var entry in recentEntries)
        {
            builder.AppendLine(entry);
        }

        return TrimToUtf8Length(SensitiveTextSanitizer.Sanitize(builder.ToString()), 16 * 1024);
    }

    public string BuildDebugLogSection()
    {
        var recentDebugLogLines = DebugLogService.ReadRecentLines(MaxDebugLogLines);
        if (recentDebugLogLines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Recent Debug Logs");
        builder.AppendLine(new string('-', 40));
        foreach (var line in recentDebugLogLines)
        {
            builder.AppendLine(line);
        }

        return TrimToUtf8Length(SensitiveTextSanitizer.Sanitize(builder.ToString()), 16 * 1024);
    }

    public string BuildCrashLogSection()
    {
        var latestCrashLog = TryReadLatestCrashLog();
        if (string.IsNullOrWhiteSpace(latestCrashLog))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Latest Crash Log");
        builder.AppendLine(new string('-', 40));
        builder.AppendLine(latestCrashLog);

        return TrimToUtf8Length(SensitiveTextSanitizer.Sanitize(builder.ToString()), 12 * 1024);
    }
```

- [ ] **Step 5: Modify BugReportService.cs — extend BugReportSubmission record**

Replace the existing `BugReportSubmission` record (lines 330-337) with:

```csharp
internal sealed record BugReportSubmission(
    string Title,
    string WhatHappened,
    string ExpectedBehavior,
    string StepsToReproduce,
    string ContactName,
    string AppVersion,
    string Category,
    string Severity,
    string Snapshot,
    string? ActivityLog,
    string? DebugLog,
    string? CrashLog);
```

- [ ] **Step 6: Modify BugReportService.cs — extend BugReportPayload record**

Replace the existing `BugReportPayload` record (lines 307-315) with:

```csharp
    private sealed record BugReportPayload(
        string Title,
        string WhatHappened,
        string ExpectedBehavior,
        string StepsToReproduce,
        string ContactName,
        string AppVersion,
        string Category,
        string Severity,
        string Snapshot,
        string? ActivityLog,
        string? DebugLog,
        string? CrashLog,
        DateTimeOffset SubmittedAtUtc);
```

- [ ] **Step 7: Modify BugReportService.cs — update SubmitAsync to build the new payload**

Replace the `SubmitAsync` method's payload-building section (lines 51-77) with:

```csharp
        var snapshotTrimmed = TrimToUtf8Length(submission.Snapshot, 2 * 1024);
        var activityTrimmed = submission.ActivityLog is null
            ? null
            : TrimToUtf8Length(submission.ActivityLog, 16 * 1024);
        var debugTrimmed = submission.DebugLog is null
            ? null
            : TrimToUtf8Length(submission.DebugLog, 16 * 1024);
        var crashTrimmed = submission.CrashLog is null
            ? null
            : TrimToUtf8Length(submission.CrashLog, 12 * 1024);

        var payload = new BugReportPayload(
            TrimForTransport(submission.Title, 120),
            TrimForTransport(submission.WhatHappened, 5000),
            TrimForTransport(submission.ExpectedBehavior, 5000),
            TrimForTransport(submission.StepsToReproduce, 5000),
            TrimForTransport(submission.ContactName, 120),
            TrimForTransport(submission.AppVersion, 80),
            TrimForTransport(submission.Category, 40),
            TrimForTransport(submission.Severity, 40),
            snapshotTrimmed,
            activityTrimmed,
            debugTrimmed,
            crashTrimmed,
            DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaxPayloadLength)
        {
            payload = payload with { DebugLog = null };
            json = JsonSerializer.Serialize(payload, JsonOptions);
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxPayloadLength)
        {
            payload = payload with { ActivityLog = null };
            json = JsonSerializer.Serialize(payload, JsonOptions);
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxPayloadLength)
        {
            payload = payload with { CrashLog = null };
            json = JsonSerializer.Serialize(payload, JsonOptions);
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxPayloadLength)
        {
            payload = payload with { Snapshot = TrimToUtf8Length(payload.Snapshot, 1 * 1024) };
        }
```

The rest of `SubmitAsync` (the HTTP send, response handling, rate-limit logic) stays unchanged.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test "<repo>\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~BugReportServiceDiagnosticSectionTests"`
Expected: PASS — 5 tests.

- [ ] **Step 9: Build the app project**

Run: `dotnet build "<repo>\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded. If the old `BuildSanitizedDiagnostics` call site in `MainWindowViewModel.cs` breaks, that's expected — it will be fixed in Task 6. To get the build green now, temporarily comment out the body of `OpenBugReportAsync` lines 17908-17919 and replace with `var diagnostics = string.Empty;`. This is a temporary bridge; Task 6 replaces the whole method.

- [ ] **Step 10: Commit**

```powershell
cd "<repo>"
git add VrcTwitchOscBridge/Services/BugReportService.cs VrcTwitchOscBridge.Tests/BugReportServiceDiagnosticSectionTests.cs VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs
git commit -m "Raise bug report caps and split diagnostic builders into per-section methods"
```

---

## Task 4: BugReportPreviewWindow (XAML + code-behind)

**Files:**
- Create: `VrcTwitchOscBridge/BugReportPreviewWindow.xaml`
- Create: `VrcTwitchOscBridge/BugReportPreviewWindow.xaml.cs`
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` (add Page + Compile entries)

- [ ] **Step 1: Create the XAML file**

Create `VrcTwitchOscBridge/BugReportPreviewWindow.xaml`:

```xml
<Window x:Class="VrcTwitchOscBridge.BugReportPreviewWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:loc="clr-namespace:VrcTwitchOscBridge.Services"
        xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        Title="{loc:Translate 'Preview Bug Report | Crystal Relay'}"
        Icon="Assets/crystal-relay-icon.ico"
        Width="640"
        Height="560"
        MinWidth="540"
        MinHeight="440"
        WindowStyle="None"
        ResizeMode="CanResize"
        WindowStartupLocation="CenterOwner"
        FontFamily="{DynamicResource BodyFontFamily}"
        UseLayoutRounding="True"
        SnapsToDevicePixels="True"
        Background="{DynamicResource WindowBackgroundBrush}">
    <shell:WindowChrome.WindowChrome>
        <shell:WindowChrome CaptionHeight="0"
                            CornerRadius="0"
                            GlassFrameThickness="0"
                            ResizeBorderThickness="6"
                            UseAeroCaptionButtons="False" />
    </shell:WindowChrome.WindowChrome>

    <Window.Resources>
        <FontFamily x:Key="BodyFontFamily">Verdana</FontFamily>
        <FontFamily x:Key="HeadingFontFamily">Constantia</FontFamily>
        <SolidColorBrush x:Key="WindowBackgroundBrush" Color="#130B1E" />
        <SolidColorBrush x:Key="BorderBrush" Color="#4B2B78" />
        <SolidColorBrush x:Key="HighlightBorderBrush" Color="#B178FF" />
        <SolidColorBrush x:Key="AccentBrush" Color="#A855F7" />
        <SolidColorBrush x:Key="TextBrush" Color="#F5EEFF" />
        <SolidColorBrush x:Key="MutedBrush" Color="#C9B8E3" />
        <SolidColorBrush x:Key="InputBrush" Color="#B8271A3D" />
        <SolidColorBrush x:Key="InputBorderBrush" Color="#5B3A8E" />
        <SolidColorBrush x:Key="SecondaryButtonBrush" Color="#2C1C48" />
        <SolidColorBrush x:Key="SecondaryButtonBorderBrush" Color="#6942A7" />
        <SolidColorBrush x:Key="TitleBarBrush" Color="#20122F" />
        <SolidColorBrush x:Key="TitleBarTextBrush" Color="#F5EEFF" />
        <SolidColorBrush x:Key="TitleBarSubTextBrush" Color="#CBB9E5" />
        <SolidColorBrush x:Key="TitleBarButtonBrush" Color="#00000000" />
        <SolidColorBrush x:Key="TitleBarButtonHoverBrush" Color="#3B235B" />
        <SolidColorBrush x:Key="TitleBarButtonPressedBrush" Color="#543183" />
        <SolidColorBrush x:Key="TitleBarCloseHoverBrush" Color="#B43D62" />
        <SolidColorBrush x:Key="TitleBarClosePressedBrush" Color="#8C2648" />
        <SolidColorBrush x:Key="ScrollTrackBrush" Color="#25183D" />
        <SolidColorBrush x:Key="ScrollThumbBrush" Color="#7B57D0" />
        <SolidColorBrush x:Key="RuleCardHoverBrush" Color="#A978FF" />

        <Style x:Key="TitleBarButtonStyle" TargetType="Button">
            <Setter Property="Width" Value="40" />
            <Setter Property="Height" Value="32" />
            <Setter Property="Margin" Value="0" />
            <Setter Property="Padding" Value="0" />
            <Setter Property="Background" Value="{DynamicResource TitleBarButtonBrush}" />
            <Setter Property="Foreground" Value="{DynamicResource TitleBarTextBrush}" />
            <Setter Property="BorderBrush" Value="Transparent" />
            <Setter Property="BorderThickness" Value="0" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="TitleBarButtonBorder"
                                Background="{TemplateBinding Background}"
                                CornerRadius="10">
                            <ContentPresenter HorizontalAlignment="Center"
                                              VerticalAlignment="Center" />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="TitleBarButtonBorder" Property="Background" Value="{DynamicResource TitleBarButtonHoverBrush}" />
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter TargetName="TitleBarButtonBorder" Property="Background" Value="{DynamicResource TitleBarButtonPressedBrush}" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="TitleBarCloseButtonStyle" TargetType="Button" BasedOn="{StaticResource TitleBarButtonStyle}">
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="TitleBarCloseBorder"
                                Background="{TemplateBinding Background}"
                                CornerRadius="10">
                            <ContentPresenter HorizontalAlignment="Center"
                                              VerticalAlignment="Center" />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="TitleBarCloseBorder" Property="Background" Value="{DynamicResource TitleBarCloseHoverBrush}" />
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter TargetName="TitleBarCloseBorder" Property="Background" Value="{DynamicResource TitleBarClosePressedBrush}" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="SecondaryButtonStyle" TargetType="Button">
            <Setter Property="FontFamily" Value="{DynamicResource HeadingFontFamily}" />
            <Setter Property="FontWeight" Value="SemiBold" />
            <Setter Property="Background" Value="{DynamicResource SecondaryButtonBrush}" />
            <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource SecondaryButtonBorderBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="Padding" Value="15,10" />
            <Setter Property="MinHeight" Value="42" />
            <Setter Property="MinWidth" Value="118" />
            <Setter Property="HorizontalContentAlignment" Value="Center" />
            <Setter Property="VerticalContentAlignment" Value="Center" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="ButtonChrome"
                                Background="{TemplateBinding Background}"
                                BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}"
                                CornerRadius="14"
                                Padding="{TemplateBinding Padding}">
                            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                              VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                                              RecognizesAccessKey="True" />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="ButtonChrome" Property="Opacity" Value="0.92" />
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter TargetName="ButtonChrome" Property="Opacity" Value="0.82" />
                            </Trigger>
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter TargetName="ButtonChrome" Property="Opacity" Value="0.45" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="ScrollBarThumbStyle" TargetType="Thumb">
            <Setter Property="MinHeight" Value="32" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Thumb">
                        <Border x:Name="ThumbChrome"
                                Margin="2"
                                Background="{DynamicResource ScrollThumbBrush}"
                                CornerRadius="8" />
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="ThumbChrome" Property="Background" Value="{DynamicResource RuleCardHoverBrush}" />
                            </Trigger>
                            <Trigger Property="IsDragging" Value="True">
                                <Setter TargetName="ThumbChrome" Property="Background" Value="{DynamicResource AccentBrush}" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="ScrollBarTrackButtonStyle" TargetType="RepeatButton">
            <Setter Property="Focusable" Value="False" />
            <Setter Property="IsTabStop" Value="False" />
            <Setter Property="Background" Value="Transparent" />
            <Setter Property="BorderThickness" Value="0" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="RepeatButton">
                        <Border Background="Transparent" />
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <ControlTemplate x:Key="VerticalScrollBarTemplate" TargetType="ScrollBar">
            <Grid Width="{TemplateBinding Width}"
                  MinWidth="18"
                  Background="Transparent">
                <Border Background="{DynamicResource ScrollTrackBrush}"
                        BorderBrush="{DynamicResource InputBorderBrush}"
                        BorderThickness="1"
                        CornerRadius="8" />
                <Track x:Name="PART_Track"
                       Margin="2">
                    <Track.DecreaseRepeatButton>
                        <RepeatButton Style="{StaticResource ScrollBarTrackButtonStyle}"
                                      Command="{x:Static ScrollBar.PageUpCommand}" />
                    </Track.DecreaseRepeatButton>
                    <Track.Thumb>
                        <Thumb Style="{StaticResource ScrollBarThumbStyle}" />
                    </Track.Thumb>
                    <Track.IncreaseRepeatButton>
                        <RepeatButton Style="{StaticResource ScrollBarTrackButtonStyle}"
                                      Command="{x:Static ScrollBar.PageDownCommand}" />
                    </Track.IncreaseRepeatButton>
                </Track>
            </Grid>
        </ControlTemplate>

        <Style TargetType="ScrollBar">
            <Setter Property="Background" Value="{DynamicResource ScrollTrackBrush}" />
            <Setter Property="Width" Value="18" />
            <Setter Property="Height" Value="Auto" />
            <Setter Property="Template" Value="{StaticResource VerticalScrollBarTemplate}" />
        </Style>

        <Style TargetType="ScrollViewer">
            <Setter Property="Background" Value="Transparent" />
            <Setter Property="Padding" Value="0" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ScrollViewer">
                        <Grid Background="{TemplateBinding Background}">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <ScrollContentPresenter x:Name="PART_ScrollContentPresenter"
                                                    Grid.Column="0"
                                                    Margin="{TemplateBinding Padding}"
                                                    CanContentScroll="{TemplateBinding CanContentScroll}"
                                                    Content="{TemplateBinding Content}" />
                            <ScrollBar x:Name="PART_VerticalScrollBar"
                                       Grid.Column="1"
                                       Margin="10,0,0,0"
                                       VerticalAlignment="Stretch"
                                       Orientation="Vertical"
                                       Minimum="0"
                                       Maximum="{TemplateBinding ScrollableHeight}"
                                       ViewportSize="{TemplateBinding ViewportHeight}"
                                       Value="{Binding VerticalOffset, RelativeSource={RelativeSource TemplatedParent}, Mode=OneWay}"
                                       Visibility="{TemplateBinding ComputedVerticalScrollBarVisibility}" />
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>

    <Border Margin="1"
            Background="{DynamicResource WindowBackgroundBrush}"
            BorderBrush="{DynamicResource BorderBrush}"
            BorderThickness="1">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="48" />
                <RowDefinition Height="*" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <Border Grid.Row="0"
                    Background="{DynamicResource TitleBarBrush}"
                    BorderBrush="{DynamicResource BorderBrush}"
                    BorderThickness="0,0,0,1"
                    MouseLeftButtonDown="OnTitleBarMouseLeftButtonDown">
                <Grid Margin="12,0,8,0">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>

                    <DockPanel VerticalAlignment="Center">
                        <Image Source="Assets/crystal-relay-icon.png"
                               Width="18"
                               Height="18"
                               Margin="0,0,10,0"
                               Stretch="Uniform" />
                        <StackPanel Orientation="Horizontal"
                                    VerticalAlignment="Center">
                            <TextBlock Text="{loc:Translate 'Preview Bug Report'}"
                                       FontFamily="{DynamicResource HeadingFontFamily}"
                                       FontSize="16"
                                       FontWeight="Bold"
                                       Foreground="{DynamicResource TitleBarTextBrush}" />
                            <TextBlock Margin="8,0,0,0"
                                       Text="Crystal Relay"
                                       Foreground="{DynamicResource TitleBarSubTextBrush}"
                                       VerticalAlignment="Center" />
                        </StackPanel>
                    </DockPanel>

                    <Button Grid.Column="1"
                            Style="{StaticResource TitleBarCloseButtonStyle}"
                            Click="OnCloseButtonClicked">
                        <TextBlock Text="&#x2715;"
                                   FontSize="13"
                                   FontWeight="SemiBold"
                                   Foreground="{DynamicResource TitleBarTextBrush}" />
                    </Button>
                </Grid>
            </Border>

            <ScrollViewer Grid.Row="1"
                          VerticalScrollBarVisibility="Auto"
                          HorizontalScrollBarVisibility="Disabled"
                          Margin="16">
                <TextBox x:Name="PreviewTextBox"
                         FontFamily="Consolas"
                         FontSize="12"
                         IsReadOnly="True"
                         TextWrapping="Wrap"
                         Background="Transparent"
                         Foreground="{DynamicResource TextBrush}"
                         BorderThickness="0"
                         CaretBrush="{DynamicResource TextBrush}"
                         Padding="0" />
            </ScrollViewer>

            <StackPanel Grid.Row="2"
                        HorizontalAlignment="Center"
                        Margin="0,0,0,16">
                <Button Content="{loc:Translate 'Close'}"
                        Width="132"
                        Style="{StaticResource SecondaryButtonStyle}"
                        IsCancel="True"
                        IsDefault="True"
                        Click="OnCloseButtonClicked" />
            </StackPanel>
        </Grid>
    </Border>
</Window>
```

- [ ] **Step 2: Create the code-behind**

Create `VrcTwitchOscBridge/BugReportPreviewWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge;

public partial class BugReportPreviewWindow : Window
{
    public BugReportPreviewWindow(string previewText, AppTheme theme)
    {
        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, theme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;
        PreviewTextBox.Text = previewText;
        Loaded += (_, _) => PreviewTextBox.ScrollToHome();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        Closed -= OnWindowClosed;
    }

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
    }

    private void OnCloseButtonClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
```

- [ ] **Step 3: Add Page + Compile entries to csproj**

In `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`, add after line 37 (`<Page Include="BugReportWindow.xaml" />`):

```xml
    <Page Include="BugReportPreviewWindow.xaml" />
```

And add after line 86 (`<Compile Include="BugReportWindow.xaml.cs" />`):

```xml
    <Compile Include="BugReportPreviewWindow.xaml.cs">
      <DependentUpon>BugReportPreviewWindow.xaml</DependentUpon>
    </Compile>
```

- [ ] **Step 4: Build the app project**

Run: `dotnet build "<repo>\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```powershell
cd "<repo>"
git add VrcTwitchOscBridge/BugReportPreviewWindow.xaml VrcTwitchOscBridge/BugReportPreviewWindow.xaml.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "Add BugReportPreviewWindow themed read-only preview modal"
```

---

## Task 5: BugReportWindow — extend XAML with category, severity, diagnostics panel

**Files:**
- Modify: `VrcTwitchOscBridge/BugReportWindow.xaml`

- [ ] **Step 1: Update window dimensions**

In `VrcTwitchOscBridge/BugReportWindow.xaml`, change lines 8-11:

Replace:
```xml
        Width="720"
        Height="760"
        MinWidth="620"
        MinHeight="640"
```

With:
```xml
        Width="720"
        Height="860"
        MinWidth="620"
        MinHeight="680"
```

- [ ] **Step 2: Add category + severity dropdowns after the danger banner**

After the danger `Border` (which ends at line 520), and before the "Bug Title" `TextBlock` (line 522), insert:

```xml
                            <Grid Margin="0,16,0,0">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="18" />
                                    <ColumnDefinition Width="*" />
                                </Grid.ColumnDefinitions>

                                <StackPanel Grid.Column="0">
                                    <TextBlock Text="{loc:Translate 'Category'}"
                                               Foreground="{DynamicResource TextBrush}"
                                               FontWeight="SemiBold" />
                                    <ComboBox x:Name="CategoryComboBox"
                                              Margin="0,7,0,0"
                                              SelectedIndex="6">
                                        <ComboBoxItem Tag="connection" Content="{loc:Translate 'Connection'}" />
                                        <ComboBoxItem Tag="rewards" Content="{loc:Translate 'Rewards &amp; Avatar Sets'}" />
                                        <ComboBoxItem Tag="scaling" Content="{loc:Translate 'Avatar Scaling'}" />
                                        <ComboBoxItem Tag="movement" Content="{loc:Translate 'Movement'}" />
                                        <ComboBoxItem Tag="ui-theme" Content="{loc:Translate 'UI / Theme'}" />
                                        <ComboBoxItem Tag="crash" Content="{loc:Translate 'Crash'}" />
                                        <ComboBoxItem Tag="other" Content="{loc:Translate 'Other'}" />
                                    </ComboBox>
                                </StackPanel>

                                <StackPanel Grid.Column="2">
                                    <TextBlock Text="{loc:Translate 'Severity'}"
                                               Foreground="{DynamicResource TextBrush}"
                                               FontWeight="SemiBold" />
                                    <ComboBox x:Name="SeverityComboBox"
                                              Margin="0,7,0,0"
                                              SelectedIndex="1">
                                        <ComboBoxItem Tag="low" Content="{loc:Translate 'Low'}" />
                                        <ComboBoxItem Tag="normal" Content="{loc:Translate 'Normal'}" />
                                        <ComboBoxItem Tag="high" Content="{loc:Translate 'High'}" />
                                        <ComboBoxItem Tag="crash" Content="{loc:Translate 'Crash'}" />
                                    </ComboBox>
                                </StackPanel>
                            </Grid>
```

Note: `SelectedIndex="6"` selects "Other" by default (0-indexed). `SelectedIndex="1"` selects "Normal" by default.

- [ ] **Step 3: Replace the old "Include sanitized logs" checkbox block with the new diagnostics panel**

Find the `Grid` that contains the `IncludeLogsCheckBox` and its helper text (lines 553-585). Replace the entire `Grid` with:

```xml
                            <Border Margin="0,16,0,0"
                                    Padding="16"
                                    Background="{DynamicResource PanelSecondaryBrush}"
                                    BorderBrush="{DynamicResource InputBorderBrush}"
                                    BorderThickness="1"
                                    CornerRadius="14">
                                <StackPanel>
                                    <TextBlock Text="{loc:Translate 'Diagnostics'}"
                                               Foreground="{DynamicResource TextBrush}"
                                               FontWeight="SemiBold"
                                               FontSize="14" />

                                    <TextBlock Margin="0,8,0,0"
                                               Text="{loc:Translate 'Live status snapshot (always included)'}"
                                               Foreground="{DynamicResource MutedBrush}"
                                               FontSize="12" />

                                    <Border Margin="0,8,0,0"
                                            Padding="10,8"
                                            Background="{DynamicResource InputBrush}"
                                            BorderBrush="{DynamicResource InputBorderBrush}"
                                            BorderThickness="1"
                                            CornerRadius="10">
                                        <TextBox x:Name="SnapshotTextBox"
                                                 FontFamily="Consolas"
                                                 FontSize="11"
                                                 IsReadOnly="True"
                                                 TextWrapping="Wrap"
                                                 Background="Transparent"
                                                 Foreground="{DynamicResource TextBrush}"
                                                 BorderThickness="0"
                                                 CaretBrush="{DynamicResource TextBrush}"
                                                 Padding="0"
                                                 MaxHeight="120"
                                                 VerticalScrollBarVisibility="Auto" />
                                    </Border>

                                    <CheckBox x:Name="CrashLogCheckBox"
                                              Margin="0,12,0,0"
                                              Content="{loc:Translate 'Include crash log'}" />
                                    <TextBlock Margin="0,4,0,0"
                                               Text="{loc:Translate 'Crash logs are sanitized before sending.'}"
                                               Foreground="{DynamicResource MutedBrush}"
                                               FontSize="12"
                                               TextWrapping="Wrap" />

                                    <CheckBox x:Name="ActivityLogCheckBox"
                                              Margin="0,8,0,0"
                                              Content="{loc:Translate 'Include activity log'}" />
                                    <TextBlock Margin="0,4,0,0"
                                               Text="{loc:Translate 'Recent activity log entries, sanitized before sending.'}"
                                               Foreground="{DynamicResource MutedBrush}"
                                               FontSize="12"
                                               TextWrapping="Wrap" />

                                    <CheckBox x:Name="DebugLogCheckBox"
                                              Margin="0,8,0,0"
                                              Content="{loc:Translate 'Include debug logs'}" />
                                    <TextBlock Margin="0,4,0,0"
                                               Text="{loc:Translate 'Recent debug log lines, sanitized before sending.'}"
                                               Foreground="{DynamicResource MutedBrush}"
                                               FontSize="12"
                                               TextWrapping="Wrap" />

                                    <Button Margin="0,12,0,0"
                                            Content="{loc:Translate 'Preview sanitized report'}"
                                            Style="{StaticResource SecondaryButtonStyle}"
                                            HorizontalAlignment="Left"
                                            Click="OnPreviewClicked" />
                                </StackPanel>
                            </Border>
```

- [ ] **Step 4: Build the app project**

Run: `dotnet build "<repo>\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build may show warnings about missing event handler `OnPreviewClicked` — that's fixed in Task 6. If the build fails due to the missing handler, proceed to Task 6 Step 1 first, then build.

- [ ] **Step 5: Commit (after Task 6 makes it build)**

This commit is combined with Task 6.

---

## Task 6: BugReportWindow — extend code-behind

**Files:**
- Modify: `VrcTwitchOscBridge/BugReportWindow.xaml.cs`

- [ ] **Step 1: Replace the entire code-behind**

Replace the full contents of `VrcTwitchOscBridge/BugReportWindow.xaml.cs` with:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge;

public partial class BugReportWindow : Window
{
    private static readonly string[] CategoryKeys =
        ["connection", "rewards", "scaling", "movement", "ui-theme", "crash", "other"];

    private readonly string snapshot;
    private readonly string? activityLogSection;
    private readonly string? debugLogSection;
    private readonly string? crashLogSection;
    private readonly string appVersion;
    private readonly AppTheme currentTheme;

    public BugReportWindow(
        AppTheme theme,
        bool hasCrashLog = true,
        string? presetCategory = null,
        string? presetTitle = null,
        string snapshot = "",
        string? activityLogSection = null,
        string? debugLogSection = null,
        string? crashLogSection = null,
        string? appVersion = null)
    {
        this.snapshot = snapshot;
        this.activityLogSection = activityLogSection;
        this.debugLogSection = debugLogSection;
        this.crashLogSection = crashLogSection;
        this.appVersion = appVersion ?? string.Empty;
        currentTheme = theme;

        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, theme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;

        SnapshotTextBox.Text = snapshot;

        if (!hasCrashLog)
        {
            CrashLogCheckBox.Visibility = Visibility.Collapsed;
        }

        if (!string.IsNullOrEmpty(presetCategory))
        {
            var index = Array.IndexOf(CategoryKeys, presetCategory);
            if (index >= 0)
            {
                CategoryComboBox.SelectedIndex = index;
            }
        }

        if (!string.IsNullOrEmpty(presetTitle))
        {
            TitleTextBox.Text = presetTitle;
        }

        Loaded += (_, _) =>
        {
            TitleTextBox.Focus();
            TitleTextBox.SelectAll();
        };
    }

    public string BugTitle => TitleTextBox.Text.Trim();

    public string WhatHappened => WhatHappenedTextBox.Text.Trim();

    public string ExpectedBehavior => ExpectedTextBox.Text.Trim();

    public string StepsToReproduce => StepsTextBox.Text.Trim();

    public string ContactName => ContactTextBox.Text.Trim();

    public string Category => (CategoryComboBox.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() : null) ?? "other";

    public string Severity => (SeverityComboBox.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() : null) ?? "normal";

    public bool IncludeActivityLog => ActivityLogCheckBox.IsChecked == true;

    public bool IncludeDebugLog => DebugLogCheckBox.IsChecked == true;

    public bool IncludeCrashLog => CrashLogCheckBox.IsChecked == true && CrashLogCheckBox.Visibility == Visibility.Visible;

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        Closed -= OnWindowClosed;
    }

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
    }

    private void OnSendClicked(object sender, RoutedEventArgs e)
    {
        if (!ValidateForm(out var validationMessage))
        {
            ValidationTextBlock.Text = validationMessage;
            ValidationTextBlock.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnCloseButtonClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnPreviewClicked(object sender, RoutedEventArgs e)
    {
        var preview = BugReportPreviewBuilder.Build(
            BugTitle,
            Category,
            Severity,
            WhatHappened,
            ExpectedBehavior,
            StepsToReproduce,
            ContactName,
            appVersion,
            snapshot,
            IncludeActivityLog ? activityLogSection : null,
            IncludeDebugLog ? debugLogSection : null,
            IncludeCrashLog ? crashLogSection : null);

        var previewWindow = new BugReportPreviewWindow(preview, currentTheme)
        {
            Owner = this
        };
        previewWindow.ShowDialog();
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
        }
    }

    private bool ValidateForm(out string validationMessage)
    {
        if (!IsWithinRequiredRange(BugTitle, 8, 120))
        {
            validationMessage = LocalizationService.Translate("Bug title must be 8 to 120 characters.");
            return false;
        }

        if (!IsWithinRequiredRange(WhatHappened, 20, 5000))
        {
            validationMessage = LocalizationService.Translate("What happened must be 20 to 5000 characters.");
            return false;
        }

        if (!IsWithinRequiredRange(ExpectedBehavior, 20, 5000))
        {
            validationMessage = LocalizationService.Translate("Expected behavior must be 20 to 5000 characters.");
            return false;
        }

        if (!IsWithinRequiredRange(StepsToReproduce, 20, 5000))
        {
            validationMessage = LocalizationService.Translate("Steps to reproduce must be 20 to 5000 characters.");
            return false;
        }

        validationMessage = string.Empty;
        return true;
    }

    private static bool IsWithinRequiredRange(string value, int minLength, int maxLength) =>
        value.Length >= minLength && value.Length <= maxLength;

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
```

- [ ] **Step 2: Build the app project**

Run: `dotnet build "<repo>\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded (the `OpenBugReportAsync` call site in the VM still needs updating — that's Task 7. If the build fails because the old call site passes the wrong constructor args, proceed to Task 7 and build after that).

- [ ] **Step 3: Commit (combined with Task 5 XAML changes)**

```powershell
cd "<repo>"
git add VrcTwitchOscBridge/BugReportWindow.xaml VrcTwitchOscBridge/BugReportWindow.xaml.cs
git commit -m "Extend BugReportWindow with category, severity, diagnostics panel, and preview"
```

---

## Task 7: MainWindowViewModel — extend OpenBugReportAsync + crash prompt

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`
- Modify: `VrcTwitchOscBridge/App.xaml.cs`

- [ ] **Step 1: Find the theme display name helper**

In `MainWindowViewModel.cs`, the `ThemeOptions` list (line 731) maps each `AppTheme` to a display string. Add a helper method near the bug report code (after line 17956, the end of the current `OpenBugReportAsync`):

```csharp
    private string GetThemeDisplayName()
    {
        foreach (var option in ThemeOptions)
        {
            if (option.Value == SelectedTheme)
            {
                return option.Label;
            }
        }

        return Enum.GetName(SelectedTheme) ?? "Unknown";
    }
```

- [ ] **Step 2: Replace OpenBugReportAsync with the extended version**

Replace the entire `OpenBugReportAsync` method (lines 17896-17956) with:

```csharp
    private async Task OpenBugReportAsync(
        string? presetCategory = null,
        string? presetTitle = null)
    {
        var latestCrashPath = Path.Combine(AppDataPaths.CrashLogFolder, "latest-crash.txt");
        var hasCrashLog = File.Exists(latestCrashPath);

        var snapshot = BugReportSnapshotService.Build(new BugReportSnapshotData(
            IsBroadcasterConnected,
            IsBotConnected,
            IsVrChatConnected,
            OscStatusDetail,
            ResolveVrChatAvatarName(CurrentVrChatAvatarId),
            CurrentVrChatAvatarId,
            CurrentAvatarHeightMeters,
            SelectedTheme,
            GetThemeDisplayName(),
            GetAppVersionDisplay()));

        var activityLogSection = bugReportService.BuildActivityLogSection(LogEntries.ToArray());
        var debugLogSection = bugReportService.BuildDebugLogSection();
        var crashLogSection = hasCrashLog ? bugReportService.BuildCrashLogSection() : null;

        var dialog = new VrcTwitchOscBridge.BugReportWindow(
            SelectedTheme,
            hasCrashLog,
            presetCategory,
            presetTitle,
            snapshot,
            activityLogSection,
            debugLogSection,
            crashLogSection,
            GetAppVersionDisplay())
        {
            Owner = Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        string? activityLog = dialog.IncludeActivityLog ? activityLogSection : null;
        string? debugLog = dialog.IncludeDebugLog ? debugLogSection : null;
        string? crashLog = dialog.IncludeCrashLog ? crashLogSection : null;

        var submission = new BugReportSubmission(
            dialog.BugTitle,
            dialog.WhatHappened,
            dialog.ExpectedBehavior,
            dialog.StepsToReproduce,
            dialog.ContactName,
            GetAppVersionDisplay(),
            dialog.Category,
            dialog.Severity,
            snapshot,
            activityLog,
            debugLog,
            crashLog);

        AppendLog("Sending bug report to Crystal Relay's bug report service.");
        var result = await bugReportService.SubmitAsync(submission);
        if (result.Succeeded && !string.IsNullOrWhiteSpace(result.IssueUrl))
        {
            AppendLog($"Bug report submitted: {result.IssueUrl}");
            var shouldOpenIssue = ThemedDialogWindow.ShowYesNo(
                Application.Current?.MainWindow,
                SelectedTheme,
                T("Bug report sent"),
                $"{T("Crystal Relay created a GitHub issue for this report.")}{Environment.NewLine}{Environment.NewLine}{result.IssueUrl}",
                T("Open Issue"),
                T("Close"));
            if (shouldOpenIssue)
            {
                OpenUri(result.IssueUrl);
            }

            if (dialog.IncludeCrashLog)
            {
                MarkCrashReportSeen();
            }

            return;
        }

        var errorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? T("The bug report service did not accept the report.")
            : result.ErrorMessage;
        AppendLog($"Bug report could not be sent: {errorMessage}");
        var shouldOpenFallback = ThemedDialogWindow.ShowYesNo(
            Application.Current?.MainWindow,
            SelectedTheme,
            T("Bug report could not be sent"),
            $"{errorMessage}{Environment.NewLine}{Environment.NewLine}{T("Open the GitHub Issues page instead?")}",
            T("Open GitHub Issues"),
            T("Close"));
        if (shouldOpenFallback)
        {
            OpenUri(BugReportService.GitHubIssuesUrl);
        }
    }
```

- [ ] **Step 3: Add crash-prompt methods after OpenBugReportAsync**

Add immediately after the `GetThemeDisplayName` method added in Step 1:

```csharp
    private async Task CheckForPendingCrashReportAsync()
    {
        var latestCrashPath = Path.Combine(AppDataPaths.CrashLogFolder, "latest-crash.txt");
        var seenMarkerPath = Path.Combine(AppDataPaths.CrashLogFolder, "crash-report-seen.marker");

        if (!File.Exists(latestCrashPath))
        {
            return;
        }

        DateTime crashTime;
        try
        {
            crashTime = File.GetLastWriteTimeUtc(latestCrashPath);
        }
        catch
        {
            return;
        }

        DateTime seenTime = DateTime.MinValue;
        if (File.Exists(seenMarkerPath))
        {
            try
            {
                seenTime = File.GetLastWriteTimeUtc(seenMarkerPath);
            }
            catch
            {
            }
        }

        if (crashTime <= seenTime)
        {
            return;
        }

        var shouldReport = ThemedDialogWindow.ShowYesNo(
            Application.Current?.MainWindow,
            SelectedTheme,
            T("Crystal Relay crashed last time"),
            T("Crystal Relay closed unexpectedly during your last session. Send a bug report with the crash log attached?"),
            T("Send crash report"),
            T("Not now"));

        if (!shouldReport)
        {
            MarkCrashReportSeen(seenMarkerPath);
            return;
        }

        await OpenBugReportAsync(
            presetCategory: "crash",
            presetTitle: T("Crash on {0}", crashTime.ToLocalTime().ToString("g")));
    }

    private void MarkCrashReportSeen(string? seenMarkerPath = null)
    {
        seenMarkerPath ??= Path.Combine(AppDataPaths.CrashLogFolder, "crash-report-seen.marker");
        try
        {
            File.WriteAllText(seenMarkerPath, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch
        {
        }
    }
```

- [ ] **Step 4: Add startup hook in App.xaml.cs**

In `VrcTwitchOscBridge/App.xaml.cs`, find line 92 (`MainWindow.Show();`) and replace it with:

```csharp
        MainWindow.Show();
        _ = (MainWindow.DataContext as MainWindowViewModel)?.CheckForPendingCrashReportAsync();
```

- [ ] **Step 5: Build the app project**

Run: `dotnet build "<repo>\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded.

- [ ] **Step 6: Run all tests**

Run: `dotnet test "<repo>\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj"`
Expected: All tests pass (including the new snapshot, preview builder, and diagnostic section tests).

- [ ] **Step 7: Commit**

```powershell
cd "<repo>"
git add VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs VrcTwitchOscBridge/App.xaml.cs
git commit -m "Add crash-prompt on startup and extend OpenBugReportAsync with snapshot, diagnostics, and presets"
```

---

## Task 8: Cloudflare bug-report-worker — extend for category, severity, and raised caps

**Files:**
- Modify: `cloudflare/bug-report-worker/src/index.js`
- Modify: `cloudflare/bug-report-worker/README.md`

- [ ] **Step 1: Raise constants in the worker**

In `cloudflare/bug-report-worker/src/index.js`, replace lines 4-5:

```js
const MAX_PAYLOAD_BYTES = 20 * 1024;
const MAX_DIAGNOSTICS_LENGTH = 12 * 1024;
```

With:

```js
const MAX_PAYLOAD_BYTES = 56 * 1024;
const MAX_DIAGNOSTICS_LENGTH = 44 * 1024;
```

- [ ] **Step 2: Add category/severity maps**

After the existing constants block (after line 11), add:

```js
const CATEGORY_LABELS = {
  "connection": "connection",
  "rewards": "rewards",
  "scaling": "scaling",
  "movement": "movement",
  "ui-theme": "ui-theme",
  "crash": "crash",
  "other": null
};

const SEVERITY_PREFIX = {
  "low": "[Low]",
  "normal": "",
  "high": "[High]",
  "crash": "[Crash]"
};
```

- [ ] **Step 3: Extend validatePayload**

Replace the `validatePayload` function (lines 84-107) with:

```js
function validatePayload(payload) {
  const title = normalize(payload.title);
  const whatHappened = normalize(payload.whatHappened);
  const expectedBehavior = normalize(payload.expectedBehavior);
  const stepsToReproduce = normalize(payload.stepsToReproduce);
  const category = normalize(payload.category) || "other";
  const severity = normalize(payload.severity) || "normal";

  if (!isInRange(title, 8, 120)) {
    return { ok: false, message: "Bug title must be 8 to 120 characters." };
  }

  if (!isInRange(whatHappened, 20, 5000)) {
    return { ok: false, message: "What happened must be 20 to 5000 characters." };
  }

  if (!isInRange(expectedBehavior, 20, 5000)) {
    return { ok: false, message: "Expected behavior must be 20 to 5000 characters." };
  }

  if (!isInRange(stepsToReproduce, 20, 5000)) {
    return { ok: false, message: "Steps to reproduce must be 20 to 5000 characters." };
  }

  if (!isInRange(category, 1, 40)) {
    return { ok: false, message: "Category is missing." };
  }

  if (!isInRange(severity, 1, 40)) {
    return { ok: false, message: "Severity is missing." };
  }

  return { ok: true };
}
```

- [ ] **Step 4: Rewrite buildGitHubIssue**

Replace the `buildGitHubIssue` function (lines 127-163) with:

```js
function buildGitHubIssue(payload) {
  const title = normalize(payload.title);
  const appVersion = normalize(payload.appVersion) || "Unknown";
  const contactName = normalize(payload.contactName) || "Not provided";
  const submittedAtUtc = normalize(payload.submittedAtUtc) || new Date().toISOString();
  const category = normalize(payload.category) || "other";
  const severity = normalize(payload.severity) || "normal";
  const severityPrefix = SEVERITY_PREFIX[severity] ?? "";
  const snapshot = trimUtf8(sanitize(normalize(payload.snapshot)), 2 * 1024);
  const activityLog = payload.activityLog ? trimUtf8(sanitize(normalize(payload.activityLog)), 16 * 1024) : null;
  const debugLog = payload.debugLog ? trimUtf8(sanitize(normalize(payload.debugLog)), 16 * 1024) : null;
  const crashLog = payload.crashLog ? trimUtf8(sanitize(normalize(payload.crashLog)), 12 * 1024) : null;

  const body = [
    "## Bug Report",
    "",
    `**Category:** ${category}`,
    `**Severity:** ${severity}`,
    `**App version:** ${appVersion}`,
    `**Submitted at:** ${submittedAtUtc}`,
    `**Contact:** ${contactName}`,
    "",
    "## What happened",
    "",
    sanitize(normalize(payload.whatHappened)),
    "",
    "## Expected behavior",
    "",
    sanitize(normalize(payload.expectedBehavior)),
    "",
    "## Steps to reproduce",
    "",
    sanitize(normalize(payload.stepsToReproduce)),
    "",
    "## Live status snapshot",
    "",
    snapshot.length > 0 ? `\`\`\`text\n${snapshot}\n\`\`\`` : "Not included.",
    "",
    "## Activity log",
    "",
    activityLog && activityLog.length > 0 ? `\`\`\`text\n${activityLog}\n\`\`\`` : "Not included.",
    "",
    "## Debug logs",
    "",
    debugLog && debugLog.length > 0 ? `\`\`\`text\n${debugLog}\n\`\`\`` : "Not included.",
    "",
    "## Crash log",
    "",
    crashLog && crashLog.length > 0 ? `\`\`\`text\n${crashLog}\n\`\`\`` : "Not included."
  ].join("\n");

  const baseLabels = ISSUE_LABELS;
  const categoryLabel = CATEGORY_LABELS[category] ?? null;
  const labels = categoryLabel ? [...baseLabels, categoryLabel] : baseLabels;

  return {
    title: `[Bug] ${severityPrefix} ${title}`.trim(),
    body,
    labels
  };
}
```

- [ ] **Step 5: Update the worker README**

In `cloudflare/bug-report-worker/README.md`, add a new section after the "Required setup" section:

```markdown
## Category and severity labels

The desktop app sends a `category` and `severity` with each report. The worker maps
category to a GitHub label on the created issue:

| Category   | GitHub label  |
|------------|---------------|
| connection | `connection`  |
| rewards    | `rewards`     |
| scaling    | `scaling`     |
| movement   | `movement`    |
| ui-theme   | `ui-theme`    |
| crash      | `crash`       |
| other      | (no label)    |

Severity adds a prefix to the issue title (`[Low]`, `[High]`, `[Crash]`; `normal` has no prefix).

Create these labels in `seluvia/crystal-relay-public` before deploying:
`connection`, `rewards`, `scaling`, `movement`, `ui-theme`, `crash`.

If a label is missing, the worker retries without labels so the report still succeeds.

## Payload caps

- Max payload: 56 KB
- Max diagnostics: 44 KB
- Snapshot: 2 KB, Activity log: 16 KB, Debug log: 16 KB, Crash log: 12 KB
```

- [ ] **Step 6: Commit**

```powershell
cd "<repo>"
git add cloudflare/bug-report-worker/src/index.js cloudflare/bug-report-worker/README.md
git commit -m "Extend bug-report-worker with category labels, severity prefix, and raised caps"
```

---

## Task 9: Localization — add new en-US keys

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json`

The following keys already exist and do NOT need adding: `"Connected"`, `"Disconnected"`, `"Theme"`, `"VRChat"`, `"Close"`, `"Cancel"`, `"Send Report"`, `"Bug report sent"`, `"Open Issue"`, `"Open GitHub Issues"`.

- [ ] **Step 1: Add new keys to en-US.extra.json**

Open `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json`. Add these keys in alphabetical order within the existing JSON object (match the file's existing sorted style):

```json
  "Category": "Category",
  "Connection": "Connection",
  "Crash": "Crash",
  "Crash logs are sanitized before sending.": "Crash logs are sanitized before sending.",
  "Crash on {0}": "Crash on {0}",
  "Current avatar": "Current avatar",
  "Diagnostics": "Diagnostics",
  "Eye height": "Eye height",
  "High": "High",
  "Include activity log": "Include activity log",
  "Include crash log": "Include crash log",
  "Include debug logs": "Include debug logs",
  "Live status snapshot (always included)": "Live status snapshot (always included)",
  "Low": "Low",
  "Movement": "Movement",
  "Normal": "Normal",
  "Not now": "Not now",
  "OSC": "OSC",
  "Other": "Other",
  "Preview Bug Report": "Preview Bug Report",
  "Preview Bug Report | Crystal Relay": "Preview Bug Report | Crystal Relay",
  "Preview sanitized report": "Preview sanitized report",
  "Recent activity log entries, sanitized before sending.": "Recent activity log entries, sanitized before sending.",
  "Recent debug log lines, sanitized before sending.": "Recent debug log lines, sanitized before sending.",
  "Rewards & Avatar Sets": "Rewards & Avatar Sets",
  "Send crash report": "Send crash report",
  "Severity": "Severity",
  "Twitch broadcaster": "Twitch broadcaster",
  "Twitch bot": "Twitch bot",
  "UI / Theme": "UI / Theme",
  "Avatar Scaling": "Avatar Scaling",
  "App version": "App version",
  "Crystal Relay closed unexpectedly during your last session. Send a bug report with the crash log attached?": "Crystal Relay closed unexpectedly during your last session. Send a bug report with the crash log attached?",
  "Crystal Relay crashed last time": "Crystal Relay crashed last time",
```

Insert each key at the correct alphabetical position. The file is sorted alphabetically by key.

- [ ] **Step 2: Build and run localization audit**

Run: `dotnet run --project "<repo>\LocalizationAudit\LocalizationAudit.csproj" -- "<repo>\VrcTwitchOscBridge\Resources\Localization"`
Expected: No failures for missing en-US keys. (There may be pre-existing failures unrelated to this change.)

- [ ] **Step 3: Commit**

```powershell
cd "<repo>"
git add VrcTwitchOscBridge/Resources/Localization/en-US.extra.json
git commit -m "Add en-US localization keys for bug report redesign"
```

---

## Task 10: Localization — translate into all non-English languages

**Files:**
- Modify: All 13 non-English `.extra.json` files in `VrcTwitchOscBridge/Resources/Localization/`

Languages: de-DE, es-ES, fr-FR, it-IT, ja-JP, ko-KR, pl-PL, pt-BR, ru-RU, sv-SE, th-TH, zh-CN, zh-TW.

- [ ] **Step 1: Add translated keys to each non-English file**

For each of the 13 non-English `.extra.json` files, add the same new keys (matching the en-US set from Task 9) with translated values. Follow AGENTS.md translation rules:
- Informal register (`du` for de-DE, `tú` for es-ES, `tu` for fr-FR, etc.)
- Brand/technical terms stay English: `Crystal Relay`, `OSC`, `OSCQuery`, `VRChat`, `Twitch`, `GitHub`, `Debug`
- Format placeholders preserved exactly (`{0}`)
- No empty values, no untranslated English (except brand/technical terms)

For each language, add each key in alphabetical position within the JSON object. Example for de-DE:

```json
  "Category": "Kategorie",
  "Connection": "Verbindung",
  "Crash": "Absturz",
  "Crash logs are sanitized before sending.": "Absturzprotokolle werden vor dem Senden bereinigt.",
  "Crash on {0}": "Absturz am {0}",
  "Current avatar": "Aktueller Avatar",
  "Diagnostics": "Diagnose",
  "Eye height": "Augenhöhe",
  "High": "Hoch",
  "Include activity log": "Aktivitätsprotokoll anhängen",
  "Include crash log": "Absturzprotokoll anhängen",
  "Include debug logs": "Debug-Logs anhängen",
  "Live status snapshot (always included)": "Live-Status-Snapshot (immer enthalten)",
  "Low": "Niedrig",
  "Movement": "Bewegung",
  "Normal": "Normal",
  "Not now": "Nicht jetzt",
  "OSC": "OSC",
  "Other": "Sonstige",
  "Preview Bug Report": "Bug-Report-Vorschau",
  "Preview Bug Report | Crystal Relay": "Bug-Report-Vorschau | Crystal Relay",
  "Preview sanitized report": "Bereinigten Report vorschauen",
  "Recent activity log entries, sanitized before sending.": "Letzte Aktivitätsprotokoll-Einträge, vor dem Senden bereinigt.",
  "Recent debug log lines, sanitized before sending.": "Letzte Debug-Log-Zeilen, vor dem Senden bereinigt.",
  "Rewards & Avatar Sets": "Rewards & Avatar-Sets",
  "Send crash report": "Absturzreport senden",
  "Severity": "Schweregrad",
  "Twitch broadcaster": "Twitch-Broadcaster",
  "Twitch bot": "Twitch-Bot",
  "UI / Theme": "UI / Theme",
  "Avatar Scaling": "Avatar-Skalierung",
  "App version": "App-Version",
  "Crystal Relay closed unexpectedly during your last session. Send a bug report with the crash log attached?": "Crystal Relay wurde in deiner letzten Sitzung unerwartet beendet. Soll ein Bug-Report mit angehängtem Absturzprotokoll gesendet werden?",
  "Crystal Relay crashed last time": "Crystal Relay ist beim letzten Mal abgestürzt",
```

Repeat for all 13 languages with natural, conversational translations.

- [ ] **Step 2: Run localization audit**

Run: `dotnet run --project "<repo>\LocalizationAudit\LocalizationAudit.csproj" -- "<repo>\VrcTwitchOscBridge\Resources\Localization"`
Expected: No new failures. All new keys present in all languages with no empty values.

- [ ] **Step 3: Commit**

```powershell
cd "<repo>"
git add VrcTwitchOscBridge/Resources/Localization/*.extra.json
git commit -m "Add translations for bug report redesign in all non-English languages"
```

---

## Task 11: Final build verification and smoke test

**Files:** None (verification only)

- [ ] **Step 1: Full build**

Run: `dotnet build "<repo>\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded with no errors.

- [ ] **Step 2: Run all tests**

Run: `dotnet test "<repo>\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj"`
Expected: All tests pass.

- [ ] **Step 3: Run localization audit**

Run: `dotnet run --project "<repo>\LocalizationAudit\LocalizationAudit.csproj" -- "<repo>\VrcTwitchOscBridge\Resources\Localization"`
Expected: No new failures related to bug report keys.

- [ ] **Step 4: Manual smoke test via debug launcher**

Launch: `<repo>\Launch-Crystal-Relay-Debug.bat`

Verify:
1. Open Report Bug window — category and severity dropdowns appear, diagnostics panel shows snapshot.
2. Toggle each diagnostic checkbox, click "Preview sanitized report" — preview window shows correct content.
3. Fill in fields, send a test report — confirm GitHub issue is created with correct labels and body sections.
4. Create a fake `latest-crash.txt` in `%LOCALAPPDATA%\CrystalRelay\CrashLogs\` newer than `crash-report-seen.marker` (or delete the marker), relaunch — crash prompt should appear.
5. Click "Not now" — relaunch — prompt should NOT reappear.
6. Delete marker again, relaunch, click "Send crash report" — bug report window opens with category preset to "Crash" and title pre-filled.

- [ ] **Step 5: Final commit if any fixes were needed during smoke test**

If the smoke test revealed issues that required fixes, commit those fixes. Otherwise no commit needed.

---

## Post-implementation notes

- **GitHub labels prerequisite:** Create labels `connection`, `rewards`, `scaling`, `movement`, `ui-theme`, `crash` in `seluvia/crystal-relay-public` before deploying the worker. The worker's 422-fallback means missing labels won't block reports, but they should exist for proper triage.
- **Worker deployment:** Deploy the updated worker manually with `wrangler deploy` from `cloudflare\bug-report-worker`. This is user-initiated.
- **Version bump:** If this ships as a test/beta build, update `AGENTS.md` active build version and `VrcTwitchOscBridge.csproj` version per the AGENTS.md versioning rules before running the build script.
- **No website change:** Beta/test builds must NOT change the Void Crystal Website download URL.
