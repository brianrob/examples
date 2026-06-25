# Collection of Code Samples

| Sample | Description |
|--------|-------------|
| [maui-diagnostics-example](src/maui-diagnostics-example) | A .NET MAUI app that emits custom EventCounters (frame rate, thread-pool queue delay) and consumes them both in-process (EventListener) and out-of-process over the EventPipe IPC channel with TraceEvent. Targets .NET 11 on Windows and Mac Catalyst. |
| [maui-inprocess-benchmark](src/maui-inprocess-benchmark) | Micro-benchmark a live .NET MAUI UI with BenchmarkDotNet in-process (no cross-process automation). Targets .NET 10 on Windows and Mac Catalyst. |
| [os-signposter-eventlistener](src/os-signposter-eventlistener) | Forward EventSource events as macOS signposts via a .NET startup hook so they show up in Instruments. |
| [realtime-session-with-stacks](src/realtime-session-with-stacks) | Consume a real-time ETW session with call stacks using TraceEvent. |
| [rolling-file-with-eventsource](src/rolling-file-with-eventsource) | Capture EventSource events to a rolling set of ETL files using TraceEvent. |
