using System.Text.Json;
using RoceTool.Core;

public static class ExternalTargetStateFile
{
    private const string FileName = "external-targets.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static ExternalTargetSettings Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path)) return new ExternalTargetSettings();
        try
        {
            return JsonSerializer.Deserialize<ExternalTargetSettings>(File.ReadAllText(path)) ?? new ExternalTargetSettings();
        }
        catch { return new ExternalTargetSettings(); }
    }

    public static void Save(string dataDirectory, ExternalTargetSettings settings)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, FileName);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}
