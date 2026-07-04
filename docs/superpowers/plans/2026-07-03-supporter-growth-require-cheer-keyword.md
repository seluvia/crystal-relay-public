# Supporter Growth Require Cheer Keyword Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-rule `SupporterGrowthRequireCheerKeyword` toggle so bits only change height when the cheer message contains the grow or shrink keyword. Bits without a keyword still add paid time but don't change height.

**Architecture:** New bool property on `AvatarScaleRule` → plumbed through the snapshot record, persistence DTO, and both directions of SettingsStore mapping. Runtime changes in `BridgeCoordinator` extend `TryResolveSupporterGrowthBitsHeightDirection` with an `anyKeywordMatched` out parameter, update the event matcher to allow time-only bits, and update the executor to skip height but proceed to time when the keyword is required but missing. UI adds a checkbox + helper in the Cheer Keywords section. 14 localization files get two new keys.

**Tech Stack:** C# / WPF / .NET 10, Crystal Relay `VrcTwitchOscBridge` project.

---

## File Structure

- **Modify:** `VrcTwitchOscBridge\Models\AvatarScaleRule.cs` — new bool backing field + property
- **Modify:** `VrcTwitchOscBridge\Services\BridgeRuntimeConfiguration.cs` — snapshot record field + mapping
- **Modify:** `VrcTwitchOscBridge\Services\SettingsStore.cs` — DTO field + both mapping directions
- **Modify:** `VrcTwitchOscBridge\Services\BridgeCoordinator.cs` — `TryResolveSupporterGrowthBitsHeightDirection` signature, `SupporterGrowthEventMatches`, `ExecuteSupporterGrowthAvatarScaleRuleAsync`
- **Modify:** `VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml` — checkbox + helper in Cheer Keywords section
- **Modify:** 14 `VrcTwitchOscBridge\Resources\Localization\*.extra.json` files — two new keys

---

## Task 1: Add `SupporterGrowthRequireCheerKeyword` to the model

**Files:**
- Modify: `VrcTwitchOscBridge\Models\AvatarScaleRule.cs`

- [ ] **Step 1: Add the backing field**

In `AvatarScaleRule.cs`, find line 345:
```csharp
    private bool supporterGrowthAllowRewardScaleOverlay = true;
```
Insert immediately after it:
```csharp
    private bool supporterGrowthRequireCheerKeyword;
```

- [ ] **Step 2: Add the property**

Find the `SupporterGrowthAllowRewardScaleOverlay` property (lines 857-861):
```csharp
    public bool SupporterGrowthAllowRewardScaleOverlay
    {
        get => supporterGrowthAllowRewardScaleOverlay;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthAllowRewardScaleOverlay, value);
    }
```
Insert immediately after it:
```csharp

    public bool SupporterGrowthRequireCheerKeyword
    {
        get => supporterGrowthRequireCheerKeyword;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthRequireCheerKeyword, value);
    }
```

- [ ] **Step 3: Build to verify the model compiles**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds. The new property is not yet referenced by runtime/UI, so no errors.

- [ ] **Step 4: Commit**

```
git add VrcTwitchOscBridge/Models/AvatarScaleRule.cs
git commit -m "Add SupporterGrowthRequireCheerKeyword property to AvatarScaleRule"
```

---

## Task 2: Plumb the property through snapshot and persistence

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeRuntimeConfiguration.cs`
- Modify: `VrcTwitchOscBridge\Services\SettingsStore.cs`

- [ ] **Step 1: Add field to `AvatarScaleRuleSnapshot` record**

In `BridgeRuntimeConfiguration.cs`, find line 243:
```csharp
    bool SupporterGrowthAllowRewardScaleOverlay,
```
Insert immediately after it:
```csharp
    bool SupporterGrowthRequireCheerKeyword,
```

- [ ] **Step 2: Add mapping in `ToAvatarScaleRuleSnapshot`**

In `BridgeRuntimeConfiguration.cs`, find line 1353:
```csharp
            rule.SupporterGrowthAllowRewardScaleOverlay,
```
Insert immediately after it:
```csharp
            rule.SupporterGrowthRequireCheerKeyword,
