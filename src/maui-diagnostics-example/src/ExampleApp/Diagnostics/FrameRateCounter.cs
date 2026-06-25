using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace ExampleApp.Diagnostics;

/// <summary>
/// Frame-rate counter — implemented entirely by THIS application.
///
/// The .NET runtime has no "frames per second" counter (it has no idea the process
/// is rendering a UI), so we measure it ourselves. <see cref="FrameCanvasDrawable"/>
/// runs a continuous redraw loop on the UI thread and calls <see cref="OnFrameRendered"/>
/// once per painted frame. We expose the value as a <see cref="PollingCounter"/>.
///
/// The FPS is computed inside the poll callback as
/// <c>(frames since last poll) / (seconds since last poll)</c>, so the reported value
/// is the real frame rate regardless of how often the listener polls (its
/// <c>EventCounterIntervalSec</c>).
///
/// Because painting happens on the UI thread, anything that blocks the UI thread (see
/// the "Freeze UI" button) stops the redraw loop and the frame rate drops toward zero —
/// which is exactly the symptom we want to surface.
/// </summary>
public sealed class FrameRateCounter
{
    public static readonly FrameRateCounter Instance = new();

    private PollingCounter? _counter;
    private long _frameCount;
    private long _lastSampleFrameCount;
    private long _lastSampleTimestamp = Stopwatch.GetTimestamp();

    // Independent sampling state for the in-app UI label, so the on-screen frame rate
    // updates even when no counter listener is attached and polling the PollingCounter.
    private long _displaySampleFrameCount;
    private long _displaySampleTimestamp = Stopwatch.GetTimestamp();

    private FrameRateCounter()
    {
    }

    /// <summary>
    /// Most recent computed frame rate. Updated each time the counter is polled, so
    /// the in-app UI can show the same number a remote listener sees.
    /// </summary>
    public double CurrentFramesPerSecond { get; private set; }

    /// <summary>Call once per rendered frame (from the drawable's Draw loop).</summary>
    public void OnFrameRendered() => Interlocked.Increment(ref _frameCount);

    /// <summary>
    /// Computes the frame rate for the in-app display from its own delta state. This does
    /// not depend on any listener polling the <see cref="PollingCounter"/>, so the on-screen
    /// value is live as soon as frames start — even with nothing attached. Call from the UI
    /// refresh timer.
    /// </summary>
    public double SampleDisplayFramesPerSecond()
    {
        long now = Stopwatch.GetTimestamp();
        long frames = Interlocked.Read(ref _frameCount);

        long deltaFrames = frames - _displaySampleFrameCount;
        double deltaSeconds = (now - _displaySampleTimestamp) / (double)Stopwatch.Frequency;

        _displaySampleFrameCount = frames;
        _displaySampleTimestamp = now;

        return deltaSeconds > 0 ? deltaFrames / deltaSeconds : 0;
    }

    /// <summary>
    /// Creates the <see cref="PollingCounter"/> the first time the provider is enabled.
    /// Safe to call repeatedly.
    /// </summary>
    internal void EnsureCounterCreated(EventSource source)
    {
        _counter ??= new PollingCounter("frame-rate", source, SampleFramesPerSecond)
        {
            DisplayName = "Frame Rate",
            DisplayUnits = "fps",
        };
    }

    private double SampleFramesPerSecond()
    {
        long now = Stopwatch.GetTimestamp();
        long frames = Interlocked.Read(ref _frameCount);

        long deltaFrames = frames - _lastSampleFrameCount;
        double deltaSeconds = (now - _lastSampleTimestamp) / (double)Stopwatch.Frequency;

        _lastSampleFrameCount = frames;
        _lastSampleTimestamp = now;

        double fps = deltaSeconds > 0 ? deltaFrames / deltaSeconds : 0;
        CurrentFramesPerSecond = fps;
        return fps;
    }
}
