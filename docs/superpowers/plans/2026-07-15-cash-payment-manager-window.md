# Cash Payment Manager Window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the inline Cash Payment tab in MainWindow with a dedicated `CashPaymentManagerWindow` for connection credentials, plus a one-time migration notice for 3.1.8→3.1.9 upgrades.

**Architecture:** New standalone window + ViewModel for connection config. Inline Cash Payment UI removed from MainWindow. Avatar Scaling Manager's Cash Payments source tab stays untouched. `AppSettings.CashPaymentRules` collection is preserved.

**Tech Stack:** C#, WPF/XAML, .NET 10

## Global Constraints

- New window must match `AvatarScalingManagerWindow` purple theme palette and custom chrome exactly
- `AppSettings.CashPaymentRules` collection must NOT be removed — still used by Avatar Scaling Manager
- `AddAvatarScalingCashPaymentRuleCommand` and `DeleteCashPaymentRuleByCard()` must stay on MainWindowVM
- Migration notice follows the exact `AvatarSwapMigrationNoticeShown` pattern
- Must build without errors: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

---

### Task 1: Migration Notice Flag (AppSettings + SettingsStore)

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Models\AppSettings.cs`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Services\SettingsStore.cs`

**Interfaces:**
- Consumes: `AppSettings.cs` pattern at line 523, `SettingsStore.cs` patterns at lines 418, 601, 2828
- Produces: `CashPaymentMigrationNoticeShown` bool on `AppSettings`, persisted through `SettingsStore`

- [ ] **Step 1: Add property to AppSettings.cs**

After line 523 (`public bool AvatarSwapMigrationNoticeShown { get; set; }`), add:

```csharp
    public bool CashPaymentMigrationNoticeShown { get; set; }
```

- [ ] **Step 2: Add load line to SettingsStore.cs**

After line 418 (`settings.AvatarSwapMigrationNoticeShown = profile.AvatarSwapMigrationNoticeShown ?? settings.AvatarSwapMigrationNoticeShown;`), add:

```csharp
            settings.CashPaymentMigrationNoticeShown = profile.CashPaymentMigrationNoticeShown ?? settings.CashPaymentMigrationNoticeShown;
```

- [ ] **Step 3: Add save line to SettingsStore.cs**

After line 601 (`AvatarSwapMigrationNoticeShown = settings.AvatarSwapMigrationNoticeShown,`), add:

```csharp
            CashPaymentMigrationNoticeShown = settings.CashPaymentMigrationNoticeShown,
```

- [ ] **Step 4: Add DTO property to SettingsStore.cs**

After line 2829 (`public bool? AvatarSwapMigrationNoticeShown { get; set; }`), add:

```csharp
        [JsonPropertyName("cashPaymentMigrationNoticeShown")]
        public bool? CashPaymentMigrationNoticeShown { get; set; }
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

---

### Task 2: CashPaymentManagerViewModel

**Files:**
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\CashPaymentManagerViewModel.cs`

**Interfaces:**
- Consumes: `AppSettings settings`, `MainWindowViewModel mainWindow`
- Produces: `CashPaymentManagerViewModel` class with `CashPayments` bindings, `OpenKoFiWebhooksCommand`, `RegenerateKoFiRelayIdentityCommand` (proxied from MainWindowVM)

- [ ] **Step 1: Create the ViewModel**

Create `CashPaymentManagerViewModel.cs`:

