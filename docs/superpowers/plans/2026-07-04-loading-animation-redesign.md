# Loading Animation Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Crystal Relay's decorative looping loading overlay with a HUD Scanner sci-fi/holographic loading screen that shows real progress through 5 initialization phases with a smooth reveal transition.

**Architecture:** A lightweight `LoadingPhaseService` owned by the ViewModel reports phase progress. The code-behind subscribes and drives the HUD Scanner UI. XAML storyboards handle the holographic crystal, scanning line, and phase pulse animations. A 3-step reveal transition (sign-off → hold → fade-out) replaces the current instant-hide.

**Tech Stack:** WPF, XAML Storyboards, C#, MVVM

---

### Task 1: Create LoadingPhaseService

**Files:**
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Services\LoadingPhaseService.cs`

- [ ] **Step 1: Create LoadingPhaseService with model and service**

```csharp
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
```

- [ ] **Step 2: Create the directory if needed**

```bash
Test-Path -LiteralPath "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Services"
```

### Task 2: Wire LoadingPhaseService into MainWindowViewModel

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`

- [ ] **Step 1: Add LoadingService property to MainWindowViewModel**

Add a public property to the ViewModel (near the top of the class, with other public properties):

```csharp
public LoadingPhaseService LoadingService { get; } = new();
```

- [ ] **Step 2: Define phases in the ViewModel constructor**

In the ViewModel constructor, after existing initialization, define the phases:

```csharp
LoadingService.DefinePhases(
    ("settings", "Loading Settings"),
    ("vrchat", "Connecting to VRChat"),
    ("twitch", "Syncing Twitch Rewards"),
    ("bridge", "Starting OSC Bridge"),
    ("finalizing", "Finalizing")
);
```

- [ ] **Step 3: Report progress at phase boundaries in InitializeAsync()**

Modify `InitializeAsync()` to report progress at key points. Add these calls at the appropriate locations:

After settings load (after `await settingsStore.LoadAsync()` line 3303):
```csharp
LoadingService.ReportProgress("settings", PhaseStatus.Completed);
LoadingService.ReportProgress("vrchat", PhaseStatus.Active);
```

Before `InitializeVrChatAsync()` at line 3383, but after the preceding await(s) complete:
```csharp
LoadingService.ReportProgress("vrchat", PhaseStatus.Completed);
LoadingService.ReportProgress("twitch", PhaseStatus.Active);
```

Before `QueueRewardRefreshAsync()` at line 3384:
```csharp
LoadingService.ReportProgress("twitch", PhaseStatus.Completed);
LoadingService.ReportProgress("bridge", PhaseStatus.Active);
```

Before `QueueBridgeRefresh()` at line 3387:
```csharp
LoadingService.ReportProgress("bridge", PhaseStatus.Completed);
LoadingService.ReportProgress("finalizing", PhaseStatus.Active);
```

At the end of `InitializeAsync()`, before the method returns, add:
```csharp
LoadingService.CompleteAll();
```

### Task 3: Update MainWindow.xaml.cs for New Animation System

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\MainWindow.xaml.cs`

- [ ] **Step 1: Replace LoadingStoryboardKeys**

Change the array at line 45-53 from the old 6 keys to the new 5 keys:

```csharp
private static readonly string[] LoadingStoryboardKeys =
[
    "HologramIdleStoryboard",
    "ScanLineStoryboard",
    "PhasePulseStoryboard",
    "HudEntranceStoryboard"
];
```

- [ ] **Step 2: Update OnLoaded to drive the reveal transition**

Replace the OnLoaded method at lines 134-148 with:

```csharp
private async void OnLoaded(object sender, RoutedEventArgs e)
{
    LoadingOverlay.Visibility = Visibility.Visible;
    StartLoadingAnimations();
    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

    await viewModel.InitializeAsync();
    ApplyTheme(viewModel.SelectedTheme);

    // Reveal transition
    await RunRevealTransitionAsync();

    StopLoadingAnimations();
    LoadingOverlay.Visibility = Visibility.Collapsed;
    RestoreRestartSessionWindows();
    QueueApplicationUpdateCheck();
    ShowAvatarSwapMigrationNoticeIfNeeded();
    _ = viewModel.CheckForPendingCrashReportAsync();
}
```

- [ ] **Step 3: Add RunRevealTransitionAsync method**

Add this method to MainWindow.xaml.cs after the StopLoadingAnimations method:

```csharp
private async Task RunRevealTransitionAsync()
{
    // Step 1: Wait for the final "All systems operational" to show
    await Task.Delay(700);

    // Step 2: Fade out overlay
    if (TryGetLoadingStoryboard("RevealTransitionStoryboard", out var revealStoryboard))
    {
        var tcs = new TaskCompletionSource();
        revealStoryboard.Completed += (_, _) => tcs.TrySetResult();
        revealStoryboard.Begin(this);
        await tcs.Task;
    }
}
```

- [ ] **Step 4: Add the using directive**

Ensure the file has `using VrcTwitchOscBridge.Services;` (add near top with other usings).

### Task 4: Replace LoadingOverlay XAML with HUD Scanner

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\MainWindow.xaml`

