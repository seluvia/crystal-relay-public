# Avatar Scaling — Dedicated Section Add Buttons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give each of the four Avatar Scaling Manager source sections (Twitch Rewards, Supporter Growth, Cash Payments, Power Ups) its own dedicated Add button that creates a new card pre-set to that section's system type.

**Architecture:** Add two new `RelayCommand`s on `MainWindowViewModel` that create `CashPaymentRule` / `PowerUpRule` with `ActionKind = AvatarScaling` (so they appear as cards). Expose them via pass-through properties on `AvatarScalingManagerViewModel`. Rework the XAML section headers so each section has its own Add button, and remove the shared "Add Reward Growth" button from the Pay System header. Add 2 new localization keys across 14 files (`"Add Power Up"` already exists).

**Tech Stack:** C# .NET 10 WPF, XAML, xUnit, JSON localization files.

**Spec:** `docs/superpowers/specs/2026-07-01-avatar-scale-dedicated-section-add-buttons-design.md`

---

## File Structure

- **Modify:** `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs` — 2 new factory methods, 2 new commands, 2 new `NotifyCanExecuteChanged` calls
- **Modify:** `VrcTwitchOscBridge\ViewModels\AvatarScalingManagerViewModel.cs` — 2 new pass-through properties
- **Modify:** `VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml` — rework 3 section headers, remove shared button
- **Modify:** `VrcTwitchOscBridge\Resources\Localization\*.extra.json` (14 files) — 2 new keys
- **Modify:** `VrcTwitchOscBridge.Tests\AvatarScalingManagerViewModelTests.cs` — command exposure + creation tests
- **Modify:** `VrcTwitchOscBridge.Tests\AvatarScalingManagerWindowXamlTests.cs` — section header button tests

---

### Task 1: Add Cash Payment scaling command to MainWindowViewModel

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs` (factory method near line 7192, command wiring near line 976, command property near line 3235, NotifyCanExecuteChanged near line 17662)
- Test: `VrcTwitchOscBridge.Tests\AvatarScalingManagerViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `VrcTwitchOscBridge.Tests\AvatarScalingManagerViewModelTests.cs` (append before the final closing class brace):

```csharp
[Fact]
public async Task AddAvatarScalingCashPaymentRuleCommand_CreatesRuleWithAvatarScalingActionKind()
{
    await using var parent = new MainWindowViewModel();

    Assert.Empty(parent.Settings.CashPaymentRules);

    parent.AddAvatarScalingCashPaymentRuleCommand.Execute(null);

    var rule = Assert.Single(parent.Settings.CashPaymentRules);
    Assert.Equal(CashPaymentActionKind.AvatarScaling, rule.ActionKind);
    Assert.True(rule.UsesAvatarScaling);
    Assert.Same(rule, parent.SelectedCashPaymentRule);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AddAvatarScalingCashPaymentRuleCommand_CreatesRuleWithAvatarScalingActionKind"`
Expected: FAIL — `AddAvatarScalingCashPaymentRuleCommand` does not exist (compile error).

- [ ] **Step 3: Add the factory method**

In `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`, immediately AFTER the existing `CreateDefaultCashPaymentRule()` method (around line 7210, after the closing brace of `CreateDefaultCashPaymentRule`), add:

```csharp
private static CashPaymentRule CreateDefaultAvatarScalingCashPaymentRule()
{
    var rule = new CashPaymentRule
    {
        Name = "New Cash Payment Scale",
        Provider = CashPaymentProvider.StreamElements,
        MinimumAmount = 1m,
        MaximumAmount = 0m,
        CurrencyCode = string.Empty,
        MessageContains = string.Empty,
        CooldownSeconds = 30,
        ActionKind = CashPaymentActionKind.AvatarScaling
    };
    rule.ScaleAction = CashPaymentRule.CreateDefaultScaleAction();
    rule.ScaleAction.Name = rule.Name;
    return rule;
}
```

- [ ] **Step 4: Add the private add method**

Immediately AFTER the existing `AddCashPaymentRule()` method (around line 7220, after its closing brace), add:

