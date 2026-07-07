# Android Live Feedback Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Update the Android live feedback app to v0.2.0 with search, favorites, disliked, history, version badges, poll interval settings, session stats, and Twitch app integration.

**Architecture:** Add store classes (Favorites, Disliked, History, Stats) to `LiveMonitor.cs`, restructure `MainActivity.cs` with bottom tab navigation and per-tab UI builders. One heartbeat line change in the main app.

**Tech Stack:** .NET Android (Xamarin.Android-style), SharedPreferences for persistence, Android AlarmManager for background polling.

**Spec:** `docs/superpowers/specs/2026-07-06-android-live-feedback-update-design.md`

---

### Task 1: Main app heartbeat — include beta label in relay version

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs:17771`

- [ ] **Step 1: Swap AppVersion for GetAppUpdateVersion()**

In `MainWindowViewModel.cs` at line 17771, change the heartbeat to send the version with beta label:

```
            AppVersion,
```

→

```
            GetAppUpdateVersion(),
```

`GetAppUpdateVersion()` returns `"3.1.9"` for stable or `"3.1.9-beta5"` for beta builds. The worker stores whatever string it receives, so the dev tools will show the full version including beta info.

- [ ] **Step 2: Verify the edit is correct**

Read around the edit to confirm context:

`MainWindowViewModel.cs:17755-17773`

- [ ] **Step 3: Build to confirm no compilation error**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeds.

---

### Task 2: Android — Add FavoritesStore, DislikedStore, HistoryStore, StatsTracker classes

**Files:**
- Modify: `tools\private\crystal-relay-live-android\LiveMonitor.cs`

- [ ] **Step 1: Add LiveFavoritesStore class**

Add to `LiveMonitor.cs` after the last class, before EOF:

```csharp
internal static class LiveFavoritesStore
{
    private const string FavoritesKey = "favorite_keys";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static HashSet<string> GetFavorites(Context context)
    {
        var saved = GetPreferences(context).GetString(FavoritesKey, "[]") ?? "[]";
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(saved, JsonOptions);
            return list is not null
                ? new HashSet<string>(list, StringComparer.OrdinalIgnoreCase)
                : [];
        }
        catch
        {
            return [];
        }
    }

    public static bool IsFavorite(Context context, string key)
    {
        return GetFavorites(context).Contains(key);
    }

    public static bool Toggle(Context context, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var favorites = GetFavorites(context);
        if (!favorites.Add(key))
        {
            favorites.Remove(key);
            SaveFavorites(context, favorites);
            return false;
        }

        SaveFavorites(context, favorites);
        return true;
    }

    private static void SaveFavorites(Context context, HashSet<string> keys)
    {
        var json = JsonSerializer.Serialize(keys.ToList(), JsonOptions);
        GetPreferences(context).Edit()
            ?.PutString(FavoritesKey, json)
            ?.Apply();
    }

    private static ISharedPreferences GetPreferences(Context context)
    {
        return context.GetSharedPreferences("crystal_relay_live_watch", FileCreationMode.Private)!;
    }
}
```

- [ ] **Step 2: Add LiveDislikedStore class**

Add after `LiveFavoritesStore`:

```csharp
internal static class LiveDislikedStore
{
    private const string DislikedKey = "disliked_keys";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static HashSet<string> GetDisliked(Context context)
    {
        var saved = GetPreferences(context).GetString(DislikedKey, "[]") ?? "[]";
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(saved, JsonOptions);
            return list is not null
                ? new HashSet<string>(list, StringComparer.OrdinalIgnoreCase)
                : [];
        }
        catch
        {
            return [];
        }
    }

    public static bool IsDisliked(Context context, string key)
    {
        return GetDisliked(context).Contains(key);
    }

    public static bool Toggle(Context context, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var disliked = GetDisliked(context);
        if (!disliked.Add(key))
        {
            disliked.Remove(key);
            SaveDisliked(context, disliked);
            return false;
        }

        SaveDisliked(context, disliked);
        return true;
    }

    private static void SaveDisliked(Context context, HashSet<string> keys)
    {
        var json = JsonSerializer.Serialize(keys.ToList(), JsonOptions);
        GetPreferences(context).Edit()
            ?.PutString(DislikedKey, json)
            ?.Apply();
    }

    private static ISharedPreferences GetPreferences(Context context)
    {
        return context.GetSharedPreferences("crystal_relay_live_watch", FileCreationMode.Private)!;
    }
}
```

- [ ] **Step 3: Add LiveHistoryEntry record and LiveHistoryStore class**

Add after `LiveDislikedStore`:

```csharp
internal sealed record LiveHistoryEntry(
    string DisplayName,
    string TwitchUrl,
    string RelayVersion,
    string BuildChannel,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

internal static class LiveHistoryStore
{
    private const string HistoryKey = "history_entries";
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static List<LiveHistoryEntry> entries = [];
    private static bool loaded;

    public static IReadOnlyList<LiveHistoryEntry> GetEntries(Context context)
    {
        EnsureLoaded(context);
        return entries.OrderByDescending(e => e.LastSeenAt).ToList().AsReadOnly();
    }

    public static void Record(Context context, IReadOnlyList<LiveUserInfo> liveUsers, DateTimeOffset observedAt)
    {
        EnsureLoaded(context);
        foreach (var user in liveUsers)
        {
            var key = CreateKey(user.TwitchUrl, user.DisplayName);
            var existing = entries.FirstOrDefault(e =>
                string.Equals(CreateKey(e.TwitchUrl, e.DisplayName), key, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                var newLast = observedAt > existing.LastSeenAt ? observedAt : existing.LastSeenAt;
                entries[entries.IndexOf(existing)] = existing with
                {
                    DisplayName = user.DisplayName,
                    TwitchUrl = user.TwitchUrl,
                    RelayVersion = user.RelayVersion,
                    BuildChannel = user.BuildChannel,
                    FirstSeenAt = existing.FirstSeenAt > observedAt ? observedAt : existing.FirstSeenAt,
                    LastSeenAt = newLast
                };
            }
            else
            {
                entries.Add(new LiveHistoryEntry(
                    user.DisplayName,
                    user.TwitchUrl,
                    user.RelayVersion,
                    user.BuildChannel,
                    observedAt,
                    observedAt));
            }
        }

        Prune(context);
        Save(context);
    }

    public static void Prune(Context context)
    {
        var cutoff = DateTimeOffset.UtcNow - HistoryWindow;
        entries.RemoveAll(e => e.LastSeenAt < cutoff);
    }

    private static void EnsureLoaded(Context context)
    {
        if (loaded)
        {
            return;
        }

        var saved = GetPreferences(context).GetString(HistoryKey, string.Empty) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(saved))
        {
            try
            {
                var deserialized = JsonSerializer.Deserialize<List<LiveHistoryEntry>>(saved, JsonOptions);
                if (deserialized is not null)
                {
                    entries = deserialized;
                }
            }
            catch
            {
                entries = [];
            }
        }

        Prune(context);
        loaded = true;
    }

    private static void Save(Context context)
    {
        try
        {
            var json = JsonSerializer.Serialize(entries, JsonOptions);
            GetPreferences(context).Edit()?.PutString(HistoryKey, json)?.Apply();
        }
        catch
        {
            // history persistence is best-effort
        }
    }

    private static string CreateKey(string twitchUrl, string displayName)
    {
        return !string.IsNullOrWhiteSpace(twitchUrl) ? twitchUrl.Trim() : displayName.Trim();
    }

    private static ISharedPreferences GetPreferences(Context context)
    {
        return context.GetSharedPreferences("crystal_relay_live_watch", FileCreationMode.Private)!;
    }
}
```

- [ ] **Step 4: Add LiveStatsTracker class**

Add after `LiveHistoryStore`:

```csharp
internal sealed class LiveStatsTracker
{
    private readonly HashSet<string> uniqueKeys = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset SessionStartedAt { get; } = DateTimeOffset.UtcNow;
    public int PeakLive { get; private set; }
    public int CurrentLive { get; private set; }
    public int UniqueStreamersSeen => uniqueKeys.Count;
    public int AlertsTriggered { get; set; }

    public void RecordSnapshot(int liveCount, IEnumerable<string>? liveKeys = null)
    {
        CurrentLive = liveCount;
        if (liveCount > PeakLive)
        {
            PeakLive = liveCount;
        }

        if (liveKeys is not null)
        {
            foreach (var key in liveKeys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    uniqueKeys.Add(key);
                }
            }
        }
    }

    public TimeSpan SessionDuration => DateTimeOffset.UtcNow - SessionStartedAt;
}
```

- [ ] **Step 5: Verify LiveMonitor.cs compiles**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-android\CrystalRelayLiveAndroid.csproj" --no-restore
```
Expected: Build succeeds.

