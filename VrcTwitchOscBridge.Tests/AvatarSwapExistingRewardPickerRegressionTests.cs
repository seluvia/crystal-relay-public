using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;
using Xunit;
using Xunit.Sdk;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSwapExistingRewardPickerRegressionTests
{
    private const string TestAccessToken = "test-" + "access-token";

    [Fact]
    public void InlineEditor_UsesVisibilityConverterAndKeepsExistingRewardBindings()
    {
        var xaml = File.ReadAllText(FindSourceFile(
            "VrcTwitchOscBridge",
            "UserControls",
            "InlineRuleEditorControl.xaml"));

        AssertInlineEditorRewardPanel(xaml);
    }

    [Fact]
    public void InlineEditor_RejectsCommentAndDormantDuplicateFalsePositive()
    {
        const string xaml = """
            <UserControl>
                <!-- Channel Points reward config -->
                <!-- A dormant duplicate can contain convincing reward-picker markup. -->
                <StackPanel>
                    <RadioButton IsChecked="{Binding Rule.RewardSyncMode, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=CreateOrManage}" />
                    <RadioButton IsChecked="{Binding Rule.RewardSyncMode, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=LinkExisting}" />
                    <StackPanel Visibility="{Binding Rule.RewardSyncMode, Converter={StaticResource EnumToVisibilityConverter}, ConverterParameter=CreateOrManage, FallbackValue=Visible}" />
                    <StackPanel Visibility="{Binding Rule.RewardSyncMode, Converter={StaticResource EnumToVisibilityConverter}, ConverterParameter=LinkExisting, FallbackValue=Collapsed}">
                        <ComboBox ItemsSource="{Binding DataContext.TwitchRewardOptions, RelativeSource={RelativeSource AncestorType=Window}}"
                                  SelectedValue="{Binding Rule.ChannelPointRewardId, UpdateSourceTrigger=PropertyChanged}" />
                        <Button Command="{Binding DataContext.RefreshTwitchRewardsCommand, RelativeSource={RelativeSource AncestorType=Window}}" />
                        <Button Command="{Binding DataContext.UnlinkTwitchRewardCommand, RelativeSource={RelativeSource AncestorType=Window}}" />
                    </StackPanel>
                </StackPanel>
                <!-- Power Up trigger fields -->
            </UserControl>
            """;

        var exception = Assert.Throws<XunitException>(() => AssertInlineEditorRewardPanel(xaml));
        Assert.Contains("channel-point reward panel", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertInlineEditorRewardPanel(string xaml)
    {
        var document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
        var rewardPanels = document.Descendants()
            .Where(IsChannelPointRewardPanel)
            .ToArray();
        if (rewardPanels.Length != 1)
        {
            throw new XunitException(
                $"Expected exactly one active channel-point reward panel, found {rewardPanels.Length}.");
        }

        var rewardPanel = rewardPanels[0];
        var rewardBorder = Assert.Single(rewardPanel.Elements(), element =>
            IsElement(element, "Border")
            && element.Descendants().Any(child =>
                IsElement(child, "TextBlock")
                && AttributeValue(child, "Text") == "{loc:Translate 'Twitch Reward'}"));
        var rewardContent = Assert.Single(rewardBorder.Elements(), element => IsElement(element, "StackPanel"));

        const string createChecked = "{Binding Rule.RewardSyncMode, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=CreateOrManage}";
        const string linkChecked = "{Binding Rule.RewardSyncMode, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=LinkExisting}";
        Assert.Single(rewardContent.Descendants(), element =>
            IsElement(element, "RadioButton") && AttributeValue(element, "IsChecked") == createChecked);
        Assert.Single(rewardContent.Descendants(), element =>
            IsElement(element, "RadioButton") && AttributeValue(element, "IsChecked") == linkChecked);

        const string createVisibility = "{Binding Rule.RewardSyncMode, Converter={StaticResource EnumToVisibilityConverter}, ConverterParameter=CreateOrManage, FallbackValue=Visible}";
        const string linkVisibility = "{Binding Rule.RewardSyncMode, Converter={StaticResource EnumToVisibilityConverter}, ConverterParameter=LinkExisting, FallbackValue=Collapsed}";
        Assert.Single(rewardContent.Elements(), element =>
            IsElement(element, "StackPanel") && AttributeValue(element, "Visibility") == createVisibility);
        var linkedRewardPanel = Assert.Single(rewardContent.Elements(), element =>
            IsElement(element, "StackPanel") && AttributeValue(element, "Visibility") == linkVisibility);
        Assert.DoesNotContain(rewardContent.Descendants(), element =>
            AttributeValue(element, "Visibility")?.Contains("EnumToBoolConverter", StringComparison.Ordinal) == true);

        var rewardComboBox = Assert.Single(linkedRewardPanel.Descendants(), element => IsElement(element, "ComboBox"));
        Assert.Equal(
            "{Binding DataContext.TwitchRewardOptions, RelativeSource={RelativeSource AncestorType=Window}}",
            AttributeValue(rewardComboBox, "ItemsSource"));
        Assert.Equal("Id", AttributeValue(rewardComboBox, "SelectedValuePath"));
        Assert.Equal("Title", AttributeValue(rewardComboBox, "DisplayMemberPath"));
        Assert.Equal(
            "{Binding Rule.ChannelPointRewardId, UpdateSourceTrigger=PropertyChanged}",
            AttributeValue(rewardComboBox, "SelectedValue"));

        const string refreshCommand = "{Binding DataContext.RefreshTwitchRewardsCommand, RelativeSource={RelativeSource AncestorType=Window}}";
        const string unlinkCommand = "{Binding DataContext.UnlinkTwitchRewardCommand, RelativeSource={RelativeSource AncestorType=Window}}";
        Assert.Single(linkedRewardPanel.Descendants(), element =>
            IsElement(element, "Button") && AttributeValue(element, "Command") == refreshCommand);
        var unlinkButton = Assert.Single(linkedRewardPanel.Descendants(), element =>
            IsElement(element, "Button") && AttributeValue(element, "Command") == unlinkCommand);
        Assert.Equal("{Binding Rule}", AttributeValue(unlinkButton, "CommandParameter"));
    }

    [Fact]
    public void OpenAvatarSwapManager_UsesCombinedRewardRefreshExactlyOnce()
    {
        var source = File.ReadAllText(FindSourceFile(
            "VrcTwitchOscBridge",
            "ViewModels",
            "MainWindowViewModel.cs"));
        var openBody = NormalizeWhitespace(GetMethodBody(source, "public void OpenAvatarSwapManager()"));
        var refreshBody = NormalizeWhitespace(GetMethodBody(source, "private async Task RefreshTwitchRewardsAsync()"));

        Assert.Single(
            Regex.Matches(openBody, Regex.Escape("_ = RefreshTwitchRewardsAsync();")));
        Assert.DoesNotContain("_ = QueuePowerUpRefreshAsync();", openBody, StringComparison.Ordinal);
        Assert.Contains(
            "await QueueRewardRefreshAsync(); await QueuePowerUpRefreshAsync();",
            refreshBody,
            StringComparison.Ordinal);

        var visibleGuardIndex = openBody.IndexOf(
            "if (_avatarSwapManagerWindow is { IsVisible: true })",
            StringComparison.Ordinal);
        var visibleReturnIndex = openBody.IndexOf("return;", visibleGuardIndex, StringComparison.Ordinal);
        var managerConstructionIndex = openBody.IndexOf(
            "var managerVm = new AvatarSwapManagerViewModel(",
            StringComparison.Ordinal);
        var refreshIndex = openBody.IndexOf("_ = RefreshTwitchRewardsAsync();", StringComparison.Ordinal);
        var showIndex = openBody.IndexOf("_avatarSwapManagerWindow.Show();", StringComparison.Ordinal);

        Assert.True(visibleGuardIndex >= 0, "The visible-window guard must remain present.");
        Assert.True(visibleReturnIndex > visibleGuardIndex, "The visible-window path must return before refresh work.");
        Assert.True(managerConstructionIndex > visibleReturnIndex, "Manager construction must follow the visible-window guard.");
        Assert.True(refreshIndex > managerConstructionIndex, "Reward refresh must follow manager ViewModel construction.");
        Assert.True(showIndex > refreshIndex, "The manager window must be shown after reward refresh is queued.");
    }

    [Fact]
    public void RewardUnlinkMethods_QueueSaveManagedSyncAndBridgeRefresh()
    {
        var source = File.ReadAllText(FindSourceFile(
            "VrcTwitchOscBridge",
            "ViewModels",
            "MainWindowViewModel.cs"));
        var unlinkBody = GetMethodBody(source, "private void UnlinkTwitchReward(object? target)");
        var wardrobeMasterBody = GetMethodBody(source, "private void UnlinkWardrobeMasterReward(object? target)");

        Assert.Contains("QueueSave(0);", unlinkBody, StringComparison.Ordinal);
        Assert.Contains("QueueManagedRewardSync(0);", unlinkBody, StringComparison.Ordinal);
        Assert.Contains("QueueBridgeRefresh();", unlinkBody, StringComparison.Ordinal);
        Assert.Contains("QueueSave(0);", wardrobeMasterBody, StringComparison.Ordinal);
        Assert.Contains("QueueManagedRewardSync(0);", wardrobeMasterBody, StringComparison.Ordinal);
        Assert.Contains("QueueBridgeRefresh();", wardrobeMasterBody, StringComparison.Ordinal);
    }

    [Fact]
    public void UniversalEditorExitPaths_ClearSelectionThroughDetachingSetter()
    {
        var source = File.ReadAllText(FindSourceFile(
            "VrcTwitchOscBridge",
            "ViewModels",
            "UniversalTriggersManagerViewModel.cs"));
        var selectedTriggerProperty = GetMethodBody(
            source,
            "public UniversalTriggerRule? SelectedTrigger");

        Assert.Contains(
            "_selectedTrigger.PropertyChanged -= OnSelectedTriggerPropertyChanged;",
            selectedTriggerProperty,
            StringComparison.Ordinal);
        Assert.Contains(
            "_selectedTrigger.PropertyChanged += OnSelectedTriggerPropertyChanged;",
            selectedTriggerProperty,
            StringComparison.Ordinal);

        foreach (var signature in new[]
                 {
                     "private void CloseEditor()",
                     "private async Task SaveEditorAsync()",
                     "private async Task DeleteSelectedTriggerAsync()",
                     "private async Task DeleteAllAsync()"
                 })
        {
            Assert.Contains(
                "SelectedTrigger = null;",
                GetMethodBody(source, signature),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ManagedRewardSync_PreHydratesExactIdTitlesBeforeCleanupAndTargetTraversal()
    {
        var source = File.ReadAllText(FindSourceFile(
            "VrcTwitchOscBridge",
            "ViewModels",
            "MainWindowViewModel.cs"));
        var syncBody = GetMethodBody(
            source,
            "private async Task<ManagedRewardSyncOutcome> SynchronizeManagedChannelPointRewardsAsync(");
        var preHydrateIndex = syncBody.IndexOf(
            "PreHydrateManagedRewardTargetTitles(",
            StringComparison.Ordinal);
        var cleanupIndex = syncBody.IndexOf(
            "CleanupStaleManagedRewardsAsync(",
            StringComparison.Ordinal);
        var capTitleIndex = syncBody.IndexOf(
            "BuildManagedRewardCapReclaimProtectedTitleKeys(allSyncTargets)",
            StringComparison.Ordinal);
        var traversalIndex = syncBody.IndexOf(
            "foreach (var target in allSyncTargets)",
            StringComparison.Ordinal);

        Assert.True(preHydrateIndex >= 0, "Managed reward sync must pre-hydrate exact-ID titles.");
        Assert.True(cleanupIndex > preHydrateIndex, "Pre-hydration must run before stale cleanup decisions.");
        Assert.True(capTitleIndex > preHydrateIndex, "Cap-reclaim title protection must use hydrated titles.");
        Assert.True(traversalIndex > capTitleIndex, "Target traversal must start after pre-hydration and cap-title capture.");
    }

    [Fact]
    public void ManagedRewardSync_RepairsPersistedMasterIdCollisionsBeforeFingerprintSkipping()
    {
        var source = File.ReadAllText(FindSourceFile(
            "VrcTwitchOscBridge",
            "ViewModels",
            "MainWindowViewModel.cs"));
        var syncBody = GetMethodBody(
            source,
            "private async Task<ManagedRewardSyncOutcome> SynchronizeManagedChannelPointRewardsAsync(");
        var repairIndex = syncBody.IndexOf(
            "RepairConflictingProfileMasterRewardIds()",
            StringComparison.Ordinal);
        var saveIndex = repairIndex >= 0
            ? syncBody.IndexOf("QueueSave(0);", repairIndex, StringComparison.Ordinal)
            : -1;
        var fingerprintIndex = syncBody.IndexOf(
            "var desiredFingerprint = BuildManagedRewardDesiredFingerprint(",
            StringComparison.Ordinal);
        var unchangedSkipIndex = syncBody.IndexOf(
            "ShouldSkipUnchangedManagedRewardSync(reason)",
            StringComparison.Ordinal);

        Assert.True(repairIndex >= 0, "Managed reward sync must repair persisted profile-master ID collisions.");
        Assert.True(saveIndex > repairIndex, "A local collision repair must queue the existing save flow.");
        Assert.True(fingerprintIndex > saveIndex, "Collision repair and save queuing must precede fingerprint capture.");
        Assert.True(unchangedSkipIndex > fingerprintIndex, "Unchanged-sync skipping must evaluate the repaired identity state.");
    }

    [Fact]
    public async Task PowerUpRefresh_ActiveManagedRewardBackoffMakesNoTwitchRequest()
    {
        var handler = new RecordingTwitchHandler();
        await using var vm = CreateRewardRefreshViewModel(handler);
        SetPrivateField(
            vm,
            "managedRewardApiBackoffUntil",
            (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(2));

        await InvokePrivateTaskAsync(vm, "QueuePowerUpRefreshAsync");

        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task PowerUpRefresh_TooManyRequestsEntersManagedRewardBackoff()
    {
        var handler = new RecordingTwitchHandler
        {
            PowerUpStatusCode = HttpStatusCode.TooManyRequests
        };
        await using var vm = CreateRewardRefreshViewModel(handler);
        var startedAt = DateTimeOffset.UtcNow;

        await InvokePrivateTaskAsync(vm, "QueuePowerUpRefreshAsync");

        var backoffUntil = GetPrivateField<DateTimeOffset?>(vm, "managedRewardApiBackoffUntil");
        Assert.NotNull(backoffUntil);
        Assert.True(backoffUntil > startedAt.AddMinutes(1));
        Assert.Contains(
            handler.RequestUris,
            uri => uri.Contains("/helix/bits/custom_power_ups", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CombinedRefresh_RewardTooManyRequestsSkipsPowerUpRequest()
    {
        var handler = new RecordingTwitchHandler
        {
            RewardStatusCode = HttpStatusCode.TooManyRequests
        };
        await using var vm = CreateRewardRefreshViewModel(handler);

        await InvokePrivateTaskAsync(vm, "RefreshTwitchRewardsAsync");

        Assert.Single(
            handler.RequestUris,
            uri => uri.Contains("/helix/channel_points/custom_rewards", StringComparison.Ordinal));
        Assert.DoesNotContain(
            handler.RequestUris,
            uri => uri.Contains("/helix/bits/custom_power_ups", StringComparison.Ordinal));
        Assert.NotNull(GetPrivateField<DateTimeOffset?>(vm, "managedRewardApiBackoffUntil"));
    }

    [Fact]
    public async Task TwitchRewardCatalog_ConcurrentRequestsShareOneInFlightFetch()
    {
        var handler = new RecordingTwitchHandler();
        using var client = new TwitchApiClient();
        var httpClientField = GetPrivateFieldInfo(typeof(TwitchApiClient), "httpClient");
        var originalHttpClient = Assert.IsType<HttpClient>(httpClientField.GetValue(client));
        httpClientField.SetValue(client, new HttpClient(handler));
        originalHttpClient.Dispose();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.GetCustomRewardsAsync(
            TestAccessToken,
            RuntimeConfig.DefaultTwitchClientId,
            "test-broadcaster-id")));

        Assert.Single(handler.RequestUris, uri => uri.Contains("/helix/channel_points/custom_rewards", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ManagedRewardBackoff_ConcurrentUpdatesPreserveLongestDeadline()
    {
        await using var vm = new MainWindowViewModel();
        _ = GetPrivateField<object>(vm, "managedRewardApiBackoffGate");
        var extendMethod = GetPrivateMethod("ExtendManagedRewardApiBackoffDeadline");
        var deadlines = new[]
        {
            DateTimeOffset.UtcNow.AddMinutes(2),
            DateTimeOffset.UtcNow.AddMinutes(7),
            DateTimeOffset.UtcNow.AddMinutes(4)
        };
        var failures = new Exception?[deadlines.Length];
        using var startBarrier = new Barrier(deadlines.Length);
        using var completed = new CountdownEvent(deadlines.Length);
        var threads = deadlines
            .Select((deadline, index) => new Thread(() =>
            {
                try
                {
                    startBarrier.SignalAndWait();
                    _ = extendMethod.Invoke(vm, new object[] { deadline });
                }
                catch (Exception ex)
                {
                    failures[index] = ex;
                }
                finally
                {
                    completed.Signal();
                }
            })
            {
                IsBackground = true
            })
            .ToArray();

        foreach (var thread in threads)
        {
            thread.Start();
        }

        Assert.True(completed.Wait(TimeSpan.FromSeconds(5)), "Concurrent backoff updates did not complete.");
        Assert.All(failures, Assert.Null);
        Assert.Equal(deadlines.Max(), GetPrivateField<DateTimeOffset?>(vm, "managedRewardApiBackoffUntil"));
    }

    [Fact]
    public async Task ManagedRewardBackoff_ExpiredReaderCannotClearConcurrentNewerDeadline()
    {
        await using var vm = new MainWindowViewModel();
        _ = GetPrivateField<object>(vm, "managedRewardApiBackoffGate");
        var getActiveMethod = GetPrivateMethod("GetActiveManagedRewardApiBackoffDeadline");
        var extendMethod = GetPrivateMethod("ExtendManagedRewardApiBackoffDeadline");
        var newerDeadline = DateTimeOffset.UtcNow.AddMinutes(5);
        var now = DateTimeOffset.UtcNow;
        SetPrivateField(
            vm,
            "managedRewardApiBackoffUntil",
            (DateTimeOffset?)now.AddMinutes(-1));
        var failures = new Exception?[2];
        using var startBarrier = new Barrier(2);
        using var completed = new CountdownEvent(2);
        var reader = new Thread(() =>
        {
            try
            {
                startBarrier.SignalAndWait();
                _ = getActiveMethod.Invoke(vm, new object[] { now });
            }
            catch (Exception ex)
            {
                failures[0] = ex;
            }
            finally
            {
                completed.Signal();
            }
        })
        {
            IsBackground = true
        };
        var updater = new Thread(() =>
        {
            try
            {
                startBarrier.SignalAndWait();
                _ = extendMethod.Invoke(vm, new object[] { newerDeadline });
            }
            catch (Exception ex)
            {
                failures[1] = ex;
            }
            finally
            {
                completed.Signal();
            }
        })
        {
            IsBackground = true
        };

        reader.Start();
        updater.Start();

        Assert.True(completed.Wait(TimeSpan.FromSeconds(5)), "Concurrent backoff operations did not complete.");
        Assert.All(failures, Assert.Null);
        Assert.Equal(newerDeadline, GetPrivateField<DateTimeOffset?>(vm, "managedRewardApiBackoffUntil"));
        Assert.Equal(
            newerDeadline,
            Assert.IsType<DateTimeOffset>(getActiveMethod.Invoke(vm, new object[] { now })));
    }

    [Fact]
    public async Task ManagedRewardBackoff_SmallerUpdateCannotShortenExistingDeadline()
    {
        await using var vm = new MainWindowViewModel();
        var extendMethod = GetPrivateMethod("ExtendManagedRewardApiBackoffDeadline");
        var longerDeadline = DateTimeOffset.UtcNow.AddMinutes(8);
        var shorterDeadline = DateTimeOffset.UtcNow.AddMinutes(2);

        Assert.Equal(longerDeadline, extendMethod.Invoke(vm, new object[] { longerDeadline }));
        Assert.Equal(longerDeadline, extendMethod.Invoke(vm, new object[] { shorterDeadline }));
        Assert.Equal(longerDeadline, GetPrivateField<DateTimeOffset?>(vm, "managedRewardApiBackoffUntil"));
    }

    [Fact]
    public async Task ManagedRewardBackoff_SchedulesPendingSyncAfterDeadline()
    {
        await using var vm = new MainWindowViewModel();
        SetPrivateField(vm, "isInitialized", true);
        SetPrivateField(
            vm,
            "managedRewardApiBackoffUntil",
            (DateTimeOffset?)DateTimeOffset.UtcNow.AddMilliseconds(75));

        var scheduleMethod = GetPrivateMethod("ScheduleManagedRewardSyncAfterBackoff");
        scheduleMethod.Invoke(vm, new object[] { MainWindowViewModel.ManagedRewardSyncReason.FireSaleChanged });
        // This test verifies retry-state cleanup, not the WPF-backed sync itself.
        SetPrivateField(vm, "isShuttingDown", true);

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline
               && (GetPrivateField<object?>(vm, "managedRewardSyncBackoffRetryCancellation") is not null
                   || GetPrivateField<object?>(vm, "pendingManagedRewardSyncReason") is not null))
        {
            await Task.Delay(25);
        }

        Assert.Null(GetPrivateField<object?>(vm, "managedRewardSyncBackoffRetryCancellation"));
        Assert.Null(GetPrivateField<object?>(vm, "pendingManagedRewardSyncReason"));
        SetPrivateField(vm, "isInitialized", false);
    }

    [Fact]
    public void ManagedRewardBackoff_StateAccessRemainsInsideSharedLockHelpers()
    {
        var source = File.ReadAllText(FindSourceFile(
            "VrcTwitchOscBridge",
            "ViewModels",
            "MainWindowViewModel.cs"));
        var readerBody = GetMethodBody(
            source,
            "private DateTimeOffset? GetActiveManagedRewardApiBackoffDeadline(DateTimeOffset now)");
        var updaterBody = GetMethodBody(
            source,
            "private DateTimeOffset ExtendManagedRewardApiBackoffDeadline(DateTimeOffset requestedRetryAfterUtc)");

        Assert.Contains("lock (managedRewardApiBackoffGate)", readerBody, StringComparison.Ordinal);
        Assert.Contains("lock (managedRewardApiBackoffGate)", updaterBody, StringComparison.Ordinal);

        var sourceOutsideLockHelpers = source
            .Replace(readerBody, string.Empty, StringComparison.Ordinal)
            .Replace(updaterBody, string.Empty, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(sourceOutsideLockHelpers, @"\bmanagedRewardApiBackoffUntil\b").Cast<Match>());
        Assert.Single(Regex.Matches(sourceOutsideLockHelpers, @"\bmanagedRewardApiBackoffGate\b").Cast<Match>());
    }

    private static bool IsChannelPointRewardPanel(XElement element)
    {
        return IsElement(element, "StackPanel")
            && element.Elements()
                .Where(child => IsElement(child, "StackPanel.Style"))
                .SelectMany(style => style.Descendants().Where(trigger => IsElement(trigger, "DataTrigger")))
                .Any(trigger =>
                    AttributeValue(trigger, "Binding") == "{Binding Rule.TriggerType}"
                    && AttributeValue(trigger, "Value") == "ChannelPoints"
                    && trigger.Descendants().Any(setter =>
                        IsElement(setter, "Setter")
                        && AttributeValue(setter, "Property") == "Visibility"
                        && AttributeValue(setter, "Value") == "Visible"))
            && element.Elements()
                .Where(child => IsElement(child, "Border"))
                .SelectMany(border => border.Descendants())
                .Any(child =>
                    IsElement(child, "TextBlock")
                    && AttributeValue(child, "Text") == "{loc:Translate 'Twitch Reward'}");
    }

    private static bool IsElement(XElement element, string localName) =>
        element.Name.LocalName == localName;

    private static string? AttributeValue(XElement element, string localName) =>
        element.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == localName)?.Value;

    private static string GetMethodBody(string source, string signature)
    {
        var methodStart = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Could not find method '{signature}'.");

        var bodyStart = source.IndexOf('{', methodStart);
        Assert.True(bodyStart >= 0, $"Could not find method body for '{signature}'.");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(bodyStart, index - bodyStart + 1);
                }
            }
        }

        throw new InvalidOperationException($"Could not find method body end for '{signature}'.");
    }

    private static string NormalizeWhitespace(string source) => Regex.Replace(source, @"\s+", " ");

    private static MainWindowViewModel CreateRewardRefreshViewModel(RecordingTwitchHandler handler)
    {
        var twitchApiClient = new TwitchApiClient();
        var apiHttpClientField = GetPrivateFieldInfo(typeof(TwitchApiClient), "httpClient");
        var originalHttpClient = Assert.IsType<HttpClient>(apiHttpClientField.GetValue(twitchApiClient));
        apiHttpClientField.SetValue(twitchApiClient, new HttpClient(handler));
        originalHttpClient.Dispose();

        var vm = new MainWindowViewModel();
        var vmApiClientField = GetPrivateFieldInfo(typeof(MainWindowViewModel), "twitchApiClient");
        var originalApiClient = Assert.IsType<TwitchApiClient>(vmApiClientField.GetValue(vm));
        vmApiClientField.SetValue(vm, twitchApiClient);
        originalApiClient.Dispose();
        SetPrivateField(vm, "runtimeConfigLoaded", true);

        vm.Settings.Broadcaster.AccessToken = TestAccessToken;
        vm.Settings.Broadcaster.UserId = "test-broadcaster-id";
        vm.Settings.Broadcaster.Login = "test-broadcaster";
        vm.Settings.Broadcaster.DisplayName = "Test Broadcaster";
        vm.Settings.Broadcaster.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
        vm.Settings.Broadcaster.Scopes = [TwitchScopes.RewardManagement];
        return vm;
    }

    private static async Task InvokePrivateTaskAsync(MainWindowViewModel vm, string methodName)
    {
        var method = typeof(MainWindowViewModel).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method.Invoke(vm, null));
        await task;
    }

    private static bool InvokePrivateBool(
        MainWindowViewModel vm,
        string methodName,
        params object[] arguments)
    {
        var method = typeof(MainWindowViewModel).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method.Invoke(vm, arguments));
    }

    private static MethodInfo GetPrivateMethod(string methodName)
    {
        var method = typeof(MainWindowViewModel).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method;
    }

    private static void SetPrivateField<T>(object target, string fieldName, T value)
    {
        GetPrivateFieldInfo(target.GetType(), fieldName).SetValue(target, value);
    }

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        var value = GetPrivateFieldInfo(target.GetType(), fieldName).GetValue(target);
        return value is null ? default! : (T)value;
    }

    private static FieldInfo GetPrivateFieldInfo(Type type, string fieldName)
    {
        var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field;
    }

    private static string FindSourceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find source file.", Path.Combine(relativeParts));
    }

    private sealed class RecordingTwitchHandler : HttpMessageHandler
    {
        public HttpStatusCode RewardStatusCode { get; init; } = HttpStatusCode.OK;

        public HttpStatusCode PowerUpStatusCode { get; init; } = HttpStatusCode.OK;

        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            RequestUris.Add(uri);

            if (uri.Contains("/oauth2/validate", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(
                    HttpStatusCode.OK,
                    $"{{\"client_id\":\"{RuntimeConfig.DefaultTwitchClientId}\",\"login\":\"test-broadcaster\",\"scopes\":[\"{TwitchScopes.RewardManagement}\"],\"user_id\":\"test-broadcaster-id\",\"expires_in\":3600}}"));
            }

            if (uri.Contains("/helix/users", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(
                    HttpStatusCode.OK,
                    "{\"data\":[{\"id\":\"test-broadcaster-id\",\"login\":\"test-broadcaster\",\"display_name\":\"Test Broadcaster\",\"profile_image_url\":\"\"}]}"));
            }

            if (uri.Contains("/helix/channel_points/custom_rewards", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(RewardStatusCode, "{\"data\":[]}"));
            }

            if (uri.Contains("/helix/bits/custom_power_ups", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(PowerUpStatusCode, "{\"data\":[]}"));
            }

            return Task.FromResult(JsonResponse(
                HttpStatusCode.NotFound,
                "{\"message\":\"Unexpected test request.\"}"));
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    statusCode == HttpStatusCode.TooManyRequests
                        ? "{\"message\":\"Synthetic rate limit.\"}"
                        : json,
                    Encoding.UTF8,
                    "application/json")
            };
            if (statusCode == HttpStatusCode.TooManyRequests)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                    TimeSpan.FromMinutes(2));
            }

            return response;
        }
    }
}
