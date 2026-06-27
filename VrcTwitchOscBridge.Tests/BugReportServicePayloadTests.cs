using System.Text;
using System.Text.Json;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class BugReportServicePayloadTests
{
    [Fact]
    public void PreparePayloadJson_SanitizesUserEnteredFieldsBeforeTransport()
    {
        var settingsPath = TestWindowsPaths.From('C', "Users", "secretuser", "crystal-relay", "settings.json");
        var contactPath = TestWindowsPaths.From('C', "Users", "secretuser", "contact.txt");

        var json = BugReportService.PreparePayloadJson(new BugReportSubmission(
            Title: "access_token=title-secret leak",
            WhatHappened: $"access_token=body-secret and {settingsPath}",
            ExpectedBehavior: "Bearer abc123secret should not be shown",
            StepsToReproduce: "Open the app, use ABCD-EFGH when it asks for a code, use the feature.",
            ContactName: contactPath,
            AppVersion: "3.1.9",
            Category: "other",
            Severity: "normal",
            Snapshot: "snapshot",
            ActivityLog: null,
            DebugLog: null,
            CrashLog: null));

        Assert.DoesNotContain("title-secret", json);
        Assert.DoesNotContain("body-secret", json);
        Assert.DoesNotContain("abc123secret", json);
        Assert.DoesNotContain("secretuser", json);
        Assert.DoesNotContain("ABCD-EFGH", json);
        Assert.Contains("[redacted]", json);

        using var document = JsonDocument.Parse(json);
        Assert.Contains("<user>", document.RootElement.GetProperty("contactName").GetString());
    }

    [Fact]
    public void PreparePayloadJson_KeepsSanitizedRequiredFieldsLongEnoughForWorkerValidation()
    {
        var pathOnlyReportText = TestWindowsPaths.From('D', "StreamTools", "CrystalRelay", "secret.json");

        var json = BugReportService.PreparePayloadJson(new BugReportSubmission(
            Title: "Path-only report title",
            WhatHappened: pathOnlyReportText,
            ExpectedBehavior: pathOnlyReportText,
            StepsToReproduce: pathOnlyReportText,
            ContactName: string.Empty,
            AppVersion: "3.1.9",
            Category: "other",
            Severity: "normal",
            Snapshot: "snapshot",
            ActivityLog: null,
            DebugLog: null,
            CrashLog: null));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.GetProperty("whatHappened").GetString()?.Length >= 20);
        Assert.True(root.GetProperty("expectedBehavior").GetString()?.Length >= 20);
        Assert.True(root.GetProperty("stepsToReproduce").GetString()?.Length >= 20);
    }

    [Fact]
    public void PreparePayloadJson_KeepsCombinedDiagnosticsWithinClientBudget()
    {
        var largeSection = new string('x', 24 * 1024);

        var json = BugReportService.PreparePayloadJson(new BugReportSubmission(
            Title: "Large diagnostic budget report",
            WhatHappened: "The app generated a large diagnostic payload during report submission.",
            ExpectedBehavior: "The app should keep diagnostics inside the configured client budget.",
            StepsToReproduce: "Open bug report, include each diagnostic section, then submit the report.",
            ContactName: string.Empty,
            AppVersion: "3.1.9",
            Category: "other",
            Severity: "normal",
            Snapshot: largeSection,
            ActivityLog: largeSection,
            DebugLog: largeSection,
            CrashLog: largeSection));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var diagnosticsBytes = Utf8Bytes(root.GetProperty("snapshot").GetString())
            + Utf8Bytes(root.GetProperty("activityLog").GetString())
            + Utf8Bytes(root.GetProperty("debugLog").GetString())
            + Utf8Bytes(root.GetProperty("crashLog").GetString());

        Assert.True(diagnosticsBytes <= 40 * 1024, $"Diagnostics used {diagnosticsBytes} bytes.");
    }

    [Fact]
    public void PreparePayloadJson_KeepsTotalPayloadWithinTransportBudgetForMultibyteText()
    {
        var multibyteText = string.Concat(Enumerable.Repeat("💎", 2500));

        var json = BugReportService.PreparePayloadJson(new BugReportSubmission(
            Title: "Large multibyte report title",
            WhatHappened: multibyteText,
            ExpectedBehavior: multibyteText,
            StepsToReproduce: multibyteText,
            ContactName: string.Empty,
            AppVersion: "3.1.9",
            Category: "other",
            Severity: "normal",
            Snapshot: new string('x', 2 * 1024),
            ActivityLog: new string('x', 16 * 1024),
            DebugLog: new string('x', 16 * 1024),
            CrashLog: new string('x', 12 * 1024)));

        var payloadBytes = Encoding.UTF8.GetByteCount(json);

        Assert.True(payloadBytes <= 56 * 1024, $"Payload used {payloadBytes} bytes.");
    }

    [Fact]
    public void PreparePayloadJson_KeepsTotalPayloadWithinTransportBudgetForJsonEscapedText()
    {
        var escapedText = new string('\u0001', 5000);

        var json = BugReportService.PreparePayloadJson(new BugReportSubmission(
            Title: "Large escaped report title",
            WhatHappened: escapedText,
            ExpectedBehavior: escapedText,
            StepsToReproduce: escapedText,
            ContactName: string.Empty,
            AppVersion: "3.1.9",
            Category: "other",
            Severity: "normal",
            Snapshot: new string('x', 2 * 1024),
            ActivityLog: new string('x', 16 * 1024),
            DebugLog: new string('x', 16 * 1024),
            CrashLog: new string('x', 12 * 1024)));

        var payloadBytes = Encoding.UTF8.GetByteCount(json);

        Assert.True(payloadBytes <= 56 * 1024, $"Payload used {payloadBytes} bytes.");
    }

    private static int Utf8Bytes(string? value) =>
        string.IsNullOrEmpty(value) ? 0 : Encoding.UTF8.GetByteCount(value);
}
