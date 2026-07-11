# Live List Channel Points Viewer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Modify the `crystal-relay-live-list` dev tool to load the real `twitch.tv` site for native channel points and auto-claim bonus points with random delays.

**Architecture:** `StreamWatcherService.cs` navigates to `https://www.twitch.tv/{channel}` instead of the local embed HTML. A new `StreamViewerInject.js` embedded resource is injected via `CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync()` to apply CSS cleanup (hide sidebar/nav) and run a MutationObserver-based bonus channel points auto-claimer with cryptographically random delays.

**Tech Stack:** C# (.NET 10 WPF), WebView2, JavaScript (Web Crypto API)

## Global Constraints
- Dev tool only: do not modify main Crystal Relay app, updater, release scripts, or public docs.
- Script injection uses `CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync` — runs on every page load.
- CSS cleanup targets `data-a-target` attributes for stability.
- Auto-claim uses `crypto.getRandomValues()` only (Web Crypto API, not `Math.random()`).
- Delay floor is 200ms minimum.
- `dataset.crClaimed` flag prevents double-claiming the same button element.

---

### Task 1: Create `StreamViewerInject.js`

**Files:**
- Create: `tools/private/crystal-relay-live-list/StreamViewerInject.js`

**Interfaces:**
- Consumes: nothing (standalone JS)
- Produces: a self-contained injected script that runs on every `twitch.tv` page load. Sets `window.__crLiveListInjected` to prevent double-injection.

- [ ] **Step 1: Create the JS file with CSS cleanup + theatre mode + auto-claim**

The file lives at `tools/private/crystal-relay-live-list/StreamViewerInject.js`:

```javascript
(function () {
    "use strict";

    if (window.__crLiveListInjected) return;
    window.__crLiveListInjected = true;

    /* ── CSS Cleanup ── */
    function applyCleanup() {
        var style = document.createElement("style");
        style.id = "cr-livelist-cleanup";
        style.textContent = [
            "[data-a-target=\"side-nav\"] { display: none !important; }",
            "[data-a-target=\"top-nav\"] { display: none !important; }",
            "[data-test-selector=\"recommended-section\"] { display: none !important; }",
            ".recommended-section, .recommended-show { display: none !important; }",
            "[data-a-target*=\"recommended\"] { display: none !important; }",
            "footer, .tw-footer { display: none !important; }",
            ".channel-root, .channel-root__content { max-width: none !important; margin: 0 !important; padding-left: 0 !important; }",
            ".video-player, .channel-info-content { max-width: none !important; }"
        ].join(" ");
        document.head.appendChild(style);
    }

    /* ── Theatre Mode ── */
    function tryTheatreMode() {
        var btn = document.querySelector('[data-a-target="player-theatre-mode-button"]');
        if (btn && btn.getAttribute("aria-pressed") !== "true") {
            btn.click();
        }
    }

    /* ── Channel Points Auto-Claim ── */
    function setupAutoClaim() {
        function getRandomDelay() {
            var arr = new Uint32Array(6);
            crypto.getRandomValues(arr);

            /* Base: 800–8000ms */
            var base = 800 + (arr[0] % 7201);

            /* Jitter 1: sinusoidal ±1500ms */
            var j1 = Math.sin(arr[1] * 0.0003) * 1500;

            /* Jitter 2: prime-multiplier 0–2296ms in steps of 177 */
            var j2 = (arr[2] % 13) * 177;

            /* Jitter 3: time-of-day modulo 991 × 0.7 */
            var j3 = (Date.now() % 991) * 0.7;

            /* Jitter 4: raw noise ±250ms */
            var j4 = (arr[3] % 500) - 250;

            /* Jitter 5: log-scaled 0~1200ms */
            var j5 = Math.log10(1 + (arr[4] % 1000)) * 400;

            /* Jitter 6: golden-ratio modulated 0~809ms */
            var j6 = (arr[5] % 1000) * 1.618033988749895 * 0.5;

            return Math.max(200, Math.floor(base + j1 + j2 + j3 + j4 + j5 + j6));
        }

        var claimTimer = null;
        var selectors = [
            '[data-a-target="claim-channel-points-button"]',
            '[data-a-target="bonus-points-button"]',
            '[data-test-selector="claim-points-button"]',
            'button[aria-label*="Claim"]'
        ];

        function tryClaim() {
            if (claimTimer !== null) return;
            for (var i = 0; i < selectors.length; i++) {
                var btn = document.querySelector(selectors[i]);
                if (btn && btn.offsetParent !== null && !btn.dataset.crClaimed) {
                    btn.dataset.crClaimed = "true";
                    var delay = getRandomDelay();
                    claimTimer = setTimeout(function () {
                        try { btn.click(); } catch (_) { }
                        claimTimer = null;
                    }, delay);
                    return;
                }
            }
        }

        /* Initial attempt after page settles */
        setTimeout(tryClaim, 3000);

        /* Observe DOM for claim button appearing dynamically */
        var observer = new MutationObserver(function () { tryClaim(); });
        observer.observe(document.body, { childList: true, subtree: true });

        /* Fallback interval in case observer misses */
        setInterval(function () {
            if (claimTimer === null) tryClaim();
        }, 5000);
    }

    /* ── Initialize ── */
    function init() {
        applyCleanup();
        setTimeout(tryTheatreMode, 2000);
        setupAutoClaim();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
```