```

- [ ] **Step 3: Add field to `PersistedAvatarScaleRule` DTO**

In `SettingsStore.cs`, find line 3827:
```csharp
        public bool SupporterGrowthAllowRewardScaleOverlay { get; set; } = true;
```
Insert immediately after it:
```csharp

        public bool SupporterGrowthRequireCheerKeyword { get; set; }
```

- [ ] **Step 4: Add model→DTO mapping**

In `SettingsStore.cs`, find line 2010 (inside the model→DTO mapping method):
```csharp
            SupporterGrowthAllowRewardScaleOverlay = rule.SupporterGrowthAllowRewardScaleOverlay,
```
Insert immediately after it:
```csharp
            SupporterGrowthRequireCheerKeyword = rule.SupporterGrowthRequireCheerKeyword,
```

- [ ] **Step 5: Add DTO→model mapping**

In `SettingsStore.cs`, find line 2116 (inside the DTO→model mapping method):
```csharp
            SupporterGrowthAllowRewardScaleOverlay = rule.SupporterGrowthAllowRewardScaleOverlay,
```
Insert immediately after it:
```csharp
            SupporterGrowthRequireCheerKeyword = rule.SupporterGrowthRequireCheerKeyword,
```

- [ ] **Step 6: Build to verify**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds. The record constructor now has the new parameter in the right position.

- [ ] **Step 7: Commit**

```
git add VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs VrcTwitchOscBridge/Services/SettingsStore.cs
git commit -m "Plumb SupporterGrowthRequireCheerKeyword through snapshot and persistence"
```

---

## Task 3: Update runtime logic in `BridgeCoordinator`

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs`

- [ ] **Step 1: Extend `TryResolveSupporterGrowthBitsHeightDirection` with `anyKeywordMatched` out parameter**

Find the method at line ~6039:
```csharp
    private static bool TryResolveSupporterGrowthBitsHeightDirection(
        AvatarScaleRuleSnapshot rule,
        string messageText,
        out int direction,
        out string? diagnostic)
    {
        direction = 1;
        diagnostic = null;

        var cheerText = ExtractBitsOutfitChoiceText(messageText);
        var growMatched = ContainsSupporterGrowthBitsKeyword(cheerText, rule.SupporterGrowthGrowKeyword);
        var shrinkMatched = ContainsSupporterGrowthBitsKeyword(cheerText, rule.SupporterGrowthShrinkKeyword);
        if (growMatched && shrinkMatched)
        {
            diagnostic = TF(
                "Avatar scale '{0}' skipped because cheer text matched both Supporter Growth keywords ('{1}' and '{2}').",
                rule.Name,
                rule.SupporterGrowthGrowKeyword,
                rule.SupporterGrowthShrinkKeyword);
            return false;
        }

        direction = shrinkMatched ? -1 : 1;
        return true;
    }
```

Replace with:
```csharp
    private static bool TryResolveSupporterGrowthBitsHeightDirection(
        AvatarScaleRuleSnapshot rule,
        string messageText,
        out int direction,
        out string? diagnostic,
        out bool anyKeywordMatched)
    {
        direction = 1;
        diagnostic = null;
        anyKeywordMatched = false;

        var cheerText = ExtractBitsOutfitChoiceText(messageText);
        var growMatched = ContainsSupporterGrowthBitsKeyword(cheerText, rule.SupporterGrowthGrowKeyword);
        var shrinkMatched = ContainsSupporterGrowthBitsKeyword(cheerText, rule.SupporterGrowthShrinkKeyword);
        anyKeywordMatched = growMatched || shrinkMatched;
        if (growMatched && shrinkMatched)
        {
            diagnostic = TF(
                "Avatar scale '{0}' skipped because cheer text matched both Supporter Growth keywords ('{1}' and '{2}').",
                rule.Name,
                rule.SupporterGrowthGrowKeyword,
                rule.SupporterGrowthShrinkKeyword);
            return false;
        }

        direction = shrinkMatched ? -1 : 1;
        return true;
    }
```

- [ ] **Step 2: Update the call site in `SupporterGrowthEventMatches` (line ~6579)**

