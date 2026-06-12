# Power Up Active Time + Bot Message Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Active Time (seconds) and Bot Message fields to the Power Up editor so users can configure how long a Power Up action stays active and what chatbox message it sends.

**Architecture:** Add a new nested panel in the existing `PowerUpRule` XAML DataTemplate, binding to existing `ActionRule.DurationSeconds` and `ActionRule.BotMessageTemplate` properties. No model or ViewModel changes needed. Add two new localization keys to all 14 `.extra.json` files.

**Tech Stack:** C#, WPF/XAML, .NET 10

---

## File Map

| File | Responsibility |
|------|---------------|
| `VrcTwitchOscBridge/MainWindow.xaml` | Add "Power Up Action Settings" panel with Active Time + Bot Message fields |
| `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json` | Add 2 new English localization keys |
| `VrcTwitchOscBridge/Resources/Localization/{lang}.extra.json` (12 files) | Add 2 placeholder/translated keys per file |
| `VrcTwitchOscBridge/Resources/Localization/zh-CN.extra.json` | Add 2 translated keys |
| `VrcTwitchOscBridge/Resources/Localization/zh-TW.extra.json` | Add 2 translated keys |
| `VrcTwitchOscBridge/Resources/Localization/ja-JP.extra.json` | Add 2 translated keys |
| `VrcTwitchOscBridge/Resources/Localization/ko-KR.extra.json` | Add 2 translated keys |

---

### Task 1: Add Power Up Action Settings panel to MainWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml:5482-5484`

The insertion point is between the closing `</Border>` of the "Power Up Rules" panel (line 5482) and the `<WrapPanel>` for the "Test Power Up" button (line 5484).

- [ ] **Step 1: Read the current XAML at the insertion point**

Read `VrcTwitchOscBridge/MainWindow.xaml` lines 5478-5490 to confirm the exact insertion point.

- [ ] **Step 2: Insert the new panel**

Insert the following XAML between the `</Border>` (end of "Power Up Rules" panel) and the `<WrapPanel>` (start of "Test Power Up" button):

```xml
                                                <Border Margin="0,14,0,0"
                                                        Background="{DynamicResource NestedPanelBrush}"
                                                        BorderBrush="{DynamicResource HighlightBorderBrush}"
                                                        BorderThickness="1"
                                                        CornerRadius="16"
                                                        Padding="14">
                                                    <Border.Style>
                                                        <Style TargetType="Border">
                                                            <Setter Property="Visibility" Value="Visible" />
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding UsesAvatarScaling}" Value="True">
                                                                    <Setter Property="Visibility" Value="Collapsed" />
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </Border.Style>
                                                    <StackPanel>
                                                        <TextBlock Text="{loc:Translate 'Power Up Action Settings'}"
                                                                   Foreground="{DynamicResource TextBrush}"
                                                                   FontWeight="SemiBold"
                                                                   FontSize="17" />
                                                        <UniformGrid Margin="0,12,0,0"
                                                                     Columns="2">
                                                            <StackPanel Margin="0,0,10,0">
                                                                <DockPanel LastChildFill="False">
                                                                    <TextBlock Text="{loc:Translate 'Active Time (seconds)'}"
                                                                               Foreground="{DynamicResource TextBrush}"
                                                                               FontWeight="SemiBold" />
                                                                    <Button Style="{StaticResource HelpIconButtonStyle}"
                                                                            Width="24"
                                                                            Height="24"
                                                                            MinWidth="24"
                                                                            MinHeight="24"
                                                                            Click="OnHelpButtonClicked"
                                                                            CommandParameter="Active Time"
                                                                            Tag="{Binding ActionRule.DurationHelpText}" />
                                                                </DockPanel>
                                                                <TextBox Text="{Binding ActionRule.DurationSeconds, UpdateSourceTrigger=PropertyChanged}" />
                                                            </StackPanel>
                                                            <StackPanel Margin="10,0,0,0">
                                                                <DockPanel LastChildFill="False">
                                                                    <TextBlock Text="{loc:Translate 'Bot Message'}"
                                                                               Foreground="{DynamicResource TextBrush}"
                                                                               FontWeight="SemiBold" />
                                                                    <Button Style="{StaticResource HelpIconButtonStyle}"
                                                                            Width="24"
                                                                            Height="24"
                                                                            MinWidth="24"
                                                                            MinHeight="24"
                                                                            Click="OnHelpButtonClicked"
                                                                            CommandParameter="Bot Message"
                                                                            Tag="{loc:Translate 'Crystal Relay sends this chatbox message when the Power Up fires. Use {user} for the viewer name, {rule} for the rule name, {duration} for active time, and {cooldown} for cooldown.'}" />
                                                                </DockPanel>
                                                                <TextBox Text="{Binding ActionRule.BotMessageTemplate, UpdateSourceTrigger=PropertyChanged}"
                                                                         AcceptsReturn="True"
                                                                         Height="70"
                                                                         TextWrapping="Wrap" />
                                                            </StackPanel>
                                                        </UniformGrid>
                                                    </StackPanel>
                                                </Border>
```

