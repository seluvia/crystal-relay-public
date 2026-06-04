# Avatar Picker Phase 3 — Multi-Select, Keyboard Nav, Virtualization & Roulette Unification

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add multi-select checkboxes, keyboard navigation, and virtualization to AvatarPickerWindow, and unify the Avatar Roulette pool picker into it.

**Architecture:** The existing `AvatarPickerViewModel` multi-select infrastructure (`isMultiSelectMode`, `SelectedMultiAvatarIds`, `ToggleMultiSelect`) is wired up to the UI with checkboxes. Keyboard navigation is added via code-behind. Virtualization replaces non-virtualized ItemsControls. The roulette pool button switches to `AvatarPickerService.OpenMulti`.

**Tech Stack:** C#, WPF, XAML, ListBox with VirtualizingPanel, KeyDown events

---

### Task 1: ViewModel Changes — Ordered Pool, SelectAll/DeselectAll

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarPickerViewModel.cs`

- [ ] **Step 1: Change SelectedMultiAvatarIds from HashSet to List**

In `AvatarPickerViewModel.cs`, change:
```csharp
    public HashSet<string> SelectedMultiAvatarIds { get; } = [];
```
to:
```csharp
    public List<string> SelectedMultiAvatarIds { get; } = [];
```

Update the constructor initialization:
```csharp
            SelectedMultiAvatarIds = new List<string>(multiSelectCurrentIds);
```

Update `ToggleMultiSelect`:
```csharp
    public void ToggleMultiSelect(AvatarPickerItem item)
    {
        if (SelectedMultiAvatarIds.Contains(item.Id))
        {
            SelectedMultiAvatarIds.Remove(item.Id);
        }
        else
        {
            SelectedMultiAvatarIds.Add(item.Id);
        }
        RaisePropertyChanged(nameof(CanConfirm));
        RaisePropertyChanged(nameof(MultiSelectCountText));
    }
```

Update `GetSelectedAvatarIds`:
```csharp
    public IReadOnlyList<string> GetSelectedAvatarIds() =>
        isMultiSelectMode
            ? SelectedMultiAvatarIds.ToList()
            : (string.IsNullOrWhiteSpace(selectedAvatarId) ? [] : [selectedAvatarId]);
```

- [ ] **Step 2: Add SelectAll and DeselectAll methods**

Add to `AvatarPickerViewModel.cs`:
```csharp
    public void SelectAll()
    {
        if (!isMultiSelectMode) return;
        SelectedMultiAvatarIds.Clear();
        foreach (var avatar in AllAvatars)
        {
            if (!SelectedMultiAvatarIds.Contains(avatar.Id))
            {
                SelectedMultiAvatarIds.Add(avatar.Id);
            }
        }
        RaisePropertyChanged(nameof(CanConfirm));
        RaisePropertyChanged(nameof(MultiSelectCountText));
    }

    public void DeselectAll()
    {
        if (!isMultiSelectMode) return;
        SelectedMultiAvatarIds.Clear();
        RaisePropertyChanged(nameof(CanConfirm));
        RaisePropertyChanged(nameof(MultiSelectCountText));
    }
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/AvatarPickerViewModel.cs
git commit -m "feat: add ordered pool and SelectAll/DeselectAll to AvatarPickerViewModel"
```

---

### Task 2: XAML Changes — Checkboxes, Virtualization

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarPickerWindow.xaml`

- [ ] **Step 1: Add checkbox to AvatarCardTemplate**

Find the `AvatarCardTemplate` DataTemplate in `AvatarPickerWindow.xaml`. Add a CheckBox to the card that binds to multi-select state.

Inside the card's Grid, add a CheckBox in the top-right corner:
```xml
<CheckBox Grid.Column="1" Grid.Row="0"
          HorizontalAlignment="Right"
          VerticalAlignment="Top"
          Margin="4"
          IsChecked="{Binding DataContext.IsAvatarSelectedInPool, Source={RelativeSource AncestorType=Window}}"
          Visibility="{Binding DataContext.IsMultiSelectMode, Source={RelativeSource AncestorType=Window}, Converter={StaticResource BoolToVisibilityConverter}}"
          Click="OnCardCheckBoxClicked" />
```

