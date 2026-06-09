using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace VrcTwitchOscBridge.Services;

internal static class ApplicationRestartService
{
    private const string HelperArgument = "--crystal-relay-restart-helper";
    private const string RestoreArgument = "--crystal-relay-restore-session";
    private const string VrChatProtocolCommandSubKey = @"vrchat\shell\open\command";
    private const string VrChatUserProtocolCommandSubKey = @"Software\Classes\vrchat\shell\open\command";
    private const string VrChatDesktopModeArgument = "--no-vr";
    private static readonly TimeSpan ParentExitTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan VrChatCloseTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RelaunchDelay = TimeSpan.FromMilliseconds(900);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private static string RestartSessionPath => Path.Combine(AppDataPaths.RootFolder, "restart-session.json");

    public static bool IsRestartSession { get; private set; }

    public static bool IsShuttingDownForRestart { get; private set; }

    public static bool TryCreateHelperRequest(string[] args, out ApplicationRestartHelperRequest request)
    {
        request = new ApplicationRestartHelperRequest();
        if (args.Length < 3 || !string.Equals(args[0], HelperArgument, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!int.TryParse(args[1], out var parentProcessId) || parentProcessId <= 0)
        {
            return false;
        }

        request = new ApplicationRestartHelperRequest
        {
            ParentProcessId = parentProcessId,
            SessionPath = args[2]
        };
        return true;
    }

    public static bool TryConsumeRestoreState(string[] args, out ApplicationRestartSessionState? state)
    {
        state = null;
        IsRestartSession = false;
        if (args.Length < 2 || !string.Equals(args[0], RestoreArgument, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        state = TryReadSessionState(args[1]);
        TryDeleteSessionFile(args[1]);
        IsRestartSession = state is not null;
        return true;
    }

    public static void StartRestartHelper(ApplicationRestartSessionState state)
    {
        IsShuttingDownForRestart = true;
        AppDataPaths.EnsureCoreFolders();
        var sessionPath = RestartSessionPath;
        var json = JsonSerializer.Serialize(state, SerializerOptions);
        File.WriteAllText(sessionPath, json);

        var executablePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Crystal Relay could not find its executable path.");

        var processInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        processInfo.ArgumentList.Add(HelperArgument);
        processInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        processInfo.ArgumentList.Add(sessionPath);

        Process.Start(processInfo);
    }

    public static async Task RunRestartHelperAsync(ApplicationRestartHelperRequest request)
    {
        var state = TryReadSessionState(request.SessionPath);
        if (state is null)
        {
            return;
        }

        await WaitForParentExitAsync(request.ParentProcessId);

        if (state.Mode == ApplicationRestartMode.VrChatAndCrystalRelay)
        {
            await RestartVrChatAsync(state.VrChatLaunchUri, state.RestartVrChatInDesktopMode);
        }

        await Task.Delay(RelaunchDelay);
        RelaunchCrystalRelay(request.SessionPath);
    }

    private static async Task WaitForParentExitAsync(int parentProcessId)
    {
        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            await parent.WaitForExitAsync().WaitAsync(ParentExitTimeout);
        }
        catch
        {
            await Task.Delay(RelaunchDelay);
        }
    }

    private static async Task RestartVrChatAsync(string? launchUri, bool restartInDesktopMode)
    {
        foreach (var process in Process.GetProcessesByName("VRChat"))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.CloseMainWindow();
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        await Task.Delay(VrChatCloseTimeout);

        foreach (var process in Process.GetProcessesByName("VRChat"))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        if (string.IsNullOrWhiteSpace(launchUri))
        {
            return;
        }

        TryLaunchVrChat(launchUri, restartInDesktopMode);
    }

    private static void TryLaunchVrChat(string launchUri, bool restartInDesktopMode)
    {
        try
        {
            if (TryGetVrChatLaunchExecutablePath(out var launchExecutablePath))
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = launchExecutablePath,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(launchExecutablePath) ?? string.Empty
                };
                processInfo.ArgumentList.Add(launchUri);
                if (restartInDesktopMode)
                {
                    processInfo.ArgumentList.Add(VrChatDesktopModeArgument);
                }

                Process.Start(processInfo)?.Dispose();
                return;
            }
        }
        catch
        {
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = launchUri,
                UseShellExecute = true
            })?.Dispose();
        }
        catch
        {
        }
    }

    private static bool TryGetVrChatLaunchExecutablePath(out string launchExecutablePath)
    {
        launchExecutablePath = string.Empty;
        if (!TryReadVrChatProtocolCommand(out var protocolCommand)
            || !TryExtractExecutablePath(protocolCommand, out var executablePath)
            || !File.Exists(executablePath))
        {
            return false;
        }

        launchExecutablePath = executablePath;
        return true;
    }

    private static bool TryReadVrChatProtocolCommand(out string protocolCommand)
    {
        protocolCommand = string.Empty;
        foreach (var pair in new[]
        {
            (Registry.CurrentUser, VrChatUserProtocolCommandSubKey),
            (Registry.ClassesRoot, VrChatProtocolCommandSubKey)
        })
        {
            try
            {
                using var key = pair.Item1.OpenSubKey(pair.Item2);
                if (key?.GetValue(null) is string command && !string.IsNullOrWhiteSpace(command))
                {
                    protocolCommand = command.Trim();
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool TryExtractExecutablePath(string command, out string executablePath)
    {
        executablePath = string.Empty;
        var trimmedCommand = command.Trim();
        if (string.IsNullOrWhiteSpace(trimmedCommand))
        {
            return false;
        }

        string candidate;
        if (trimmedCommand[0] == '"')
        {
            var closingQuoteIndex = trimmedCommand.IndexOf('"', 1);
            if (closingQuoteIndex <= 1)
            {
                return false;
            }

            candidate = trimmedCommand[1..closingQuoteIndex];
        }
        else
        {
            var firstSpaceIndex = trimmedCommand.IndexOf(' ');
            candidate = firstSpaceIndex < 0
                ? trimmedCommand
                : trimmedCommand[..firstSpaceIndex];
        }

        candidate = Environment.ExpandEnvironmentVariables(candidate.Trim());
        if (!candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        executablePath = candidate;
        return true;
    }

    private static void RelaunchCrystalRelay(string sessionPath)
    {
        var executablePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        var processInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Normal
        };
        processInfo.ArgumentList.Add(RestoreArgument);
        processInfo.ArgumentList.Add(sessionPath);

        Process.Start(processInfo);
    }

    private static ApplicationRestartSessionState? TryReadSessionState(string sessionPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sessionPath) || !File.Exists(sessionPath))
            {
                return null;
            }

            var json = File.ReadAllText(sessionPath);
            return JsonSerializer.Deserialize<ApplicationRestartSessionState>(json, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void TryDeleteSessionFile(string sessionPath)
    {
        try
        {
            if (File.Exists(sessionPath))
            {
                File.Delete(sessionPath);
            }
        }
        catch
        {
        }
    }
}

internal enum ApplicationRestartMode
{
    CrystalRelayOnly,
    VrChatAndCrystalRelay
}

internal sealed class ApplicationRestartHelperRequest
{
    public int ParentProcessId { get; init; }

    public string SessionPath { get; init; } = string.Empty;
}

internal sealed class ApplicationRestartSessionState
{
    public ApplicationRestartMode Mode { get; init; }

    public string? VrChatLaunchUri { get; init; }

    public bool RestartVrChatInDesktopMode { get; init; }

    public List<WindowPlacementSnapshot> Windows { get; init; } = [];
}
