using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Session;

namespace realtime_session_with_stacks
{
    class Program
    {
        public static void Main(string[] args)
        {
            // Start logging events.
            EventProducer.LogEvents();

            // Create a new session.
            using TraceEventSession session = new TraceEventSession("Live-Stacks-Session");

            // Register for CTRL+C.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs cancelArgs)
            {
                session.Dispose();
                Environment.Exit(0);
            };

            // Filter all CLR events to just the current process.
            // This is optional, but for the purposes of this example, we only want to monitor the current process.
            TraceEventProviderOptions filterOptions = new TraceEventProviderOptions()
            {
                ProcessIDFilter = new List<int>()
                {
                    Process.GetCurrentProcess().Id
                },
            };

            // Create filter options for the events with stacks that we're going to consume.
            TraceEventProviderOptions filterWithStacksOptions = filterOptions.Clone();
            filterWithStacksOptions.StacksEnabled = true;

            // Enable process and image load events.
            // Process events are used to resolve the process name.
            // ImageLoad events are used to map IPs to a loaded module.
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.ImageLoad);

            // Enable CLR rundown to capture module load and jitted method information for
            // module loads and method jit operations that occurred prior to tracing start.
            session.EnableProvider(
                ClrRundownTraceEventParser.ProviderGuid,
                TraceEventLevel.Verbose,
                (ulong)(ClrRundownTraceEventParser.Keywords.Jit |
                ClrRundownTraceEventParser.Keywords.Loader |
                ClrRundownTraceEventParser.Keywords.StartEnumeration),
                options: filterOptions);

            // Enable CLR events so that we are notified of future module loads and method jit operations.
            session.EnableProvider(
                ClrTraceEventParser.ProviderGuid,
                TraceEventLevel.Verbose,
                (ulong)(ClrTraceEventParser.Keywords.Jit |
                ClrTraceEventParser.Keywords.Loader),
                options: filterOptions);

            // Turn on the event producer.
            session.EnableProvider(
                EventSource.GetGuid(typeof(EventProducer)),
                options: filterWithStacksOptions);

            // Create a TraceLogEventSource to consume events as they are dispatched in real-time.
            TraceLogEventSource eventSource = TraceLog.CreateFromTraceEventSession(session);

            // Disable CLR rundown once it completes.
            // NOTE: This provider is filtered to just enable events for the current process.
            // If processing events from all processes, this code becomes more complex because you have to decide when
            // you've seen rundown complete for all the proceseses that you care about.
            // One option is to just leave it on, but that means that new processes will rundown, which is duplicate work.
            // If you know exactly which processes you care about, you can use the list to determine when to disable rundown.
            ClrRundownTraceEventParser rundownParser = new ClrRundownTraceEventParser(eventSource);
            rundownParser.MethodDCStartComplete += delegate (ClrRundownTraceEventParser.DCStartEndTraceData data)
            {
                session.DisableProvider(ClrRundownTraceEventParser.ProviderGuid);
            };

            // Register for events from the event producer.
            eventSource.Dynamic.AddCallbackForProviderEvent(
                EventProducer.Log.Name,
                "TestEvent",
                (data) =>
                {
                    Console.WriteLine();
                    Console.WriteLine($"PID: {data.ProcessID} ProcessName: {data.ProcessName}");
                    TraceCallStack callStack = data.CallStack();
                    if (callStack != null)
                    {
                        PrintCallStack(callStack);
                    }
                    else
                    {
                        Console.WriteLine("No Stack.");
                    }
                });

            // Start processing events.
            eventSource.Process();
        }

        private static void PrintCallStack(TraceCallStack callStack)
        {
            TraceCallStack current = callStack;
            while (current != null)
            {
                // Asynchronously resolve symbols for the module if they've not been resolved before.
                // You can do this synchronously by just removing the call to ResolveSymbolsForModule from the Task and calling synchronously.
                if(string.IsNullOrEmpty(current.CodeAddress.FullMethodName) && !ResolvedSymbolsForModule(current.CodeAddress.ModuleFile))
                {
                    Task.Factory.StartNew(() =>
                    {
                        ResolveSymbolsForModule(current.CodeAddress.CodeAddresses, current.CodeAddress.ModuleFile);
                    });
                }

                Console.WriteLine($"[0x{current.CodeAddress.Address:X}] {current.CodeAddress.ModuleName}!{current.CodeAddress.FullMethodName}");
                current = current.Caller;
            }
        }

        private static HashSet<string> ResolvedModules = new HashSet<string>();
        private static SymbolReader SymbolReader = new SymbolReader(System.IO.TextWriter.Null);

        private static bool ResolvedSymbolsForModule(TraceModuleFile moduleFile)
        {
            if (moduleFile == null)
            {
                // Treat null modules as already resolved, since there's nothing that we can do to resolve them.
                return true;
            }

            bool resolvedSymbols = true;
            if(!ResolvedModules.Contains(moduleFile.PdbLookupValue()))
            {
                lock (ResolvedModules)
                {
                    if (!ResolvedModules.Contains(moduleFile.PdbLookupValue()))
                    {
                        resolvedSymbols = false;
                    }
                }
            }

            return resolvedSymbols;
        }

        private static void ResolveSymbolsForModule(TraceCodeAddresses codeAddresses, TraceModuleFile moduleFile)
        {
            // Treat null modules as already resolved, since there's nothing that we can do to resolve them.
            if (moduleFile == null)
            {
                return;
            }

            bool resolveSymbols = false;
            if (!ResolvedModules.Contains(moduleFile.PdbLookupValue()))
            {
                lock (ResolvedModules)
                {
                    if(!ResolvedModules.Contains(moduleFile.PdbLookupValue()))
                    {
                        ResolvedModules.Add(moduleFile.PdbLookupValue());
                        resolveSymbols = true;
                    }
                }
            }

            if(resolveSymbols)
            {
                codeAddresses.LookupSymbolsForModule(SymbolReader, moduleFile);
            }
        }
    }

    internal static class TraceModuleFileExtensions
    {
        internal static string PdbLookupValue(this TraceModuleFile moduleFile)
        {
            return moduleFile.Name + moduleFile.PdbAge + moduleFile.PdbSignature;
        }
    }
}
