using System.Diagnostics;

namespace Drydock.Cli;

/// <summary>Shows a spinner on stderr while slow work runs and leaves each finished step with its time.</summary>
static class Status
{
    static readonly char[] Frames = ['|', '/', '-', '\\'];

    /// <summary>Runs <paramref name="work"/> on the calling thread, the game's allocator is thread-bound.</summary>
    public static T Step<T>(string label, Func<T> work)
    {
        var watch = Stopwatch.StartNew();
        if (Console.IsErrorRedirected)
        {
            var result = work();
            Console.Error.WriteLine($"  {label} {watch.Elapsed.TotalSeconds:0.0}s");
            return result;
        }
        using var done = new CancellationTokenSource();
        var spinner = new Thread(() =>
        {
            for (int i = 0; !done.IsCancellationRequested; i++)
            {
                Console.Error.Write($"\r{Frames[i % Frames.Length]} {label} {watch.Elapsed.TotalSeconds:0.0}s ");
                done.Token.WaitHandle.WaitOne(120);
            }
        }) { IsBackground = true };
        spinner.Start();
        bool ok = false;
        try
        {
            var result = work();
            ok = true;
            return result;
        }
        finally
        {
            done.Cancel();
            spinner.Join();
            Console.Error.WriteLine($"\r{(ok ? "+" : "x")} {label} {watch.Elapsed.TotalSeconds:0.0}s ");
        }
    }
}