```csharp
using System;
using System.ComponentModel;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class CashPaymentManagerViewModel : ObservableObject, IDisposable
{
    private readonly MainWindowViewModel? mainWindowViewModel;
    private bool disposed;

    public CashPaymentConnectionSettings CashPayments { get; }

    public CashPaymentManagerViewModel(AppSettings settings, MainWindowViewModel? mainWindowViewModel)
    {
        ArgumentNullException.ThrowIfNull(settings);
        CashPayments = settings.CashPayments;
        this.mainWindowViewModel = mainWindowViewModel;
        CashPayments.PropertyChanged += OnCashPaymentsPropertyChanged;
    }

    // Proxy commands through MainWindowViewModel — follows the same pattern
    // used by AvatarScalingManagerViewModel.AddAvatarScalingCashPaymentRuleCommand
    public System.Windows.Input.ICommand? OpenKoFiWebhooksCommand =>
        mainWindowViewModel?.OpenKoFiWebhooksCommand;

    public System.Windows.Input.ICommand? RegenerateKoFiRelayIdentityCommand =>
        mainWindowViewModel?.RegenerateKoFiRelayIdentityCommand;

    private void OnCashPaymentsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CashPayments.PropertyChanged -= OnCashPaymentsPropertyChanged;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

---

### Task 3: CashPaymentManagerWindow (XAML + Code-Behind)

**Files:**
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\CashPaymentManagerWindow.xaml`
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\CashPaymentManagerWindow.xaml.cs`

**Interfaces:**
- Consumes: `CashPaymentManagerViewModel`
- Produces: Standalone window showing connection settings for all three providers

- [ ] **Step 1: Create the XAML**

Create `CashPaymentManagerWindow.xaml`:

```xml
<Window x:Class="VrcTwitchOscBridge.CashPaymentManagerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:loc="clr-namespace:VrcTwitchOscBridge.Services"
        xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        xmlns:local="clr-namespace:VrcTwitchOscBridge"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d"
        d:DataContext="{d:DesignInstance Type=local:CashPaymentManagerWindow}"
        Title="{loc:Translate 'Cash Payment Connections'}"
        Icon="Assets/crystal-relay-icon.ico"
        Width="700"
        Height="600"
        MinWidth="540"
        MinHeight="480"
        WindowStyle="None"
        WindowStartupLocation="CenterOwner"
        FontFamily="{DynamicResource BodyFontFamily}"
        UseLayoutRounding="True"
        SnapsToDevicePixels="True"
        Background="{DynamicResource WindowBackgroundBrush}">
    <shell:WindowChrome.WindowChrome>
        <shell:WindowChrome CaptionHeight="0" CornerRadius="0" GlassFrameThickness="0" ResizeBorderThickness="6" UseAeroCaptionButtons="False" />
    </shell:WindowChrome.WindowChrome>

    <Window.Resources>
        <SolidColorBrush x:Key="WindowBackgroundBrush" Color="#130B1E" />
        <SolidColorBrush x:Key="PanelBrush" Color="#CC1C132B" />
        <SolidColorBrush x:Key="PanelSecondaryBrush" Color="#C8241739" />
        <SolidColorBrush x:Key="NestedPanelBrush" Color="#B8241739" />
        <SolidColorBrush x:Key="BorderBrush" Color="#4B2B78" />
        <SolidColorBrush x:Key="AccentBrush" Color="#A855F7" />
        <SolidColorBrush x:Key="AccentDimBrush" Color="#552D1B47" />
        <SolidColorBrush x:Key="TextBrush" Color="#F5EEFF" />
        <SolidColorBrush x:Key="MutedBrush" Color="#C9B8E3" />
        <SolidColorBrush x:Key="InputBorderBrush" Color="#5B3A8E" />
        <SolidColorBrush x:Key="InputBrush" Color="#241733" />
        <SolidColorBrush x:Key="SecondaryButtonBrush" Color="#2C1C48" />
        <SolidColorBrush x:Key="SecondaryButtonBorderBrush" Color="#6942A7" />
        <SolidColorBrush x:Key="TitleBarBrush" Color="#20122F" />
        <SolidColorBrush x:Key="TitleBarTextBrush" Color="#F5EEFF" />
        <SolidColorBrush x:Key="TitleBarSubTextBrush" Color="#CBB9E5" />
        <SolidColorBrush x:Key="TitleBarButtonBrush" Color="Transparent" />
        <SolidColorBrush x:Key="TitleBarButtonHoverBrush" Color="#3B235B" />
        <SolidColorBrush x:Key="TitleBarButtonPressedBrush" Color="#543183" />
        <SolidColorBrush x:Key="TitleBarCloseHoverBrush" Color="#B43D62" />
        <SolidColorBrush x:Key="TitleBarClosePressedBrush" Color="#8C2648" />
        <FontFamily x:Key="HeadingFontFamily">Constantia</FontFamily>
        <FontFamily x:Key="BodyFontFamily">Verdana</FontFamily>
    </Window.Resources>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <!-- Title Bar -->
        <Border Grid.Row="0"
                Height="38"
                Background="{DynamicResource TitleBarBrush}"
                MouseLeftButtonDown="OnTitleBarMouseDown">
            <Grid>
                <TextBlock Text="{loc:Translate 'Cash Payment Connections'}"
                           FontFamily="{DynamicResource HeadingFontFamily}"
                           FontSize="14"
                           Foreground="{DynamicResource TitleBarTextBrush}"
                           VerticalAlignment="Center"
                           HorizontalAlignment="Center"
                           Margin="40,0,40,0"
                           TextTrimming="CharacterEllipsis" />
                <StackPanel Orientation="Horizontal"
                            HorizontalAlignment="Right"
                            VerticalAlignment="Center">
                    <Button x:Name="MinimizeButton"
                            Width="38" Height="38"
                            Background="{DynamicResource TitleBarButtonBrush}"
                            BorderThickness="0"
                            Click="OnMinimizeClicked">
                        <TextBlock Text="&#xE921;"
                                   FontFamily="Segoe MDL2 Assets"
                                   FontSize="10"
                                   Foreground="{DynamicResource TitleBarTextBrush}"
                                   HorizontalAlignment="Center"
                                   VerticalAlignment="Center" />
                    </Button>
                    <Button x:Name="CloseButton"
                            Width="38" Height="38"
                            Background="{DynamicResource TitleBarButtonBrush}"
                            BorderThickness="0"
                            Click="OnCloseClicked">
                        <TextBlock Text="&#xE8BB;"
                                   FontFamily="Segoe MDL2 Assets"
                                   FontSize="10"
                                   Foreground="{DynamicResource TitleBarTextBrush}"
                                   HorizontalAlignment="Center"
                                   VerticalAlignment="Center" />
                    </Button>
                </StackPanel>
            </Grid>
        </Border>

        <!-- Content -->
        <ScrollViewer Grid.Row="1"
                      Margin="20,16,20,20"
                      VerticalScrollBarVisibility="Auto"
                      HorizontalScrollBarVisibility="Disabled">
            <StackPanel>
                <!-- Help text -->
                <Border Padding="14"
                        Background="{DynamicResource NestedPanelBrush}"
                        BorderBrush="{DynamicResource BorderBrush}"
                        BorderThickness="1"
                        CornerRadius="14"
                        Margin="0,0,0,16">
                    <TextBlock Text="{loc:Translate 'Cash Payments listen for StreamElements tips, Streamlabs donations, and Ko-fi payments. Ko-fi uses the Crystal Relay hosted relay by default, with a local webhook fallback for advanced setups. Tokens, client secrets, and webhook verification secrets are stored in Windows Credential Manager.'}"
                               Foreground="{DynamicResource MutedBrush}"
                               TextWrapping="Wrap" />
                </Border>

                <!-- StreamElements Card -->
                <Border Padding="18"
                        Background="{DynamicResource NestedPanelBrush}"
                        BorderBrush="{DynamicResource BorderBrush}"
                        BorderThickness="1"
                        CornerRadius="18"
                        Margin="0,0,0,16">
                    <StackPanel>
                        <TextBlock Text="StreamElements"
                                   FontFamily="{DynamicResource HeadingFontFamily}"
                                   FontSize="17"
                                   FontWeight="SemiBold"
                                   Foreground="{DynamicResource TextBrush}" />
                        <CheckBox Margin="0,10,0,0"
                                  Content="{loc:Translate 'Enable StreamElements'}"
                                  IsChecked="{Binding CashPayments.StreamElementsEnabled, UpdateSourceTrigger=PropertyChanged}" />
                        <TextBlock Margin="0,8,0,0"
                                   Text="{loc:Translate 'StreamElements account / room ID'}"
                                   Foreground="{DynamicResource TextBrush}"
                                   FontWeight="SemiBold" />
                        <TextBox Text="{Binding CashPayments.StreamElementsAccountId, UpdateSourceTrigger=PropertyChanged}" />
                        <TextBlock Margin="0,8,0,0"
                                   Text="{loc:Translate 'StreamElements JWT token'}"
                                   Foreground="{DynamicResource TextBrush}"
                                   FontWeight="SemiBold" />
                        <TextBox Text="{Binding CashPayments.StreamElementsJwtToken, UpdateSourceTrigger=PropertyChanged}" />
                    </StackPanel>
                </Border>

                <!-- Streamlabs Card -->
                <Border Padding="18"
                        Background="{DynamicResource NestedPanelBrush}"
                        BorderBrush="{DynamicResource BorderBrush}"
                        BorderThickness="1"
                        CornerRadius="18"
                        Margin="0,0,0,16">
                    <StackPanel>
                        <TextBlock Text="Streamlabs"
                                   FontFamily="{DynamicResource HeadingFontFamily}"
                                   FontSize="17"
                                   FontWeight="SemiBold"
                                   Foreground="{DynamicResource TextBrush}" />
                        <CheckBox Margin="0,10,0,0"
                                  Content="{loc:Translate 'Enable Streamlabs'}"
                                  IsChecked="{Binding CashPayments.StreamlabsEnabled, UpdateSourceTrigger=PropertyChanged}" />
                        <TextBlock Margin="0,8,0,0"
                                   Text="{loc:Translate 'Streamlabs access token'}"
                                   Foreground="{DynamicResource TextBrush}"
                                   FontWeight="SemiBold" />
                        <TextBox Text="{Binding CashPayments.StreamlabsAccessToken, UpdateSourceTrigger=PropertyChanged}" />
                    </StackPanel>
                </Border>

                <!-- Ko-fi Card -->
                <Border Padding="18"
                        Background="{DynamicResource NestedPanelBrush}"
                        BorderBrush="{DynamicResource BorderBrush}"
                        BorderThickness="1"
                        CornerRadius="18">
                    <StackPanel>
                        <TextBlock Text="Ko-fi"
                                   FontFamily="{DynamicResource HeadingFontFamily}"
                                   FontSize="17"
                                   FontWeight="SemiBold"
                                   Foreground="{DynamicResource TextBrush}" />
                        <CheckBox Margin="0,10,0,0"
                                  Content="{loc:Translate 'Enable Ko-fi'}"
                                  IsChecked="{Binding CashPayments.KoFiEnabled, UpdateSourceTrigger=PropertyChanged}" />
                        <CheckBox Margin="0,8,0,0"
                                  Content="{loc:Translate 'Use Crystal Relay hosted Ko-fi relay'}"
                                  IsChecked="{Binding CashPayments.KoFiUseHostedRelay, UpdateSourceTrigger=PropertyChanged}" />

                        <!-- Hosted relay section -->
                        <StackPanel Margin="0,8,0,0">
                            <StackPanel.Style>
                                <Style TargetType="StackPanel">
                                    <Setter Property="Visibility" Value="Collapsed" />
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding CashPayments.KoFiUseHostedRelay}" Value="True">
                                            <Setter Property="Visibility" Value="Visible" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </StackPanel.Style>
                            <TextBlock Text="{loc:Translate 'Ko-fi webhook URL'}"
                                       Foreground="{DynamicResource TextBrush}"
                                       FontWeight="SemiBold" />
                            <TextBox Text="{Binding CashPayments.KoFiRelayWebhookUrl, Mode=OneWay}"
                                     IsReadOnly="True" />
                            <Button Margin="0,8,0,0"
                                    Style="{StaticResource SecondaryButtonStyle}"
                                    Content="{loc:Translate 'Regenerate Ko-fi Relay Link'}"
                                    Command="{Binding RegenerateKoFiRelayIdentityCommand}" />
                            <TextBlock Margin="0,8,0,0"
                                       Text="{loc:Translate 'Paste this webhook URL into Ko-fi. Crystal Relay connects outward to the hosted relay, so streamers do not need Cloudflare, ngrok, port forwarding, or a public local URL.'}"
                                       Foreground="{DynamicResource MutedBrush}"
                                       TextWrapping="Wrap" />
                            <Button Margin="0,8,0,0"
                                    Style="{StaticResource SecondaryButtonStyle}"
                                    Content="{loc:Translate 'Open Ko-fi Webhooks Page'}"
                                    Command="{Binding OpenKoFiWebhooksCommand}" />
                        </StackPanel>

                        <!-- Local webhook section -->
                        <StackPanel Margin="0,8,0,0">
                            <StackPanel.Style>
                                <Style TargetType="StackPanel">
                                    <Setter Property="Visibility" Value="Collapsed" />
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding CashPayments.KoFiUseLocalWebhook}" Value="True">
                                            <Setter Property="Visibility" Value="Visible" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </StackPanel.Style>
                            <UniformGrid Columns="2">
                                <StackPanel Margin="0,0,10,0">
                                    <TextBlock Text="{loc:Translate 'Local port'}"
                                               Foreground="{DynamicResource TextBrush}"
                                               FontWeight="SemiBold" />
                                    <TextBox Text="{Binding CashPayments.KoFiLocalPort, UpdateSourceTrigger=PropertyChanged}" />
                                </StackPanel>
                                <StackPanel Margin="10,0,0,0">
                                    <TextBlock Text="{loc:Translate 'Webhook path'}"
                                               Foreground="{DynamicResource TextBrush}"
                                               FontWeight="SemiBold" />
                                    <TextBox Text="{Binding CashPayments.KoFiWebhookPath, UpdateSourceTrigger=PropertyChanged}" />
                                </StackPanel>
                            </UniformGrid>
                            <TextBlock Margin="0,8,0,0"
                                       Text="{loc:Translate 'Ko-fi public webhook URL'}"
                                       Foreground="{DynamicResource TextBrush}"
                                       FontWeight="SemiBold" />
                            <TextBox Text="{Binding CashPayments.KoFiPublicWebhookUrl, UpdateSourceTrigger=PropertyChanged}" />
                            <TextBlock Margin="0,8,0,0"
                                       Text="{Binding CashPayments.KoFiLocalWebhookUrl}"
                                       Foreground="{DynamicResource MutedBrush}"
                                       TextWrapping="Wrap" />
                        </StackPanel>

                        <TextBlock Margin="0,8,0,0"
                                   Text="{loc:Translate 'Ko-fi verification token'}"
                                   Foreground="{DynamicResource TextBrush}"
                                   FontWeight="SemiBold" />
                        <PasswordBox local:PasswordBoxBinding.BindPassword="True"
                                     local:PasswordBoxBinding.BoundPassword="{Binding CashPayments.KoFiVerificationToken, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                    </StackPanel>
                </Border>
            </StackPanel>
        </ScrollViewer>
    </Grid>
