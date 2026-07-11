# Crystal Relay Live List: Enhanced Stream Viewer + Channel Points Design

## Summary
Modify the `crystal-relay-live-list` dev tool's stream viewer to load the real `twitch.tv` site instead of the embed SDK, enabling native channel points accumulation. Add an auto-claim system for bonus channel points with cryptographically random delays to avoid pattern detection.

## Motivation
The current Twitch Embed SDK (`embed.twitch.tv/embed/v1.js`) only loads a basic player + chat widget. It does not load Twitch's full React app, so channel points (passive accumulation and bonus claims) do not work even when the user is logged into the WebView2 profile. The user needs to:
1. Earn passive channel points while watching streams in the dev tool
2. Automatically claim bonus channel point rewards with randomized timing

## Scope
**In scope:**
- `StreamWatcherService.cs` — navigation to real Twitch site + script injection
- New `StreamViewerInject.js` — CSS cleanup + auto-claim logic (embedded resource)
- `CrystalRelayLiveList.csproj` — include new JS resource

**Out of scope:**
- The main Crystal Relay app, updater, release scripts, or public docs
- The Android companion app
- Any Twitch API integration (DOM-based only)
- Chat message automation

## Architecture

### Navigation Change
`StreamWatcherService.BuildViewerUri` changes from virtual host + local HTML to `https://www.twitch.tv/{channel}`. The existing WebView2 profile and cookie store are preserved, so the user's Twitch login session carries over.

### Script Injection via WebView2
After `EnsureReadyAsync` initializes the WebView2, register `StreamViewerInject.js` via `CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(...)`. This injects the cleanup + auto-claim script on every page load, including after navigation.

The JavaScript is stored as an embedded resource in the `.csproj` and read at runtime.

### CSS Cleanup Strategy
The injected script creates a `<style>` element that hides Twitch UI chrome:
- Left sidebar navigation (`data-a-target="side-nav"`)
- Top navigation bar (`data-a-target="top-nav"`)
- Recommendation sections below player
- Footer
- Adjusts main content area to fill viewport

After a short settling delay, it clicks the Theatre Mode button (`data-a-target="player-theatre-mode-button"`) for a cleaner player+chat layout.

### Channel Points Auto-Claim

#### Detection
A `MutationObserver` watches `document.body` for elements matching known Twitch bonus-claim selectors:
```
[data-a-target="claim-channel-points-button"]
[data-a-target="bonus-points-button"]
[data-test-selector="claim-points-button"]
button[aria-label*="Claim"]
```

#### Random Delay Algorithm
Uses 6 sources of entropy from `crypto.getRandomValues()` (Web Crypto API) for uncorrelated jitter:

| Source | Range | Description |
|--------|-------|-------------|
| Base | 800–8000ms | Random 32-bit mod 7201 + 800 |
| Jitter 1 | ±1500ms | `sin(random1 * 0.0003) * 1500` |
| Jitter 2 | 0~2296ms | `(random2 % 13) * 177` |
| Jitter 3 | 0~693ms | `(Date.now() % 991) * 0.7` |
| Jitter 4 | ±250ms | `(random3 % 500) - 250` |
| Jitter 5 | 0~1200ms | `log10(1 + (random4 % 1000)) * 400` |
| Jitter 6 | 0~809ms | `(random5 % 1000) * 1.618 * 0.5` |

Final delay = sum of all sources, floored at 200ms minimum. Each claim event generates a fresh independent delay. No timing pattern can be derived across claims.

#### Safety
- `btn.dataset.crClaimed` flag prevents double-claiming the same button instance
- Claim timer is null-checked so only one pending claim exists at a time
- A 5-second interval fallback catches any claim buttons the observer misses (e.g., if the script loaded after the button appeared)

## Files Changed

### `StreamWatcherService.cs`
- Remove `TwitchViewerHost` / `StreamViewerPageName` constants
- Change `BuildViewerUri` to return `https://www.twitch.tv/{channel}`
- Add `InjectViewerScriptAsync()` called from `EnsureReadyAsync`
- Add method to load embedded JS resource and call `AddScriptToExecuteOnDocumentCreatedAsync`
- The HTML file mapping (`SetVirtualHostNameToFolderMapping`) can be removed

### `StreamViewerInject.js` (new)
Embedded resource containing the full CSS cleanup + auto-claim script. Self-contained, no external dependencies.

### `CrystalRelayLiveList.csproj`
Add `StreamViewerInject.js` as an embedded resource (`<EmbeddedResource>`).

### `stream-viewer.html` (unchanged, becomes unused)
Kept as a fallback reference. Not deleted.

## Edge Cases & Considerations
- **Twitch layout changes**: Selectors use `data-a-target` attributes which are relatively stable (used by Twitch's own test automation). If Twitch changes these, the CSS cleanup degrades gracefully (site just looks like normal Twitch) and the auto-claim falls back to interval polling.
- **Theatre mode fails**: If the theatre mode button isn't found or the layout doesn't support it, cleanup falls back gracefully.
- **Login expired**: If the WebView2 Twitch session expires, the user sees the Twitch login page as normal. The `Clear Twitch Login` button still works.
- **Multiple streams**: Only one stream plays at a time (single WebView2). Navigation to a new channel replaces the current page.
- **Hidden window**: If the stream viewer tab is not active, the WebView2 may be suspended by WPF. The stream won't play in background; the user should keep the viewer open while watching.
