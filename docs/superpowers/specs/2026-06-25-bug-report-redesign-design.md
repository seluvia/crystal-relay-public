# Bug Report System Redesign — Design Spec

**Date:** 2026-06-25
**Status:** Approved (pending implementation plan)
**Scope:** Richer diagnostics, crash-triggered prompting, payload budget bump
**Active version:** v3.1.9 (beta4 lane)

## Goal

Allow more useful information to be shared through Crystal Relay's in-app bug report
system, and make bug reporting easier to complete, by:

1. Adding an always-on lean live-status snapshot to every report.
2. Replacing the single "Include sanitized logs" checkbox with granular log toggles.
3. Adding a privacy preview so users can see the exact sanitized text before sending.
4. Adding category + severity fields that map to GitHub labels.
5. Auto-prompting a bug report after a crash on next launch, pre-filled with context.
6. Raising payload and per-section caps so richer reports are not trimmed to `[trimmed]`.

## Non-goals

- No screenshot capture (deferred).
- No local draft / retry store (deferred).
- No rate-limit identity changes (IP-hash stays).
- No lowering of existing character minimums on user fields.
- No changes to `SensitiveTextSanitizer` patterns.
- No changes to release/build scripts, manifests, or package layout.
- No public repo / README / CHANGELOG edits (release-time workflow handles those).

## Architecture

The pipeline keeps its current shape and is enriched rather than rewritten:

```
BugReportWindow (extended)
  -> BugReportSubmission (+category, +severity, +snapshot, +sections)
  -> BugReportService.SubmitAsync (extended, raised caps)
  -> Cloudflare bug-report-worker /report (extended)
  -> GitHub issue with labels [bug, from-crystal-relay, needs-triage, <category?>]
```

A separate crash-prompt flow fires on startup:

```
App.OnStartup -> MainWindow.Show()
  -> MainWindowViewModel.CheckForPendingCrashReportAsync()
  -> if latest-crash.txt newer than crash-report-seen.marker:
       ThemedDialogWindow.ShowYesNo(crash prompt)
       Yes -> OpenBugReportAsync(presetCategory: "crash", presetTitle: "Crash on <date>")
       No  -> MarkCrashReportSeen()
```

### Components

| Component | Change | Purpose |
|---|---|---|
| `BugReportWindow.xaml` / `.xaml.cs` | Extend | Category + severity combos, collapsible Diagnostics panel with 3 toggles, preview button, preset support |
| `BugReportSnapshotService` (new) | New | Builds the lean live-status snapshot string from VM public properties |
| `BugReportPreviewBuilder` (new) | New | Mirrors worker body template; renders preview text client-side |
| `BugReportPreviewWindow.xaml` / `.xaml.cs` (new) | New | Themed read-only preview modal |
| `BugReportService.cs` | Extend | Raise caps; split `BuildSanitizedDiagnostics` into 3 section builders; extend payload/submission records |
| `MainWindowViewModel.cs` | Extend | Extend `OpenBugReportAsync`; add crash-prompt + seen-marker logic; build snapshot |
| `App.xaml.cs` | Extend | Add crash-check call after `MainWindow.Show()` |
| `VrcTwitchOscBridge.csproj` | Extend | Add `<Page>` + compile entries for new XAML/.cs files |
| `bug-report-worker/src/index.js` | Extend | Raise caps; add category -> label map; rewrite `buildGitHubIssue` body; extend `validatePayload` |
| `bug-report-worker/README.md` | Extend | Note new category/severity fields and label requirements |
| Localization `.extra.json` files (all languages) | Extend | New keys + translations |

## Detailed design

### 1. BugReportWindow UI

Window size grows from `720x760` to `720x860` (min `620x680`). Single-page layout, top to bottom:

1. **Category + severity row** (new): two-column grid directly under the existing intro/danger banner.
   - Category `ComboBox`: Connection, Rewards & Avatar Sets, Avatar Scaling, Movement, UI / Theme, Crash, Other. Default: `Other`.
   - Severity `ComboBox`: Low, Normal, High, Crash. Default: `Normal`.
   - Each `ComboBoxItem` carries its machine key as `Tag` (e.g. `Tag="connection"`); display text is localized.
