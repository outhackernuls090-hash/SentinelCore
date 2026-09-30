using System.Text.Json;

namespace Sentinel.Shared;

public class PinStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, string> _pins = new();

    public PinStore(string path)
    {
        _path = path;
        Load();
    }

    public static string KeyFor(string url)
    {
        var u = new Uri(url);
        return $"{u.Host}:{u.Port}";
    }

    public string? Get(string url)
    {
        lock (_gate) return _pins.TryGetValue(KeyFor(url), out var v) ? v : null;
    }

    public void Set(string url, string fingerprint)
    {
        lock (_gate)
        {
            _pins[KeyFor(url)] = fingerprint;
            Save();
        }
    }

    public static string Format(string fingerprint)
    {
        var parts = new List<string>();
        for (int i = 0; i + 1 < fingerprint.Length; i += 2) parts.Add(fingerprint.Substring(i, 2));
        return string.Join(":", parts);
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
                _pins = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? new Dictionary<string, string>();
        }
        catch
        {
            _pins = new Dictionary<string, string>();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_pins, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }
}
