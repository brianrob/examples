# Running the sample

You build and run **two** programs and have them talk over the local EventPipe channel:

- `ExampleApp` — the .NET MAUI app that produces the counters.
- `CounterListener` — the console app that attaches and prints them.

> Both must run on the **same machine**. EventPipe is a local diagnostics IPC channel; there
> is no remote/over-the-network mode.

---

## Prerequisites

```bash
dotnet --version            # expect 11.0.x (this sample pins 11.0.100-preview.5 via global.json)
dotnet workload install maui
```

- **Windows:** Windows 10 19041 or newer.
- **macOS:** a Mac with Xcode installed (required to build the Mac Catalyst head).

---

## Windows

### 1. Build and launch the app

```powershell
cd src/maui-diagnostics-example/src/ExampleApp
dotnet build -c Debug -f net11.0-windows10.0.19041.0
./bin/Debug/net11.0-windows10.0.19041.0/win-x64/ExampleApp.exe
```

The app window shows its **PID** at the top, e.g.:

```
PID: 15404  (pass this to: CounterListener --pid 15404)
```

### 2. Attach the out-of-process listener

In a second terminal:

```powershell
cd src/maui-diagnostics-example/src/CounterListener
dotnet run -- --pid 15404 --interval 1
```

You should see a stream like:

```
12:34:56  [System.Runtime]            GC Heap Size               =        13.2 MB
12:34:56  [System.Runtime]            ThreadPool Thread Count    =           2
12:34:56  [Example-App-Diagnostics]   Frame Rate                 =        59.7 fps
12:34:56  [Example-App-Diagnostics]   Thread Pool Queue Delay    =        0.12 ms
```

At the same time the **in-process** pane inside the app window shows the same samples — the
two consumers are reading the identical counters.

### 3. Drive the demos

With both running, click the app's buttons:

- **Add render load** → `Frame Rate` drops but stays above zero.
- **Induce thread-pool starvation** → `Thread Pool Queue Delay` and `ThreadPool Queue
  Length` spike for ~3 s, then recover.
- **Freeze UI (3s)** → `Frame Rate` collapses to ~0 fps for 3 s, then recovers.

### 4. Reverse-connect (optional on Windows)