2. **Existing fields** (unchanged): Title, What happened, Expected behavior, Steps to reproduce, Contact name. Same validation, same max lengths.
3. **Diagnostics panel** (new, replaces the single "Include sanitized logs" checkbox): collapsible `Border` titled "Diagnostics" with chevron toggle, expanded by default. Contains:
   - Live status snapshot box (read-only, auto-populated, always included, not toggleable).
   - Three checkboxes: Include crash log (default on, hidden if no crash log), Include activity log (default off), Include debug logs (default off).
   - "Preview sanitized report" button.
4. **Validation text** (unchanged location).
5. **Cancel / Send Report buttons** (unchanged).

The old `IncludeSanitizedLogs` checkbox and its helper text are removed from the XAML. The localization key remains in files (no removal churn).

### 2. BugReportSnapshotService

New file: `VrcTwitchOscBridge\Services\BugReportSnapshotService.cs`.

**Signature:**

```csharp
internal static class BugReportSnapshotService
{
    public static string Build(BugReportSnapshotData data);
}

internal sealed record BugReportSnapshotData(
    bool IsBroadcasterConnected,
    bool IsBotConnected,
    bool IsVrChatConnected,
    string OscStatusDetail,
    string CurrentAvatarName,
    string CurrentAvatarId,
    double CurrentAvatarHeightMeters,
    AppTheme SelectedTheme,
    string AppVersion);
```

**Output format (~10 lines, ~500 bytes):**

```
Crystal Relay Status Snapshot
Twitch broadcaster: Connected
Twitch bot: Disconnected
VRChat: Connected
OSC: VRChat is connected through OSCQuery.
Current avatar: Ryo Adoption (avtr_abc123-...)
Eye height: 1.62 m
Theme: Void Crystal
App version: 3.1.9 (DEBUG)
```

**Rules:**
- Connection states render as localized `Connected` / `Disconnected`.
- Avatar name resolves via existing `ResolveVrChatAvatarName(CurrentVrChatAvatarId)`; falls back to `Unknown` if blank.
- Avatar ID is truncated to first 20 chars + `...` to avoid dominating the line.
- `OscStatusDetail` passed through as-is (already localized, no secrets).
- Theme renders via `Enum.GetName`.
- App version uses existing `GetAppVersionDisplay()` (includes ` (DEBUG)` suffix on debug builds).
- Snapshot runs through `SensitiveTextSanitizer.Sanitize` as a final safety net.

### 3. BugReportService changes

**Constants (raised):**

```csharp
private const int MaxDiagnosticLogLength = 40 * 1024;   // was 11 KB
private const int MaxPayloadLength = 56 * 1024;          // was 20 KB
private const int MaxActivityLogLines = 200;             // was 80
private const int MaxDebugLogLines = 400;                // was 160
```

**`BugReportSubmission` record (extended):**

```csharp
internal sealed record BugReportSubmission(
    string Title,
    string WhatHappened,
    string ExpectedBehavior,
    string StepsToReproduce,
    string ContactName,
    string AppVersion,
    string Category,          // new
    string Severity,          // new
    string Snapshot,          // new — always populated
    string? ActivityLog,      // new — null when toggled off
    string? DebugLog,         // new — null when toggled off
    string? CrashLog);        // new — null when toggled off or absent
```

Null means "section toggled off" -> worker renders "Not included." Empty string means "toggled on but produced nothing" (rare) -> worker renders "Included but empty."

**`BugReportPayload` (wire record):**

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

Each diagnostic field trimmed to its own UTF-8 budget before the overall payload check:
- Snapshot: capped at 2 KB.
- Activity log: capped at 16 KB.
- Debug log: capped at 16 KB.
- Crash log: capped at 12 KB.

Sum <= 46 KB diagnostics + 15 KB user fields ~ 61 KB, under the 56 KB payload cap after per-field character trims. If serialized JSON still exceeds `MaxPayloadLength`, tiered fallback in this exact order:
1. Drop debug log (set to null).
2. Drop activity log (set to null).
3. Drop crash log (set to null).
4. Trim snapshot to 1 KB.

