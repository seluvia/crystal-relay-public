# VRChat Avatar Thumbnail Auth Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix VRChat avatar thumbnails not displaying in the Pick Avatar window by adding the auth cookie to image download requests and capturing the `thumbnailImageUrl` field from the VRChat API.

**Architecture:** `VrChatAvatarRecord` gains a `thumbnailImageUrl` field (preferred, smaller) over `imageUrl`. `AvatarImageService` gains a `SetVrChatAuthCookie(string?)` method that stores the cookie and applies it per-request via `Cookie: auth=...` header. `MainWindowViewModel` calls `AvatarPickerService.SetVrChatAuthCookie` whenever VRChat connects or disconnects.

**Tech Stack:** C#, .NET 10, WPF, HttpClient, Windows Credential Manager (auth cookie already in `Settings.VrChat.AuthCookie`)

---

## File Structure

| File | Change |
|------|--------|
| `VrcTwitchOscBridge/Services/VrChatApiClient.cs` | Add `ThumbnailImageUrl` to `VrChatAvatarRecord`; prefer it in `MergeAvatars` |
| `VrcTwitchOscBridge/Services/AvatarImageService.cs` | Add `vrChatAuthCookie` field, `SetVrChatAuthCookie` method, cookie header in `LoadVrChatThumbnailAsync` |
| `VrcTwitchOscBridge/Services/AvatarPickerService.cs` | Add `SetVrChatAuthCookie` static pass-through |
| `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs` | Call `AvatarPickerService.SetVrChatAuthCookie` on VRChat connect and disconnect |

---

### Task 1: Add `thumbnailImageUrl` to VrChatAvatarRecord and prefer it in MergeAvatars

**Files:**
- Modify: `VrcTwitchOscBridge/Services/VrChatApiClient.cs` lines 513–523 (VrChatAvatarRecord) and line 276 (MergeAvatars)

- [ ] **Step 1: Add `ThumbnailImageUrl` field to `VrChatAvatarRecord`**

Find the `VrChatAvatarRecord` class (around line 513). Change from:

```csharp
private sealed class VrChatAvatarRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; set; }
}
```

To:

```csharp
private sealed class VrChatAvatarRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("thumbnailImageUrl")]
    public string? ThumbnailImageUrl { get; set; }
}
```

- [ ] **Step 2: Prefer `thumbnailImageUrl` over `imageUrl` in `MergeAvatars`**

Find the `MergeAvatars` method (around line 262). Change the line that sets `ThumbnailUrl` from:

```csharp
ThumbnailUrl = avatar.ImageUrl,
```

To:

```csharp
ThumbnailUrl = avatar.ThumbnailImageUrl ?? avatar.ImageUrl,
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds with 0 errors

---

### Task 2: Add auth cookie support to AvatarImageService

**Files:**
- Modify: `VrcTwitchOscBridge/Services/AvatarImageService.cs`

- [ ] **Step 1: Add `vrChatAuthCookie` field and `SetVrChatAuthCookie` method**

Add after the existing `imageCache` field (around line 24) and after the constructor:

```csharp
private string? vrChatAuthCookie;

