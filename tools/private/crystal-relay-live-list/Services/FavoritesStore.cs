using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CrystalRelayLiveList.Services;

public sealed class FavoritesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string path;
    private readonly HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);

    public FavoritesStore(string path)
    {
        this.path = path;
        Load();
    }

    public IReadOnlyCollection<string> Keys => keys;

    public bool IsFavorite(string key) => keys.Contains(key);

    public bool Toggle(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }
        if (!keys.Add(key))
        {
            keys.Remove(key);
            Save();
            return false;
        }
        Save();
        return true;
    }

    private void Load()
    {
        if (!File.Exists(path))
        {
            return;
        }
        try
        {
            var json = File.ReadAllText(path);
            var payload = JsonSerializer.Deserialize<FavoritesPayload>(json, JsonOptions);
            keys.Clear();
            if (payload?.Keys is not null)
            {
                foreach (var k in payload.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(k))
                    {
                        keys.Add(k);
                    }
                }
            }
        }
        catch
        {
            // ignore corrupt file
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var payload = new FavoritesPayload { Keys = keys.ToList() };
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }));
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        catch
        {
            // favorites are a convenience; never throw
        }
    }

    private sealed class FavoritesPayload
    {
        public List<string> Keys { get; set; } = new();
    }
}
