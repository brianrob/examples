using System.Diagnostics.Tracing;
using System.Globalization;

namespace ExampleApp.Diagnostics;

/// <summary>
/// A single decoded EventCounter sample, plus the formatting shared by the in-app
/// events pane.
/// </summary>
public sealed record CounterLine(
    string Provider,
    string Name,
    string DisplayName,
    double Value,
    string Unit)
{
    /// <summary>
    /// Builds a <see cref="CounterLine"/> from the dictionary payload of an
    /// "EventCounters" event. Returns <c>null</c> if the payload is not a counter.
    /// </summary>
    public static CounterLine? FromPayload(string provider, IDictionary<string, object> payload)
    {
        string name = GetString(payload, "Name");
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        string displayName = GetString(payload, "DisplayName");
        if (string.IsNullOrEmpty(displayName))
        {
            displayName = name;
        }

        string unit = GetString(payload, "DisplayUnits");

        // "Sum" counters (IncrementingEventCounter) report "Increment"; "Mean" counters
        // (EventCounter / PollingCounter) report "Mean".
        string counterType = GetString(payload, "CounterType");
        double value = counterType == "Sum"
            ? GetDouble(payload, "Increment")
            : GetDouble(payload, "Mean");

        return new CounterLine(provider, name, displayName, value, unit);
    }

    /// <summary>Formats the line for display, e.g. "12:00:01  [System.Runtime]  GC Heap Size = 12.3 MB".</summary>
    public string Format()
    {
        string unitSuffix = string.IsNullOrEmpty(Unit) ? string.Empty : " " + Unit;
        return string.Create(CultureInfo.InvariantCulture,
            $"{DateTime.Now:HH:mm:ss}  [{Provider}]  {DisplayName} = {Value:0.##}{unitSuffix}");
    }

    /// <summary>
    /// Formats one row of the latest-snapshot table (no timestamp — the table as a whole is
    /// stamped once per refresh), aligned for readability, e.g. "App      Frame Rate = 60 fps".
    /// </summary>
    public string FormatSnapshot()
    {
        string tag = Provider == AppEventSource.ProviderName ? "App" : "Runtime";
        string unitSuffix = string.IsNullOrEmpty(Unit) ? string.Empty : " " + Unit;
        return string.Create(CultureInfo.InvariantCulture,
            $"{tag,-8}{DisplayName,-34}{Value,12:0.##}{unitSuffix}");
    }

    private static string GetString(IDictionary<string, object> payload, string key) =>
        payload.TryGetValue(key, out object? value) && value is not null
            ? value.ToString() ?? string.Empty
            : string.Empty;

    private static double GetDouble(IDictionary<string, object> payload, string key) =>
        payload.TryGetValue(key, out object? value) && value is IConvertible convertible
            ? convertible.ToDouble(CultureInfo.InvariantCulture)
            : 0d;
}

/// <summary>
/// A single decoded NON-counter event (a regular <see cref="EventSource"/> event, e.g.
/// "starvation detected"). Carried separately from <see cref="CounterLine"/> so the UI can
/// show counters and discrete events in different panes.
/// </summary>
public sealed record RawEventLine(string Provider, string EventName, string Message)
{
    /// <summary>Formats the line for the raw-events log, e.g. "12:00:01  [Example-App-Diagnostics]  ...".</summary>
    public string Format() =>
        $"{DateTime.Now:HH:mm:ss}  [{Provider}]  {Message}";

    /// <summary>
    /// Builds a <see cref="RawEventLine"/> from an event. Prefers the event's formatted
    /// <c>Message</c> template; falls back to "EventName(name=value, ...)" when there is none.
    /// </summary>
    public static RawEventLine FromEvent(EventWrittenEventArgs e)
    {
        string provider = e.EventSource.Name;
        string eventName = e.EventName ?? "(event)";
        string message = BuildMessage(e, eventName);
        return new RawEventLine(provider, eventName, message);
    }

    private static string BuildMessage(EventWrittenEventArgs e, string eventName)
    {
        object?[] payload = e.Payload is { Count: > 0 } p ? p.ToArray() : Array.Empty<object?>();

        if (!string.IsNullOrEmpty(e.Message))
        {
            try
            {
                return string.Format(CultureInfo.InvariantCulture, e.Message, payload);
            }
            catch (FormatException)
            {
                // Fall through to the name=value form below.
            }
        }

        if (payload.Length == 0 || e.PayloadNames is not { Count: > 0 } names)
        {
            return eventName;
        }

        IEnumerable<string> pairs = names
            .Zip(payload, (name, value) => $"{name}={value}");
        return $"{eventName}({string.Join(", ", pairs)})";
    }
}