```csharp
private void AddAvatarScalingCashPaymentRule()
{
    var rule = CreateDefaultAvatarScalingCashPaymentRule();
    Settings.CashPaymentRules.Add(rule);
    SelectedCashPaymentRule = rule;
    QueueSave();
    QueueBridgeRefresh();
    AppendLog($"Added cash payment scaling rule '{rule.DisplayTitle}'.");
}
```

- [ ] **Step 5: Wire the command in the constructor**

Find the constructor wiring line (~line 976):
```csharp
AddCashPaymentRuleCommand = new RelayCommand(AddCashPaymentRule);
```
Immediately AFTER it, add:
```csharp
AddAvatarScalingCashPaymentRuleCommand = new RelayCommand(AddAvatarScalingCashPaymentRule);
```

- [ ] **Step 6: Add the command property**

Find the property declaration (~line 3235):
```csharp
public RelayCommand AddCashPaymentRuleCommand { get; }
```
Immediately AFTER it, add:
```csharp
public RelayCommand AddAvatarScalingCashPaymentRuleCommand { get; }
```

- [ ] **Step 7: Add NotifyCanExecuteChanged call**

Find the refresh block (~line 17662):
```csharp
AddCashPaymentRuleCommand.NotifyCanExecuteChanged();
```
Immediately AFTER it, add:
```csharp
AddAvatarScalingCashPaymentRuleCommand.NotifyCanExecuteChanged();
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AddAvatarScalingCashPaymentRuleCommand_CreatesRuleWithAvatarScalingActionKind"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay"
git add VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs VrcTwitchOscBridge.Tests/AvatarScalingManagerViewModelTests.cs
git commit -m "Add AddAvatarScalingCashPaymentRuleCommand for dedicated Cash Payment section add"
```

---

### Task 2: Add Power Up scaling command to MainWindowViewModel

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs` (factory method near line 7278, command wiring near line 982, command property near line 3247, NotifyCanExecuteChanged near line 17668)
- Test: `VrcTwitchOscBridge.Tests\AvatarScalingManagerViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `VrcTwitchOscBridge.Tests\AvatarScalingManagerViewModelTests.cs` (append before the final closing class brace):

```csharp
[Fact]
public async Task AddAvatarScalingPowerUpRuleCommand_CreatesRuleWithAvatarScalingActionKind()
{
    await using var parent = new MainWindowViewModel();

    Assert.Empty(parent.Settings.PowerUpRules);

    parent.AddAvatarScalingPowerUpRuleCommand.Execute(null);

    var rule = Assert.Single(parent.Settings.PowerUpRules);
    Assert.Equal(PowerUpActionKind.AvatarScaling, rule.ActionKind);
    Assert.True(rule.UsesAvatarScaling);
    Assert.Same(rule, parent.SelectedPowerUpRule);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AddAvatarScalingPowerUpRuleCommand_CreatesRuleWithAvatarScalingActionKind"`
Expected: FAIL — `AddAvatarScalingPowerUpRuleCommand` does not exist (compile error).

- [ ] **Step 3: Add the factory method**

In `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`, immediately AFTER the existing `CreateDefaultPowerUpRule()` method (around line 7293, after its closing brace), add:

```csharp
private static PowerUpRule CreateDefaultAvatarScalingPowerUpRule()
{
    var rule = new PowerUpRule
    {
        Name = "New Power Up Scale",
        SourceMode = TwitchRewardSyncMode.LinkExisting,
        BitsCost = 100,
        CooldownSeconds = 30,
        ActionKind = PowerUpActionKind.AvatarScaling
    };
    rule.ScaleAction = PowerUpRule.CreateDefaultScaleAction();
    rule.ScaleAction.Name = rule.Name;
    return rule;
}
```

- [ ] **Step 4: Add the private add method**

Immediately AFTER the existing `AddPowerUpRule()` method (around line 7303, after its closing brace), add:

```csharp
private void AddAvatarScalingPowerUpRule()
{
    var rule = CreateDefaultAvatarScalingPowerUpRule();
    Settings.PowerUpRules.Add(rule);
    SelectedPowerUpRule = rule;
    QueueSave();
    QueueBridgeRefresh();
    AppendLog($"Added Power Up scaling rule '{rule.DisplayTitle}'.");
}
```

- [ ] **Step 5: Wire the command in the constructor**

