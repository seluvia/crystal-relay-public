using System.Text.RegularExpressions;

namespace VrcTwitchOscBridge.Services;

internal static class SensitiveTextSanitizer
{
    private static readonly Regex SecretAssignmentRegex = new(
        @"(?i)\b(access[_-]?token|refresh[_-]?token|client[_-]?secret|device[_-]?code|user[_-]?code|verification[_-]?token|relay[_-]?client[_-]?secret|jwt|password|secret|token|set-cookie|authcookie|twofactorauth|vrchat[-_ ]?auth|cookie|streamlabs|streamelements|ko[-_ ]?fi)\b(?<sep>\s*[:=]\s*)([^\r\n;]+)",
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
        @"(?i)\b[A-Z]:\\Users\\[^\\\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StackTraceSourcePathRegex = new(
        @"(?i)(?<prefix>\s+in\s+)[A-Z]:\\[^\r\n]*\\(?<file>[^\\\r\n:]+(?:\.cs|\.xaml|\.js|\.ts|\.json|\.xml|\.ps1|\.txt))(?<suffix>:line\s+\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AbsoluteWindowsPathRegex = new(
        @"(?i)\b[A-Z]:\\(?!Users\\<user>)[^\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TwitchLoginCodeRegex = new(
        @"(?i)(asks for a code,\s*use\s+)[A-Z0-9-]{4,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TwitchLoginCodeBeforePromptRegex = new(
        @"(?i)(use\s+)[A-Z0-9-]{4,}(\s+when\s+it\s+asks\s+for\s+a\s+code)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex QuerySecretRegex = new(
        @"(?i)([?&](?:access_token|refresh_token|token|code|client_secret|secret|jwt|signature)=)([^&#\s]+)",
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
        sanitized = QuerySecretRegex.Replace(sanitized, "$1[redacted]");
        sanitized = StackTraceSourcePathRegex.Replace(sanitized, match =>
            $"{match.Groups["prefix"].Value}<local path>\\{match.Groups["file"].Value}{match.Groups["suffix"].Value}");
        sanitized = WindowsUserPathRegex.Replace(sanitized, match => $"{match.Value[..3]}Users\\<user>");
        sanitized = AbsoluteWindowsPathRegex.Replace(sanitized, "<local path>");
        sanitized = TwitchLoginCodeRegex.Replace(sanitized, "$1[redacted]");
        sanitized = TwitchLoginCodeBeforePromptRegex.Replace(sanitized, "$1[redacted]$2");
        return sanitized;
    }
}