Find:
```csharp
    private static bool SupporterGrowthEventMatches(
        AvatarScaleRuleSnapshot rule,
        UniversalIncomingEvent incomingEvent)
    {
        var bitHeightDirection = 1;
        if (incomingEvent.TriggerType == UniversalTriggerType.Bits
            && !TryResolveSupporterGrowthBitsHeightDirection(
                rule,
                incomingEvent.ChatMessageText,
                out bitHeightDirection,
                out _))
        {
            return false;
        }

        return GetSupporterGrowthHeightAdd(rule, incomingEvent, isTest: false, bitHeightDirection) != 0;
    }
```

Replace with:
```csharp
    private static bool SupporterGrowthEventMatches(
        AvatarScaleRuleSnapshot rule,
        UniversalIncomingEvent incomingEvent)
    {
        var bitHeightDirection = 1;
        var anyKeywordMatched = true;
        if (incomingEvent.TriggerType == UniversalTriggerType.Bits
            && !TryResolveSupporterGrowthBitsHeightDirection(
                rule,
                incomingEvent.ChatMessageText,
                out bitHeightDirection,
                out _,
                out anyKeywordMatched))
        {
            return false;
        }

        if (rule.SupporterGrowthRequireCheerKeyword
            && incomingEvent.TriggerType == UniversalTriggerType.Bits
            && !anyKeywordMatched)
        {
            return GetSupporterGrowthAddedTimeSeconds(rule, incomingEvent, isTest: false) > 0;
        }

        return GetSupporterGrowthHeightAdd(rule, incomingEvent, isTest: false, bitHeightDirection) != 0;
    }
```

- [ ] **Step 3: Update the call site in `ExecuteSupporterGrowthAvatarScaleRuleAsync` (line ~5656)**

Find the block at lines 5656-5674:
```csharp
        var bitHeightDirection = 1;
        if (!isTest
            && incomingEvent.TriggerType == UniversalTriggerType.Bits
            && !TryResolveSupporterGrowthBitsHeightDirection(
                rule,
                incomingEvent.ChatMessageText,
                out bitHeightDirection,
                out var directionDiagnostic))
        {
            WriteLog(directionDiagnostic ?? TF("Avatar scale '{0}' skipped because the cheer text matched both grow and shrink keywords.", rule.Name));
            return;
        }

        var addedHeight = GetSupporterGrowthHeightAdd(rule, incomingEvent, isTest, bitHeightDirection);
        if (addedHeight == 0)
        {
            WriteLog($"Avatar scale '{rule.Name}' skipped because this supporter event does not match a configured tier or bits range.");
            return;
        }
```

Replace with:
```csharp
        var bitHeightDirection = 1;
        var anyKeywordMatched = true;
        if (!isTest
            && incomingEvent.TriggerType == UniversalTriggerType.Bits
            && !TryResolveSupporterGrowthBitsHeightDirection(
                rule,
                incomingEvent.ChatMessageText,
                out bitHeightDirection,
                out var directionDiagnostic,
                out anyKeywordMatched))
        {
            WriteLog(directionDiagnostic ?? TF("Avatar scale '{0}' skipped because the cheer text matched both grow and shrink keywords.", rule.Name));
            return;
        }

        var keywordRequiredButMissing = rule.SupporterGrowthRequireCheerKeyword
            && !isTest
            && incomingEvent.TriggerType == UniversalTriggerType.Bits
            && !anyKeywordMatched;

        var addedHeight = keywordRequiredButMissing
            ? 0
            : GetSupporterGrowthHeightAdd(rule, incomingEvent, isTest, bitHeightDirection);
        if (addedHeight == 0 && !keywordRequiredButMissing)
        {
            WriteLog($"Avatar scale '{rule.Name}' skipped because this supporter event does not match a configured tier or bits range.");
            return;
        }
```

- [ ] **Step 4: Update the success log line to reflect time-only when keyword required but missing**

Find the log line at ~5800:
```csharp
            WriteLog($"{incomingEvent.UserDisplayName} changed supporter growth '{rule.Name}' by {addedHeight:+0.###;-0.###;0}m and added {DescribeDuration(addedPaidSeconds)} for a target of {targetHeight:0.###}m. Paid time remaining: {DescribeDuration(remainingPaidSeconds)}.");
```

