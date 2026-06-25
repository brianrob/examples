using System.Globalization;
using System.Linq;
using Microsoft.Diagnostics.Tracing;

namespace CounterListener;

/// <summary>
/// Decodes events from the EventPipe stream into readable lines.
///
/// <para>Two kinds of event arrive, and we surface both so the out-of-process view matches
/// what the in-process listener shows:</para>
/// <list type="bullet">
///   <item>
///     <b>Counters</b> — every EventCounter arrives as an event named "EventCounters".
///     TraceEvent surfaces its argument as a nested dictionary:
///     <c>PayloadValue(0) -&gt; { "Payload" : { "Name", "DisplayName", "Mean"/"Increment", ... } }</c>.
///     "Mean" counters (EventCounter / PollingCounter) report a "Mean" field; "Sum" counters
///     (IncrementingEventCounter) report an "Increment" field.
///   </item>
///   <item>
///     <b>Discrete app events</b> — regular <c>EventSource</c> events authored by the app
///     (e.g. "UI hang DETECTED", "thread-pool starvation DETECTED"). These are <i>not</i>
///     counters; we print them so a hang is visibly <b>logged</b> over EventPipe, not just
///     inferable from the <c>ui-thread-lag-ms</c> counter.
///   </item>
/// </list>
/// </summary>
internal static class CounterTablePrinter
{
    /// <summary>
    /// Formats a counter sample ("EventCounters" event). Returns <c>null</c> for anything
    /// that is not a counter — call <see cref="DescribeAppEvent"/> for those.
    /// </summary>
    public static string? Describe(TraceEvent traceEvent)
    {
        if (traceEvent.EventName != "EventCounters")
        {
            return null;
        }

        if (traceEvent.PayloadValue(0) is not IDictionary<string, object> outer)
        {
            return null;
        }

        if (!outer.TryGetValue("Payload", out object? innerObj) ||
            innerObj is not IDictionary<string, object> fields)
        {
            return null;
        }

        string name = GetString(fields, "Name");
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        string displayName = GetString(fields, "DisplayName");
        if (string.IsNullOrEmpty(displayName))
        {
            displayName = name;
        }

        string unit = GetString(fields, "DisplayUnits");
        string counterType = GetString(fields, "CounterType");
        double value = counterType == "Sum"
            ? GetDouble(fields, "Increment")
            : GetDouble(fields, "Mean");

        string unitSuffix = string.IsNullOrEmpty(unit) ? string.Empty : " " + unit;
        return string.Create(CultureInfo.InvariantCulture,
            $"{DateTime.Now:HH:mm:ss}  [{traceEvent.ProviderName}]  {displayName,-26} = {value,12:0.##}{unitSuffix}");
    }

    /// <summary>
    /// Formats a discrete (non-counter) event from the app's provider, so events like the
    /// UI-hang and thread-pool-starvation notifications show up out of process. Returns
    /// <c>null</c> for counters, the EventSource's internal bookkeeping events, and events
    /// from other providers.
    /// </summary>
    public static string? DescribeAppEvent(TraceEvent traceEvent, string appProviderName)
    {
        if (!string.Equals(traceEvent.ProviderName, appProviderName, StringComparison.Ordinal))
        {
            return null;
        }

        // Skip counters (handled by Describe) and the EventSource's own manifest/diagnostic
        // events, which aren't app events.
        if (traceEvent.EventName is "EventCounters" or "EventSourceMessage" or "ManifestData" ||
            string.IsNullOrEmpty(traceEvent.EventName))
        {
            return null;
        }

        // Prefer the event's filled-in message template; fall back to "EventName(name=value, ...)".
        string message = !string.IsNullOrEmpty(traceEvent.FormattedMessage)
            ? traceEvent.FormattedMessage
            : BuildFromPayload(traceEvent);

        return $"{DateTime.Now:HH:mm:ss}  [{traceEvent.ProviderName}]  {message}";
    }

    private static string BuildFromPayload(TraceEvent traceEvent)
    {
        string[] names = traceEvent.PayloadNames;
        if (names.Length == 0)
        {
            return traceEvent.EventName;
        }

        IEnumerable<string> pairs = names.Select((n, i) => $"{n}={traceEvent.PayloadString(i)}");
        return $"{traceEvent.EventName}({string.Join(", ", pairs)})";
    }

    private static string GetString(IDictionary<string, object> fields, string key) =>
        fields.TryGetValue(key, out object? value) && value is not null
            ? value.ToString() ?? string.Empty
            : string.Empty;

    private static double GetDouble(IDictionary<string, object> fields, string key) =>
        fields.TryGetValue(key, out object? value) && value is IConvertible convertible
            ? convertible.ToDouble(CultureInfo.InvariantCulture)
            : 0d;
}