---

### Task 3: Android — Add poll interval settings and update LiveMonitorClient

**Files:**
- Modify: `tools\private\crystal-relay-live-android\LiveMonitor.cs`

- [ ] **Step 1: Add poll interval constants and settings**

Add to `LiveWatchConstants`:
```csharp
public const string PollIntervalNormal = "normal";
public const string PollIntervalFast = "fast";
public const long FastPollIntervalMillis = 30 * 1000L;
```

Add to `LiveSettings`:
```csharp
public static string GetPollInterval(Context context)
{
    return Preferences(context).GetString("poll_interval", LiveWatchConstants.PollIntervalNormal)
        ?? LiveWatchConstants.PollIntervalNormal;
}

public static void SavePollInterval(Context context, string interval)
{
    Preferences(context).Edit()?.PutString("poll_interval", interval)?.Apply();
}

public static long GetPollIntervalMillis(Context context)
{
    var interval = GetPollInterval(context);
    return interval == LiveWatchConstants.PollIntervalFast
        ? LiveWatchConstants.FastPollIntervalMillis
        : LiveWatchConstants.CheckIntervalMillis;
}
```

- [ ] **Step 2: Update LiveMonitorResult and LiveMonitorClient.CheckAsync to return history + stats**

Change `LiveMonitorResult` to include history and stats:
```csharp
internal sealed record LiveMonitorResult(
    IReadOnlyList<LiveUserInfo> Users,
    string StatusText,
    string LastUpdatedText,
    IReadOnlyList<LiveHistoryEntry> History,
    LiveStatsTracker Stats);
```

Update `LiveMonitorClient.CheckAsync` — after computing new users and saving snapshot, record history. Also track stats. The method signature stays the same but now returns enriched data. At the end of `CheckAsync`, add:

```csharp
LiveHistoryStore.Record(appContext, parsed.Users, DateTimeOffset.UtcNow);
stats.RecordSnapshot(parsed.Users.Count, parsed.Users.Select(CreateLiveUserKey));
```

And update the return to include history + stats:
```csharp
return new LiveMonitorResult(
    parsed.Users,
    status,
    $"Last updated: {updatedAt}",
    LiveHistoryStore.GetEntries(appContext),
    stats);
```

Also add a `stats` parameter to `LiveMonitorClient` class:
```csharp
public static async Task<LiveMonitorResult> CheckAsync(Context context, bool notifyOnNewLive, LiveStatsTracker stats)
```

Update the notification line count:
```csharp
if (notifyOnNewLive && newUsers.Length > 0)
{
    stats.AlertsTriggered += newUsers.Length;
    LiveNotificationService.ShowLiveNotification(appContext, newUsers, parsed.Users.Count);
}
```

