using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace CrystalRelayLiveList.Services;

public sealed class DevCommandService
{
    private const int DefaultHistoryCapacity = 25;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string? presetsPath;
    private readonly int historyCapacity;
    private readonly LinkedList<string> copyHistory = new();

    public DevCommandService(string? presetsPath = null, int historyCapacity = DefaultHistoryCapacity)
    {
        this.presetsPath = presetsPath;
        this.historyCapacity = historyCapacity;
    }

    public string BuildGrow(double meters, int seconds, double transition) =>
        string.Format(CultureInfo.InvariantCulture, "!screm grow {0:0.###} {1} {2:0.###}", meters, seconds, transition);

    public string BuildShrink(double meters, int seconds, double transition) =>
        string.Format(CultureInfo.InvariantCulture, "!screm shrink {0:0.###} {1} {2:0.###}", meters, seconds, transition);

    public string BuildScaleRandom(double min, double max, int seconds) =>
        string.Format(CultureInfo.InvariantCulture, "!screm scalerandom {0:0.###}-{1:0.###} {2}", min, max, seconds);

    public string BuildMove(string direction, int seconds) =>
        string.Format(CultureInfo.InvariantCulture, "!screm move {0} {1}", direction, seconds);

    public string BuildSnapLeft(int seconds) =>
        string.Format(CultureInfo.InvariantCulture, "!screm move snapleft {0}", seconds);

    public string BuildSnapRight(int seconds) =>
        string.Format(CultureInfo.InvariantCulture, "!screm move snapright {0}", seconds);

    public string BuildMoveRandom(int seconds) =>
        string.Format(CultureInfo.InvariantCulture, "!screm moverandom {0}", seconds);

    public string BuildFireSale(int percent, int seconds) =>
        string.Format(CultureInfo.InvariantCulture, "!screm firesale {0} {1}", percent, seconds);

    public void RecordCopy(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }
        copyHistory.Remove(command);
        copyHistory.AddFirst(command);
        while (copyHistory.Count > historyCapacity)
        {
            copyHistory.RemoveLast();
        }
    }

    public IReadOnlyList<string> CopyHistory() => copyHistory.ToList();

    public IReadOnlyDictionary<string, string> LoadPresets()
    {
        if (presetsPath is null || !File.Exists(presetsPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        try
        {
            var json = File.ReadAllText(presetsPath);
            var payload = JsonSerializer.Deserialize<DevCommandPresetsPayload>(json, JsonOptions);
            return payload?.Presets is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(payload.Presets, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SavePreset(string name, string command)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        var current = new Dictionary<string, string>(LoadPresets(), StringComparer.OrdinalIgnoreCase);
        current[name.Trim()] = command;
        WritePresets(current);
    }

    public bool DeletePreset(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }
        var current = new Dictionary<string, string>(LoadPresets(), StringComparer.OrdinalIgnoreCase);
        if (!current.Remove(name.Trim()))
        {
            return false;
        }
        WritePresets(current);
        return true;
    }

    private void WritePresets(IReadOnlyDictionary<string, string> presets)
    {
        if (presetsPath is null)
        {
            return;
        }
        try
        {
            var dir = Path.GetDirectoryName(presetsPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var payload = new DevCommandPresetsPayload { Presets = presets.ToDictionary(k => k.Key, v => v.Value) };
            var temp = presetsPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }));
            if (File.Exists(presetsPath))
            {
                File.Replace(temp, presetsPath, null);
            }
            else
            {
                File.Move(temp, presetsPath);
            }
        }
        catch
        {
            // presets are a convenience; never throw
        }
    }

    private sealed class DevCommandPresetsPayload
    {
        public Dictionary<string, string> Presets { get; set; } = new();
    }
}
