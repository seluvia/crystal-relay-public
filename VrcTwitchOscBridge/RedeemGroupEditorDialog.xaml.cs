using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class RedeemGroupEditorDialog : Window
{
    private readonly MainWindowViewModel viewModel;
    private readonly RedeemGroup? editingGroup;

    public ObservableCollection<RuleCheckItem> AvailableRules { get; } = [];

    public RedeemGroupEditorDialog(AppTheme theme, MainWindowViewModel viewModel, RedeemGroup? group)
    {
        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, theme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;
        this.viewModel = viewModel;
        editingGroup = group;

        var existingIds = group?.AssignedRuleIds ?? [];
        foreach (var rule in viewModel.GetAllManagedRulesList())
        {
            AvailableRules.Add(new RuleCheckItem(rule.Id, rule.DisplayTitle, existingIds.Contains(rule.Id)));
        }

        if (group is not null)
        {
            GroupNameTextBox.Text = group.Name;
            var cmd = group.CommandText;
            if (cmd.StartsWith('!'))
            {
                cmd = cmd[1..];
            }

            CommandTextTextBox.Text = cmd;
        }

        DataContext = this;
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        var name = GroupNameTextBox.Text.Trim();
        var command = CommandTextTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        var assignedIds = AvailableRules
            .Where(r => r.IsInGroup)
            .Select(r => r.Id)
            .ToList();

        var updated = new RedeemGroup
        {
            Name = name,
            CommandText = "!" + command,
            AssignedRuleIds = new ObservableCollection<Guid>(assignedIds)
        };

        if (editingGroup is not null)
        {
            viewModel.UpdateRedeemGroup(editingGroup, updated);
        }
        else
        {
            viewModel.AddRedeemGroup(updated);
        }

        DialogResult = true;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        Closed -= OnWindowClosed;
    }

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
    }
}

public sealed class RuleCheckItem : INotifyPropertyChanged
{
    private bool isInGroup;

    public RuleCheckItem(Guid id, string displayTitle, bool isInGroup)
    {
        Id = id;
        DisplayTitle = displayTitle;
        this.isInGroup = isInGroup;
    }

    public Guid Id { get; }

    public string DisplayTitle { get; }

    public bool IsInGroup
    {
        get => isInGroup;
        set
        {
            if (isInGroup != value)
            {
                isInGroup = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInGroup)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
