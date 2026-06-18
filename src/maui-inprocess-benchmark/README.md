# MauiBenchDemo — driving a .NET MAUI UI from BenchmarkDotNet, in-process

This is a small, self-contained repro that shows how to **micro-benchmark a live
.NET MAUI user interface with [BenchmarkDotNet](https://benchmarkdotnet.org/),
without any cross-process communication.**

The benchmark and the running MAUI app live in the **same process**, so the
benchmark can poke at real views, view models, bindings, layout and navigation
in a tight loop. There is no UI-automation driver, no Appium/WinAppDriver, no
pipe or socket — and therefore none of the per-call latency that cross-process
automation adds.

> Built for .NET 10, targeting **Windows (WinUI 3)** and **Mac Catalyst**.

---

## TL;DR

```powershell
# Windows
cd src/MauiBenchDemo
dotnet build -c Release -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None

# Run as a normal demo app (a clickable window):
./bin/Release/net10.0-windows10.0.19041.0/win-x64/MauiBenchDemo.exe

# Run the benchmarks in-process (self-quits when done):
$env:RUN_BENCHMARKS = '1'
./bin/Release/net10.0-windows10.0.19041.0/win-x64/MauiBenchDemo.exe
```

Results print to a console window and are also written to
`BenchmarkDotNet.Artifacts/` next to the executable.

See **[docs/RUNNING.md](docs/RUNNING.md)** for the Mac Catalyst steps and more
detail, **[docs/MAC-RUNBOOK.md](docs/MAC-RUNBOOK.md)** for a full step-by-step
Mac guide, and **[docs/HOW-IT-WORKS.md](docs/HOW-IT-WORKS.md)** for the design.

---

## The idea in one picture

```
+---------------------------- single OS process ----------------------------+
|                                                                           |
|   UI thread (WinUI on Windows / Mac Catalyst on macOS)                     |
|        MAUI host  ->  App  ->  MainPage (REAL, on-screen views)            |
|             ^                              |                               |
|             |  Dispatch(work)             |  result / exception           |
|             |                              v                               |
|   BenchmarkDotNet engine thread  --(blocking in-memory marshal)--          |
|        [Benchmark] BuildViewTree / UpdateBinding / LayoutPass / ...        |
|                                                                           |
+---------------------------------------------------------------------------+
```

The app **is** the benchmark host. A launch switch tells it to start
BenchmarkDotNet (using the **InProcess toolchain**) on a background thread once
the window is up. Each benchmark hands its UI work to the UI thread through a
tiny dispatcher helper and blocks for the result.

---

## What's in the box

```
src/MauiBenchDemo/
  MainPage.xaml(.cs)              Demo home page (bound VM + buttons, incl. "Run benchmarks")
  Views/ItemsPage.xaml(.cs)      CollectionView bound to an ObservableCollection
  Views/DetailPage.xaml(.cs)     Navigation target
  ViewModels/                    INotifyPropertyChanged view models
  Models/Item.cs                 Trivial data record
  Benchmarks/
    UiDispatcher.cs              Blocking "run this on the UI thread" helper
    BenchmarkHost.cs             Holds references to the live app objects
    UiBenchmarks.cs              The 5 sample [Benchmark] methods
    BenchmarkLauncher.cs         Starts BenchmarkDotNet in-process, then quits
docs/
  HOW-IT-WORKS.md                Architecture + how to add your own benchmarks
  RUNNING.md                     Per-platform build/run instructions
```

## The sample benchmarks

| Benchmark                | What it exercises                                            |
|--------------------------|-------------------------------------------------------------|
| `BuildViewTree`          | Constructing a fresh view tree (view object creation)       |
| `UpdateBindingAndLayout` | Changing a bound property + a layout pass                   |
| `LayoutPass`             | Measure + arrange of the realized page (layout engine only) |
| `MutateBoundList`        | Add/remove on a collection bound to a `CollectionView`      |
| `NavigatePushPop`        | Navigate to a page and back (handler create/teardown)       |

### Example output (Windows, Debug, ShortRun)

```
BenchmarkDotNet v0.15.8, .NET 10.0.9, Toolchain=InProcessNoEmitToolchain

| Method                 | Mean        | Allocated |
|----------------------- |------------:|----------:|
| BuildViewTree          |  2,040.4 us | 706.28 KB |
| UpdateBindingAndLayout |    155.9 us |   1.69 KB |
| LayoutPass             |     98.8 us |    1.4 KB |
| MutateBoundList        |  5,071.2 us |  82.56 KB |
| NavigatePushPop        | 49,035.9 us | 286.16 KB |
```

(Numbers are illustrative — use Release + `Job.Default` for real measurements;
see [docs/RUNNING.md](docs/RUNNING.md).)

---

## Adapting this to your app

You only need to copy the four files in `Benchmarks/` and follow the pattern:

1. In your first page's constructor, set `UiDispatcher.Instance = Dispatcher;`
   and stash a reference to the live page (`BenchmarkHost.Home = this;`).
2. Call `BenchmarkLauncher.AutoRunWhenReady(this);` so `--benchmark` works.
3. In `UiBenchmarks`, replace the method bodies with the operations you care
   about, always wrapping UI work in `UiDispatcher.Run(...)`.

Full walkthrough in **[docs/HOW-IT-WORKS.md](docs/HOW-IT-WORKS.md)**.

## Prerequisites

- .NET 10 SDK
- The MAUI workload: `dotnet workload install maui`
- Windows: Windows 10 19041+; macOS: a Mac with Xcode for the Mac Catalyst head
