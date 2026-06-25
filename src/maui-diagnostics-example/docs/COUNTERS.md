# Counter reference

Every counter in this sample arrives the same way: as an `"EventCounters"` event from a
provider that a listener enabled with an `EventCounterIntervalSec` argument. What differs is
**who authored it** and **what kind of counter it is**.

> **Reading the value:** `PollingCounter` and `EventCounter` report a **`Mean`** field;
> `IncrementingEventCounter` reports an **`Increment`** (a sum over the interval). Both
> consumers in this sample pick the right field based on the payload's `CounterType`
> (`"Sum"` → Increment, otherwise → Mean).

---

## App-authored counters — provider `Example-App-Diagnostics`

These are implemented by this app. They do **not** exist in the .NET runtime.

### `frame-rate` — "Frame Rate" (fps)

| | |
|---|---|
| **Provider** | `Example-App-Diagnostics` |
| **Counter type** | `PollingCounter` (Mean) |
| **Unit** | `fps` |
| **Source file** | `Diagnostics/FrameRateCounter.cs`, `Diagnostics/FrameCanvasDrawable.cs` |

The number of UI frames painted per second. A `GraphicsView` is repainted once per display
frame, driven by the platform's compositor signal (`CompositionTarget.Rendering` on Windows,
`CADisplayLink` on Mac Catalyst); each paint increments a frame tally. The `PollingCounter`
callback computes `framesSinceLastPoll / secondsSinceLastPoll`, so the value is
interval-independent.

**Make it move:**
- *Add render load* button → each frame burns ~25 ms of busy work, lowering fps.
- *Freeze UI (3s)* button → blocks the UI thread, so **no** frames paint and fps → ~0.

A minimized or fully occluded window paints nothing, so 0 fps there is correct, not a fault.

### `threadpool-queue-delay-ms` — "Thread Pool Queue Delay" (ms)

| | |
|---|---|
| **Provider** | `Example-App-Diagnostics` |
| **Counter type** | `EventCounter` (Mean/Min/Max) |
| **Unit** | `ms` |
| **Source file** | `Diagnostics/ThreadPoolQueueDelayCounter.cs`, `Diagnostics/ThreadPoolStarvation.cs` |

How long a freshly queued work item waits before it starts executing — the most direct
signal of **thread-pool starvation**. A dedicated background thread (not a pool thread)
queues a probe every 250 ms and measures the enqueue→start latency with `Stopwatch`,
writing each measurement via `WriteMetric`.

**Make it move:**
- *Induce thread-pool starvation* button → floods the pool with `4 × ProcessorCount`
  blocking work items for 3 s, so the delay spikes and then recovers.

On top of the raw value, the probe runs an **edge-triggered starvation detector**: when a
sample crosses 100 ms it marks the app starved (recording the peak) and emits a one-shot
`ThreadPoolStarvationDetected` event; when delays fall back under 20 ms it emits
`ThreadPoolStarvationRecovered`. While starved, `MainPage` appends
`⚠ THREAD-POOL STARVATION (peak N ms)` next to the queue-delay readout and turns it orange-red.

### `ui-thread-lag-ms` — "UI Thread Lag" (ms)

| | |
|---|---|
| **Provider** | `Example-App-Diagnostics` |
| **Counter type** | `PollingCounter` (Mean) |
| **Unit** | `ms` |
| **Source file** | `Diagnostics/UiHangDetector.cs` |

How long it has been since the UI thread last "checked in" — i.e. the current **UI-thread
lag**. A 100 ms heartbeat timer on the UI thread stamps a timestamp each tick; this counter
reports `now − lastBeat`. It is ~0–100 ms when the UI is healthy and climbs once the UI thread
is blocked. Because the polling callback runs on the runtime's counter thread (not the UI
thread), it keeps reporting *while the UI is frozen* — so a value over **500 ms** is a
purely counter-based hang signal, visible to any consumer (in or out of process).

**Make it move:**
- *Freeze UI (3s)* button → blocks the UI thread, so the lag climbs past 500 ms toward ~3000,
  then drops back to normal.

### `ui-hang-count` — "UI Hang Count"

| | |
|---|---|
| **Provider** | `Example-App-Diagnostics` |
| **Counter type** | `IncrementingEventCounter` (Sum) |
| **Unit** | _(count)_ |
| **Source file** | `Diagnostics/UiHangDetector.cs` |