/// <summary>
/// Sets the VRChat auth cookie used for authenticated image downloads.
/// Call when VRChat connects (pass the cookie) or disconnects (pass null).
/// </summary>
public void SetVrChatAuthCookie(string? cookie)
{
    vrChatAuthCookie = string.IsNullOrWhiteSpace(cookie) ? null : cookie.Trim();
}
```

- [ ] **Step 2: Use the cookie in `LoadVrChatThumbnailAsync`**

Find `LoadVrChatThumbnailAsync` (around line 189). Change the download block from:

```csharp
try
{
    using var response = await HttpClient.GetAsync(thumbnailUrl, cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
        return null;
    }

    var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
    File.WriteAllBytes(cachePath, bytes);
    return LoadImageFromFile(cachePath);
}
catch (OperationCanceledException)
{
    return null;
}
catch
{
    return null;
}
```

To:

```csharp
try
{
    using var request = new HttpRequestMessage(HttpMethod.Get, thumbnailUrl);
    if (!string.IsNullOrWhiteSpace(vrChatAuthCookie))
    {
        request.Headers.TryAddWithoutValidation("Cookie", $"auth={vrChatAuthCookie}");
    }

    using var response = await HttpClient.SendAsync(request, cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
        return null;
    }

    var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
    File.WriteAllBytes(cachePath, bytes);
    return LoadImageFromFile(cachePath);
}
catch (OperationCanceledException)
{
    return null;
}
catch
{
    return null;
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds with 0 errors

---

### Task 3: Add `SetVrChatAuthCookie` pass-through to AvatarPickerService

**Files:**
- Modify: `VrcTwitchOscBridge/Services/AvatarPickerService.cs`

- [ ] **Step 1: Add pass-through method**

Add after the existing `ClearImageCache` method (around line 94):

```csharp
/// <summary>
/// Sets the VRChat auth cookie for authenticated thumbnail downloads.
/// Call when VRChat connects (pass cookie) or disconnects (pass null).
/// </summary>
public static void SetVrChatAuthCookie(string? cookie)
{
    Instance.SetVrChatAuthCookie(cookie);
    Instance.ClearCache();
}
```

Note: `ClearCache()` is called so that any previously failed downloads (without auth) are retried with the new cookie next time the picker opens.

- [ ] **Step 2: Build to verify**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds with 0 errors

---

### Task 4: Hook auth cookie into MainWindowViewModel VRChat connect/disconnect

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

The auth cookie lives at `Settings.VrChat.AuthCookie`. It needs to be passed to `AvatarPickerService` whenever VRChat connects or disconnects.

- [ ] **Step 1: Set cookie on VRChat connect in `InitializeVrChatAsync`**

Find `InitializeVrChatAsync` (around line 3992). At the point where `Settings.VrChat.IsConnected` is confirmed true (after the early return), add a call to `AvatarPickerService.SetVrChatAuthCookie`. Insert it right after `RaiseVrChatConnectionStateProperties()` within the connected branch:

Change from:

```csharp
private async Task InitializeVrChatAsync()
{
    RaiseVrChatConnectionStateProperties();

    if (!Settings.VrChat.IsConnected)
    {
        VrChatStatus = T("VRChat avatar access is not connected.");
        VrChatAvatarStatus = T("Connect VRChat to load avatar choices.");
        VrChatOscParameterStatus = T("Connect VRChat to load avatar parameters.");
        ResetVrChatLocalRuntimeTracking();
        DisposeVrChatLocalOscWatcher();
        RefreshVrChatAvatarSelectionOptions();
        return;
    }
```

To:

```csharp
private async Task InitializeVrChatAsync()
{
    RaiseVrChatConnectionStateProperties();

    if (!Settings.VrChat.IsConnected)
    {
        AvatarPickerService.SetVrChatAuthCookie(null);
        VrChatStatus = T("VRChat avatar access is not connected.");
        VrChatAvatarStatus = T("Connect VRChat to load avatar choices.");
        VrChatOscParameterStatus = T("Connect VRChat to load avatar parameters.");
        ResetVrChatLocalRuntimeTracking();
        DisposeVrChatLocalOscWatcher();
        RefreshVrChatAvatarSelectionOptions();
        return;
    }

    AvatarPickerService.SetVrChatAuthCookie(Settings.VrChat.AuthCookie);
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds with 0 errors

---

### Task 5: Final build verification

- [ ] **Step 1: Full clean build**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds with 0 warnings, 0 errors

- [ ] **Step 2: Manual test checklist**

After rebuilding the test package:
1. Open Pick Avatar — placeholders show instantly for all avatars
2. Wait a few seconds — thumbnails should start appearing as they download
3. Reopen the picker — already-cached thumbnails load instantly from disk
4. Disconnect VRChat and reopen — only placeholders, no auth cookie passed
5. Reconnect VRChat and reopen — thumbnails load again
6. Check debug output for `[AvatarPicker]` summary line — `loaded` count should be > 0

---

## Self-Review

**Spec coverage:**
- ✅ `thumbnailImageUrl` captured from VRChat API → Task 1
- ✅ Prefer thumbnail over full-res → Task 1 Step 2
- ✅ Auth cookie added to download requests → Task 2
- ✅ Cookie plumbed via `AvatarPickerService` → Task 3
- ✅ Cookie set on VRChat connect/disconnect → Task 4

**Placeholder scan:** No TBD, TODO, or vague steps. All code blocks complete.

**Type consistency:**
- `SetVrChatAuthCookie(string? cookie)` defined in Task 2, called in Task 3 (via `Instance.SetVrChatAuthCookie`), and called in Task 4 (via `AvatarPickerService.SetVrChatAuthCookie`)
- `Instance.ClearCache()` in Task 3 matches existing `ClearCache()` method on `AvatarImageService`
- `Settings.VrChat.AuthCookie` in Task 4 matches existing usage at line 3790 of `MainWindowViewModel.cs`
