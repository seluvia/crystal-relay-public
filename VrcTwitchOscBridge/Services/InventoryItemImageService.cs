using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace VrcTwitchOscBridge.Services;

public sealed class InventoryItemImageService : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly ConcurrentDictionary<string, BitmapImage?> _cache = new();
    private string? _authCookie;
    private bool _disposed;

    public void SetAuthCookie(string authCookie)
    {
        _authCookie = authCookie;
    }

    public async Task<BitmapImage?> LoadImageAsync(string imageUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return null;

        if (_cache.TryGetValue(imageUrl, out var cached))
            return cached;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
            if (!string.IsNullOrWhiteSpace(_authCookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", $"auth={_authCookie.Trim()}");
            }

            using var response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            _cache[imageUrl] = bitmap;
            return bitmap;
        }
        catch
        {
            _cache[imageUrl] = null;
            return null;
        }
    }

    public void ClearCache() => _cache.Clear();

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _httpClient.Dispose();
            _cache.Clear();
        }
    }
}
