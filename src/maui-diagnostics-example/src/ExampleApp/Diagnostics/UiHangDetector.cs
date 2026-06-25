using System.Diagnostics;
using System.Diagnostics.Tracing;
using Microsoft.Maui.Dispatching;

namespace ExampleApp.Diagnostics;

/// <summary>
/// Detects UI-thread hangs (the UI thread blocked for longer than
/// <see cref="HangThresholdMilliseconds"/>) and publishes the signal through THIS app's
/// <see cref="EventSource"/> — as both counters and discrete events — so it can be observed
/// the same way <b>in process</b> (the in-process <see cref="EventListener"/>) and
/// <b>out of process</b> (the EventPipe <c>CounterListener</c>). Nothing here is specific to
/// either consumer; the detector just emits, and either side reads.
///
/// <para><b>How it works — a heartbeat plus a watchdog.</b></para>
/// <list type="number">
///   <item>
///     A 100&#160;ms <b>heartbeat</b> <see cref="IDispatcherTimer"/> runs <i>on the UI thread</i>
///     and writes the current timestamp on every tick. While the UI thread is healthy the
///     timestamp is never more than ~100&#160;ms old.
///   </item>
///   <item>
///     A dedicated background <b>watchdog</b> thread (which the UI thread can never block)
///     reads that timestamp and computes the <b>UI-thread lag</b> = <c>now − lastBeat</c>.
///     If the UI thread is stuck (e.g. the "Freeze UI" button does <c>Thread.Sleep</c> on it),
///     the heartbeat can't tick, the timestamp goes stale, and the lag climbs.
///   </item>
///   <item>
///     When the lag crosses <see cref="HangThresholdMilliseconds"/> the watchdog declares a
///     hang. Detection is <b>edge-triggered</b>: one "detected" event when a hang starts and
///     one "ended" event (with the peak duration) when the UI thread recovers — not one per
///     poll.
///   </item>
/// </list>
///
/// <para><b>Why it works from counters/events (in and out of process).</b> The signal is
/// carried entirely by the <c>Example-App-Diagnostics</c> provider, so a consumer never needs
/// in-process access to detect a hang:</para>
/// <list type="bullet">
///   <item>
///     <b><c>ui-thread-lag-ms</c></b> — a <see cref="PollingCounter"/> reporting the <i>current</i>
///     lag. Its sample callback runs on the runtime's counter-timer thread (not the UI thread),
///     so it keeps reporting <i>while the UI is frozen</i>; any listener sees the value shoot past
///     500&#160;ms. This is the purely counter-based way to detect a hang.
///   </item>
///   <item>
///     <b><c>ui-hang-count</c></b> — an <see cref="IncrementingEventCounter"/> that ticks up by
///     one per detected hang, so a tool like <c>dotnet-counters</c> shows "how many hangs this
///     interval" at a glance.
///   </item>
///   <item>
///     <b><see cref="AppEventSource.UiHangDetected"/> / <see cref="AppEventSource.UiHangEnded"/></b>
///     — discrete events that log the hang and its peak duration.
///   </item>
/// </list>
///
/// <para>The counters are created lazily when the provider is first enabled (see
/// <see cref="EnsureCountersCreated"/>); the heartbeat and watchdog run between
/// <see cref="Start"/> and <see cref="Stop"/>, which the page calls as it appears/disappears
/// (a real app would start it once with its main dispatcher). Render load that merely slows
/// painting does <i>not</i> trip the detector — frames still arrive every few tens of ms, far
/// under the 500&#160;ms threshold; only an actual UI-thread stall does.</para>
/// </summary>
public sealed class UiHangDetector
{
    public static readonly UiHangDetector Instance = new();

    /// <summary>A UI-thread stall at or beyond this many ms is treated as a hang.</summary>
    public const double HangThresholdMilliseconds = 500;

    // The heartbeat ticks ~5x faster than the threshold so a healthy UI keeps the lag small,
    // and the watchdog samples frequently enough to time a hang to within ~50 ms.
    private const int HeartbeatIntervalMs = 100;
    private const int WatchdogIntervalMs = 50;

    private PollingCounter? _lagCounter;
    private IncrementingEventCounter? _hangCounter;

    private IDispatcherTimer? _heartbeatTimer;
    private Thread? _watchdog;
    private volatile bool _running;

    // Last time the UI thread proved it was alive (Stopwatch ticks). Read by the watchdog and
    // the polling-counter callback from other threads, so all access goes through Volatile/Interlocked.
    private long _lastBeatTimestamp;

    // Hang-episode state, owned by the watchdog thread.
    private bool _hanging;
    private double _peakHangMilliseconds;

    private UiHangDetector()
    {
    }

    /// <summary>True while the UI thread is currently considered hung.</summary>
    public bool IsHanging { get; private set; }

