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

    public IReadOnlyList<Models.OscParameterType> ParameterTypes { get; } =
        [Models.OscParameterType.Bool, Models.OscParameterType.Int, Models.OscParameterType.Float];

    public IReadOnlyList<string> BoolValueOptions { get; } = ["True", "False"];

    public IReadOnlyList<TwitchRewardSyncModeOption> RewardSyncModeOptions => _mainVm.RewardSyncModeOptions;

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
        AddWardrobeSnapshotParamCommand = new RelayCommand(AddWardrobeSnapshotParam, () => SelectedWardrobeOutfit is not null);
        RemoveWardrobeSnapshotParamCommand = new RelayCommand(RemoveWardrobeSnapshotParam, () => SelectedWardrobeSnapshotParam is not null);
        CopyWardrobeOutfitCommand = new RelayCommand(CopyWardrobeOutfit, () => SelectedWardrobeOutfit is not null);
        PasteWardrobeOutfitCommand = new RelayCommand(PasteWardrobeOutfit, () => SelectedProfile is not null && _copiedWardrobeOutfit is not null);
        CopyWardrobeSnapshotParamCommand = new RelayCommand(CopyWardrobeSnapshotParam, () => SelectedWardrobeSnapshotParam is not null);
        PasteWardrobeSnapshotParamCommand = new RelayCommand(PasteWardrobeSnapshotParam, () => SelectedWardrobeOutfit is not null && _copiedWardrobeSnapshotParam is not null);
        RefreshWardrobeParametersCommand = new AsyncRelayCommand(RefreshWardrobeParametersAsync);
        TestWardrobeOutfitCommand = new AsyncRelayCommand(TestWardrobeOutfitAsync, () => SelectedWardrobeOutfit is not null && SelectedProfile is not null);
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
    private Models.WardrobeSnapshotParam? _selectedWardrobeSnapshotParam;
    private Models.VrChatOscParameterSummary? _selectedWardrobeParameterOption;
    private string _wardrobeParameterText = string.Empty;
    private IReadOnlyList<Models.VrChatOscParameterSummary> _wardrobeParameterSourceParameters = [];
    private IReadOnlyList<Models.VrChatOscParameterSummary> _availableWardrobeParameters = [];
    private Models.WardrobeOutfit? _copiedWardrobeOutfit;
    private Models.WardrobeSnapshotParam? _copiedWardrobeSnapshotParam;
    private bool _isRestoringWardrobeParameterSelection;
    private bool _isRestoringWardrobeParameterText;
    private string _wardrobeParameterNameFilter = string.Empty;

    public Models.WardrobeOutfit? SelectedWardrobeOutfit
    {
        get => _selectedWardrobeOutfit;
        set
        {
            if (SetProperty(ref _selectedWardrobeOutfit, value))
            {
                if (value != null) _selectedAvatarRule = null;
                RaisePropertyChanged(nameof(SelectedAvatarRule));

                SelectedWardrobeSnapshotParam = value?.SnapshotParams.FirstOrDefault();
                AddWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
                RemoveWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
                CopyWardrobeOutfitCommand.NotifyCanExecuteChanged();
                PasteWardrobeOutfitCommand.NotifyCanExecuteChanged();
                CopyWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
                PasteWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
                AddWardrobeOutfitCommand.NotifyCanExecuteChanged();
                DeleteWardrobeOutfitCommand.NotifyCanExecuteChanged();
                TestWardrobeOutfitCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public Models.WardrobeSnapshotParam? SelectedWardrobeSnapshotParam
    {
        get => _selectedWardrobeSnapshotParam;
        set
        {
            var previous = _selectedWardrobeSnapshotParam;
            if (SetProperty(ref _selectedWardrobeSnapshotParam, value))
            {
                if (previous is not null)
                {
                    previous.PropertyChanged -= SelectedWardrobeSnapshotParamChanged;
                }

                if (_selectedWardrobeSnapshotParam is not null)
                {
                    _selectedWardrobeSnapshotParam.PropertyChanged += SelectedWardrobeSnapshotParamChanged;
                }

                RefreshWardrobeParameterOptions();
                RemoveWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
                CopyWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
                PasteWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public Models.VrChatOscParameterSummary? SelectedWardrobeParameterOption
    {
        get => _selectedWardrobeParameterOption;
        set
        {
            if (SetProperty(ref _selectedWardrobeParameterOption, value)
                && !_isRestoringWardrobeParameterSelection
                && SelectedWardrobeSnapshotParam is not null
                && value is not null)
            {
                if (SelectedWardrobeSnapshotParam.ParameterType != value.ParameterType)
                    SelectedWardrobeSnapshotParam.ParameterType = value.ParameterType;
                SelectedWardrobeSnapshotParam.ParameterName = value.Address;
                SetWardrobeParameterText(value.DisplayLabel);
            }
        }
    }

    public string WardrobeParameterText
    {
        get => _wardrobeParameterText;
        set
        {
            if (SetProperty(ref _wardrobeParameterText, value ?? string.Empty)
                && !_isRestoringWardrobeParameterText)
            {
                CommitWardrobeParameterText(value);
            }
        }
    }

    public IReadOnlyList<Models.VrChatOscParameterSummary> AvailableWardrobeParameters
    {
        get => _availableWardrobeParameters;
        private set => SetProperty(ref _availableWardrobeParameters, value);
    }

    public string WardrobeParameterNameFilter
    {
        get => _wardrobeParameterNameFilter;
        set
        {
            if (SetProperty(ref _wardrobeParameterNameFilter, value ?? string.Empty))
            {
                ApplyWardrobeParameterFilter();
            }
        }
    }

    public IReadOnlyList<Models.VrChatOscParameterSummary> FilteredWardrobeParameters { get; private set; } = [];

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
    public RelayCommand AddWardrobeSnapshotParamCommand { get; }
    public RelayCommand RemoveWardrobeSnapshotParamCommand { get; }
    public RelayCommand CopyWardrobeOutfitCommand { get; }
    public RelayCommand PasteWardrobeOutfitCommand { get; }
    public RelayCommand CopyWardrobeSnapshotParamCommand { get; }
    public RelayCommand PasteWardrobeSnapshotParamCommand { get; }
    public AsyncRelayCommand RefreshWardrobeParametersCommand { get; }
    public AsyncRelayCommand TestWardrobeOutfitCommand { get; }
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
        nameof(Models.WardrobeOutfit.TwitchRewardSyncMode),
        nameof(Models.WardrobeOutfit.ManagedRewardReadyColor),
        nameof(Models.WardrobeOutfit.ManagedRewardCooldownColor)
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

    private void SelectedWardrobeSnapshotParamChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isRestoringWardrobeParameterSelection)
        {
            return;
        }

        if (ReferenceEquals(sender, SelectedWardrobeSnapshotParam)
            && (e.PropertyName == nameof(Models.WardrobeSnapshotParam.ParameterType)
                || e.PropertyName == nameof(Models.WardrobeSnapshotParam.ParameterName)))
        {
            RefreshWardrobeParameterOptions();
        }
    }

    private void RefreshWardrobeParameterOptions()
    {
        _isRestoringWardrobeParameterSelection = true;
        try
        {
            if (_selectedWardrobeSnapshotParam is null)
            {
                AvailableWardrobeParameters = [];
                ApplyWardrobeParameterFilter();
                _selectedWardrobeParameterOption = null;
                RaisePropertyChanged(nameof(SelectedWardrobeParameterOption));
                SetWardrobeParameterText(string.Empty);
                return;
            }

            TryRepairSelectedWardrobeParameter();
            var address = NormalizeAvatarParameterAddressOrEmpty(_selectedWardrobeSnapshotParam.ParameterName ?? string.Empty);
            AvailableWardrobeParameters = BuildWardrobeParameterOptionsForType(
                _selectedWardrobeSnapshotParam.ParameterType,
                _selectedWardrobeSnapshotParam.ParameterName ?? string.Empty);
            ApplyWardrobeParameterFilter();
            var match = AvailableWardrobeParameters.FirstOrDefault(p =>
                string.Equals(p.Address, address, StringComparison.Ordinal));
            _selectedWardrobeParameterOption = match;
            RaisePropertyChanged(nameof(SelectedWardrobeParameterOption));
            SetWardrobeParameterText(match?.DisplayLabel ?? _selectedWardrobeSnapshotParam.ParameterName ?? string.Empty);
        }
        finally
        {
            _isRestoringWardrobeParameterSelection = false;
        }
    }

    private void CommitWardrobeParameterText(string? rawText)
    {
        if (SelectedWardrobeSnapshotParam is not { } selectedParam)
        {
            return;
        }

        var text = rawText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            if (!string.IsNullOrWhiteSpace(selectedParam.ParameterName))
            {
                selectedParam.ParameterName = string.Empty;
            }

            RefreshWardrobeParameterOptions();
            return;
        }

        var changed = false;
        if (TryResolveWardrobeParameterInput(
                text,
                selectedParam.ParameterType,
                out var resolvedAddress,
                out var resolvedType,
                out var matchedOption))
        {
            if (selectedParam.ParameterType != resolvedType)
            {
                selectedParam.ParameterType = resolvedType;
                changed = true;
            }

            if (!string.Equals(selectedParam.ParameterName?.Trim(), resolvedAddress, StringComparison.Ordinal))
            {
                selectedParam.ParameterName = resolvedAddress;
                changed = true;
            }

            if (matchedOption is not null)
            {
                _selectedWardrobeParameterOption = matchedOption;
                RaisePropertyChanged(nameof(SelectedWardrobeParameterOption));
            }
        }
        else
        {
            var cleanedText = StripWardrobeParameterDisplayTypeSuffix(text, out var parsedType);
            if (parsedType is Models.OscParameterType supportedType && selectedParam.ParameterType != supportedType)
            {
                selectedParam.ParameterType = supportedType;
                changed = true;
            }

            var normalizedAddress = NormalizeAvatarParameterAddressOrEmpty(cleanedText);
            if (!string.Equals(selectedParam.ParameterName?.Trim(), normalizedAddress, StringComparison.Ordinal))
            {
                selectedParam.ParameterName = normalizedAddress;
                changed = true;
            }
        }

        if (!changed)
        {
            RefreshWardrobeParameterOptions();
        }
    }

    private void SetWardrobeParameterText(string text)
    {
        _isRestoringWardrobeParameterText = true;
        try
        {
            WardrobeParameterText = text ?? string.Empty;
        }
        finally
        {
            _isRestoringWardrobeParameterText = false;
        }
    }

    private void TryRepairSelectedWardrobeParameter()
    {
        if (_selectedWardrobeSnapshotParam is null)
        {
            return;
        }

        var rawName = _selectedWardrobeSnapshotParam.ParameterName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return;
        }

        if (TryResolveWardrobeParameterInput(
                rawName,
                _selectedWardrobeSnapshotParam.ParameterType,
                out var resolvedAddress,
                out var resolvedType,
                out _))
        {
            if (_selectedWardrobeSnapshotParam.ParameterType != resolvedType)
            {
                _selectedWardrobeSnapshotParam.ParameterType = resolvedType;
            }

            if (!string.Equals(_selectedWardrobeSnapshotParam.ParameterName?.Trim(), resolvedAddress, StringComparison.Ordinal))
            {
                _selectedWardrobeSnapshotParam.ParameterName = resolvedAddress;
            }

            return;
        }

        var cleanedName = StripWardrobeParameterDisplayTypeSuffix(rawName, out var parsedType);
        if (parsedType is Models.OscParameterType supportedType && _selectedWardrobeSnapshotParam.ParameterType != supportedType)
        {
            _selectedWardrobeSnapshotParam.ParameterType = supportedType;
        }

        var normalizedAddress = NormalizeAvatarParameterAddressOrEmpty(cleanedName);
        if (!string.Equals(_selectedWardrobeSnapshotParam.ParameterName?.Trim(), normalizedAddress, StringComparison.Ordinal))
        {
            _selectedWardrobeSnapshotParam.ParameterName = normalizedAddress;
        }
    }

    private List<Models.VrChatOscParameterSummary> BuildWardrobeParameterOptionsForType(
        Models.OscParameterType parameterType,
        string selectedParameterName)
    {
        var nextOptions = _wardrobeParameterSourceParameters
            .Where(parameter => parameter.ParameterType == parameterType)
            .OrderBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var cleanedName = StripWardrobeParameterDisplayTypeSuffix(selectedParameterName ?? string.Empty, out _);
        var selectedParameterAddress = NormalizeAvatarParameterAddressOrEmpty(cleanedName);
        if (!string.IsNullOrWhiteSpace(selectedParameterAddress)
            && !nextOptions.Any(option => string.Equals(option.Address, selectedParameterAddress, StringComparison.Ordinal)))
        {
            nextOptions.Insert(0, CreateCustomAvatarParameterOption(selectedParameterAddress, parameterType));
        }

        return nextOptions;
    }

    private void ApplyWardrobeParameterFilter()
    {
        // Filter the type-filtered list (which is in _availableWardrobeParameters, set
        // by RefreshWardrobeParameterOptions) by the name search text. The typed-text
        // resolution path in RefreshWardrobeParameterOptions still uses
        // _availableWardrobeParameters directly so the match is computed against the
        // full same-type set, not just the filtered subset.
        var query = (_wardrobeParameterNameFilter ?? string.Empty).Trim();
        var nameFiltered = string.IsNullOrEmpty(query)
            ? _availableWardrobeParameters.ToList()
            : _availableWardrobeParameters.Where(p =>
                p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Address.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.DisplayLabel.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        FilteredWardrobeParameters = nameFiltered;
    }

    private bool TryResolveWardrobeParameterInput(
        string rawText,
        Models.OscParameterType preferredType,
        out string address,
        out Models.OscParameterType parameterType,
        out Models.VrChatOscParameterSummary? matchedOption)
    {
        address = string.Empty;
        parameterType = preferredType;
        matchedOption = null;

        var trimmedText = rawText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedText))
        {
            return false;
        }

        var cleanedText = StripWardrobeParameterDisplayTypeSuffix(trimmedText, out var parsedType);
        var normalizedAddress = NormalizeAvatarParameterAddressOrEmpty(cleanedText);
        var sourceParameters = _wardrobeParameterSourceParameters.Count > 0
            ? _wardrobeParameterSourceParameters
            : _availableWardrobeParameters;
        var candidates = parsedType is Models.OscParameterType parsed
            ? sourceParameters.Where(parameter => parameter.ParameterType == parsed)
            : sourceParameters
                .Where(parameter => parameter.ParameterType == preferredType)
                .Concat(sourceParameters.Where(parameter => parameter.ParameterType != preferredType));

        matchedOption = candidates.FirstOrDefault(parameter =>
            string.Equals(parameter.Address, trimmedText, StringComparison.OrdinalIgnoreCase)
            || string.Equals(parameter.Address, normalizedAddress, StringComparison.OrdinalIgnoreCase)
            || string.Equals(parameter.Name, trimmedText, StringComparison.OrdinalIgnoreCase)
            || string.Equals(parameter.Name, cleanedText, StringComparison.OrdinalIgnoreCase)
            || string.Equals(parameter.DisplayLabel, trimmedText, StringComparison.OrdinalIgnoreCase));

        if (matchedOption is null)
        {
            return false;
        }

        address = matchedOption.Address;
        parameterType = matchedOption.ParameterType;
        return true;
    }

    private static string StripWardrobeParameterDisplayTypeSuffix(string rawText, out Models.OscParameterType? parsedType)
    {
        parsedType = null;
        var text = rawText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        foreach (var parameterType in new[] { Models.OscParameterType.Bool, Models.OscParameterType.Int, Models.OscParameterType.Float })
        {
            var suffix = $" [{parameterType}]";
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                parsedType = parameterType;
                return text[..^suffix.Length].Trim();
            }
        }

        return text;
    }

    private void AddWardrobeSnapshotParam()
    {
        if (SelectedWardrobeOutfit is null) return;
        var param = new Models.WardrobeSnapshotParam();
        SelectedWardrobeOutfit.SnapshotParams.Add(param);
        SelectedWardrobeSnapshotParam = param;
    }

    private void RemoveWardrobeSnapshotParam()
    {
        if (SelectedWardrobeOutfit is null || SelectedWardrobeSnapshotParam is null) return;
        var param = SelectedWardrobeSnapshotParam;
        var index = SelectedWardrobeOutfit.SnapshotParams.IndexOf(param);
        SelectedWardrobeOutfit.SnapshotParams.Remove(param);
        SelectedWardrobeSnapshotParam = index < SelectedWardrobeOutfit.SnapshotParams.Count
            ? SelectedWardrobeOutfit.SnapshotParams[index]
            : SelectedWardrobeOutfit.SnapshotParams.FirstOrDefault();
    }

    private void CopyWardrobeOutfit()
    {
        if (SelectedWardrobeOutfit is null) return;
        _copiedWardrobeOutfit = CloneWardrobeOutfit(SelectedWardrobeOutfit, clearRewardId: false, copyName: SelectedWardrobeOutfit.Name);
        PasteWardrobeOutfitCommand.NotifyCanExecuteChanged();
    }

    private void PasteWardrobeOutfit()
    {
        if (SelectedProfile is null || _copiedWardrobeOutfit is null) return;

        var pastedName = GetUniqueWardrobeCopyName(_copiedWardrobeOutfit.Name, SelectedProfile.WardrobeOutfits.Select(outfit => outfit.Name));
        var outfit = CloneWardrobeOutfit(_copiedWardrobeOutfit, clearRewardId: true, copyName: pastedName);
        if (!string.IsNullOrWhiteSpace(outfit.TwitchRewardTitle))
        {
            outfit.TwitchRewardTitle = GetUniqueWardrobeCopyName(
                outfit.TwitchRewardTitle,
                SelectedProfile.WardrobeOutfits.Select(existing => existing.TwitchRewardTitle));
        }

        SelectedProfile.WardrobeOutfits.Add(outfit);
        SelectedWardrobeOutfit = outfit;
        SelectedWardrobeSnapshotParam = outfit.SnapshotParams.FirstOrDefault();
    }

    private void CopyWardrobeSnapshotParam()
    {
        if (SelectedWardrobeSnapshotParam is null) return;
        _copiedWardrobeSnapshotParam = CloneWardrobeSnapshotParam(SelectedWardrobeSnapshotParam);
        PasteWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
    }

    private void PasteWardrobeSnapshotParam()
    {
        if (SelectedWardrobeOutfit is null || _copiedWardrobeSnapshotParam is null) return;

        var param = CloneWardrobeSnapshotParam(_copiedWardrobeSnapshotParam);
        var insertIndex = SelectedWardrobeSnapshotParam is not null
            ? SelectedWardrobeOutfit.SnapshotParams.IndexOf(SelectedWardrobeSnapshotParam) + 1
            : SelectedWardrobeOutfit.SnapshotParams.Count;
        if (insertIndex < 0 || insertIndex > SelectedWardrobeOutfit.SnapshotParams.Count)
        {
            insertIndex = SelectedWardrobeOutfit.SnapshotParams.Count;
        }

        SelectedWardrobeOutfit.SnapshotParams.Insert(insertIndex, param);
        SelectedWardrobeSnapshotParam = param;
    }

    private async Task RefreshWardrobeParametersAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedProfile.AvatarId))
        {
            _wardrobeParameterSourceParameters = [];
            AvailableWardrobeParameters = [];
            RefreshWardrobeParameterOptions();
            return;
        }

        try
        {
            var parameters = await _mainVm.LoadAvatarParameterSummariesAsync(SelectedProfile.AvatarId);
            _wardrobeParameterSourceParameters = parameters;
            RefreshWardrobeParameterOptions();
        }
        catch
        {
            _wardrobeParameterSourceParameters = [];
            AvailableWardrobeParameters = [];
            RefreshWardrobeParameterOptions();
        }
    }

    private async Task TestWardrobeOutfitAsync()
    {
        if (SelectedWardrobeOutfit is null || SelectedProfile is null) return;
        await _mainVm.TestWardrobeOutfitPublicAsync(SelectedWardrobeOutfit, SelectedProfile, CancellationToken.None);
    }

    private static Models.WardrobeOutfit CloneWardrobeOutfit(Models.WardrobeOutfit source, bool clearRewardId, string copyName)
    {
        return new Models.WardrobeOutfit
        {
            Id = Guid.NewGuid(),
            IsEnabled = source.IsEnabled,
            Name = string.IsNullOrWhiteSpace(copyName) ? "New Outfit Copy" : copyName.Trim(),
            ActiveTimeSeconds = source.ActiveTimeSeconds,
            TwitchRewardId = clearRewardId ? string.Empty : source.TwitchRewardId,
            TwitchRewardTitle = source.TwitchRewardTitle,
            TwitchRewardDescription = source.TwitchRewardDescription,
            TwitchRewardCost = source.TwitchRewardCost,
            TwitchRewardSyncMode = source.TwitchRewardSyncMode,
            ChatCommandText = source.ChatCommandText,
            ManagedRewardReadyColor = source.ManagedRewardReadyColor,
            ManagedRewardCooldownColor = source.ManagedRewardCooldownColor,
            SnapshotParams = new ObservableCollection<Models.WardrobeSnapshotParam>(
                source.SnapshotParams.Select(CloneWardrobeSnapshotParam))
        };
    }

    private static Models.WardrobeSnapshotParam CloneWardrobeSnapshotParam(Models.WardrobeSnapshotParam source)
    {
        return new Models.WardrobeSnapshotParam
        {
            Id = Guid.NewGuid(),
            ParameterName = source.ParameterName,
            ParameterType = source.ParameterType,
            SetValue = source.SetValue
        };
    }

    private static string GetUniqueWardrobeCopyName(string sourceName, IEnumerable<string> existingNames)
    {
        var baseName = string.IsNullOrWhiteSpace(sourceName) ? "New Outfit" : sourceName.Trim();
        if (!baseName.EndsWith(" Copy", StringComparison.OrdinalIgnoreCase))
        {
            baseName += " Copy";
        }

        var usedNames = existingNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!usedNames.Contains(baseName))
        {
            return baseName;
        }

        var index = 2;
        while (usedNames.Contains($"{baseName} {index}"))
        {
            index++;
        }

        return $"{baseName} {index}";
    }

    private static string NormalizeAvatarParameterAddressOrEmpty(string parameterName)
    {
        return string.IsNullOrWhiteSpace(parameterName)
            ? string.Empty
            : Services.VrChatOscClient.NormalizeAvatarParameterAddress(parameterName);
    }

    private static Models.VrChatOscParameterSummary CreateCustomAvatarParameterOption(string parameterName, Models.OscParameterType parameterType)
    {
        var normalizedAddress = Services.VrChatOscClient.NormalizeAvatarParameterAddress(parameterName);
        var displayName = normalizedAddress.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? normalizedAddress;
        return new Models.VrChatOscParameterSummary(normalizedAddress, displayName, parameterType);
    }
}