- [ ] **Step 3: Build to verify XAML compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds (localization keys may show as raw key text until Task 2)

---

### Task 2: Add localization keys to en-US.extra.json

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json`

- [ ] **Step 1: Add "Power Up Action Settings" key**

Find the line:
```
  "Power Up Action": "Power Up Action",
```
Add after it:
```
  "Power Up Action Settings": "Power Up Action Settings",
```

- [ ] **Step 2: Add Bot Message help text key**

Find the line:
```
  "Power Up Title": "Power Up Title",
```
Add after it:
```
  "Crystal Relay sends this chatbox message when the Power Up fires. Use {user} for the viewer name, {rule} for the rule name, {duration} for active time, and {cooldown} for cooldown.": "Crystal Relay sends this chatbox message when the Power Up fires. Use {user} for the viewer name, {rule} for the rule name, {duration} for active time, and {cooldown} for cooldown.",
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

---

### Task 3: Add localization keys to all other .extra.json files

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/de-DE.extra.json`
- Modify: `VrcTwitchOscBridge/Resources/Localization/es-ES.extra.json`
- Modify: `VrcTwitchOscBridge/Resources/Localization/fr-FR.extra.json`
- Modify: `VrcTwitchOscBridge/Resources/Localization/it-IT.extra.json`
- Modify: `VrcTwitchOscBridge/Resources/Localization/ja-JP.extra.json`
- Modify: `VrcTwitchOscBridge/Resources/Localization/ko-KR.extra.json`
- Modify: `VrcTwitchOscBridge/Resources/Localization/pl-PL.extra.json`
- Modify: `VrcTwitchOscBridge/Resources/Localization/pt-BR.extra.json`
- Modify: `VrcTwitchOscBridge/Resources/Localization/ru-RU.extra.json`
- Modify: `VrcTwitchOscBridge/Resources/Localization/sv-SE.extra.json`
- Modify: `VrcTwitchOscBridge/Resources/Localization/th-TH.extra.json`
- Modify: `VrcTwitchOscBridge/Resources/Localization/zh-CN.extra.json`
- Modify: `VrcTwitchOscBridge/Resources/Localization/zh-TW.extra.json`

For each file, add two keys. The pattern is the same for all files:

**Step A:** Find the `"Power Up Action"` line and add `"Power Up Action Settings"` after it.

**Step B:** Find the `"Power Up Title"` line and add the Bot Message help text after it.

Real translations for CJK languages:

**zh-CN:**
```
  "Power Up Action Settings": "Power Up 动作设置",
```
```
  "Crystal Relay sends this chatbox message when the Power Up fires. Use {user} for the viewer name, {rule} for the rule name, {duration} for active time, and {cooldown} for cooldown.": "Crystal Relay 在 Power Up 触发时发送此聊天框消息。使用 {user} 表示观众名称，{rule} 表示规则名称，{duration} 表示持续时间，{cooldown} 表示冷却时间。",
