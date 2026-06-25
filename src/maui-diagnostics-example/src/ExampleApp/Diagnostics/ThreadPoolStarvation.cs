namespace ExampleApp.Diagnostics;

/// <summary>
/// Demo control that deliberately starves the thread pool.
///
/// It queues a burst of work items that each block (sleep) for a while. Because they
/// occupy every available pool thread, any other work queued during that window has to
/// wait for the pool's starvation-detection logic to inject additional threads. While
/// that happens you will see <c>threadpool-queue-delay-ms</c> (our app counter) and the
/// runtime's <c>threadpool-queue-length</c> climb, then recover.
/// </summary>
public static class ThreadPoolStarvation
{
    /// <summary>
    /// Floods the thread pool with blocking work items for the given duration.
    /// </summary>
    /// <param name="duration">How long each work item blocks a pool thread.</param>
    /// <param name="workItemCount">
    /// Number of blocking work items. Defaults to 4× the processor count, which is
    /// comfortably more than the pool's initial thread count.
    /// </param>
    public static void Induce(TimeSpan duration, int? workItemCount = null)
    {
        int count = workItemCount ?? Environment.ProcessorCount * 4;

        for (int i = 0; i < count; i++)
        {
            ThreadPool.QueueUserWorkItem(_ => Thread.Sleep(duration));
        }
    }
}