Wait — the card's DataContext is `AvatarPickerItem`, not the VM. Use a different approach: bind via ElementName or use a converter.

Better approach: Add a method to the VM that checks if an avatar is selected:
```csharp
    public bool IsAvatarSelectedInPool(string avatarId) =>
        isMultiSelectMode && SelectedMultiAvatarIds.Contains(avatarId);
```

But this won't raise PropertyChanged when the pool changes for individual items. Instead, use a simpler approach: bind the CheckBox's `IsChecked` to a command that toggles, and use the card's click handler.

Actually, the simplest approach: In the card template, add a CheckBox that uses a `MultiBinding` or just handle the click in code-behind. Let me use the code-behind approach:

Add to the AvatarCardTemplate Border:
```xml
<CheckBox HorizontalAlignment="Right" VerticalAlignment="Top" Margin="6"
          Visibility="{Binding DataContext.IsMultiSelectMode, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource BoolToVisibilityConverter}}"
          Click="OnCardCheckBoxClicked" />
```

In code-behind, `OnCardCheckBoxClicked` gets the card's DataContext (AvatarPickerItem) and calls `viewModel.ToggleMultiSelect(item)`.

- [ ] **Step 2: Add checkbox to AvatarListItemTemplate**

Similarly, add a CheckBox to the `AvatarListItemTemplate` ListBoxItem.

- [ ] **Step 3: Enable virtualization on list view**

The list view is already a `ListBox`. Add virtualization properties:
```xml
<ListBox x:Name="ListViewControl"
         ItemsSource="{Binding FilteredAvatars}"
         ItemTemplate="{StaticResource AvatarListItemTemplate}"
         Background="Transparent"
         BorderThickness="0"
         VirtualizingPanel.IsVirtualizing="True"
         VirtualizingPanel.VirtualizationMode="Recycling"
         Visibility="{Binding ViewMode, Converter={StaticResource EnumToVisibilityConverter}, ConverterParameter=List}" />
```

- [ ] **Step 4: Enable virtualization on grid view**

Replace the `ItemsControl` with a `ListBox` that uses a `WrapPanel` and virtualization:
```xml
<ListBox x:Name="GridViewControl"
         ItemsSource="{Binding FilteredAvatars}"
         ItemTemplate="{StaticResource AvatarCardTemplate}"
         Background="Transparent"
         BorderThickness="0"
         VirtualizingPanel.IsVirtualizing="True"
         VirtualizingPanel.VirtualizationMode="Recycling"
         ScrollViewer.CanContentScroll="True"
         Visibility="{Binding ViewMode, Converter={StaticResource EnumToVisibilityConverter}, ConverterParameter=Grid}">
    <ListBox.ItemsPanel>
        <ItemsPanelTemplate>
            <WrapPanel Orientation="Horizontal" />
        </ItemsPanelTemplate>
    </ListBox.ItemsPanel>
</ListBox>
```

Note: `WrapPanel` doesn't support virtualization natively. For simplicity, keep `ItemsControl` for grid view but add a `VirtualizingStackPanel` alternative. Actually, the simplest working approach for grid virtualization is to use a `UniformGrid` with fixed columns, but that changes layout.

**Decision**: Keep `ItemsControl` for grid view (no virtualization) since avatar counts are typically <200. Only virtualize the list view which is the primary view for large libraries.

- [ ] **Step 5: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add VrcTwitchOscBridge/AvatarPickerWindow.xaml
git commit -m "feat: add checkboxes and list view virtualization to AvatarPickerWindow"
```

---

### Task 3: Code-Behind — Keyboard Navigation, Checkbox Handlers

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarPickerWindow.xaml.cs`

