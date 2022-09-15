using System.Diagnostics.Tracing;
using System.Threading;
using System.Threading.Tasks;

namespace rolling_file_with_eventsource
{
    [EventSource(Name = "EventProducer")]
    internal class EventProducer : EventSource
    {
        internal static EventProducer Log = new EventProducer();

        [Event(1)]
        public void TestEvent()
        {
            WriteEvent(1);
        }

        internal static void LogEvents()
        {
            Task.Factory.StartNew(() =>
            {
                while(true)
                {
                    Log.TestEvent();
                    Thread.Sleep(100);
                }
            },
            TaskCreationOptions.LongRunning);
        }
    }
}