Find the constructor wiring line (~line 982):
```csharp
AddPowerUpRuleCommand = new RelayCommand(AddPowerUpRule);
```
Immediately AFTER it, add:
```csharp
AddAvatarScalingPowerUpRuleCommand = new RelayCommand(AddAvatarScalingPowerUpRule);
```

- [ ] **Step 6: Add the command property**

Find the property declaration (~line 3247):
```csharp
public RelayCommand AddPowerUpRuleCommand { get; }
```
Immediately AFTER it, add:
```csharp
public RelayCommand AddAvatarScalingPowerUpRuleCommand { get; }
```

- [ ] **Step 7: Add NotifyCanExecuteChanged call**

Find the refresh block (~line 17668):
```csharp
AddPowerUpRuleCommand.NotifyCanExecuteChanged();
```
Immediately AFTER it, add:
```csharp
AddAvatarScalingPowerUpRuleCommand.NotifyCanExecuteChanged();
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AddAvatarScalingPowerUpRuleCommand_CreatesRuleWithAvatarScalingActionKind"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay"
git add VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs VrcTwitchOscBridge.Tests/AvatarScalingManagerViewModelTests.cs
git commit -m "Add AddAvatarScalingPowerUpRuleCommand for dedicated Power Up section add"
```

---

### Task 3: Add pass-through properties to AvatarScalingManagerViewModel

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\AvatarScalingManagerViewModel.cs` (near line 180, after `AddRewardGrowthCommand`)
- Test: `VrcTwitchOscBridge.Tests\AvatarScalingManagerViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `VrcTwitchOscBridge.Tests\AvatarScalingManagerViewModelTests.cs` (append before the final closing class brace):

```csharp
[Fact]
public async Task Constructor_WithParentMainWindow_ExposesAvatarScalingCashAndPowerUpAddCommands()
{
    await using var parent = new MainWindowViewModel();
    using var vm = new AvatarScalingManagerViewModel(parent.Settings, parent);

    Assert.Same(parent.AddAvatarScalingCashPaymentRuleCommand, vm.AddAvatarScalingCashPaymentRuleCommand);
    Assert.Same(parent.AddAvatarScalingPowerUpRuleCommand, vm.AddAvatarScalingPowerUpRuleCommand);
}

[Fact]
public void PassThroughs_AvatarScalingCashAndPowerUpAddCommands_NullWhenParentMissing()
{
    using var vm = new AvatarScalingManagerViewModel(new AppSettings(), null);

    Assert.Null(vm.AddAvatarScalingCashPaymentRuleCommand);
    Assert.Null(vm.AddAvatarScalingPowerUpRuleCommand);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScalingCashAndPowerUp"`
Expected: FAIL — `AddAvatarScalingCashPaymentRuleCommand` / `AddAvatarScalingPowerUpRuleCommand` properties do not exist on `AvatarScalingManagerViewModel` (compile error).

- [ ] **Step 3: Add the pass-through properties**

In `VrcTwitchOscBridge\ViewModels\AvatarScalingManagerViewModel.cs`, find the existing pass-through (~line 180):
```csharp
public RelayCommand? AddRewardGrowthCommand => mainWindowViewModel?.AddRewardGrowthCommand;
```
Immediately AFTER it, add:
```csharp
public RelayCommand? AddAvatarScalingCashPaymentRuleCommand => mainWindowViewModel?.AddAvatarScalingCashPaymentRuleCommand;

public RelayCommand? AddAvatarScalingPowerUpRuleCommand => mainWindowViewModel?.AddAvatarScalingPowerUpRuleCommand;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScalingCashAndPowerUp"`
Expected: PASS (both tests).

- [ ] **Step 5: Commit**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay"
git add VrcTwitchOscBridge/ViewModels/AvatarScalingManagerViewModel.cs VrcTwitchOscBridge.Tests/AvatarScalingManagerViewModelTests.cs
git commit -m "Expose AvatarScaling Cash/PowerUp add commands on manager ViewModel"
```

---

### Task 4: Add localization keys (2 new keys × 14 files)

**Files:**
- Modify: all 14 `VrcTwitchOscBridge\Resources\Localization\*.extra.json` files

Note: `"Add Power Up"` already exists in all 14 files. Only 2 new keys are needed: `"Add Supporter Growth"` and `"Add Cash Payment"`. Insert each new key immediately AFTER the existing `"Add Reward Growth"` line in every file (keeps keys grouped).

The exact line to find in every file is:
```
  "Add Reward Growth": "<localized value>",
