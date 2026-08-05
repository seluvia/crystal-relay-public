using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class WorldCommandPrivacyRegressionTests
{
    private const string GenericProtectedWorldMessage =
        "This VRChat world is protected right now, so Crystal Relay is not sharing the world link.";
    private const string BuildWorldCommandSignature =
        "private async Task<string> BuildWorldCommandMessageAsync(";
    private const string CurrentWorldLookupSignature =
        "private async Task<VrChatCurrentWorldLookupResult> GetCurrentWorldForCommandAsync(";

    [Fact]
    public void BridgeCoordinator_DoesNotExposeWorldGuardReasons()
    {
        var source = File.ReadAllText(GetBridgeCoordinatorPath());
        var method = ExtractVerifiedTargetMethodBlock(
            source,
            BuildWorldCommandSignature,
            "BuildWorldCommandMessageAsync",
            typeof(Task<string>),
            typeof(BridgeRuntimeConfiguration),
            typeof(int),
            typeof(CancellationToken));
        var guardEvaluations = Regex.Matches(
            method.Code,
            @"\bworldCommandBlacklistService\s*\.\s*EvaluateAsync\s*\(").Cast<Match>();
        var guardEvaluation = Assert.Single(guardEvaluations);
        var blockedConditions = Regex.Matches(
            method.Code,
            @"\bif\s*\(\s*blacklistDecision\s*\.\s*IsBlocked\s*\)").Cast<Match>();
        var blockedCondition = Assert.Single(blockedConditions);
        var blockedBranch = ExtractBlockAfter(method, blockedCondition.Index + blockedCondition.Length);

        Assert.True(
            guardEvaluation.Index < blockedCondition.Index,
            "World Guard must be evaluated before the blocked branch is considered.");
        var blockedReturns = GetExecutableReturns(blockedBranch);
        var blockedReturn = Assert.Single(blockedReturns);
        var genericReturnPattern = $@"^\s*return\s+T\s*\(\s*""{Regex.Escape(GenericProtectedWorldMessage)}""\s*\)\s*;\s*$";
        Assert.Matches(genericReturnPattern, GetOriginalRange(blockedBranch, blockedReturn));

        const string blockedBranchShape =
            @"^\s*\{\s*WriteLog\s*\(\s*blacklistDecision\s*\.\s*IsFailClosed\s*\?\s*T\s*\(\s*\)\s*:\s*T\s*\(\s*\)\s*\)\s*;\s*return\s+T\s*\(\s*\)\s*;\s*\}\s*$";
        Assert.True(
            Regex.IsMatch(blockedBranch.Code, blockedBranchShape, RegexOptions.Singleline),
            "The blocked branch must contain only the static log ternary and generic return.");
        var privateDetailIdentifiers = Regex.Matches(blockedBranch.Code, @"\b[A-Za-z_]\w*\b")
            .Cast<Match>()
            .Select(match => match.Value)
            .Where(identifier => identifier.Contains("reason", StringComparison.OrdinalIgnoreCase)
                || identifier.Contains("explanation", StringComparison.OrdinalIgnoreCase)
                || identifier.Contains("detail", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Empty(privateDetailIdentifiers);

        var blockedInvocationPaths = GetInvocationPaths(blockedBranch.InnerCode);
        Assert.All(
            blockedInvocationPaths,
            invocationPath => Assert.True(
                invocationPath is "WriteLog" or "T",
                $"Unexpected helper flow in blocked branch: {invocationPath}"));

        var methodReturns = GetExecutableReturns(method);
        var unavailableReturnPattern = $@"^\s*return\s+T\s*\(\s*""{Regex.Escape("Crystal Relay could not find a public VRChat world to share right now.")}""\s*\)\s*;\s*$";
        var currentWorldReturnPattern = $@"^\s*return\s+SanitizeBotMessage\s*\(\s*TF\s*\(\s*""{Regex.Escape("Current VRChat world: {0} - {1}")}""\s*,\s*world\s*\.\s*WorldName\s*,\s*world\s*\.\s*WorldUrl\s*\)\s*\)\s*;\s*$";
        Assert.Collection(
            methodReturns,
            unavailableReturn => Assert.Matches(
                unavailableReturnPattern,
                GetOriginalRange(method, unavailableReturn)),
            protectedReturn => Assert.Matches(
                genericReturnPattern,
                GetOriginalRange(method, protectedReturn)),
            currentWorldReturn => Assert.Matches(
                currentWorldReturnPattern,
                GetOriginalRange(method, currentWorldReturn)));
        Assert.DoesNotMatch(@"\b(?:goto|throw)\b", method.Code);

        // Defense in depth: the coordinator must not regain the deleted private-reason member flow.
        Assert.DoesNotMatch(@"(?i)blacklistDecision\s*\.\s*Reason", method.Code);
        Assert.DoesNotContain(
            "This VRChat world is protected for this reason:",
            method.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BridgeCoordinator_CurrentWorldLookupIsFreshAndSerialized()
    {
        var source = File.ReadAllText(GetBridgeCoordinatorPath());

        Assert.DoesNotContain("cachedWorldCommandResult", source, StringComparison.Ordinal);
        Assert.DoesNotContain("cachedWorldCommandUserId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("cachedWorldCommandResultExpiresAt", source, StringComparison.Ordinal);

        var method = ExtractVerifiedTargetMethodBlock(
            source,
            CurrentWorldLookupSignature,
            "GetCurrentWorldForCommandAsync",
            typeof(Task<VrChatCurrentWorldLookupResult>),
            typeof(BridgeRuntimeConfiguration),
            typeof(int),
            typeof(CancellationToken));
        ValidateLookupGateCardinality(method.Code);
        var methodReturns = GetExecutableReturns(method);
        const string unavailableReturnPattern =
            @"^\s*return\s+VrChatCurrentWorldLookupResult\s*\.\s*Unavailable\s*;\s*$";
        const string directFreshReturnPattern =
            @"^\s*return\s+await\s+vrChatApiClient\s*\.\s*GetCurrentWorldAsync\s*\(\s*configuration\s*\.\s*VrChatSession\s*\.\s*AuthCookie\s*,\s*cancellationToken\s*\)\s*;\s*$";
        Assert.Collection(
            methodReturns,
            disconnectedReturn => Assert.Matches(
                unavailableReturnPattern,
                GetOriginalRange(method, disconnectedReturn)),
            freshReturn => Assert.Matches(
                directFreshReturnPattern,
                GetOriginalRange(method, freshReturn)),
            catchReturn => Assert.Matches(
                unavailableReturnPattern,
                GetOriginalRange(method, catchReturn)));

        var gateWaits = Regex.Matches(
            method.Code,
            @"\bawait\s+worldCommandLookupGate\s*\.\s*WaitAsync\s*\(\s*cancellationToken\s*\)\s*;").Cast<Match>();
        var gateWait = Assert.Single(gateWaits);
        var tryKeywords = Regex.Matches(method.Code, @"\btry\b").Cast<Match>();
        var tryKeyword = Assert.Single(tryKeywords);
        var tryBlock = ExtractBlockAfter(method, tryKeyword.Index + tryKeyword.Length);
        var worldLookupCalls = Regex.Matches(method.Code, @"\bGetCurrentWorldAsync\s*\(").Cast<Match>();
        var worldLookupCall = Assert.Single(worldLookupCalls);

        Assert.True(
            gateWait.Index + gateWait.Length <= tryKeyword.Index,
            "The serialized gate must be acquired before entering try.");
        Assert.True(
            string.IsNullOrWhiteSpace(method.Code.Substring(
                gateWait.Index + gateWait.Length,
                tryKeyword.Index - gateWait.Index - gateWait.Length)),
            "Nothing may run between gate acquisition and the lookup try block.");
        Assert.True(
            worldLookupCall.Index > tryBlock.OpenBraceIndex
                && worldLookupCall.Index < tryBlock.CloseBraceIndex,
            "The fresh current-world API call must be inside try.");

        const string directFreshTryShape =
            @"^\s*\{\s*return\s+await\s+vrChatApiClient\s*\.\s*GetCurrentWorldAsync\s*\(\s*configuration\s*\.\s*VrChatSession\s*\.\s*AuthCookie\s*,\s*cancellationToken\s*\)\s*;\s*\}\s*$";
        Assert.True(
            Regex.IsMatch(tryBlock.Code, directFreshTryShape, RegexOptions.Singleline),
            "The try block must contain only the direct awaited fresh-world API return.");
        var tryReturn = Assert.Single(GetExecutableReturns(tryBlock));
        Assert.Matches(directFreshReturnPattern, GetOriginalRange(tryBlock, tryReturn));

        var apiClientCalls = Regex.Matches(
            method.Code,
            @"\bvrChatApiClient\s*\.\s*(?<method>[A-Za-z_]\w*)\s*\(").Cast<Match>();
        var apiClientCall = Assert.Single(apiClientCalls);
        Assert.Equal("GetCurrentWorldAsync", apiClientCall.Groups["method"].Value);

        var awaitedInvocationPaths = Regex.Matches(
                method.Code,
                @"\bawait\s+(?<path>[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*\(")
            .Cast<Match>()
            .Select(match => Regex.Replace(match.Groups["path"].Value, @"\s+", string.Empty))
            .ToArray();
        Assert.Equal(
            ["worldCommandLookupGate.WaitAsync", "vrChatApiClient.GetCurrentWorldAsync"],
            awaitedInvocationPaths);

        var finallyKeywords = Regex.Matches(method.Code, @"\bfinally\b").Cast<Match>();
        var finallyKeyword = Assert.Single(finallyKeywords);
        Assert.True(
            finallyKeyword.Index > tryBlock.CloseBraceIndex,
            "finally must follow the lookup try block and its catches.");

        var catches = Regex.Matches(method.Code, @"\bcatch\b").Cast<Match>().ToArray();
        Assert.NotEmpty(catches);
        Assert.All(
            catches,
            catchKeyword => Assert.True(
                catchKeyword.Index > tryBlock.CloseBraceIndex
                    && catchKeyword.Index < finallyKeyword.Index,
                "Every catch must appear after try and before finally."));

        var handlerCursor = tryBlock.CloseBraceIndex + 1;
        foreach (var catchKeyword in catches)
        {
            Assert.True(
                string.IsNullOrWhiteSpace(method.Code.Substring(
                    handlerCursor,
                    catchKeyword.Index - handlerCursor)),
                "Only ordered catch handlers may appear between try and finally.");
            var catchBlock = ExtractBlockAfter(method, catchKeyword.Index + catchKeyword.Length);
            Assert.True(
                catchBlock.CloseBraceIndex < finallyKeyword.Index,
                "Every catch block must end before finally.");
            handlerCursor = catchBlock.CloseBraceIndex + 1;
        }

        Assert.True(
            string.IsNullOrWhiteSpace(method.Code.Substring(
                handlerCursor,
                finallyKeyword.Index - handlerCursor)),
            "finally must directly follow the ordered catch handlers.");

        var finallyBlock = ExtractBlockAfter(method, finallyKeyword.Index + finallyKeyword.Length);
        Assert.True(
            Regex.IsMatch(
                finallyBlock.Code,
                @"^\s*\{\s*worldCommandLookupGate\s*\.\s*Release\s*\(\s*\)\s*;\s*\}\s*$",
                RegexOptions.Singleline),
            "finally must contain only the lookup-gate release.");
        Assert.Single(
            Regex.Matches(
                method.Code,
                @"\bworldCommandLookupGate\s*\.\s*Release\s*\(\s*\)").Cast<Match>());
        Assert.True(
            string.IsNullOrWhiteSpace(method.Code.Substring(
                finallyBlock.CloseBraceIndex + 1,
                method.Code.Length - finallyBlock.CloseBraceIndex - 2)),
            "finally must be the last block in the lookup method.");

        Assert.DoesNotMatch(@"(?i)\b(?:lock|stateGate)\b", method.Code);
        Assert.DoesNotMatch(
            @"(?i)\b(?:cache\w*|last[\s_-]*known\w*|previous\w*|fallback\w*|configured\w*|local[\s_-]*low\w*)\b",
            method.Code);
        Assert.DoesNotMatch(
            @"(?i)\b(?:currentWorld(?:Source|Provider|Service|Resolver|Repository|Cache)|world(?:Source|Provider|Service|Resolver|Repository|Cache)|localAvatar|localOsc|oscCache|location(?:Source|Provider|Service|Resolver)|instance(?:Source|Provider|Service|Resolver))\w*\b",
            method.Code);
        Assert.DoesNotMatch(@"\b(?:File|Directory|SettingsStore|AppDataPaths)\s*\.", method.Code);

        var worldRelatedInvocations = GetInvocationPaths(method.Code)
            .Where(path => path.Contains("world", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.All(
            worldRelatedInvocations,
            invocationPath => Assert.True(
                invocationPath is "worldCommandLookupGate.WaitAsync"
                    or "vrChatApiClient.GetCurrentWorldAsync"
                    or "worldCommandLookupGate.Release",
                $"Unexpected current-world source or helper call: {invocationPath}"));

        var sourceLikeInvocations = GetInvocationPaths(method.Code)
            .Where(path => Regex.IsMatch(
                path[(path.LastIndexOf('.') + 1)..],
                @"^(?:Get|Read|Load|Resolve|Fetch|Find)",
                RegexOptions.IgnoreCase))
            .ToArray();
        var sourceLikeInvocation = Assert.Single(sourceLikeInvocations);
        Assert.Equal("vrChatApiClient.GetCurrentWorldAsync", sourceLikeInvocation);
    }

    [Fact]
    public void EveryLocale_HasNonemptyGenericProtectedWorldMessageWithoutPlaceholders()
    {
        var localizationDirectory = Path.Combine(
            FindRepositoryRoot(),
            "VrcTwitchOscBridge",
            "Resources",
            "Localization");
        var localeFiles = Directory.GetFiles(localizationDirectory, "*.json", SearchOption.TopDirectoryOnly);

        Assert.NotEmpty(localeFiles);
        foreach (var localeFile in localeFiles.OrderBy(path => path, StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(localeFile));
            Assert.True(
                document.RootElement.TryGetProperty(GenericProtectedWorldMessage, out var localizedValue),
                $"{Path.GetFileName(localeFile)} is missing the generic protected-world message.");
            Assert.Equal(JsonValueKind.String, localizedValue.ValueKind);

            var value = localizedValue.GetString();
            Assert.False(
                string.IsNullOrWhiteSpace(value),
                $"{Path.GetFileName(localeFile)} has an empty generic protected-world message.");
            Assert.DoesNotContain("{", value!, StringComparison.Ordinal);
            Assert.DoesNotContain("}", value!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MaskNonCode_HidesFakeCodeAcrossCommentsAndNonInterpolatedLiteralForms()
    {
        var source = string.Join(
            "\n",
            "private void Sample()",
            "{",
            "    // worldCommandLookupGate.WaitAsync(cancellationToken); return FakeLineComment();",
            "    /* return T(\"generic\"); { return FakeBlockComment(); } */",
            "    var character = '}';",
            "    var escapedCharacter = '\\'';",
            "    var regular = \"escaped \\\" } return FakeRegular();\";",
            "    var verbatim = @\"doubled \"\" quote } return FakeVerbatim();\";",
            "    var raw = \"\"\"{ return FakeRaw(); }\"\"\";",
            "    return Real();",
            "}");

        var code = MaskNonCode(source);
        var method = ExtractMethodBlock(source, "private void Sample()");
        var executableReturns = GetExecutableReturns(method);

        AssertMaskPreservesLayout(source, code);
        Assert.DoesNotContain("Fake", code, StringComparison.Ordinal);
        var executableReturn = Assert.Single(executableReturns);
        Assert.Equal("return Real();", method.Text.Substring(executableReturn.Index, executableReturn.Length));
    }

    [Fact]
    public void MaskNonCode_FourQuoteRawDelimiterIgnoresShorterQuoteRun()
    {
        var source = string.Join(
            "\n",
            "private void Sample()",
            "{",
            "    var raw = \"\"\"\"before \"\"\" return FakeShortDelimiter(); { after\"\"\"\";",
            "    return Real();",
            "}");

        var code = MaskNonCode(source);
        var method = ExtractMethodBlock(source, "private void Sample()");
        var executableReturns = GetExecutableReturns(method);

        AssertMaskPreservesLayout(source, code);
        Assert.DoesNotContain("FakeShortDelimiter", code, StringComparison.Ordinal);
        var executableReturn = Assert.Single(executableReturns);
        Assert.Equal("return Real();", method.Text.Substring(executableReturn.Index, executableReturn.Length));
    }

    [Fact]
    public void MaskNonCode_LeavesRenamedFallbackReturnExecutable()
    {
        var source = string.Join(
            "\n",
            "private object Lookup()",
            "{",
            "    if (!connected) return Unavailable;",
            "    // return CommentedFallback;",
            "    try { return Fresh(); }",
            "    catch { return renamedFallbackValue; }",
            "}");
        var method = ExtractMethodBlock(source, "private object Lookup()");
        var executableReturns = GetExecutableReturns(method);

        Assert.Collection(
            executableReturns,
            first => Assert.Equal(
                "return Unavailable;",
                method.Text.Substring(first.Index, first.Length)),
            second => Assert.Equal(
                "return Fresh();",
                method.Text.Substring(second.Index, second.Length)),
            third => Assert.Equal(
                "return renamedFallbackValue;",
                method.Text.Substring(third.Index, third.Length)));
    }

    [Theory]
    [InlineData("/* unterminated")]
    [InlineData("\"unterminated")]
    [InlineData("'x")]
    [InlineData("@\"unterminated")]
    [InlineData("$\"{value}")]
    [InlineData("\"\"\"unterminated")]
    [InlineData("$$$$\"\"\"\"unterminated \"\"\"")]
    public void MaskNonCode_RejectsUnterminatedConstructs(string construct)
    {
        var source = $"private void Sample()\n{{\n    var value = {construct};\n}}";

        Assert.Throws<InvalidOperationException>(() => MaskNonCode(source));
    }

    [Theory]
    [InlineData("$\"{blacklistDecision.Reason}\"")]
    [InlineData("$\"{await vrChatApiClient.GetCurrentWorldAsync(configuration.VrChatSession.AuthCookie, cancellationToken)}\"")]
    [InlineData("$@\"{blacklistDecision.Reason}\"")]
    [InlineData("@$\"{blacklistDecision.Reason}\"")]
    [InlineData("$\"\"\"raw {blacklistDecision.Reason}\"\"\"")]
    [InlineData("$$\"\"\"\"before \"\"\" {{ /* nested */ \"value\" }} after\"\"\"\"")]
    [InlineData("$$$$\"\"\"{ /* fake */ }\"\"\"")]
    public void ExtractMethodBlock_RejectsInterpolatedStrings(string interpolatedLiteral)
    {
        var source = string.Join(
            "\n",
            "private void Sample()",
            "{",
            $"    var value = {interpolatedLiteral};",
            "    return Real();",
            "}");

        var exception = Assert.Throws<InvalidOperationException>(
            () => ExtractMethodBlock(source, "private void Sample()"));
        Assert.Contains("Interpolated C# strings are not allowed", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"escaped\\\ncontinued\"")]
    [InlineData("\"escaped\\\r\ncontinued\"")]
    [InlineData("'\\\n'")]
    [InlineData("'\\\r\n'")]
    [InlineData("\"raw\rcontinued\"")]
    [InlineData("\"raw\ncontinued\"")]
    [InlineData("'raw\r'")]
    [InlineData("'raw\n'")]
    public void MaskNonCode_RejectsRawOrEscapedNewlinesInRegularAndCharacterLiterals(string literal)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => MaskNonCode(literal));

        Assert.Contains("newline", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MaskNonCode_LineCommentEndsAtCarriageReturn()
    {
        const string source = "private void Sample()\r{\r// return Fake(); {\rreturn Real();\r}";

        var code = MaskNonCode(source);
        var method = ExtractMethodBlock(source, "private void Sample()");
        var executableReturn = Assert.Single(GetExecutableReturns(method));

        AssertMaskPreservesLayout(source, code);
        Assert.DoesNotContain("Fake", code, StringComparison.Ordinal);
        Assert.Equal("return Real();", GetOriginalRange(method, executableReturn));
    }

    [Fact]
    public void ExtractMethodBlock_StartsLexingAtLocatedSignature()
    {
        const string source = "$\"unterminated before target\nprivate void Sample()\n{\n    return Real();\n}";

        var method = ExtractMethodBlock(source, "private void Sample()");
        var executableReturn = Assert.Single(GetExecutableReturns(method));

        Assert.Equal("return Real();", GetOriginalRange(method, executableReturn));
    }

    [Theory]
    [InlineData("await worldCommandLookupGate.WaitAsync(cancellationToken); worldCommandLookupGate.WaitAsync(default); worldCommandLookupGate.Release();")]
    [InlineData("await worldCommandLookupGate.WaitAsync(cancellationToken); worldCommandLookupGate.Release(2); worldCommandLookupGate.Release();")]
    public void LookupGateCardinality_RejectsExtraInvocationForms(string code)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ValidateLookupGateCardinality(code));

        Assert.Contains("worldCommandLookupGate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifiedTargetExtraction_RejectsRawSignatureDecoysBeforeOrAfterRealMethod()
    {
        var realMethod = $"{BuildWorldCommandSignature})\n{{\n}}";
        var decoyBlock = $"{BuildWorldCommandSignature}) {{ }}";
        var sources = new[]
        {
            $"/* {decoyBlock} */\n{realMethod}",
            $"{realMethod}\n/* {decoyBlock} */",
            $"var decoy = \"{decoyBlock}\";\n{realMethod}",
            $"{realMethod}\nvar decoy = \"{decoyBlock}\";",
            $"var decoy = \"\"\"{decoyBlock}\"\"\";\n{realMethod}",
            $"{realMethod}\nvar decoy = \"\"\"{decoyBlock}\"\"\";"
        };

        foreach (var source in sources)
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ExtractVerifiedTargetMethodBlock(
                    source,
                    BuildWorldCommandSignature,
                    "BuildWorldCommandMessageAsync",
                    typeof(Task<string>),
                    typeof(BridgeRuntimeConfiguration),
                    typeof(int),
                    typeof(CancellationToken)));
            Assert.Contains("exactly one source occurrence", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void VerifiedTargetExtraction_RejectsSoleSourceMethodWithoutReflectedShape()
    {
        const string signature = "private async Task<string> MissingWorldMethodAsync(";
        var source = $"{signature})\n{{\n}}";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ExtractVerifiedTargetMethodBlock(
                source,
                signature,
                "MissingWorldMethodAsync",
                typeof(Task<string>),
                typeof(BridgeRuntimeConfiguration),
                typeof(int),
                typeof(CancellationToken)));
        Assert.Contains("compiled BridgeCoordinator method", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"\"\"before \"\" after\"\"\"")]
    [InlineData("\"\"\"\"before \"\"\" after\"\"\"\"")]
    public void MaskNonCode_RawStringAllowsShorterRunsAndExactClosingDelimiter(string literal)
    {
        var source = $"private void Sample()\n{{\nvar value = {literal};\nreturn Real();\n}}";

        var method = ExtractMethodBlock(source, "private void Sample()");
        var executableReturn = Assert.Single(GetExecutableReturns(method));

        Assert.Equal("return Real();", GetOriginalRange(method, executableReturn));
    }

    [Theory]
    [InlineData("\"\"\"before \"\"\"\" after \"\"\"")]
    [InlineData("\"\"\"\"before \"\"\"\"\" after \"\"\"\"")]
    public void MaskNonCode_RawStringRejectsOverlongRunBeforeLaterExactDelimiter(string literal)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => MaskNonCode(literal));

        Assert.Contains("longer than", exception.Message, StringComparison.Ordinal);
    }

    private static string GetBridgeCoordinatorPath() => Path.Combine(
        FindRepositoryRoot(),
        "VrcTwitchOscBridge",
        "Services",
        "BridgeCoordinator.cs");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var coordinatorPath = Path.Combine(
                directory.FullName,
                "VrcTwitchOscBridge",
                "Services",
                "BridgeCoordinator.cs");
            var localizationPath = Path.Combine(
                directory.FullName,
                "VrcTwitchOscBridge",
                "Resources",
                "Localization");
            if (File.Exists(coordinatorPath) && Directory.Exists(localizationPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository from {AppContext.BaseDirectory}.");
    }

    private static void AssertMaskPreservesLayout(string source, string code)
    {
        Assert.Equal(source.Length, code.Length);
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] is '\r' or '\n')
            {
                Assert.Equal(source[index], code[index]);
            }
        }
    }

    private static void ValidateLookupGateCardinality(string code)
    {
        var gateReferences = Regex.Matches(code, @"\bworldCommandLookupGate\b").Count;
        var waitInvocations = Regex.Matches(
            code,
            @"\bworldCommandLookupGate\s*\.\s*WaitAsync\s*\(").Count;
        var releaseInvocations = Regex.Matches(
            code,
            @"\bworldCommandLookupGate\s*\.\s*Release\s*\(").Count;

        if (gateReferences != 2 || waitInvocations != 1 || releaseInvocations != 1)
        {
            throw new InvalidOperationException(
                "Expected worldCommandLookupGate to have exactly two references, "
                + "one WaitAsync invocation, and one Release invocation; "
                + $"found {gateReferences}, {waitInvocations}, and {releaseInvocations}.");
        }
    }

    private static SourceBlock ExtractVerifiedTargetMethodBlock(
        string source,
        string methodSignature,
        string methodName,
        Type returnType,
        params Type[] parameterTypes)
    {
        var sourceOccurrences = CountOccurrences(source, methodSignature);
        if (sourceOccurrences != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one source occurrence of target signature '{methodSignature}', found {sourceOccurrences}.");
        }

        var namedMethods = typeof(BridgeCoordinator)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
            .ToArray();
        if (namedMethods.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one nonpublic instance compiled BridgeCoordinator method named '{methodName}', found {namedMethods.Length}.");
        }

        var reflectedMethod = namedMethods[0];
        var reflectedParameterTypes = reflectedMethod.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        if (reflectedMethod.ReturnType != returnType
            || !reflectedParameterTypes.SequenceEqual(parameterTypes))
        {
            throw new InvalidOperationException(
                $"The compiled BridgeCoordinator method '{methodName}' does not match the expected return and parameter type contract.");
        }

        return ExtractMethodBlock(source, methodSignature);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        for (var index = 0; index <= source.Length - value.Length;)
        {
            var occurrence = source.IndexOf(value, index, StringComparison.Ordinal);
            if (occurrence < 0)
            {
                break;
            }

            count++;
            index = occurrence + value.Length;
        }

        return count;
    }

    private static SourceBlock ExtractMethodBlock(string source, string methodSignatureStart)
    {
        var methodStart = source.IndexOf(methodSignatureStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Could not find method signature starting with '{methodSignatureStart}'.");

        var bodyStart = FindNextCodeOpeningBrace(source, methodStart + methodSignatureStart.Length);
        return ExtractBalancedBlock(source, bodyStart);
    }

    private static SourceBlock ExtractBlockAfter(SourceBlock source, int startIndex)
    {
        var bodyStart = source.Code.IndexOf('{', startIndex);
        return ExtractBalancedBlock(source.Text, bodyStart);
    }

    private static int FindNextCodeOpeningBrace(string source, int startIndex)
    {
        for (var index = startIndex; index < source.Length;)
        {
            if (TryScanComment(source, index, out var commentEnd))
            {
                index = commentEnd;
                continue;
            }

            if (TryScanLiteral(source, index, out var literalEnd))
            {
                index = literalEnd;
                continue;
            }

            if (source[index] == '{')
            {
                return index;
            }

            index++;
        }

        throw new InvalidOperationException("Could not find the target method's opening brace.");
    }

    private static SourceBlock ExtractBalancedBlock(string source, int bodyStart)
    {
        Assert.True(
            bodyStart >= 0 && bodyStart < source.Length && source[bodyStart] == '{',
            "Could not find the expected C# block opening brace.");

        var masked = source.ToCharArray();
        var depth = 0;
        for (var index = bodyStart; index < source.Length;)
        {
            if (TryScanComment(source, index, out var commentEnd))
            {
                MaskRange(masked, source, index, commentEnd);
                index = commentEnd;
                continue;
            }

            if (TryScanLiteral(source, index, out var literalEnd))
            {
                MaskRange(masked, source, index, literalEnd);
                index = literalEnd;
                continue;
            }

            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    var length = index - bodyStart + 1;
                    return new SourceBlock(
                        bodyStart,
                        index,
                        source.Substring(bodyStart, length),
                        new string(masked, bodyStart, length));
                }
            }

            index++;
        }

        throw new InvalidOperationException("Could not find the balanced C# block closing brace.");
    }

    private static Match[] GetExecutableReturns(SourceBlock source) =>
        Regex.Matches(source.Code, @"\breturn\b[\s\S]*?;")
            .Cast<Match>()
            .ToArray();

    private static string GetOriginalRange(SourceBlock source, Match codeMatch) =>
        source.Text.Substring(codeMatch.Index, codeMatch.Length);

    private static string MaskNonCode(string source)
    {
        var masked = source.ToCharArray();
        for (var index = 0; index < source.Length;)
        {
            if (TryScanComment(source, index, out var commentEnd))
            {
                MaskRange(masked, source, index, commentEnd);
                index = commentEnd;
                continue;
            }

            if (TryScanLiteral(source, index, out var literalEnd))
            {
                MaskRange(masked, source, index, literalEnd);
                index = literalEnd;
                continue;
            }

            index++;
        }

        return new string(masked);
    }

    private static bool TryScanComment(string source, int start, out int end)
    {
        end = start;
        if (start + 1 >= source.Length || source[start] != '/')
        {
            return false;
        }

        if (source[start + 1] == '/')
        {
            end = start + 2;
            while (end < source.Length && source[end] is not ('\r' or '\n'))
            {
                end++;
            }

            return true;
        }

        if (source[start + 1] != '*')
        {
            return false;
        }

        var blockEnd = source.IndexOf("*/", start + 2, StringComparison.Ordinal);
        if (blockEnd < 0)
        {
            throw new InvalidOperationException(
                $"Unterminated C# block comment starting at index {start}.");
        }

        end = blockEnd + 2;
        return true;
    }

    private static bool TryScanLiteral(string source, int start, out int end)
    {
        end = start;
        if (IsInterpolatedStringOpener(source, start))
        {
            throw new InvalidOperationException(
                $"Interpolated C# strings are not allowed in structural source (opener at index {start}).");
        }

        if (source[start] == '\'')
        {
            end = ScanCharacterLiteral(source, start);
            return true;
        }

        if (source[start] == '"')
        {
            var quoteCount = CountRun(source, start, '"');
            if (quoteCount >= 3)
            {
                end = ScanRawStringLiteral(source, start, start + quoteCount, quoteCount);
                return true;
            }

            end = ScanQuotedStringLiteral(source, start, start, isVerbatim: false);
            return true;
        }

        if (source[start] == '@' && start + 1 < source.Length && source[start + 1] == '"')
        {
            end = ScanQuotedStringLiteral(source, start, start + 1, isVerbatim: true);
            return true;
        }

        return false;
    }

    private static bool IsInterpolatedStringOpener(string source, int start)
    {
        if (source[start] == '@')
        {
            return start + 2 < source.Length
                && source[start + 1] == '$'
                && source[start + 2] == '"';
        }

        if (source[start] != '$')
        {
            return false;
        }

        var cursor = start;
        while (cursor < source.Length && source[cursor] == '$')
        {
            cursor++;
        }

        if (cursor < source.Length && source[cursor] == '"')
        {
            return true;
        }

        return cursor == start + 1
            && cursor + 1 < source.Length
            && source[cursor] == '@'
            && source[cursor + 1] == '"';
    }

    private static int ScanCharacterLiteral(string source, int start)
    {
        for (var cursor = start + 1; cursor < source.Length; cursor++)
        {
            if (source[cursor] is '\r' or '\n')
            {
                throw new InvalidOperationException(
                    $"C# character literal starting at index {start} contains a raw newline at index {cursor}.");
            }

            if (source[cursor] == '\\')
            {
                if (cursor + 1 >= source.Length)
                {
                    break;
                }

                if (source[cursor + 1] is '\r' or '\n')
                {
                    throw new InvalidOperationException(
                        $"C# character literal starting at index {start} contains an escaped newline at index {cursor + 1}.");
                }

                cursor++;
                continue;
            }

            if (source[cursor] == '\'')
            {
                return cursor + 1;
            }
        }

        throw new InvalidOperationException(
            $"Unterminated C# character literal starting at index {start}.");
    }

    private static int ScanQuotedStringLiteral(
        string source,
        int start,
        int quoteStart,
        bool isVerbatim)
    {
        for (var cursor = quoteStart + 1; cursor < source.Length; cursor++)
        {
            if (!isVerbatim && source[cursor] is '\r' or '\n')
            {
                throw new InvalidOperationException(
                    $"C# string literal starting at index {start} contains a raw newline at index {cursor}.");
            }

            if (!isVerbatim && source[cursor] == '\\')
            {
                if (cursor + 1 >= source.Length)
                {
                    break;
                }

                if (source[cursor + 1] is '\r' or '\n')
                {
                    throw new InvalidOperationException(
                        $"C# string literal starting at index {start} contains an escaped newline at index {cursor + 1}.");
                }

                cursor++;
                continue;
            }

            if (source[cursor] != '"')
            {
                continue;
            }

            if (isVerbatim && cursor + 1 < source.Length && source[cursor + 1] == '"')
            {
                cursor++;
                continue;
            }

            return cursor + 1;
        }

        var literalKind = isVerbatim ? "verbatim string" : "string";
        throw new InvalidOperationException(
            $"Unterminated C# {literalKind} literal starting at index {start}.");
    }

    private static int ScanRawStringLiteral(
        string source,
        int start,
        int contentStart,
        int delimiterQuoteCount)
    {
        for (var cursor = contentStart; cursor < source.Length;)
        {
            if (source[cursor] != '"')
            {
                cursor++;
                continue;
            }

            var quoteCount = CountRun(source, cursor, '"');
            if (quoteCount == delimiterQuoteCount)
            {
                return cursor + quoteCount;
            }

            if (quoteCount > delimiterQuoteCount)
            {
                throw new InvalidOperationException(
                    $"C# raw string literal starting at index {start} contains a {quoteCount}-quote run longer than its {delimiterQuoteCount}-quote delimiter.");
            }

            cursor += quoteCount;
        }

        throw new InvalidOperationException(
            $"Unterminated C# raw string literal with a {delimiterQuoteCount}-quote delimiter starting at index {start}.");
    }

    private static int CountRun(string source, int start, char value)
    {
        var cursor = start;
        while (cursor < source.Length && source[cursor] == value)
        {
            cursor++;
        }

        return cursor - start;
    }

    private static void MaskRange(char[] masked, string source, int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            if (source[index] is not ('\r' or '\n'))
            {
                masked[index] = ' ';
            }
        }
    }

    private static IReadOnlyList<string> GetInvocationPaths(string source) =>
        Regex.Matches(
                source,
                @"(?<![\w.])(?<path>[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*\(")
            .Cast<Match>()
            .Select(match => Regex.Replace(match.Groups["path"].Value, @"\s+", string.Empty))
            .ToArray();

    private readonly record struct SourceBlock(
        int OpenBraceIndex,
        int CloseBraceIndex,
        string Text,
        string Code)
    {
        public string InnerText => Text.Substring(1, Text.Length - 2);

        public string InnerCode => Code.Substring(1, Code.Length - 2);
    }
}