- [ ] **Step 2: Verify no syntax errors with Node.js (optional check)**

```bash
node -c "tools/private/crystal-relay-live-list/StreamViewerInject.js"
```
Expected: no output (syntax is valid).

- [ ] **Step 3: Commit**

```bash
git add tools/private/crystal-relay-live-list/StreamViewerInject.js
git commit -m "feat: add CSS cleanup and channel points auto-claim script"
```

---

### Task 2: Modify `CrystalRelayLiveList.csproj`

**Files:**
- Modify: `tools/private/crystal-relay-live-list/CrystalRelayLiveList.csproj`

**Interfaces:**
- Consumes: the JS file from Task 1
- Produces: embedded resource loaded by `StreamWatcherService`

- [ ] **Step 1: Add `StreamViewerInject.js` as embedded resource**

Change the `<ItemGroup>` section to add:

```xml
  <ItemGroup>
    <EmbeddedResource Include="StreamViewerInject.js" />
  </ItemGroup>
```

Full resulting `<ItemGroup>` (adding after the existing `<None>` lines):

```xml
  <ItemGroup>
    <None Include="live-list.example.json" CopyToOutputDirectory="PreserveNewest" />
    <None Include="stream-viewer.html" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
  <ItemGroup>
    <EmbeddedResource Include="StreamViewerInject.js" />
  </ItemGroup>
```

- [ ] **Step 2: Build to verify project loads**

```bash
dotnet build "tools/private/crystal-relay-live-list/CrystalRelayLiveList.csproj" --no-restore
```
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add tools/private/crystal-relay-live-list/CrystalRelayLiveList.csproj
git commit -m "chore: add StreamViewerInject.js as embedded resource"
```

---

### Task 3: Modify `StreamWatcherService.cs`

**Files:**
- Modify: `tools/private/crystal-relay-live-list/Services/StreamWatcherService.cs`

**Interfaces:**
- Consumes: embedded `StreamViewerInject.js` resource from Task 2
- Produces: navigates to `https://www.twitch.tv/{channel}`, injects cleanup/auto-claim script on each page load

- [ ] **Step 1: Rewrite `StreamWatcherService.cs`**

Full file replacement:

