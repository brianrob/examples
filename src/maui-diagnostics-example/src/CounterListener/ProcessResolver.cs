using System.Diagnostics;
using Microsoft.Diagnostics.NETCore.Client;

namespace CounterListener;

/// <summary>
/// Resolves the target process to attach to.
///
/// <see cref="DiagnosticsClient.GetPublishedProcesses"/> returns the PIDs of every .NET
/// process on the machine that has opened a diagnostics IPC channel — i.e. every process
/// we can attach an EventPipe session to. We either use an explicit <c>--pid</c> or match
/// one of those processes by name.
/// </summary>
internal static class ProcessResolver
{
    public static int? Resolve(CommandLineOptions options)
    {
        if (options.Pid is int explicitPid)
        {
            return explicitPid;
        }

        string nameFilter = options.Name ?? "ExampleApp";

        var matches = new List<(int Pid, string Name)>();
        foreach (int candidate in DiagnosticsClient.GetPublishedProcesses())
        {
            string name;
            try
            {
                name = Process.GetProcessById(candidate).ProcessName;
            }
            catch
            {
                // Process exited between enumeration and lookup; skip it.
                continue;
            }

            if (name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add((candidate, name));
            }
        }

        if (matches.Count == 1)
        {
            return matches[0].Pid;
        }

        if (matches.Count == 0)
        {
            Console.Error.WriteLine($"No diagnosable .NET process matching '{nameFilter}' was found.");
            PrintPublishedProcesses();
            return null;
        }

        Console.Error.WriteLine($"Multiple processes match '{nameFilter}'. Pick one with --pid:");
        foreach ((int pid, string name) in matches)
        {
            Console.Error.WriteLine($"  {pid}  {name}");
        }

        return null;
    }

    private static void PrintPublishedProcesses()
    {
        Console.Error.WriteLine("Diagnosable .NET processes currently running:");
        foreach (int candidate in DiagnosticsClient.GetPublishedProcesses())
        {
            try
            {
                Console.Error.WriteLine($"  {candidate}  {Process.GetProcessById(candidate).ProcessName}");
            }
            catch
            {
                // Ignore processes that vanished.
            }
        }
    }
}