- [ ] **Step 1: Replace LoadingOverlay content**

Replace lines 7230-7439 (the entire LoadingOverlay Grid content including its children and resources) with the new HUD Scanner layout:

```xml
<Grid x:Name="LoadingOverlay"
      Grid.RowSpan="2"
      Panel.ZIndex="999"
      Background="{DynamicResource WindowBackgroundBrush}">
    <Grid.Resources>
        <!-- Crystal hologram animation: slow rotation + gentle pulse + glow breathing -->
        <Storyboard x:Key="HologramIdleStoryboard">
            <DoubleAnimation Storyboard.TargetName="HologramRotateTransform"
                             Storyboard.TargetProperty="Angle"
                             From="0" To="360" Duration="0:0:8"
                             RepeatBehavior="Forever" />
            <DoubleAnimationUsingKeyFrames Storyboard.TargetName="HologramScaleTransform"
                                           Storyboard.TargetProperty="ScaleX"
                                           RepeatBehavior="Forever">
                <LinearDoubleKeyFrame KeyTime="0:0:0" Value="1" />
                <LinearDoubleKeyFrame KeyTime="0:0:2" Value="1.05" />
                <LinearDoubleKeyFrame KeyTime="0:0:4" Value="1" />
            </DoubleAnimationUsingKeyFrames>
            <DoubleAnimationUsingKeyFrames Storyboard.TargetName="HologramScaleTransform"
                                           Storyboard.TargetProperty="ScaleY"
                                           RepeatBehavior="Forever">
                <LinearDoubleKeyFrame KeyTime="0:0:0" Value="1" />
                <LinearDoubleKeyFrame KeyTime="0:0:2" Value="1.05" />
                <LinearDoubleKeyFrame KeyTime="0:0:4" Value="1" />
            </DoubleAnimationUsingKeyFrames>
            <DoubleAnimationUsingKeyFrames Storyboard.TargetName="HologramGlowEllipse"
                                           Storyboard.TargetProperty="Opacity"
                                           RepeatBehavior="Forever">
                <LinearDoubleKeyFrame KeyTime="0:0:0" Value="0.25" />
                <LinearDoubleKeyFrame KeyTime="0:0:2" Value="0.55" />
                <LinearDoubleKeyFrame KeyTime="0:0:4" Value="0.25" />
            </DoubleAnimationUsingKeyFrames>
        </Storyboard>

        <!-- Scanning line sweeping top to bottom -->
        <Storyboard x:Key="ScanLineStoryboard">
            <DoubleAnimation Storyboard.TargetName="ScanLineTranslateTransform"
                             Storyboard.TargetProperty="Y"
                             From="-400" To="600" Duration="0:0:3"
                             RepeatBehavior="Forever" />
        </Storyboard>

        <!-- Phase pulse: animates the loading overlay accent (no DataTemplate naming conflict) -->
        <Storyboard x:Key="PhasePulseStoryboard">
            <DoubleAnimationUsingKeyFrames Storyboard.TargetName="PhasePulseGlow"
                                           Storyboard.TargetProperty="Opacity"
                                           RepeatBehavior="Forever">
                <LinearDoubleKeyFrame KeyTime="0:0:0" Value="0.3" />
                <LinearDoubleKeyFrame KeyTime="0:0:0.8" Value="0.7" />
                <LinearDoubleKeyFrame KeyTime="0:0:1.6" Value="0.3" />
            </DoubleAnimationUsingKeyFrames>
        </Storyboard>

        <!-- HUD entrance: power-on flicker + header slide-down + HUD panel slide-up -->
        <Storyboard x:Key="HudEntranceStoryboard">
            <DoubleAnimationUsingKeyFrames Storyboard.TargetName="CrystalHeaderText"
                                           Storyboard.TargetProperty="Opacity">
                <DiscreteDoubleKeyFrame KeyTime="0:0:0" Value="0" />
                <DiscreteDoubleKeyFrame KeyTime="0:0:0.05" Value="0.4" />
                <DiscreteDoubleKeyFrame KeyTime="0:0:0.1" Value="0" />
                <DiscreteDoubleKeyFrame KeyTime="0:0:0.15" Value="0.8" />
                <DiscreteDoubleKeyFrame KeyTime="0:0:0.2" Value="0" />
                <DiscreteDoubleKeyFrame KeyTime="0:0:0.35" Value="0.6" />
                <DiscreteDoubleKeyFrame KeyTime="0:0:0.5" Value="1" />
            </DoubleAnimationUsingKeyFrames>
            <DoubleAnimation Storyboard.TargetName="CrystalHeaderTransform"
                             Storyboard.TargetProperty="Y"
                             From="-60" To="-120" Duration="0:0:0.5"
                             BeginTime="0:0:0.3" />
            <DoubleAnimation Storyboard.TargetName="HudPanelBorder"
                             Storyboard.TargetProperty="Opacity"
                             From="0" To="1" Duration="0:0:0.6"
                             BeginTime="0:0:0.3" />
            <ThicknessAnimation Storyboard.TargetName="HudPanelBorder"
                                Storyboard.TargetProperty="Margin"
                                From="0,40,0,0" To="0,160,0,0"
                                Duration="0:0:0.6"
                                BeginTime="0:0:0.3" />
        </Storyboard>

        <!-- Reveal transition: fade out overlay -->
        <Storyboard x:Key="RevealTransitionStoryboard">
            <DoubleAnimation Storyboard.TargetName="HologramGlowEllipse"
                             Storyboard.TargetProperty="Opacity"
                             To="0" Duration="0:0:0.3" />
            <DoubleAnimation Storyboard.TargetName="LoadingOverlay"
                             Storyboard.TargetProperty="Opacity"
                             From="1" To="0" Duration="0:0:0.5"
                             BeginTime="0:0:0.3" />
        </Storyboard>
    </Grid.Resources>

    <!-- Subtle overlay pulse glow for phase transitions -->
    <Rectangle x:Name="PhasePulseGlow"
               Grid.RowSpan="2"
               Fill="{DynamicResource AccentBrush}"
               Opacity="0"
               IsHitTestVisible="False" />

    <!-- Thin scan line bar at the very top -->
    <Rectangle Height="1"
               VerticalAlignment="Top"
               Fill="{DynamicResource AccentBrush}"
               Opacity="0.3"
               Margin="40,0,40,0" />

    <!-- CRYSTAL RELAY header text -->
    <TextBlock x:Name="CrystalHeaderText"
               Text="CRYSTAL RELAY"
               FontFamily="Consolas"
               FontSize="13"
               FontWeight="SemiBold"
               Foreground="{DynamicResource AccentBrush}"
               Opacity="0.45"
               LetterSpacing="6"
               HorizontalAlignment="Center"
               VerticalAlignment="Center"
               Margin="0,36,0,0"
                RenderTransformOrigin="0.5,0.5">
        <TextBlock.RenderTransform>
            <TranslateTransform x:Name="CrystalHeaderTransform" Y="-120" />
        </TextBlock.RenderTransform>
    </TextBlock>

    <!-- Holographic crystal projection area -->
    <Grid Width="106" Height="106"
          HorizontalAlignment="Center"
          VerticalAlignment="Center"
          RenderTransformOrigin="0.5,0.5"
          Margin="0,0,0,60">
        <Grid.RenderTransform>
            <TransformGroup>
                <RotateTransform x:Name="HologramRotateTransform" Angle="0" />
                <ScaleTransform x:Name="HologramScaleTransform" ScaleX="1" ScaleY="1" />
            </TransformGroup>
        </Grid.RenderTransform>

        <!-- Glow behind crystal -->
        <Ellipse x:Name="HologramGlowEllipse"
                 Width="140" Height="140"
                 Fill="{DynamicResource AccentBrush}"
                 Opacity="0.35"
                 HorizontalAlignment="Center"
                 VerticalAlignment="Center">
            <Ellipse.Effect>
                <BlurEffect Radius="40" />
            </Ellipse.Effect>
        </Ellipse>

        <!-- Crystal icon -->
        <Image Source="Assets/crystal-relay-icon.png"
               Width="80" Height="80"
               Stretch="Uniform"
               HorizontalAlignment="Center"
               VerticalAlignment="Center"
               Opacity="0.85" />
    </Grid>

    <!-- Scanning line sweep -->
    <Rectangle x:Name="ScanLineRectangle"
               Width="200" Height="1"
               Fill="{DynamicResource AccentBrush}"
               HorizontalAlignment="Center"
               VerticalAlignment="Top"
               Opacity="0.5"
               IsHitTestVisible="False"
               RenderTransformOrigin="0.5,0.5">
        <Rectangle.RenderTransform>
            <TranslateTransform x:Name="ScanLineTranslateTransform" Y="-400" />
        </Rectangle.RenderTransform>
        <Rectangle.Effect>
            <BlurEffect Radius="3" />
        </Rectangle.Effect>
    </Rectangle>

    <!-- HUD Status Panel -->
    <Border x:Name="HudPanelBorder"
            HorizontalAlignment="Center"
            VerticalAlignment="Center"
            Margin="0,160,0,0"
            Opacity="0"
            BorderThickness="1"
            BorderBrush="{DynamicResource AccentBrush}"
            Background="#0A000000"
            CornerRadius="4"
            MinWidth="320">
        <StackPanel Margin="16,12,16,12">
            <TextBlock Text="INITIALIZATION SEQUENCE"
                       FontFamily="Consolas"
                       FontSize="10"
                       Foreground="{DynamicResource AccentBrush}"
                       Opacity="0.4"
                       Margin="0,0,0,10" />

            <ItemsControl ItemsSource="{Binding LoadingService.Phases}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Grid Margin="0,0,0,4">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="36" />
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>

                            <!-- Status tag -->
                            <TextBlock Text="{Binding StatusTag}"
                                       FontFamily="Consolas"
                                       FontSize="12"
                                       Foreground="{DynamicResource AccentBrush}"
                                       Opacity="{Binding RowOpacity}"
                                       VerticalAlignment="Center" />

                            <!-- Active pulsing glow dot -->
                            <Ellipse Grid.Column="1"
                                     Width="6" Height="6"
                                     Fill="{DynamicResource AccentBrush}"
                                     HorizontalAlignment="Center"
                                     VerticalAlignment="Center"
                                     Margin="0,0,8,0"
                                     Opacity="0.7"
                                     Visibility="{Binding ShowActiveIndicator, Converter={StaticResource BooleanToVisibilityConverter}}">
                                <Ellipse.Effect>
                                    <BlurEffect Radius="3" />
                                </Ellipse.Effect>
                            </Ellipse>

                            <!-- Completed checkmark -->
                            <TextBlock Grid.Column="1"
                                       Text="✓"
                                       FontSize="12"
                                       Foreground="{DynamicResource AccentBrush}"
                                       VerticalAlignment="Center"
                                       Margin="0,0,8,0"
                                       Visibility="{Binding ShowCheckmark, Converter={StaticResource BooleanToVisibilityConverter}}" />

                            <!-- Phase label -->
                            <TextBlock Grid.Column="2"
                                       Text="{Binding Label}"
                                       FontFamily="Consolas"
                                       FontSize="12"
                                       Foreground="{DynamicResource AccentBrush}"
                                       Opacity="{Binding RowOpacity}"
                                       VerticalAlignment="Center" />
                        </Grid>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
    </Border>

    <!-- Sign-off message (visible during reveal transition) -->
    <TextBlock x:Name="SignOffMessage"
               Text="All systems operational."
               FontFamily="Consolas"
               FontSize="11"
               Foreground="{DynamicResource AccentBrush}"
               Opacity="0"
               HorizontalAlignment="Center"
               VerticalAlignment="Center"
               Margin="0,240,0,0" />

    <!-- Bottom-right version info -->
    <TextBlock x:Name="VersionInfoText"
               FontFamily="Consolas"
               FontSize="10"
               Foreground="{DynamicResource AccentBrush}"
               Opacity="0.2"
               HorizontalAlignment="Right"
               VerticalAlignment="Bottom"
               Margin="0,0,16,12" />
</Grid>
```

