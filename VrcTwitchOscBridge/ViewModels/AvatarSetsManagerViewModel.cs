using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed partial class AvatarSetsManagerViewModel : ObservableObject, IDisposable
{
    private readonly MainWindowViewModel _mainVm;
    private readonly AvatarImageService _imageService;
    private readonly ObservableCollection<AvatarSetCardViewModel> _cardsBacking = [];
    private ICollectionView? _cardsView;
    private FileSystemWatcher? _oscFolderWatcher;

    public ObservableCollection<Models.VrChatOscParameterSummary> AvailableParameters { get; } = [];
    public ObservableCollection<TwitchApiClient.CustomRewardResponse> AvailableTwitchRewards { get; } = [];
    private bool _isLoadingTwitchRewards;
    public bool IsLoadingTwitchRewards
    {
        get => _isLoadingTwitchRewards;
        private set => SetProperty(ref _isLoadingTwitchRewards, value);
    }
    private string _twitchRewardsLoadStatus = string.Empty;
    public string TwitchRewardsLoadStatus
    {
        get => _twitchRewardsLoadStatus;
        private set => SetProperty(ref _twitchRewardsLoadStatus, value);
    }
    private ICollectionView? _filteredParametersView;
    public ICollectionView FilteredParameters
    {
        get
        {
            if (_filteredParametersView == null)
            {
                _filteredParametersView = CollectionViewSource.GetDefaultView(AvailableParameters);
                _filteredParametersView.Filter = obj => ParameterMatchesFilter(obj as Models.VrChatOscParameterSummary);
            }
            return _filteredParametersView;
        }
    }

    public enum ParameterTypeFilterMode
    {
        All,
        Bool,
        Int,
        Float
    }

    private ParameterTypeFilterMode _parameterTypeFilter = ParameterTypeFilterMode.All;
    public ParameterTypeFilterMode ParameterTypeFilter
    {
        get => _parameterTypeFilter;
        set
        {
            if (SetProperty(ref _parameterTypeFilter, value))
            {
                ApplyParameterFilter();
            }
        }
    }

    private string _parameterNameFilter = string.Empty;
    public string ParameterNameFilter
    {
        get => _parameterNameFilter;
        set
        {
            if (SetProperty(ref _parameterNameFilter, value ?? string.Empty))
            {
                ApplyParameterFilter();
            }
        }
    }

    public RelayCommand FilterBoolCommand { get; }
    public RelayCommand FilterIntCommand { get; }
    public RelayCommand FilterFloatCommand { get; }
    public RelayCommand FilterAllCommand { get; }

    private bool _isLoadingParameters;
    public bool IsLoadingParameters
    {
        get => _isLoadingParameters;
        set => SetProperty(ref _isLoadingParameters, value);
    }

    private Models.TriggerRule? _selectedPairingTarget;
    public Models.TriggerRule? SelectedPairingTarget
    {
        get => _selectedPairingTarget;
        set => SetProperty(ref _selectedPairingTarget, value);
    }

    private Guid? _selectedPairingRuleId;
    public Guid? SelectedPairingRuleId
    {
        get => _selectedPairingRuleId;
        set => SetProperty(ref _selectedPairingRuleId, value);
    }

    public AvatarSetsManagerViewModel(MainWindowViewModel mainVm)
    {
        _mainVm = mainVm ?? throw new ArgumentNullException(nameof(mainVm));
        _imageService = new AvatarImageService();

        _cardsView = CollectionViewSource.GetDefaultView(_cardsBacking);
        _cardsView.Filter = OnCardFilter;
        _cardsView.CollectionChanged += (_, _) => RefreshCounts();

        _mainVm.AvatarRuleProfiles.CollectionChanged += OnProfilesCollectionChanged;

        RebuildCards();

        AddNewSetCommand = new RelayCommand(AddNewSet);
        OpenEditorCommand = new RelayCommand(p => OpenEditor((AvatarTriggerProfile?)p));
        CloseEditorCommand = new RelayCommand(CloseEditor);
        EnableAllCommand = new RelayCommand(EnableAll);
        DisableAllCommand = new RelayCommand(DisableAll);
        DeleteAllCommand = new RelayCommand(DeleteAll);
        ShowAllCommand = new RelayCommand(() => FilterMode = AvatarSetsFilterMode.All);
        ShowActiveCommand = new RelayCommand(() => FilterMode = AvatarSetsFilterMode.Active);
        ShowDisabledCommand = new RelayCommand(() => FilterMode = AvatarSetsFilterMode.Disabled);
        ShowLiveNowCommand = new RelayCommand(() => FilterMode = AvatarSetsFilterMode.LiveNow);
        ShowMasterCommand = new RelayCommand(() => FilterMode = AvatarSetsFilterMode.Master);
        SortByNameCommand = new RelayCommand(() => SortMode = AvatarSetsSortMode.ByName);
        SortByStatusCommand = new RelayCommand(() => SortMode = AvatarSetsSortMode.ByStatus);
        SortByRecentCommand = new RelayCommand(() => SortMode = AvatarSetsSortMode.RecentlyEdited);
        DeleteSetCommand = new RelayCommand(DeleteSet, () => SelectedProfile is not null);
        SetModeCommand = new RelayCommand(p => SetMode(p, false));
        WardrobeModeCommand = new RelayCommand(p => SetMode(p, true));
        AddChannelPointRuleCommand = new RelayCommand(() => AddRuleTo(SelectedProfile));
        DeleteChannelPointRuleCommand = new RelayCommand(p => DeleteRuleFrom(SelectedProfile, p as Models.TriggerRule));
        AddWardrobeOutfitCommand = new RelayCommand(() => AddOutfitTo(SelectedProfile));
        DeleteWardrobeOutfitCommand = new RelayCommand(p => DeleteOutfitFrom(SelectedProfile, p as Models.WardrobeOutfit));
        SelectAvatarRuleCommand = new RelayCommand(p => SelectedAvatarRule = p as Models.TriggerRule);
        SelectWardrobeOutfitCommand = new RelayCommand(p => SelectedWardrobeOutfit = p as Models.WardrobeOutfit);
        AddPairedRuleCommand = new RelayCommand(_ => ExecuteAddPairedRule(), _ => SelectedAvatarRule is not null);
        RemovePairedRuleCommand = new RelayCommand(p => ExecuteRemovePairedRule(p as Guid?), _ => SelectedAvatarRule is not null);
        LoadParametersCommand = new RelayCommand(async () => await LoadAvailableParametersAsync());
        LoadTwitchRewardsCommand = new RelayCommand(async () => await LoadTwitchRewardsAsync());
        OpenAvatarPickerCommand = new RelayCommand(p => BridgeOpenAvatarPicker(p as string), p => SelectedProfile is not null);
        UseCurrentVrChatAvatarForProfileCommand = new RelayCommand(BridgeUseCurrentVrChatAvatar, () => SelectedProfile is not null);
        FilterBoolCommand = new RelayCommand(() => ParameterTypeFilter = ParameterTypeFilterMode.Bool);
        FilterIntCommand = new RelayCommand(() => ParameterTypeFilter = ParameterTypeFilterMode.Int);
        FilterFloatCommand = new RelayCommand(() => ParameterTypeFilter = ParameterTypeFilterMode.Float);
        FilterAllCommand = new RelayCommand(() => ParameterTypeFilter = ParameterTypeFilterMode.All);
        InitializeOscWatcher();
    }

    private void InitializeOscWatcher()
    {
        try
        {
            var rootPath = Services.VrChatLocalClientStateService.GetVrChatRootPath();
            if (string.IsNullOrWhiteSpace(rootPath)) return;
            var oscRoot = System.IO.Path.Combine(rootPath, "OSC");
            if (!System.IO.Directory.Exists(oscRoot)) return;

            _oscFolderWatcher = new FileSystemWatcher(oscRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _oscFolderWatcher.Changed += OnOscFileChanged;
            _oscFolderWatcher.Created += OnOscFileChanged;
            _oscFolderWatcher.Renamed += OnOscFileChanged;
        }
        catch
        {
            // Watcher is best-effort; ignore errors
        }
    }

    private DateTime _lastOscReload = DateTime.MinValue;
    private async void OnOscFileChanged(object sender, FileSystemEventArgs e)
    {
        if (e.Name == null || !e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return;
        if (SelectedProfile == null || string.IsNullOrWhiteSpace(SelectedProfile.AvatarId)) return;
        if (!e.Name.Contains(SelectedProfile.AvatarId, StringComparison.OrdinalIgnoreCase)) return;

        // Debounce: skip if we reloaded in the last 500ms
        if ((DateTime.UtcNow - _lastOscReload).TotalMilliseconds < 500) return;
        _lastOscReload = DateTime.UtcNow;

        await LoadAvailableParametersAsync();
    }

    private void SetMode(object? parameter, bool useWardrobe)
    {
        var profile = parameter as AvatarTriggerProfile ?? SelectedProfile;
        if (profile == null) return;
        profile.UseWardrobeMode = useWardrobe;
        RaisePropertyChanged(nameof(SelectedProfile));
    }

    private Models.TriggerRule? _selectedAvatarRule;
    public Models.TriggerRule? SelectedAvatarRule
    {
        get => _selectedAvatarRule;
        set
        {
            if (SetProperty(ref _selectedAvatarRule, value))
            {
                if (value != null) _selectedWardrobeOutfit = null;
                RaisePropertyChanged(nameof(SelectedWardrobeOutfit));
                RaisePropertyChanged(nameof(OtherRulesInSet));
                AddPairedRuleCommand?.NotifyCanExecuteChanged();
                RemovePairedRuleCommand?.NotifyCanExecuteChanged();
                // Load parameters when a rule is selected
                _ = LoadAvailableParametersAsync();
                // Load Twitch custom rewards for the Link-to-listen dropdown
                if (AvailableTwitchRewards.Count == 0)
                {
                    _ = LoadTwitchRewardsAsync();
                }
            }
        }
    }

    private Models.WardrobeOutfit? _selectedWardrobeOutfit;
    public Models.WardrobeOutfit? SelectedWardrobeOutfit
    {
        get => _selectedWardrobeOutfit;
        set
        {
            if (SetProperty(ref _selectedWardrobeOutfit, value))
            {
                if (value != null) _selectedAvatarRule = null;
                RaisePropertyChanged(nameof(SelectedAvatarRule));
            }
        }
    }

    private void AddRuleTo(AvatarTriggerProfile? profile)
    {
        if (profile == null) return;
        var rule = new Models.TriggerRule
        {
            Name = "New Rule",
            TriggerType = Models.TwitchTriggerType.ChannelPoints,
            RewardSyncMode = Models.TwitchRewardSyncMode.CreateOrManage,
            ChannelPointRewardCost = 100,
            ActionType = Models.OscActionType.AvatarParameter,
            ParameterType = Models.OscParameterType.Bool,
            ParameterValue = "true",
            IsEnabled = true
        };
        SubscribeRulePropertyChanged(rule);
        profile.ChannelPointRules.Add(rule);
        _mainVm.QueueManagedRewardSyncPublic();
        SelectedAvatarRule = rule;
    }

    private void DeleteRuleFrom(AvatarTriggerProfile? profile, Models.TriggerRule? rule)
    {
        if (profile == null || rule == null) return;
        UnsubscribeRulePropertyChanged(rule);
        _mainVm.RetireManagedRewardsPublic(new[] { rule });
        profile.ChannelPointRules.Remove(rule);
        if (SelectedAvatarRule == rule) SelectedAvatarRule = null;
    }

    private void AddOutfitTo(AvatarTriggerProfile? profile)
    {
        if (profile == null) return;
        var outfit = new Models.WardrobeOutfit
        {
            Name = "New Outfit",
            IsEnabled = true,
            ActiveTimeSeconds = 60,
            TwitchRewardSyncMode = Models.TwitchRewardSyncMode.CreateOrManage,
            TwitchRewardCost = "100"
        };
        SubscribeOutfitPropertyChanged(outfit);
        profile.WardrobeOutfits.Add(outfit);
        _mainVm.QueueManagedRewardSyncPublic();
        SelectedWardrobeOutfit = outfit;
    }

    private void DeleteOutfitFrom(AvatarTriggerProfile? profile, Models.WardrobeOutfit? outfit)
    {
        if (profile == null || outfit == null) return;
        UnsubscribeOutfitPropertyChanged(outfit);
        _mainVm.RetireWardrobeManagedReward(outfit);
        profile.WardrobeOutfits.Remove(outfit);
        if (SelectedWardrobeOutfit == outfit) SelectedWardrobeOutfit = null;
    }

    public IEnumerable<Models.TriggerRule> OtherRulesInSet
    {
        get
        {
            if (SelectedProfile == null || SelectedAvatarRule == null)
                return Enumerable.Empty<Models.TriggerRule>();
            return SelectedProfile.ChannelPointRules
                .Where(r => r.Id != SelectedAvatarRule.Id && !SelectedAvatarRule.TemporarilyDisabledRuleIds.Contains(r.Id))
                .ToList();
        }
    }

    public IEnumerable<Models.TriggerRule> PairedRules
    {
        get
        {
            if (SelectedProfile == null || SelectedAvatarRule == null)
                return Enumerable.Empty<Models.TriggerRule>();
            return SelectedAvatarRule.TemporarilyDisabledRuleIds
                .Select(id => SelectedProfile.ChannelPointRules.FirstOrDefault(r => r.Id == id))
                .Where(r => r != null)
                .ToList()!;
        }
    }

    private void ExecuteAddPairedRule()
    {
        if (SelectedAvatarRule == null || SelectedPairingTarget == null) return;
        if (SelectedAvatarRule.Id == SelectedPairingTarget.Id) return;
        if (SelectedAvatarRule.TemporarilyDisabledRuleIds.Contains(SelectedPairingTarget.Id)) return;
        SelectedAvatarRule.TemporarilyDisabledRuleIds.Add(SelectedPairingTarget.Id);
        RaisePropertyChanged(nameof(OtherRulesInSet));
        RaisePropertyChanged(nameof(PairedRules));
        SelectedPairingTarget = null;
    }

    private void ExecuteRemovePairedRule(Guid? ruleId)
    {
        if (SelectedAvatarRule == null || !ruleId.HasValue) return;
        SelectedAvatarRule.TemporarilyDisabledRuleIds.Remove(ruleId.Value);
        RaisePropertyChanged(nameof(PairedRules));
    }

    public async Task LoadAvailableParametersAsync()
    {
        if (SelectedProfile == null || string.IsNullOrWhiteSpace(SelectedProfile.AvatarId))
        {
            AvailableParameters.Clear();
            _filteredParametersView?.Refresh();
            return;
        }

        IsLoadingParameters = true;
        try
        {
            var summaries = await _mainVm.LoadAvatarParameterSummariesAsync(SelectedProfile.AvatarId);
            AvailableParameters.Clear();
            foreach (var p in summaries) AvailableParameters.Add(p);
            ApplyParameterFilter();
        }
        finally
        {
            IsLoadingParameters = false;
        }
    }

    public async Task LoadTwitchRewardsAsync()
    {
        IsLoadingTwitchRewards = true;
        try
        {
            var rewards = await _mainVm.LoadTwitchCustomRewardsAsync();
            AvailableTwitchRewards.Clear();
            if (rewards.Count == 0)
            {
                TwitchRewardsLoadStatus = "Connect Twitch as broadcaster to load rewards";
            }
            else
            {
                foreach (var r in rewards) AvailableTwitchRewards.Add(r);
                TwitchRewardsLoadStatus = $"{rewards.Count} reward(s) loaded";
            }
        }
        finally
        {
            IsLoadingTwitchRewards = false;
        }
    }

    private void BridgeOpenAvatarPicker(string? context)
    {
        if (SelectedProfile is null) return;
        _mainVm.SelectedAvatarProfile = SelectedProfile;
        _mainVm.OpenAvatarPickerCommand.Execute(context ?? "Profile");
        RefreshCardThumbnailFor(SelectedProfile);
    }

    private void BridgeUseCurrentVrChatAvatar()
    {
        if (SelectedProfile is null) return;
        _mainVm.SelectedAvatarProfile = SelectedProfile;
        _mainVm.UseCurrentVrChatAvatarForProfileCommand.Execute(null);
        RefreshCardThumbnailFor(SelectedProfile);
    }

    private void ApplyParameterFilter()
    {
        if (_filteredParametersView != null) _filteredParametersView.Refresh();
    }

    private bool ParameterMatchesFilter(Models.VrChatOscParameterSummary? p)
    {
        if (p == null) return false;
        if (ParameterTypeFilter != ParameterTypeFilterMode.All && p.ParameterType.ToString() != ParameterTypeFilter.ToString())
            return false;
        var nameQuery = (ParameterNameFilter ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(nameQuery) && p.Name.IndexOf(nameQuery, StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        return true;
    }

    public ICollectionView Cards => _cardsView ?? throw new InvalidOperationException("Cards view not initialized");

    public IReadOnlyList<AvatarSetsSortMode> SortModeOptions { get; } =
        Enum.GetValues<AvatarSetsSortMode>().ToList().AsReadOnly();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                ApplyFilterSort();
            }
        }
    }

    private string _searchText = string.Empty;

    public AvatarSetsFilterMode FilterMode
    {
        get => _filterMode;
        set
        {
            if (SetProperty(ref _filterMode, value))
            {
                RefreshCounts();
                ApplyFilterSort();
            }
        }
    }

    private AvatarSetsFilterMode _filterMode = AvatarSetsFilterMode.All;

    public AvatarSetsSortMode SortMode
    {
        get => _sortMode;
        set
        {
            if (SetProperty(ref _sortMode, value))
            {
                ApplyFilterSort();
            }
        }
    }

    private AvatarSetsSortMode _sortMode = AvatarSetsSortMode.ByName;

    public AvatarTriggerProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetProperty(ref _selectedProfile, value)) return;
            DeleteSetCommand.NotifyCanExecuteChanged();
            OpenAvatarPickerCommand.NotifyCanExecuteChanged();
            UseCurrentVrChatAvatarForProfileCommand.NotifyCanExecuteChanged();
        }
    }

    private AvatarTriggerProfile? _selectedProfile;

    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        set => SetProperty(ref _isEditorOpen, value);
    }

    private bool _isEditorOpen;

    public string SubtitleSummary => TF("Avatar Sets Subtitle Format", CountAll, CountActive, CountDisabled);

    public bool IsEmpty => CountAll == 0;

    public int CountAll => _cardsBacking.Count;
    public int CountActive => _cardsBacking.Count(c => c.IsEnabled);
    public int CountDisabled => _cardsBacking.Count(c => c.IsDisabled);
    public int CountLiveNow => _cardsBacking.Count(c => c.IsLive);
    public int CountMaster => _cardsBacking.Count(c => c.IsMaster);

    public string AllFilterText => TF("Avatar Sets Filter All With Count", CountAll);
    public string ActiveFilterText => TF("Avatar Sets Filter Active With Count", CountActive);
    public string DisabledFilterText => TF("Avatar Sets Filter Disabled With Count", CountDisabled);
    public string LiveNowFilterText => TF("Avatar Sets Filter Live With Count", CountLiveNow);
    public string MasterFilterText => TF("Avatar Sets Filter Master With Count", CountMaster);

    public RelayCommand AddNewSetCommand { get; }
    public RelayCommand OpenEditorCommand { get; }
    public RelayCommand CloseEditorCommand { get; }
    public RelayCommand EnableAllCommand { get; }
    public RelayCommand DisableAllCommand { get; }
    public RelayCommand DeleteAllCommand { get; }
    public RelayCommand ShowAllCommand { get; }
    public RelayCommand ShowActiveCommand { get; }
    public RelayCommand ShowDisabledCommand { get; }
    public RelayCommand ShowLiveNowCommand { get; }
    public RelayCommand ShowMasterCommand { get; }
    public RelayCommand SortByNameCommand { get; }
    public RelayCommand SortByStatusCommand { get; }
    public RelayCommand SortByRecentCommand { get; }
    public RelayCommand DeleteSetCommand { get; }
    public RelayCommand SetModeCommand { get; }
    public RelayCommand WardrobeModeCommand { get; }
    public RelayCommand AddChannelPointRuleCommand { get; }
    public RelayCommand DeleteChannelPointRuleCommand { get; }
    public RelayCommand AddWardrobeOutfitCommand { get; }
    public RelayCommand DeleteWardrobeOutfitCommand { get; }
    public RelayCommand SelectAvatarRuleCommand { get; }
    public RelayCommand SelectWardrobeOutfitCommand { get; }
    public RelayCommand AddPairedRuleCommand { get; }
    public RelayCommand RemovePairedRuleCommand { get; }
    public RelayCommand LoadParametersCommand { get; }
    public RelayCommand LoadTwitchRewardsCommand { get; }
    public RelayCommand OpenAvatarPickerCommand { get; }
    public RelayCommand UseCurrentVrChatAvatarForProfileCommand { get; }

    private void AddNewSet()
    {
        _mainVm.AddAvatarProfileCommand.Execute(null);
        // The new profile was added to AvatarRuleProfiles; find it and open editor
        var newProfile = _mainVm.AvatarRuleProfiles.LastOrDefault();
        if (newProfile != null)
        {
            SelectedProfile = newProfile;
            IsEditorOpen = true;
        }
    }

    private void OpenEditor(AvatarTriggerProfile? profile)
    {
        if (profile is null) return;
        SelectedProfile = profile;
        IsEditorOpen = true;
    }

    private void CloseEditor()
    {
        IsEditorOpen = false;
        SelectedProfile = null;
    }

    private void EnableAll()
    {
        foreach (var profile in _mainVm.AvatarRuleProfiles)
        {
            profile.IsEnabled = true;
        }
    }

    private void DisableAll()
    {
        foreach (var profile in _mainVm.AvatarRuleProfiles)
        {
            profile.IsEnabled = false;
        }
    }

    private void DeleteAll()
    {
        var count = _mainVm.AvatarRuleProfiles.Count;
        if (count == 0) return;

        var title = LocalizationService.Translate("Avatar Sets Delete All Confirm");
        var body = LocalizationService.Format("Avatar Sets Delete All Confirm", count);
        var owner = Application.Current?.MainWindow;
        var ok = ThemedDialogWindow.ShowYesNo(owner, _mainVm.Settings.Theme, title, body);
        if (!ok) return;

        IsEditorOpen = false;
        SelectedProfile = null;
        _mainVm.DeleteAllAvatarProfilesCommand.Execute(null);
    }

    private void DeleteSet()
    {
        var profile = SelectedProfile;
        if (profile is null) return;

        var title = LocalizationService.Translate("Avatar Sets Delete Set Confirm");
        var body = LocalizationService.Translate("Avatar Sets Delete Set Confirm");
        var owner = Application.Current?.MainWindow;
        var ok = ThemedDialogWindow.ShowYesNo(owner, _mainVm.Settings.Theme, title, body);
        if (!ok) return;

        IsEditorOpen = false;
        SelectedProfile = null;
        _mainVm.DeleteAvatarProfilePublic(profile);
    }

    private void RebuildCards()
    {
        _cardsBacking.Clear();
        foreach (var profile in _mainVm.AvatarRuleProfiles)
        {
            var card = new AvatarSetCardViewModel(profile, _imageService);
            card.SetThumbnailUrl(_mainVm.TryGetVrChatAvatarThumbnailUrl(profile.AvatarId));

            card.OpenEditorCommand = new RelayCommand(() =>
            {
                SelectedProfile = profile;
                IsEditorOpen = true;
            });

            card.TestCommand = new RelayCommand(() =>
            {
                _mainVm.TestAvatarSet(profile);
            });

            _cardsBacking.Add(card);
        }

        RefreshCounts();
    }

    public void RefreshCardThumbnailFor(AvatarTriggerProfile profile)
    {
        var card = _cardsBacking.FirstOrDefault(c => c.Profile == profile);
        if (card == null) return;
        card.SetThumbnailUrl(_mainVm.TryGetVrChatAvatarThumbnailUrl(profile.AvatarId));
    }

    private void OnProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (AvatarTriggerProfile newProfile in e.NewItems)
            {
                newProfile.ChannelPointRules.CollectionChanged += OnProfileChannelPointRulesChanged;
                newProfile.WardrobeOutfits.CollectionChanged += OnProfileWardrobeOutfitsChanged;
                foreach (var existingRule in newProfile.ChannelPointRules)
                {
                    SubscribeRulePropertyChanged(existingRule);
                }
                foreach (var existingOutfit in newProfile.WardrobeOutfits)
                {
                    SubscribeOutfitPropertyChanged(existingOutfit);
                }
            }
        }

        if (e.OldItems != null)
        {
            foreach (AvatarTriggerProfile oldProfile in e.OldItems)
            {
                oldProfile.ChannelPointRules.CollectionChanged -= OnProfileChannelPointRulesChanged;
                oldProfile.WardrobeOutfits.CollectionChanged -= OnProfileWardrobeOutfitsChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (AvatarTriggerProfile profile in e.NewItems)
            {
                var card = new AvatarSetCardViewModel(profile, _imageService);

                card.OpenEditorCommand = new RelayCommand(() =>
                {
                    SelectedProfile = profile;
                    IsEditorOpen = true;
                });

                card.TestCommand = new RelayCommand(() =>
                {
                    _mainVm.TestAvatarSet(profile);
                });

                _cardsBacking.Add(card);
            }
        }

        if (e.OldItems != null)
        {
            foreach (AvatarTriggerProfile profile in e.OldItems)
            {
                var card = _cardsBacking.FirstOrDefault(c => c.Profile == profile);
                if (card != null)
                {
                    _cardsBacking.Remove(card);
                    card.Dispose();
                }
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var card in _cardsBacking)
            {
                card.Dispose();
            }
            _cardsBacking.Clear();
        }

        RefreshCounts();
    }

    private bool OnCardFilter(object obj)
    {
        if (obj is not AvatarSetCardViewModel card) return false;

        // Filter by mode
        if (!MatchesFilterMode(card)) return false;

        // Filter by search text
        if (!MatchesSearchText(card)) return false;

        return true;
    }

    private bool MatchesFilterMode(AvatarSetCardViewModel card) => FilterMode switch
    {
        AvatarSetsFilterMode.All => true,
        AvatarSetsFilterMode.Active => card.IsEnabled,
        AvatarSetsFilterMode.Disabled => card.IsDisabled,
        AvatarSetsFilterMode.LiveNow => card.IsLive,
        AvatarSetsFilterMode.Master => card.IsMaster,
        _ => true,
    };

    private bool MatchesSearchText(AvatarSetCardViewModel card)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var query = SearchText.Trim();
        return card.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase)
            || card.AvatarSubtitle.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyFilterSort()
    {
        var view = _cardsView;
        if (view == null) return;

        view.Refresh();

        // Apply sort
        view.SortDescriptions.Clear();
        switch (SortMode)
        {
            case AvatarSetsSortMode.ByName:
                view.SortDescriptions.Add(new SortDescription(nameof(AvatarSetCardViewModel.DisplayTitle), ListSortDirection.Ascending));
                break;
            case AvatarSetsSortMode.ByStatus:
                view.SortDescriptions.Add(new SortDescription(nameof(AvatarSetCardViewModel.IsEnabled), ListSortDirection.Descending));
                view.SortDescriptions.Add(new SortDescription(nameof(AvatarSetCardViewModel.DisplayTitle), ListSortDirection.Ascending));
                break;
            case AvatarSetsSortMode.RecentlyEdited:
                // AvatarTriggerProfile doesn't track LastEdited, so sort by insertion order (ByName as fallback)
                view.SortDescriptions.Add(new SortDescription(nameof(AvatarSetCardViewModel.DisplayTitle), ListSortDirection.Ascending));
                break;
        }
    }

    private void RefreshCounts()
    {
        RaisePropertyChanged(nameof(CountAll));
        RaisePropertyChanged(nameof(CountActive));
        RaisePropertyChanged(nameof(CountDisabled));
        RaisePropertyChanged(nameof(CountLiveNow));
        RaisePropertyChanged(nameof(CountMaster));
        RaisePropertyChanged(nameof(SubtitleSummary));
        RaisePropertyChanged(nameof(IsEmpty));
        RaisePropertyChanged(nameof(AllFilterText));
        RaisePropertyChanged(nameof(ActiveFilterText));
        RaisePropertyChanged(nameof(DisabledFilterText));
        RaisePropertyChanged(nameof(LiveNowFilterText));
        RaisePropertyChanged(nameof(MasterFilterText));
    }

    private static string TF(string key, params object[] args) =>
        LocalizationService.Format(key, args);

    private static readonly string[] _ruleSyncProperties =
    {
        nameof(Models.TriggerRule.Name),
        nameof(Models.TriggerRule.IsEnabled),
        nameof(Models.TriggerRule.ChannelPointRewardId),
        nameof(Models.TriggerRule.ChannelPointRewardTitle),
        nameof(Models.TriggerRule.ChannelPointRewardCost),
        nameof(Models.TriggerRule.RewardSyncMode),
        nameof(Models.TriggerRule.DeleteManagedRewardWhenInactive),
        nameof(Models.TriggerRule.ManagedRewardReadyColor),
        nameof(Models.TriggerRule.ManagedRewardCooldownColor),
        nameof(Models.TriggerRule.CooldownSeconds)
    };

    private static readonly string[] _outfitSyncProperties =
    {
        nameof(Models.WardrobeOutfit.Name),
        nameof(Models.WardrobeOutfit.IsEnabled),
        nameof(Models.WardrobeOutfit.TwitchRewardId),
        nameof(Models.WardrobeOutfit.TwitchRewardTitle),
        nameof(Models.WardrobeOutfit.TwitchRewardCost),
        nameof(Models.WardrobeOutfit.TwitchRewardSyncMode)
    };

    private void SubscribeRulePropertyChanged(Models.TriggerRule rule)
    {
        rule.PropertyChanged += OnRuleSyncPropertyChanged;
    }

    private void UnsubscribeRulePropertyChanged(Models.TriggerRule rule)
    {
        rule.PropertyChanged -= OnRuleSyncPropertyChanged;
    }

    private void SubscribeOutfitPropertyChanged(Models.WardrobeOutfit outfit)
    {
        outfit.PropertyChanged += OnOutfitSyncPropertyChanged;
    }

    private void UnsubscribeOutfitPropertyChanged(Models.WardrobeOutfit outfit)
    {
        outfit.PropertyChanged -= OnOutfitSyncPropertyChanged;
    }

    private void OnRuleSyncPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null) return;
        if (Array.IndexOf(_ruleSyncProperties, e.PropertyName) < 0) return;
        _mainVm.QueueManagedRewardSyncPublic();
    }

    private void OnOutfitSyncPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null) return;
        if (Array.IndexOf(_outfitSyncProperties, e.PropertyName) < 0) return;
        _mainVm.QueueManagedRewardSyncPublic();
    }

    public void SubscribeAllRulesAndOutfits()
    {
        foreach (var profile in _mainVm.AvatarRuleProfiles)
        {
            foreach (var rule in profile.ChannelPointRules)
            {
                SubscribeRulePropertyChanged(rule);
            }
            foreach (var outfit in profile.WardrobeOutfits)
            {
                SubscribeOutfitPropertyChanged(outfit);
            }
        }
    }

    private void OnProfileChannelPointRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (Models.TriggerRule rule in e.NewItems)
            {
                SubscribeRulePropertyChanged(rule);
            }
        }
        if (e.OldItems != null)
        {
            foreach (Models.TriggerRule rule in e.OldItems)
            {
                UnsubscribeRulePropertyChanged(rule);
            }
        }
    }

    private void OnProfileWardrobeOutfitsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (Models.WardrobeOutfit outfit in e.NewItems)
            {
                SubscribeOutfitPropertyChanged(outfit);
            }
        }
        if (e.OldItems != null)
        {
            foreach (Models.WardrobeOutfit outfit in e.OldItems)
            {
                UnsubscribeOutfitPropertyChanged(outfit);
            }
        }
    }

    public void Dispose()
    {
        _mainVm.AvatarRuleProfiles.CollectionChanged -= OnProfilesCollectionChanged;
        _oscFolderWatcher?.Dispose();
        _oscFolderWatcher = null;
        foreach (var card in _cardsBacking)
        {
            card.Dispose();
        }
        _cardsBacking.Clear();
    }
}