using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VrcTwitchOscBridge.Services;

public enum PhaseStatus
{
    Pending,
    Active,
    Completed,
    Failed
}

public class LoadingPhase : INotifyPropertyChanged
{
    private PhaseStatus status;

    public string Key { get; }
    public string Label { get; }

    public PhaseStatus Status
    {
        get => status;
        set
        {
            if (status == value) return;
            status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusTag));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(ShowActiveIndicator));
            OnPropertyChanged(nameof(ShowCheckmark));
            OnPropertyChanged(nameof(RowOpacity));
        }
    }

    public LoadingPhase(string key, string label)
    {
        Key = key;
        Label = label;
        status = PhaseStatus.Pending;
    }

    public string StatusTag => Status switch
    {
        PhaseStatus.Pending => "[--]",
        PhaseStatus.Active => "[--]",
        PhaseStatus.Completed => "[OK]",
        PhaseStatus.Failed => "[!!]",
        _ => "[--]"
    };

    public bool IsActive => Status == PhaseStatus.Active;
    public bool IsCompleted => Status == PhaseStatus.Completed;
    public bool ShowActiveIndicator => IsActive;
    public bool ShowCheckmark => IsCompleted;
    public double RowOpacity => Status == PhaseStatus.Pending ? 0.35 : 1.0;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class LoadingPhaseService : INotifyPropertyChanged
{
    public ObservableCollection<LoadingPhase> Phases { get; } = [];

    private bool allComplete;

    public bool AllComplete
    {
        get => allComplete;
        set
        {
            if (allComplete == value) return;
            allComplete = value;
            OnPropertyChanged();
        }
    }

    public void DefinePhases(params (string key, string label)[] phases)
    {
        Phases.Clear();
        foreach (var (key, label) in phases)
        {
            Phases.Add(new LoadingPhase(key, label));
        }
    }

    public void ReportProgress(string key, PhaseStatus newStatus)
    {
        foreach (var phase in Phases)
        {
            if (phase.Key == key)
            {
                phase.Status = newStatus;
                return;
            }
        }
    }

    public void CompleteAll()
    {
        foreach (var phase in Phases)
        {
            if (phase.Status != PhaseStatus.Completed)
            {
                phase.Status = PhaseStatus.Completed;
            }
        }
        AllComplete = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
