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
