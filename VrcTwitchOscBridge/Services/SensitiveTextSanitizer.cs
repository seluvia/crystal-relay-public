using System.Text.RegularExpressions;

namespace VrcTwitchOscBridge.Services;

internal static class SensitiveTextSanitizer
{
    private const char WindowsSystemDrive = 'C';
    private const char WindowsSeparator = '\\';
    private static readonly string WindowsUsersPrefix = string.Concat(WindowsSystemDrive, ":", WindowsSeparator, "Users", WindowsSeparator);

    private static readonly Regex SecretAssignmentRegex = new(
        @"(?i)\b(access[_-]?token|refresh[_-]?token|client[_-]?secret|device[_-]?code|user[_-]?code|set-cookie|authcookie|twofactorauth|vrchat[-_ ]?auth|cookie)\b(?<sep>\s*[:=]\s*)([^\r\n;]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AuthorizationHeaderRegex = new(
        @"(?i)\bAuthorization\b(?<sep>\s*[:=]\s*)(?!\s*(?:Bearer|OAuth)\b)([^\r\n;]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BearerTokenRegex = new(
        @"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OAuthTokenRegex = new(
        @"(?i)\bOAuth\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WindowsUserPathRegex = new(
        string.Concat("(?i)", Regex.Escape(WindowsUsersPrefix), @"[^\\\r\n]+"),
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TwitchLoginCodeRegex = new(
        @"(?i)(asks for a code,\s*use\s+)[A-Z0-9-]{4,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sanitized = SecretAssignmentRegex.Replace(value, match => $"{match.Groups[1].Value}{match.Groups["sep"].Value}[redacted]");
        sanitized = AuthorizationHeaderRegex.Replace(sanitized, match => $"Authorization{match.Groups["sep"].Value}[redacted]");
        sanitized = BearerTokenRegex.Replace(sanitized, "Bearer [redacted]");
        sanitized = OAuthTokenRegex.Replace(sanitized, "OAuth [redacted]");
        sanitized = WindowsUserPathRegex.Replace(sanitized, $"{WindowsUsersPrefix}<user>");
        sanitized = TwitchLoginCodeRegex.Replace(sanitized, "$1[redacted]");
        return sanitized;
    }
}