</Window>
```

- [ ] **Step 2: Create the code-behind**

Create `CashPaymentManagerWindow.xaml.cs`:

```csharp
using System;
using System.Windows;
using System.Windows.Input;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class CashPaymentManagerWindow : Window
{
    public CashPaymentManagerWindow(CashPaymentManagerViewModel viewModel)
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, ThemeManager.CurrentTheme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;
    }

    private CashPaymentManagerViewModel Vm => (CashPaymentManagerViewModel)DataContext;

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed)
            return;
        try { DragMove(); }
        catch (InvalidOperationException) { }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnMinimizeClicked(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        Closed -= OnWindowClosed;
        if (DataContext is IDisposable disposableDataContext)
            disposableDataContext.Dispose();
    }
}
```

- [ ] **Step 3: Register XAML and code in the project file**

Since the project has `EnableDefaultItems=false`, add the new files to `VrcTwitchOscBridge.csproj`. Search for existing `<Page Include="*ManagerWindow.xaml"` entries and add matching entries.

Find the block with other manager windows (e.g., `<Page Include="AvatarScalingManagerWindow.xaml" />`) and add:
```xml
    <Page Include="CashPaymentManagerWindow.xaml" />
```

Find matching `<Compile Include="*ManagerWindow.xaml.cs"` entries and add:
```xml
    <Compile Include="CashPaymentManagerWindow.xaml.cs" />
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

