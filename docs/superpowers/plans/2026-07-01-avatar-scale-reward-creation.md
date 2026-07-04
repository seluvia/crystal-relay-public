# Avatar Scaling Manager — Reward Creation & Card Actions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Separate add buttons per section, add Delete buttons to cards, remove editor action buttons, default to All Sources view.

**Architecture:** ViewModel changes to add `DeleteCardCommand` and `AddRewardGrowthCommand`. XAML changes to move buttons, add Delete to SourceCardTemplate, and remove the editor WrapPanel. Localization for new keys.

**Spec:** `docs/superpowers/specs/2026-07-01-avatar-scale-reward-creation-design.md`

---

### Task 1: ViewModel — default to AllSources, add DeleteCardCommand, add AddRewardGrowthCommand

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarScalingManagerViewModel.cs`
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Change default ActiveSourceView to AllSources**

In `AvatarScalingManagerViewModel.cs`, change line 50:
```csharp
private AvatarScalingManagerSourceView activeSourceView = AvatarScalingManagerSourceView.AllSources;
```

- [ ] **Step 2: Add AddRewardGrowthCommand to MainWindowViewModel**

In `MainWindowViewModel.cs`, after the `AddAvatarScaleRuleCommand` initialization (around line 968), add:
```csharp
AddRewardGrowthCommand = new RelayCommand(AddRewardGrowth);
```

Add the command property near line 3217:
```csharp
public RelayCommand AddRewardGrowthCommand { get; }
```

Add the method after `AddAvatarScaleRule()` (around line 7069):
```csharp
private void AddRewardGrowth()
{
    EnsureSelectedAvatarScaleSet();
    if (SelectedAvatarScaleSet is null)
    {
        return;
    }

    var rule = CreateDefaultAvatarScaleRule();
    rule.TriggerType = AvatarScaleTriggerType.SupporterGrowth;
    rule.Name = "New Supporter Growth";
    SelectedAvatarScaleSet.ScaleRules.Add(rule);
    SelectedAvatarScaleRule = rule;
    QueueSave();
    QueueBridgeRefresh();
    AppendLog($"Added supporter growth rule '{rule.DisplayTitle}' to '{SelectedAvatarScaleSet.DisplayTitle}'.");
}
```

Also add `AddRewardGrowthCommand.NotifyCanExecuteChanged();` next to the existing `AddAvatarScaleRuleCommand.NotifyCanExecuteChanged();` calls (lines 2458 and 17594).

- [ ] **Step 3: Expose AddRewardGrowthCommand and add DeleteCardCommand on AvatarScalingManagerViewModel**

In `AvatarScalingManagerViewModel.cs`, after the existing command properties (around line 180), add:
```csharp
public RelayCommand? AddRewardGrowthCommand => mainWindowViewModel?.AddRewardGrowthCommand;

public RelayCommand DeleteCardCommand { get; }
```

In the constructor (around line 61), add:
```csharp
DeleteCardCommand = new RelayCommand(DeleteCard);
```

Add the `DeleteCard` method:
```csharp
private void DeleteCard(object? parameter)
{
    if (parameter is not AvatarScalingSourceCardViewModel card)
    {
        return;
    }

    switch (card.Kind)
    {
        case AvatarScalingSourceKind.MasterReward:
            return;
        case AvatarScalingSourceKind.TwitchReward:
        case AvatarScalingSourceKind.TwitchEvent:
        case AvatarScalingSourceKind.SupporterGrowth:
            if (card.ScaleRule is { } scaleRule)
            {
                mainWindowViewModel?.DeleteAvatarScaleRuleByCard(scaleRule);
            }
            break;
        case AvatarScalingSourceKind.CashPayment:
            if (card.CashPaymentRule is { } cashRule)
            {
                mainWindowViewModel?.DeleteCashPaymentRuleByCard(cashRule);
            }
            break;
        case AvatarScalingSourceKind.PowerUp:
            if (card.PowerUpRule is { } powerUpRule)
            {
                mainWindowViewModel?.DeletePowerUpRuleByCard(powerUpRule);
            }
            break;
    }
}
```

- [ ] **Step 4: Add card-level delete helpers on MainWindowViewModel**

In `MainWindowViewModel.cs`, add these public methods after `RemoveSelectedAvatarScaleRule()` (around line 7086):
```csharp
public void DeleteAvatarScaleRuleByCard(AvatarScaleRule rule)
{
    RemoveAvatarScaleRuleLockoutReferencesToRule(rule.Id);
    var ownerSet = GetOwningAvatarScaleSet(rule);
    ownerSet?.ScaleRules.Remove(rule);
    if (ReferenceEquals(SelectedAvatarScaleRule, rule))
    {
        SelectedAvatarScaleRule = GetRememberedAvatarScaleRule();
    }
    QueueSave();
    QueueBridgeRefresh();
    AppendLog($"Removed avatar scale redeem '{rule.DisplayTitle}'.");
}

public void DeleteCashPaymentRuleByCard(CashPaymentRule rule)
{
    Settings.CashPaymentRules.Remove(rule);
    if (ReferenceEquals(SelectedCashPaymentRule, rule))
    {
        SelectedCashPaymentRule = GetRememberedCashPaymentRule();
    }
    QueueSave();
    QueueBridgeRefresh();
    AppendLog($"Removed cash payment rule '{rule.DisplayTitle}'.");
}

