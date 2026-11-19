using System.Text.Json;
using System.Text.Json.Nodes;

namespace Drydock.Core;

/// <param name="Theme">"System", "Light" or "Dark".</param>
public sealed record Settings(string? GameDir = null, string Theme = "System")
{
    public static string UserPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drydock", "settings.json");

    /// <summary>Next to the exe, for running Drydock from a stick. Overrides the user file key by key.</summary>
    public static string PortablePath { get; } = Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static Settings Load(string? userPath = null, string? portablePath = null)
    {
        var merged = new JsonObject();
        foreach (var path in new[] { userPath ?? UserPath, portablePath ?? PortablePath })
            if (Read(path) is { } values)
                foreach (var (key, value) in values)
                    merged[key] = value?.DeepClone();
        try
        {
            return merged.Deserialize<Settings>() ?? new Settings();
        }
        catch (JsonException)
        {
            return new Settings();
        }
    }

    static JsonObject? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>Writes to the portable file when there is one, otherwise to the user file.</summary>
    public void Save(string? userPath = null, string? portablePath = null)
    {
        portablePath ??= PortablePath;
        var path = File.Exists(portablePath) ? portablePath : userPath ?? UserPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
