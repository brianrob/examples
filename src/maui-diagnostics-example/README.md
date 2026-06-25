# maui-diagnostics-example — custom EventCounters on .NET MAUI, in-process and over EventPipe

This sample shows how to instrument a **.NET MAUI** app with **EventCounters** and read
those counters two different ways:

1. **In-process** — an `EventListener` living *inside* the app receives counter samples
   directly (no IPC) and prints them to a scrolling pane in the UI.
2. **Out-of-process** — a separate console app (`CounterListener`) attaches to the running
   MAUI process over the **EventPipe diagnostics IPC channel** and streams the same
   counters, parsing the nettrace stream with **TraceEvent 3.2.4**.

It demonstrates counters from both the runtime and the app:

| Counter | Provider | Authored by |
|---------|----------|-------------|
| GC heap size, thread-pool thread count, queue length, etc. | `System.Runtime` | **the runtime** (no app code) |
| **Frame rate** (`frame-rate`, fps) | `Example-App-Diagnostics` | **this app** |
| **Thread-pool queue delay** (`threadpool-queue-delay-ms`) | `Example-App-Diagnostics` | **this app** |
| **UI-thread lag / hang count** (`ui-thread-lag-ms`, `ui-hang-count`) | `Example-App-Diagnostics` | **this app** |

The frame-rate, queue-delay, and UI-lag counters don't exist in the runtime, so the app
implements them. The app also detects two failure modes — **thread-pool starvation** and
**UI-thread hangs (>500 ms)** — and reports each as both a counter *and* an edge-triggered
event, so they're visible in-process and over EventPipe. Buttons let you **deliberately**
trigger every signal: add per-frame render load, induce thread-pool starvation, and freeze the
UI thread.

> Built for **.NET 11** (which runs Mac Catalyst on **CoreCLR**), targeting **Windows (WinUI 3)**
> and **Mac Catalyst**. The `CounterListener` console is plain cross-platform .NET 11. The sample
> can also be built for .NET 10 (Mac Catalyst on MonoVM) — see
> [docs/RUNNING.md](docs/RUNNING.md) for the .NET 10 vs .NET 11 differences.

---

## TL;DR (Windows)

```powershell
# 1. Build and launch the MAUI app
cd src/maui-diagnostics-example/src/ExampleApp
dotnet build -c Debug -f net11.0-windows10.0.19041.0
./bin/Debug/net11.0-windows10.0.19041.0/win-x64/ExampleApp.exe
# The app window shows its own PID at the top.

# 2. In another terminal, attach the out-of-process listener (uses the PID from the app)
cd ../CounterListener
dotnet run -- --pid <PID-from-app> --interval 1
```

You'll see both `[System.Runtime]` and `[Example-App-Diagnostics]` counters stream in the
console **and** in the app's in-process pane at the same time. Press the app's buttons and
watch the numbers react in both places.

> **macOS (Mac Catalyst) is different.** The app is sandboxed, so `--pid` attach does not work
> (the socket path exceeds the 108-char UNIX limit). Use **reverse-connect**: the listener hosts
> the port and the app connects to it. See the macOS section of **[docs/RUNNING.md](docs/RUNNING.md)**.

See **[docs/RUNNING.md](docs/RUNNING.md)** for the full Windows + Mac steps,
**[docs/HOW-IT-WORKS.md](docs/HOW-IT-WORKS.md)** for the design, and
**[docs/COUNTERS.md](docs/COUNTERS.md)** for a per-counter reference.

---

## The idea in one picture

```
+------------------ app process (ExampleApp) ------------------+        +------------- CounterListener process ----------+
|  AppEventSource  "Example-App-Diagnostics"  (app code)       |        |  DiagnosticsClient (NETCore.Client)            |
|    - PollingCounter "frame-rate" / "ui-thread-lag-ms"        |        |    opens an EventPipe streaming session         |
|    - EventCounter   "threadpool-queue-delay-ms"            | EventPipe |  by PID/name and enables:                     |
|    - IncrementingEventCounter "ui-hang-count"               |==IPC====>|    - System.Runtime          (runtime)        |
|    - events: starvation/hang detected, recovered, ...      | (stream) |    - Example-App-Diagnostics (app)            |
|  FrameCanvasDrawable -> frames; ThreadPoolQueueDelayCounter |        |  EventPipeEventSource (TraceEvent 3.2.4)       |
|  UiHangDetector -> heartbeat + watchdog (UI hang)           |        |    decodes counters AND app events -> a line    |
|  InProcessCounterListener : EventListener -> two-pane log   |        |                                                |
|  MainPage: PID, live values, demo buttons, two-pane log     |        |                                                |
+-------------------------------------------------------------+        +-----------------------------------------------+
```