```csharp
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CrystalRelayLiveList.Services;

public sealed class StreamWatcherService : IDisposable
{
    private const string TwitchUrlTemplate = "https://www.twitch.tv/{0}";

    private readonly WebView2 webView;
    private bool initialized;
    private bool disposed;
    private string? injectedScript;

    public StreamWatcherService(WebView2 webView)
    {
        this.webView = webView;
    }

    public bool IsReady => initialized && webView.CoreWebView2 is not null;

    public static string GetUserDataFolder() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrystalRelay",
            "DevTools",
            "LiveList",
            "WebView2");

    public async Task EnsureReadyAsync()
    {
        if (IsReady) return;

        injectedScript = LoadInjectScript();

        var folder = GetUserDataFolder();
        Directory.CreateDirectory(folder);
        var env = await CoreWebView2Environment.CreateAsync(null, folder);
        await webView.EnsureCoreWebView2Async(env);
        var core = webView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 did not initialize.");

        if (injectedScript is not null)
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(injectedScript);
        }

        core.NewWindowRequested += OnNewWindowRequested;
        core.Settings.IsStatusBarEnabled = false;
        initialized = true;
    }

    public void Navigate(string channelSlug) =>
        (webView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 not ready."))
            .Navigate(string.Format(TwitchUrlTemplate, Uri.EscapeDataString(channelSlug)));

    public async Task ClearLoginAsync(string? channelSlug)
    {
        if (webView.CoreWebView2 is null) return;
        webView.CoreWebView2.CookieManager.DeleteAllCookies();
        await webView.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
        if (!string.IsNullOrWhiteSpace(channelSlug))
        {
            webView.CoreWebView2.Navigate(string.Format(TwitchUrlTemplate, Uri.EscapeDataString(channelSlug)));
        }
    }

    public void Stop()
    {
        if (webView.CoreWebView2 is not null)
        {
            webView.CoreWebView2.Stop();
            webView.CoreWebView2.Navigate("about:blank");
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (webView.CoreWebView2 is null || string.IsNullOrWhiteSpace(e.Uri)) return;
        webView.CoreWebView2.Navigate(e.Uri);
    }

    private static string? LoadInjectScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("StreamViewerInject.js", StringComparison.OrdinalIgnoreCase));

        if (name is null) return null;

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null) return null;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (webView.CoreWebView2 is not null)
            {
                webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
                webView.CoreWebView2.Stop();
                webView.CoreWebView2.Navigate("about:blank");
            }
            webView.Dispose();
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
```

Key changes from the original:
- `TwitchViewerHost`, `StreamViewerPageName` removed, replaced by `TwitchUrlTemplate`
- `BuildViewerUri` removed; `Navigate` formats `TwitchUrlTemplate` directly
- `SetVirtualHostNameToFolderMapping` removed (no longer maps the HTML embed)
- New `LoadInjectScript()` reads embedded JS resource
- `AddScriptToExecuteOnDocumentCreatedAsync` called in `EnsureReadyAsync`
- `ClearLoginAsync` and `OnNewWindowRequested` use string formatting instead of `BuildViewerUri`

- [ ] **Step 2: Build to verify compilation**

```bash
dotnet build "tools/private/crystal-relay-live-list/CrystalRelayLiveList.csproj" --no-restore
```
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add tools/private/crystal-relay-live-list/Services/StreamWatcherService.cs
git commit -m "feat: navigate to real twitch.tv site and inject channel points script"
```

---

### Task 4: Build and Smoke Test

**Files:**
- No source changes.

- [ ] **Step 1: Full build**

```bash
dotnet build "tools/private/crystal-relay-live-list/CrystalRelayLiveList.csproj"
```
Expected: Build succeeded, no errors.

- [ ] **Step 2: Verify embedded resource is packed**

```bash
dotnet build "tools/private/crystal-relay-live-list/CrystalRelayLiveList.csproj" -v:n 2>&1 | Select-String "StreamViewerInject"
```
Expected: shows the JS file being included as embedded resource.

- [ ] **Step 3: Commit if not already clean**

```bash
git add -A
git commit -m "chore: build verification"
```