/// <summary>
/// IN-PROCESS consumer. This <see cref="EventListener"/> lives inside the app and
/// receives counter samples directly, with no IPC and no external tooling. It is the
/// counterpart to the out-of-process <c>CounterListener</c> console app, which receives
/// the very same counters over the EventPipe channel.
///
/// It enables two providers:
///  - <c>Example-App-Diagnostics</c> — this app's custom frame-rate / queue-delay counters.
///  - <c>System.Runtime</c> — the runtime's built-in GC and thread-pool counters (no app code).
///
/// Each decoded sample is raised via <see cref="CounterReceived"/>; the UI subscribes
/// and appends it to a scrolling pane.
///
/// <para><b>Ordering gotcha (important):</b> the base <see cref="EventListener"/> constructor
/// calls <see cref="OnEventSourceCreated"/> for every already-existing provider <i>before</i>
/// this class's own constructor body (and field assignments in it) have run. If we called
/// <c>EnableEvents</c> straight from <see cref="OnEventSourceCreated"/> using a constructor
/// argument, that argument would still be its default value (e.g. an interval of <c>0</c>,
/// which disables counter polling entirely). To avoid that, we record the providers we see
/// and only enable them from <see cref="Start"/>, once the desired interval is known.</para>
/// </summary>
public sealed class InProcessCounterListener : EventListener
{
    public const string AppProviderName = AppEventSource.ProviderName;
    public const string RuntimeProviderName = "System.Runtime";

    private readonly object _gate = new();
    private readonly List<EventSource> _pendingSources = new();
    private int _intervalSeconds = 1;
    private bool _started;

    /// <summary>Raised on a background thread for every counter sample received.</summary>
    public event Action<CounterLine>? CounterReceived;

    /// <summary>Raised on a background thread for every non-counter (regular) event received.</summary>
    public event Action<RawEventLine>? RawEventReceived;

    /// <summary>
    /// Enables counter polling on the providers at the requested interval. Call once after
    /// construction (see the ordering note on the class). Providers discovered before this
    /// call were queued; providers discovered afterwards are enabled immediately.
    /// </summary>
    public void Start(int intervalSeconds = 1)
    {
        lock (_gate)
        {
            _intervalSeconds = intervalSeconds;
            _started = true;

            foreach (EventSource source in _pendingSources)
            {
                EnableCounters(source);
            }

            _pendingSources.Clear();
        }
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name is not (AppProviderName or RuntimeProviderName))
        {
            return;
        }

        lock (_gate)
        {
            if (_started)
            {
                EnableCounters(eventSource);
            }
            else
            {
                // Defer: see the ordering gotcha on the class. The interval is not known yet.
                _pendingSources.Add(eventSource);
            }
        }
    }

    private void EnableCounters(EventSource eventSource)
    {
        // The "EventCounterIntervalSec" argument is what turns a provider's counters into
        // periodic samples. Without it (or with 0), counters stay silent.
        EnableEvents(
            eventSource,
            EventLevel.Informational,
            EventKeywords.All,
            new Dictionary<string, string?>
            {
                ["EventCounterIntervalSec"] = _intervalSeconds.ToString(CultureInfo.InvariantCulture),
            });
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        // Counter samples arrive as the synthetic "EventCounters" meta-event; everything
        // else is a regular event. Route the two to different consumers.
        if (eventData.EventName == "EventCounters")
        {
            if (eventData.Payload is { Count: > 0 } &&
                eventData.Payload[0] is IDictionary<string, object> payload)
            {
                CounterLine? line = CounterLine.FromPayload(eventData.EventSource.Name, payload);
                if (line is not null)
                {
                    CounterReceived?.Invoke(line);
                }
            }

            return;
        }

        // Ignore the EventSource self-diagnostic message event; it is not an app event.
        if (eventData.EventName == "EventSourceMessage")
        {
            return;
        }

        // This sample stringifies every event generically (see RawEventLine) because the UI
        // just shows a scrolling log. But note that EventWrittenEventArgs payloads are
        // STRONGLY TYPED: eventData.Payload[i] holds the exact CLR type that was written, in
        // the same order as the event method's parameters (and eventData.PayloadNames[i] is
        // the parameter name). So when you are handling a KNOWN event you do NOT have to parse
        // text — you can read the value directly. For example, to react to a UI hang:
        //
        //     if (eventData.EventId == 6 /* UiHangDetected */)
        //     {
        //         double lagMs = (double)eventData.Payload![0]!;   // no string parsing
        //         // ...use lagMs directly (compare, threshold, aggregate, etc.)
        //     }
        //
        // Matching on EventId (or EventName) plus a cast is the fast, robust path for events
        // you own; the generic name=value formatting below is only for the "show me anything"
        // case where the consumer does not know the event ahead of time.
        RawEventReceived?.Invoke(RawEventLine.FromEvent(eventData));
    }
}
