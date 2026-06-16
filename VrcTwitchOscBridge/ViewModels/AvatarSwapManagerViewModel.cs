using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class AvatarSwapManagerViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly MainWindowViewModel _mainVm;
    private readonly AvatarImageService _imageService;

    private readonly ObservableCollection<AvatarSwapCardViewModel> _channelPointCards = [];
    private readonly ObservableCollection<AvatarSwapCardViewModel> _bitsSubsCards = [];
    private readonly ObservableCollection<AvatarSwapCardViewModel> _rouletteCards = [];
    private readonly ObservableCollection<AvatarSwapProfile> _channelPointProfiles = [];
    private readonly ObservableCollection<AvatarSwapProfile> _bitsSubsProfiles = [];
    private readonly ObservableCollection<AvatarSwapProfile> _rouletteProfiles = [];

    private AvatarSwapProfileSnapshot? _editorSnapshot;
    private AvatarSwapProfile? _editorProfile;
    private bool _editorIsNew;
    private bool _isEditorOpen;
    private AvatarSwapRuleEditorViewModel? _editingRule;
    private string? _filterText;
    private System.Windows.Media.ImageSource? _returnAvatarImage;
    private System.Threading.CancellationTokenSource? _returnAvatarImageCts;

    public AvatarSwapManagerViewModel(AppSettings settings, MainWindowViewModel mainVm, AvatarImageService imageService)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _mainVm = mainVm ?? throw new ArgumentNullException(nameof(mainVm));
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));

        _settings.AvatarSwapProfiles.CollectionChanged += OnProfilesChanged;

        AddNewSwapCommand = new RelayCommand(AddNewSwap);
        OpenEditorCommand = new RelayCommand(p => OpenEditor(p as AvatarSwapProfile));
        CloseEditorCommand = new RelayCommand(CloseEditor);
        SaveEditorCommand = new AsyncRelayCommand(SaveEditorAsync);
        DeleteSelectedProfileCommand = new AsyncRelayCommand(DeleteSelectedProfileAsync, () => SelectedProfile is not null);
        PickReturnAvatarCommand = new RelayCommand(PickReturnAvatar);
        UseCurrentAvatarForReturnCommand = new RelayCommand(UseCurrentAvatarForReturn);
        ClearReturnAvatarCommand = new RelayCommand(ClearReturnAvatar);
        EnableAllCommand = new RelayCommand(EnableAll);
        DisableAllCommand = new RelayCommand(DisableAll);
        PickTargetAvatarCommand = new RelayCommand(p => PickTargetAvatar(p as AvatarSwapProfile));
        UseCurrentAvatarForTargetCommand = new RelayCommand(p => UseCurrentAvatarForTarget(p as AvatarSwapProfile));
        DeleteChannelPointRuleCommand = new RelayCommand(p => DeleteRule(p as TriggerRule, isBitsSubs: false));
        DeleteBitsSubsRuleCommand = new RelayCommand(p => DeleteRule(p as TriggerRule, isBitsSubs: true));
        DeleteRouletteRuleCommand = new RelayCommand(p => DeleteRouletteRule(p as TriggerRule));
        AddChannelPointRuleCommand = new RelayCommand(AddChannelPointRule);
        AddBitsSubsRuleCommand = new RelayCommand(AddBitsSubsRule);
        AddRouletteRuleCommand = new RelayCommand(AddRouletteRule, () => SelectedProfile is not null);
        OpenRuleEditorCommand = new RelayCommand(p => OpenRuleEditor(p as TriggerRule));
        ToggleChannelPointSectionCommand = new RelayCommand(() => IsChannelPointSectionCollapsed = !IsChannelPointSectionCollapsed);
        ToggleBitsSubsSectionCommand = new RelayCommand(() => IsBitsSubsSectionCollapsed = !IsBitsSubsSectionCollapsed);
        ToggleRouletteSectionCommand = new RelayCommand(() => IsRouletteSectionCollapsed = !IsRouletteSectionCollapsed);
        CommitRuleEditCommand = new RelayCommand(CommitRuleEdit);
        CancelRuleEditCommand = new RelayCommand(CancelRuleEdit);

        RebuildCollections();
        LoadReturnAvatarImage();
    }

    public ObservableCollection<AvatarSwapCardViewModel> ChannelPointCards => _channelPointCards;
    public ObservableCollection<AvatarSwapCardViewModel> BitsSubsCards => _bitsSubsCards;
    public ObservableCollection<AvatarSwapCardViewModel> RouletteCards => _rouletteCards;

    public IReadOnlyList<ReturnAvatarMode> ReturnAvatarModes { get; } =
        [ReturnAvatarMode.UseGlobal, ReturnAvatarMode.UseCustom, ReturnAvatarMode.SameAsTarget];

    public string? ReturnAvatarId
    {
        get => _settings.MasterAvatarSwapReturnId;
        set
        {
            _settings.MasterAvatarSwapReturnId = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(ReturnAvatarName));
            RaisePropertyChanged(nameof(HasReturnAvatar));
            RaisePropertyChanged(nameof(ReturnAvatarDisplayName));
            LoadReturnAvatarImage();
        }
    }

    public string? ReturnAvatarName
    {
        get => _settings.MasterAvatarSwapReturnName;
        set
        {
            _settings.MasterAvatarSwapReturnName = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(ReturnAvatarDisplayName));
        }
    }

    public bool HasReturnAvatar => !string.IsNullOrWhiteSpace(ReturnAvatarId);

    public string ReturnAvatarDisplayName => _settings.MasterAvatarSwapReturnDisplayName;

    public System.Windows.Media.ImageSource? ReturnAvatarImage
    {
        get => _returnAvatarImage;
        private set
        {
            if (SetProperty(ref _returnAvatarImage, value))
            {
                RaisePropertyChanged(nameof(HasReturnAvatarImage));
            }
        }
    }

    public bool HasReturnAvatarImage => _returnAvatarImage is not null;

    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        set
        {
            if (SetProperty(ref _isEditorOpen, value))
            {
                RaisePropertyChanged(nameof(IsEditorClosed));
            }
        }
    }

    public bool IsEditorClosed => !_isEditorOpen;

    public AvatarSwapProfile? SelectedProfile
    {
        get => _editorProfile;
        private set
        {
            if (SetProperty(ref _editorProfile, value))
            {
                RaisePropertyChanged(nameof(EditorProfileId));
                RaisePropertyChanged(nameof(EditorTargetAvatarName));
                RaisePropertyChanged(nameof(EditorTargetAvatarId));
                RaisePropertyChanged(nameof(EditorReturnMode));
                RaisePropertyChanged(nameof(EditorReturnAvatarId));
                RaisePropertyChanged(nameof(EditorReturnAvatarName));
                RaisePropertyChanged(nameof(EditorChannelPointRules));
                RaisePropertyChanged(nameof(EditorBitsSubsRules));
                RaisePropertyChanged(nameof(EditorHasTarget));
            }
        }
    }

    public Guid? EditorProfileId => _editorProfile?.Id;

    public string EditorTargetAvatarName
    {
        get => _editorProfile?.TargetAvatarName ?? string.Empty;
        set
        {
            if (_editorProfile is not null)
            {
                _editorProfile.TargetAvatarName = value;
                RaisePropertyChanged();
            }
        }
    }

    public string EditorTargetAvatarId
    {
        get => _editorProfile?.TargetAvatarId ?? string.Empty;
        set
        {
            if (_editorProfile is not null)
            {
                _editorProfile.TargetAvatarId = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(EditorHasTarget));
            }
        }
    }

    public bool EditorHasTarget => _editorProfile?.HasTarget == true;

    public ReturnAvatarMode EditorReturnMode
    {
        get => _editorProfile?.ReturnAvatarMode ?? ReturnAvatarMode.UseGlobal;
        set
        {
            if (_editorProfile is not null)
            {
                _editorProfile.ReturnAvatarMode = value;
                RaisePropertyChanged();
            }
        }
    }

    public string? EditorReturnAvatarId
    {
        get => _editorProfile?.ReturnAvatarId;
        set
        {
            if (_editorProfile is not null)
            {
                _editorProfile.ReturnAvatarId = value;
                RaisePropertyChanged();
            }
        }
    }

    public string? EditorReturnAvatarName
    {
        get => _editorProfile?.ReturnAvatarName;
        set
        {
            if (_editorProfile is not null)
            {
                _editorProfile.ReturnAvatarName = value;
                RaisePropertyChanged();
            }
        }
    }

    public ObservableCollection<TriggerRule> EditorChannelPointRules =>
        _editorProfile?.ChannelPointRules ?? [];

    public ObservableCollection<TriggerRule> EditorBitsSubsRules =>
        _editorProfile?.BitsSubsRules ?? [];

    public AvatarSwapRuleEditorViewModel? EditingRule
    {
        get => _editingRule;
        private set => SetProperty(ref _editingRule, value);
    }

    public string? FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                ChannelPointCardsView.Refresh();
                BitsSubsCardsView.Refresh();
                RouletteCardsView.Refresh();
                RaisePropertyChanged(nameof(ChannelPointSectionSuffix));
                RaisePropertyChanged(nameof(BitsSubsSectionSuffix));
                RaisePropertyChanged(nameof(RouletteSectionSuffix));
            }
        }
    }

    public ICollectionView ChannelPointCardsView { get; private set; } = null!;
    public ICollectionView BitsSubsCardsView { get; private set; } = null!;
    public ICollectionView RouletteCardsView { get; private set; } = null!;

    private bool _isChannelPointSectionCollapsed;
    public bool IsChannelPointSectionCollapsed
    {
        get => _isChannelPointSectionCollapsed;
        set => SetProperty(ref _isChannelPointSectionCollapsed, value);
    }

    private bool _isBitsSubsSectionCollapsed;
    public bool IsBitsSubsSectionCollapsed
    {
        get => _isBitsSubsSectionCollapsed;
        set => SetProperty(ref _isBitsSubsSectionCollapsed, value);
    }

    private bool _isRouletteSectionCollapsed;
    public bool IsRouletteSectionCollapsed
    {
        get => _isRouletteSectionCollapsed;
        set => SetProperty(ref _isRouletteSectionCollapsed, value);
    }

    public int ChannelPointCount => _channelPointProfiles.Count;
    public int BitsSubsCount => _bitsSubsProfiles.Count;
    public int RouletteCount => _rouletteProfiles.Count;

    public string ChannelPointSectionSuffix => FilterText is { Length: > 0 }
        ? $"({_channelPointCards.Count} of {ChannelPointCount})"
        : $"({ChannelPointCount})";

    public string BitsSubsSectionSuffix => FilterText is { Length: > 0 }
        ? $"({_bitsSubsCards.Count} of {BitsSubsCount})"
        : $"({BitsSubsCount})";

    public string RouletteSectionSuffix => FilterText is { Length: > 0 }
        ? $"({_rouletteCards.Count} of {RouletteCount})"
        : $"({RouletteCount})";

    public RelayCommand AddNewSwapCommand { get; }
    public RelayCommand OpenEditorCommand { get; }
    public RelayCommand CloseEditorCommand { get; }
    public AsyncRelayCommand SaveEditorCommand { get; }
    public AsyncRelayCommand DeleteSelectedProfileCommand { get; }
    public RelayCommand PickReturnAvatarCommand { get; }
    public RelayCommand UseCurrentAvatarForReturnCommand { get; }
    public RelayCommand ClearReturnAvatarCommand { get; }
    public RelayCommand EnableAllCommand { get; }
    public RelayCommand DisableAllCommand { get; }
    public RelayCommand PickTargetAvatarCommand { get; }
    public RelayCommand UseCurrentAvatarForTargetCommand { get; }
    public RelayCommand DeleteChannelPointRuleCommand { get; }
    public RelayCommand DeleteBitsSubsRuleCommand { get; }
    public RelayCommand DeleteRouletteRuleCommand { get; }
    public RelayCommand AddChannelPointRuleCommand { get; }
    public RelayCommand AddBitsSubsRuleCommand { get; }
    public RelayCommand AddRouletteRuleCommand { get; }
    public RelayCommand OpenRuleEditorCommand { get; }
    public RelayCommand ToggleChannelPointSectionCommand { get; }
    public RelayCommand ToggleBitsSubsSectionCommand { get; }
    public RelayCommand ToggleRouletteSectionCommand { get; }
    public RelayCommand CommitRuleEditCommand { get; }
    public RelayCommand CancelRuleEditCommand { get; }

    public void OnWindowClosed()
    {
        CloseEditor();
    }

    public void Dispose()
    {
        CloseEditor();
        _returnAvatarImageCts?.Cancel();
        _returnAvatarImageCts?.Dispose();
        _settings.AvatarSwapProfiles.CollectionChanged -= OnProfilesChanged;
        foreach (var card in _channelPointCards)
        {
            card.Dispose();
        }
        foreach (var card in _bitsSubsCards)
        {
            card.Dispose();
        }
        foreach (var card in _rouletteCards)
        {
            card.Dispose();
        }
    }

    private void LoadReturnAvatarImage()
    {
        _returnAvatarImageCts?.Cancel();
        _returnAvatarImageCts?.Dispose();
        _returnAvatarImageCts = new System.Threading.CancellationTokenSource();
        var avatarId = _settings.MasterAvatarSwapReturnId;
        var thumbnailUrl = _mainVm.TryGetVrChatAvatarThumbnailUrl(avatarId);
        var ct = _returnAvatarImageCts.Token;

        if (string.IsNullOrWhiteSpace(avatarId))
        {
            ReturnAvatarImage = null;
            return;
        }

        var syncImage = _imageService.GetAvatarImage(avatarId, null, thumbnailUrl);
        if (syncImage is not null && !ct.IsCancellationRequested)
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => ReturnAvatarImage = syncImage);
            return;
        }

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var asyncImage = await _imageService.GetAvatarImageAsync(avatarId, null, thumbnailUrl, ct);
                if (asyncImage is not null && !ct.IsCancellationRequested)
                {
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => ReturnAvatarImage = asyncImage);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                if (!ct.IsCancellationRequested)
                {
                    var placeholder = _imageService.GetPlaceholderImage();
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => ReturnAvatarImage = placeholder);
                }
            }
        }, ct);
    }

    private void RebuildCollections()
    {
        _channelPointCards.Clear();
        _bitsSubsCards.Clear();
        _rouletteCards.Clear();
        _channelPointProfiles.Clear();
        _bitsSubsProfiles.Clear();
        _rouletteProfiles.Clear();

        foreach (var profile in _settings.AvatarSwapProfiles)
        {
            if (profile.UsesChannelPointRules)
            {
                AddCardToSection(profile, _channelPointProfiles, _channelPointCards);
            }
            if (profile.UsesBitsSubsRules)
            {
                AddCardToSection(profile, _bitsSubsProfiles, _bitsSubsCards);
            }
            if (profile.UsesRouletteRules)
            {
                AddCardToSection(profile, _rouletteProfiles, _rouletteCards);
            }
        }

        ChannelPointCardsView = CollectionViewSource.GetDefaultView(_channelPointCards);
        ChannelPointCardsView.Filter = FilterCard;
        BitsSubsCardsView = CollectionViewSource.GetDefaultView(_bitsSubsCards);
        BitsSubsCardsView.Filter = FilterCard;
        RouletteCardsView = CollectionViewSource.GetDefaultView(_rouletteCards);
        RouletteCardsView.Filter = FilterCard;

        RaisePropertyChanged(nameof(ChannelPointCardsView));
        RaisePropertyChanged(nameof(BitsSubsCardsView));
        RaisePropertyChanged(nameof(RouletteCardsView));
        RaisePropertyChanged(nameof(ChannelPointSectionSuffix));
        RaisePropertyChanged(nameof(BitsSubsSectionSuffix));
        RaisePropertyChanged(nameof(RouletteSectionSuffix));
    }

    private void AddCardToSection(AvatarSwapProfile profile, ObservableCollection<AvatarSwapProfile> section, ObservableCollection<AvatarSwapCardViewModel> cards)
    {
        if (!section.Contains(profile))
        {
            section.Add(profile);
        }
        if (!cards.Any(c => c.Profile == profile))
        {
            var card = new AvatarSwapCardViewModel(profile, _imageService);
            card.SetThumbnailUrl(_mainVm.TryGetVrChatAvatarThumbnailUrl(profile.TargetAvatarId));
            cards.Add(card);
        }
    }

    private void RemoveCardFromSection(AvatarSwapProfile profile)
    {
        var cp = _channelPointCards.FirstOrDefault(c => c.Profile == profile);
        if (cp is not null)
        {
            _channelPointCards.Remove(cp);
            cp.Dispose();
        }
        var bs = _bitsSubsCards.FirstOrDefault(c => c.Profile == profile);
        if (bs is not null)
        {
            _bitsSubsCards.Remove(bs);
            bs.Dispose();
        }
        var rl = _rouletteCards.FirstOrDefault(c => c.Profile == profile);
        if (rl is not null)
        {
            _rouletteCards.Remove(rl);
            rl.Dispose();
        }
        _channelPointProfiles.Remove(profile);
        _bitsSubsProfiles.Remove(profile);
        _rouletteProfiles.Remove(profile);
        RaisePropertyChanged(nameof(ChannelPointSectionSuffix));
        RaisePropertyChanged(nameof(BitsSubsSectionSuffix));
        RaisePropertyChanged(nameof(RouletteSectionSuffix));
    }

    private bool FilterCard(object obj)
    {
        if (obj is not AvatarSwapCardViewModel card) return false;
        if (string.IsNullOrWhiteSpace(FilterText)) return true;
        var text = FilterText.Trim();
        return card.DisplayTitle.Contains(text, StringComparison.OrdinalIgnoreCase)
            || card.AvatarSubtitle.Contains(text, StringComparison.OrdinalIgnoreCase)
            || card.Profile.TargetAvatarId.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private void OnProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            RebuildCollections();
            return;
        }
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<AvatarSwapProfile>())
            {
                AddCardToSection(item, _channelPointProfiles, _channelPointCards);
                AddCardToSection(item, _bitsSubsProfiles, _bitsSubsCards);
                AddCardToSection(item, _rouletteProfiles, _rouletteCards);
            }
        }
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<AvatarSwapProfile>())
            {
                RemoveCardFromSection(item);
            }
        }
        RaisePropertyChanged(nameof(ChannelPointSectionSuffix));
        RaisePropertyChanged(nameof(BitsSubsSectionSuffix));
        RaisePropertyChanged(nameof(RouletteSectionSuffix));
    }

    private void AddNewSwap()
    {
        var profile = new AvatarSwapProfile
        {
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _settings.AvatarSwapProfiles.Add(profile);
        _editorIsNew = true;
        OpenEditor(profile);
    }

    private void OpenEditor(AvatarSwapProfile? profile)
    {
        if (profile is null) return;
        if (_editorProfile is not null && IsEditorOpen)
        {
            CloseEditor();
        }
        _editorSnapshot = CreateSnapshot(profile);
        _editorProfile = profile;
        _editorIsNew = false;
        SelectedProfile = profile;
        IsEditorOpen = true;
        RaisePropertyChanged(nameof(EditorChannelPointRules));
        RaisePropertyChanged(nameof(EditorBitsSubsRules));
    }

    private static AvatarSwapProfileSnapshot? CreateSnapshot(AvatarSwapProfile profile)
    {
        return new AvatarSwapProfileSnapshot(
            profile.Id,
            profile.TargetAvatarId,
            profile.TargetAvatarName,
            profile.ReturnAvatarMode,
            profile.ReturnAvatarId,
            profile.ReturnAvatarName,
            profile.IsEnabled,
            profile.TargetThumbnailUrl,
            profile.ChannelPointRules.Select(SafeCreateTriggerRuleSnapshot).Where(s => s is not null).Select(s => s!).ToList(),
            profile.BitsSubsRules.Select(SafeCreateTriggerRuleSnapshot).Where(s => s is not null).Select(s => s!).ToList(),
            profile.RouletteRules.Select(SafeCreateTriggerRuleSnapshot).Where(s => s is not null).Select(s => s!).ToList());
    }

    private void CloseEditor()
    {
        if (_editorProfile is not null)
        {
            if (_editorIsNew)
            {
                _settings.AvatarSwapProfiles.Remove(_editorProfile);
            }
            else if (_editorSnapshot is not null)
            {
                RestoreFromSnapshot(_editorProfile, _editorSnapshot);
            }
        }
        _editorSnapshot = null;
        _editorIsNew = false;
        EditingRule = null;
        IsEditorOpen = false;
        SelectedProfile = null;
    }

    private static void RestoreFromSnapshot(AvatarSwapProfile profile, AvatarSwapProfileSnapshot snapshot)
    {
        profile.TargetAvatarId = snapshot.TargetAvatarId;
        profile.TargetAvatarName = snapshot.TargetAvatarName;
        profile.ReturnAvatarMode = snapshot.ReturnAvatarMode;
        profile.ReturnAvatarId = snapshot.ReturnAvatarId;
        profile.ReturnAvatarName = snapshot.ReturnAvatarName;
        profile.IsEnabled = snapshot.IsEnabled;
        profile.UpdatedAt = DateTime.UtcNow;
    }

    private async Task SaveEditorAsync()
    {
        if (_editorProfile is null) return;
        _editorProfile.UpdatedAt = DateTime.UtcNow;
        await _mainVm.SaveSettingsAsync();
        _editorIsNew = false;
        IsEditorOpen = false;
        SelectedProfile = null;
        _editorSnapshot = null;
    }

    private async Task DeleteSelectedProfileAsync()
    {
        if (_editorProfile is null) return;
        var result = MessageBox.Show(
            "Delete this Avatar Swap and all of its rules? This cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        _settings.AvatarSwapProfiles.Remove(_editorProfile);
        await _mainVm.SaveSettingsAsync();
        IsEditorOpen = false;
        SelectedProfile = null;
    }

    private void PickReturnAvatar()
    {
        var avatars = _mainVm.GetAllVrChatAvatars();
        var result = AvatarPickerService.OpenSingle(
            ThemeManager.CurrentTheme,
            avatars,
            _settings.AvatarLibrary,
            _settings.MasterAvatarSwapReturnId,
            Application.Current?.MainWindow);
        if (result is null) return;
        ReturnAvatarId = result.AvatarId;
        ReturnAvatarName = result.AvatarName;
    }

    private void UseCurrentAvatarForReturn()
    {
        var currentId = _settings.VrChat.CurrentAvatarId;
        if (string.IsNullOrWhiteSpace(currentId)) return;
        ReturnAvatarId = currentId;
        ReturnAvatarName = _mainVm.ResolveVrChatAvatarName(currentId);
    }

    private void ClearReturnAvatar()
    {
        ReturnAvatarId = null;
        ReturnAvatarName = null;
    }

    private void EnableAll()
    {
        foreach (var profile in _settings.AvatarSwapProfiles)
        {
            profile.IsEnabled = true;
        }
    }

    private void DisableAll()
    {
        foreach (var profile in _settings.AvatarSwapProfiles)
        {
            profile.IsEnabled = false;
        }
    }

    private void PickTargetAvatar(AvatarSwapProfile? profile)
    {
        if (profile is null) return;
        var avatars = _mainVm.GetAllVrChatAvatars();
        if (avatars.Count == 0)
        {
            ShowNoAvatarsDialog();
            return;
        }
        var result = AvatarPickerService.OpenSingle(
            ThemeManager.CurrentTheme,
            avatars,
            _settings.AvatarLibrary,
            profile.TargetAvatarId,
            Application.Current?.MainWindow);
        if (result is null) return;
        profile.TargetAvatarId = result.AvatarId;
        profile.TargetAvatarName = result.AvatarName;
        profile.TargetThumbnailUrl = _mainVm.TryGetVrChatAvatarThumbnailUrl(result.AvatarId);
        var card = _channelPointCards.FirstOrDefault(c => c.Profile == profile)
            ?? _bitsSubsCards.FirstOrDefault(c => c.Profile == profile);
        card?.SetThumbnailUrl(profile.TargetThumbnailUrl);
    }

    private void UseCurrentAvatarForTarget(AvatarSwapProfile? profile)
    {
        if (profile is null) return;
        var currentId = _settings.VrChat.CurrentAvatarId;
        if (string.IsNullOrWhiteSpace(currentId))
        {
            ShowNoCurrentAvatarDialog();
            return;
        }
        profile.TargetAvatarId = currentId;
        profile.TargetAvatarName = _mainVm.ResolveVrChatAvatarName(currentId);
        profile.TargetThumbnailUrl = _mainVm.TryGetVrChatAvatarThumbnailUrl(currentId);
        var card = _channelPointCards.FirstOrDefault(c => c.Profile == profile)
            ?? _bitsSubsCards.FirstOrDefault(c => c.Profile == profile);
        card?.SetThumbnailUrl(profile.TargetThumbnailUrl);
    }

    private void ShowNoAvatarsDialog()
    {
        MessageBox.Show(
            Application.Current?.MainWindow,
            "No VRChat avatars are loaded yet. Connect to VRChat on the Home tab and let Crystal Relay load your avatars, or pick an avatar from the Avatar Library instead.",
            "No Avatars Available",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ShowNoCurrentAvatarDialog()
    {
        MessageBox.Show(
            Application.Current?.MainWindow,
            "No current VRChat avatar is set. Connect to VRChat and switch into a world first, then try again.",
            "No Current Avatar",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void DeleteRule(TriggerRule? rule, bool isBitsSubs)
    {
        if (_editorProfile is null || rule is null) return;
        var collection = isBitsSubs ? _editorProfile.BitsSubsRules : _editorProfile.ChannelPointRules;
        collection.Remove(rule);
        RaisePropertyChanged(nameof(EditorChannelPointRules));
        RaisePropertyChanged(nameof(EditorBitsSubsRules));
    }

    private void DeleteRouletteRule(TriggerRule? rule)
    {
        if (_editorProfile is null || rule is null) return;
        _editorProfile.RouletteRules.Remove(rule);
        if (ReferenceEquals(EditingRule?.Rule, rule))
        {
            EditingRule = null;
        }
    }

    private void AddChannelPointRule()
    {
        if (_editorProfile is null) return;
        var rule = new TriggerRule
        {
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = _editorProfile.TargetAvatarId,
            AvatarTargetName = _editorProfile.TargetAvatarName,
            TriggerType = TwitchTriggerType.ChannelPoints,
            ChannelPointRewardCost = 100,
            CooldownSeconds = 0,
            IsEnabled = true,
            Name = "New Avatar Swap",
            Source = TriggerRuleSource.Native
        };
        _editorProfile.ChannelPointRules.Add(rule);
        RaisePropertyChanged(nameof(EditorChannelPointRules));
    }

    private void AddBitsSubsRule()
    {
        if (_editorProfile is null) return;
        var rule = new TriggerRule
        {
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = _editorProfile.TargetAvatarId,
            AvatarTargetName = _editorProfile.TargetAvatarName,
            TriggerType = TwitchTriggerType.Bits,
            MinimumAmount = 100,
            IsEnabled = true,
            Name = "New Bits/Subs Swap",
            Source = TriggerRuleSource.Native
        };
        _editorProfile.BitsSubsRules.Add(rule);
        RaisePropertyChanged(nameof(EditorBitsSubsRules));
    }

    private void AddRouletteRule()
    {
        if (SelectedProfile is null) return;
        var rule = new TriggerRule
        {
            ActionType = OscActionType.AvatarRoulet,
            Name = "New Roulette Rule",
            Source = TriggerRuleSource.Native
        };
        SelectedProfile.RouletteRules.Add(rule);
        OpenRuleEditor(rule);
    }

    private void OpenRuleEditor(TriggerRule? rule)
    {
        if (rule is null) return;
        EditingRule = new AvatarSwapRuleEditorViewModel(rule);
        IsEditorOpen = true;
    }

    public void CommitRuleEdit()
    {
        EditingRule?.Save();
        EditingRule = null;
        IsEditorOpen = false;
    }

    public void CancelRuleEdit()
    {
        EditingRule = null;
        IsEditorOpen = false;
    }

    private static TriggerRuleSnapshot? SafeCreateTriggerRuleSnapshot(TriggerRule rule)
    {
        try
        {
            return BridgeRuntimeConfiguration.CreateManualTestSnapshot(rule, isGlobalOverride: false, profile: null);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
