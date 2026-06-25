namespace CounterListener;

/// <summary>
/// Parsed command-line options for the listener.
/// </summary>
internal sealed class CommandLineOptions
{
    public int? Pid { get; private set; }

    public string? Name { get; private set; }

    public int IntervalSeconds { get; private set; } = 1;

    public int? DurationSeconds { get; private set; }

    public string AppProviderName { get; private set; } = "Example-App-Diagnostics";

    /// <summary>
    /// Reverse-connect diagnostic port (a socket path on macOS/Linux, or a named-pipe name on
    /// Windows). When set, the listener HOSTS this port and waits for the target app to connect
    /// to it — the app must be launched with <c>DOTNET_DiagnosticPorts=&lt;this value&gt;,nosuspend</c>.
    /// This is the attach model required for sandboxed Mac Catalyst apps (and works on Windows
    /// too). When null, the listener attaches by PID/name instead.
    /// </summary>
    public string? DiagnosticPort { get; private set; }

    /// <summary>How long to wait for the app to connect in reverse-connect mode.</summary>
    public int ConnectTimeoutSeconds { get; private set; } = 60;

    /// <summary>
    /// Reverse-connect only: seconds to wait, AFTER the app connects, before enabling counters.
    /// On MonoVM (Mac Catalyst, .NET 10) enabling the runtime's EventCounters during the app's very
    /// first moments can interfere with the Obj-C registrar's startup initialization. Letting the
    /// app finish starting up first avoids that — even a couple of seconds is enough. Harmless on
    /// CoreCLR (.NET 11+) and Windows. Set to 0 to disable.
    /// </summary>
    public int WarmupSeconds { get; private set; } = 3;

    /// <summary>Returns parsed options, or <c>null</c> if usage should be printed.</summary>
    public static CommandLineOptions? Parse(string[] args)
    {
        var options = new CommandLineOptions();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--warmup-seconds":
                    if (++i >= args.Length || !int.TryParse(args[i], out int warmup) || warmup < 0)
                    {
                        return null;
                    }

                    options.WarmupSeconds = warmup;
                    break;

                case "--pid" or "-p":
                    if (++i >= args.Length || !int.TryParse(args[i], out int pid))
                    {
                        return null;
                    }

                    options.Pid = pid;
                    break;

                case "--diagnostic-port" or "--dport":
                    if (++i >= args.Length)
                    {
                        return null;
                    }

                    options.DiagnosticPort = args[i];
                    break;

                case "--connect-timeout":
                    if (++i >= args.Length || !int.TryParse(args[i], out int connectTimeout) || connectTimeout <= 0)
                    {
                        return null;
                    }

                    options.ConnectTimeoutSeconds = connectTimeout;
                    break;

                case "--name" or "-n":
                    if (++i >= args.Length)
                    {
                        return null;
                    }

                    options.Name = args[i];
                    break;

                case "--interval" or "-i":
                    if (++i >= args.Length || !int.TryParse(args[i], out int interval) || interval <= 0)
                    {
                        return null;
                    }

                    options.IntervalSeconds = interval;
                    break;

                case "--duration" or "-d":
                    if (++i >= args.Length || !int.TryParse(args[i], out int duration) || duration <= 0)
                    {
                        return null;
                    }

                    options.DurationSeconds = duration;
                    break;

                case "--provider":
                    if (++i >= args.Length)
                    {
                        return null;
                    }

                    options.AppProviderName = args[i];
                    break;

                case "--help" or "-h" or "-?":
                    return null;

                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return null;
            }
        }

        return options;
    }

    public static void PrintUsage()
    {
        Console.WriteLine(
            """
            CounterListener — out-of-process EventCounter viewer (EventPipe + TraceEvent).

            Connects to a running .NET process over the EventPipe diagnostics channel and
            streams the System.Runtime counters plus this sample's custom app counters.

            Two attach modes:
              * PID/name (default)   — attaches to an already-running, diagnosable process.
                                       Works on Windows and on desktop .NET; does NOT work for
                                       sandboxed Mac Catalyst apps (their socket path exceeds the
                                       108-char UNIX limit).
              * Reverse-connect      — the listener HOSTS a diagnostic port and the app connects
                (--diagnostic-port)    out to it. Required for Mac Catalyst; also works on Windows.
                                       Launch the app with DOTNET_DiagnosticPorts=<port>,nosuspend.

            Usage:
              CounterListener [--pid <id> | --name <substring> | --diagnostic-port <path>]
                              [--interval <seconds>] [--duration <seconds>] [--provider <name>]
                              [--connect-timeout <seconds>]

            Options:
              -p, --pid <id>             PID of the target process. If omitted (and no
                                         --diagnostic-port), the listener looks up a diagnosable
                                         process by name.
              -n, --name <substring>     Match the target by process name (default: "ExampleApp").
                  --diagnostic-port <p>  Reverse-connect: host this socket path (macOS/Linux) or
                  --dport <p>            named-pipe name (Windows) and wait for the app to connect.
                                         Launch the app with DOTNET_DiagnosticPorts=<p>,nosuspend.
                  --connect-timeout <s>  Seconds to wait for the app to connect in reverse-connect
                                         mode (default: 60).
                  --warmup-seconds <s>   Reverse-connect: seconds to wait after the app connects
                                         before enabling counters, so a freshly-launched app can
                                         finish initializing first (default: 3; needed on
                                         .NET 10/Mono Mac Catalyst, harmless elsewhere; 0 disables).
              -i, --interval <sec>       Counter sampling interval in seconds (default: 1).
              -d, --duration <sec>       Stop automatically after this many seconds
                                         (default: run until Ctrl+C).
                  --provider <name>      Custom app provider name
                                         (default: "Example-App-Diagnostics").
              -h, --help                 Show this help.

            Examples:
              CounterListener --pid 12345
              CounterListener --name ExampleApp --interval 2
              CounterListener --diagnostic-port $TMPDIR/exampleapp.sock --duration 10
            """);
    }
}
