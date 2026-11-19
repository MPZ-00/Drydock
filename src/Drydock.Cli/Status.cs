using System.Diagnostics;

namespace Drydock.Cli;

/// <summary>Shows a spinner on stderr while slow work runs, so a long load doesn't look like a hang.</summary>
static class Status
{
    static readonly char[] Frames = ['|', '/', '-', '\\'];

    /// <summary>Runs <paramref name="work"/> on the calling thread, the game's allocator is thread-bound.</summary>
    public static T Step<T>(string label, Func<T> work)
    {
        if (Console.IsErrorRedirected)
            return work();
        using var done = new CancellationTokenSource();
        var watch = Stopwatch.StartNew();
        var spinner = new Thread(() =>
        {
            for (int i = 0; !done.IsCancellationRequested; i++)
            {
                Console.Error.Write($"\r{Frames[i % Frames.Length]} {label} {watch.Elapsed.TotalSeconds:0.0}s ");
                done.Token.WaitHandle.WaitOne(120);
            }
        }) { IsBackground = true };
        spinner.Start();
        try
        {
            return work();
        }
        finally
        {
            done.Cancel();
            spinner.Join();
            Console.Error.Write($"\r{new string(' ', label.Length + 12)}\r");
        }
    }
}