Replace with:
```csharp
            WriteLog(keywordRequiredButMissing
                ? $"{incomingEvent.UserDisplayName} added {DescribeDuration(addedPaidSeconds)} to supporter growth '{rule.Name}' without changing height (no grow/shrink keyword). Paid time remaining: {DescribeDuration(remainingPaidSeconds)}."
                : $"{incomingEvent.UserDisplayName} changed supporter growth '{rule.Name}' by {addedHeight:+0.###;-0.###;0}m and added {DescribeDuration(addedPaidSeconds)} for a target of {targetHeight:0.###}m. Paid time remaining: {DescribeDuration(remainingPaidSeconds)}.");
```

- [ ] **Step 5: Build to verify**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds. All three call sites of `TryResolveSupporterGrowthBitsHeightDirection` now pass the new `out anyKeywordMatched` argument.

- [ ] **Step 6: Commit**

```
git add VrcTwitchOscBridge/Services/BridgeCoordinator.cs
git commit -m "Gate Supporter Growth bits height on cheer keyword when toggle is on"
```

---

## Task 4: Add the checkbox to the UI

**Files:**
- Modify: `VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml`

- [ ] **Step 1: Locate the Cheer Keywords section**

Grep for `Supporter Growth Cheer Keywords` in `AvatarScalingManagerWindow.xaml` to find the section. Read ~30 lines around it to see the Grow/Shrink keyword grid and the "Subscription Growth" subheader that follows.

- [ ] **Step 2: Insert the checkbox + helper after the Grow/Shrink keyword grid**

Find the closing `</UniformGrid>` of the Grow/Shrink keyword 2-column grid, immediately followed by the "Subscription Growth" subheader `<TextBlock Margin="0,14,0,0" Text="{loc:Translate 'Subscription Growth'}" ...>`.

Insert between them:
```xml
                                                        <CheckBox Margin="0,10,0,0"
                                                                  Content="{loc:Translate 'Require grow or shrink keyword for height changes'}"
                                                                  IsChecked="{Binding SupporterGrowthRequireCheerKeyword, UpdateSourceTrigger=PropertyChanged}" />
                                                        <TextBlock Margin="26,6,0,0"
                                                                   Text="{loc:Translate 'When enabled, bits only change height if the cheer message says your grow or shrink keyword. Bits without a keyword still add paid time, but do not grow or shrink the avatar.'}"
                                                                   Foreground="{DynamicResource TitleBarSubTextBrush}"
                                                                   TextWrapping="Wrap" />
```

The `oldString` for the edit should capture the closing `</UniformGrid>` of the Grow/Shrink grid plus the opening of the "Subscription Growth" TextBlock to make the insertion point unique. The `newString` is the same content with the two new elements inserted between.

- [ ] **Step 3: Build to verify**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds. The `loc:Translate` keys do not exist yet — WPF XAML compile does not fail on missing localization keys (falls back to key text at runtime), so the build will pass. The keys are added in Task 5.

- [ ] **Step 4: Commit**

```
git add VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml
git commit -m "Add Require cheer keyword checkbox to Supporter Growth UI"
```

---

## Task 5: Add localization keys to all 14 language files

**Files:**
- Modify: 14 `VrcTwitchOscBridge\Resources\Localization\*.extra.json` files

- [ ] **Step 1: Add the two new keys to `en-US.extra.json`**

Find a line near the existing Supporter Growth cheer keyword keys. Insert after the existing `"Shrink Keyword"` line (or any nearby Supporter Growth key):
```json
  "Require grow or shrink keyword for height changes": "Require grow or shrink keyword for height changes",
  "When enabled, bits only change height if the cheer message says your grow or shrink keyword. Bits without a keyword still add paid time, but do not grow or shrink the avatar.": "When enabled, bits only change height if the cheer message says your grow or shrink keyword. Bits without a keyword still add paid time, but do not grow or shrink the avatar.",
```

- [ ] **Step 2: Add translations to all 13 non-English `.extra.json` files**

Add the same two keys with natural translations to each file. Keep `Bits` in English per project rules. Use informal register (`du`/`tú`/`tu`). The word `grow` and `shrink` refer to the keyword the streamer configures — keep them as-is since they're keyword examples.

