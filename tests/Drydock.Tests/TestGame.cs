using System.Runtime.CompilerServices;
using Drydock.Core;

namespace Drydock.Tests;

/// <summary>Tests run against the locally installed game and a real save; they skip when either is missing.</summary>
static class TestGame
{
    public static string? BinDir { get; private set; }

    /// <summary>DRYDOCK_TEST_SAVE (a savegame.vrb), else the most recently written save.</summary>
    public static string? SavePath { get; } = FindSave();

    [ModuleInitializer]
    internal static void Init()
    {
        BinDir = GameInstall.Find();
        if (BinDir != null)
            GameInstall.Register(BinDir);
    }

    static string? FindSave()
    {
        if (Environment.GetEnvironmentVariable("DRYDOCK_TEST_SAVE") is { Length: > 0 } explicitPath)
            return explicitPath;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpaceEngineers2", "AppData", "SaveGames");
        if (!Directory.Exists(root))
            return null;
        return Directory.EnumerateFiles(root, "savegame.vrb", SearchOption.AllDirectories)
            .Where(p => !p.Contains(@"\.autosaves\") && !p.Contains(@"\.backups\"))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}

public sealed class GameFactAttribute : FactAttribute
{
    public GameFactAttribute()
    {
        if (TestGame.BinDir == null)
            Skip = "Space Engineers 2 is not installed.";
    }
}

public sealed class SaveFactAttribute : FactAttribute
{
    public SaveFactAttribute()
    {
        if (TestGame.BinDir == null)
            Skip = "Space Engineers 2 is not installed.";
        else if (TestGame.SavePath == null)
            Skip = "No save found, set DRYDOCK_TEST_SAVE.";
    }
}