PID attach (above) is the simplest path on Windows, but the listener can also **host** the
diagnostic port and have the app connect out to it — the same flow that is required on
Mac Catalyst. On Windows the diagnostic port is a **named pipe**; pass a **bare name**
(no `\\.\pipe\` prefix) to **both** sides — the diagnostics library adds the transport
prefix for you.

```powershell
# Terminal 1 — listener hosts the port and waits for the app:
cd src/maui-diagnostics-example/src/CounterListener
dotnet run -- --diagnostic-port exampleapp-diag --warmup-seconds 0 --interval 1
# It prints the exact DOTNET_DiagnosticPorts value to use.

# Terminal 2 — launch the app pointed at the same port:
$env:DOTNET_DiagnosticPorts = "exampleapp-diag,nosuspend"
./bin/Debug/net11.0-windows10.0.19041.0/win-x64/ExampleApp.exe
```

The listener prints `App connected.` and then streams both providers. `--warmup-seconds 0`
is recommended on Windows/CoreCLR (the warm-up only matters for .NET 10/Mono Mac Catalyst).
The `,nosuspend` suffix lets the app start without waiting to be resumed, so the
listener's `ResumeRuntime()` call is a harmless no-op.

---

## macOS (Mac Catalyst)

macOS differs from Windows in two ways (both described below):

- **Runtime:** .NET 11 runs Mac Catalyst on **CoreCLR** (the sample sets `UseMonoRuntime=false`),
  which has the EventPipe diagnostics server built in. On .NET 10 / MonoVM, diagnostics are an
  opt-in component (the sample sets `EnableDiagnostics=true`).
- **Attach:** the app is **sandboxed**, so `--pid` does not work (the default socket path exceeds
  the 108-char UNIX limit). Use **reverse-connect**: the listener hosts a short socket path inside
  the app's container and the app connects to it.

### 1. Build the app

```bash
cd src/maui-diagnostics-example/src/ExampleApp
dotnet build -c Debug -f net11.0-maccatalyst -p:RuntimeIdentifier=maccatalyst-arm64   # or -x64 on Intel
```

### 2. Start the listener (it hosts the port), then launch the app

```bash
# Rendezvous socket MUST live inside the app's sandbox container:
APPTMP="$HOME/Library/Containers/com.companyname.exampleapp/Data/tmp"; mkdir -p "$APPTMP"
PORT="$APPTMP/exampleapp.sock"

# Terminal 1 — listener hosts the port and waits:
cd src/maui-diagnostics-example/src/CounterListener
dotnet run -- --diagnostic-port "$PORT" --interval 1

# Terminal 2 — launch the app so it connects to the listener:
APP=../ExampleApp/bin/Debug/net11.0-maccatalyst/maccatalyst-arm64/ExampleApp.app
DOTNET_DiagnosticPorts="$PORT,nosuspend" "$APP/Contents/MacOS/ExampleApp"
```

The listener prints `App connected.` and streams both `[System.Runtime]` and
`[Example-App-Diagnostics]` counters, matching the app's in-process pane.

### macOS notes

- The rendezvous socket must be **inside the app's container** (`~/Library/Containers/<bundle-id>/Data/tmp/`);
  the sandbox blocks the app from reaching sockets elsewhere (e.g. your shell's `TMPDIR`).
- Launch the **inner executable** (not `open`) so the app inherits `DOTNET_DiagnosticPorts`.
- Listener and app must run as the **same user**.

---

## Finding the PID without the app window

If you'd rather not read the PID off the window, the listener can look the process up by
name (it defaults to `ExampleApp`):

```bash
dotnet run -- --name ExampleApp --interval 1
```

If more than one match is found, the listener lists the candidates and asks you to pick one
with `--pid`.

---

## `CounterListener` options

```
-p, --pid <id>             PID of the target process. If omitted, looks up by name.
                           (Windows / desktop only — not for sandboxed Mac Catalyst.)
-n, --name <substring>     Match the target by process name (default: "ExampleApp").
    --diagnostic-port <p>  Reverse-connect: host socket path <p> (macOS/Linux) or named pipe
    --dport <p>            (Windows) and wait for the app to connect. Launch the app with
                           DOTNET_DiagnosticPorts=<p>,nosuspend. Required on Mac Catalyst.
    --connect-timeout <s>  Seconds to wait for the app in reverse-connect mode (default: 60).
    --warmup-seconds <s>   Reverse-connect: seconds to wait after the app connects before enabling
                           counters, so a freshly-launched app finishes initializing first
                           (default: 3; needed on .NET 10/Mono Mac Catalyst, harmless elsewhere).
-i, --interval <sec>       Counter sampling interval in seconds (default: 1).
-d, --duration <sec>       Stop automatically after this many seconds (default: run until Ctrl+C).
    --provider <name>      Custom app provider name (default: "Example-App-Diagnostics").
-h, --help                 Show help.
```

Examples:

```bash
dotnet run -- --pid 15404                                  # Windows: attach by PID
dotnet run -- --name ExampleApp --interval 2               # Windows: attach by name
dotnet run -- --diagnostic-port "$PORT" --duration 10      # reverse-connect (required on macOS)
```

Stop a manual run any time with **Ctrl+C**.

---

## Building everything at once

The solution builds both projects (the app head matches your OS):

```bash
cd src/maui-diagnostics-example
dotnet build DiagnosticsExample.slnx -c Debug
```

---

## Troubleshooting

| Symptom | Likely cause / fix |
|---------|--------------------|
| `No diagnosable .NET process matching 'ExampleApp'` | The app isn't running, or it's a different user. Launch the app first; pass `--pid` (Windows). |
| Listener connects but shows **only** `System.Runtime` counters | The app's provider isn't enabled. Make sure you're attached to the MAUI app (not another .NET process) and that `--provider` matches `Example-App-Diagnostics`. |
| `Frame Rate` is `0 fps` | The app window is minimized/occluded (nothing is painting) or the UI thread is frozen. Bring the window to the foreground. |
| **macOS:** `--pid` fails with *"socket path may exceed 108 characters"* | Expected for sandboxed Mac Catalyst. Use reverse-connect: `--diagnostic-port "$PORT"` + launch with `DOTNET_DiagnosticPorts="$PORT,nosuspend"`. |
| **macOS:** listener keeps "waiting for the app to connect" | `$PORT` must be **inside** the app's container (`~/Library/Containers/com.companyname.exampleapp/Data/tmp/`), and start the listener *before* the app. |