---

### Task 4: MainWindowViewModel — Remove Inline Tab, Add Open Manager + Migration Command

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`

**Interfaces:**
- Consumes: `CashPaymentManagerViewModel`, `CashPaymentManagerWindow`
- Produces: `OpenCashPaymentManagerCommand`, `DismissCashPaymentMigrationNoticeCommand`, removal of all Cash Payment inline tab members

- [ ] **Step 1: Remove `RuleListView.CashPayments` from the enum**

Find the `RuleListView` enum (around line 21003) and remove `CashPayments,`.

Before:
```csharp
        AvatarTriggers, MasterAvatar, MovementRedeems, PowerUps,
        UniversalTriggers, AvatarScaling, CashPayments, RewardFireSale, Wardrobe
```

After:
```csharp
        AvatarTriggers, MasterAvatar, MovementRedeems, PowerUps,
        UniversalTriggers, AvatarScaling, RewardFireSale, Wardrobe
```

- [ ] **Step 2: Remove `IsViewingCashPayments` property**

Remove line 1423:
```csharp
    public bool IsViewingCashPayments => activeRuleListView == RuleListView.CashPayments;
```

- [ ] **Step 3: Remove help text branches referencing `IsViewingCashPayments`**

Remove the CashPayments-specific branches from the help text properties (around lines 1671, 1685, 1701, 1715, 1729, 1743, 1757). Each branch looks like:

```csharp
        : IsViewingCashPayments
            ? T("...")
            : IsViewingPowerUps...