Number of UI hangs (lag over 500 ms) detected during the interval. A background **watchdog**
thread reads the UI-thread lag and, edge-triggered, increments this counter once per hang
episode and emits a `UiHangDetected` / `UiHangEnded` event pair. So `ui-thread-lag-ms` tells
you *how bad* the UI responsiveness is right now, while `ui-hang-count` tells you *how many*
threshold-crossing hangs occurred. While hung (and for ~3 s after), `MainPage` appends
`⚠ UI HANG (peak N ms)` next to the metrics and turns it orange-red.

---

## App-authored events (non-counter) — provider `Example-App-Diagnostics`

The same provider also emits ordinary (non-counter) events for **discrete things that
happen**. Unlike counters, these aren't periodic samples — they fire once, when the thing
occurs, and carry a human-readable `Message`. In-process they appear in the **raw-events
pane**; out-of-process the `CounterListener` prints them too (via `DescribeAppEvent`), so the
two views match.

| Id | Event | Level | Opcode | Fired when |
|----|-------|-------|--------|-----------|
| 1 | `RenderLoadChanged` | Informational | — | the per-frame render load is changed |
| 2 | `ThreadPoolStarvationInduced` | Warning | — | *Induce Starvation* floods the pool |
| 3 | `ThreadPoolStarvationDetected` | Warning | **Start** | a queued item first waits over the 100 ms threshold |
| 4 | `ThreadPoolStarvationRecovered` | Informational | **Stop** | delay falls back under the 20 ms recovery threshold |
| 5 | `UiFrozen` | Warning | — | *Freeze UI* blocks the UI thread (logged because the app *deliberately* did it) |
| 6 | `UiHangDetected` | Warning | **Start** | the watchdog *observes* the UI thread stalled over 500 ms (also catches unintended hangs) |
| 7 | `UiHangEnded` | Informational | **Stop** | the UI thread recovers; carries the hang's peak duration |

> These have no keywords, so any listener that enables the provider at Informational level (as
> both consumers here do) receives them. Both detectors are edge-triggered, so you get exactly
> one *Detected* and one *Ended/Recovered* per episode rather than a flood.
>
> **Start/Stop pairing:** the two duration conditions are each logged as a paired *Start* event
> (`…Detected`) and *Stop* event (`…Recovered`/`…Ended`) that share an `EventTask`. Trace tools
> (PerfView, dotnet-trace, TraceEvent) recognise the Start/Stop opcodes and automatically show
> the elapsed time between them as a single activity — you don't have to subtract timestamps by
> hand. The *Stop* event also still carries the measured peak duration in its payload.

---

## Runtime-provided counters — provider `System.Runtime`

The app writes **zero** code for these. The listener simply enables the `System.Runtime`
provider and the runtime emits them. This is the same set you'd see from `dotnet-counters`.
Common ones you'll observe in this sample:

| Counter (DisplayName) | Type | Notes |
|-----------------------|------|-------|
| CPU Usage | Mean (%) | Process CPU. |
| Working Set | Mean (MB) | Process memory. |
| GC Heap Size | Mean (MB) | Managed heap size. |
| Gen 0 / 1 / 2 GC Count | Sum | Collections per interval. |
| Allocation Rate | Sum (B) | Bytes allocated over the interval. |
| **ThreadPool Thread Count** | Mean | Live pool threads — watch it grow during starvation. |
| **ThreadPool Queue Length** | Mean | Items waiting — spikes alongside our `threadpool-queue-delay-ms`. |
| ThreadPool Completed Work Item Count | Sum | Throughput. |
| Monitor Lock Contention Count | Sum | Lock contention. |
| Exception Count | Sum | Exceptions thrown. |
| % Time in GC since last GC | Mean (%) | GC pressure. |
| Number of Active Timers | Mean | Active timers. |

> The exact list depends on the runtime version. The two **bolded** rows pair naturally with
> the app's `threadpool-queue-delay-ms` counter when you press *Induce thread-pool
> starvation*: queue length and thread count climb while items wait, then settle.

---

## How to tell app counters from runtime counters at a glance

- **Provider name.** App counters come from `Example-App-Diagnostics`; runtime counters come
  from `System.Runtime`. Both consumers prefix every line with the provider name in
  brackets, e.g. `[Example-App-Diagnostics]` vs `[System.Runtime]`.
- **Source.** App counters have a file under `ExampleApp/Diagnostics/`. Runtime counters have
  no app code at all — only the listener's call to enable the provider.