public void DeletePowerUpRuleByCard(PowerUpRule rule)
{
    Settings.PowerUpRules.Remove(rule);
    if (ReferenceEquals(SelectedPowerUpRule, rule))
    {
        SelectedPowerUpRule = GetRememberedPowerUpRule();
    }
    QueueSave();
    QueueBridgeRefresh();
    AppendLog($"Removed Power Up rule '{rule.DisplayTitle}'.");
}
```

- [ ] **Step 5: Build and test**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScaling"`
Expected: All pass (update the default-view test if it fails)

---

### Task 2: Localization keys

**Files:**
- Modify: all 14 `VrcTwitchOscBridge/Resources/Localization/*.extra.json`

Add these two keys to every file:
- `"Add Reward Growth"` — translated per language
- `"Delete"` — translated per language

en-US: `"Add Reward Growth"`, `"Delete"`
de-DE: `"Supporter-Wachstum hinzufügen"`, `"Löschen"`
es-ES: `"Añadir crecimiento de seguidores"`, `"Eliminar"`
fr-FR: `"Ajouter une croissance de supporters"`, `"Supprimer"`
it-IT: `"Aggiungi crescita supporter"`, `"Elimina"`
ja-JP: `"サポーターの成長を追加"`, `"削除"`
ko-KR: `"서포터 성장 추가"`, `"삭제"`
pl-PL: `"Dodaj wzrost wspierających"`, `"Usuń"`
pt-BR: `"Adicionar crescimento de apoiadores"`, `"Excluir"`
ru-RU: `"Добавить рост саппортеров"`, `"Удалить"`
sv-SE: `"Lägg till supporter-tillväxt"`, `"Ta bort"`
th-TH: `"เพิ่มการเติบโตของผู้สนับสนุน"`, `"ลบ"`
zh-CN: `"添加支持者成长"`, `"删除"`
zh-TW: `"新增支持者成長"`, `"刪除"`

---

### Task 3: XAML — move Add buttons, add Delete to cards, remove editor WrapPanel

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml`

- [ ] **Step 1: Add Delete button to SourceCardTemplate**

In the `SourceCardTemplate` (around line 738), replace the single Edit button with a horizontal panel containing Edit and Delete:

```xml
                    <StackPanel Margin="0,8,0,0" Orientation="Horizontal">
                        <Button Content="{loc:Translate 'Edit'}"
                                Command="{Binding DataContext.OpenEditorCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                CommandParameter="{Binding}"
                                Style="{StaticResource SecondaryButtonStyle}" />
                        <Button Margin="8,0,0,0"
                                Content="{loc:Translate 'Delete'}"
                                Command="{Binding DataContext.DeleteCardCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                CommandParameter="{Binding}"
                                Visibility="{Binding MasterReward, Converter={StaticResource InverseNullToVisibilityConverter}}"
                                Style="{StaticResource SecondaryButtonStyle}" />
                    </StackPanel>
```

Since there's no `InverseNullToVisibilityConverter`, use a `DataTrigger` on the button instead. Replace the Delete button with:
```xml
                        <Button Margin="8,0,0,0"
                                Content="{loc:Translate 'Delete'}"
                                Command="{Binding DataContext.DeleteCardCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                CommandParameter="{Binding}"
                                Style="{StaticResource SecondaryButtonStyle}">
                            <Button.Style>
                                <Style TargetType="Button" BasedOn="{StaticResource SecondaryButtonStyle}">
                                    <Setter Property="Visibility" Value="Visible" />
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding Kind}" Value="MasterReward">
                                            <Setter Property="Visibility" Value="Collapsed" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </Button.Style>
                        </Button>
```

- [ ] **Step 2: Move Add Scale Redeem to Child Scale Rewards section header**

In the Child Scale Rewards left column (around line 972), add an Add button next to the section title:

```xml
                                <StackPanel>
                                    <DockPanel LastChildFill="True">
                                        <Button DockPanel.Dock="Right"
                                                Content="{loc:Translate 'Add Scale Redeem'}"
                                                Command="{Binding AddAvatarScaleRuleCommand}"
                                                Visibility="{Binding HasSelectedAvatarScaleSet, Converter={StaticResource BoolToVisibilityConverter}}"
                                                Style="{StaticResource SecondaryButtonStyle}"
                                                Margin="8,0,0,0" />
                                        <TextBlock Text="{loc:Translate 'Child Scale Rewards'}" FontWeight="Bold" FontSize="16" />
                                    </DockPanel>
                                    <TextBlock Margin="0,4,0,10"
                                               Text="{loc:Translate 'Channel point rewards and chat command fallbacks that change avatar height.'}"
                                               Foreground="{DynamicResource TitleBarSubTextBrush}" />
```

- [ ] **Step 3: Add "Add Reward Growth" button to Pay System Rewards section header**

In the right column StackPanel (around line 1027), add the button:

```xml
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
                                           Text="{loc:Translate 'Supporter Growth, Cash Payments & Power Ups'}"
                                           Foreground="{DynamicResource TitleBarSubTextBrush}" />
```

- [ ] **Step 4: Remove the top "Add Scale Redeem" button**

Remove the Button at lines 914-919 (the one in the "Twitch Reward Scaling" header Grid). The header becomes just the title and subtitle without a button.

- [ ] **Step 5: Remove the editor action WrapPanel**

Remove the entire WrapPanel at lines 1121-1138 (Add Scale Set, Delete Scale Set, Add Scale Redeem, Delete Scale Redeem, Test Scale).

- [ ] **Step 6: Build and test**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScaling"`

Update any failing tests that reference the removed WrapPanel or the old default view.