- [ ] **Step 3: Remove IsFavorite/IsDisliked properties from LiveUserInfo**

Skip adding `IsFavorite`/`IsDisliked` to `LiveUserInfo` — the UI reads directly from `LiveFavoritesStore`/`LiveDislikedStore` instead. The existing `LiveUserInfo` record stays unchanged.

- [ ] **Step 4: Update LiveAlarmScheduler to use configurable interval**

Change `LiveAlarmScheduler.Schedule` to accept a context parameter and read the interval from settings:

```csharp
public static void Schedule(Context context)
{
    var manager = (AlarmManager?)context.GetSystemService(Context.AlarmService);
    if (manager is null)
    {
        return;
    }

    var pendingIntent = CreatePendingIntent(context);
    var interval = LiveSettings.GetPollIntervalMillis(context);
    manager.SetInexactRepeating(
        AlarmType.ElapsedRealtimeWakeup,
        SystemClock.ElapsedRealtime() + interval,
        interval,
        pendingIntent);
}
```

- [ ] **Step 5: Build**

```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-android\CrystalRelayLiveAndroid.csproj" --no-restore
```
Expected: Build succeeds.

---

### Task 4: Android — Restructure MainActivity with bottom tab navigation

**Files:**
- Modify: `tools\private\crystal-relay-live-android\MainActivity.cs`

This is the largest change. The existing single-scroll layout becomes a tab container with a bottom bar.

- [ ] **Step 1: Add tab state fields and constants**

Add to the class fields:
```csharp
private const int TabLive = 0;
private const int TabHistory = 1;
private const int TabSettings = 2;

private int currentTab = TabLive;
private LinearLayout? tabContentContainer;
private LinearLayout? tabBar;

// Live tab controls
private EditText? searchInput;
private Switch? favoritesFilterSwitch;
private Switch? showDislikedSwitch;
private LinearLayout? liveUsersPanel;
private TextView? liveStatusText;
private TextView? liveLastUpdatedText;

// History tab controls
private LinearLayout? historyPanel;

// Settings tab controls
private RadioGroup? pollIntervalGroup;
private TextView? statsStartedText;
private TextView? statsDurationText;
private TextView? statsPeakText;
private TextView? statsUniqueText;
private TextView? statsAlertsText;

private LiveStatsTracker statsTracker = new();
```

- [ ] **Step 2: Replace BuildUi() with tab-based layout**

Replace the `BuildUi()` and `BuildUi`-related methods. The new layout structure:

```csharp
private void BuildUi()
{
    Window?.SetStatusBarColor(Color.ParseColor("#080A13"));
    Window?.SetNavigationBarColor(Color.ParseColor("#080A13"));

    var rootLayout = new LinearLayout(this)
    {
        Orientation = Orientation.Vertical
    };
    rootLayout.SetBackgroundColor(Color.ParseColor("#080A13"));

    // Header (title area)
    var header = BuildHeader();
    rootLayout.AddView(header);

    // Tab content container (switches between tabs)
    tabContentContainer = new LinearLayout(this)
    {
        Orientation = Orientation.Vertical
    };
    tabContentContainer.LayoutParameters = new LinearLayout.LayoutParams(
        ViewGroup.LayoutParams.MatchParent, 0, 1f);
    rootLayout.AddView(tabContentContainer);

    // Bottom tab bar
    tabBar = BuildTabBar();
    rootLayout.AddView(tabBar);

    ApplySafeAreaPadding(rootLayout);
    SetContentView(rootLayout);

    SelectTab(TabLive);
}

private LinearLayout BuildHeader()
{
    var header = new LinearLayout(this)
    {
        Orientation = Orientation.Vertical
    };
    ApplySafeAreaPadding(header);

    var title = MakeText("Crystal Relay Live Watch", 28, "#F8F4FF", TypefaceStyle.Bold);
    header.AddView(title);

    var subtitle = MakeText("Void Crystal phone heartbeat monitor", 14, "#CDBEEE", TypefaceStyle.Normal);
    subtitle.SetPadding(0, Dp(4), 0, Dp(14));
    header.AddView(subtitle);

    return header;
}

private LinearLayout BuildTabBar()
{
    var bar = new LinearLayout(this)
    {
        Orientation = Orientation.Horizontal
    };
    bar.SetBackgroundColor(Color.ParseColor("#0D0F1A"));
    bar.SetPadding(Dp(8), Dp(6), Dp(8), Dp(6));

    var liveTab = MakeTabButton("Live", TabLive);
    var historyTab = MakeTabButton("History", TabHistory);
    var settingsTab = MakeTabButton("Settings", TabSettings);

    bar.AddView(liveTab, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
    bar.AddView(historyTab, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
    bar.AddView(settingsTab, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

    return bar;
}

private TextView MakeTabButton(string text, int tabId)
{
    var button = MakeText(text, 14, "#CDBEEE", TypefaceStyle.Bold);
    button.Gravity = GravityFlags.Center;
    button.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
    button.Click += (_, _) => SelectTab(tabId);
    return button;
}

private void SelectTab(int tab)
{
    currentTab = tab;
    tabContentContainer!.RemoveAllViews();

    // Update tab button styling
    for (int i = 0; i < tabBar!.ChildCount; i++)
    {
        var child = tabBar.GetChildAt(i);
        if (child is TextView tv)
        {
            tv.SetTextColor(Color.ParseColor(i == tab ? "#7DF9FF" : "#CDBEEE"));
        }
    }

    switch (tab)
    {
        case TabLive:
            BuildLiveTab();
            break;
        case TabHistory:
            BuildHistoryTab();
            break;
        case TabSettings:
            BuildSettingsTab();
            break;
    }
}
```

- [ ] **Step 3: Remove old BuildUi content and update OnCreate**