```

For each one, remove the `IsViewingCashPayments ? ... :` part so the flow goes to the `IsViewingPowerUps` check directly.

- [ ] **Step 4: Remove `SelectedCashPaymentRule` and related fields**

Remove:
- Line 440: `private CashPaymentRule? selectedCashPaymentRule;`
- Line 557: `private Guid lastSelectedCashPaymentRuleId = Guid.Empty;`
- Lines 2382-2399: The `SelectedCashPaymentRule` property (get/set with all its logic)

- [ ] **Step 5: Remove Cash Payment CRUD commands (keep connection commands)**

Remove lines that create these commands:
- Line 963: `AddCashPaymentRuleCommand = new RelayCommand(AddCashPaymentRule);`
- Line 965: `RemoveSelectedCashPaymentRuleCommand = new RelayCommand(...)`
- Line 966: `EnableAllCashPaymentRulesCommand = new RelayCommand(...)`
- Line 967: `DisableAllCashPaymentRulesCommand = new RelayCommand(...)`
- Line 968: `DeleteAllCashPaymentRulesCommand = new RelayCommand(...)`
- Line 969: `TestSelectedCashPaymentRuleCommand = new AsyncRelayCommand(...)`

Keep:
- Line 964: `AddAvatarScalingCashPaymentRuleCommand = new RelayCommand(AddAvatarScalingCashPaymentRule);`
- Line 876: `OpenKoFiWebhooksCommand = new RelayCommand(OpenKoFiWebhooksPage);` — still proxied by CashPaymentManagerViewModel
- Line 981: `RegenerateKoFiRelayIdentityCommand = new RelayCommand(RegenerateKoFiRelayIdentity);` — still proxied by CashPaymentManagerViewModel

- [ ] **Step 6: Remove Redundant Cash Payment command properties (keep connection proxies)**

Remove these command property declarations (around lines 3143-3155):
```csharp
    public RelayCommand AddCashPaymentRuleCommand { get; }
    public RelayCommand RemoveSelectedCashPaymentRuleCommand { get; }
    public RelayCommand EnableAllCashPaymentRulesCommand { get; }
    public RelayCommand DisableAllCashPaymentRulesCommand { get; }
    public RelayCommand DeleteAllCashPaymentRulesCommand { get; }
    public AsyncRelayCommand TestSelectedCashPaymentRuleCommand { get; }
