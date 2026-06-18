# Mac Catalyst runbook

A step-by-step guide to building and running this repro on a Mac, including the
in-process benchmark run. This complements [RUNNING.md](RUNNING.md) with macOS
specifics.

> Authored/validated on Windows; the Mac Catalyst head is wired up and ready.
> This runbook is what to follow on the Mac.

---

## 1. Prerequisites (one time)

```bash
# .NET 10 SDK — verify
dotnet --version          # should print 10.x

# Xcode (required for any Apple build). Install from the App Store, then:
sudo xcode-select -s /Applications/Xcode.app/Contents/Developer
sudo xcodebuild -license accept

# The MAUI workload
dotnet workload install maui
```

Know your Mac's CPU architecture — it picks the runtime identifier (RID):

```bash
uname -m
#   arm64  -> Apple Silicon (M1/M2/M3/M4)  -> RID: maccatalyst-arm64
#   x86_64 -> Intel                         -> RID: maccatalyst-x64
```

Set a shell variable you'll reuse below:

```bash
RID=maccatalyst-arm64   # or maccatalyst-x64 on Intel
```

---

## 2. Get the code

```bash
git clone https://github.com/brianrob/repros.git
cd repros
# (main already contains this repro)
```

---

## 3. Build the Mac Catalyst head

```bash
cd src/MauiBenchDemo
dotnet build -c Release -f net10.0-maccatalyst -p:RuntimeIdentifier=$RID
```

This produces an app bundle at:

```
bin/Release/net10.0-maccatalyst/$RID/MauiBenchDemo.app
```

---

## 4. Run it as a normal demo app

Easiest:

```bash
dotnet build -c Release -f net10.0-maccatalyst -p:RuntimeIdentifier=$RID -t:Run
```

A window opens. Click **Increment counter**, **Go to Items list**, or
**Run UI benchmarks (in-process)** to trigger a run while the app stays open.

---

## 5. Run the benchmarks headlessly (the important one)

The benchmark switch is read from the `--benchmark` argument **or** the
`RUN_BENCHMARKS=1` environment variable. Launch the **inner executable** inside
the app bundle (not `open`) so the terminal's stdout and environment are
inherited — that's how you see the live BenchmarkDotNet output and pass the env var:

```bash
APP=bin/Release/net10.0-maccatalyst/$RID/MauiBenchDemo.app
EXE="$APP/Contents/MacOS/MauiBenchDemo"

# Either of these works:
"$EXE" --benchmark
# or
RUN_BENCHMARKS=1 "$EXE"
```

The app boots, runs the five benchmarks **in-process** (you'll see
`Toolchain=InProcessNoEmitToolchain` in the header), prints the table, then quits.

> If `Contents/MacOS/MauiBenchDemo` doesn't exist, list the bundle to find the
> executable name: `ls "$APP/Contents/MacOS/"`.

---

## 6. Where the results land

```
BenchmarkDotNet.Artifacts/results/*-report-github.md   <- the Markdown table
BenchmarkDotNet.Artifacts/results/*-report.csv
BenchmarkDotNet.Artifacts/results/*-report.html
BenchmarkDotNet.Artifacts/*.log
```

These are written relative to the process's working directory (the folder you
ran the executable from).

---

## 7. Sanity checklist

- [ ] `dotnet --version` is 10.x and `dotnet workload list` shows `maui`.
- [ ] `uname -m` matched the `RID` you used.
- [ ] Release build succeeded with `-f net10.0-maccatalyst`.
- [ ] Headless run printed a table whose header says
      `Toolchain=InProcessNoEmitToolchain` and `.NET 10`.
- [ ] The process exited on its own (benchmark mode self-quits).

---

## Troubleshooting (macOS specific)

- **`xcrun: error` / signing failures** — re-run the `xcode-select` and
  `xcodebuild -license accept` steps above; open Xcode once to finish setup.
- **Wrong architecture / "bad CPU type"** — your `RID` doesn't match `uname -m`.
  Rebuild with the correct `maccatalyst-arm64` vs `maccatalyst-x64`.
- **No benchmark output / app just shows a window** — you launched via `open`
  (which doesn't inherit env vars) or forgot the switch. Use the inner
  `Contents/MacOS/MauiBenchDemo` executable with `--benchmark`.
- **Want higher-precision numbers** — switch `Job.ShortRun` to `Job.Default` in
  `Benchmarks/BenchmarkLauncher.cs` (`CreateConfig`).
- **Optimization warning** — build `-c Release` (Debug is allowed for iteration
  but not for trustworthy measurements).

## Notes

- No `AllocConsole` hack is needed on macOS: launched from a terminal, the app's
  stdout goes straight to your terminal. (That Windows-only code is guarded by
  `#if WINDOWS`.)
- Everything still runs in **one process** — the BenchmarkDotNet engine thread
  and the live UIKit-backed MAUI UI share the address space, so there is no
  cross-process latency, exactly as on Windows.