Remove the old inline UI building from `BuildUi()` (the ScrollView-based layout). Keep the helper methods (`MakeCard`, `MakeText`, `MakeButton`, `Rounded`, `MatchWrap`, `WeightedRowButton`, `Dp`, `ApplySafeAreaPadding`, `GetSystemBarDimension`, `SafeAreaInsetListener`, `HideKeyboard`, `RequestNotificationPermissionIfNeeded`).

Update `OnCreate` to remove `LoadSettingsIntoUi()` — the Settings tab now builds controls with current values directly:

```csharp
protected override void OnCreate(Bundle? savedInstanceState)
{
    RequestWindowFeature(WindowFeatures.NoTitle);
    base.OnCreate(savedInstanceState);
    ActionBar?.Hide();

    LiveNotificationService.EnsureChannel(this);
    BuildUi();
    RequestNotificationPermissionIfNeeded();

    if (LiveSettings.GetNotificationsEnabled(this))
    {
        LiveAlarmScheduler.Schedule(this);
    }

    _ = RefreshLiveListAsync(false);
}
```

Remove `LoadSettingsIntoUi()` method entirely — each tab now initializes controls with current settings when built.

- [ ] **Step 4: Build**

```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-android\CrystalRelayLiveAndroid.csproj" --no-restore
```
Expected: Build succeeds.

---

### Task 5: Android — Implement Live tab with search, badges, favorites

**Files:**
- Modify: `tools\private\crystal-relay-live-android\MainActivity.cs`

- [ ] **Step 1: Add BuildLiveTab method**

```csharp
private void BuildLiveTab()
{
    var container = new ScrollView(this)
    {
        FillViewport = true
    };
    var inner = new LinearLayout(this)
    {
        Orientation = Orientation.Vertical
    };
    container.AddView(inner);
    container.SetPadding(Dp(18), 0, Dp(18), 0);

    // Search bar
    searchInput = new EditText(this)
    {
        InputType = InputTypes.ClassText,
        TextSize = 13
    };
    searchInput.SetHint("Search streamers...");
    searchInput.SetSingleLine(true);
    searchInput.SetTextColor(Color.ParseColor("#F8F4FF"));
    searchInput.SetHintTextColor(Color.ParseColor("#7DCDBEEE"));
    searchInput.SetPadding(Dp(10), Dp(8), Dp(10), Dp(8));
    searchInput.Background = Rounded("#24141830", "#55436A93", 10, 1);
    searchInput.TextChanged += (_, _) => RefreshLiveUserCards();
    inner.AddView(searchInput);

    // Favorites filter toggle
    favoritesFilterSwitch = new Switch(this)
    {
        Text = "Favorites only",
        TextSize = 14,
        ShowText = false
    };
    favoritesFilterSwitch.SetTextColor(Color.ParseColor("#F8F4FF"));
    favoritesFilterSwitch.SetPadding(0, Dp(10), 0, Dp(6));
    favoritesFilterSwitch.CheckedChange += (_, _) => RefreshLiveUserCards();
    inner.AddView(favoritesFilterSwitch);

    // Show disliked toggle
    showDislikedSwitch = new Switch(this)
    {
        Text = "Show disliked",
        TextSize = 14,
        ShowText = false
    };
    showDislikedSwitch.SetTextColor(Color.ParseColor("#F8F4FF"));
    showDislikedSwitch.SetPadding(0, Dp(2), 0, Dp(6));
    showDislikedSwitch.CheckedChange += (_, _) => RefreshLiveUserCards();
    inner.AddView(showDislikedSwitch);

    // Status text
    liveStatusText = MakeText("Loading live users...", 16, "#E6F9FF", TypefaceStyle.Bold);
    liveStatusText.SetPadding(0, Dp(10), 0, Dp(4));
    inner.AddView(liveStatusText);

    liveLastUpdatedText = MakeText("Not refreshed yet.", 12, "#CDBEEE", TypefaceStyle.Normal);
    liveLastUpdatedText.SetPadding(0, 0, 0, Dp(8));
    inner.AddView(liveLastUpdatedText);

    // Live users panel
    liveUsersPanel = new LinearLayout(this)
    {
        Orientation = Orientation.Vertical
    };
    inner.AddView(liveUsersPanel);

    tabContentContainer!.AddView(container);
    RenderCachedLiveUsers();
}
```

- [ ] **Step 2: Add RenderCachedLiveUsers and RefreshLiveUserCards methods**

```csharp
private List<LiveUserInfo> cachedLiveUsers = [];

private void RenderCachedLiveUsers()
{
    if (liveUsersPanel is null)
    {
        return;
    }

    RefreshLiveUserCards();
}

private void RefreshLiveUserCards()
{
    if (liveUsersPanel is null || searchInput is null)
    {
        return;
    }

    var searchText = searchInput.Text?.Trim() ?? string.Empty;
    var favoritesOnly = favoritesFilterSwitch?.Checked ?? false;
    var showDisliked = showDislikedSwitch?.Checked ?? false;
    var favorites = LiveFavoritesStore.GetFavorites(this);
    var disliked = LiveDislikedStore.GetDisliked(this);

    var filtered = cachedLiveUsers
        .Where(u => string.IsNullOrWhiteSpace(searchText)
            || u.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
        .Where(u => !favoritesOnly || favorites.Contains(CreateLiveUserKey(u)))
        .Where(u => showDisliked || !disliked.Contains(CreateLiveUserKey(u)))
        .ToList();

    liveUsersPanel.RemoveAllViews();

    if (filtered.Count == 0)
    {
        var empty = MakeCard();
        empty.SetGravity(GravityFlags.Center);
        empty.SetMinimumHeight(Dp(120));
        var message = favoritesOnly
            ? "No favorites live right now"
            : "No live users right now";
        empty.AddView(MakeText(message, 17, "#F8F4FF", TypefaceStyle.Bold));
        liveUsersPanel.AddView(empty, MatchWrap());
        return;
    }

    foreach (var user in filtered)
    {
        var isFav = favorites.Contains(CreateLiveUserKey(user));
        var isDis = disliked.Contains(CreateLiveUserKey(user));
        var card = BuildUserCard(user, isFav, isDis);
        liveUsersPanel.AddView(card, MatchWrap(0, 0, 0, Dp(12)));
    }
}

private string CreateLiveUserKey(LiveUserInfo user)
{
    return !string.IsNullOrWhiteSpace(user.TwitchUrl)
        ? user.TwitchUrl.Trim()
        : user.DisplayName.Trim();
}
```