    /// <summary>Peak UI-thread lag observed during the current/most-recent hang, in ms.</summary>
    public double LastHangPeakMilliseconds { get; private set; }

    /// <summary>UTC time of the most recent over-threshold sample; lets the UI keep a brief warning visible.</summary>
    public DateTime LastHangAtUtc { get; private set; }

    /// <summary>
    /// Current UI-thread lag in ms (how stale the heartbeat is). ~0 when healthy; climbs while
    /// the UI thread is blocked. Returns 0 when the detector is not running. Also the value
    /// reported by the <c>ui-thread-lag-ms</c> polling counter.
    /// </summary>
    public double CurrentLagMilliseconds
    {
        get
        {
            if (!_running)
            {
                return 0;
            }

            long last = Interlocked.Read(ref _lastBeatTimestamp);
            return (Stopwatch.GetTimestamp() - last) * 1000.0 / Stopwatch.Frequency;
        }
    }

    /// <summary>
    /// Creates the counters the first time the provider is enabled. Safe to call repeatedly.
    /// </summary>
    internal void EnsureCountersCreated(EventSource source)
    {
        _lagCounter ??= new PollingCounter("ui-thread-lag-ms", source, () => CurrentLagMilliseconds)
        {
            DisplayName = "UI Thread Lag",
            DisplayUnits = "ms",
        };

        _hangCounter ??= new IncrementingEventCounter("ui-hang-count", source)
        {
            DisplayName = "UI Hang Count",
        };
    }

    /// <summary>
    /// Starts the UI heartbeat and the background watchdog. Call on the UI thread (the
    /// <paramref name="dispatcher"/> must be the UI thread's dispatcher). Safe to call repeatedly.
    /// </summary>
    public void Start(IDispatcher dispatcher)
    {
        if (_running)
        {
            return;
        }

        _running = true;

        // Seed the heartbeat so the watchdog doesn't see a stale (zero) timestamp at startup.
        Interlocked.Exchange(ref _lastBeatTimestamp, Stopwatch.GetTimestamp());

        // The heartbeat runs on the UI thread; if that thread blocks, this stops ticking — which
        // is exactly what the watchdog measures.
        _heartbeatTimer = dispatcher.CreateTimer();
        _heartbeatTimer.Interval = TimeSpan.FromMilliseconds(HeartbeatIntervalMs);
        _heartbeatTimer.Tick += OnHeartbeat;
        _heartbeatTimer.Start();

        _watchdog = new Thread(WatchdogLoop)
        {
            IsBackground = true,
            Name = "ui-hang-watchdog",
        };
        _watchdog.Start();
    }

    /// <summary>Stops the heartbeat and watchdog. Call on the UI thread (e.g. when the page disappears).</summary>
    public void Stop()
    {
        _running = false;

        if (_heartbeatTimer is not null)
        {
            _heartbeatTimer.Tick -= OnHeartbeat;
            _heartbeatTimer.Stop();
            _heartbeatTimer = null;
        }

        // Reset episode state so a future Start() doesn't carry a stale "hanging" flag.
        _hanging = false;
        IsHanging = false;
    }

    private void OnHeartbeat(object? sender, EventArgs e) =>
        Interlocked.Exchange(ref _lastBeatTimestamp, Stopwatch.GetTimestamp());

    private void WatchdogLoop()
    {
        while (_running)
        {
            EvaluateHang(CurrentLagMilliseconds);
            Thread.Sleep(WatchdogIntervalMs);
        }
    }

    /// <summary>
    /// Edge-triggered hang detection. Fires a "detected" event (and bumps the hang counter)
    /// when lag first crosses the threshold, tracks the episode's peak lag, and fires an
    /// "ended" event carrying that peak when the UI thread beats again.
    /// </summary>
    private void EvaluateHang(double lagMs)
    {
        if (lagMs >= HangThresholdMilliseconds)
        {
            LastHangAtUtc = DateTime.UtcNow;

            if (!_hanging)
            {
                _hanging = true;
                IsHanging = true;
                _peakHangMilliseconds = lagMs;
                LastHangPeakMilliseconds = lagMs;

                // No-op if no listener has enabled the provider yet.
                _hangCounter?.Increment();
                AppEventSource.Log.UiHangDetected(lagMs);
            }
            else if (lagMs > _peakHangMilliseconds)
            {
                _peakHangMilliseconds = lagMs;
                LastHangPeakMilliseconds = lagMs;
            }
        }
        else if (_hanging)
        {
            // The heartbeat ticked again, so the UI thread is responsive once more.
            _hanging = false;
            IsHanging = false;
            AppEventSource.Log.UiHangEnded(_peakHangMilliseconds);
        }
    }
}
