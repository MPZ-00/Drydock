using System.Collections.Concurrent;

namespace Drydock.Core;

/// <summary>Runs work one item at a time on a single thread. The game's parser state is bound to the thread that used it.</summary>
public sealed class WorkerThread : IDisposable
{
    readonly BlockingCollection<Action> _queue = new();
    readonly Thread _thread;

    public WorkerThread(string name)
    {
        _thread = new Thread(() =>
        {
            foreach (var work in _queue.GetConsumingEnumerable())
                work();
        }) { IsBackground = true, Name = name };
        _thread.Start();
    }

    public int ManagedThreadId => _thread.ManagedThreadId;

    public Task<T> Run<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        });
        return done.Task;
    }

    public Task Run(Action work) => Run(() =>
    {
        work();
        return true;
    });

    public void Dispose() => _queue.CompleteAdding();
}