```

Keep:
- `AddAvatarScalingCashPaymentRuleCommand` (line 3145)
- `OpenKoFiWebhooksCommand` (line 2985)
- `RegenerateKoFiRelayIdentityCommand` (line 3175)

- [ ] **Step 7: Remove `CashPaymentRules` property**

Remove around line 1520:
```csharp
    public IReadOnlyList<CashPaymentRule> CashPaymentRules => Settings.CashPaymentRules.ToArray();
```

- [ ] **Step 8: Remove `CashPaymentRuleStatusText` and `CashPaymentActionEditorHelpText` properties**

Remove the properties around lines 1923-1941.

- [ ] **Step 9: Remove `CashPaymentProviderOptions` and `CashPaymentActionKindOptions`**

Remove these property declarations and their backing fields (search for them around lines 706-708).

- [ ] **Step 10: Remove `ShowCashPayments()` and related methods**

Remove:
- `ShowCashPayments()` method (around line 5402)
- `GetRememberedCashPaymentRule()` method (around line 7535)
- Reference in `ShowPowerUps` if it calls `GetRememberedCashPaymentRule()` — keep `GetRememberedPowerUpRule()` only

- [ ] **Step 11: Remove Cash Payments branch from SwitchRuleView**

Remove lines 7726-7729:
```csharp
            if (targetView != RuleListView.CashPayments)
            {
                SelectedCashPaymentRule = null;
            }