```
Insert the 2 new lines directly after it.

- [ ] **Step 1: Add keys to en-US.extra.json**

In `VrcTwitchOscBridge\Resources\Localization\en-US.extra.json`, find (line ~406):
```
  "Add Reward Growth": "Add Reward Growth",
```
Replace with:
```
  "Add Reward Growth": "Add Reward Growth",
  "Add Supporter Growth": "Add Supporter Growth",
  "Add Cash Payment": "Add Cash Payment",
```

- [ ] **Step 2: Add keys to de-DE.extra.json**

Find:
```
  "Add Reward Growth": "Supporter-Wachstum hinzufügen",
```
Replace with:
```
  "Add Reward Growth": "Supporter-Wachstum hinzufügen",
  "Add Supporter Growth": "Supporter-Wachstum hinzufügen",
  "Add Cash Payment": "Barzahlung hinzufügen",
```

- [ ] **Step 3: Add keys to fr-FR.extra.json**

Find:
```
  "Add Reward Growth": "Ajouter une croissance de supporters",
```
Replace with:
```
  "Add Reward Growth": "Ajouter une croissance de supporters",
  "Add Supporter Growth": "Ajouter Croissance des supporters",
  "Add Cash Payment": "Ajouter Paiement en cash",
```

- [ ] **Step 4: Add keys to es-ES.extra.json**

Find:
```
  "Add Reward Growth": "Añadir crecimiento de seguidores",
```
Replace with:
```
  "Add Reward Growth": "Añadir crecimiento de seguidores",
  "Add Supporter Growth": "Añadir Crecimiento de supporters",
  "Add Cash Payment": "Añadir Pago en efectivo",
```

- [ ] **Step 5: Add keys to it-IT.extra.json**

Find:
```
  "Add Reward Growth": "Aggiungi crescita supporter",
```
Replace with:
```
  "Add Reward Growth": "Aggiungi crescita supporter",
  "Add Supporter Growth": "Aggiungi Crescita sostenitore",
  "Add Cash Payment": "Aggiungi Pagamento contante",
```

- [ ] **Step 6: Add keys to sv-SE.extra.json**

Find:
```
  "Add Reward Growth": "Lägg till supporter-tillväxt",
```
Replace with:
```
  "Add Reward Growth": "Lägg till supporter-tillväxt",
  "Add Supporter Growth": "Lägg till Supportertillväxt",
  "Add Cash Payment": "Lägg till Kontantbetalning",
```

- [ ] **Step 7: Add keys to ru-RU.extra.json**

Find:
```
  "Add Reward Growth": "Добавить рост саппортеров",
```
Replace with:
```
  "Add Reward Growth": "Добавить рост саппортеров",
  "Add Supporter Growth": "Добавить Рост саппортеров",
  "Add Cash Payment": "Добавить Денежный платёж",
```

- [ ] **Step 8: Add keys to pt-BR.extra.json**

Find:
```
  "Add Reward Growth": "Adicionar crescimento de apoiadores",
```
Replace with:
```
  "Add Reward Growth": "Adicionar crescimento de apoiadores",
  "Add Supporter Growth": "Adicionar Crescimento de Apoiadores",
  "Add Cash Payment": "Adicionar Pagamento em Dinheiro",
```

- [ ] **Step 9: Add keys to pl-PL.extra.json**

Find:
```
  "Add Reward Growth": "Dodaj wzrost wspierających",
```
Replace with:
```
  "Add Reward Growth": "Dodaj wzrost wspierających",
  "Add Supporter Growth": "Dodaj Rozwój wspierających",
  "Add Cash Payment": "Dodaj Płatność gotówkową",
```

- [ ] **Step 10: Add keys to ko-KR.extra.json**

Find:
```
  "Add Reward Growth": "서포터 성장 추가",
```
Replace with:
```
  "Add Reward Growth": "서포터 성장 추가",
  "Add Supporter Growth": "서포터 성장 추가",
  "Add Cash Payment": "현금 결제 추가",
```

- [ ] **Step 11: Add keys to ja-JP.extra.json**

Find:
```
  "Add Reward Growth": "サポーターの成長を追加",