- [ ] **Step 3: Add BuildUserCard method with version/channel badges and favorite star**

```csharp
private LinearLayout BuildUserCard(LiveUserInfo user, bool isFavorite, bool isDisliked)
{
    var card = MakeCard();
    card.Orientation = Orientation.Vertical;

    var isDis = isDisliked;
    var textColor = isDis ? "#7D6B84" : "#F8F4FF";
    var detailColor = isDis ? "#5D4B64" : "#CDBEEE";

    // Top row: star + name + LIVE pill
    var topRow = new LinearLayout(this)
    {
        Orientation = Orientation.Horizontal
    };
    topRow.SetGravity(GravityFlags.CenterVertical);
    card.AddView(topRow);

    // Favorite star
    var starText = isFavorite ? "\u2605" : "\u2606";
    var starColor = isFavorite ? "#FFD700" : "#CDBEEE";
    var star = MakeText(starText, 22, starColor, TypefaceStyle.Normal);
    star.SetPadding(0, 0, Dp(8), 0);
    star.Click += (_, _) =>
    {
        var key = CreateLiveUserKey(user);
        var nowFav = LiveFavoritesStore.Toggle(this, key);
        RefreshLiveUserCards();
    };
    topRow.AddView(star);

    var name = MakeText(user.DisplayName, 20, textColor, TypefaceStyle.Bold);
    topRow.AddView(name, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

    var livePill = MakeText("LIVE", 11, "#130A1B", TypefaceStyle.Bold);
    livePill.Gravity = GravityFlags.Center;
    livePill.SetPadding(Dp(10), Dp(4), Dp(10), Dp(4));
    livePill.Background = Rounded("#B8FF78D8", "#D97DF9FF", 999, 1);
    topRow.AddView(livePill);

    // Badges row: version + channel pills
    var badgesRow = new LinearLayout(this)
    {
        Orientation = Orientation.Horizontal
    };
    badgesRow.SetPadding(0, Dp(8), 0, Dp(6));

    if (!string.IsNullOrWhiteSpace(user.RelayVersion))
    {
        var versionPill = MakeText(user.RelayVersion, 11, "#F8F4FF", TypefaceStyle.Bold);
        versionPill.Gravity = GravityFlags.Center;
        versionPill.SetPadding(Dp(8), Dp(3), Dp(8), Dp(3));
        versionPill.Background = Rounded("#33243344", "#6643693A", 999, 1);
        badgesRow.AddView(versionPill);
        badgesRow.AddView(new View(this) { LayoutParameters = new LinearLayout.LayoutParams(Dp(6), 0) });
    }

    if (!string.IsNullOrWhiteSpace(user.BuildChannel))
    {
        var channelPill = MakeText(user.BuildChannel, 11, "#130A1B", TypefaceStyle.Bold);
        channelPill.Gravity = GravityFlags.Center;
        channelPill.SetPadding(Dp(8), Dp(3), Dp(8), Dp(3));
        var channelColor = user.BuildChannel.Equals("beta", StringComparison.OrdinalIgnoreCase)
            ? "#Ffe77400" : user.BuildChannel.Equals("test", StringComparison.OrdinalIgnoreCase)
            ? "#Ffcc4444" : "#B37DF9FF";
        channelPill.Background = Rounded(channelColor, "#D97DF9FF", 999, 1);
        badgesRow.AddView(channelPill);
    }

    card.AddView(badgesRow);

    // Twitch URL
    var url = MakeText(user.TwitchUrl, 13, detailColor, TypefaceStyle.Bold);
    url.SetPadding(0, Dp(4), 0, Dp(4));
    card.AddView(url);

    // Last heartbeat
    var details = new List<string>();
    if (user.LastPingAt is { } lastPing)
    {
        details.Add($"Last heartbeat {lastPing.ToLocalTime():g}");
    }

    if (details.Count > 0)
    {
        var detailText = MakeText(string.Join(" | ", details), 12, detailColor, TypefaceStyle.Normal);
        card.AddView(detailText);
    }

    // Tap card → open Twitch app
    card.Click += (_, _) => OpenTwitchStream(user.TwitchUrl);

    // Long-press star → toggle disliked
    star.LongClick += (_, _) =>
    {
        var key = CreateLiveUserKey(user);
        LiveDislikedStore.Toggle(this, key);
        RefreshLiveUserCards();
    };

    return card;
}

private void OpenTwitchStream(string twitchUrl)
{
    try
    {
        // Try to extract channel name from URL
        var channel = twitchUrl.TrimEnd('/');
        var lastSlash = channel.LastIndexOf('/');
        if (lastSlash >= 0)
        {
            channel = channel[(lastSlash + 1)..];
        }

        var twitchIntent = new Intent(Intent.ActionView,
            Android.Net.Uri.Parse($"twitch://stream/{channel}"));
        StartActivity(twitchIntent);
    }
    catch
    {
        // Fall back to browser
        var browserIntent = new Intent(Intent.ActionView,
            Android.Net.Uri.Parse(twitchUrl));
        StartActivity(browserIntent);
    }
}
```

