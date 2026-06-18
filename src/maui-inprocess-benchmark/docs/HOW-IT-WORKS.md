# How it works

This document explains the design of the in-process MAUI benchmark and how to
extend it for your own application.

## The problem with cross-process UI benchmarking

The usual way to "drive" a UI from the outside is a UI-automation framework
(Appium, WinAppDriver, the Mac accessibility APIs, etc.). Those run in a
**separate process** from the app and talk to it over an RPC channel. Every
"tap this button" or "read this property" is a round-trip across a process
boundary — typically **milliseconds** of latency per call, dominated by
serialization and IPC, not by the app code you actually want to measure.

For micro-benchmarking — where the operations themselves can take microseconds —
that overhead makes the measurement meaningless.

## The approach: one process, two threads

Instead, we run BenchmarkDotNet **inside the live MAUI app process**:

- The **UI thread** runs the real MAUI host, the real `Application`, and the
  real pages with real platform handlers (WinUI on Windows, UIKit on Mac
  Catalyst). These are the on-screen objects, not mocks.
- The **BenchmarkDotNet engine thread** runs the benchmark loop. It is a normal
  background thread — *not* the UI thread.
- Each benchmark hands its UI work to the UI thread with an in-memory dispatcher
  post and blocks until it completes. That hand-off is a queue insert + thread
  wakeup, all in the same address space. No IPC.

```
BDN engine thread                 UI thread
-----------------                 ---------
UiDispatcher.Run(work) ----post--> (work runs against real views)
        |  (blocks)                       |
        | <----------- result/exception --+
   measured wall-clock includes only the in-process marshal + the work
```

## The pieces

### `UiDispatcher` (Benchmarks/UiDispatcher.cs)

A static helper around MAUI's `IDispatcher`. It posts a delegate to the UI
thread and blocks the caller until it finishes, propagating the return value or
any exception. There are sync overloads (`Run`) and async overloads (`RunAsync`,
for things like navigation that return a `Task`).

`UiDispatcher.Instance` is assigned once, from the UI thread, in
`MainPage`'s constructor:

```csharp
UiDispatcher.Instance = Dispatcher;   // Page.Dispatcher is the UI dispatcher
```

### `BenchmarkHost` (Benchmarks/BenchmarkHost.cs)

A tiny static holder for references to the **live** app objects (the home page).
`MainPage` populates it in its constructor. The benchmark class reads from it in
`[GlobalSetup]`. This is how the benchmark gets its hands on real, realized UI.

### `UiBenchmarks` (Benchmarks/UiBenchmarks.cs)

The `[Benchmark]` methods. Each one wraps its UI work in `UiDispatcher.Run(...)`
so it executes on the UI thread. `[GlobalSetup]` grabs the live page and pushes
an `ItemsPage` so its `CollectionView` is realized for the list/layout
benchmarks; `[GlobalCleanup]` pops back to the home page.

### `BenchmarkLauncher` (Benchmarks/BenchmarkLauncher.cs)

Starts BenchmarkDotNet. Two important details:

1. **InProcess toolchain.** By default BenchmarkDotNet *generates and launches a
   separate console executable* per job. That generated host cannot bootstrap
   WinUI/Mac Catalyst, and spawning a second process would reintroduce exactly
   the cross-process latency we are trying to avoid. So we configure an
   in-process job:

   ```csharp
   ManualConfig.Create(DefaultConfig.Instance)
       .AddJob(Job.ShortRun.WithToolchain(InProcessNoEmitToolchain.Instance))
       .WithOptions(ConfigOptions.DisableOptimizationsValidator);
   ```

2. **It runs on a background thread.** `BenchmarkRunner.Run` is blocking; if we
   called it on the UI thread the app would freeze and the dispatcher posts from
   the benchmark would deadlock. So the launcher does `Task.Run(RunCore)`,
   leaving the UI thread free to service the dispatcher.

`DisableOptimizationsValidator` lets the demo also run from a **Debug** build.
For real numbers, build **Release** (see RUNNING.md).

## Threading rules (important)

- The benchmark loop must **not** run on the UI thread (it blocks on dispatcher
  posts → deadlock). The launcher guarantees this with `Task.Run`.
- All UI object access must go **through `UiDispatcher`**, even reads, because
  MAUI views are not thread-safe.
- The UI thread must stay pumping while benchmarks run. It does, because nothing
  blocks it — the blocking `BenchmarkRunner.Run` is on a background thread.

## Launch modes

`BenchmarkLauncher.ShouldAutoRun()` returns true when **either**:

- the process was started with the `--benchmark` argument, or
- the environment variable `RUN_BENCHMARKS=1` is set.

In that case the app, once its first page is `Loaded`, runs the benchmarks and
then calls `Application.Current.Quit()` so the process exits cleanly (handy for
CI). Without the switch, the same project is just a normal, clickable MAUI app —
and you can still trigger a run from the **"Run UI benchmarks"** button on the
home page.

## Adding your own benchmarks

1. Add a `[Benchmark]` method to `UiBenchmarks` (or a new class — register it in
   `BenchmarkLauncher.RunCore`). Keep the body inside `UiDispatcher.Run(...)`:

   ```csharp
   [Benchmark]
   public void MyScenario() => UiDispatcher.Run(() =>
   {
       // touch your real view models / views here
   });
   ```

2. If your scenario needs a realized control (handlers attached), build it and
   push it in `[GlobalSetup]` the way `ItemsPage` is, and store a field for it.

3. For `async` UI operations (navigation, animations, `Task`-returning APIs) use
   `UiDispatcher.RunAsync(async () => { ... });`.

4. Swap `Job.ShortRun` for `Job.Default` in `BenchmarkLauncher.CreateConfig()`
   when you want full-precision results.

## Limitations / notes

- **Measure cross-platform code, not GPU frames.** The benchmarks drive the
  cross-platform view/binding/layout code (measure/arrange, property sets,
  collection changes). They do not wait on the native compositor to present a
  frame, so results reflect *your app's code*, not display refresh timing. If
  you specifically need frame timing, that is a different (and inherently more
  platform-specific) measurement.
- **Console output.** A MAUI WinUI app is a windowed process with no console, so
  on Windows the launcher calls `AllocConsole()` so you can watch the run live.
  Either way, BenchmarkDotNet always writes the full report to
  `BenchmarkDotNet.Artifacts/`.
- **One process per machine architecture.** Because everything is in-process,
  the benchmark runs against the same runtime/JIT the app uses — which is
  usually exactly what you want.
