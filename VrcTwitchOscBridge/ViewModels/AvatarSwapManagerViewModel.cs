using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.UserControls;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class AvatarSwapManagerViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ITwitchRewardSource _twitchRewardSource;
    private readonly AvatarImageService _imageService;
    private readonly Func<string?, string?>? _thumbnailUrlResolver;
    private readonly Action? _onSettingsChanged;

    private bool _isChannelPointSectionCollapsed;
    private bool _isBitsSubsSectionCollapsed;
    private bool _isRouletteSectionCollapsed;
    private string? _filterText;

    public AvatarSwapManagerViewModel(AppSettings settings, ITwitchRewardSource twitchRewardSource, Func<string?, string?>? thumbnailUrlResolver = null, Action? onSettingsChanged = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _twitchRewardSource = twitchRewardSource ?? throw new ArgumentNullException(nameof(twitchRewardSource));
        _imageService = new AvatarImageService();
        _thumbnailUrlResolver = thumbnailUrlResolver;
        _onSettingsChanged = onSettingsChanged;

        TwitchRewardOptions = _twitchRewardSource.RewardOptions;
        RefreshTwitchRewardsCommand = _twitchRewardSource.RefreshTwitchRewardsCommand;
        UnlinkTwitchRewardCommand = _twitchRewardSource.UnlinkTwitchRewardCommand;

        _settings.AvatarSwapProfiles.CollectionChanged += OnProfilesChanged;
        _settings.AvatarRouletteProfiles.CollectionChanged += OnRouletteProfilesChanged;

        AddSwapCommand = new RelayCommand(AddSwap);
        AddRouletteCommand = new RelayCommand(AddRoulette);
        OpenSwapEditorCommand = new RelayCommand(p => OpenSwapEditor(p as AvatarSwapCardViewModel));
        OpenRouletteEditorCommand = new RelayCommand(p => OpenRouletteEditor(p as AvatarRouletteCardViewModel));
        SaveSwapEditorCommand = new RelayCommand(SaveSwapEditor);
        SaveRouletteEditorCommand = new RelayCommand(SaveRouletteEditor);
        CloseEditorCommand = new RelayCommand(CloseEditor);
        DeleteSwapCommand = new RelayCommand(DeleteSwap, () => SelectedSwapCard is not null);
        DeleteRouletteCommand = new RelayCommand(DeleteRoulette, () => SelectedRouletteCard is not null);
        AddChannelPointRuleCommand = new RelayCommand(AddChannelPointRule, () => SelectedSwapCard is not null);
        AddBitsRuleCommand = new RelayCommand(AddBitsRule, () => SelectedSwapCard is not null);
        AddSubsRuleCommand = new RelayCommand(AddSubsRule, () => SelectedSwapCard is not null);
        AddPaymentRuleCommand = new RelayCommand(AddPaymentRule, () => SelectedSwapCard is not null);
        AddAdvancedTriggerCommand = new RelayCommand(p => AddAdvancedTrigger(p as string));
        AddRoulettePoolEntryCommand = new RelayCommand(AddRoulettePoolEntry, () => SelectedRouletteCard is not null);
        DeleteRuleCommand = new RelayCommand(p => DeleteRule(p as IRuleRowViewModel));
        BackToListCommand = new RelayCommand(BackToList);
        PickGlobalReturnAvatarCommand = new RelayCommand(PickGlobalReturnAvatar);
        UseCurrentAvatarForGlobalReturnCommand = new RelayCommand(UseCurrentAvatarForGlobalReturn);
        ClearGlobalReturnCommand = new RelayCommand(ClearGlobalReturn);

        ToggleChannelPointSectionCommand = new RelayCommand(() => IsChannelPointSectionCollapsed = !IsChannelPointSectionCollapsed);
        ToggleBitsSubsSectionCommand = new RelayCommand(() => IsBitsSubsSectionCollapsed = !IsBitsSubsSectionCollapsed);
        ToggleRouletteSectionCommand = new RelayCommand(() => IsRouletteSectionCollapsed = !IsRouletteSectionCollapsed);

        RebuildCards();
        RefreshGlobalReturnAvatarImage();
    }

    private System.Windows.Media.ImageSource? _globalReturnAvatarImage;
    public System.Windows.Media.ImageSource? GlobalReturnAvatarImage
    {
        get => _globalReturnAvatarImage;
        private set
        {
            if (SetProperty(ref _globalReturnAvatarImage, value))
            {
                RaisePropertyChanged(nameof(HasGlobalReturnAvatarImage));
            }
        }
    }

    public bool HasGlobalReturnAvatarImage => _globalReturnAvatarImage is not null;

    private void RefreshGlobalReturnAvatarImage()
    {
        var avatarId = _settings.MasterAvatarSwapReturnId;
        if (string.IsNullOrWhiteSpace(avatarId))
        {
            GlobalReturnAvatarImage = null;
            return;
        }

        var thumbnailUrl = _thumbnailUrlResolver?.Invoke(avatarId);
        var capturedId = avatarId;

        var syncImage = _imageService.GetAvatarImage(avatarId, null, thumbnailUrl);
        if (syncImage is not null)
        {
            GlobalReturnAvatarImage = syncImage;
        }

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var asyncImage = await _imageService.GetAvatarImageAsync(capturedId, null, thumbnailUrl, System.Threading.CancellationToken.None);
                if (asyncImage is not null && string.Equals(_settings.MasterAvatarSwapReturnId, capturedId, StringComparison.Ordinal))
                {
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => GlobalReturnAvatarImage = asyncImage);
                }
            }
            catch
            {
                // Keep whatever sync image we already have.
            }
        });
    }

    public ObservableCollection<AvatarSwapCardViewModel> SwapCards { get; } = new();

    public ObservableCollection<AvatarRouletteCardViewModel> RouletteCards { get; } = new();

    public ObservableCollection<TwitchRewardOption> TwitchRewardOptions { get; }
    public IReadOnlyList<ChatCommandPermission> ChatCommandPermissionOptions { get; } = Enum.GetValues<ChatCommandPermission>();
    public ICommand RefreshTwitchRewardsCommand { get; }
    public ICommand UnlinkTwitchRewardCommand { get; }

    public ObservableCollection<InlineChannelPointRuleRowViewModel> ChannelPointRows { get; } = new();

    public ObservableCollection<InlineBitsRuleRowViewModel> BitsRows { get; } = new();

    public ObservableCollection<InlineSubsRuleRowViewModel> SubsRows { get; } = new();

    public ObservableCollection<InlinePaymentRuleRowViewModel> PaymentRows { get; } = new();

    public ObservableCollection<IRuleRowViewModel> RouletteTriggerRows { get; } = new();

    public ObservableCollection<RoulettePoolEntryRowViewModel> RoulettePoolRows { get; } = new();

    public ObservableCollection<InlineChannelPointRuleRowViewModel> RouletteChannelPointRows { get; } = new();
    public ObservableCollection<InlineBitsRuleRowViewModel> RouletteBitsRows { get; } = new();
    public ObservableCollection<InlineSubsRuleRowViewModel> RouletteSubsRows { get; } = new();
    public ObservableCollection<InlineRouletteRuleRowViewModel> RouletteAdvancedRows { get; } = new();

    public string? GlobalReturnAvatarId => _settings.MasterAvatarSwapReturnId;

    public string? GlobalReturnAvatarName => _settings.MasterAvatarSwapReturnName;

    public string GlobalReturnAvatarDisplayName => _settings.MasterAvatarSwapReturnDisplayName;

    public bool HasGlobalReturnAvatar => !string.IsNullOrWhiteSpace(GlobalReturnAvatarId);

    public bool PermanentSwapModeEnabled
    {
        get => _settings.PermanentSwapModeEnabled;
        set
        {
            if (_settings.PermanentSwapModeEnabled != value)
            {
                _settings.PermanentSwapModeEnabled = value;
                RaisePropertyChanged();
                NotifySettingsChanged();
            }
        }
    }

    private AvatarSwapCardViewModel? _selectedSwapCard;
    public AvatarSwapCardViewModel? SelectedSwapCard
    {
        get => _selectedSwapCard;
        set
        {
            if (SetProperty(ref _selectedSwapCard, value))
            {
                RebuildRows();
                NotifySelectionCommandsChanged();
            }
        }
    }

    private AvatarRouletteCardViewModel? _selectedRouletteCard;
    public AvatarRouletteCardViewModel? SelectedRouletteCard
    {
        get => _selectedRouletteCard;
        set
        {
            if (SetProperty(ref _selectedRouletteCard, value))
            {
                RebuildRows();
                NotifySelectionCommandsChanged();
            }
        }
    }

    private bool _isSwapEditorOpen;
    public bool IsSwapEditorOpen
    {
        get => _isSwapEditorOpen;
        set
        {
            if (SetProperty(ref _isSwapEditorOpen, value))
            {
                RaisePropertyChanged(nameof(IsEditorOpen));
            }
        }
    }

    private bool _isRouletteEditorOpen;
    public bool IsRouletteEditorOpen
    {
        get => _isRouletteEditorOpen;
        set
        {
            if (SetProperty(ref _isRouletteEditorOpen, value))
            {
                RaisePropertyChanged(nameof(IsEditorOpen));
            }
        }
    }

    public bool IsEditorOpen => IsSwapEditorOpen || IsRouletteEditorOpen;

    private object? _rightPaneContent;
    public object? RightPaneContent
    {
        get => _rightPaneContent;
        private set
        {
            if (SetProperty(ref _rightPaneContent, value))
            {
                RaisePropertyChanged(nameof(IsEditorOpen));
            }
        }
    }

    private IRuleRowViewModel? _selectedRule;
    public IRuleRowViewModel? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (SetProperty(ref _selectedRule, value))
            {
                if (value is not null)
                {
                    RightPaneContent = value;
                }
            }
        }
    }

    private void BackToList()
    {
        SelectedRule = null;
        if (SelectedSwapCard is not null)
        {
            RightPaneContent = new RuleListPaneViewModel(RuleListPaneKind.Swap, SelectedSwapCard.Profile?.TargetAvatarName);
        }
        else if (SelectedRouletteCard is not null)
        {
            RightPaneContent = new RuleListPaneViewModel(RuleListPaneKind.Roulette, SelectedRouletteCard.Roulette?.Name);
        }
    }

    public bool IsChannelPointSectionCollapsed
    {
        get => _isChannelPointSectionCollapsed;
        set => SetProperty(ref _isChannelPointSectionCollapsed, value);
    }

    public bool IsBitsSubsSectionCollapsed
    {
        get => _isBitsSubsSectionCollapsed;
        set => SetProperty(ref _isBitsSubsSectionCollapsed, value);
    }

    public bool IsRouletteSectionCollapsed
    {
        get => _isRouletteSectionCollapsed;
        set => SetProperty(ref _isRouletteSectionCollapsed, value);
    }

    public string? FilterText
    {
        get => _filterText;
        set => SetProperty(ref _filterText, value);
    }

    public int SwapCardCount => SwapCards.Count;
    public int RouletteCardCount => RouletteCards.Count;

    public RelayCommand AddSwapCommand { get; }
    public RelayCommand AddRouletteCommand { get; }
    public RelayCommand OpenSwapEditorCommand { get; }
    public RelayCommand OpenRouletteEditorCommand { get; }
    public RelayCommand SaveSwapEditorCommand { get; }
    public RelayCommand SaveRouletteEditorCommand { get; }
    public RelayCommand CloseEditorCommand { get; }
    public RelayCommand DeleteSwapCommand { get; }
    public RelayCommand DeleteRouletteCommand { get; }
    public RelayCommand AddChannelPointRuleCommand { get; }
    public RelayCommand AddBitsRuleCommand { get; }
    public RelayCommand AddSubsRuleCommand { get; }
    public RelayCommand AddPaymentRuleCommand { get; }
    public RelayCommand AddAdvancedTriggerCommand { get; }
    public RelayCommand AddRoulettePoolEntryCommand { get; }
    public RelayCommand DeleteRuleCommand { get; }
    public RelayCommand BackToListCommand { get; }
    public RelayCommand PickGlobalReturnAvatarCommand { get; }
    public RelayCommand UseCurrentAvatarForGlobalReturnCommand { get; }
    public RelayCommand ClearGlobalReturnCommand { get; }

    public RelayCommand ToggleChannelPointSectionCommand { get; }
    public RelayCommand ToggleBitsSubsSectionCommand { get; }
    public RelayCommand ToggleRouletteSectionCommand { get; }

    public void RebuildCards()
    {
        SwapCards.Clear();
        foreach (var profile in _settings.AvatarSwapProfiles)
        {
            SwapCards.Add(new AvatarSwapCardViewModel(profile, _imageService));
        }

        RouletteCards.Clear();
        foreach (var roulette in _settings.AvatarRouletteProfiles)
        {
            RouletteCards.Add(new AvatarRouletteCardViewModel(roulette, _imageService));
        }
    }

    private void RebuildRows()
    {
        ChannelPointRows.Clear();
        BitsRows.Clear();
        SubsRows.Clear();
        PaymentRows.Clear();
        RouletteTriggerRows.Clear();
        RoulettePoolRows.Clear();
        RouletteChannelPointRows.Clear();
        RouletteBitsRows.Clear();
        RouletteSubsRows.Clear();
        RouletteAdvancedRows.Clear();

        if (SelectedSwapCard is not null)
        {
            var swapProfile = SelectedSwapCard.Profile;
            foreach (var r in swapProfile.ChannelPointRules)
            {
                var row = new InlineChannelPointRuleRowViewModel(r, swapProfile);
                WireRowCommands(row);
                ChannelPointRows.Add(row);
            }
            foreach (var r in swapProfile.BitsRules)
            {
                var row = new InlineBitsRuleRowViewModel(r, swapProfile);
                WireRowCommands(row);
                BitsRows.Add(row);
            }
            foreach (var r in swapProfile.SubsRules)
            {
                var row = new InlineSubsRuleRowViewModel(r, swapProfile);
                WireRowCommands(row);
                SubsRows.Add(row);
            }
            foreach (var r in swapProfile.PaymentRules)
            {
                var row = new InlinePaymentRuleRowViewModel(r, swapProfile);
                WireRowCommands(row);
                PaymentRows.Add(row);
            }
        }

        if (SelectedRouletteCard is not null)
        {
            foreach (var entry in SelectedRouletteCard.Roulette.Pool)
            {
                RoulettePoolRows.Add(new RoulettePoolEntryRowViewModel(entry, _imageService));
            }

            foreach (var r in SelectedRouletteCard.Roulette.Triggers)
            {
                IRuleRowViewModel row = r.TriggerType switch
                {
                    TwitchTriggerType.ChannelPoints => new InlineChannelPointRuleRowViewModel(r),
                    TwitchTriggerType.Bits => new InlineBitsRuleRowViewModel(r),
                    TwitchTriggerType.Subscriptions or TwitchTriggerType.GiftSubscription => new InlineSubsRuleRowViewModel(r),
                    _ => new InlineRouletteRuleRowViewModel(r)
                };
                WireRowCommands(row);
                RouletteTriggerRows.Add(row);

                switch (r.TriggerType)
                {
                    case TwitchTriggerType.ChannelPoints:
                        RouletteChannelPointRows.Add((InlineChannelPointRuleRowViewModel)row);
                        break;
                    case TwitchTriggerType.Bits:
                        RouletteBitsRows.Add((InlineBitsRuleRowViewModel)row);
                        break;
                    case TwitchTriggerType.Subscriptions:
                    case TwitchTriggerType.GiftSubscription:
                        RouletteSubsRows.Add((InlineSubsRuleRowViewModel)row);
                        break;
                    default:
                        RouletteAdvancedRows.Add((InlineRouletteRuleRowViewModel)row);
                        break;
                }
            }
        }
    }

    private void WireRowCommands(IRuleRowViewModel row)
    {
        row.EditCommand = new RelayCommand(() => SelectedRule = row);
        row.DeleteCommand = new RelayCommand(() => DeleteRule(row));
    }

    private void AddSwap()
    {
        var profile = new AvatarSwapProfile { TargetAvatarName = "New Avatar" };
        _settings.AvatarSwapProfiles.Add(profile);
        var card = SwapCards.FirstOrDefault(c => c.Profile == profile)
            ?? new AvatarSwapCardViewModel(profile, _imageService);
        SelectedRouletteCard = null;
        SelectedSwapCard = card;
        IsSwapEditorOpen = true;
        IsRouletteEditorOpen = false;
    }

    private void AddRoulette()
    {
        var roulette = new AvatarRouletteProfile { Name = "New Roulette" };
        _settings.AvatarRouletteProfiles.Add(roulette);
        var card = RouletteCards.FirstOrDefault(c => c.Roulette == roulette)
            ?? new AvatarRouletteCardViewModel(roulette, _imageService);
        SelectedSwapCard = null;
        SelectedRouletteCard = card;
        IsRouletteEditorOpen = true;
        IsSwapEditorOpen = false;
    }

    private void OpenSwapEditor(AvatarSwapCardViewModel? card)
    {
        if (card is null) return;
        SelectedRouletteCard = null;
        SelectedSwapCard = card;
        IsSwapEditorOpen = true;
        IsRouletteEditorOpen = false;
        RebuildRows();
        RightPaneContent = new RuleListPaneViewModel(RuleListPaneKind.Swap, card.Profile?.TargetAvatarName);
    }

    private void OpenRouletteEditor(AvatarRouletteCardViewModel? card)
    {
        if (card is null) return;
        SelectedSwapCard = null;
        SelectedRouletteCard = card;
        IsRouletteEditorOpen = true;
        IsSwapEditorOpen = false;
        RebuildRows();
        RightPaneContent = new RuleListPaneViewModel(RuleListPaneKind.Roulette, card.Roulette?.Name);
    }

    private void SaveSwapEditor()
    {
        if (SelectedSwapCard is null) return;
        SelectedSwapCard.Profile.UpdatedAt = DateTime.UtcNow;
        IsSwapEditorOpen = false;
        RightPaneContent = null;
        SelectedRule = null;
        SelectedSwapCard = null;
        NotifySettingsChanged();
    }

    private void SaveRouletteEditor()
    {
        if (SelectedRouletteCard is null) return;
        SelectedRouletteCard.Roulette.UpdatedAt = DateTime.UtcNow;
        IsRouletteEditorOpen = false;
        RightPaneContent = null;
        SelectedRule = null;
        SelectedRouletteCard = null;
        NotifySettingsChanged();
    }

    private void NotifySelectionCommandsChanged()
    {
        DeleteSwapCommand.NotifyCanExecuteChanged();
        DeleteRouletteCommand.NotifyCanExecuteChanged();
        AddChannelPointRuleCommand.NotifyCanExecuteChanged();
        AddBitsRuleCommand.NotifyCanExecuteChanged();
        AddSubsRuleCommand.NotifyCanExecuteChanged();
        AddPaymentRuleCommand.NotifyCanExecuteChanged();
        AddRoulettePoolEntryCommand.NotifyCanExecuteChanged();
    }

    private void CloseEditor()
    {
        IsSwapEditorOpen = false;
        IsRouletteEditorOpen = false;
        RightPaneContent = null;
        SelectedRule = null;
        SelectedSwapCard = null;
        SelectedRouletteCard = null;
    }

    private void DeleteSwap()
    {
        if (SelectedSwapCard is null) return;
        _settings.AvatarSwapProfiles.Remove(SelectedSwapCard.Profile);
        SwapCards.Remove(SelectedSwapCard);
        IsSwapEditorOpen = false;
        RightPaneContent = null;
        SelectedRule = null;
        SelectedSwapCard = null;
        NotifySettingsChanged();
    }

    private void DeleteRoulette()
    {
        if (SelectedRouletteCard is null) return;
        _settings.AvatarRouletteProfiles.Remove(SelectedRouletteCard.Roulette);
        RouletteCards.Remove(SelectedRouletteCard);
        IsRouletteEditorOpen = false;
        RightPaneContent = null;
        SelectedRule = null;
        SelectedRouletteCard = null;
        NotifySettingsChanged();
    }

    private void NotifySettingsChanged()
    {
        _onSettingsChanged?.Invoke();
    }

    private void AddChannelPointRule()
    {
        if (SelectedSwapCard is null) return;
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = SelectedSwapCard.Profile.TargetAvatarId,
            AvatarTargetName = SelectedSwapCard.Profile.TargetAvatarName,
            ChannelPointRewardCost = 100,
            Name = "New Channel Point Swap",
            ChannelPointRewardTitle = "New Channel Point Swap"
        };
        SelectedSwapCard.Profile.ChannelPointRules.Add(rule);
        var row = new InlineChannelPointRuleRowViewModel(rule, SelectedSwapCard.Profile);
        WireRowCommands(row);
        ChannelPointRows.Add(row);
        NotifySettingsChanged();
    }

    private void AddBitsRule()
    {
        if (SelectedSwapCard is null) return;
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.Bits,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = SelectedSwapCard.Profile.TargetAvatarId,
            AvatarTargetName = SelectedSwapCard.Profile.TargetAvatarName,
            MinimumAmount = 100,
            Name = "New Bits Swap"
        };
        SelectedSwapCard.Profile.BitsRules.Add(rule);
        var row = new InlineBitsRuleRowViewModel(rule, SelectedSwapCard.Profile);
        WireRowCommands(row);
        BitsRows.Add(row);
        NotifySettingsChanged();
    }

    private void AddSubsRule()
    {
        if (SelectedSwapCard is null) return;
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.Subscriptions,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = SelectedSwapCard.Profile.TargetAvatarId,
            AvatarTargetName = SelectedSwapCard.Profile.TargetAvatarName,
            Name = "New Subs Swap"
        };
        SelectedSwapCard.Profile.SubsRules.Add(rule);
        var row = new InlineSubsRuleRowViewModel(rule, SelectedSwapCard.Profile);
        WireRowCommands(row);
        SubsRows.Add(row);
        NotifySettingsChanged();
    }

    private void AddPaymentRule()
    {
        if (SelectedSwapCard is null) return;
        var rule = new CashPaymentRule
        {
            Name = "New Cash Payment Swap",
            Provider = CashPaymentProvider.StreamElements,
            IsEnabled = true,
            ActionKind = CashPaymentActionKind.TriggerAction,
            TriggerAction = new TriggerRule
            {
                ActionType = OscActionType.AvatarChange,
                AvatarChangeTargetId = SelectedSwapCard.Profile.TargetAvatarId,
                AvatarTargetName = SelectedSwapCard.Profile.TargetAvatarName
            }
        };
        SelectedSwapCard.Profile.PaymentRules.Add(rule);
        var row = new InlinePaymentRuleRowViewModel(rule, SelectedSwapCard.Profile);
        WireRowCommands(row);
        PaymentRows.Add(row);
        NotifySettingsChanged();
    }

    private void AddAdvancedTrigger(string? triggerSource)
    {
        if (string.IsNullOrEmpty(triggerSource)) return;
        if (!Enum.TryParse<TwitchTriggerType>(triggerSource, out var type)) return;

        if (SelectedRouletteCard is not null)
        {
            var rule = new TriggerRule
            {
                TriggerType = type,
                ActionType = OscActionType.AvatarRoulet,
                ChatCommandEnabled = type == TwitchTriggerType.ChatCommand,
                ChatCommandText = type == TwitchTriggerType.ChatCommand ? "!roulette" : string.Empty,
                Name = $"New {type} Roulette Trigger"
            };
            SelectedRouletteCard.Roulette.Triggers.Add(rule);
            var row = GetRowViewModelForRule(rule);
            if (row != null)
            {
                WireRowCommands(row);
                RouletteTriggerRows.Add(row);

                switch (type)
                {
                    case TwitchTriggerType.ChannelPoints:
                        RouletteChannelPointRows.Add((InlineChannelPointRuleRowViewModel)row);
                        break;
                    case TwitchTriggerType.Bits:
                        RouletteBitsRows.Add((InlineBitsRuleRowViewModel)row);
                        break;
                    case TwitchTriggerType.Subscriptions:
                    case TwitchTriggerType.GiftSubscription:
                        RouletteSubsRows.Add((InlineSubsRuleRowViewModel)row);
                        break;
                    default:
                        var advancedRow = (InlineRouletteRuleRowViewModel)row;
                        RouletteAdvancedRows.Add(advancedRow);
                        SelectedRule = advancedRow;
                        break;
                }
            }
        }
        else if (SelectedSwapCard is not null)
        {
            var rule = new TriggerRule
            {
                TriggerType = type,
                ActionType = OscActionType.AvatarChange,
                AvatarChangeTargetId = SelectedSwapCard.Profile.TargetAvatarId,
                AvatarTargetName = SelectedSwapCard.Profile.TargetAvatarName,
                Name = $"New {type} Swap"
            };

            switch (type)
            {
                case TwitchTriggerType.ChannelPoints:
                    SelectedSwapCard.Profile.ChannelPointRules.Add(rule);
                    var cpRow = new InlineChannelPointRuleRowViewModel(rule, SelectedSwapCard.Profile);
                    WireRowCommands(cpRow);
                    ChannelPointRows.Add(cpRow);
                    break;
                case TwitchTriggerType.Bits:
                    SelectedSwapCard.Profile.BitsRules.Add(rule);
                    var bitsRow = new InlineBitsRuleRowViewModel(rule, SelectedSwapCard.Profile);
                    WireRowCommands(bitsRow);
                    BitsRows.Add(bitsRow);
                    break;
                case TwitchTriggerType.Subscriptions:
                case TwitchTriggerType.GiftSubscription:
                    SelectedSwapCard.Profile.SubsRules.Add(rule);
                    var subsRow = new InlineSubsRuleRowViewModel(rule, SelectedSwapCard.Profile);
                    WireRowCommands(subsRow);
                    SubsRows.Add(subsRow);
                    break;
                default:
                    SelectedSwapCard.Profile.ChannelPointRules.Add(rule);
                    var defaultRow = new InlineChannelPointRuleRowViewModel(rule, SelectedSwapCard.Profile);
                    WireRowCommands(defaultRow);
                    ChannelPointRows.Add(defaultRow);
                    break;
            }
        }

        NotifySettingsChanged();
    }

    private IRuleRowViewModel? GetRowViewModelForRule(TriggerRule rule)
    {
        return rule.TriggerType switch
        {
            TwitchTriggerType.ChannelPoints => new InlineChannelPointRuleRowViewModel(rule),
            TwitchTriggerType.Bits => new InlineBitsRuleRowViewModel(rule),
            TwitchTriggerType.Subscriptions or TwitchTriggerType.GiftSubscription => new InlineSubsRuleRowViewModel(rule),
            _ => new InlineRouletteRuleRowViewModel(rule)
        };
    }

    private void AddRoulettePoolEntry()
    {
        // Pool selection requires the window-owned avatar picker; never create blank entries.
    }

    private void RebuildRoulettePoolRows()
    {
        RoulettePoolRows.Clear();
        if (SelectedRouletteCard is null) return;

        foreach (var entry in SelectedRouletteCard.Roulette.Pool)
        {
            RoulettePoolRows.Add(new RoulettePoolEntryRowViewModel(entry, _imageService));
        }
    }

    public void SetRoulettePoolSelection(
        IReadOnlyList<string> selectedAvatarIds,
        IReadOnlyList<VrChatAvatarSummary> availableAvatars)
    {
        if (SelectedRouletteCard is null) return;

        var avatarById = availableAvatars
            .Where(avatar => !string.IsNullOrWhiteSpace(avatar.Id))
            .GroupBy(avatar => avatar.Id.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var selectedIds = selectedAvatarIds
            .Select(id => id?.Trim() ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var pool = SelectedRouletteCard.Roulette.Pool;
        pool.Clear();
        foreach (var avatarId in selectedIds)
        {
            avatarById.TryGetValue(avatarId, out var avatar);
            pool.Add(new RouletteAvatarEntry
            {
                AvatarId = avatarId,
                AvatarName = string.IsNullOrWhiteSpace(avatar?.Name) ? avatarId : avatar.Name,
                ThumbnailUrl = avatar?.ThumbnailUrl
            });
        }

        RebuildRoulettePoolRows();
        NotifySettingsChanged();
    }

    private void DeleteRule(IRuleRowViewModel? row)
    {
        if (row is null) return;

        var ruleObj = row.Rule;

        if (SelectedRouletteCard is not null && ruleObj is TriggerRule rTrigger
            && SelectedRouletteCard.Roulette.Triggers.Remove(rTrigger))
        {
            RouletteTriggerRows.Remove(row);
            if (row is InlineChannelPointRuleRowViewModel cpRow)
                RouletteChannelPointRows.Remove(cpRow);
            else if (row is InlineBitsRuleRowViewModel bitsRow)
                RouletteBitsRows.Remove(bitsRow);
            else if (row is InlineSubsRuleRowViewModel subsRow)
                RouletteSubsRows.Remove(subsRow);
            else if (row is InlineRouletteRuleRowViewModel advancedRow)
                RouletteAdvancedRows.Remove(advancedRow);
        }
        else if (SelectedSwapCard is not null)
        {
            if (row is InlineChannelPointRuleRowViewModel cp
                && SelectedSwapCard.Profile.ChannelPointRules.Remove((TriggerRule)cp.Rule))
            {
                ChannelPointRows.Remove(cp);
            }
            else if (row is InlineBitsRuleRowViewModel bits
                && SelectedSwapCard.Profile.BitsRules.Remove((TriggerRule)bits.Rule))
            {
                BitsRows.Remove(bits);
            }
            else if (row is InlineSubsRuleRowViewModel subs
                && SelectedSwapCard.Profile.SubsRules.Remove((TriggerRule)subs.Rule))
            {
                SubsRows.Remove(subs);
            }
            else if (row is InlinePaymentRuleRowViewModel pay
                && SelectedSwapCard.Profile.PaymentRules.Remove((CashPaymentRule)pay.Rule))
            {
                PaymentRows.Remove(pay);
            }
        }

        if (ReferenceEquals(SelectedRule, row))
        {
            SelectedRule = null;
        }

        NotifySettingsChanged();
    }

    private void BeginInlineEdit(IRuleRowViewModel? row)
    {
        // No-op: rows are now compact cards; the full editor opens in the right pane
        // via SelectedRule. Kept for binary compatibility; callers should use
        // SelectedRule instead.
    }

    private void CommitInlineEdit()
    {
        // No-op: see BeginInlineEdit.
    }

    private void CancelInlineEdit()
    {
        // No-op: see BeginInlineEdit.
    }

    private void PickGlobalReturnAvatar()
    {
    }

    private void UseCurrentAvatarForGlobalReturn()
    {
    }

    private void ClearGlobalReturn()
    {
        _settings.MasterAvatarSwapReturnId = null;
        _settings.MasterAvatarSwapReturnName = null;
        RaisePropertyChanged(nameof(GlobalReturnAvatarId));
        RaisePropertyChanged(nameof(GlobalReturnAvatarName));
        RaisePropertyChanged(nameof(GlobalReturnAvatarDisplayName));
        RaisePropertyChanged(nameof(HasGlobalReturnAvatar));
        RefreshGlobalReturnAvatarImage();
        NotifySettingsChanged();
    }

    public void SetGlobalReturnAvatar(string? id, string? name)
    {
        _settings.MasterAvatarSwapReturnId = id;
        _settings.MasterAvatarSwapReturnName = name;
        RaisePropertyChanged(nameof(GlobalReturnAvatarId));
        RaisePropertyChanged(nameof(GlobalReturnAvatarName));
        RaisePropertyChanged(nameof(GlobalReturnAvatarDisplayName));
        RaisePropertyChanged(nameof(HasGlobalReturnAvatar));
        RefreshGlobalReturnAvatarImage();
        NotifySettingsChanged();
    }

    public void SetTargetAvatar(string? id, string? name)
    {
        if (SelectedSwapCard is null) return;
        SelectedSwapCard.Profile.TargetAvatarId = id ?? string.Empty;
        SelectedSwapCard.Profile.TargetAvatarName = name ?? string.Empty;
        var thumbnailUrl = _thumbnailUrlResolver?.Invoke(id);
        if (!string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            SelectedSwapCard.SetThumbnailUrl(thumbnailUrl);
        }
        NotifySettingsChanged();
    }

    public void OnWindowClosed()
    {
        CloseEditor();
    }

    private void OnProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            RebuildCards();
            return;
        }
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<AvatarSwapProfile>())
            {
                SwapCards.Add(new AvatarSwapCardViewModel(item, _imageService));
            }
        }
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<AvatarSwapProfile>())
            {
                var card = SwapCards.FirstOrDefault(c => c.Profile == item);
                if (card is not null)
                {
                    SwapCards.Remove(card);
                }
            }
        }
    }

    private void OnRouletteProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            RebuildCards();
            return;
        }
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<AvatarRouletteProfile>())
            {
                RouletteCards.Add(new AvatarRouletteCardViewModel(item, _imageService));
            }
        }
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<AvatarRouletteProfile>())
            {
                var card = RouletteCards.FirstOrDefault(c => c.Roulette == item);
                if (card is not null)
                {
                    RouletteCards.Remove(card);
                }
            }
        }
    }
}

public sealed class RoulettePoolEntryRowViewModel : ObservableObject
{
    private readonly RouletteAvatarEntry _entry;
    private readonly System.Windows.Media.ImageSource? _image;

    public RoulettePoolEntryRowViewModel(RouletteAvatarEntry entry, AvatarImageService imageService)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        if (imageService is null) throw new ArgumentNullException(nameof(imageService));

        _image = imageService.GetAvatarImage(_entry.AvatarId, null, _entry.ThumbnailUrl);
    }

    public string AvatarId => _entry.AvatarId;
    public string AvatarName => _entry.AvatarName;
    public string? ThumbnailUrl => _entry.ThumbnailUrl;
    public System.Windows.Media.ImageSource? Image => _image;
    public bool HasImage => _image is not null;
}
