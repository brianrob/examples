# How it works

This sample has two halves that share one set of counters:

- The **MAUI app** (`ExampleApp`) **produces** counters.
- Two consumers **read** them: an in-process `EventListener`, and an out-of-process
  console (`CounterListener`) connected over EventPipe.

This document explains each piece and how they fit together.

---

## 1. EventCounters in 60 seconds

An [`EventSource`](https://learn.microsoft.com/dotnet/api/system.diagnostics.tracing.eventsource)
is a named provider of diagnostic events. **EventCounters** are a special kind of payload
that an `EventSource` publishes *periodically* — but only while a listener has asked for
them. There are a few flavours:

| Counter type | Reports | Use it for |
|--------------|---------|------------|
| `PollingCounter` | a value sampled on demand (a "Mean") | a current level you can read at any time (e.g. **frame rate**) |
| `EventCounter` | Mean/Min/Max of values you write | a stream of measurements (e.g. **per-sample latency**) |
| `IncrementingEventCounter` | a sum over the interval | a rate of occurrences (e.g. requests/sec) |

Two rules drive everything in this sample:

1. **Counters are silent until a listener enables the provider with an
   `EventCounterIntervalSec` argument.** That argument is what turns counters into periodic
   samples. Both consumers here pass it.
2. **A listener can only find an `EventSource` that has actually been instantiated.** That's
   why `DiagnosticsBootstrapper.Start()` touches `AppEventSource.Log` at startup — to make
   sure the provider exists.

---

## 2. The app's single EventSource — `Example-App-Diagnostics`

`Diagnostics/AppEventSource.cs` declares **one** `EventSource`, named
`Example-App-Diagnostics`. The name is deliberately app-flavoured so that, in any trace, it
is obvious these counters come from *this application* and not from a framework.

The counters themselves are created **lazily**, the first time a listener enables the
provider:

```csharp
protected override void OnEventCommand(EventCommandEventArgs command)
{
    if (command.Command == EventCommand.Enable)
    {
        FrameRateCounter.Instance.EnsureCounterCreated(this);
        ThreadPoolQueueDelayCounter.Instance.EnsureCounterCreated(this);
    }
}
```

`EnsureCounterCreated` is idempotent (`_counter ??= …`), so it is safe when **both** the
in-process listener and an out-of-process EventPipe session enable the provider at the same
time.

> **Design choice:** there is exactly one `EventSource`, but each counter's implementation
> lives in its own file (`FrameRateCounter.cs`, `ThreadPoolQueueDelayCounter.cs`). The
> `EventSource` only owns the provider identity; the counters own their measurement logic.

---

## 3. The frame-rate counter (app code)

The runtime has no idea the process is rendering a UI, so there is no built-in frames-per-
second counter. We build one:

- **`FrameCanvasDrawable`** is an `IDrawable` attached to a MAUI `GraphicsView`. Every time
  it paints, it calls `FrameRateCounter.Instance.OnFrameRendered()` (a cheap interlocked
  increment) and draws a moving dot so there is always something to render.
- **The frame pump.** `MainPage` drives repaints from the platform's own per-frame signal
  via the small cross-platform `FramePump` helper: `CompositionTarget.Rendering` on Windows
  (WinUI 3) and `CADisplayLink` on Mac Catalyst. Both fire **once per composed display
  frame**, so calling `FrameCanvas.Invalidate()` on each one produces a smooth, display-locked
  rate (~60 fps on a 60 Hz screen).

  > **Why not a dispatcher timer?** The first cut pumped from a 16 ms `IDispatcherTimer`. On
  > WinUI that timer delivers irregular, coalesced ticks, and each `Invalidate()` only repaints
  > on the *next* composition pass — so painting came in bursts and the readout swung wildly
  > (3–40 fps) even when idle. The platform frame signal removes that jitter. Both signals run
  > on the UI thread, so the freeze demo (below) still works.
- **`FrameRateCounter`** exposes a `PollingCounter("frame-rate")`. Its sample callback
  computes `framesSinceLastPoll / secondsSinceLastPoll`, so the reported fps is correct no
  matter what interval the listener uses. It also offers `SampleDisplayFramesPerSecond()` —
  an independent delta computation the **in-app label** uses so the on-screen number is live
  even when no listener is attached and polling the counter.

Because the pump and the painting both run on the **UI thread**, anything that blocks that
thread stops the frames. That is why the **Freeze UI** button (a `Thread.Sleep` on the UI
thread) drives the frame rate to ~0: the per-frame callback can't fire, nothing repaints, and
the counter reports zero frames over that interval. A minimized/occluded window legitimately
paints nothing too — so a real 0 fps there is expected, not a bug.

---

## 4. The thread-pool queue-delay counter (app code)

`System.Runtime` exposes thread-pool *size* and *queue length*, but not the most direct
starvation signal: **how long does a freshly queued work item wait before it runs?**
`ThreadPoolQueueDelayCounter` measures exactly that.

- A **dedicated background thread** (not a pool thread — so the probe itself is never
  starved) queues a tiny work item every 250 ms.
- The work item measures the time from *enqueue* to *start* with `Stopwatch` and writes it to
  an `EventCounter("threadpool-queue-delay-ms")` via `WriteMetric`.
- When the pool is healthy the delay is a fraction of a millisecond. When it's starved, work
  items sit in the queue and the delay climbs until the pool injects more threads.

`ThreadPoolStarvation.Induce()` makes this happen on demand: it floods the pool with
`4 × ProcessorCount` blocking work items, so for a few seconds the queue-delay counter (and
the runtime's `ThreadPool Queue Length`) spike and then recover.

### Starvation detection (not just a counter)

A counter shows a *number*; it doesn't tell you "we are starved **now**". So the probe also
runs an **edge-triggered detector** on top of the same per-sample delay measurement:

- When a sample's delay crosses **`StarvationThresholdMs` (100 ms)**, the detector flips
  `IsStarved` true, remembers the peak delay, and fires a one-shot
  `AppEventSource.Log.ThreadPoolStarvationDetected(peakMs)` event.
- When delays fall back under **`RecoveryThresholdMs` (20 ms)**, it flips `IsStarved` false
  and fires `ThreadPoolStarvationRecovered(peakMs)`.

Because it is edge-triggered, you get exactly **one** "detected" and **one** "recovered"
event per episode, not a flood. These are ordinary (non-counter) events, so they show up in
the **raw-events pane** in-process and in the EventPipe stream out-of-process. `MainPage`
also surfaces the live state next to the queue-delay readout: while starved (or within ~3 s
of the last over-threshold sample) it appends `⚠ THREAD-POOL STARVATION (peak N ms)` and
turns the metrics text orange-red, so the freeze is visible on the UI, not just in the logs.

### The app's non-counter events

Besides the two counters, `AppEventSource` declares a handful of regular `[Event]` methods so
that *discrete things that happen* are traceable (in-process and over EventPipe):

| Id | Event | Fired when |
|----|-------|-----------|
| 1 | `RenderLoadChanged` | the on-canvas render load (extra work per frame) is changed |
| 2 | `ThreadPoolStarvationInduced` | the **Induce Starvation** button floods the pool |
| 3 | `ThreadPoolStarvationDetected` | the detector first sees delay over threshold |
| 4 | `ThreadPoolStarvationRecovered` | delay falls back under the recovery threshold |
| 5 | `UiFrozen` | the **Freeze UI** button blocks the UI thread |
| 6 | `UiHangDetected` | the UI-hang watchdog first sees the UI thread stalled past 500 ms |
| 7 | `UiHangEnded` | the UI thread becomes responsive again (carries the hang's peak duration) |

---

## 5. Detecting UI hangs (app code)

A *hang* is the UI thread being unable to do any work — process input, lay out, or paint —
for long enough that the user notices. The runtime can't tell you this; only something
watching the UI thread can. `UiHangDetector` does it with a **heartbeat + watchdog**, and —
crucially — publishes the result **through the same `EventSource` as counters and events**, so
a hang is detectable identically **in process and out of process**, with no special hooks.

**How it detects a hang:**

1. A 100 ms **heartbeat** `IDispatcherTimer` runs *on the UI thread* and stamps a timestamp
   each tick. While the UI thread is healthy, that timestamp is never more than ~100 ms old.
2. A dedicated background **watchdog** thread (which the UI thread can never block) reads the
   timestamp and computes the **UI-thread lag** = `now − lastBeat`. If the UI thread is stuck
   (e.g. the **Freeze UI** button does `Thread.Sleep` on it), the heartbeat can't tick, the
   timestamp goes stale, and the lag climbs.
3. When the lag crosses **`HangThresholdMilliseconds` (500 ms)** the watchdog declares a hang.
   Like starvation, it is **edge-triggered**: one `UiHangDetected(lagMs)` when the hang begins
   and one `UiHangEnded(peakMs)` when the UI thread recovers — not one per poll.

> **Why a heartbeat timer and not the frame pump?** Painting can legitimately pause (a
> minimized or occluded window stops compositing), which would look like a hang. A
> *dispatcher* timer keeps firing as long as the UI message loop is pumping, so it only goes
> quiet when the thread is genuinely stuck. Render load that merely slows painting doesn't trip
> it either — frames still arrive every few tens of ms, far under 500 ms.

**Why this works from counters *and* events, in and out of process** — the signal rides on
the `Example-App-Diagnostics` provider three ways, so any consumer can see it:

- **`ui-thread-lag-ms`** — a `PollingCounter` reporting the *current* lag. Its sample callback
  runs on the runtime's counter-timer thread (not the UI thread), so it keeps reporting *while
  the UI is frozen*; the value shoots past 500 ms. This is the purely **counter-based** way to
  detect a hang — visible to `dotnet-counters` or the `CounterListener` with zero app
  knowledge.
- **`ui-hang-count`** — an `IncrementingEventCounter` that ticks up by one per detected hang,
  so a tool shows "how many hangs this interval" at a glance.
- **`UiHangDetected` / `UiHangEnded`** — discrete events that **log** the hang and its peak
  duration.

`MainPage` also surfaces it on screen the same way as starvation: because the UI tick can't
run *during* the freeze, the `⚠ UI HANG (peak N ms)` warning (orange-red) appears the instant
the thread recovers — by which point the watchdog has already recorded the peak. Note the
distinction between two events the **Freeze UI** button produces: `UiFrozen` is logged by the
app because it *deliberately* blocked the thread, whereas `UiHangDetected` is raised by the
watchdog from its *own observation* — so the watchdog would also catch an **unintended** hang
that no one logged.

The detector's heartbeat/watchdog run between `Start(dispatcher)` and `Stop()`, which the page
calls as it appears/disappears (a real app would start it once at init with its main
dispatcher). The counters are created lazily when the provider is first enabled, like the
others.

---

## 6. Consumer A — the in-process `EventListener`

`InProcessCounterListener` is an `EventListener` that lives inside the app. This is the
simplest possible way to read counters: no IPC, no tooling.

It records the providers it cares about and enables them from an explicit `Start(interval)`
call:

```csharp
protected override void OnEventSourceCreated(EventSource eventSource)
{
    if (eventSource.Name is "Example-App-Diagnostics" or "System.Runtime")
    {
        // Defer — the interval isn't known during the base constructor (see gotcha below).
        if (_started) EnableCounters(eventSource);
        else _pendingSources.Add(eventSource);
    }
}

public void Start(int intervalSeconds = 1)
{
    _intervalSeconds = intervalSeconds;
    _started = true;
    foreach (var s in _pendingSources) EnableCounters(s);   // EnableEvents(..., EventCounterIntervalSec)
}
```

> **Ordering gotcha (this bit you in early testing):** the base `EventListener` constructor
> calls `OnEventSourceCreated` for every *already-existing* provider **before** the derived
> constructor body runs. If you call `EnableEvents` straight from `OnEventSourceCreated`
> using a constructor argument for the interval, that field is still its default — **`0`** —
> which means "don't poll", and your counters stay silent until *something else* (e.g. an
> out-of-process `dotnet-counters` session) enables the provider with a real interval. The
> deferred `Start()` avoids this by enabling providers only once the interval is set.

When a counter fires, the runtime calls `OnEventWritten` with an event named
`"EventCounters"`. **In-process, the payload is a flat `IDictionary<string, object>`** at
`Payload[0]` — you read `Name`, `DisplayName`, `CounterType`, `Mean`/`Increment`, and
`DisplayUnits` directly. `CounterLine.FromPayload` does this decode.

`OnEventWritten` **routes** each event by name:

- `"EventCounters"` → decoded into a `CounterLine`, raised on `CounterReceived`.
- `"EventSourceMessage"` (the runtime's internal diagnostics) → ignored.
- everything else (the app's discrete `[Event]`s — starvation detected/recovered, UI frozen,
  etc.) → wrapped in a `RawEventLine` (which formats the event's `Message` template against
  its payload) and raised on `RawEventReceived`.

### The two-pane log

The in-process log is split into two halves that reflect those two kinds of data:

- **Top — counter snapshot.** Counters are "the current value of each named metric", so the
  pane shows a **table that is replaced each interval**, not a scrolling history. `MainPage`
  keeps the latest `CounterLine` per `Provider/Name` in a `ConcurrentDictionary` and bumps a
  generation number on each update; the UI timer rebuilds the table text **only when the
  generation changed**. App counters are listed first, then `System.Runtime` counters by name.
- **Bottom — raw events.** Discrete events have history that matters, so they **scroll**:
  each `RawEventLine` is appended to a capped (`MaxEventLines`) list rendered into a single
  auto-following `Label`.

Both `CounterReceived` and `RawEventReceived` fire on a **background thread**. Rather than
marshal each sample to the UI thread individually, `MainPage` enqueues into lock-free
collections and **drains them in one batch** from its UI refresh timer. Each pane is a single
`Label` (inside a `ScrollView`) whose text is rebuilt at most once per drain — not a
`CollectionView`. With ~30 `System.Runtime` counters plus the app counters arriving every
interval, mutating a `CollectionView` item-by-item ran expensive per-item layout on the UI
thread once a second, which periodically starved the render loop and dipped the frame rate; a
single batched text update avoids that.

---

## 7. Consumer B — the out-of-process `CounterListener`

`CounterListener` is a normal console app. It does what `dotnet-counters`/`dotnet-trace` do:

1. **Find the target.** Two ways:
   - **PID/name (Windows/desktop):** `ProcessResolver` uses
     `DiagnosticsClient.GetPublishedProcesses()` to list .NET processes that have opened a
     diagnostics channel, and matches `--pid` or `--name`; then `new DiagnosticsClient(pid)`.
   - **Reverse-connect (`--diagnostic-port`, required for sandboxed Mac Catalyst):** the listener
     instead *hosts* a diagnostic port and the app connects out to it (launched with
     `DOTNET_DiagnosticPorts=<port>,nosuspend`). This uses
     `DiagnosticsClientConnector.FromDiagnosticPort("<port>,listen", …)`, whose `.Instance` is the
     `DiagnosticsClient`. (Needed because a Mac Catalyst app's default per-PID socket lives under
     a long sandbox-container path that exceeds the 108-char UNIX socket limit.)
2. **Open an EventPipe session.**
   ```csharp
   var session = client.StartEventPipeSession(providers, requestRundown: false);
   ```
   `providers` enables both `System.Runtime` and `Example-App-Diagnostics`, each with the
   `EventCounterIntervalSec` argument. `requestRundown: false` keeps the stream lean.
3. **Parse the stream with TraceEvent.**
   ```csharp
   using var source = new EventPipeEventSource(session.EventStream);
   source.Dynamic.All += e =>
   {
       // Counters first; otherwise try to surface it as a discrete app event.
       string? line = CounterTablePrinter.Describe(e)
           ?? CounterTablePrinter.DescribeAppEvent(e, appProviderName);
       if (line is not null) Console.WriteLine(line);
   };
   source.Process(); // blocks until the session stops
   ```

**Important payload difference.** Over EventPipe the `"EventCounters"` payload is **nested**:

```
traceEvent.PayloadValue(0)  ->  { "Payload": { "Name": ..., "Mean"/"Increment": ... } }
```

So `CounterTablePrinter.Describe` reads `PayloadValue(0)["Payload"]` and *then* the fields — a
different decode path from the in-process listener. Both honour the same Mean-vs-Increment
rule based on `CounterType`.

**It prints the app's discrete events too.** `DescribeAppEvent` handles any non-counter event
from `Example-App-Diagnostics` (e.g. `UiHangDetected`, `ThreadPoolStarvationDetected`), so the
out-of-process view mirrors the in-process raw-events pane — a hang is **logged**, not merely
inferable from the `ui-thread-lag-ms` counter climbing. One detail: with `requestRundown:false`
the EventSource message templates aren't in the stream, so `TraceEvent.FormattedMessage` is
empty and these print in a `EventName(name=value, …)` form (e.g.
`UiHangDetected(lagMilliseconds=552.3)`) — still complete, just not the templated sentence the
in-process listener shows.

The session ends on **Ctrl+C** (`session.Stop()` unblocks `source.Process()`), or
automatically after `--duration` seconds if you pass it.

---

## 8. Startup wiring

`App.xaml.cs` calls `DiagnosticsBootstrapper.Start()` once. That method:

1. touches `AppEventSource.Log` so the provider exists,
2. starts the thread-pool queue-delay probe, and
3. creates the in-process `InProcessCounterListener` and calls `Start(1)` on it, which
   enables the providers (triggering `OnEventCommand`, which creates the counters).

After that, the counters are live and *any* listener — in-process or over EventPipe — can
sample them.

---

## Adapting this to your own app

1. Copy `Diagnostics/AppEventSource.cs` and rename the provider to something app-specific.
2. Add a counter file per metric. Use a `PollingCounter` for "current level" values and an
   `EventCounter` for "stream of measurements"; create them in `OnEventCommand`.
3. Call your bootstrapper once at startup and touch the `EventSource` singleton.
4. Point `CounterListener --provider <your-provider-name>` at it, or just reuse the
   in-process `EventListener` pattern. Nothing else changes.
