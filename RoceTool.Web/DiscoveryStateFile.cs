using System.Text.Json;
using RoceTool.Core;

public static class DiscoveryStateFile
{
    private const string FileName = "discovery-state.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static DiscoverySettings Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path)) return new DiscoverySettings();
        try { return JsonSerializer.Deserialize<DiscoverySettings>(File.ReadAllText(path)) ?? new DiscoverySettings(); }
        catch { return new DiscoverySettings(); }
    }

    public static void Save(string dataDirectory, DiscoverySettings settings) =>
        File.WriteAllText(Path.Combine(dataDirectory, FileName), JsonSerializer.Serialize(settings, Options));
}
