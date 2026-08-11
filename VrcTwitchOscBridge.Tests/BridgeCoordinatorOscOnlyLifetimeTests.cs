using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class BridgeCoordinatorOscOnlyLifetimeTests
{
    [Fact]
    public void StartOscOnlyAsync_UsesOwnedRuntimeCancellationAfterStartup()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var body = NormalizeWhitespace(GetMethodBody(source, "private async Task StartOscOnlyCoreAsync"));

        Assert.Contains(
            "await oscRouterService.StartAsync(GetOscSubscriptionRules(configuration), cancellationToken)",
            body,
            StringComparison.Ordinal);
        Assert.Contains("runtimeCancellation ??= new CancellationTokenSource()", body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "runtimeCancellation ??= CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)",
            body,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CreateLinkedTokenSource(cancellationToken)", body, StringComparison.Ordinal);

        var stopBody = NormalizeWhitespace(GetMethodBody(source, "private async Task StopCoreAsync()"));
        var cancellationCapture = Regex.Match(
            stopBody,
            @"var (?<runtimeCancellationVariable>\w+) = runtimeCancellation;",
            RegexOptions.CultureInvariant);
        Assert.True(
            cancellationCapture.Success,
            "StopAsync should capture runtimeCancellation before canceling the owned source.");

        var runtimeCancellationVariable = cancellationCapture.Groups["runtimeCancellationVariable"].Value;
        var cancelExpression = $"{runtimeCancellationVariable}?.Cancel()";
        var cancelIndex = stopBody.IndexOf(cancelExpression, StringComparison.Ordinal);
        Assert.True(
            cancelIndex > cancellationCapture.Index,
            "StopAsync should cancel the captured runtime cancellation source after capturing it.");
    }

    private static string GetMethodBody(string source, string methodSignatureStart)
    {
        var methodStart = source.IndexOf(methodSignatureStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Could not find method signature starting with '{methodSignatureStart}'.");

        var bodyStart = source.IndexOf('{', methodStart);
        Assert.True(bodyStart >= 0, $"Could not find method body for '{methodSignatureStart}'.");
        return GetBalancedBlock(source, bodyStart);
    }

    private static string GetBalancedBlock(string source, int bodyStart)
    {
        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[bodyStart..(index + 1)];
                }
            }
        }

        throw new InvalidOperationException("Could not find the end of a source block.");
    }

    private static string NormalizeWhitespace(string source) => Regex.Replace(source, @"\s+", " ");

    private static string FindSourceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find source file.", Path.Combine(relativeParts));
    }
}
