using Drydock.Core;

namespace Drydock.Tests;

public sealed class SavesTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("drydock-saves-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    void MakeSave(string name, DateTime written)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        var file = Path.Combine(dir, "savegame.vrb");
        File.WriteAllBytes(file, [1]);
        File.SetLastWriteTime(file, written);
    }

    [Fact]
    public void Saves_are_listed_newest_first()
    {
        MakeSave("Old", new DateTime(2026, 1, 1));
        MakeSave("New", new DateTime(2026, 2, 1));
        Assert.Equal(["New", "Old"], Saves.List(_root).Select(s => s.Name));
    }

    [Fact]
    public void Folders_without_a_save_are_skipped()
    {
        MakeSave("Real", new DateTime(2026, 1, 1));
        Directory.CreateDirectory(Path.Combine(_root, "Empty"));
        Assert.Equal(["Real"], Saves.List(_root).Select(s => s.Name));
    }

    [Fact]
    public void Resolve_takes_number_exact_name_and_unique_part()
    {
        MakeSave("Old", new DateTime(2026, 1, 1));
        MakeSave("New", new DateTime(2026, 2, 1));
        Assert.EndsWith(@"New\savegame.vrb", Saves.Resolve("1", _root));
        Assert.EndsWith(@"Old\savegame.vrb", Saves.Resolve("old", _root));
        Assert.EndsWith(@"New\savegame.vrb", Saves.Resolve("ne", _root));
    }

    [Fact]
    public void Resolve_refuses_ambiguous_parts()
    {
        MakeSave("Base A", new DateTime(2026, 1, 1));
        MakeSave("Base B", new DateTime(2026, 2, 1));
        var e = Assert.Throws<FileNotFoundException>(() => Saves.Resolve("Base", _root));
        Assert.Contains("\"Base A\"", e.Message);
    }
}
