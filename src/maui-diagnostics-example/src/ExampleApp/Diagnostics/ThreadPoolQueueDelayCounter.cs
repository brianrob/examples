using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace ExampleApp.Diagnostics;

/// <summary>
/// Thread-pool queue-delay counter — implemented by THIS application.
///
/// The runtime exposes thread-pool size and queue length (via the
/// <c>System.Runtime</c> provider), but not "how long does a freshly queued work item
/// wait before it actually runs?". That wait time is the most direct signal of
/// thread-pool starvation, so we measure it ourselves.
///
/// A dedicated background thread (NOT a thread-pool thread, so the scheduler itself is
/// never starved) periodically queues a tiny work item to the thread pool and records
/// how long it sat in the queue before executing. Each measurement is written to an
/// <see cref="EventCounter"/>, which reports Mean/Min/Max over the listener's interval.
///
/// Use <see cref="ThreadPoolStarvation"/> to flood the pool and watch this value spike.
/// </summary>
public sealed class ThreadPoolQueueDelayCounter
{
    public static readonly ThreadPoolQueueDelayCounter Instance = new();

    private EventCounter? _counter;
    private Thread? _scheduler;
    private volatile bool _running;

    // Starvation detection. Normal enqueue-to-start delay is well under 1 ms; a delay above
    // the threshold means queued work is waiting on the pool to inject threads — i.e. the
    // pool is starved. Detection is edge-triggered so we log once per episode, not per sample.
    private const double StarvationThresholdMs = 100;
    private const double RecoveryThresholdMs = 20;

    private ThreadPoolQueueDelayCounter()
    {
    }

    /// <summary>Most recent measured enqueue-to-start delay, for the in-app UI.</summary>
    public double LastDelayMilliseconds { get; private set; }

    /// <summary>True while the pool is currently considered starved (delay over threshold).</summary>
    public bool IsStarved { get; private set; }

    /// <summary>Peak queue delay observed during the current/most-recent starvation episode.</summary>
    public double LastStarvationPeakMilliseconds { get; private set; }

    /// <summary>UTC time of the most recent over-threshold sample, used to keep the UI warning visible briefly.</summary>
    public DateTime LastStarvationAtUtc { get; private set; }

    /// <summary>
    /// Creates the <see cref="EventCounter"/> the first time the provider is enabled.
    /// Safe to call repeatedly.
    /// </summary>
    internal void EnsureCounterCreated(EventSource source)
    {
        _counter ??= new EventCounter("threadpool-queue-delay-ms", source)
        {
            DisplayName = "Thread Pool Queue Delay",
            DisplayUnits = "ms",
        };
    }

    /// <summary>Starts the background probe loop. Safe to call once at startup.</summary>
    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _scheduler = new Thread(ProbeLoop)
        {
            IsBackground = true,
            Name = "tp-queue-delay-probe",
        };
        _scheduler.Start();
    }

    private void ProbeLoop()
    {
        while (_running)
        {
            long enqueuedTimestamp = Stopwatch.GetTimestamp();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                double delayMs =
                    (Stopwatch.GetTimestamp() - enqueuedTimestamp) * 1000.0 / Stopwatch.Frequency;

                LastDelayMilliseconds = delayMs;

                // No-op if no listener has enabled the provider yet.
                _counter?.WriteMetric(delayMs);

                EvaluateStarvation(delayMs);
            });

            Thread.Sleep(250);
        }
    }

    /// <summary>
    /// Edge-triggered starvation detection. Logs an event when a sample first crosses the
    /// starvation threshold and again when it recovers, tracking the episode's peak delay so
    /// the UI can surface it next to the live queue-delay number.
    /// </summary>
    private void EvaluateStarvation(double delayMs)
    {
        if (delayMs >= StarvationThresholdMs)
        {
            LastStarvationAtUtc = DateTime.UtcNow;

            if (!IsStarved)
            {
                IsStarved = true;
                LastStarvationPeakMilliseconds = delayMs;
                AppEventSource.Log.ThreadPoolStarvationDetected(delayMs);
            }
            else if (delayMs > LastStarvationPeakMilliseconds)
            {
                LastStarvationPeakMilliseconds = delayMs;
            }
        }
        else if (delayMs <= RecoveryThresholdMs && IsStarved)
        {
            IsStarved = false;
            AppEventSource.Log.ThreadPoolStarvationRecovered(LastStarvationPeakMilliseconds);
        }
    }
}
