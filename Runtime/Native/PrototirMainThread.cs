using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Prototir.Native
{
    /// <summary>A small player-loop mailbox for engine calls made after protocol awaits resume
    /// on a worker. The ticker drains it; the dispatch rule stays testable without Unity.</summary>
    internal sealed class PrototirMainThread
    {
        private readonly int _threadId;
        private readonly ConcurrentQueue<Action> _actions = new();

        public PrototirMainThread(int threadId) => _threadId = threadId;

        public Task<T> Run<T>(Func<Task<T>> work)
        {
            if (Thread.CurrentThread.ManagedThreadId == _threadId) return work();
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _actions.Enqueue(() => Complete(work, completion));
            return completion.Task;
        }

        public void Drain()
        {
            if (Thread.CurrentThread.ManagedThreadId != _threadId)
                throw new InvalidOperationException("Prototir's player-loop work must drain on the main thread.");
            while (_actions.TryDequeue(out var action)) action();
        }

        private static async void Complete<T>(Func<Task<T>> work, TaskCompletionSource<T> completion)
        {
            try { completion.TrySetResult(await work()); }
            catch (Exception error) { completion.TrySetException(error); }
        }
    }
}
