using System;
using System.Threading;
using System.Threading.Tasks;
using Prototir.Native;
using Xunit;

namespace Prototir.Native.Tests;

public class MainThreadTests
{
    [Fact]
    public async Task Worker_requests_wait_for_the_player_loop_and_start_on_its_thread()
    {
        var mainThreadId = Thread.CurrentThread.ManagedThreadId;
        var dispatcher = new PrototirMainThread(mainThreadId);
        Task<int> request = null;
        var executedOn = 0;

        var worker = new Thread(() =>
        {
            request = dispatcher.Run(() =>
            {
                executedOn = Thread.CurrentThread.ManagedThreadId;
                return Task.FromResult(42);
            });
        });
        worker.Start();
        worker.Join();

        Assert.False(request.IsCompleted);
        Assert.Equal(0, executedOn);
        dispatcher.Drain();
        Assert.Equal(42, await request);
        Assert.Equal(mainThreadId, executedOn);
    }

    [Fact]
    public async Task Work_already_on_the_main_thread_runs_immediately()
    {
        var dispatcher = new PrototirMainThread(Thread.CurrentThread.ManagedThreadId);
        var ran = false;
        var result = dispatcher.Run(() =>
        {
            ran = true;
            return Task.FromResult(7);
        });

        Assert.True(ran);
        Assert.Equal(7, await result);
    }
}