**Snapshot is never dropped entirely** — it is the most valuable triage data and is always <2 KB.

**`BuildSanitizedDiagnostics` removed; replaced by three focused methods:**

```csharp
public string BuildActivityLogSection(IEnumerable<string> activityLogEntries);
public string BuildDebugLogSection();
public string BuildCrashLogSection();
```

Each returns a sanitized, section-headed, UTF-8-trimmed string (or empty). The snapshot is built separately by `BugReportSnapshotService` and passed in already-formed.

**`SubmitAsync`** builds the payload from the new submission fields, applies per-field `TrimForTransport` plus per-section UTF-8 trim, then posts. `X-Crystal-Relay-Version` header stays. Rate-limit handling (`TooManyRequests` -> `Retry-After`) unchanged.

### 4. Cloudflare worker changes

**Constants (raised):**

```js
const MAX_PAYLOAD_BYTES = 56 * 1024;      // was 20 KB
const MAX_DIAGNOSTICS_LENGTH = 44 * 1024;  // was 12 KB
```

**Category -> label map (new):**

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

Unknown category values fall back to `other` (no extra label). Unknown severity values fall back to `normal` (no prefix). This is defensive so a future client version's new value never blocks reporting.

**`validatePayload` (extended):** adds category + severity validation. Each must be a non-empty string <= 40 chars; unknown values are normalized to `other` / `normal` rather than rejected.

**`buildGitHubIssue` (rewritten body):**