- [ ] **Step 1: Add checkbox click handlers**

```csharp
    private void OnCardCheckBoxClicked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is AvatarPickerItem item)
        {
            viewModel.ToggleMultiSelect(item);
            UpdateSelectionDisplay();
            e.Handled = true;
        }
    }
```

- [ ] **Step 2: Add keyboard navigation**

In the constructor, add a `PreviewKeyDown` handler on the main content area or the window itself:

```csharp
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (viewModel.IsMultiSelectMode)
        {
            if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                viewModel.SelectAll();
                UpdateSelectionDisplay();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
            {
                viewModel.DeselectAll();
                UpdateSelectionDisplay();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Enter || e.Key == Key.Space)
        {
            var focused = FocusManager.GetFocusedElement(this) as FrameworkElement;
            if (focused?.DataContext is AvatarPickerItem item)
            {
                if (viewModel.IsMultiSelectMode)
                {
                    viewModel.ToggleMultiSelect(item);
                }
                else
                {
                    viewModel.SelectedItem = item;
                }
                UpdateSelectionDisplay();
                e.Handled = true;
            }
        }
    }
```

Wire it up in the constructor:
```csharp
        PreviewKeyDown += OnPreviewKeyDown;
```

And clean up in `OnWindowClosed`:
```csharp
        PreviewKeyDown -= OnPreviewKeyDown;
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/AvatarPickerWindow.xaml.cs
git commit -m "feat: add keyboard navigation and checkbox handlers to AvatarPickerWindow"
```

---

### Task 4: Roulette Pool Integration

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Replace OpenAvatarRouletPoolPicker to use AvatarPickerService.OpenMulti**

Read the current `OpenAvatarRouletPoolPicker` method (around line 17458). Replace it with:

```csharp
    private void OpenAvatarRouletPoolPicker()
    {
        if (!CanOpenAvatarRouletPoolPicker() || SelectedRule is null)
        {
            return;
        }

        var configuredIds = SelectedRule.AvatarRouletPool
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        var avatars = availableVrChatAvatars
            .Select(a => new VrChatAvatarSummary(a.Id, a.Name, a.SourceLabel, a.IsCurrentAvatar))
            .ToList();

        var result = AvatarPickerService.OpenMulti(
            ThemeManager.CurrentTheme,
            avatars,
            Settings.AvatarLibrary,
            configuredIds,
            Application.Current.MainWindow);

        if (result is not null)
        {
            SelectedRule.AvatarRouletPool = result.ToList();
            RaisePropertyChanged(nameof(AvatarRouletPoolSummary));
            QueueSave();
        }
    }
```

Check that `SelectedRule.AvatarRouletPool` exists and is a `List<string>`. If it's a different type, adjust accordingly.

- [ ] **Step 2: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs
git commit -m "feat: unify roulette pool picker with AvatarPickerService.OpenMulti"
```

---

### Task 5: Localization

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json`
- Modify: All other `*.extra.json` files

- [ ] **Step 1: Add English source keys**

Add to `en-US.extra.json`:
```json
  "Select All": "Select All",
  "Deselect All": "Deselect All",
  "{0} avatar(s) in pool": "{0} avatar(s) in pool",
  "Avatar is selected": "Avatar is selected",
  "Avatar is not selected": "Avatar is not selected"
```

- [ ] **Step 2: Add translations to all non-English files**

For each language file, add translations for the 5 new keys.

- [ ] **Step 3: Run localization audit**

Run: `dotnet run --project LocalizationAudit --no-restore`

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/*.extra.json
git commit -m "feat: add localization keys for phase 3 multi-select"
```

---

### Task 6: Final Verification

- [ ] **Step 1: Full build**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 warnings, 0 errors

- [ ] **Step 2: Git review**

Run: `git diff --stat HEAD~8..HEAD`
Verify all expected files are changed, no stray files

- [ ] **Step 3: Final commit if needed**