```

- [ ] **Step 12: Remove Cash Payment initialization in Init/Dispose**

Remove from the constructor/init area:
- `appSettings.CashPayments.PropertyChanged += CashPaymentConnectionsChanged;` (line 7766)
- `appSettings.CashPaymentRules.CollectionChanged += CashPaymentRulesCollectionChanged;` (line 7767)
- The `foreach` wiring of cash payment rules (lines 7797-7799)
- From Dispose:
  - `appSettings.CashPayments.PropertyChanged -= CashPaymentConnectionsChanged;` (line 7821)
  - `appSettings.CashPaymentRules.CollectionChanged -= CashPaymentRulesCollectionChanged;` (line 7822)
  - The `foreach` unwiring (lines 7852-7854)

- [ ] **Step 13: Remove `CashPaymentConnectionsChanged` handler**

Remove the method at around line 8358.

- [ ] **Step 14: Remove `CashPaymentRulesCollectionChanged`, `WireCashPaymentRule`, `UnwireCashPaymentRule`, `CashPaymentRuleChanged`**

Remove these methods (around lines 8365-8424).

- [ ] **Step 15: Remove Cash Payment rule CRUD methods**

Remove these methods:
- `AddCashPaymentRule()` (line 6977)
- `RemoveSelectedCashPaymentRule()` (line 6997)
- `EnableAllCashPaymentRules()` (line 7012)
- `DisableAllCashPaymentRules()` (line 7024)
- `DeleteAllCashPaymentRules()` (line 7036)
- `TestSelectedCashPaymentRuleAsync()` (line 10233)
- `CreateDefaultCashPaymentRule()` (line 6939)

Keep:
- `AddAvatarScalingCashPaymentRule()` (line 6987)
- `CreateDefaultAvatarScalingCashPaymentRule()` (line 6959)
- `DeleteCashPaymentRuleByCard()` (line 6871)

- [ ] **Step 16: Remove references to `IsViewingCashPayments` in helper methods**

- Line 2072: Remove the `if (IsViewingCashPayments)` block in `GetActionTypeOptionsForSelectedContext` so it only checks `IsViewingPowerUps`
- Line 19671: Remove the `if (IsViewingCashPayments)` block in `GetSelectedParameterCacheAvatarId()` so it falls through to `IsViewingPowerUps`
- Line 18652: Change `return (IsViewingMasterAvatar || IsViewingCashPayments)` to `return IsViewingMasterAvatar`

- [ ] **Step 17: Remove `RaisePropertyChanged(nameof(IsViewingCashPayments))` calls**

Remove lines 7700 and 18983 which raise `PropertyChanged` for `IsViewingCashPayments`.

- [ ] **Step 18: Remove `testModeCashProvider` related Cash Payment items**

Keep `testModeCashProvider` and `testModeCashProviderOptions` for the Test Mode window (it still simulates cash payments). But remove `CashPaymentProvider`-specific references that were only for the inline tab.

- [ ] **Step 19: Add `OpenCashPaymentManagerCommand`**

Add the field declaration (alongside other manager window fields around line 5174):
```csharp
    private CashPaymentManagerWindow? _cashPaymentManagerWindow;
```

Add initialization in the constructor (alongside other command inits, around line 919):
```csharp
        OpenCashPaymentManagerCommand = new RelayCommand(OpenCashPaymentManager);
```

Add property declaration (alongside other command properties, around line 3059):
```csharp
    public RelayCommand OpenCashPaymentManagerCommand { get; }
```

Add the method (alongside other `Open*Manager` methods, around lines 5199-5214):
```csharp
    private void OpenCashPaymentManager()
    {
        if (_cashPaymentManagerWindow is { IsVisible: true })
        {
            _cashPaymentManagerWindow.Activate();
            return;
        }

        var managerVm = new CashPaymentManagerViewModel(Settings, this);
        _cashPaymentManagerWindow = new CashPaymentManagerWindow(managerVm)
        {
            Owner = Application.Current?.MainWindow,
        };
        _cashPaymentManagerWindow.Closed += (_, _) => _cashPaymentManagerWindow = null;
        _cashPaymentManagerWindow.Show();
    }
```

- [ ] **Step 20: Add migration notice command and property**

Add `DismissCashPaymentMigrationNoticeCommand` alongside `DismissMigrationNoticeCommand` (around line 911):
```csharp
        DismissCashPaymentMigrationNoticeCommand = new RelayCommand(DismissCashPaymentMigrationNotice);
```

Add property declaration (around line 3059):
```csharp
    public RelayCommand DismissCashPaymentMigrationNoticeCommand { get; }
```

Add `ShowCashPaymentMigrationNotice` property (around line 1360):
```csharp
    public bool ShowCashPaymentMigrationNotice =>
        !Settings.CashPaymentMigrationNoticeShown;
```

Add dismiss method (around line 3791):
```csharp
    private void DismissCashPaymentMigrationNotice()
    {
        if (Settings.CashPaymentMigrationNoticeShown)
        {
            return;
        }

        Settings.CashPaymentMigrationNoticeShown = true;
        _ = SaveSettingsAsync();
    }
