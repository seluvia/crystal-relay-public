# Live Watch Version/Channel Badges Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add visually distinct version and build-channel badges to the Live Now cards in the Crystal Relay Live Watch dev tool.

**Architecture:** Modify the existing `LiveUserViewModel` to expose badge-ready properties, and update the Live Now card `DataTemplate` in `MainWindow.xaml` to bind those properties as styled pills. The 24h History cards remain untouched.

**Tech Stack:** C# WPF, XAML, .NET 10

---

### File Structure

| File | Responsibility |
|------|----------------|
| `tools/private/crystal-relay-live-list/MainWindow.xaml.cs` | `LiveUserViewModel` — add `VersionBadgeText`, `ChannelBadgeText`, and update `DetailText` |
| `tools/private/crystal-relay-live-list/MainWindow.xaml` | Live Now card `DataTemplate` — add badge `Border` elements inside the top row |

---

### Task 1: Update LiveUserViewModel Badge Properties

**Files:**
- Modify: `tools/private/crystal-relay-live-list/MainWindow.xaml.cs:1379-1424`

- [ ] **Step 1: Add `VersionBadgeText` and `ChannelBadgeText` properties**

In the `LiveUserViewModel` class (around line 1379), add two new readonly string properties after the existing properties:

```csharp
public string VersionBadgeText { get; }

public string ChannelBadgeText { get; }
```

- [ ] **Step 2: Update constructor to populate badges and simplify DetailText**

In the `LiveUserViewModel` constructor, set the new properties and change `DetailText` to only include the heartbeat timestamp:

Find this constructor block:
```csharp
public LiveUserViewModel(
    string displayName,
    string twitchUrl,
    string relayVersion,
    string buildChannel,
    DateTimeOffset? lastPingAt)
{
    DisplayName = displayName.Trim();
    TwitchUrl = twitchUrl.Trim();
    RelayVersion = relayVersion.Trim();
    BuildChannel = buildChannel.Trim();
    LastPingAt = lastPingAt?.ToUniversalTime();

    var details = new List<string>();
    if (!string.IsNullOrWhiteSpace(RelayVersion))
    {
        details.Add($"Crystal Relay {RelayVersion}");
    }

    if (!string.IsNullOrWhiteSpace(BuildChannel))
    {
        details.Add(BuildChannel);
    }

    if (LastPingAt is { } lastPing)
    {
        details.Add($"Last heartbeat {lastPing.ToLocalTime():g}");
    }

    DetailText = details.Count > 0 ? string.Join(" | ", details) : "Live heartbeat active.";
}
```

Replace it with:
```csharp
public LiveUserViewModel(
    string displayName,
    string twitchUrl,
    string relayVersion,
    string buildChannel,
    DateTimeOffset? lastPingAt)
{
    DisplayName = displayName.Trim();
    TwitchUrl = twitchUrl.Trim();
    RelayVersion = relayVersion.Trim();
    BuildChannel = buildChannel.Trim();
    LastPingAt = lastPingAt?.ToUniversalTime();
    VersionBadgeText = RelayVersion;
    ChannelBadgeText = BuildChannel;

    DetailText = LastPingAt is { } lastPing
        ? $"Last heartbeat {lastPing.ToLocalTime():g}"
        : "Live heartbeat active.";
}
```

- [ ] **Step 3: Build the dev tool to verify no compile errors**

Run:
```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-list\CrystalRelayLiveList.csproj" --no-restore
```

Expected: Build succeeds with 0 errors.

---

### Task 2: Add Badge UI to Live Now Card DataTemplate

**Files:**
- Modify: `tools/private/crystal-relay-live-list/MainWindow.xaml:796-818`

- [ ] **Step 1: Add badge `WrapPanel` inside the top row `Grid`**

Find the live card top row `Grid` (around line 796):
```xml
<Grid Grid.Row="0">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>
    <TextBlock Grid.Column="0"
               Text="{Binding DisplayName}"
               Foreground="{StaticResource TextBrush}"
               FontSize="19"
               FontWeight="Black"
               TextWrapping="Wrap" />
    <Border Grid.Column="1"
            Margin="10,0,0,0"
            Padding="8,4"
            VerticalAlignment="Top"
            Background="{StaticResource LivePillBrush}"
            CornerRadius="999">
        <TextBlock Text="LIVE"
                   Foreground="{StaticResource ButtonTextBrush}"
                   FontSize="10"
                   FontWeight="Bold" />
    </Border>
</Grid>
```

