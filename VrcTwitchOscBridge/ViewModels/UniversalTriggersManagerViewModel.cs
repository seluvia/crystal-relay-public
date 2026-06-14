using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public enum UniversalTriggerFilterMode
{
    All,
    Active,
    Disabled,
    NeedsFix,
    FromFooma,
}

public enum UniversalTriggerSortMode
{
    ByType,
    ByStatus,
    ByName,
    RecentlyEdited,
}

public class UniversalTriggersManagerViewModel : ObservableObject
{
    private const string FoomaHelpUrl = "https://foomaring.gumroad.com/l/lmrjbl";

    private readonly AppSettings _settings;
    private readonly MainWindowViewModel _mainWindowViewModel;
    private string _searchText = string.Empty;
    private UniversalTriggerFilterMode _filterMode = UniversalTriggerFilterMode.All;
    private UniversalTriggerSortMode _sortMode = UniversalTriggerSortMode.ByType;
    private bool _isChatSectionCollapsed;
    private bool _isRewardSectionCollapsed;
    private bool _isBitsSectionCollapsed;
    private bool _isSubsSectionCollapsed;
    private bool _isFollowsSectionCollapsed;
    private readonly ObservableCollection<UniversalTriggerCardViewModel> _chatSectionSource = [];
    private readonly ObservableCollection<UniversalTriggerCardViewModel> _rewardSectionSource = [];
    private readonly ObservableCollection<UniversalTriggerCardViewModel> _bitsSectionSource = [];
    private readonly ObservableCollection<UniversalTriggerCardViewModel> _subsSectionSource = [];
    private readonly ObservableCollection<UniversalTriggerCardViewModel> _followsSectionSource = [];
    private readonly Dictionary<UniversalTriggerRule, UniversalTriggerCardViewModel> _cardLookup = new();
    private UniversalTriggerRule? _selectedTrigger;
    private bool _isEditorOpen;
    private UniversalTriggerRuleSnapshot? _editorSnapshot;
    private bool _editorIsNew;

    public UniversalTriggersManagerViewModel(AppSettings settings, MainWindowViewModel mainWindowViewModel)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

        _isChatSectionCollapsed = _settings.UniversalTriggersChatCollapsed;
        _isRewardSectionCollapsed = _settings.UniversalTriggersRewardCollapsed;
        _isBitsSectionCollapsed = _settings.UniversalTriggersBitsCollapsed;
        _isSubsSectionCollapsed = _settings.UniversalTriggersSubsCollapsed;
        _isFollowsSectionCollapsed = _settings.UniversalTriggersFollowsCollapsed;

        ChatSection = BuildSection(_chatSectionSource, t => t.TriggerType == UniversalTriggerType.ChatCommand);
        RewardSection = BuildSection(_rewardSectionSource, t => t.TriggerType == UniversalTriggerType.ChannelPointReward);
        BitsSection = BuildSection(_bitsSectionSource, t => t.TriggerType == UniversalTriggerType.Bits);
        SubsSection = BuildSection(_subsSectionSource, t => t.TriggerType == UniversalTriggerType.Subscription || t.TriggerType == UniversalTriggerType.GiftSubscription);
        FollowsSection = BuildSection(_followsSectionSource, t => t.TriggerType == UniversalTriggerType.Follow);

        foreach (var rule in _settings.UniversalTriggers)
        {
            AddRuleToSectionSources(rule);
        }

        _settings.UniversalTriggers.CollectionChanged += OnUniversalTriggersCollectionChanged;

        ImportFoomaCommand = new RelayCommand(async () => await ImportFoomaAsync());
        AddNewTriggerCommand = new RelayCommand(AddNewTrigger);
        OpenFoomaHelpCommand = new RelayCommand(OpenFoomaHelp);

        ShowAllCommand = new RelayCommand(ShowAll);
        ShowActiveCommand = new RelayCommand(ShowActive);
        ShowDisabledCommand = new RelayCommand(ShowDisabled);
        ShowNeedsFixCommand = new RelayCommand(ShowNeedsFix);
        ShowFoomaCommand = new RelayCommand(ShowFooma);

        EnableAllCommand = new AsyncRelayCommand(EnableAllAsync);
        DisableAllCommand = new AsyncRelayCommand(DisableAllAsync);

        CollapseAllCommand = new RelayCommand(CollapseAll);
        ExpandAllCommand = new RelayCommand(ExpandAll);

