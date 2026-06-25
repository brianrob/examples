using System.Diagnostics.Tracing;
using System.Globalization;
using CounterListener;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;

CommandLineOptions? options = CommandLineOptions.Parse(args);
if (options is null)
{
    CommandLineOptions.PrintUsage();
    return 1;
}

// Both providers are enabled as EventCounter providers by passing "EventCounterIntervalSec".
// System.Runtime ships with the runtime (GC, thread pool, etc.); Example-App-Diagnostics is
// authored by the sample MAUI app.
var counterArguments = new Dictionary<string, string>
{
    ["EventCounterIntervalSec"] = options.IntervalSeconds.ToString(CultureInfo.InvariantCulture),
};

var providers = new[]
{
    new EventPipeProvider("System.Runtime", EventLevel.Informational, (long)EventKeywords.All, counterArguments),
    new EventPipeProvider(options.AppProviderName, EventLevel.Informational, (long)EventKeywords.All, counterArguments),
};

// Obtain a DiagnosticsClient in one of two ways:
//   1. Reverse-connect (--diagnostic-port): WE host the diagnostics socket/pipe and the app
//      connects out to it. This is the model required for sandboxed Mac Catalyst apps, whose
//      default per-PID socket lives under a long container path that exceeds the 108-char UNIX
//      socket limit (so PID attach cannot open it). It also works on Windows.
//   2. PID/name: attach to an already-running, diagnosable process via its default port. Works on
//      Windows and desktop .NET.
DiagnosticsClient client;
DiagnosticsClientConnector? connector = null;
bool reverseConnect = options.DiagnosticPort is not null;

if (options.DiagnosticPort is string diagnosticPort)
{
    Console.WriteLine($"Hosting diagnostic port '{diagnosticPort}' and waiting up to " +
        $"{options.ConnectTimeoutSeconds}s for the app to connect ...");
    Console.WriteLine($"Launch the app with: DOTNET_DiagnosticPorts={diagnosticPort},nosuspend");

    try
    {
        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.ConnectTimeoutSeconds));

        // ",listen" makes us the server side of the reverse-connect handshake: FromDiagnosticPort
        // hosts the port and completes once the runtime connects back.
        connector = await DiagnosticsClientConnector.FromDiagnosticPort($"{diagnosticPort},listen", connectCts.Token);
        client = connector.Instance;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"No app connected to diagnostic port '{diagnosticPort}': {ex.Message}");
        Console.Error.WriteLine("Make sure the app is launched with the matching DOTNET_DiagnosticPorts value.");
        return 1;
    }

    Console.WriteLine("App connected.");

    // Warm-up wait: on MonoVM (Mac Catalyst, .NET 10) enabling the runtime's EventCounters during
    // the app's very first moments can interfere with the Obj-C registrar's startup initialization.
    // A freshly reverse-connected app has only just started, so give it a moment to finish starting
    // up before we enable counters below. (Harmless on CoreCLR / .NET 11 and on Windows.)
    if (options.WarmupSeconds > 0)
    {
        Console.WriteLine($"Letting the app finish initializing for {options.WarmupSeconds}s before enabling counters ...");
        await Task.Delay(TimeSpan.FromSeconds(options.WarmupSeconds));
    }
}
else
{
    int? targetPid = ProcessResolver.Resolve(options);
    if (targetPid is null)
    {
        return 1;
    }

    Console.WriteLine($"Attaching to PID {targetPid} over EventPipe ...");
    client = new DiagnosticsClient(targetPid.Value);
}

EventPipeSession session;
try
{
    // requestRundown:false keeps the stream to just the counter events we care about.
    session = client.StartEventPipeSession(providers, requestRundown: false);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to start an EventPipe session: {ex.Message}");
    Console.Error.WriteLine("Make sure the target is a running .NET process on this machine and try again.");
    if (connector is not null)
    {
        await connector.DisposeAsync();
    }

    return 1;
}

// In reverse-connect mode the runtime may have been started suspended (waiting for us). Now that
// the session is configured, let it run. This is a no-op when the app used ",nosuspend".
if (reverseConnect)
{
    try
    {
        client.ResumeRuntime();
    }
    catch
    {
        // Already running (nosuspend) — nothing to resume.
    }
}

using (session)
{
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        Console.WriteLine();
        Console.WriteLine("Stopping session ...");

        try
        {
            // Stopping the session ends EventPipeEventSource.Process() and unblocks us.
            session.Stop();
        }
        catch
        {
            // The session may already be closing; nothing useful to do here.
        }
    };

    using var source = new EventPipeEventSource(session.EventStream);

    source.Dynamic.All += traceEvent =>
    {
        // Counters first; if it isn't a counter, try to surface it as a discrete app event
        // (e.g. the UI-hang / starvation notifications) so the out-of-process view matches the
        // in-process one.
        string? line = CounterTablePrinter.Describe(traceEvent)
            ?? CounterTablePrinter.DescribeAppEvent(traceEvent, options.AppProviderName);
        if (line is not null)
        {
            Console.WriteLine(line);
        }
    };

    // Optional auto-stop after a fixed duration (handy for scripted/demo runs).
    Timer? durationTimer = null;
    if (options.DurationSeconds is int seconds)
    {
        durationTimer = new Timer(
            _ =>
            {
                try
                {
                    session.Stop();
                }
                catch
                {
                    // Session may already be closing.
                }
            },
            state: null,
            dueTime: TimeSpan.FromSeconds(seconds),
            period: Timeout.InfiniteTimeSpan);
    }

    string stopHint = options.DurationSeconds is int d
        ? $"for {d}s (or press Ctrl+C)"
        : "(press Ctrl+C to stop)";
    Console.WriteLine($"Streaming counters every {options.IntervalSeconds}s {stopHint}.");
    Console.WriteLine(new string('-', 78));

    try
    {
        source.Process();
    }
    catch (Exception ex) when (ex is EndOfStreamException or FormatException or ObjectDisposedException)
    {
        // The stream ended abruptly — the target process exited or was disconnected while we were
        // reading (e.g. you closed the app). That's an expected way for the session to end, not an
        // error, so finish cleanly instead of throwing.
        Console.WriteLine();
        Console.WriteLine("Target disconnected (the app exited or the session was closed).");
    }

    durationTimer?.Dispose();
}

if (connector is not null)
{
    await connector.DisposeAsync();
}

Console.WriteLine("Session ended.");
return 0;