- [ ] **Step 4: Update OnCreate to pass statsTracker to CheckAsync**

```csharp
_ = RefreshLiveListAsync(false);
```
Change to accept the stats tracker reference. Update the `RefreshLiveListAsync` method:

```csharp
private async Task RefreshLiveListAsync(bool notifyOnNewLive)
{
    ...
    var result = await LiveMonitorClient.CheckAsync(this, notifyOnNewLive, statsTracker);
    ...
}
```

And update the result handling to cache users:
```csharp
cachedLiveUsers = result.Users.ToList();
RenderCachedLiveUsers();
```

- [ ] **Step 5: Build**

```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-android\CrystalRelayLiveAndroid.csproj" --no-restore
```
Expected: Build succeeds.

---

### Task 6: Android — Implement History tab

**Files:**
- Modify: `tools\private\crystal-relay-live-android\MainActivity.cs`

- [ ] **Step 1: Add BuildHistoryTab method**

```csharp
private void BuildHistoryTab()
{
    var container = new ScrollView(this)
    {
        FillViewport = true
    };
    var inner = new LinearLayout(this)
    {
        Orientation = Orientation.Vertical
    };
    container.AddView(inner);
    container.SetPadding(Dp(18), 0, Dp(18), 0);

    var title = MakeText("History (24h)", 18, "#F8F4FF", TypefaceStyle.Bold);
    title.SetPadding(0, Dp(4), 0, Dp(12));
    inner.AddView(title);

    var entries = LiveHistoryStore.GetEntries(this);
    if (entries.Count == 0)
    {
        var empty = MakeCard();
        empty.SetGravity(GravityFlags.Center);
        empty.SetMinimumHeight(Dp(120));
        empty.AddView(MakeText("No history yet", 17, "#F8F4FF", TypefaceStyle.Bold));
        var emptySub = MakeText("Users you observe will appear here.", 13, "#CDBEEE", TypefaceStyle.Normal);
        emptySub.SetPadding(0, Dp(6), 0, 0);
        empty.AddView(emptySub);
        inner.AddView(empty, MatchWrap());
        tabContentContainer!.AddView(container);
        return;
    }

    foreach (var entry in entries)
    {
        var card = BuildHistoryCard(entry);
        inner.AddView(card, MatchWrap(0, 0, 0, Dp(12)));
    }

    tabContentContainer!.AddView(container);
}
```

- [ ] **Step 2: Add BuildHistoryCard method**

```csharp
private LinearLayout BuildHistoryCard(LiveHistoryEntry entry)
{
    var card = MakeCard();
    card.Orientation = Orientation.Vertical;

    var name = MakeText(entry.DisplayName, 18, "#F8F4FF", TypefaceStyle.Bold);
    card.AddView(name);

    // Badges row
    var badgesRow = new LinearLayout(this)
    {
        Orientation = Orientation.Horizontal
    };
    badgesRow.SetPadding(0, Dp(6), 0, Dp(6));

    if (!string.IsNullOrWhiteSpace(entry.RelayVersion))
    {
        var versionPill = MakeText(entry.RelayVersion, 11, "#F8F4FF", TypefaceStyle.Bold);
        versionPill.Gravity = GravityFlags.Center;
        versionPill.SetPadding(Dp(8), Dp(3), Dp(8), Dp(3));
        versionPill.Background = Rounded("#33243344", "#6643693A", 999, 1);
        badgesRow.AddView(versionPill);
        badgesRow.AddView(new View(this) { LayoutParameters = new LinearLayout.LayoutParams(Dp(6), 0) });
    }

    if (!string.IsNullOrWhiteSpace(entry.BuildChannel))
    {
        var channelPill = MakeText(entry.BuildChannel, 11, "#130A1B", TypefaceStyle.Bold);
        channelPill.Gravity = GravityFlags.Center;
        channelPill.SetPadding(Dp(8), Dp(3), Dp(8), Dp(3));
        var channelColor = entry.BuildChannel.Equals("beta", StringComparison.OrdinalIgnoreCase)
            ? "#Ffe77400" : entry.BuildChannel.Equals("test", StringComparison.OrdinalIgnoreCase)
            ? "#Ffcc4444" : "#B37DF9FF";
        channelPill.Background = Rounded(channelColor, "#D97DF9FF", 999, 1);
        badgesRow.AddView(channelPill);
    }

    card.AddView(badgesRow);

    var firstSeen = MakeText($"First seen: {entry.FirstSeenAt.ToLocalTime():g}", 12, "#CDBEEE", TypefaceStyle.Normal);
    card.AddView(firstSeen);

    var lastSeen = MakeText($"Last seen: {entry.LastSeenAt.ToLocalTime():g}", 12, "#CDBEEE", TypefaceStyle.Normal);
    card.AddView(lastSeen);

    // Tap → open Twitch app
    card.Click += (_, _) => OpenTwitchStream(entry.TwitchUrl);

    return card;
}
```

- [ ] **Step 3: Build**

```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-android\CrystalRelayLiveAndroid.csproj" --no-restore
```
Expected: Build succeeds.

---

### Task 7: Android — Implement Settings tab with poll interval and stats

**Files:**
- Modify: `tools\private\crystal-relay-live-android\MainActivity.cs`

- [ ] **Step 1: Add BuildSettingsTab method**

