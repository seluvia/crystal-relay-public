using System.IO;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows.Controls;

namespace CrystalRelayLiveList.Services;

public sealed class StreamWatcherService : IDisposable
{
    private const string TwitchViewerHost = "crystal-relay-live-feedback.test";
    private const string StreamViewerPageName = "stream-viewer.html";

    private readonly WebView2 webView;
    private bool initialized;
    private bool disposed;

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
        var folder = GetUserDataFolder();
        Directory.CreateDirectory(folder);
        var env = await CoreWebView2Environment.CreateAsync(null, folder);
        await webView.EnsureCoreWebView2Async(env);
        var core = webView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 did not initialize.");
        core.SetVirtualHostNameToFolderMapping(
            TwitchViewerHost,
            AppContext.BaseDirectory,
            CoreWebView2HostResourceAccessKind.DenyCors);
        core.NewWindowRequested += OnNewWindowRequested;
        core.Settings.IsStatusBarEnabled = false;
        initialized = true;
    }

    public void Navigate(string channelSlug) =>
        (webView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 not ready."))
            .Navigate(BuildViewerUri(channelSlug).ToString());

    public async Task ClearLoginAsync(string? channelSlug)
    {
        if (webView.CoreWebView2 is null) return;
        webView.CoreWebView2.CookieManager.DeleteAllCookies();
        await webView.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
        if (!string.IsNullOrWhiteSpace(channelSlug))
        {
            webView.CoreWebView2.Navigate(BuildViewerUri(channelSlug).ToString());
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

    private static Uri BuildViewerUri(string channelSlug)
    {
        var builder = new UriBuilder(Uri.UriSchemeHttps, TwitchViewerHost)
        {
            Path = StreamViewerPageName,
            Query = $"channel={Uri.EscapeDataString(channelSlug)}"
        };
        return builder.Uri;
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
