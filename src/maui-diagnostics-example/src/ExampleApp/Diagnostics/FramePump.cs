namespace ExampleApp.Diagnostics;

/// <summary>
/// Cross-platform per-frame driver. Invokes a callback once per <em>display</em> frame,
/// aligned to the platform compositor / vsync.
///
/// <para>
/// The first version of this demo pumped frames from a fixed-interval
/// <see cref="IDispatcherTimer"/> set to 16&#160;ms ("~60&#160;Hz"). On WinUI 3 that timer
/// (a <c>DispatcherQueueTimer</c>) does <b>not</b> deliver ticks on a steady 16&#160;ms
/// cadence — the OS coalesces and defers them — and each tick's <c>Invalidate()</c> only
/// repaints on the <i>next</i> composition pass. The result was bursty painting and a
/// frame-rate readout that swung wildly (e.g. 3&#8211;40&#160;fps) even with the app idle.
/// </para>
///
/// <para>
/// Driving repaints from the platform's own frame signal fixes that: the callback fires
/// exactly once per composed frame, so requesting a repaint each time yields a smooth,
/// display-locked rate.
/// </para>
///
/// <list type="bullet">
///   <item><b>Windows (WinUI 3):</b> <c>Microsoft.UI.Xaml.Media.CompositionTarget.Rendering</c>.</item>
///   <item><b>Mac Catalyst:</b> <c>CADisplayLink</c> on the main run loop.</item>
///   <item><b>Any other platform head:</b> unsupported — <see cref="Start"/> throws
///   <see cref="PlatformNotSupportedException"/> rather than falling back to a fixed-interval
///   timer (which would reintroduce the bursty, non-vsync behaviour described above).</item>
/// </list>
///
/// <para>
/// Both signals fire on the UI thread, so blocking the UI thread (the "Freeze UI" demo)
/// stops the callbacks and the measured frame rate collapses toward 0 — exactly the
/// behaviour this sample is meant to surface.
/// </para>
/// </summary>
public sealed class FramePump
{
    private Action? _onFrame;

#if WINDOWS
    private EventHandler<object>? _renderingHandler;
#elif MACCATALYST || IOS
    private CoreAnimation.CADisplayLink? _displayLink;
#endif

    /// <summary>
    /// Begins invoking <paramref name="onFrame"/> once per display frame on the UI thread.
    /// Call from the UI thread (e.g. <c>OnAppearing</c>).
    /// </summary>
    public void Start(Action onFrame)
    {
        _onFrame = onFrame;

#if WINDOWS
        _renderingHandler = (_, _) => _onFrame?.Invoke();
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += _renderingHandler;
#elif MACCATALYST || IOS
        _displayLink = CoreAnimation.CADisplayLink.Create(() => _onFrame?.Invoke());
        _displayLink.AddToRunLoop(Foundation.NSRunLoop.Main, Foundation.NSRunLoopMode.Common);
#else
        // This sample only targets Windows (WinUI 3) and Mac Catalyst. A fixed-interval
        // dispatcher-timer fallback was deliberately removed: it does NOT track real vsync,
        // which is the whole point of this pump, so it would silently produce the bursty,
        // misleading frame rate this class exists to avoid. Fail loudly instead.
        throw new PlatformNotSupportedException(
            "FramePump requires a vsync source (Windows CompositionTarget.Rendering or " +
            "Mac Catalyst CADisplayLink). This platform head is not supported.");
#endif
    }

    /// <summary>Stops the per-frame callbacks. Call from the UI thread (e.g. <c>OnDisappearing</c>).</summary>
    public void Stop()
    {
#if WINDOWS
        if (_renderingHandler is not null)
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= _renderingHandler;
            _renderingHandler = null;
        }
#elif MACCATALYST || IOS
        if (_displayLink is not null)
        {
            _displayLink.Invalidate();
            _displayLink.Dispose();
            _displayLink = null;
        }
#endif

        _onFrame = null;
    }
}