```csharp
private void BuildSettingsTab()
{
    var container = new ScrollView(this)
    {
        FillViewport = true
    };
    var inner = new LinearLayout(this)
    {
        Orientation = Orientation.Vertical
    };
    container.AddView(inner);
    container.SetPadding(Dp(18), 0, Dp(18), 0);

    // Endpoint card
    var endpointCard = MakeCard();
    endpointCard.Orientation = Orientation.Vertical;
    inner.AddView(endpointCard, MatchWrap(0, 0, 0, Dp(14)));

    endpointCard.AddView(MakeText("Live endpoint", 12, "#CDBEEE", TypefaceStyle.Bold));

    endpointInput = new EditText(this)
    {
        InputType = InputTypes.ClassText | InputTypes.TextVariationUri,
        TextSize = 13,
        Text = LiveSettings.GetEndpoint(this)
    };
    endpointInput.SetSingleLine(true);
    endpointInput.SetTextColor(Color.ParseColor("#F8F4FF"));
    endpointInput.SetHintTextColor(Color.ParseColor("#7DCDBEEE"));
    endpointInput.SetPadding(Dp(10), Dp(8), Dp(10), Dp(8));
    endpointInput.Background = Rounded("#24141830", "#55436A93", 10, 1);
    endpointCard.AddView(endpointInput, MatchWrap(0, Dp(6), 0, Dp(10)));

    var buttonRow = new LinearLayout(this)
    {
        Orientation = Orientation.Horizontal
    };
    endpointCard.AddView(buttonRow);

    saveButton = MakeButton("Save");
    saveButton.Click += (_, _) => SaveEndpoint();
    buttonRow.AddView(saveButton, WeightedRowButton(0, 0, Dp(6), 0));

    refreshButton = MakeButton("Refresh");
    refreshButton.Click += async (_, _) => await RefreshLiveListAsync(false);
    buttonRow.AddView(refreshButton, WeightedRowButton(Dp(6), 0, 0, 0));

    // Alerts toggle
    alertsSwitch = new Switch(this)
    {
        Text = "Phone live alerts",
        TextSize = 15,
        ShowText = false,
        Checked = LiveSettings.GetNotificationsEnabled(this)
    };
    alertsSwitch.SetTextColor(Color.ParseColor("#F8F4FF"));
    alertsSwitch.SetPadding(0, Dp(12), 0, 0);
    alertsSwitch.CheckedChange += (_, args) =>
    {
        if (isLoadingSettings) return;
        LiveSettings.SaveNotificationsEnabled(this, args.IsChecked);
        if (args.IsChecked)
        {
            RequestNotificationPermissionIfNeeded();
            LiveAlarmScheduler.Schedule(this);
        }
        else
        {
            LiveAlarmScheduler.Cancel(this);
        }
    };
    endpointCard.AddView(alertsSwitch);

    // Poll interval card
    var pollCard = MakeCard();
    pollCard.Orientation = Orientation.Vertical;
    inner.AddView(pollCard, MatchWrap(0, 0, 0, Dp(14)));

    pollCard.AddView(MakeText("Poll interval", 12, "#CDBEEE", TypefaceStyle.Bold));

    pollIntervalGroup = new RadioGroup(this);
    pollIntervalGroup.SetPadding(0, Dp(4), 0, 0);

    var currentInterval = LiveSettings.GetPollInterval(this);

    var pollNormalRadio = new RadioButton(this)
    {
        Text = "Normal (~15 min)",
        TextSize = 14,
        Checked = currentInterval == LiveWatchConstants.PollIntervalNormal
    };
    pollNormalRadio.SetTextColor(Color.ParseColor("#F8F4FF"));
    pollNormalRadio.Id = 1;
    pollIntervalGroup.AddView(pollNormalRadio);

    var pollFastRadio = new RadioButton(this)
    {
        Text = "Fast (~30 sec, while app is open)",
        TextSize = 14,
        Checked = currentInterval == LiveWatchConstants.PollIntervalFast
    };
    pollFastRadio.SetTextColor(Color.ParseColor("#F8F4FF"));
    pollFastRadio.Id = 2;
    pollIntervalGroup.AddView(pollFastRadio);

    pollIntervalGroup.CheckedChange += (_, args) =>
    {
        var interval = args.CheckedId == 2
            ? LiveWatchConstants.PollIntervalFast
            : LiveWatchConstants.PollIntervalNormal;
        LiveSettings.SavePollInterval(this, interval);
        LiveAlarmScheduler.Schedule(this);
    };

    pollCard.AddView(pollIntervalGroup);

    var cadence = MakeText(
        $"Background checks are roughly every {LiveWatchConstants.CheckIntervalMinutes} minutes. " +
        "Android may delay them in battery saver or deep sleep.",
        12, "#CDBEEE", TypefaceStyle.Normal);
    cadence.SetPadding(0, Dp(8), 0, 0);
    pollCard.AddView(cadence);

    // Stats card
    var statsCard = MakeCard();
    statsCard.Orientation = Orientation.Vertical;
    inner.AddView(statsCard, MatchWrap(0, 0, 0, Dp(14)));

    statsCard.AddView(MakeText("Session stats", 12, "#CDBEEE", TypefaceStyle.Bold));

    statsStartedText = MakeText($"Started: {statsTracker.SessionStartedAt.ToLocalTime():g}", 14, "#F8F4FF", TypefaceStyle.Normal);
    statsStartedText.SetPadding(0, Dp(8), 0, 0);
    statsCard.AddView(statsStartedText);

    statsDurationText = MakeText("", 14, "#F8F4FF", TypefaceStyle.Normal);
    statsCard.AddView(statsDurationText);

    statsPeakText = MakeText($"Peak live: {statsTracker.PeakLive}", 14, "#F8F4FF", TypefaceStyle.Normal);
    statsCard.AddView(statsPeakText);

    statsUniqueText = MakeText($"Unique streamers seen: {statsTracker.UniqueStreamersSeen}", 14, "#F8F4FF", TypefaceStyle.Normal);
    statsCard.AddView(statsUniqueText);

    statsAlertsText = MakeText($"Alerts triggered: {statsTracker.AlertsTriggered}", 14, "#F8F4FF", TypefaceStyle.Normal);
    statsCard.AddView(statsAlertsText);

    // Status card
    var statusCard = MakeCard();
    statusCard.Orientation = Orientation.Vertical;
    inner.AddView(statusCard, MatchWrap(0, 0, 0, Dp(14)));

    statusText = MakeText("Ready.", 16, "#E6F9FF", TypefaceStyle.Bold);
    statusCard.AddView(statusText);

    lastUpdatedText = MakeText("Not refreshed yet.", 12, "#CDBEEE", TypefaceStyle.Normal);
    lastUpdatedText.SetPadding(0, Dp(6), 0, 0);
    statusCard.AddView(lastUpdatedText);

    tabContentContainer!.AddView(container);
    StartStatsTimer();
}

private void StartStatsTimer()
{
    var timer = new Java.Util.Timer();
    timer.ScheduleAtFixedRate(new StatsUpdateTask(this), 0, 10000);
}

private sealed class StatsUpdateTask(MainActivity activity) : Java.Util.TimerTask
{
    public override void Run()
    {
        activity.RunOnUiThread(() => activity.UpdateStatsDisplay());
    }
}

private void UpdateStatsDisplay()
{
    if (statsDurationText is not null)
    {
        statsDurationText.Text = $"Duration: {(int)statsTracker.SessionDuration.TotalHours}h {statsTracker.SessionDuration.Minutes}m";
    }

    if (statsPeakText is not null)
    {
        statsPeakText.Text = $"Peak live: {statsTracker.PeakLive}";
    }

    if (statsUniqueText is not null)
    {
        statsUniqueText.Text = $"Unique streamers seen: {statsTracker.UniqueStreamersSeen}";
    }

    if (statsAlertsText is not null)
    {
        statsAlertsText.Text = $"Alerts triggered: {statsTracker.AlertsTriggered}";
    }
}
```