**de-DE:**
```json
  "Require grow or shrink keyword for height changes": "grow- oder shrink-Schlüsselwort für Größenänderung verlangen",
  "When enabled, bits only change height if the cheer message says your grow or shrink keyword. Bits without a keyword still add paid time, but do not grow or shrink the avatar.": "Wenn aktiviert, ändern Bits die Größe nur, wenn die Cheer-Nachricht dein grow- oder shrink-Schlüsselwort enthält. Bits ohne Schlüsselwort addieren trotzdem bezahlte Zeit, ändern die Avatar-Größe aber nicht.",
```

**fr-FR:**
```json
  "Require grow or shrink keyword for height changes": "Exiger le mot-clé grow ou shrink pour changer la taille",
  "When enabled, bits only change height if the cheer message says your grow or shrink keyword. Bits without a keyword still add paid time, but do not grow or shrink the avatar.": "Quand activé, les Bits ne changent la taille que si le message de cheer contient ton mot-clé grow ou shrink. Les Bits sans mot-clé ajoutent quand même du temps payé, mais ne grandissent ni ne rétrécissent l'avatar.",
```

**es-ES:**
```json
  "Require grow or shrink keyword for height changes": "Exigir la palabra clave grow o shrink para cambiar la altura",
  "When enabled, bits only change height if the cheer message says your grow or shrink keyword. Bits without a keyword still add paid time, but do not grow or shrink the avatar.": "Cuando está activado, los Bits solo cambian la altura si el mensaje de cheer contiene tu palabra clave grow o shrink. Los Bits sin palabra clave siguen sumando tiempo de pago, pero no hacen crecer ni encoger el avatar.",
```

**it-IT:**
```json
  "Require grow or shrink keyword for height changes": "Richiedi la parola chiave grow o shrink per cambiare l'altezza",
  "When enabled, bits only change height if the cheer message says your grow or shrink keyword. Bits without a keyword still add paid time, but do not grow or shrink the avatar.": "Quando attivato, i Bits cambiano altezza solo se il messaggio di cheer contiene la parola chiave grow o shrink. I Bits senza parola chiave aggiungono comunque tempo a pagamento, ma non fanno crescere né rimpicciolire l'avatar.",
```

**sv-SE:**
```json
  "Require grow or shrink keyword for height changes": "Kräv grow- eller shrink-nyckelord för höjdändring",
  "When enabled, bits only change height if the cheer message says your grow or shrink keyword. Bits without a keyword still add paid time, but do not grow or shrink the avatar.": "När aktiverat ändrar Bits höjd bara om cheer-meddelandet innehåller ditt grow- eller shrink-nyckelord. Bits utan nyckelord lägger fortfarande till betald tid, men gör inte avataren större eller mindre.",
```

**ru-RU:**
```json
  "Require grow or shrink keyword for height changes": "Требовать ключевое слово grow или shrink для изменения роста",
  "When enabled, bits only change height if the cheer message says your grow or shrink keyword. Bits without a keyword still add paid time, but do not grow or shrink the avatar.": "Если включено, Bits меняют рост только если сообщение cheer содержит ваше ключевое слово grow или shrink. Bits без ключевого слова всё равно добавляют платное время, но не увеличивают и не уменьшают аватар.",
```

**pt-BR:**
```json
  "Require grow or shrink keyword for height changes": "Exigir a palavra-chave grow ou shrink para mudar a altura",
  "When enabled, bits only change height if the cheer message says your grow or shrink keyword. Bits without a keyword still add paid time, but do not grow or shrink the avatar.": "Quando ativado, os Bits só mudam a altura se a mensagem de cheer contiver sua palavra-chave grow ou shrink. Bits sem palavra-chave ainda adicionam tempo pago, mas não fazem o avatar crescer nem encolher.",
```

**pl-PL:**
```json
  "Require grow or shrink keyword for height changes": "Wymagaj słowa kluczowego grow lub shrink do zmiany wysokości",
  "When enabled, bits only change height if the cheer message says your grow or shrink keyword. Bits without a keyword still add paid time, but do not grow or shrink the avatar.": "Gdy włączone, Bits zmieniają wysokość tylko, jeśli wiadomość cheer zawiera Twoje słowo kluczowe grow lub shrink. Bits bez słowa kluczowego nadal dodają płatny czas, ale nie powiększają ani nie pomniejszają awatara.",
```

