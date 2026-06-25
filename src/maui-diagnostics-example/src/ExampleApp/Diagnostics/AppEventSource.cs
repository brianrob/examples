using System.Diagnostics.Tracing;

namespace ExampleApp.Diagnostics;

/// <summary>
/// The single <see cref="EventSource"/> authored by THIS application.
///
/// Everything published through here is "app code" — it is not provided by the
/// .NET runtime or by MAUI. The provider name <c>Example-App-Diagnostics</c> is
/// deliberately app-flavored so that, in a trace, it is obvious these counters
/// come from the application and not from a framework.
///
/// The actual counter implementations live in their own files
/// (<see cref="FrameRateCounter"/>, <see cref="ThreadPoolQueueDelayCounter"/>); this
/// class only owns the <see cref="EventSource"/> identity and creates the counters
/// the first time a listener turns the provider on.
/// </summary>
[EventSource(Name = ProviderName)]
public sealed class AppEventSource : EventSource
{
    /// <summary>The provider name listeners enable (in-process and over EventPipe).</summary>
    public const string ProviderName = "Example-App-Diagnostics";

    /// <summary>The process-wide singleton instance.</summary>
    public static readonly AppEventSource Log = new();

    private AppEventSource()
    {
    }

    /// <summary>
    /// Called by the runtime whenever a listener enables or disables this provider.
    /// We create the counters lazily on the first <see cref="EventCommand.Enable"/> so
    /// there is no cost when nobody is listening. <c>EnsureCounterCreated</c> is
    /// idempotent, so repeated enables (e.g. an in-process listener AND an
    /// out-of-process EventPipe session at the same time) are harmless.
    /// </summary>
    protected override void OnEventCommand(EventCommandEventArgs command)
    {
        if (command.Command == EventCommand.Enable)
        {
            FrameRateCounter.Instance.EnsureCounterCreated(this);
            ThreadPoolQueueDelayCounter.Instance.EnsureCounterCreated(this);
            UiHangDetector.Instance.EnsureCountersCreated(this);
        }
    }

    /// <summary>
    /// Task ids used to pair an event with its matching "begin"/"end" event.
    ///
    /// A condition that has a duration (thread-pool starvation, a UI hang) is logged as two
    /// events that share a <see cref="EventTask"/>: one with <see cref="EventOpcode.Start"/>
    /// and one with <see cref="EventOpcode.Stop"/>. Trace tools (PerfView, dotnet-trace,
    /// TraceEvent) recognise that Start/Stop pairing and automatically compute and display the
    /// elapsed time between them as a single activity — so you get the duration "for free"
    /// without having to subtract timestamps by hand. We also still carry the measured peak
    /// duration in the Stop event's payload for consumers that only look at one event.
    /// </summary>
    public static class Tasks
    {
        public const EventTask ThreadPoolStarvation = (EventTask)1;
        public const EventTask UiHang = (EventTask)2;
    }

    // ---------------------------------------------------------------------------------
    // Regular (non-counter) events.
    //
    // EventCounters answer "what is the value, sampled over time?". Regular events answer
    // "this discrete thing just happened". The same provider can carry both. A listener
    // (in-process or over EventPipe) receives these alongside the counters; this app routes
    // them to the separate "Raw events" pane so the difference is visible.
    // ---------------------------------------------------------------------------------

    /// <summary>The render-load demo toggle changed.</summary>
    [Event(1, Level = EventLevel.Informational, Message = "Render load set to {0} ms/frame")]
    public void RenderLoadChanged(int extraMillisecondsPerFrame) => WriteEvent(1, extraMillisecondsPerFrame);

    /// <summary>The user deliberately flooded the thread pool from the demo button.</summary>
    [Event(2, Level = EventLevel.Warning, Message = "Induced thread-pool starvation: {0} blocking work items for {1} ms")]
    public void ThreadPoolStarvationInduced(int workItemCount, int durationMilliseconds) =>
        WriteEvent(2, workItemCount, durationMilliseconds);

    /// <summary>
    /// The queue-delay probe observed a delay above the starvation threshold (edge-triggered:
    /// fired once when starvation begins, not on every sample). This is the <b>Start</b> of the
    /// starvation activity; <see cref="ThreadPoolStarvationRecovered"/> is its <b>Stop</b>, so
    /// tools show how long starvation lasted.
    /// </summary>
    [Event(3, Level = EventLevel.Warning, Opcode = EventOpcode.Start, Task = Tasks.ThreadPoolStarvation,
        Message = "Thread-pool starvation DETECTED: a queued work item waited {0} ms before running")]
    public void ThreadPoolStarvationDetected(double queueDelayMilliseconds) =>
        WriteEvent(3, queueDelayMilliseconds);

    /// <summary>
    /// The queue-delay probe recovered to normal after a starvation episode. This is the
    /// <b>Stop</b> that closes the activity opened by <see cref="ThreadPoolStarvationDetected"/>;
    /// it also carries the peak queue delay for consumers that only read the Stop event.
    /// </summary>
    [Event(4, Level = EventLevel.Informational, Opcode = EventOpcode.Stop, Task = Tasks.ThreadPoolStarvation,
        Message = "Thread-pool starvation recovered (peak queue delay was {0} ms)")]
    public void ThreadPoolStarvationRecovered(double peakQueueDelayMilliseconds) =>
        WriteEvent(4, peakQueueDelayMilliseconds);

    /// <summary>The UI thread was deliberately blocked from the demo button.</summary>
    [Event(5, Level = EventLevel.Warning, Message = "UI thread frozen for {0} ms")]
    public void UiFrozen(int durationMilliseconds) => WriteEvent(5, durationMilliseconds);

    /// <summary>
    /// The UI-hang watchdog observed the UI thread stalled past its threshold (edge-triggered:
    /// fired once when the hang begins). Unlike <see cref="UiFrozen"/> — which the app logs
    /// because it *deliberately* blocked the thread — this is raised by the watchdog from its
    /// own observation, so it also catches *unintended* hangs. This is the <b>Start</b> of the
    /// hang activity; <see cref="UiHangEnded"/> is its <b>Stop</b>, so tools show the hang's
    /// duration automatically.
    /// </summary>
    [Event(6, Level = EventLevel.Warning, Opcode = EventOpcode.Start, Task = Tasks.UiHang,
        Message = "UI hang DETECTED: the UI thread has been unresponsive for {0} ms")]
    public void UiHangDetected(double lagMilliseconds) => WriteEvent(6, lagMilliseconds);

    /// <summary>
    /// The UI thread became responsive again after a hang. This is the <b>Stop</b> that closes
    /// the activity opened by <see cref="UiHangDetected"/>; it also reports the hang's peak
    /// duration for consumers that only read the Stop event.
    /// </summary>
    [Event(7, Level = EventLevel.Informational, Opcode = EventOpcode.Stop, Task = Tasks.UiHang,
        Message = "UI hang ended (UI thread was unresponsive for {0} ms)")]
    public void UiHangEnded(double peakLagMilliseconds) => WriteEvent(7, peakLagMilliseconds);
}