- [ ] **Step 2: Update SaveEndpoint to refresh cached live list**

```csharp
private void SaveEndpoint()
{
    HideKeyboard();
    var endpoint = endpointInput?.Text?.Trim() ?? string.Empty;
    if (LiveEndpointTools.BuildLiveApiUri(endpoint) is null)
    {
        statusText!.Text = "That endpoint does not look valid.";
        return;
    }

    LiveSettings.SaveEndpoint(this, endpoint);
    LiveSettings.ResetSnapshot(this);
    LiveAlarmScheduler.Schedule(this);
    statusText!.Text = "Endpoint saved. Refreshing...";
    _ = RefreshLiveListAsync(false);
}
```

- [ ] **Step 3: Build**

```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-android\CrystalRelayLiveAndroid.csproj" --no-restore
```
Expected: Build succeeds.

---

### Task 8: Android — Fast poll resume/pause and Twitch app deep link

**Files:**
- Modify: `tools\private\crystal-relay-live-android\MainActivity.cs`

- [ ] **Step 1: Add OnResume/OnPause for fast poll behavior**

```csharp
private Java.Util.Timer? fastPollTimer;

protected override void OnResume()
{
    base.OnResume();

    if (LiveSettings.GetPollInterval(this) == LiveWatchConstants.PollIntervalFast)
    {
        StartFastPoll();
    }
}

protected override void OnPause()
{
    base.OnPause();
    StopFastPoll();

    // On pause, re-schedule AlarmManager at normal interval if fast was active
    if (LiveSettings.GetNotificationsEnabled(this))
    {
        LiveAlarmScheduler.Schedule(this);
    }
}

private void StartFastPoll()
{
    StopFastPoll();
    fastPollTimer = new Java.Util.Timer();
    fastPollTimer.ScheduleAtFixedRate(new FastPollTask(this), 0, LiveWatchConstants.FastPollIntervalMillis);

    // While app is visible with fast poll, also re-schedule AlarmManager at fast interval
    if (LiveSettings.GetNotificationsEnabled(this))
    {
        LiveAlarmScheduler.Schedule(this);
    }
}

private void StopFastPoll()
{
    fastPollTimer?.Cancel();
    fastPollTimer?.Dispose();
    fastPollTimer = null;
}

private sealed class FastPollTask(MainActivity activity) : Java.Util.TimerTask
{
    public override void Run()
    {
        activity.RunOnUiThread(async () =>
        {
            try
            {
                await activity.RefreshLiveListAsync(false);
            }
            catch
            {
                // Fast poll failures are silent
            }
        });
    }
}
```

- [ ] **Step 2: Ensure Twitch app intent works (verify OpenTwitchStream)**

The existing `OpenTwitchStream` method handles the `twitch://stream/<channel>` intent with browser fallback. Already implemented in Task 5.

- [ ] **Step 3: Build**

```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-android\CrystalRelayLiveAndroid.csproj" --no-restore
```
Expected: Build succeeds.

---

### Task 9: Android — Bump version, final build, and deploy

**Files:**
- Modify: `tools\private\crystal-relay-live-android\CrystalRelayLiveAndroid.csproj`

- [ ] **Step 1: Bump version to 0.2.0**

In `CrystalRelayLiveAndroid.csproj`:
```xml
<ApplicationVersion>5</ApplicationVersion>
<ApplicationDisplayVersion>0.2.0</ApplicationDisplayVersion>
```

- [ ] **Step 2: Full build for deployment**

```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-android\CrystalRelayLiveAndroid.csproj" -c Release
```
Expected: Build succeeds, APK generated.

- [ ] **Step 3: Deploy to connected Android device via USB**

```
dotnet install "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-android\CrystalRelayLiveAndroid.csproj" -c Release
```
Or use ADB directly if the installed `.apk` path is known:
```
adb install -r "path/to/bin/Release/net10.0-android/dev.seluvia.crystalrelay.livewatch.apk"
```

Expected: App installs on device. Open the app and verify tabs, search, favorites, history, badges, stats, and Twitch app integration work.
