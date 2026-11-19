using Drydock.Core;

namespace Drydock.Tests;

public sealed class SettingsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("drydock-settings-").FullName;
    string UserPath => Path.Combine(_dir, "user.json");
    string PortablePath => Path.Combine(_dir, "portable.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Portable_settings_override_per_key()
    {
        File.WriteAllText(UserPath, """{"GameDir":"A","Theme":"Dark"}""");
        File.WriteAllText(PortablePath, """{"Theme":"Light"}""");
        Assert.Equal(new Settings("A", "Light"), Settings.Load(UserPath, PortablePath));
    }

    [Fact]
    public void Broken_settings_fall_back_to_defaults()
    {
        File.WriteAllText(UserPath, "not json");
        Assert.Equal(new Settings(), Settings.Load(UserPath, PortablePath));
    }

    [Fact]
    public void Saving_goes_to_the_portable_file_when_there_is_one()
    {
        File.WriteAllText(PortablePath, "{}");
        new Settings("B", "Dark").Save(UserPath, PortablePath);
        Assert.False(File.Exists(UserPath));
        Assert.Equal(new Settings("B", "Dark"), Settings.Load(UserPath, PortablePath));
    }
}

public class WorkerThreadTests
{
    [Fact]
    public async Task All_work_runs_on_one_thread()
    {
        using var worker = new WorkerThread("test");
        var ids = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => worker.Run(() => Environment.CurrentManagedThreadId))));
        Assert.All(ids, id => Assert.Equal(worker.ManagedThreadId, id));
    }

    [Fact]
    public async Task Exceptions_reach_the_caller()
    {
        using var worker = new WorkerThread("test");
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.Run<int>(() => throw new InvalidOperationException()));
        Assert.Equal(2, await worker.Run(() => 2));
    }
}
