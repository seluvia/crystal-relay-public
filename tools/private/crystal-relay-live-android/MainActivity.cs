using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;

namespace CrystalRelayLiveAndroid;

[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true, Theme = "@style/CrystalRelayLiveTheme")]
public sealed class MainActivity : Activity
{
    private const int NotificationPermissionRequestCode = 4215;
    private const int ContentPaddingHorizontalDp = 18;
    private const int ContentPaddingTopDp = 18;
    private const int ContentPaddingBottomDp = 20;

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

    // Settings tab controls
    private RadioGroup? pollIntervalGroup;
    private TextView? statsStartedText;
    private TextView? statsDurationText;
    private TextView? statsPeakText;
    private TextView? statsUniqueText;
    private TextView? statsAlertsText;

    private LiveStatsTracker statsTracker = new();
    private List<LiveUserInfo> cachedLiveUsers = [];

    private Java.Util.Timer? fastPollTimer;

    private Button? refreshButton;
    private Switch? alertsSwitch;
    private TextView? statusText;
    private TextView? lastUpdatedText;

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

        if (LiveSettings.GetNotificationsEnabled(this))
        {
            LiveAlarmScheduler.Schedule(this);
        }
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode != NotificationPermissionRequestCode || statusText is null)
        {
            return;
        }

        statusText.Text = grantResults.Length > 0 && grantResults[0] == Permission.Granted
            ? "Phone alerts are enabled."
            : "Notification permission was not granted. The live list still works in the app.";
    }

    private void BuildUi()
    {
        Window?.SetStatusBarColor(Color.ParseColor("#080A13"));
        Window?.SetNavigationBarColor(Color.ParseColor("#080A13"));

        var rootLayout = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        rootLayout.SetBackgroundColor(Color.ParseColor("#080A13"));

        var header = BuildHeader();
        rootLayout.AddView(header);

        tabContentContainer = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        tabContentContainer.LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, 1f);
        rootLayout.AddView(tabContentContainer);

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

    private void BuildLiveTab()
    {
        var container = new ScrollView(this)
        {
            FillViewport = true,
            VerticalScrollBarEnabled = false
        };
        var inner = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        container.AddView(inner);
        container.SetPadding(Dp(18), 0, Dp(18), 0);

        searchInput = new EditText(this)
        {
            InputType = InputTypes.ClassText,
            TextSize = 13
        };
        searchInput.Hint = "Search streamers...";
        searchInput.SetSingleLine(true);
        searchInput.SetTextColor(Color.ParseColor("#F8F4FF"));
        searchInput.SetHintTextColor(Color.ParseColor("#7DCDBEEE"));
        searchInput.SetPadding(Dp(10), Dp(8), Dp(10), Dp(8));
        searchInput.Background = Rounded("#24141830", "#55436A93", 10, 1);
        searchInput.TextChanged += (_, _) => RefreshLiveUserCards();
        inner.AddView(searchInput);

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

        refreshButton = MakeButton("Refresh");
        refreshButton.Click += async (_, _) => await RefreshLiveListAsync(false);
        refreshButton.SetPadding(0, Dp(6), 0, Dp(2));
        inner.AddView(refreshButton, MatchWrap());

        liveStatusText = MakeText("Loading live users...", 16, "#E6F9FF", TypefaceStyle.Bold);
        liveStatusText.SetPadding(0, Dp(10), 0, Dp(4));
        inner.AddView(liveStatusText);

        liveLastUpdatedText = MakeText("Not refreshed yet.", 12, "#CDBEEE", TypefaceStyle.Normal);
        liveLastUpdatedText.SetPadding(0, 0, 0, Dp(8));
        inner.AddView(liveLastUpdatedText);

        liveUsersPanel = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        inner.AddView(liveUsersPanel);

        tabContentContainer!.AddView(container);
        RenderCachedLiveUsers();
    }

    private void BuildHistoryTab()
    {
        var container = new ScrollView(this)
        {
            FillViewport = true,
            VerticalScrollBarEnabled = false
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

    private LinearLayout BuildHistoryCard(LiveHistoryEntry entry)
    {
        var card = MakeCard();
        card.Orientation = Orientation.Vertical;

        var name = MakeText(entry.DisplayName, 18, "#F8F4FF", TypefaceStyle.Bold);
        card.AddView(name);

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
            var channelColor = entry.BuildChannel.StartsWith("beta", StringComparison.OrdinalIgnoreCase)
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

        card.Click += (_, _) => OpenTwitchStream(entry.TwitchUrl);

        return card;
    }

    private void BuildSettingsTab()
    {
        var container = new ScrollView(this)
        {
            FillViewport = true,
            VerticalScrollBarEnabled = false
        };
        var inner = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        container.AddView(inner);
        container.SetPadding(Dp(18), 0, Dp(18), 0);

        // Poll interval card
        var pollCard = MakeCard();
        pollCard.Orientation = Orientation.Vertical;
        inner.AddView(pollCard, MatchWrap(0, 0, 0, Dp(14)));

        pollCard.AddView(MakeText("Poll interval & alerts", 12, "#CDBEEE", TypefaceStyle.Bold));

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

        // Alerts toggle (below background checks text)
        alertsSwitch = new Switch(this)
        {
            Text = "Phone live alerts",
            TextSize = 15,
            ShowText = false,
            Checked = LiveSettings.GetNotificationsEnabled(this)
        };
        alertsSwitch.SetTextColor(Color.ParseColor("#F8F4FF"));
        alertsSwitch.SetPadding(0, Dp(8), 0, 0);
        alertsSwitch.CheckedChange += (_, args) =>
        {
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
        pollCard.AddView(alertsSwitch);

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

    private void StartFastPoll()
    {
        StopFastPoll();
        fastPollTimer = new Java.Util.Timer();
        fastPollTimer.ScheduleAtFixedRate(new FastPollTask(this), 0, LiveWatchConstants.FastPollIntervalMillis);

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

    private async Task RefreshLiveListAsync(bool notifyOnNewLive)
    {
        if (refreshButton is not null)
            refreshButton.Enabled = false;
        if (statusText is not null)
            statusText.Text = "Refreshing live list...";
        if (liveStatusText is not null)
            liveStatusText.Text = "Refreshing live list...";

        try
        {
            var result = await LiveMonitorClient.CheckAsync(this, notifyOnNewLive, statsTracker);
            cachedLiveUsers = result.Users.ToList();

            if (statusText is not null)
                statusText.Text = result.StatusText;
            if (lastUpdatedText is not null)
                lastUpdatedText.Text = result.LastUpdatedText;
            if (liveStatusText is not null)
                liveStatusText.Text = result.StatusText;
            if (liveLastUpdatedText is not null)
                liveLastUpdatedText.Text = result.LastUpdatedText;

            RenderCachedLiveUsers();
        }
        catch (Exception ex)
        {
            var errorText = $"Could not refresh: {ex.Message}";
            var attemptText = $"Last attempt: {DateTimeOffset.Now:g}";

            if (statusText is not null)
                statusText.Text = errorText;
            if (lastUpdatedText is not null)
                lastUpdatedText.Text = attemptText;
            if (liveStatusText is not null)
                liveStatusText.Text = errorText;
            if (liveLastUpdatedText is not null)
                liveLastUpdatedText.Text = attemptText;
        }
        finally
        {
            if (refreshButton is not null)
                refreshButton.Enabled = true;
        }
    }

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

    private LinearLayout BuildUserCard(LiveUserInfo user, bool isFavorite, bool isDisliked)
    {
        var card = MakeCard();
        card.Orientation = Orientation.Vertical;

        var isDis = isDisliked;
        var textColor = isDis ? "#7D6B84" : "#F8F4FF";
        var detailColor = isDis ? "#5D4B64" : "#CDBEEE";

        var topRow = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };
        topRow.SetGravity(GravityFlags.CenterVertical);
        card.AddView(topRow);

        var starText = isFavorite ? "\u2605" : "\u2606";
        var starColor = isFavorite ? "#FFD700" : "#CDBEEE";
        var star = MakeText(starText, 22, starColor, TypefaceStyle.Normal);
        star.SetPadding(0, 0, Dp(8), 0);
        star.Click += (_, _) =>
        {
            var key = CreateLiveUserKey(user);
            LiveFavoritesStore.Toggle(this, key);
            RefreshLiveUserCards();
        };
        topRow.AddView(star);

        // Dislike button (always visible)
        var dislikeIcon = MakeText(isDis ? "\u2298" : "\u2299", 18, isDis ? "#FF4444" : "#5D4B64", TypefaceStyle.Normal);
        dislikeIcon.SetPadding(0, 0, Dp(8), 0);
        dislikeIcon.Click += (_, _) =>
        {
            var key = CreateLiveUserKey(user);
            LiveDislikedStore.Toggle(this, key);
            RefreshLiveUserCards();
        };
        topRow.AddView(dislikeIcon);

        var name = MakeText(user.DisplayName, 20, textColor, TypefaceStyle.Bold);
        topRow.AddView(name, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var livePill = MakeText("LIVE", 11, "#130A1B", TypefaceStyle.Bold);
        livePill.Gravity = GravityFlags.Center;
        livePill.SetPadding(Dp(10), Dp(4), Dp(10), Dp(4));
        livePill.Background = Rounded("#B8FF78D8", "#D97DF9FF", 999, 1);
        topRow.AddView(livePill);

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
            var channelColor = user.BuildChannel.StartsWith("beta", StringComparison.OrdinalIgnoreCase)
                ? "#Ffe77400" : user.BuildChannel.Equals("test", StringComparison.OrdinalIgnoreCase)
                ? "#Ffcc4444" : "#B37DF9FF";
            channelPill.Background = Rounded(channelColor, "#D97DF9FF", 999, 1);
            badgesRow.AddView(channelPill);
        }

        card.AddView(badgesRow);

        var url = MakeText(user.TwitchUrl, 13, detailColor, TypefaceStyle.Bold);
        url.SetPadding(0, Dp(4), 0, Dp(4));
        card.AddView(url);

        if (user.LastPingAt is { } lastPing)
        {
            var detailText = MakeText($"Last heartbeat {lastPing.ToLocalTime():g}", 12, detailColor, TypefaceStyle.Normal);
            card.AddView(detailText);
        }

        card.Click += (_, _) => OpenTwitchStream(user.TwitchUrl);

        return card;
    }

    private void OpenTwitchStream(string twitchUrl)
    {
        try
        {
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
            var browserIntent = new Intent(Intent.ActionView,
                Android.Net.Uri.Parse(twitchUrl));
            StartActivity(browserIntent);
        }
    }

    private void RequestNotificationPermissionIfNeeded()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu
            || CheckSelfPermission(Manifest.Permission.PostNotifications) == Permission.Granted)
        {
            return;
        }

        RequestPermissions([Manifest.Permission.PostNotifications], NotificationPermissionRequestCode);
    }

    private void HideKeyboard()
    {
        var manager = (InputMethodManager?)GetSystemService(InputMethodService);
        manager?.HideSoftInputFromWindow(CurrentFocus?.WindowToken, HideSoftInputFlags.None);
    }

    private LinearLayout MakeCard()
    {
        var card = new LinearLayout(this);
        card.SetPadding(Dp(14), Dp(14), Dp(14), Dp(14));
        card.Background = Rounded("#D0111422", "#725E9EFF", 14, 1);
        return card;
    }

    private Button MakeButton(string text)
    {
        var button = new Button(this)
        {
            Text = text,
            TextSize = 14
        };
        button.SetTextColor(Color.ParseColor("#130A1B"));
        button.SetTypeface(Typeface.Default, TypefaceStyle.Bold);
        button.Background = Rounded("#CFF7B7FF", "#D97DF9FF", 12, 1);
        return button;
    }

    private TextView MakeText(string text, int sp, string color, TypefaceStyle style)
    {
        var view = new TextView(this)
        {
            Text = text,
            TextSize = sp
        };
        view.SetTextColor(Color.ParseColor(color));
        view.SetTypeface(Typeface.Default, style);
        return view;
    }

    private GradientDrawable Rounded(string fill, string stroke, int radiusDp, int strokeDp)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(Color.ParseColor(fill));
        drawable.SetCornerRadius(Dp(radiusDp));
        drawable.SetStroke(Dp(strokeDp), Color.ParseColor(stroke));
        return drawable;
    }

    private LinearLayout.LayoutParams MatchWrap(int left = 0, int top = 0, int right = 0, int bottom = 0)
    {
        var layout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent);
        layout.SetMargins(left, top, right, bottom);
        return layout;
    }

    private LinearLayout.LayoutParams WeightedRowButton(int left, int top, int right, int bottom)
    {
        var layout = new LinearLayout.LayoutParams(0, Dp(48), 1f);
        layout.SetMargins(left, top, right, bottom);
        return layout;
    }

    private int Dp(int value)
    {
        var density = Resources?.DisplayMetrics?.Density ?? 1f;
        return (int)(value * density + 0.5f);
    }

    private void ApplySafeAreaPadding(View view)
    {
        var left = Dp(ContentPaddingHorizontalDp);
        var top = Dp(ContentPaddingTopDp) + GetSystemBarDimension("status_bar_height");
        var right = Dp(ContentPaddingHorizontalDp);
        var bottom = Dp(ContentPaddingBottomDp) + GetSystemBarDimension("navigation_bar_height");

        view.SetPadding(left, top, right, bottom);
        view.SetOnApplyWindowInsetsListener(new SafeAreaInsetListener(
            Dp(ContentPaddingHorizontalDp),
            Dp(ContentPaddingTopDp),
            Dp(ContentPaddingHorizontalDp),
            Dp(ContentPaddingBottomDp)));
        view.RequestApplyInsets();
    }

    private int GetSystemBarDimension(string resourceName)
    {
        var resourceId = Resources?.GetIdentifier(resourceName, "dimen", "android") ?? 0;
        return resourceId > 0 && Resources is not null
            ? Resources.GetDimensionPixelSize(resourceId)
            : 0;
    }

    private sealed class SafeAreaInsetListener(int baseLeft, int baseTop, int baseRight, int baseBottom)
        : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View? view, WindowInsets? insets)
        {
            if (view is null || insets is null)
            {
                return insets!;
            }

            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                var bars = insets.GetInsets(WindowInsets.Type.SystemBars());
                view.SetPadding(
                    baseLeft + bars.Left,
                    baseTop + bars.Top,
                    baseRight + bars.Right,
                    baseBottom + bars.Bottom);
            }
            else
            {
                view.SetPadding(
                    baseLeft + insets.SystemWindowInsetLeft,
                    baseTop + insets.SystemWindowInsetTop,
                    baseRight + insets.SystemWindowInsetRight,
                    baseBottom + insets.SystemWindowInsetBottom);
            }

            return insets;
        }
    }

    private sealed class StatsUpdateTask(MainActivity activity) : Java.Util.TimerTask
    {
        public override void Run()
        {
            activity.RunOnUiThread(() => activity.UpdateStatsDisplay());
        }
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
}