Issue title: `[Bug] ${severityPrefix} ${title}` (empty prefix for normal -> identical to today's `[Bug] ${title}`).

Issue body sections, in order:

```
## Bug Report

**Category:** <category>
**Severity:** <severity>
**App version:** <version>
**Submitted at:** <timestamp>
**Contact:** <contact>

## What happened
<whatHappened>

## Expected behavior
<expectedBehavior>

## Steps to reproduce
<stepsToReproduce>

## Live status snapshot
```text
<snapshot>
```

## Activity log
```text
<activityLog>```  (or "Not included.")

## Debug logs
```text
<debugLog>```  (or "Not included.")

## Crash log
```text
<crashLog>```  (or "Not included.")
```

Each diagnostic field run through existing worker `sanitize()` + `trimUtf8()` before rendering. Null/empty sections render `Not included.` (preserving the existing convention).

**Labels (extended):**

```js
const ISSUE_LABELS = ["bug", "from-crystal-relay", "needs-triage"];
const categoryLabel = CATEGORY_LABELS[category] ?? null;
const labels = categoryLabel ? [...ISSUE_LABELS, categoryLabel] : ISSUE_LABELS;
```

Existing 422-with-labels fallback retry (`createResult.retryWithoutLabels`) preserved. A missing label on the repo won't block reports.

**Rate limits, auth, desktop-client validation, sanitization:** unchanged.

### 5. MainWindowViewModel changes

**`OpenBugReportAsync` (extended with optional presets):**

The VM pre-builds all potential diagnostic sections *before* opening the dialog so the preview window can show the exact text that will be sent. Building is cheap (reading a few files, sanitizing) and the strings are small (capped at 16 KB each). The pre-built strings are reused for the final submission, so no duplicate work.

```csharp
private async Task OpenBugReportAsync(
    string? presetCategory = null,
    string? presetTitle = null)
{
    var latestCrashPath = Path.Combine(AppDataPaths.CrashLogFolder, "latest-crash.txt");
    var hasCrashLog = File.Exists(latestCrashPath);

    // Pre-build the snapshot and all diagnostic sections so the preview is accurate.
    var snapshot = BugReportSnapshotService.Build(new BugReportSnapshotData(
        IsBroadcasterConnected,
        IsBotConnected,
        IsVrChatConnected,
        OscStatusDetail,
        ResolveVrChatAvatarName(CurrentVrChatAvatarId),
        CurrentVrChatAvatarId,
        CurrentAvatarHeightMeters,
        SelectedTheme,
        GetAppVersionDisplay()));

    var activityLogSection = bugReportService.BuildActivityLogSection(LogEntries.ToArray());
    var debugLogSection = bugReportService.BuildDebugLogSection();
    var crashLogSection = hasCrashLog ? bugReportService.BuildCrashLogSection() : null;

    var dialog = new BugReportWindow(
        SelectedTheme,
        hasCrashLog,
        presetCategory,
        presetTitle,
        snapshot,
        activityLogSection,
        debugLogSection,
        crashLogSection)
    {
        Owner = Application.Current?.MainWindow
    };

    if (dialog.ShowDialog() != true)
        return;

    // Reuse the pre-built sections; pass null for toggled-off sections.
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
    // ... success/failure handling unchanged ...

    // After a successful report that included the crash log, mark the crash as seen
    // so the prompt doesn't fire again for the same crash. This covers both the
    // crash-preset path and a manual report where the user toggled the crash log on.
    if (dialog.IncludeCrashLog && result.Succeeded)
    {
        MarkCrashReportSeen();
    }
}
```

The parameterless call path (normal "Report Bug" button) is unchanged behaviorally.

**Crash-prompt method (new):**

```csharp
private async Task CheckForPendingCrashReportAsync()
{
    var latestCrashPath = Path.Combine(AppDataPaths.CrashLogFolder, "latest-crash.txt");
    var seenMarkerPath = Path.Combine(AppDataPaths.CrashLogFolder, "crash-report-seen.marker");

    if (!File.Exists(latestCrashPath))
        return;

    DateTime crashTime;
    try { crashTime = File.GetLastWriteTimeUtc(latestCrashPath); }
    catch { return; }

    DateTime seenTime = DateTime.MinValue;
    if (File.Exists(seenMarkerPath))
    {
        try { seenTime = File.GetLastWriteTimeUtc(seenMarkerPath); }
        catch { }
    }

    if (crashTime <= seenTime)
        return;

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
    try { File.WriteAllText(seenMarkerPath, DateTimeOffset.UtcNow.ToString("O")); }
    catch { }
}
```

- One prompt per crash, ever. "Not now" writes the marker; a successful crash-preset report also writes it.
- `crash-report-seen.marker` lives in `AppDataPaths.CrashLogFolder` (no new top-level runtime folders).
- The method catches its own exceptions internally and never throws on the startup path.

**Startup hook** (`App.xaml.cs` `OnStartup`, after `MainWindow.Show()`):

```csharp
MainWindow.Show();
_ = ((MainWindowViewModel)MainWindow.DataContext).CheckForPendingCrashReportAsync();
```

Fire-and-forget is safe — the method is self-contained and never throws.

### 6. BugReportWindow.xaml.cs

**Constructor (extended):**

```csharp
public BugReportWindow(
    AppTheme theme,
    bool hasCrashLog = true,
    string? presetCategory = null,
    string? presetTitle = null,
    string snapshot = "",
    string? activityLogSection = null,
    string? debugLogSection = null,
    string? crashLogSection = null)
```

- Sets category combo box via `IndexOfCategory(presetCategory)` when provided.
- Pre-fills title textbox when `presetTitle` provided (user can still edit).
- Collapses `CrashLogCheckBox` + label when `hasCrashLog` is false.
- Stores `snapshot`, `activityLogSection`, `debugLogSection`, `crashLogSection` for use by the preview button.

**New public properties:**

```csharp
public string Category => ((ComboBoxItem?)CategoryComboBox.SelectedItem)?.Tag?.ToString() ?? "other";
public string Severity => ((ComboBoxItem?)SeverityComboBox.SelectedItem)?.Tag?.ToString() ?? "normal";
public bool IncludeActivityLog => ActivityLogCheckBox.IsChecked == true;
public bool IncludeDebugLog => DebugLogCheckBox.IsChecked == true;
public bool IncludeCrashLog => CrashLogCheckBox.IsChecked == true;
```

The old `IncludeSanitizedLogs` property is removed.

**Validation:** unchanged (four length-range checks). Category and severity enforced by combo boxes; no extra validation code.

**Preview button handler:** builds preview text via `BugReportPreviewBuilder.Build(...)`, passing the stored snapshot and diagnostic section strings (or null for toggled-off / unavailable sections), plus current dialog field values. Opens `BugReportPreviewWindow`.

**`IndexOfCategory`:** maps a machine key to combo box index via a static array mirroring the combo box items.

### 7. BugReportPreviewBuilder

New file: `VrcTwitchOscBridge\Services\BugReportPreviewBuilder.cs`.

A small static helper (~40 lines) that mirrors the worker's `buildGitHubIssue` body layout. Renders placeholder lines (`Not included.`) for toggled-off sections and `[trimmed]` markers where applicable, so the preview matches what GitHub will show as closely as possible. Returns a single string for the preview window to display.

**Signature:**

```csharp
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
        string? activityLogSection,   // null or empty -> "Not included."
        string? debugLogSection,      // null or empty -> "Not included."
        string? crashLogSection);     // null or empty -> "Not included."
}
```

The builder does **not** re-sanitize or re-trim — it receives the same pre-built, already-sanitized section strings the VM built before opening the dialog, so the preview is byte-for-byte accurate to what the worker will render (modulo the worker's own `sanitize()` + `trimUtf8()` pass, which is a final safety net the client-side sanitizer already covers).

### 8. BugReportPreviewWindow

New files: `VrcTwitchOscBridge\BugReportPreviewWindow.xaml` + `.xaml.cs`.

- Reuses the same themed chrome pattern as `BugReportWindow` (custom `WindowChrome`, themed title bar, themed scrollbars). Resource section copied from `BugReportWindow` so visuals match.
- Size: `640x560`, min `540x440`. `ResizeMode="CanResize"`, `WindowStartupLocation="CenterOwner"`.
- Title bar: localized "Preview Bug Report | Crystal Relay".
- Body: single read-only `TextBox`, `FontFamily="Consolas"`, `IsReadOnly="True"`, `TextWrapping="Wrap"`, `VerticalScrollBarVisibility="Auto"`, `Margin="16"`.
- Footer: single centered "Close" button (`SecondaryButtonStyle`), `IsCancel="True"`, `IsDefault="True"`.
- Code-behind: constructor takes `(string previewText, AppTheme theme)`, applies theme, subscribes to `ThemeManager.ThemeChanged`, sets `PreviewTextBox.Text`. No validation, no send.
- Both files must be explicitly added to `VrcTwitchOscBridge.csproj` (`<Page>` for XAML, compile entry for `.cs`).

### 9. Localization

**New keys (en-US source, translated into all non-English files):**

Category labels: `"Category"`, `"Connection"`, `"Rewards & Avatar Sets"`, `"Avatar Scaling"`, `"Movement"`, `"UI / Theme"`, `"Crash"`, `"Other"`.

Severity labels: `"Severity"`, `"Low"`, `"Normal"`, `"High"`. (`"Crash"` reuses the category string.)

Diagnostics panel: `"Diagnostics"`, `"Live status snapshot (always included)"`, `"Include crash log"`, `"Include activity log"`, `"Include debug logs"`, `"Preview sanitized report"`.

Snapshot line labels: `"Connected"`, `"Disconnected"`, `"Twitch broadcaster"`, `"Twitch bot"`, `"VRChat"`, `"OSC"`, `"Current avatar"`, `"Eye height"`, `"Theme"`, `"App version"`. (Most likely already exist as keys; reuse via grep rather than duplicate.)

Per-toggle helper text: `"Crash logs are sanitized before sending."`, `"Recent activity log entries, sanitized before sending."`, `"Recent debug log lines, sanitized before sending."`.

Preview window: `"Preview Bug Report | Crystal Relay"`, `"Preview Bug Report"`.

Crash prompt: `"Crystal Relay crashed last time"`, `"Crystal Relay closed unexpectedly during your last session. Send a bug report with the crash log attached?"`, `"Send crash report"`, `"Not now"`, `"Crash on {0}"`.

**Removals:** `"Include sanitized logs"` and `"Logs are optional and Crystal Relay redacts known tokens, cookies, and local user paths before sending."` stay in files (no removal churn); simply unreferenced.

**Translation rules (per AGENTS.md):**
- Informal register (`du`, `tu`, etc.) in all non-English files.
- Brand/technical terms stay English: `Crystal Relay`, `OSC`, `OSCQuery`, `VRChat`, `Twitch`, `GitHub`, `Debug`.
- Format placeholders preserved exactly (e.g. `{0}`).
- New keys translated into all non-English languages before merge; no untranslated English values left.

**Audit:** run the localization audit after implementation to verify key coverage, placeholder integrity, and no empty values.

## Caps summary

| Limit | Current | New |
|---|---|---|
| Activity log entries | 80 | 200 |
| Debug log lines | 160 | 400 |
| Client diagnostic cap | 11 KB | 40 KB |
| Client payload cap | 20 KB | 56 KB |
| Worker payload cap | 20 KB | 56 KB |
| Worker diagnostic cap | 12 KB | 44 KB |
| Per-section budgets (client) | n/a | Snapshot 2 KB, Activity 16 KB, Debug 16 KB, Crash 12 KB |

Worst case: 15 KB (user fields) + 0.5 KB (snapshot) + 44 KB (diagnostics) + 1 KB (markdown) ~ 60.5 KB, under GitHub's ~65,536-char body limit.

## File impact

### New files (5)
- `VrcTwitchOscBridge\Services\BugReportSnapshotService.cs`
- `VrcTwitchOscBridge\Services\BugReportPreviewBuilder.cs`
- `VrcTwitchOscBridge\BugReportPreviewWindow.xaml`
- `VrcTwitchOscBridge\BugReportPreviewWindow.xaml.cs`
- `docs/superpowers/specs/2026-06-25-bug-report-redesign-design.md` (this file)

### Modified files
- `VrcTwitchOscBridge\BugReportWindow.xaml`
- `VrcTwitchOscBridge\BugReportWindow.xaml.cs`
- `VrcTwitchOscBridge\Services\BugReportService.cs`
- `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`
- `VrcTwitchOscBridge\App.xaml.cs`
- `VrcTwitchOscBridge\VrcTwitchOscBridge.csproj`
- `cloudflare\bug-report-worker\src\index.js`
- `cloudflare\bug-report-worker\README.md`
- `VrcTwitchOscBridge\Resources\Localization\en-US.extra.json`
- All non-English `.extra.json` localization files (de-DE, es-ES, fr-FR, it-IT, ja-JP, ko-KR, pl-PL, pt-BR, pt-PT, ru-RU, tr-TR, zh-CN, and any others present at implementation time)

### Files explicitly NOT touched
- `SensitiveTextSanitizer.cs`
- `DebugLogService.cs`, `CrashLogService.cs` (read paths unchanged; only callers change)
- `AppDataPaths.cs` (no new folders; `crash-report-seen.marker` goes in existing `CrashLogFolder`)
- Release/build scripts, manifests, package layout
- `VrcTwitchOscBridge.slnx`
- Public repo / README / CHANGELOG (release-time workflow)

## Rollout prerequisites

Before the worker's category labels can appear on issues, create these labels in `seluvia/crystal-relay-public` (one-time manual step):
`connection`, `rewards`, `scaling`, `movement`, `ui-theme`, `crash`.

The worker's 422-fallback retry means a missing label won't block reports, but the labels should exist for proper triage. Document this in the worker README update.

## Verification

- Build: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
- Localization audit (run via the build scripts' audit step, or directly during implementation).
- Worker: deploy via `wrangler deploy` from `cloudflare\bug-report-worker` (manual, user-initiated).
- Manual smoke test: open Report Bug, verify snapshot populates, toggle each diagnostic, open preview, send a test report, confirm GitHub issue renders all sections and the category label applies.
- Crash prompt smoke test: trigger a crash (or fake `latest-crash.txt` newer than `crash-report-seen.marker`), relaunch, verify prompt appears, verify "Not now" writes the marker and doesn't re-prompt.
