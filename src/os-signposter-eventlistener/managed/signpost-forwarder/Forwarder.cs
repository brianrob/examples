namespace signpost_forwarder;

using System;
using System.Diagnostics.Tracing;
using System.Text;

public sealed class Forwarder : EventListener
{
    private IntPtr _logHandle;
    private ulong _signposterID;

    [ThreadStatic]
    private static StringBuilder _stringBuilder;

    public Forwarder()
    {
        _logHandle = Signposter.CreateLogHandle("com.brianrob.forwarder");
        _signposterID = Signposter.GenerateSignpostID(_logHandle);
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        // Break recursion caused by consuming the ArrayPool EventSource.
        if (!eventSource.Name.Contains("ArrayPool"))
        {
            EnableEvents(eventSource, EventLevel.Verbose);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        string fullPayload = $"Source=\"{eventData.EventSource.Name}\" Event=\"{eventData.EventName}\" {ToPayloadString(eventData)}";
        Signposter.EmitSignpostEvent(_logHandle, _signposterID, fullPayload);
    }

    private static string ToPayloadString(EventWrittenEventArgs eventData)
    {
        StringBuilder builder = GetBuilder();
        for(int i=0; i<eventData.PayloadNames.Count; i++)
        {
            builder.Append($"{eventData.PayloadNames[i]}=\"{eventData.Payload[i].ToString()}\"");
            if(i < eventData.PayloadNames.Count - 1)
            {
                builder.Append(" ");
            }
        }

        return builder.ToString();
    }

    private static StringBuilder GetBuilder()
    {
        if (_stringBuilder == null)
        {
            _stringBuilder = new StringBuilder();
        }

        _stringBuilder.Clear();
        return _stringBuilder;
    }
}