- [ ] **Step 2: Add BooleanToVisibilityConverter resource**

In MainWindow.xaml, add a BooleanToVisibilityConverter to the Window.Resources (or the LoadingOverlay.Resources) if one doesn't already exist:

```xml
<BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />
```

- [ ] **Step 3: Update loading text in the code-behind**

In `OnLoaded`, set the version info text:

```csharp
// In OnLoaded, before StartLoadingAnimations():
var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
if (version != null)
{
    VersionInfoText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
}
```

### Task 5: Test the Build

**Files:** (no new files)

- [ ] **Step 1: Build to verify compilation**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

- [ ] **Step 2: Fix any build errors**

Address compilation errors (likely: missing using directives, converter setup, binding path issues).

### Task 6: Verify Runtime Behavior

- [ ] **Step 1: Launch in debug mode**

```bash
powershell -ExecutionPolicy Bypass -File "E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"
```

- [ ] **Step 2: Verify visually**
  - Loading overlay appears with HUD Scanner layout
  - Holographic crystal rotates and pulses
  - Scanning line sweeps across
  - Phases progress through from [--] to [OK]
  - Active phase shows pulsing indicator
  - "All systems operational" message appears briefly
  - Overlay fades out smoothly
  - Main content is revealed

### Task 7: Commit

- [ ] **Step 1: Stage and commit**

```bash
git add "VrcTwitchOscBridge/Services/LoadingPhaseService.cs"
git add "VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs"
git add "VrcTwitchOscBridge/MainWindow.xaml"
git add "VrcTwitchOscBridge/MainWindow.xaml.cs"
git add "docs/superpowers/specs/2026-07-04-loading-animation-redesign.md"
git commit -m "feat: replace loading overlay with HUD Scanner animation system"
```