```
Replace with:
```
  "Add Reward Growth": "サポーターの成長を追加",
  "Add Supporter Growth": "サポーター成長を追加",
  "Add Cash Payment": "キャッシュ支払いを追加",
```

- [ ] **Step 12: Add keys to zh-CN.extra.json**

Find:
```
  "Add Reward Growth": "添加支持者成长",
```
Replace with:
```
  "Add Reward Growth": "添加支持者成长",
  "Add Supporter Growth": "添加支持者成长",
  "Add Cash Payment": "添加现金打赏",
```

- [ ] **Step 13: Add keys to zh-TW.extra.json**

Find:
```
  "Add Reward Growth": "新增支持者成長",
```
Replace with:
```
  "Add Reward Growth": "新增支持者成長",
  "Add Supporter Growth": "新增支持者成長",
  "Add Cash Payment": "新增現金付款",
```

- [ ] **Step 14: Add keys to th-TH.extra.json**

Find:
```
  "Add Reward Growth": "เพิ่มการเติบโตของผู้สนับสนุน",
```
Replace with:
```
  "Add Reward Growth": "เพิ่มการเติบโตของผู้สนับสนุน",
  "Add Supporter Growth": "เพิ่มการเติบโตของซัพพอร์ตเตอร์",
  "Add Cash Payment": "เพิ่มการชำระเงินสด",
```

- [ ] **Step 15: Verify all 14 files contain both new keys**

Run this PowerShell snippet and confirm it prints `14` then `14`:
```powershell
$files = Get-ChildItem "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization\*.extra.json"
($files | Where-Object { (Get-Content $_.FullName -Raw) -match '"Add Supporter Growth"' }).Count
($files | Where-Object { (Get-Content $_.FullName -Raw) -match '"Add Cash Payment"' }).Count
```
Expected: `14` and `14`.

- [ ] **Step 16: Commit**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay"
git add VrcTwitchOscBridge/Resources/Localization/*.extra.json
git commit -m "Add localized Add Supporter Growth and Add Cash Payment keys"
```

---

### Task 5: Rework XAML section headers

**Files:**
- Modify: `VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml` (lines ~1020-1106, the Pay System Rewards right column)

- [ ] **Step 1: Remove the shared "Add Reward Growth" button from the Pay System header**

In `VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml`, find the Pay System Rewards header DockPanel (around lines 1021-1033):

```xml
                            <!-- RIGHT COLUMN: Pay System Rewards -->
                            <StackPanel Grid.Column="2">
                                <DockPanel LastChildFill="True">
                                    <Button DockPanel.Dock="Right"
                                            Content="{loc:Translate 'Add Reward Growth'}"
                                            Command="{Binding AddRewardGrowthCommand}"
                                            Visibility="{Binding HasSelectedAvatarScaleSet, Converter={StaticResource BoolToVisibilityConverter}}"
                                            Style="{StaticResource SecondaryButtonStyle}"
                                            Margin="8,0,0,0" />
                                    <TextBlock Text="{loc:Translate 'Pay System Rewards'}" FontWeight="Bold" FontSize="16" />
                                </DockPanel>
                                <TextBlock Margin="0,4,0,10"
                                           Text="{loc:Translate 'Supporter Growth, Cash Payments &amp; Power Ups'}"
                                           Foreground="{DynamicResource TitleBarSubTextBrush}" />
```

Replace with (removes the DockPanel + Button, keeps the title and description):

```xml
                            <!-- RIGHT COLUMN: Pay System Rewards -->
                            <StackPanel Grid.Column="2">
                                <TextBlock Text="{loc:Translate 'Pay System Rewards'}" FontWeight="Bold" FontSize="16" />
                                <TextBlock Margin="0,4,0,10"
                                           Text="{loc:Translate 'Supporter Growth, Cash Payments &amp; Power Ups'}"
                                           Foreground="{DynamicResource TitleBarSubTextBrush}" />
```

- [ ] **Step 2: Add "Add Supporter Growth" button to the Supporter Growth section header**

Find the Supporter Growth section (around lines 1035-1057). Replace this block:

```xml
                                <!-- Supporter Growth -->
                                <Border>
                                    <Border.Style>
                                        <Style TargetType="Border" BasedOn="{StaticResource CardGroupStyle}">
                                            <Setter Property="Visibility" Value="Collapsed" />
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding ActiveSourceView}" Value="SupporterGrowth">
                                                    <Setter Property="Visibility" Value="Visible" />
                                                </DataTrigger>
                                                <DataTrigger Binding="{Binding ActiveSourceView}" Value="AllSources">
                                                    <Setter Property="Visibility" Value="Visible" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Border.Style>
                                    <StackPanel>
                                        <TextBlock Text="{loc:Translate 'Supporter Growth'}" FontWeight="Bold" FontSize="14" />
                                        <TextBlock Margin="0,4,0,10"
                                                   Text="{loc:Translate 'Event-driven Bits and Subs growth rules.'}"
                                                   Foreground="{DynamicResource TitleBarSubTextBrush}" />
                                        <ItemsControl ItemsSource="{Binding SupporterGrowthCards}" ItemTemplate="{StaticResource SourceCardTemplate}" />
                                    </StackPanel>
                                </Border>
```

With:

```xml
                                <!-- Supporter Growth -->
                                <Border>
                                    <Border.Style>
                                        <Style TargetType="Border" BasedOn="{StaticResource CardGroupStyle}">
                                            <Setter Property="Visibility" Value="Collapsed" />
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding ActiveSourceView}" Value="SupporterGrowth">
                                                    <Setter Property="Visibility" Value="Visible" />
                                                </DataTrigger>
                                                <DataTrigger Binding="{Binding ActiveSourceView}" Value="AllSources">
                                                    <Setter Property="Visibility" Value="Visible" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Border.Style>
                                    <StackPanel>
                                        <DockPanel LastChildFill="True">
                                            <Button DockPanel.Dock="Right"
                                                    Content="{loc:Translate 'Add Supporter Growth'}"
                                                    Command="{Binding AddRewardGrowthCommand}"
                                                    Style="{StaticResource SecondaryButtonStyle}"
                                                    Margin="8,0,0,0" />
                                            <TextBlock Text="{loc:Translate 'Supporter Growth'}" FontWeight="Bold" FontSize="14" />
                                        </DockPanel>
                                        <TextBlock Margin="0,4,0,10"
                                                   Text="{loc:Translate 'Event-driven Bits and Subs growth rules.'}"
                                                   Foreground="{DynamicResource TitleBarSubTextBrush}" />
                                        <ItemsControl ItemsSource="{Binding SupporterGrowthCards}" ItemTemplate="{StaticResource SourceCardTemplate}" />
                                    </StackPanel>
                                </Border>
```

- [ ] **Step 3: Add "Add Cash Payment" button to the Cash Payments section header**

Find the Cash Payments section (around lines 1059-1081). Replace this block:

```xml
                                <!-- Cash Payments -->
                                <Border>
                                    <Border.Style>
                                        <Style TargetType="Border" BasedOn="{StaticResource CardGroupStyle}">
                                            <Setter Property="Visibility" Value="Collapsed" />
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding ActiveSourceView}" Value="CashPayments">
                                                    <Setter Property="Visibility" Value="Visible" />
                                                </DataTrigger>
                                                <DataTrigger Binding="{Binding ActiveSourceView}" Value="AllSources">
                                                    <Setter Property="Visibility" Value="Visible" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Border.Style>
                                    <StackPanel>
                                        <TextBlock Text="{loc:Translate 'Cash Payments'}" FontWeight="Bold" FontSize="14" />
                                        <TextBlock Margin="0,4,0,10"
                                                   Text="{loc:Translate 'StreamElements, Streamlabs, and Ko-fi payment scaling rules.'}"
                                                   Foreground="{DynamicResource TitleBarSubTextBrush}" />
                                        <ItemsControl ItemsSource="{Binding CashPaymentCards}" ItemTemplate="{StaticResource SourceCardTemplate}" />
                                    </StackPanel>
                                </Border>
```

With:

```xml
                                <!-- Cash Payments -->
                                <Border>
                                    <Border.Style>
                                        <Style TargetType="Border" BasedOn="{StaticResource CardGroupStyle}">
                                            <Setter Property="Visibility" Value="Collapsed" />
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding ActiveSourceView}" Value="CashPayments">
                                                    <Setter Property="Visibility" Value="Visible" />
                                                </DataTrigger>
                                                <DataTrigger Binding="{Binding ActiveSourceView}" Value="AllSources">
                                                    <Setter Property="Visibility" Value="Visible" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Border.Style>
                                    <StackPanel>
                                        <DockPanel LastChildFill="True">
                                            <Button DockPanel.Dock="Right"
                                                    Content="{loc:Translate 'Add Cash Payment'}"
                                                    Command="{Binding AddAvatarScalingCashPaymentRuleCommand}"
                                                    Style="{StaticResource SecondaryButtonStyle}"
                                                    Margin="8,0,0,0" />
                                            <TextBlock Text="{loc:Translate 'Cash Payments'}" FontWeight="Bold" FontSize="14" />
                                        </DockPanel>
                                        <TextBlock Margin="0,4,0,10"
                                                   Text="{loc:Translate 'StreamElements, Streamlabs, and Ko-fi payment scaling rules.'}"
                                                   Foreground="{DynamicResource TitleBarSubTextBrush}" />
                                        <ItemsControl ItemsSource="{Binding CashPaymentCards}" ItemTemplate="{StaticResource SourceCardTemplate}" />
                                    </StackPanel>
                                </Border>
```

- [ ] **Step 4: Add "Add Power Up" button to the Power Ups section header**

Find the Power Ups section (around lines 1083-1105). Replace this block:

```xml
                                <!-- Power Ups -->
                                <Border>
                                    <Border.Style>
                                        <Style TargetType="Border" BasedOn="{StaticResource CardGroupStyle}">
                                            <Setter Property="Visibility" Value="Collapsed" />
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding ActiveSourceView}" Value="PowerUps">
                                                    <Setter Property="Visibility" Value="Visible" />
                                                </DataTrigger>
                                                <DataTrigger Binding="{Binding ActiveSourceView}" Value="AllSources">
                                                    <Setter Property="Visibility" Value="Visible" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Border.Style>
                                    <StackPanel>
                                        <TextBlock Text="{loc:Translate 'Power Ups'}" FontWeight="Bold" FontSize="14" />
                                        <TextBlock Margin="0,4,0,10"
                                                   Text="{loc:Translate 'Twitch Power-up Bits scaling rules.'}"
                                                   Foreground="{DynamicResource TitleBarSubTextBrush}" />
                                        <ItemsControl ItemsSource="{Binding PowerUpCards}" ItemTemplate="{StaticResource SourceCardTemplate}" />
                                    </StackPanel>
                                </Border>
```

With:

```xml
                                <!-- Power Ups -->
                                <Border>
                                    <Border.Style>
                                        <Style TargetType="Border" BasedOn="{StaticResource CardGroupStyle}">
                                            <Setter Property="Visibility" Value="Collapsed" />
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding ActiveSourceView}" Value="PowerUps">
                                                    <Setter Property="Visibility" Value="Visible" />
                                                </DataTrigger>
                                                <DataTrigger Binding="{Binding ActiveSourceView}" Value="AllSources">
                                                    <Setter Property="Visibility" Value="Visible" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Border.Style>
                                    <StackPanel>
                                        <DockPanel LastChildFill="True">
                                            <Button DockPanel.Dock="Right"
                                                    Content="{loc:Translate 'Add Power Up'}"
                                                    Command="{Binding AddAvatarScalingPowerUpRuleCommand}"
                                                    Style="{StaticResource SecondaryButtonStyle}"
                                                    Margin="8,0,0,0" />
                                            <TextBlock Text="{loc:Translate 'Power Ups'}" FontWeight="Bold" FontSize="14" />
                                        </DockPanel>
                                        <TextBlock Margin="0,4,0,10"
                                                   Text="{loc:Translate 'Twitch Power-up Bits scaling rules.'}"
                                                   Foreground="{DynamicResource TitleBarSubTextBrush}" />
                                        <ItemsControl ItemsSource="{Binding PowerUpCards}" ItemTemplate="{StaticResource SourceCardTemplate}" />
                                    </StackPanel>
                                </Border>
```

