using Microsoft.Maui.Dispatching;

namespace MauiBenchDemo.Benchmarks;

/// <summary>
/// Runs delegates on the MAUI UI thread and BLOCKS the calling (benchmark) thread
/// until they complete, returning the result or rethrowing any exception.
///
/// This is the heart of the "no cross-process" design: the benchmark engine thread
/// and the live UI both live in the SAME process, so handing work to the UI thread
/// is an in-memory dispatcher post — there is no IPC, no serialization, no pipe.
/// The only cost is the in-process marshal, which is what we want to measure.
/// </summary>
public static class UiDispatcher
{
    /// <summary>The dispatcher for the UI thread. Set once at startup (see MainPage ctor).</summary>
    public static IDispatcher Instance { get; set; } = default!;

    public static void Run(Action action)
    {
        var tcs = new TaskCompletionSource();
        Instance.Dispatch(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        tcs.Task.GetAwaiter().GetResult();
    }

    public static T Run<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>();
        Instance.Dispatch(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task.GetAwaiter().GetResult();
    }

    public static void RunAsync(Func<Task> func)
    {
        var tcs = new TaskCompletionSource();
        Instance.Dispatch(async () =>
        {
            try { await func().ConfigureAwait(true); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        tcs.Task.GetAwaiter().GetResult();
    }

    public static T RunAsync<T>(Func<Task<T>> func)
    {
        var tcs = new TaskCompletionSource<T>();
        Instance.Dispatch(async () =>
        {
            try { tcs.SetResult(await func().ConfigureAwait(true)); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task.GetAwaiter().GetResult();
    }
}
