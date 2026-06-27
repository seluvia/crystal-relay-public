using System.Globalization;

namespace CrystalRelayLiveList.Services;

public static class LiveUserKey
{
    public static string Normalize(string? twitchUrl, string? displayName)
    {
        if (TryGetChannelSlug(twitchUrl, out var slug))
        {
            return string.Format(CultureInfo.InvariantCulture, "https://www.twitch.tv/{0}", slug.ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(twitchUrl))
        {
            return twitchUrl.Trim();
        }

        return displayName?.Trim() ?? string.Empty;
    }

    public static bool TryGetChannelSlug(string? twitchUrl, out string channelSlug)
    {
        channelSlug = string.Empty;
        if (string.IsNullOrWhiteSpace(twitchUrl)
            || !Uri.TryCreate(twitchUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !IsTwitchHost(uri.Host))
        {
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 1)
        {
            return false;
        }

        var slug = segments[0];
        if (string.IsNullOrWhiteSpace(slug) || !IsChannelSlug(slug))
        {
            return false;
        }

        channelSlug = slug;
        return true;
    }

    public static string BuildVodUrl(string? twitchUrl)
    {
        return TryGetChannelSlug(twitchUrl, out var slug)
            ? string.Format(CultureInfo.InvariantCulture, "https://www.twitch.tv/{0}/videos?filter=archives&sort=time", slug)
            : twitchUrl ?? string.Empty;
    }

    private static bool IsTwitchHost(string host)
    {
        return host.Equals("twitch.tv", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".twitch.tv", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChannelSlug(string slug)
    {
        return slug.All(c => char.IsLetterOrDigit(c) || c == '_');
    }
}