```

Also add the command to the sidebar button binding. Actually, wait — the MainWindow.xaml sidebar button for Cash Payments will now call `OpenCashPaymentManagerCommand`. The migration notice is shown in MainWindow.xaml.cs, not from a XAML binding. But the DismissCashPaymentMigrationNoticeCommand is needed by the code-behind.

- [ ] **Step 21: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

---

### Task 5: MainWindow.xaml — Remove Inline Sections, Change Sidebar Button

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\MainWindow.xaml`

**Interfaces:**
- Consumes: `MainWindowViewModel` changes from Task 4
- Produces: Clean MainWindow.xaml with no Cash Payment inline sections

- [ ] **Step 1: Change sidebar button command and remove active-tag DataTrigger**

Find the "Cash Payments" sidebar button (around line 3540-3554). Replace `Command="{Binding ShowCashPaymentsCommand}"` with `Command="{Binding OpenCashPaymentManagerCommand}"` and remove the `<DataTrigger Binding="{Binding IsViewingCashPayments}" Value="True">` block inside the button's style.

- [ ] **Step 2: Remove the Cash Payment actions panel**

Remove the entire `<StackPanel>` block that is the Cash Payment sidebar actions (around lines 3620-3795), which includes:
- The "Add Cash Rule" / "Add Avatar Scaling Cash Rule" buttons
- The Enable All / Disable All buttons
- The Delete / Delete All buttons
- The entire provider connections Expander panel

Find the closing tag that corresponds to this panel — it should end around line 3795 with a `</StackPanel>` followed by the Power Ups panel starting at line 3797.

- [ ] **Step 3: Remove the Cash Payment rules ListBox**

Remove the `CashPaymentRules` ListBox (around lines 3898-3930+) which includes:
- The `<ListBox ItemsSource="{Binding CashPaymentRules}" ...>`
- Its `<ListBox.Style>` with the `IsViewingCashPayments` DataTrigger
- Its `<ListBox.ItemTemplate>` with the `CashPaymentRule` data template

- [ ] **Step 4: Remove empty-state condition for CashPayments**

Find the `<MultiDataTrigger>` around line 3997 that checks both `IsViewingCashPayments` and `CashPaymentRules.Count == 0`. Remove the first `<Condition>` for `IsViewingCashPayments` — or if both conditions are in a single `MultiDataTrigger` that's only relevant for Cash Payments, remove the entire trigger.

- [ ] **Step 5: Remove the Cash Payment workspace editor**

Remove the workspace editor border (around lines 6073-6420+), which includes:
- The `<Border>` with `<DataTrigger Binding="{Binding IsViewingCashPayments}" Value="True">`
- The `<ContentControl Content="{Binding SelectedCashPaymentRule}">`
- The entire data template with Payment Match, Action Family, and scaling action sections

- [ ] **Step 6: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

---

### Task 6: MainWindow.xaml.cs — Add Migration Notice

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `ShowCashPaymentMigrationNotice`, `DismissCashPaymentMigrationNoticeCommand` from MainWindowVM
- Produces: One-time popup on 3.1.8→3.1.9 upgrade

- [ ] **Step 1: Add migration notice method**

After `ShowAvatarSwapMigrationNoticeIfNeeded()` (around line 168), add:

```csharp
    private void ShowCashPaymentMigrationNoticeIfNeeded()
    {
        if (!viewModel.ShowCashPaymentMigrationNotice)
        {
            return;
        }

        MessageBox.Show(
            this,
            LocalizationService.Translate(
                "Cash Payments has moved into its own window and no longer has its own rules tab. Cash Payment rules are now managed through the Avatar Scaling Manager's 'Cash Payments' source tab. All Cash Payment connections (StreamElements, Streamlabs, Ko-fi) can be found by clicking Cash Payments in the sidebar. This notice will not appear again."),
            LocalizationService.Translate("Cash Payment Connections"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        viewModel.DismissCashPaymentMigrationNoticeCommand.Execute(null);
    }
```

- [ ] **Step 2: Call the method from OnLoaded**

In `OnLoaded` (around line 149), after `ShowAvatarSwapMigrationNoticeIfNeeded();`, add:

```csharp
        ShowCashPaymentMigrationNoticeIfNeeded();
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

---

### Task 7: Final Build and Test

- [ ] **Step 1: Final build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds with no errors

- [ ] **Step 2: Run tests**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`
Expected: All tests pass
