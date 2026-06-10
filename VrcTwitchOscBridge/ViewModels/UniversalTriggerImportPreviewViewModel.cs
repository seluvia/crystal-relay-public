using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class UniversalTriggerImportPreviewViewModel : ObservableObject
{
    private int currentStep = 1;
    public int CurrentStep { get => currentStep; set => SetProperty(ref currentStep, Math.Clamp(value, 1, 3)); }

    private string? filePath;
    public string? FilePath { get => filePath; set => SetProperty(ref filePath, value); }

    private string? fileName;
    public string? FileName { get => fileName; set => SetProperty(ref fileName, value); }

    private long fileSize;
    public long FileSize { get => fileSize; set => SetProperty(ref fileSize, value); }

    private FoomaInteractionImportResult? parsedResult;
    public FoomaInteractionImportResult? ParsedResult
    {
        get => parsedResult;
        set
        {
            if (SetProperty(ref parsedResult, value))
            {
                RaisePropertyChanged(nameof(HasParsedResult));
                RaisePropertyChanged(nameof(HasDirectOscWarning));
                RaisePropertyChanged(nameof(DirectOscWarningCommandName));
                RaisePropertyChanged(nameof(CommandCount));
                RaisePropertyChanged(nameof(RewardCount));
                RaisePropertyChanged(nameof(BitsCount));
                RaisePropertyChanged(nameof(SubscriptionCount));
                RaisePropertyChanged(nameof(FollowCount));
                RaisePropertyChanged(nameof(FusedCount));
            }
        }
    }

    public bool HasParsedResult => ParsedResult is not null;
    public bool HasDirectOscWarning =>
        ParsedResult is not null && ParsedResult.Triggers.Any(IsAllDirectOsc);
    public string DirectOscWarningCommandName =>
        ParsedResult?.Triggers.FirstOrDefault(IsAllDirectOsc)?.Name ?? "?";

    public int CommandCount => ParsedResult?.Triggers.Count(t => t.TriggerType == UniversalTriggerType.ChatCommand) ?? 0;
    public int RewardCount => ParsedResult?.Triggers.Count(t => t.TriggerType == UniversalTriggerType.ChannelPointReward) ?? 0;
    public int BitsCount => ParsedResult?.Triggers.Count(t => t.TriggerType == UniversalTriggerType.Bits) ?? 0;
    public int SubscriptionCount => ParsedResult?.Triggers.Count(t => t.TriggerType == UniversalTriggerType.Subscription) ?? 0;
    public int FollowCount => ParsedResult?.Triggers.Count(t => t.TriggerType == UniversalTriggerType.Follow) ?? 0;
    public int FusedCount => ParsedResult?.FusedCommandCount ?? 0;

    public AsyncRelayCommand PickFileCommand { get; }
    public AsyncRelayCommand BackCommand { get; }
    public AsyncRelayCommand ImportCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }

    public event Action? CancelRequested;
    public event Action<FoomaInteractionImportResult>? ImportRequested;

    public UniversalTriggerImportPreviewViewModel()
    {
        PickFileCommand = new AsyncRelayCommand(async () => await PickFileAsync());
        BackCommand = new AsyncRelayCommand(async () => { CurrentStep--; await Task.CompletedTask; });
        ImportCommand = new AsyncRelayCommand(async () => { if (ParsedResult is not null) ImportRequested?.Invoke(ParsedResult); await Task.CompletedTask; });
        CancelCommand = new AsyncRelayCommand(async () => { CancelRequested?.Invoke(); await Task.CompletedTask; });
    }

    private async Task PickFileAsync()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Fooma Config (*.json)|*.json|All files (*.*)|*.*",
            Title = "Pick a Fooma Twitch Interaction JSON"
        };
        if (dlg.ShowDialog() == true)
        {
            FilePath = dlg.FileName;
            FileName = Path.GetFileName(FilePath);
            try
            {
                FileSize = new FileInfo(FilePath).Length;
                ParsedResult = await FoomaInteractionConfigImporter.ImportAsync(FilePath);
                CurrentStep = 2;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fooma Import Failed: {ex.Message}", "Fooma Import", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        await Task.CompletedTask;
    }

    private static bool IsAllDirectOsc(UniversalTriggerRule t) =>
        t.Actions.Count > 0
        && t.Actions.All(a => !a.OscAddress.StartsWith("/avatar/parameters/") && !a.OscAddress.StartsWith("avatar/parameters/"));
}