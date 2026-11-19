using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;

namespace Drydock.Core;

/// <summary>
/// Locates the Space Engineers 2 install and resolves the game's assemblies from it.
/// Must not reference any game type, it runs before those can be loaded.
/// </summary>
public static partial class GameInstall
{
    const string RelativeGame2 = @"steamapps\common\SpaceEngineers2\Game2";

    static string? _binDir;

    /// <summary>The Game2 folder holding the game's binaries.</summary>
    public static string BinDir => _binDir ?? throw new InvalidOperationException("GameInstall.Register has not been called.");

    /// <summary>The root folder holding Game2 and GameData.</summary>
    public static string RootDir => Path.GetDirectoryName(BinDir)!;

    /// <summary>Find the Game2 folder: explicit path, SE2_GAME_DIR, then every Steam library.</summary>
    public static string? Find(string? explicitDir = null)
    {
        foreach (var dir in Candidates(explicitDir))
        {
            if (File.Exists(Path.Combine(dir, "VRage.Library.dll")))
                return dir;
            var nested = Path.Combine(dir, "Game2");
            if (File.Exists(Path.Combine(nested, "VRage.Library.dll")))
                return nested;
        }
        return null;
    }

    static IEnumerable<string> Candidates(string? explicitDir)
    {
        if (!string.IsNullOrEmpty(explicitDir))
            yield return explicitDir;
        if (Environment.GetEnvironmentVariable("SE2_GAME_DIR") is { Length: > 0 } env)
            yield return env;
        var steam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        yield return Path.Combine(steam, RelativeGame2);
        var vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
        if (!File.Exists(vdf))
            yield break;
        foreach (Match m in LibraryPath().Matches(File.ReadAllText(vdf)))
            yield return Path.Combine(m.Groups[1].Value.Replace(@"\\", @"\"), RelativeGame2);
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex LibraryPath();

    /// <summary>Make the game's assemblies loadable from <paramref name="binDir"/>. Call once, first thing.</summary>
    public static void Register(string binDir)
    {
        if (_binDir != null)
            return;
        _binDir = binDir;
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            var path = Path.Combine(binDir, name.Name + ".dll");
            return File.Exists(path) ? ctx.LoadFromAssemblyPath(path) : null;
        };
    }

    /// <summary>Find and register the game, or throw with a hint.</summary>
    public static string Initialize(string? explicitDir = null)
    {
        var dir = Find(explicitDir)
            ?? throw new DirectoryNotFoundException("Space Engineers 2 not found. Pass --game-dir or set SE2_GAME_DIR to its Game2 folder.");
        Register(dir);
        return dir;
    }

    public static Version? GameVersion()
    {
        var path = Path.Combine(BinDir, "VRage.Library.dll");
        return AssemblyName.GetAssemblyName(path).Version;
    }
}
