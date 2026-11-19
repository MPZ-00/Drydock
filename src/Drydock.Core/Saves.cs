namespace Drydock.Core;

public sealed record SaveInfo(string Name, string File, DateTime Written);

/// <summary>The saves in the game's SaveGames folder.</summary>
public static class Saves
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpaceEngineers2", "AppData", "SaveGames");

    /// <summary>Saves newest first.</summary>
    public static List<SaveInfo> List(string? root = null)
    {
        root ??= Root;
        if (!Directory.Exists(root))
            return [];
        return Directory.EnumerateDirectories(root)
            .Select(d => Path.Combine(d, "savegame.vrb"))
            .Where(File.Exists)
            .Select(f => new SaveInfo(Path.GetFileName(Path.GetDirectoryName(f))!, f, File.GetLastWriteTime(f)))
            .OrderByDescending(s => s.Written)
            .ToList();
    }

    /// <summary>A save folder path, a folder name, its number in <see cref="List"/>, or a unique part of its name.</summary>
    public static string Resolve(string query, string? root = null)
    {
        if (Directory.Exists(query) && File.Exists(Path.Combine(query, "savegame.vrb")))
            return Path.Combine(query, "savegame.vrb");
        var saves = List(root);
        var exact = saves.FirstOrDefault(s => s.Name.Equals(query, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact.File;
        if (int.TryParse(query, out int n) && n >= 1 && n <= saves.Count)
            return saves[n - 1].File;
        var matches = saves.Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1)
            return matches[0].File;
        var hint = matches.Count == 0
            ? "Run 'saves' to see them."
            : "It matches: " + string.Join(", ", matches.Take(8).Select(s => $"\"{s.Name}\""));
        throw new FileNotFoundException($"No single save matches '{query}'. {hint}");
    }
}