```

**zh-TW:**
```
  "Power Up Action Settings": "Power Up 動作設定",
```
```
  "Crystal Relay sends this chatbox message when the Power Up fires. Use {user} for the viewer name, {rule} for the rule name, {duration} for active time, and {cooldown} for cooldown.": "Crystal Relay 在 Power Up 觸發時傳送此聊天框訊息。使用 {user} 表示觀眾名稱，{rule} 表示規則名稱，{duration} 表示持續時間，{cooldown} 表示冷卻時間。",
```

**ja-JP:**
```
  "Power Up Action Settings": "Power Upアクション設定",
```
```
  "Crystal Relay sends this chatbox message when the Power Up fires. Use {user} for the viewer name, {rule} for the rule name, {duration} for active time, and {cooldown} for cooldown.": "Crystal Relay が Power Up 発動時にこのチャットボックスメッセージを送信します。{user} は視聴者名、{rule} はルール名、{duration} はアクティブ時間、{cooldown} はクールダウンを表します。",
```

**ko-KR:**
```
  "Power Up Action Settings": "Power Up 액션 설정",
```
```
  "Crystal Relay sends this chatbox message when the Power Up fires. Use {user} for the viewer name, {rule} for the rule name, {duration} for active time, and {cooldown} for cooldown.": "Crystal Relay가 Power Up 발동 시 이 채팅박스 메시지를 전송합니다. {user}는 시청자 이름, {rule}은 규칙 이름, {duration}은 활성 시간, {cooldown}은 쿨다운을 나타냅니다.",
```

Placeholder format for all other languages (de-DE, es-ES, fr-FR, it-IT, pl-PL, pt-BR, ru-RU, sv-SE, th-TH):
```
  "Power Up Action Settings": "[lang] Power Up Action Settings",
```
```
  "Crystal Relay sends this chatbox message when the Power Up fires. Use {user} for the viewer name, {rule} for the rule name, {duration} for active time, and {cooldown} for cooldown.": "[lang] Crystal Relay sends this chatbox message when the Power Up fires. Use {user} for the viewer name, {rule} for the rule name, {duration} for active time, and {cooldown} for cooldown.",
```

- [ ] **Step 1: Add keys to de-DE.extra.json**
- [ ] **Step 2: Add keys to es-ES.extra.json**
- [ ] **Step 3: Add keys to fr-FR.extra.json**
- [ ] **Step 4: Add keys to it-IT.extra.json**
- [ ] **Step 5: Add keys to ja-JP.extra.json**
- [ ] **Step 6: Add keys to ko-KR.extra.json**
- [ ] **Step 7: Add keys to pl-PL.extra.json**
- [ ] **Step 8: Add keys to pt-BR.extra.json**
- [ ] **Step 9: Add keys to ru-RU.extra.json**
- [ ] **Step 10: Add keys to sv-SE.extra.json**
- [ ] **Step 11: Add keys to th-TH.extra.json**
- [ ] **Step 12: Add keys to zh-CN.extra.json**
- [ ] **Step 13: Add keys to zh-TW.extra.json**

- [ ] **Step 14: Build to verify all localization keys resolve**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

---

### Task 4: Final verification

- [ ] **Step 1: Run the localization audit**

Run the localization audit to verify no empty values and all keys are present across all language files.

- [ ] **Step 2: Visual verification checklist**

After launching the app:
1. Navigate to the Power Up tab
2. Select or create a Power Up rule with Action Kind = "Trigger Action"
3. Verify "Power Up Action Settings" panel appears after "Power Up Rules"
4. Verify Active Time field shows default value (10) and is editable
5. Verify Bot Message field shows default template and is editable
6. Change Action Kind to "Avatar Scaling"
7. Verify the "Power Up Action Settings" panel hides
8. Change Action Kind back to "Trigger Action"
9. Verify the panel reappears with the values preserved
