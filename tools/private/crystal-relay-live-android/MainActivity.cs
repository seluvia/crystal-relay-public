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

    private EditText? endpointInput;
    private Switch? alertsSwitch;
    private TextView? statusText;
    private TextView? lastUpdatedText;
    private LinearLayout? usersPanel;
    private Button? refreshButton;
    private Button? saveButton;
    private bool isLoadingSettings;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        RequestWindowFeature(WindowFeatures.NoTitle);
        base.OnCreate(savedInstanceState);
        ActionBar?.Hide();

        LiveNotificationService.EnsureChannel(this);
        BuildUi();
        LoadSettingsIntoUi();
        RequestNotificationPermissionIfNeeded();

        if (LiveSettings.GetNotificationsEnabled(this))
        {
            LiveAlarmScheduler.Schedule(this);
        }

        _ = RefreshLiveListAsync(false);
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

        var rootScroll = new ScrollView(this)
        {
            FillViewport = true
        };

        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        ApplySafeAreaPadding(root);
        root.SetBackgroundColor(Color.ParseColor("#080A13"));
        rootScroll.AddView(root, new ScrollView.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent));

        var title = MakeText("Crystal Relay Live Watch", 28, "#F8F4FF", TypefaceStyle.Bold);
        root.AddView(title);

        var subtitle = MakeText("Void Crystal phone heartbeat monitor", 14, "#CDBEEE", TypefaceStyle.Normal);
        subtitle.SetPadding(0, Dp(4), 0, Dp(14));
        root.AddView(subtitle);

        var endpointCard = MakeCard();
        endpointCard.Orientation = Orientation.Vertical;
        root.AddView(endpointCard, MatchWrap(0, 0, 0, Dp(14)));

        endpointCard.AddView(MakeText("Live endpoint", 12, "#CDBEEE", TypefaceStyle.Bold));

        endpointInput = new EditText(this)
        {
            InputType = InputTypes.ClassText | InputTypes.TextVariationUri,
            TextSize = 13
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

        alertsSwitch = new Switch(this)
        {
            Text = "Phone live alerts",
            TextSize = 15,
            ShowText = false
        };
        alertsSwitch.SetTextColor(Color.ParseColor("#F8F4FF"));
        alertsSwitch.SetPadding(0, Dp(12), 0, 0);
        alertsSwitch.CheckedChange += (_, args) =>
        {
            if (isLoadingSettings)
            {
                return;
            }

            LiveSettings.SaveNotificationsEnabled(this, args.IsChecked);
            if (args.IsChecked)
            {
                RequestNotificationPermissionIfNeeded();
                LiveAlarmScheduler.Schedule(this);
                statusText!.Text = "Phone alerts are on. Android will check in the background when allowed.";
            }
            else
            {
                LiveAlarmScheduler.Cancel(this);
                statusText!.Text = "Phone alerts are off. Manual refresh still works.";
            }
        };
        endpointCard.AddView(alertsSwitch);

        var cadence = MakeText("Background checks are roughly every 15 minutes. Android may delay them in battery saver or deep sleep.", 12, "#CDBEEE", TypefaceStyle.Normal);
        cadence.SetPadding(0, Dp(8), 0, 0);
        endpointCard.AddView(cadence);

        var statusCard = MakeCard();
        statusCard.Orientation = Orientation.Vertical;
        root.AddView(statusCard, MatchWrap(0, 0, 0, Dp(14)));

        statusText = MakeText("Loading live users...", 16, "#E6F9FF", TypefaceStyle.Bold);
        statusCard.AddView(statusText);

        lastUpdatedText = MakeText("Not refreshed yet.", 12, "#CDBEEE", TypefaceStyle.Normal);
        lastUpdatedText.SetPadding(0, Dp(6), 0, 0);
        statusCard.AddView(lastUpdatedText);

        usersPanel = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        root.AddView(usersPanel);

        SetContentView(rootScroll);
    }

    private void LoadSettingsIntoUi()
    {
        isLoadingSettings = true;
        try
        {
            endpointInput!.Text = LiveSettings.GetEndpoint(this);
            alertsSwitch!.Checked = LiveSettings.GetNotificationsEnabled(this);
        }
        finally
        {
            isLoadingSettings = false;
        }
    }

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
        statusText!.Text = "Endpoint saved. The next refresh will become the new baseline.";
        _ = RefreshLiveListAsync(false);
    }

    private async Task RefreshLiveListAsync(bool notifyOnNewLive)
    {
        if (refreshButton is null || statusText is null || lastUpdatedText is null || usersPanel is null)
        {
            return;
        }

        refreshButton.Enabled = false;
        statusText.Text = "Refreshing live list...";
        try
        {
            var result = await LiveMonitorClient.CheckAsync(this, notifyOnNewLive);
            statusText.Text = result.StatusText;
            lastUpdatedText.Text = result.LastUpdatedText;
            RenderUsers(result.Users);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Could not refresh: {ex.Message}";
            lastUpdatedText.Text = $"Last attempt: {DateTimeOffset.Now:g}";
        }
        finally
        {
            refreshButton.Enabled = true;
        }
    }

    private void RenderUsers(IReadOnlyList<LiveUserInfo> users)
    {
        usersPanel!.RemoveAllViews();

        if (users.Count == 0)
        {
            var empty = MakeCard();
            empty.SetGravity(GravityFlags.Center);
            empty.SetMinimumHeight(Dp(170));
            empty.AddView(MakeText("No Crystal Relay users are live right now", 17, "#F8F4FF", TypefaceStyle.Bold));
            usersPanel.AddView(empty, MatchWrap());
            return;
        }

        foreach (var user in users)
        {
            var card = MakeCard();
            card.Orientation = Orientation.Vertical;
            usersPanel.AddView(card, MatchWrap(0, 0, 0, Dp(12)));

            var row = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal
            };
            row.SetGravity(GravityFlags.CenterVertical);
            card.AddView(row);

            var name = MakeText(user.DisplayName, 20, "#F8F4FF", TypefaceStyle.Bold);
            row.AddView(name, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            var livePill = MakeText("LIVE", 11, "#130A1B", TypefaceStyle.Bold);
            livePill.Gravity = GravityFlags.Center;
            livePill.SetPadding(Dp(10), Dp(4), Dp(10), Dp(4));
            livePill.Background = Rounded("#B8FF78D8", "#D97DF9FF", 999, 1);
            row.AddView(livePill);

            var url = MakeText(user.TwitchUrl, 13, "#7DF9FF", TypefaceStyle.Bold);
            url.SetPadding(0, Dp(10), 0, Dp(8));
            card.AddView(url);

            var details = MakeText(user.DetailText, 13, "#CDBEEE", TypefaceStyle.Normal);
            card.AddView(details);
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
        manager?.HideSoftInputFromWindow(endpointInput?.WindowToken, HideSoftInputFlags.None);
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
}
