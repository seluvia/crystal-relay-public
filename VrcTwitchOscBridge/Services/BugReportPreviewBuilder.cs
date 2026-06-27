using System.Text;

namespace VrcTwitchOscBridge.Services;

internal static class BugReportPreviewBuilder
{
    public static string Build(
        string title,
        string category,
        string severity,
        string whatHappened,
        string expectedBehavior,
        string stepsToReproduce,
        string contactName,
        string appVersion,
        string snapshot,
        string? activityLogSection,
        string? debugLogSection,
        string? crashLogSection)
    {
        var contact = string.IsNullOrWhiteSpace(contactName)
            ? "Not provided"
            : SensitiveTextSanitizer.Sanitize(contactName);
        var appVer = string.IsNullOrWhiteSpace(appVersion)
            ? "Unknown"
            : SensitiveTextSanitizer.Sanitize(appVersion);

        var builder = new StringBuilder();
        builder.AppendLine("## Bug Report");
        builder.AppendLine();
        builder.AppendLine($"**Category:** {category}");
        builder.AppendLine($"**Severity:** {severity}");
        builder.AppendLine($"**App version:** {appVer}");
        builder.AppendLine($"**Contact:** {contact}");
        builder.AppendLine();
        builder.AppendLine("## What happened");
        builder.AppendLine();
        builder.AppendLine(SensitiveTextSanitizer.Sanitize(whatHappened));
        builder.AppendLine();
        builder.AppendLine("## Expected behavior");
        builder.AppendLine();
        builder.AppendLine(SensitiveTextSanitizer.Sanitize(expectedBehavior));
        builder.AppendLine();
        builder.AppendLine("## Steps to reproduce");
        builder.AppendLine();
        builder.AppendLine(SensitiveTextSanitizer.Sanitize(stepsToReproduce));
        builder.AppendLine();
        builder.AppendLine("## Live status snapshot");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(snapshot);
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Activity log");
        builder.AppendLine();
        AppendSection(builder, activityLogSection);
        builder.AppendLine();
        builder.AppendLine("## Debug logs");
        builder.AppendLine();
        AppendSection(builder, debugLogSection);
        builder.AppendLine();
        builder.AppendLine("## Crash log");
        builder.AppendLine();
        AppendSection(builder, crashLogSection);

        return builder.ToString().TrimEnd();
    }

    private static void AppendSection(StringBuilder builder, string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            builder.AppendLine("Not included.");
            return;
        }

        builder.AppendLine("```text");
        builder.AppendLine(section);
        builder.AppendLine("```");
    }
}
