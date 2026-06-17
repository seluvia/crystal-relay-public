using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class AvatarSwapManagerViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly AvatarImageService _imageService;

    private bool _isChannelPointSectionCollapsed;
    private bool _isBitsSubsSectionCollapsed;
    private bool _isRouletteSectionCollapsed;
    private string? _filterText;

    public AvatarSwapManagerViewModel(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _imageService = new AvatarImageService();

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
        DeleteRuleCommand = new RelayCommand(p => DeleteRule(p as InlineAvatarSwapRuleRowViewModel));
        BeginInlineEditCommand = new RelayCommand(p => BeginInlineEdit(p as InlineAvatarSwapRuleRowViewModel));
        CommitInlineEditCommand = new RelayCommand(CommitInlineEdit);
        CancelInlineEditCommand = new RelayCommand(CancelInlineEdit);
        PickGlobalReturnAvatarCommand = new RelayCommand(PickGlobalReturnAvatar);
        UseCurrentAvatarForGlobalReturnCommand = new RelayCommand(UseCurrentAvatarForGlobalReturn);
        ClearGlobalReturnCommand = new RelayCommand(ClearGlobalReturn);

        ToggleChannelPointSectionCommand = new RelayCommand(() => IsChannelPointSectionCollapsed = !IsChannelPointSectionCollapsed);
        ToggleBitsSubsSectionCommand = new RelayCommand(() => IsBitsSubsSectionCollapsed = !IsBitsSubsSectionCollapsed);
        ToggleRouletteSectionCommand = new RelayCommand(() => IsRouletteSectionCollapsed = !IsRouletteSectionCollapsed);

        RebuildCards();
    }

    public ObservableCollection<AvatarSwapCardViewModel> SwapCards { get; } = new();

    public ObservableCollection<AvatarRouletteCardViewModel> RouletteCards { get; } = new();

    public ObservableCollection<InlineAvatarSwapRuleRowViewModel> ChannelPointRows { get; } = new();

    public ObservableCollection<InlineAvatarSwapRuleRowViewModel> BitsRows { get; } = new();

    public ObservableCollection<InlineAvatarSwapRuleRowViewModel> SubsRows { get; } = new();

    public ObservableCollection<InlineAvatarSwapRuleRowViewModel> PaymentRows { get; } = new();

    public ObservableCollection<InlineAvatarSwapRuleRowViewModel> RouletteTriggerRows { get; } = new();

    public string? GlobalReturnAvatarId => _settings.MasterAvatarSwapReturnId;

    public string? GlobalReturnAvatarName => _settings.MasterAvatarSwapReturnName;

    public string GlobalReturnAvatarDisplayName => _settings.MasterAvatarSwapReturnDisplayName;

    public bool HasGlobalReturnAvatar => !string.IsNullOrWhiteSpace(GlobalReturnAvatarId);

    private AvatarSwapCardViewModel? _selectedSwapCard;
    public AvatarSwapCardViewModel? SelectedSwapCard
    {
        get => _selectedSwapCard;
        set
        {
            if (SetProperty(ref _selectedSwapCard, value))
            {
                RebuildRows();
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

    private InlineAvatarSwapRuleRowViewModel? _editingRule;
    public InlineAvatarSwapRuleRowViewModel? EditingRule
    {
        get => _editingRule;
        set => SetProperty(ref _editingRule, value);
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
    public RelayCommand DeleteRuleCommand { get; }
    public RelayCommand BeginInlineEditCommand { get; }
    public RelayCommand CommitInlineEditCommand { get; }
    public RelayCommand CancelInlineEditCommand { get; }
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
            RouletteCards.Add(new AvatarRouletteCardViewModel(roulette));
        }
    }

    private void RebuildRows()
    {
        ChannelPointRows.Clear();
        BitsRows.Clear();
        SubsRows.Clear();
        PaymentRows.Clear();
        RouletteTriggerRows.Clear();

        if (SelectedSwapCard is not null)
        {
            foreach (var r in SelectedSwapCard.Profile.ChannelPointRules) ChannelPointRows.Add(new InlineAvatarSwapRuleRowViewModel(r));
            foreach (var r in SelectedSwapCard.Profile.BitsRules) BitsRows.Add(new InlineAvatarSwapRuleRowViewModel(r));
            foreach (var r in SelectedSwapCard.Profile.SubsRules) SubsRows.Add(new InlineAvatarSwapRuleRowViewModel(r));
            foreach (var r in SelectedSwapCard.Profile.PaymentRules) PaymentRows.Add(new InlineAvatarSwapRuleRowViewModel(r));
        }

        if (SelectedRouletteCard is not null)
        {
            foreach (var r in SelectedRouletteCard.Roulette.Triggers) RouletteTriggerRows.Add(new InlineAvatarSwapRuleRowViewModel(r));
        }
    }

    private void AddSwap()
    {
        var profile = new AvatarSwapProfile { TargetAvatarName = "New Avatar" };
        _settings.AvatarSwapProfiles.Add(profile);
        var card = new AvatarSwapCardViewModel(profile, _imageService);
        SwapCards.Add(card);
        SelectedSwapCard = card;
        IsSwapEditorOpen = true;
        IsRouletteEditorOpen = false;
    }

    private void AddRoulette()
    {
        var roulette = new AvatarRouletteProfile { Name = "New Roulette" };
        _settings.AvatarRouletteProfiles.Add(roulette);
        var card = new AvatarRouletteCardViewModel(roulette);
        RouletteCards.Add(card);
        SelectedRouletteCard = card;
        IsRouletteEditorOpen = true;
        IsSwapEditorOpen = false;
    }

    private void OpenSwapEditor(AvatarSwapCardViewModel? card)
    {
        if (card is null) return;
        SelectedSwapCard = card;
        IsSwapEditorOpen = true;
        IsRouletteEditorOpen = false;
        RebuildRows();
    }

    private void OpenRouletteEditor(AvatarRouletteCardViewModel? card)
    {
        if (card is null) return;
        SelectedRouletteCard = card;
        IsRouletteEditorOpen = true;
        IsSwapEditorOpen = false;
        RebuildRows();
    }

    private void SaveSwapEditor()
    {
        if (SelectedSwapCard is null) return;
        SelectedSwapCard.Profile.UpdatedAt = DateTime.UtcNow;
        IsSwapEditorOpen = false;
        SelectedSwapCard = null;
    }

    private void SaveRouletteEditor()
    {
        if (SelectedRouletteCard is null) return;
        SelectedRouletteCard.Roulette.UpdatedAt = DateTime.UtcNow;
        IsRouletteEditorOpen = false;
        SelectedRouletteCard = null;
    }

    private void CloseEditor()
    {
        IsSwapEditorOpen = false;
        IsRouletteEditorOpen = false;
        EditingRule = null;
        SelectedSwapCard = null;
        SelectedRouletteCard = null;
    }

    private void DeleteSwap()
    {
        if (SelectedSwapCard is null) return;
        _settings.AvatarSwapProfiles.Remove(SelectedSwapCard.Profile);
        SwapCards.Remove(SelectedSwapCard);
        IsSwapEditorOpen = false;
        SelectedSwapCard = null;
    }

    private void DeleteRoulette()
    {
        if (SelectedRouletteCard is null) return;
        _settings.AvatarRouletteProfiles.Remove(SelectedRouletteCard.Roulette);
        RouletteCards.Remove(SelectedRouletteCard);
        IsRouletteEditorOpen = false;
        SelectedRouletteCard = null;
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
            Name = "New Channel Point Swap"
        };
        SelectedSwapCard.Profile.ChannelPointRules.Add(rule);
        ChannelPointRows.Add(new InlineAvatarSwapRuleRowViewModel(rule));
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
        BitsRows.Add(new InlineAvatarSwapRuleRowViewModel(rule));
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
        SubsRows.Add(new InlineAvatarSwapRuleRowViewModel(rule));
    }

    private void AddPaymentRule()
    {
        if (SelectedSwapCard is null) return;
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarChange,
            Source = TriggerRuleSource.CashPayment,
            Name = "New Cash Payment Swap"
        };
        SelectedSwapCard.Profile.PaymentRules.Add(rule);
        PaymentRows.Add(new InlineAvatarSwapRuleRowViewModel(rule));
    }

    private void AddAdvancedTrigger(string? triggerSource)
    {
        if (SelectedSwapCard is null || string.IsNullOrEmpty(triggerSource)) return;
        if (!Enum.TryParse<TwitchTriggerType>(triggerSource, out var type)) return;
        var rule = new TriggerRule
        {
            TriggerType = type,
            ActionType = OscActionType.AvatarChange,
            Name = $"New {type} Swap"
        };
        SelectedSwapCard.Profile.ChannelPointRules.Add(rule);
        ChannelPointRows.Add(new InlineAvatarSwapRuleRowViewModel(rule));
    }

    private void DeleteRule(InlineAvatarSwapRuleRowViewModel? row)
    {
        if (row is null || SelectedSwapCard is null) return;
        if (SelectedSwapCard.Profile.ChannelPointRules.Remove(row.Rule)) ChannelPointRows.Remove(row);
        else if (SelectedSwapCard.Profile.BitsRules.Remove(row.Rule)) BitsRows.Remove(row);
        else if (SelectedSwapCard.Profile.SubsRules.Remove(row.Rule)) SubsRows.Remove(row);
        else if (SelectedSwapCard.Profile.PaymentRules.Remove(row.Rule)) PaymentRows.Remove(row);
        else if (SelectedRouletteCard is not null && SelectedRouletteCard.Roulette.Triggers.Remove(row.Rule)) RouletteTriggerRows.Remove(row);

        if (ReferenceEquals(EditingRule, row))
        {
            EditingRule = null;
        }
    }

    private void BeginInlineEdit(InlineAvatarSwapRuleRowViewModel? row)
    {
        if (row is null) return;
        foreach (var r in ChannelPointRows) r.IsExpanded = false;
        foreach (var r in BitsRows) r.IsExpanded = false;
        foreach (var r in SubsRows) r.IsExpanded = false;
        foreach (var r in PaymentRows) r.IsExpanded = false;
        foreach (var r in RouletteTriggerRows) r.IsExpanded = false;
        row.IsExpanded = true;
        EditingRule = row;
    }

    private void CommitInlineEdit()
    {
        if (EditingRule is not null)
        {
            EditingRule.IsExpanded = false;
            EditingRule.UpdateSummary();
        }
        EditingRule = null;
    }

    private void CancelInlineEdit()
    {
        if (EditingRule is not null)
        {
            EditingRule.IsExpanded = false;
        }
        EditingRule = null;
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
    }

    public void SetGlobalReturnAvatar(string? id, string? name)
    {
        _settings.MasterAvatarSwapReturnId = id;
        _settings.MasterAvatarSwapReturnName = name;
        RaisePropertyChanged(nameof(GlobalReturnAvatarId));
        RaisePropertyChanged(nameof(GlobalReturnAvatarName));
        RaisePropertyChanged(nameof(GlobalReturnAvatarDisplayName));
        RaisePropertyChanged(nameof(HasGlobalReturnAvatar));
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
                RouletteCards.Add(new AvatarRouletteCardViewModel(item));
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