        DisableSectionCommand = new RelayCommand(parameter =>
        {
            if (parameter is string section && !string.IsNullOrWhiteSpace(section))
            {
                _ = DisableSectionAsync(section);
            }
        });

        OpenEditorCommand = new RelayCommand(parameter => OpenEditor(parameter as UniversalTriggerRule));
        CloseEditorCommand = new RelayCommand(CloseEditor);
        TestTriggerCommand = new RelayCommand(parameter => _ = TestTriggerAsync(parameter as UniversalTriggerRule));
        SaveEditorCommand = new AsyncRelayCommand(SaveEditorAsync);
        DeleteSelectedTriggerCommand = new AsyncRelayCommand(DeleteSelectedTriggerAsync);
        TestSelectedTriggerCommand = new AsyncRelayCommand(TestSelectedTriggerAsync);
        AddActionCommand = new RelayCommand(AddAction);
        RemoveActionCommand = new RelayCommand(parameter => RemoveAction(parameter as UniversalTriggerAction));
        DeleteAllCommand = new AsyncRelayCommand(DeleteAllAsync);

        PropertyChanged += OnSelfPropertyChanged;

        RefreshAllSections();
    }

    public ObservableCollection<UniversalTriggerRule> Triggers => _settings.UniversalTriggers;

    public bool IsEmpty => Triggers.Count == 0;

    public string SubtitleSummary
    {
        get
        {
            var total = Triggers.Count;
            if (total == 0) return string.Empty;
            var active = 0;
            var needsFix = 0;
            foreach (var t in Triggers)
            {
                if (!t.IsEnabled) continue;
                active++;
                if (HasWarning(t)) needsFix++;
            }
            return LocalizationService.Format("Universal Triggers Subtitle Summary", total, active, needsFix);
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(IsSearchActive));
                RefreshAllSections();
            }
        }
    }

    public bool IsSearchActive => !string.IsNullOrWhiteSpace(_searchText);

    public UniversalTriggerFilterMode FilterMode
    {
        get => _filterMode;
        set
        {
            if (!SetProperty(ref _filterMode, value)) return;
            RaiseFilterChipTextChanged();
            RefreshAllSections();
        }
    }

    public UniversalTriggerSortMode SortMode
    {
        get => _sortMode;
        set
        {
            if (!SetProperty(ref _sortMode, value)) return;
            RefreshAllSections();
        }
    }

    public IReadOnlyList<UniversalTriggerSortMode> SortModeOptions { get; } =
        (UniversalTriggerSortMode[])Enum.GetValues(typeof(UniversalTriggerSortMode));

    public IReadOnlyList<UniversalTriggerType> UniversalTriggerTypes { get; } =
        (UniversalTriggerType[])Enum.GetValues(typeof(UniversalTriggerType));

    public IReadOnlyList<ChatCommandPermission> ChatCommandPermissions { get; } =
        (ChatCommandPermission[])Enum.GetValues(typeof(ChatCommandPermission));

    public IReadOnlyList<UniversalTriggerValueKind> UniversalTriggerValueKinds { get; } =
        (UniversalTriggerValueKind[])Enum.GetValues(typeof(UniversalTriggerValueKind));

    public ICollectionView ChatSection { get; }

    public ICollectionView RewardSection { get; }

    public ICollectionView BitsSection { get; }

    public ICollectionView SubsSection { get; }

    public ICollectionView FollowsSection { get; }

    public int ChatSectionCount => CountOf(ChatSection);
    public int RewardSectionCount => CountOf(RewardSection);
    public int BitsSectionCount => CountOf(BitsSection);
    public int SubsSectionCount => CountOf(SubsSection);
    public int FollowsSectionCount => CountOf(FollowsSection);

    public string ChatSectionSuffix => BuildSuffix(ChatSection);
    public string RewardSectionSuffix => BuildSuffix(RewardSection);
    public string BitsSectionSuffix => BuildSuffix(BitsSection);
    public string SubsSectionSuffix => BuildSuffix(SubsSection);
    public string FollowsSectionSuffix => BuildSuffix(FollowsSection);

    public UniversalTriggerRule? SelectedTrigger
    {
        get => _selectedTrigger;
        set
        {
            if (!SetProperty(ref _selectedTrigger, value)) return;
            _editorSnapshot = value is null ? null : SafeCreateSnapshot(value);
        }
    }

    private static UniversalTriggerRuleSnapshot? SafeCreateSnapshot(UniversalTriggerRule rule)
    {
        try
        {
            return BridgeRuntimeConfiguration.CreateManualTestSnapshot(rule);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        set => SetProperty(ref _isEditorOpen, value);
    }

    public ObservableCollection<AvatarReadinessRow> AvatarReadinessRows { get; } = [];

    public bool HasNoAvatarParams => AvatarReadinessRows.Count == 0;

    public string RewardVisibilityStatusText
    {
        get
        {
            if (SelectedTrigger is null) return string.Empty;
            if (!SelectedTrigger.UsesChannelPointReward) return string.Empty;
            if (!SelectedTrigger.UsesCreateOrManageReward) return string.Empty;
            if (string.IsNullOrWhiteSpace(SelectedTrigger.RewardId)) return "Pending sync - new reward will be created on Save";
            if (MainWindowViewModel.HasUniversalTriggerAvatarParameterGate(SelectedTrigger)
                && !_mainWindowViewModel.IsUniversalTriggerReadyForCurrentAvatarJson(SelectedTrigger, _mainWindowViewModel.CurrentVrChatAvatarId))
            {
                return "Hidden - current avatar is missing the required avatar parameter(s)";
            }
            return "Visible";
        }
    }

    public int CountAll => Triggers.Count;
    public int CountActive => Triggers.Count(t => t.IsEnabled);
    public int CountDisabled => Triggers.Count(t => !t.IsEnabled);
    public int CountNeedsFix => Triggers.Count(t => t.IsEnabled && HasWarning(t));
    public int CountFooma => Triggers.Count(t => FoomaInteractionConfigImporter.IsFoomaImport(t));

    public string AllFilterText => string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} ({1})", LocalizationService.Translate("Universal Triggers Filter All"), CountAll);
    public string ActiveFilterText => string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} ({1})", LocalizationService.Translate("Universal Triggers Filter Active"), CountActive);
    public string DisabledFilterText => string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} ({1})", LocalizationService.Translate("Universal Triggers Filter Disabled"), CountDisabled);
    public string NeedsFixFilterText => string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} ({1})", LocalizationService.Translate("Universal Triggers Filter Needs Fix"), CountNeedsFix);
    public string FoomaFilterText => string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} ({1})", LocalizationService.Translate("Universal Triggers Filter From Fooma"), CountFooma);

    public bool IsChatSectionCollapsed
    {
        get => _isChatSectionCollapsed;
        set => SetProperty(ref _isChatSectionCollapsed, value);
    }

    public bool IsRewardSectionCollapsed
    {
        get => _isRewardSectionCollapsed;
        set => SetProperty(ref _isRewardSectionCollapsed, value);
    }

    public bool IsBitsSectionCollapsed
    {
        get => _isBitsSectionCollapsed;
        set => SetProperty(ref _isBitsSectionCollapsed, value);
    }

    public bool IsSubsSectionCollapsed
    {
        get => _isSubsSectionCollapsed;
        set => SetProperty(ref _isSubsSectionCollapsed, value);
    }

    public bool IsFollowsSectionCollapsed
    {
        get => _isFollowsSectionCollapsed;
        set => SetProperty(ref _isFollowsSectionCollapsed, value);
    }

    public RelayCommand ImportFoomaCommand { get; }

    public RelayCommand AddNewTriggerCommand { get; }

    public RelayCommand OpenFoomaHelpCommand { get; }

    public RelayCommand ShowAllCommand { get; }

    public RelayCommand ShowActiveCommand { get; }

    public RelayCommand ShowDisabledCommand { get; }

    public RelayCommand ShowNeedsFixCommand { get; }

    public RelayCommand ShowFoomaCommand { get; }

    public AsyncRelayCommand EnableAllCommand { get; }

    public AsyncRelayCommand DisableAllCommand { get; }

    public RelayCommand CollapseAllCommand { get; }

    public RelayCommand ExpandAllCommand { get; }

    public RelayCommand DisableSectionCommand { get; }

    public RelayCommand OpenEditorCommand { get; }

    public RelayCommand CloseEditorCommand { get; }

    public RelayCommand TestTriggerCommand { get; }

    public AsyncRelayCommand SaveEditorCommand { get; }

    public AsyncRelayCommand DeleteSelectedTriggerCommand { get; }

    public IReadOnlyList<TwitchRewardSyncModeOption> RewardSyncModeOptions => _mainWindowViewModel.RewardSyncModeOptions;

    public IReadOnlyList<AvatarScaleSubscriptionTierOption> SubscriptionTierOptions => _mainWindowViewModel.AvatarScaleSubscriptionTierOptions;

    public AsyncRelayCommand TestSelectedTriggerCommand { get; }

    public RelayCommand AddActionCommand { get; }

    public RelayCommand RemoveActionCommand { get; }

    public AsyncRelayCommand DeleteAllCommand { get; }

    public async Task ImportFoomaAsync()
    {
        await _mainWindowViewModel.ImportFoomaAndSyncAsync();
        RaisePropertyChanged(nameof(IsEmpty));
        RaisePropertyChanged(nameof(SubtitleSummary));
        RaiseCountsChanged();
    }

    public void AddNewTrigger()
    {
        var rule = new UniversalTriggerRule
        {
            Id = Guid.NewGuid(),
            Name = LocalizationService.Translate("Universal Triggers New Trigger Default Name"),
            TriggerType = UniversalTriggerType.ChatCommand,
            IsEnabled = true,
            ChatCommandEnabled = true,
            CommandText = "!example",
            ChatCommandPermission = ChatCommandPermission.Everyone,
        };
        _editorIsNew = true;
        _settings.UniversalTriggers.Add(rule);
        FilterMode = UniversalTriggerFilterMode.All;
        RaisePropertyChanged(nameof(IsEmpty));
        RaisePropertyChanged(nameof(SubtitleSummary));
        RaiseCountsChanged();
        RefreshAllSections();
        SelectedTrigger = rule;
        IsEditorOpen = true;
        RefreshAvatarReadiness();
        RaisePropertyChanged(nameof(RewardVisibilityStatusText));
    }

    private void ShowAll() => FilterMode = UniversalTriggerFilterMode.All;
    private void ShowActive() => FilterMode = UniversalTriggerFilterMode.Active;
    private void ShowDisabled() => FilterMode = UniversalTriggerFilterMode.Disabled;
    private void ShowNeedsFix() => FilterMode = UniversalTriggerFilterMode.NeedsFix;
    private void ShowFooma() => FilterMode = UniversalTriggerFilterMode.FromFooma;

    private async Task EnableAllAsync()
    {
        foreach (var t in Triggers)
        {
            t.IsEnabled = true;
        }
        await _mainWindowViewModel.SaveSettingsAsync();
        RaiseCountsChanged();
    }

    private async Task DisableAllAsync()
    {
        foreach (var t in Triggers)
        {
            t.IsEnabled = false;
        }
        await _mainWindowViewModel.SaveSettingsAsync();
        RaiseCountsChanged();
    }

    private void CollapseAll()
    {
        IsChatSectionCollapsed = true;
        IsRewardSectionCollapsed = true;
        IsBitsSectionCollapsed = true;
        IsSubsSectionCollapsed = true;
        IsFollowsSectionCollapsed = true;
        PersistCollapseFlags();
    }

    private void ExpandAll()
    {
        IsChatSectionCollapsed = false;
        IsRewardSectionCollapsed = false;
        IsBitsSectionCollapsed = false;
        IsSubsSectionCollapsed = false;
        IsFollowsSectionCollapsed = false;
        PersistCollapseFlags();
    }

    private void PersistCollapseFlags()
    {
        _settings.UniversalTriggersChatCollapsed = IsChatSectionCollapsed;
        _settings.UniversalTriggersRewardCollapsed = IsRewardSectionCollapsed;
        _settings.UniversalTriggersBitsCollapsed = IsBitsSectionCollapsed;
        _settings.UniversalTriggersSubsCollapsed = IsSubsSectionCollapsed;
        _settings.UniversalTriggersFollowsCollapsed = IsFollowsSectionCollapsed;
        _ = _mainWindowViewModel.SaveSettingsAsync();
    }

    private void OpenEditor(UniversalTriggerRule? rule)
    {
        if (rule is null)
        {
            SelectedTrigger = null;
            IsEditorOpen = false;
            RefreshAvatarReadiness();
            return;
        }
        SelectedTrigger = rule;
        IsEditorOpen = true;
        RefreshAvatarReadiness();
        RaisePropertyChanged(nameof(RewardVisibilityStatusText));
    }

    private void CloseEditor()
    {
        if (_editorIsNew && SelectedTrigger is not null)
        {
            _settings.UniversalTriggers.Remove(SelectedTrigger);
            RaisePropertyChanged(nameof(IsEmpty));
            RaisePropertyChanged(nameof(SubtitleSummary));
            RaiseCountsChanged();
            RefreshAllSections();
        }
        else if (SelectedTrigger is not null && _editorSnapshot is not null)
        {
            RestoreFromSnapshot(SelectedTrigger, _editorSnapshot);
        }
        IsEditorOpen = false;
        SelectedTrigger = null;
        _editorSnapshot = null;
        _editorIsNew = false;
        RefreshAvatarReadiness();
    }

    private async Task TestTriggerAsync(UniversalTriggerRule? rule)
    {
        if (rule is null) return;
        var snapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(rule);
        if (snapshot is null) return;
        await _mainWindowViewModel.Coordinator.SendTestUniversalTriggerAsync(snapshot, CancellationToken.None).ConfigureAwait(true);
    }

    private async Task TestSelectedTriggerAsync()
    {
        if (SelectedTrigger is null) return;
        await TestTriggerAsync(SelectedTrigger).ConfigureAwait(true);
    }

    private async Task SaveEditorAsync()
    {
        if (SelectedTrigger is null) return;
        await _mainWindowViewModel.SaveSettingsAsync().ConfigureAwait(true);
        await _mainWindowViewModel.SynchronizeUniversalManagedRewardsAsync().ConfigureAwait(true);
        _editorIsNew = false;
        IsEditorOpen = false;
        SelectedTrigger = null;
        _editorSnapshot = null;
        RefreshAllSections();
    }

    private async Task DeleteSelectedTriggerAsync()
    {
        if (SelectedTrigger is null) return;
        var title = LocalizationService.Translate("Universal Triggers Delete Confirm Title");
        var body = LocalizationService.Translate("Universal Triggers Delete Confirm Body");
        var owner = Application.Current?.MainWindow;
        var ok = ThemedDialogWindow.ShowYesNo(owner, _settings.Theme, title, body);
        if (!ok) return;

        var ruleToRemove = SelectedTrigger;
        IsEditorOpen = false;
        SelectedTrigger = null;
        _editorSnapshot = null;
        _settings.UniversalTriggers.Remove(ruleToRemove);
        await _mainWindowViewModel.SaveSettingsAsync().ConfigureAwait(true);
        await _mainWindowViewModel.SynchronizeUniversalManagedRewardsAsync().ConfigureAwait(true);
        RaisePropertyChanged(nameof(IsEmpty));
        RaiseSectionCountsChanged();
        RaiseCountsChanged();
    }

    private async Task DeleteAllAsync()
    {
        var count = _settings.UniversalTriggers.Count;
        if (count == 0) return;
        var title = LocalizationService.Translate("Universal Triggers Delete All Confirm Title");
        var body = LocalizationService.Format("Universal Triggers Delete All Confirm Body", count);
        var owner = Application.Current?.MainWindow;
        var ok = ThemedDialogWindow.ShowYesNo(owner, _settings.Theme, title, body);
        if (!ok) return;

        IsEditorOpen = false;
        SelectedTrigger = null;
        _editorSnapshot = null;
        _settings.UniversalTriggers.Clear();
        await _mainWindowViewModel.SaveSettingsAsync().ConfigureAwait(true);
        await _mainWindowViewModel.SynchronizeUniversalManagedRewardsAsync().ConfigureAwait(true);
        RaisePropertyChanged(nameof(IsEmpty));
        RaiseSectionCountsChanged();
        RaiseCountsChanged();
    }

    private static void RestoreFromSnapshot(UniversalTriggerRule rule, UniversalTriggerRuleSnapshot snapshot)
    {
        rule.IsEnabled = snapshot.IsEnabled;
        rule.Name = snapshot.Name;
        rule.TriggerType = snapshot.TriggerType;
        rule.ChatCommandEnabled = snapshot.ChatCommandEnabled;
        rule.CommandText = snapshot.CommandText;
        rule.ChatCommandPermission = snapshot.ChatCommandPermission;
        rule.RewardId = snapshot.RewardId;
        rule.RewardTitle = snapshot.RewardTitle;
        rule.MinimumBits = snapshot.MinimumBits;
        rule.MaximumBits = snapshot.MaximumBits;
        rule.SubscriptionTier = snapshot.SubscriptionTier;
        rule.MinimumMonths = snapshot.MinimumMonths;
        rule.MaximumMonths = snapshot.MaximumMonths;
        rule.GlobalDelaySeconds = snapshot.GlobalDelaySeconds;
        rule.UserDelaySeconds = snapshot.UserDelaySeconds;
        rule.ExecuteRandomAction = snapshot.ExecuteRandomAction;
        rule.Actions.Clear();
        foreach (var actionSnapshot in snapshot.Actions)
        {
            rule.Actions.Add(new UniversalTriggerAction
            {
                Id = Guid.NewGuid(),
                OscAddress = actionSnapshot.OscAddress,
                ValueKind = actionSnapshot.ValueKind,
                TargetValue = actionSnapshot.TargetValue,
                DefaultValue = actionSnapshot.DefaultValue,
                DurationSeconds = actionSnapshot.DurationSeconds,
                AddToQueue = actionSnapshot.AddToQueue,
            });
        }
    }

    private void OpenFoomaHelp()
    {
        var title = LocalizationService.Translate("Universal Triggers Fooma Help Title");
        var body = LocalizationService.Translate("Universal Triggers Fooma Help Body");
        var yesText = LocalizationService.Translate("Universal Triggers Fooma Help Yes");
        var noText = LocalizationService.Translate("Universal Triggers Fooma Help No");
        var owner = Application.Current?.MainWindow;
        var openLink = ThemedDialogWindow.ShowYesNo(owner, _settings.Theme, title, body, yesText, noText);
        if (!openLink) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = FoomaHelpUrl,
                UseShellExecute = true,
            });
        }
        catch
        {
        }
    }

    private void RaiseCountsChanged()
    {
        RaisePropertyChanged(nameof(CountAll));
        RaisePropertyChanged(nameof(CountActive));
        RaisePropertyChanged(nameof(CountDisabled));
        RaisePropertyChanged(nameof(CountNeedsFix));
        RaisePropertyChanged(nameof(CountFooma));
        RaisePropertyChanged(nameof(SubtitleSummary));
        RaiseFilterChipTextChanged();
        RaiseSectionCountsChanged();
        RefreshAllSections();
    }

    private void RaiseFilterChipTextChanged()
    {
        RaisePropertyChanged(nameof(AllFilterText));
        RaisePropertyChanged(nameof(ActiveFilterText));
        RaisePropertyChanged(nameof(DisabledFilterText));
        RaisePropertyChanged(nameof(NeedsFixFilterText));
        RaisePropertyChanged(nameof(FoomaFilterText));
    }

    private void RaiseSectionCountsChanged()
    {
        RaisePropertyChanged(nameof(ChatSectionCount));
        RaisePropertyChanged(nameof(RewardSectionCount));
        RaisePropertyChanged(nameof(BitsSectionCount));
        RaisePropertyChanged(nameof(SubsSectionCount));
        RaisePropertyChanged(nameof(FollowsSectionCount));
        RaisePropertyChanged(nameof(ChatSectionSuffix));
        RaisePropertyChanged(nameof(RewardSectionSuffix));
        RaisePropertyChanged(nameof(BitsSectionSuffix));
        RaisePropertyChanged(nameof(SubsSectionSuffix));
        RaisePropertyChanged(nameof(FollowsSectionSuffix));
    }

    private void RefreshAllSections()
    {
        var views = new[] { ChatSection, RewardSection, BitsSection, SubsSection, FollowsSection };
        foreach (var view in views)
        {
            view.Refresh();
            ApplySort(view);
        }
        RaiseSectionCountsChanged();
    }

    private void ApplySort(ICollectionView view)
    {
        view.SortDescriptions.Clear();
        switch (SortMode)
        {
            case UniversalTriggerSortMode.ByName:
                view.SortDescriptions.Add(new SortDescription(nameof(UniversalTriggerRule.Name), ListSortDirection.Ascending));
                break;
            case UniversalTriggerSortMode.ByStatus:
                view.SortDescriptions.Add(new SortDescription(nameof(UniversalTriggerRule.IsEnabled), ListSortDirection.Descending));
                break;
            case UniversalTriggerSortMode.ByType:
            default:
                view.SortDescriptions.Add(new SortDescription(nameof(UniversalTriggerRule.TriggerType), ListSortDirection.Ascending));
                break;
        }
    }

    private ICollectionView BuildSection(ObservableCollection<UniversalTriggerCardViewModel> source, Predicate<UniversalTriggerRule> typeFilter)
    {
        var view = CollectionViewSource.GetDefaultView(source);
        view.Filter = o =>
        {
            if (o is not UniversalTriggerCardViewModel card) return false;
            if (!typeFilter(card.Rule)) return false;
            if (!MatchesFilterMode(card.Rule)) return false;
            if (!MatchesSearchText(card.Rule)) return false;
            return true;
        };
        return view;
    }

    private bool MatchesFilterMode(UniversalTriggerRule t) => FilterMode switch
    {
        UniversalTriggerFilterMode.All => true,
        UniversalTriggerFilterMode.Active => t.IsEnabled,
        UniversalTriggerFilterMode.Disabled => !t.IsEnabled,
        UniversalTriggerFilterMode.NeedsFix => t.IsEnabled && HasWarning(t),
        UniversalTriggerFilterMode.FromFooma => FoomaInteractionConfigImporter.IsFoomaImport(t),
        _ => true,
    };

    private bool MatchesSearchText(UniversalTriggerRule t)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var query = SearchText.Trim();
        return ContainsIgnoreCase(t.Name, query)
            || ContainsIgnoreCase(t.CommandText, query)
            || ContainsIgnoreCase(t.RewardTitle, query)
            || t.Actions.Any(a => ContainsIgnoreCase(a.OscAddress, query));
    }

    private static bool ContainsIgnoreCase(string? value, string query) =>
        !string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static int CountOf(ICollectionView view) => view.Cast<object>().Count();

    private void OnUniversalTriggersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (UniversalTriggerRule rule in e.NewItems)
            {
                AddRuleToSectionSources(rule);
            }
        }

        if (e.OldItems != null)
        {
            foreach (UniversalTriggerRule rule in e.OldItems)
            {
                RemoveRuleFromSectionSources(rule);
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _chatSectionSource.Clear();
            _rewardSectionSource.Clear();
            _bitsSectionSource.Clear();
            _subsSectionSource.Clear();
            _followsSectionSource.Clear();
            _cardLookup.Clear();
            foreach (var rule in _settings.UniversalTriggers)
            {
                AddRuleToSectionSources(rule);
            }
        }

        RaiseCountsChanged();
    }

    private void AddRuleToSectionSources(UniversalTriggerRule rule)
    {
        var card = GetCard(rule);
        switch (rule.TriggerType)
        {
            case UniversalTriggerType.ChatCommand:
                _chatSectionSource.Add(card);
                break;
            case UniversalTriggerType.ChannelPointReward:
                _rewardSectionSource.Add(card);
                break;
            case UniversalTriggerType.Bits:
                _bitsSectionSource.Add(card);
                break;
            case UniversalTriggerType.Subscription:
            case UniversalTriggerType.GiftSubscription:
                _subsSectionSource.Add(card);
                break;
            case UniversalTriggerType.Follow:
                _followsSectionSource.Add(card);
                break;
        }
    }

    private void RemoveRuleFromSectionSources(UniversalTriggerRule rule)
    {
        if (!_cardLookup.TryGetValue(rule, out var card)) return;
        switch (rule.TriggerType)
        {
            case UniversalTriggerType.ChatCommand:
                _chatSectionSource.Remove(card);
                break;
            case UniversalTriggerType.ChannelPointReward:
                _rewardSectionSource.Remove(card);
                break;
            case UniversalTriggerType.Bits:
                _bitsSectionSource.Remove(card);
                break;
            case UniversalTriggerType.Subscription:
            case UniversalTriggerType.GiftSubscription:
                _subsSectionSource.Remove(card);
                break;
            case UniversalTriggerType.Follow:
                _followsSectionSource.Remove(card);
                break;
        }
    }

    private UniversalTriggerCardViewModel GetCard(UniversalTriggerRule rule)
    {
        if (!_cardLookup.TryGetValue(rule, out var card))
        {
            card = new UniversalTriggerCardViewModel(rule, HasWarning);
            _cardLookup[rule] = card;
        }
        return card;
    }

    private string BuildSuffix(ICollectionView view)
    {
        var rules = view.Cast<UniversalTriggerCardViewModel>().Select(c => c.Rule).ToList();
        var active = rules.Count(t => t.IsEnabled && !HasWarning(t));
        var warn = rules.Count(t => t.IsEnabled && HasWarning(t));
        var off = rules.Count(t => !t.IsEnabled);
        return (warn, off) switch
        {
            (0, 0) => LocalizationService.Format("Universal Triggers Section Active Suffix", active),
            (> 0, 0) => LocalizationService.Format("Universal Triggers Section Active Hidden Suffix", active, warn),
            (0, > 0) => LocalizationService.Format("Universal Triggers Section Off Suffix", active, off),
            _ => LocalizationService.Format("Universal Triggers Section Mixed Suffix", active, warn, off),
        };
    }

    private async Task DisableSectionAsync(string section)
    {
        Predicate<UniversalTriggerRule> match = section switch
        {
            "Chat" => t => t.TriggerType == UniversalTriggerType.ChatCommand,
            "Reward" => t => t.TriggerType == UniversalTriggerType.ChannelPointReward,
            "Bits" => t => t.TriggerType == UniversalTriggerType.Bits,
            "Subs" => t => t.TriggerType == UniversalTriggerType.Subscription || t.TriggerType == UniversalTriggerType.GiftSubscription,
            "Follows" => t => t.TriggerType == UniversalTriggerType.Follow,
            _ => _ => false,
        };
        foreach (var t in _settings.UniversalTriggers.Where(t => match(t)))
        {
            t.IsEnabled = false;
        }
        await _mainWindowViewModel.SaveSettingsAsync();
        RaiseCountsChanged();
    }

    private void OnSelfPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IsChatSectionCollapsed) or nameof(IsRewardSectionCollapsed)
            or nameof(IsBitsSectionCollapsed) or nameof(IsSubsSectionCollapsed)
            or nameof(IsFollowsSectionCollapsed))
        {
            PersistCollapseFlags();
        }
    }

    private bool HasWarning(UniversalTriggerRule rule) =>
        MainWindowViewModel.HasUniversalTriggerAvatarParameterGate(rule)
        && !_mainWindowViewModel.IsUniversalTriggerReadyForCurrentAvatarJson(rule, _mainWindowViewModel.CurrentVrChatAvatarId);

    private void AddAction()
    {
        SelectedTrigger?.Actions.Add(new UniversalTriggerAction
        {
            Id = Guid.NewGuid(),
            OscAddress = "/avatar/parameters/Example",
            ValueKind = UniversalTriggerValueKind.Bool,
            TargetValue = "true",
            DefaultValue = "false",
            DurationSeconds = 1.0,
            AddToQueue = false,
        });
        RefreshAvatarReadiness();
    }

    private void RemoveAction(UniversalTriggerAction? action)
    {
        if (action is null || SelectedTrigger is null) return;
        SelectedTrigger.Actions.Remove(action);
        RefreshAvatarReadiness();
    }

    private void RefreshAvatarReadiness()
    {
        AvatarReadinessRows.Clear();
        var rule = SelectedTrigger;
        if (rule is null) return;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in rule.Actions)
        {
            var rawAddress = action.OscAddress?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawAddress)) continue;
            if (!rawAddress.StartsWith("/avatar/parameters/", StringComparison.OrdinalIgnoreCase)) continue;
            if (action.ValueKind is not (UniversalTriggerValueKind.Bool or UniversalTriggerValueKind.Int or UniversalTriggerValueKind.Float)) continue;
            if (string.IsNullOrWhiteSpace(action.TargetValue)) continue;
            if (!seen.Add(rawAddress)) continue;

            var isFound = _mainWindowViewModel.IsCurrentAvatarParameterAvailable(rawAddress);
            var (statusText, brush) = ResolveAvatarReadinessStatus(isFound);
            AvatarReadinessRows.Add(new AvatarReadinessRow
            {
                Address = rawAddress,
                StatusText = statusText,
                StatusBrush = brush,
            });
        }

        RaisePropertyChanged(nameof(HasNoAvatarParams));
    }

    private (string Text, Brush Brush) ResolveAvatarReadinessStatus(bool isFound)
    {
        var app = Application.Current;
        var foundBrush = (app?.TryFindResource("StatusStripeReadyBrush") as Brush) ?? Brushes.LimeGreen;
        var missingBrush = (app?.TryFindResource("StatusStripeWarnBrush") as Brush) ?? Brushes.Goldenrod;
        if (string.IsNullOrWhiteSpace(_mainWindowViewModel.CurrentVrChatAvatarId))
        {
            return (LocalizationService.Translate("Universal Triggers Editor Avatar Param Unknown"), Brushes.Gray);
        }
        if (isFound)
        {
            return (LocalizationService.Translate("Universal Triggers Editor Avatar Param Found"), foundBrush);
        }
        return (LocalizationService.Translate("Universal Triggers Editor Avatar Param Missing"), missingBrush);
    }
}

public sealed class AvatarReadinessRow
{
    public string Address { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public Brush StatusBrush { get; set; } = Brushes.Gray;
}
