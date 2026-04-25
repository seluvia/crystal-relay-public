using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VrcTwitchOscBridge.Services;

/// <summary>
/// Loads and saves optional runtime config.
/// Runtime config is now mostly a compatibility layer, so failures here should not stop startup.
/// </summary>
public sealed class RuntimeConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string configPath;

    public RuntimeConfigStore()
    {
        AppDataPaths.MigrateLegacyRootIfNeeded();
        configPath = AppDataPaths.RuntimeConfigPath;
    }

    public string ConfigPath => configPath;

    // Runtime config is best-effort. If the file is missing or bad, Crystal Relay falls back to defaults.
    public async Task<RuntimeConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(configPath))
        {
            return RuntimeConfig.CreateDefault();
        }

        try
        {
            var json = await File.ReadAllTextAsync(configPath, cancellationToken);
            var config = JsonSerializer.Deserialize<RuntimeConfig>(json, SerializerOptions)?.Normalize()
                ?? RuntimeConfig.CreateDefault();

            if (ConfigNeedsRewrite(json))
            {
                await SaveAsync(config, cancellationToken);
            }

            return config;
        }
        catch
        {
            return RuntimeConfig.CreateDefault();
        }
    }

    // Save keeps the file normalized, but write failures are intentionally treated as non-fatal.
    public async Task SaveAsync(RuntimeConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            var normalized = config.Normalize();
            var json = JsonSerializer.Serialize(normalized, SerializerOptions);

            if (File.Exists(configPath))
            {
                File.SetAttributes(configPath, FileAttributes.Normal);
            }

            await File.WriteAllTextAsync(configPath, json, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            // Runtime config is optional now that the Twitch app ID is built in.
            // If an old file is locked or read-only, keep running with defaults.
        }
        catch (IOException)
        {
            // Treat the runtime config as best-effort to avoid blocking startup.
        }
    }

    // Rewrite check is used to normalize older runtime config files into the current expected shape.
    private static bool ConfigNeedsRewrite(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            var currentNode = JsonNode.Parse(json);
            if (currentNode is not JsonObject)
            {
                return true;
            }

            var normalizedConfig = JsonSerializer.Deserialize<RuntimeConfig>(json, SerializerOptions)?.Normalize()
                ?? RuntimeConfig.CreateDefault();
            var normalizedNode = JsonSerializer.SerializeToNode(normalizedConfig, SerializerOptions);

            return normalizedNode is null || !JsonNode.DeepEquals(currentNode, normalizedNode);
        }
        catch
        {
            return true;
        }
    }
}