**ko-KR:**
```json
  "Require grow or shrink keyword for height changes": "키워드 grow 또는 shrink를 입력해야 크기가 변합니다",
  "When enabled, bits only change height if the cheer message says your grow or shrink keyword. Bits without a keyword still add paid time, but do not grow or shrink the avatar.": "활성화하면 Bits가 cheer 메시지에 grow 또는 shrink 키워드가 있을 때만 크기를 변화시킵니다. 키워드 없는 Bits는 여전히 유료 시간을 추가하지만, 아바타를 키우거나 줄이지는 않습니다.",
```

**ja-JP:**
```json
  "Require grow or shrink keyword for height changes": "サイズ変更に grow または shrink キーワードを必須にする",
  "When enabled, bits only change height if the cheer message says your grow or shrink keyword. Bits without a keyword still add paid time, but do not grow or shrink the avatar.": "有効にすると、cheer メッセージに grow または shrink キーワードが含まれる場合のみ Bits が高さを変更します。キーワードのない Bits は有料時間を追加しますが、アバターのサイズは変化しません。",
```

**zh-CN:**
```json
  "Require grow or shrink keyword for height changes": "需要 grow 或 shrink 关键词才能改变身高",
  "When enabled, bits only change height if the cheer message says your grow or shrink keyword. Bits without a keyword still add paid time, but do not grow or shrink the avatar.": "启用后，只有在 cheer 消息中包含你的 grow 或 shrink 关键词时，Bits 才会改变身高。没有关键词的 Bits 仍然会增加付费时间，但不会让 avatar 变大或变小。",
```

**zh-TW:**
```json
  "Require grow or shrink keyword for height changes": "需要 grow 或 shrink 關鍵字才能改變身高",
  "When enabled, bits only change height if the cheer message says your grow or shrink keyword. Bits without a keyword still add paid time, but do not grow or shrink the avatar.": "啟用後，只有在 cheer 訊息中包含你的 grow 或 shrink 關鍵字時，Bits 才會改變身高。沒有關鍵字的 Bits 仍然會增加付費時間，但不會讓 avatar 變大或變小。",
```

**th-TH:**
```json
  "Require grow or shrink keyword for height changes": "ต้องพิมพ์คำว่า grow หรือ shrink เพื่อเปลี่ยนความสูง",
  "When enabled, bits only change height if the cheer message says your grow or shrink keyword. Bits without a keyword still add paid time, but do not grow or shrink the avatar.": "เปิดใช้งานแล้ว Bits จะเปลี่ยนความสูงเฉพาะเมื่อข้อความ cheer มีคำว่า grow หรือ shrink ที่คุณตั้งไว้ Bits ที่ไม่มีคำเหล่านี้ยังคงเพิ่มเวลาจ่ายเงิน แต่ไม่ทำให้อวตารใหญ่หรือเล็กลง",
```

- [ ] **Step 3: Run the localization audit**

Run:
```
dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj"
```
Expected: no new missing keys for the two new keys. Pre-existing audit warnings (from other windows) are unchanged.

- [ ] **Step 4: Commit**

```
git add VrcTwitchOscBridge/Resources/Localization/*.extra.json
git commit -m "Add localized keys for Supporter Growth require cheer keyword toggle"
```

---

## Task 6: Build, launch, and manually verify

**Files:**
- No file changes. Manual verification only.

- [ ] **Step 1: Build the Debug configuration**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --configuration Debug
```
Expected: 0 errors.

- [ ] **Step 2: Launch the debug build**

Run:
```
& "E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"
```

- [ ] **Step 3: Verify the checkbox appears**

Open Avatar Scaling Manager. Add or select a Supporter Growth card. Scroll to the Cheer Keywords section. Confirm the "Require grow or shrink keyword for height changes" checkbox is visible below the Grow/Shrink keyword fields, with the helper text below it.

- [ ] **Step 4: Verify toggle persistence**

Toggle the checkbox on. Close the Avatar Scaling Manager. Reopen it and select the same card. Confirm the checkbox is still on. Toggle it off, close, reopen — confirm it's off.

- [ ] **Step 5: Close the app**

Close Crystal Relay.