- [ ] **Step 5: Build to verify XAML compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay"
git add VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml
git commit -m "Rework Avatar Scaling section headers with dedicated Add buttons"
```

---

### Task 6: Add XAML header button tests

**Files:**
- Modify: `VrcTwitchOscBridge.Tests\AvatarScalingManagerWindowXamlTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `VrcTwitchOscBridge.Tests\AvatarScalingManagerWindowXamlTests.cs` (append before the final closing class brace):

```csharp
[Fact]
public void Window_EachPaySystemSectionHasDedicatedAddButton()
{
    var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
    var listAreaStart = xaml.IndexOf("<ScrollViewer Grid.Column=\"1\"", StringComparison.Ordinal);
    var editorStart = xaml.IndexOf("<Border Grid.Column=\"3\"", listAreaStart, StringComparison.Ordinal);
    var listArea = listAreaStart >= 0 && editorStart > listAreaStart
        ? xaml[listAreaStart..editorStart]
        : string.Empty;

    Assert.Contains("Add Supporter Growth", listArea, StringComparison.Ordinal);
    Assert.Contains("AddRewardGrowthCommand", listArea, StringComparison.Ordinal);
    Assert.Contains("Add Cash Payment", listArea, StringComparison.Ordinal);
    Assert.Contains("AddAvatarScalingCashPaymentRuleCommand", listArea, StringComparison.Ordinal);
    Assert.Contains("Add Power Up", listArea, StringComparison.Ordinal);
    Assert.Contains("AddAvatarScalingPowerUpRuleCommand", listArea, StringComparison.Ordinal);
}

[Fact]
public void Window_PaySystemHeaderDoesNotHaveSharedAddRewardGrowthButton()
{
    var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
    var listAreaStart = xaml.IndexOf("<ScrollViewer Grid.Column=\"1\"", StringComparison.Ordinal);
    var editorStart = xaml.IndexOf("<Border Grid.Column=\"3\"", listAreaStart, StringComparison.Ordinal);
    var listArea = listAreaStart >= 0 && editorStart > listAreaStart
        ? xaml[listAreaStart..editorStart]
        : string.Empty;

    var paySystemStart = listArea.IndexOf("Pay System Rewards", StringComparison.Ordinal);
    Assert.True(paySystemStart >= 0, "Pay System Rewards header should exist.");
    var paySystemBlock = listArea[paySystemStart..Math.Min(listArea.Length, paySystemStart + 400)];

    Assert.DoesNotContain("Add Reward Growth", paySystemBlock, StringComparison.Ordinal);
    Assert.DoesNotContain("AddRewardGrowthCommand", paySystemBlock, StringComparison.Ordinal);
}

[Fact]
public void Window_DedicatedAddButtonStringsAreLocalizedInAllExtraFiles()
{
    var expectedKeys = new[]
    {
        "Add Supporter Growth",
        "Add Cash Payment"
    };
    var localizationFolder = FindSourceDirectory("VrcTwitchOscBridge", "Resources", "Localization");
    var extraFiles = Directory.GetFiles(localizationFolder, "*.extra.json");

    Assert.NotEmpty(extraFiles);
    foreach (var file in extraFiles)
    {
        var content = File.ReadAllText(file);
        foreach (var key in expectedKeys)
        {
            Assert.Contains($"\"{key}\"", content, StringComparison.Ordinal);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScalingManagerWindowXamlTests"`
Expected: PASS (all XAML tests including the 3 new ones).

- [ ] **Step 3: Commit**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay"
git add VrcTwitchOscBridge.Tests/AvatarScalingManagerWindowXamlTests.cs
git commit -m "Add XAML tests for dedicated section Add buttons"
```

---

### Task 7: Final build + full Avatar Scaling test run

**Files:** none (verification only)

- [ ] **Step 1: Build the app project**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Run all Avatar Scaling tests**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScaling"`
Expected: ALL PASS (previous 73 + new tests, 0 failed).

- [ ] **Step 3: Report results**

Report the final test count and any failures to the user. Remind the user of the manual verification steps from the spec:
1. Open Avatar Scaling Manager (no crash).
2. Supporter Growth section shows its own "Add Supporter Growth" button.
3. Cash Payments section shows its own "Add Cash Payment" button; clicking creates a card.
4. Power Ups section shows its own "Add Power Up" button; clicking creates a card.
5. Shared "Add Reward Growth" button is gone from the Pay System header.
6. Card Delete buttons still work for all four card kinds.