The same counters feed **both** consumers. The in-process `EventListener` is the simplest
possible consumer; the out-of-process `CounterListener` is what a tool like `dotnet-counters`
or `dotnet-trace` does under the hood.

---

## What's in the box

```
src/maui-diagnostics-example/
  README.md                          This file
  global.json                        Pin the .NET 11 SDK (11.0.100-preview.5)
  DiagnosticsExample.slnx            Solution with both projects
  docs/
    HOW-IT-WORKS.md                  Design: EventSource, counters, in-proc vs out-of-proc, EventPipe IPC
    RUNNING.md                       Build/run both apps; Windows + Mac steps; finding the PID
    COUNTERS.md                      Reference for every counter (name, type, app vs runtime)
  src/
    ExampleApp/                      .NET MAUI app (Windows + Mac Catalyst)
      Diagnostics/
        AppEventSource.cs              THE single app EventSource ("Example-App-Diagnostics")
        FrameRateCounter.cs            Frame-rate counter (PollingCounter + frame tally)
        FrameCanvasDrawable.cs         IDrawable that paints + counts each frame
        ThreadPoolQueueDelayCounter.cs Queue-delay counter (EventCounter + latency probe + starvation detect)
        ThreadPoolStarvation.cs        Helper that deliberately starves the pool (demo control)
        UiHangDetector.cs              UI-hang detection (heartbeat + watchdog -> lag counter, hang count, events)
        FramePump.cs                   Cross-platform per-frame (vsync) driver
        InProcessCounterListener.cs    In-proc EventListener + the shared CounterLine / RawEventLine records
        DiagnosticsBootstrapper.cs     Turns it all on at startup
      MainPage.xaml(.cs)             PID, live values, buttons, two-pane log (counter snapshot + raw events)
    CounterListener/                 Out-of-process console consumer
      Program.cs                       Session start, stream parse, print loop
      ProcessResolver.cs               Find the target by --pid or --name
      CounterTablePrinter.cs           Decode "EventCounters" + app events -> a line
      CommandLineOptions.cs            Arg parsing + usage
```

## Demo buttons

| Button | What it does | What to watch |
|--------|--------------|---------------|
| **Add render load** | Burns ~25 ms of busy work per painted frame | `frame-rate` drops (but stays > 0) |
| **Induce thread-pool starvation** | Floods the pool with blocking work items for 3 s | `threadpool-queue-delay-ms` and `System.Runtime` `ThreadPool Queue Length` spike, then recover |
| **Freeze UI (3s)** | Blocks the UI thread with `Thread.Sleep` | `frame-rate` collapses to **~0 fps**; `ui-thread-lag-ms` climbs past **500 ms** toward ~3000; `ui-hang-count` ticks up; a `UiHangDetected`/`UiHangEnded` event pair is logged — all visible in-process and over EventPipe |

The "Freeze UI" button is the headline hang demo: a hung UI thread shows up immediately as a
frame-rate cliff **and** as `ui-thread-lag-ms` shooting past the 500 ms hang threshold, in both
the in-process pane and the out-of-process console.

## Prerequisites

- .NET 11 SDK (this sample pins `11.0.100-preview.5` via `global.json`)
- The MAUI workload: `dotnet workload install maui`
- Windows: Windows 10 19041+; macOS: a Mac with Xcode for the Mac Catalyst head
- Listener and app must run on the **same machine** (EventPipe IPC is local-only)
- On macOS the app is sandboxed → attach via **reverse-connect** (`CounterListener --diagnostic-port`),
  not `--pid`; see the macOS section of [docs/RUNNING.md](docs/RUNNING.md)
