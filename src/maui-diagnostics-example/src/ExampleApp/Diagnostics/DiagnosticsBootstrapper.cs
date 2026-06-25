namespace ExampleApp.Diagnostics;

/// <summary>
/// One place that turns the app's diagnostics on at startup, so the wiring is easy to
/// follow. Called once from <see cref="App"/>.
/// </summary>
public static class DiagnosticsBootstrapper
{
    private static InProcessCounterListener? s_inProcessListener;
    private static bool s_started;

    /// <summary>The in-process listener, available to the UI once <see cref="Start"/> has run.</summary>
    public static InProcessCounterListener? InProcessListener => s_inProcessListener;

    public static void Start()
    {
        if (s_started)
        {
            return;
        }

        s_started = true;

        // 1. Touch the singleton so the EventSource actually exists. Until an EventSource
        //    instance is created, no listener can find it.
        _ = AppEventSource.Log;

        // 2. Start the background probe that feeds the thread-pool queue-delay counter.
        ThreadPoolQueueDelayCounter.Instance.Start();

        // 3. Start the IN-PROCESS listener. We construct it first, then call Start() to
        //    enable the providers. Enabling must happen *after* construction — see the
        //    ordering note on InProcessCounterListener.
        var listener = new InProcessCounterListener();
        listener.Start(intervalSeconds: 1);
        s_inProcessListener = listener;
    }
}