Replace it with:
```xml
<Grid Grid.Row="0">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>
    <TextBlock Grid.Column="0"
               Text="{Binding DisplayName}"
               Foreground="{StaticResource TextBrush}"
               FontSize="19"
               FontWeight="Black"
               TextWrapping="Wrap" />
    <WrapPanel Grid.Column="1"
               Margin="10,0,0,0"
               VerticalAlignment="Top">
        <Border Padding="6,3"
                Margin="0,0,6,0"
                Background="{StaticResource LivePillBrush}"
                CornerRadius="999"
                Visibility="{Binding VersionBadgeText, Converter={StaticResource StringToVisibilityConverter}}">
            <TextBlock Text="{Binding VersionBadgeText}"
                       Foreground="{StaticResource ButtonTextBrush}"
                       FontSize="10"
                       FontWeight="Bold" />
        </Border>
        <Border Padding="6,3"
                Background="{StaticResource LivePillBrush}"
                CornerRadius="999"
                Visibility="{Binding ChannelBadgeText, Converter={StaticResource StringToVisibilityConverter}}">
            <TextBlock Text="{Binding ChannelBadgeText}"
                       Foreground="{StaticResource ButtonTextBrush}"
                       FontSize="10"
                       FontWeight="Bold" />
        </Border>
    </WrapPanel>
    <Border Grid.Column="2"
            Margin="10,0,0,0"
            Padding="8,4"
            VerticalAlignment="Top"
            Background="{StaticResource LivePillBrush}"
            CornerRadius="999">
        <TextBlock Text="LIVE"
                   Foreground="{StaticResource ButtonTextBrush}"
                   FontSize="10"
                   FontWeight="Bold" />
    </Border>
</Grid>
```

- [ ] **Step 2: Verify `StringToVisibilityConverter` exists in the XAML resources**

Check that the `Window.Resources` section (earlier in the file) already contains `BoolToVisibilityConverter` and likely `StringToVisibilityConverter` or similar. If `StringToVisibilityConverter` is not present, add it inside `<Window.Resources>`:

```xml
< converters:StringToVisibilityConverter x:Key="StringToVisibilityConverter" />
```

Actually, WPF does not have a built-in `StringToVisibilityConverter`. Check if the project already defines one. If not, add a simple converter inside the `MainWindow.xaml.cs` file (or inline in XAML if supported). The simplest approach is to use a `TextBlock` with `Visibility="Collapsed"` bound to a boolean property, OR add a converter.

**Better approach:** Instead of a converter, we can use a `DataTrigger` on the `Border` that checks if the text is empty. But the simplest clean approach is to add a `BooleanToVisibilityConverter` and bind to a bool property on the view model.

**Simpler approach:** In `LiveUserViewModel`, add:
```csharp
public bool HasVersionBadge => !string.IsNullOrWhiteSpace(VersionBadgeText);
public bool HasChannelBadge => !string.IsNullOrWhiteSpace(ChannelBadgeText);
```

Then bind `Visibility` with `BoolToVisibilityConverter`:
```xml
Visibility="{Binding HasVersionBadge, Converter={StaticResource BoolToVisibilityConverter}}"
```

- [ ] **Step 3: Re-visit the XAML binding with bool properties**

If using the bool approach, update the `LiveUserViewModel` constructor to also set:
```csharp
HasVersionBadge = !string.IsNullOrWhiteSpace(RelayVersion);
HasChannelBadge = !string.IsNullOrWhiteSpace(BuildChannel);
```

And add properties:
```csharp
public bool HasVersionBadge { get; }
public bool HasChannelBadge { get; }
```

Then update the XAML `Border` elements to use:
```xml
Visibility="{Binding HasVersionBadge, Converter={StaticResource BoolToVisibilityConverter}}"
```

- [ ] **Step 4: Build the dev tool to verify no compile errors**

Run:
```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-list\CrystalRelayLiveList.csproj" --no-restore
```

Expected: Build succeeds with 0 errors.

---

### Task 3: Final Verification

- [ ] **Step 1: Verify the 24h History card template is untouched**

The history card `DataTemplate` (around line 919) should still show the original inline detail text. No changes there.

- [ ] **Step 2: Run a quick sanity check**

If the dev tool is runnable locally, launch it to confirm the Live Now card renders badges correctly. This requires a configured endpoint or local JSON file.

---

## Spec Coverage Check

| Spec Requirement | Plan Task |
|------------------|-----------|
| Live Now cards show version badge | Task 2 |
| Live Now cards show channel badge | Task 2 |
| Detail text only shows heartbeat | Task 1 |
| History cards unchanged | Task 3 verification |
| No new Cloudflare/app changes | Scope boundary |
| Build passes | Task 1 Step 3, Task 2 Step 4 |

## Placeholder Scan

- No TBDs, TODOs, or incomplete code blocks.
- All property names consistent across tasks.
- All file paths exact.
- Build commands included.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-07-livewatch-version-badges.md`. Two execution options:

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration
2. **Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?
