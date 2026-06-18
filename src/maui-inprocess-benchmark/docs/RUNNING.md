# Running the demo and the benchmarks

## Prerequisites

- **.NET 10 SDK** (`dotnet --version` should report 10.x)
- **MAUI workload**:
  ```
  dotnet workload install maui
  ```
- **Windows**: Windows 10 build 19041 or newer (for the WinUI head)
- **macOS**: a Mac with Xcode installed (for the Mac Catalyst head)

The solution (`MauiBenchDemo.slnx`) and project intentionally target only
**`net10.0-windows10.0.19041.0`** and **`net10.0-maccatalyst`**. Each OS builds
its own head (Windows builds WinUI, macOS builds Mac Catalyst).

---

## Windows (WinUI 3)

All commands from the repo root unless noted.

### Build

```powershell
cd src/MauiBenchDemo
dotnet build -c Release -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
```

`-p:WindowsPackageType=None` builds an **unpackaged** app so you can launch the
`.exe` directly.

### Run as a normal demo app

```powershell
./bin/Release/net10.0-windows10.0.19041.0/win-x64/MauiBenchDemo.exe
```

A window opens. Click **Increment counter**, **Go to Items list**, or
**Run UI benchmarks (in-process)** to trigger a benchmark run while the app keeps
running.

### Run the benchmarks headlessly (self-quits)

```powershell
$env:RUN_BENCHMARKS = '1'
./bin/Release/net10.0-windows10.0.19041.0/win-x64/MauiBenchDemo.exe
```

The app boots, runs the benchmarks in-process, prints to a console window, writes
results to `BenchmarkDotNet.Artifacts/`, and exits.

You can also pass the switch as an argument instead of the env var:

```powershell
./bin/Release/net10.0-windows10.0.19041.0/win-x64/MauiBenchDemo.exe --benchmark
```

---

## Mac Catalyst

> Run these on a Mac. (This repro was authored/validated on Windows; the Mac
> Catalyst head is wired up and ready, validate on macOS.)
>
> For a full step-by-step Mac guide (prerequisites, architecture/RID selection,
> sanity checklist, troubleshooting) see **[MAC-RUNBOOK.md](MAC-RUNBOOK.md)**.

Pick the runtime identifier (RID) for your Mac — `maccatalyst-arm64` on Apple
Silicon, `maccatalyst-x64` on Intel (`uname -m` → `arm64` vs `x86_64`):

```bash
RID=maccatalyst-arm64   # or maccatalyst-x64 on Intel
```

### Build

```bash
cd src/MauiBenchDemo
dotnet build -c Release -f net10.0-maccatalyst -p:RuntimeIdentifier=$RID
```

### Run as a normal demo app

```bash
dotnet build -c Release -f net10.0-maccatalyst -p:RuntimeIdentifier=$RID -t:Run
```

### Run the benchmarks headlessly

Launch the **inner executable** inside the built `.app` bundle (not `open`) so
the terminal's stdout and environment are inherited:

```bash
APP=bin/Release/net10.0-maccatalyst/$RID/MauiBenchDemo.app
"$APP/Contents/MacOS/MauiBenchDemo" --benchmark
# or:  RUN_BENCHMARKS=1 "$APP/Contents/MacOS/MauiBenchDemo"
```

Console output appears in your terminal; the full report is written to
`BenchmarkDotNet.Artifacts/` relative to the working directory.

---

## Debug vs Release

- The benchmarks are configured to also run from a **Debug** build (via
  `ConfigOptions.DisableOptimizationsValidator`) so you can iterate quickly.
- For **trustworthy numbers, always use `-c Release`**. BenchmarkDotNet will warn
  if it detects a non-optimized build.

## Faster vs more precise

`BenchmarkLauncher.CreateConfig()` uses **`Job.ShortRun`** (1 launch, 3 warmup,
3 iterations) so the demo finishes in seconds. For higher precision, change it to
`Job.Default` (or remove the explicit job and just keep the toolchain):

```csharp
.AddJob(Job.Default.WithToolchain(InProcessNoEmitToolchain.Instance))
```

## Where are the results?

Next to the executable / inside the app bundle's working directory:

```
BenchmarkDotNet.Artifacts/
  results/
    *-report-github.md     <- Markdown table (the one shown in the README)
    *-report.csv
    *-report.html
  *.log                    <- full run log
```

## Troubleshooting

- **"workload not installed"** → run `dotnet workload install maui` and retry.
- **No console output on Windows** → that's expected for a windowed app; the
  launcher allocates a console for you, but if you don't see it, open the
  `*-report-github.md` in `BenchmarkDotNet.Artifacts/results/`.
- **The app window stays open in benchmark mode** → make sure `RUN_BENCHMARKS=1`
  is set in the *same* shell that launches the exe, or pass `--benchmark`.
- **A deadlock / hang** → check that any new UI access goes through
  `UiDispatcher.Run(...)` and that you did not call `BenchmarkRunner.Run` on the
  UI thread (the launcher already runs it on a background thread).
