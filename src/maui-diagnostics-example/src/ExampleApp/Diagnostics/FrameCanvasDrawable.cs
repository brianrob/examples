using System.Diagnostics;
using Microsoft.Maui.Graphics;

namespace ExampleApp.Diagnostics;

/// <summary>
/// Draws the animated canvas and counts each painted frame.
///
/// This <see cref="IDrawable"/> is attached to a <c>GraphicsView</c> on the main page.
/// A ~60 Hz frame pump (a dispatcher timer in <c>MainPage</c>) invalidates the view, and
/// every resulting paint calls <see cref="FrameRateCounter.OnFrameRendered"/>. Driving the
/// pump from a dispatcher timer keeps the loop alive identically on Windows (WinUI 3) and
/// Mac Catalyst.
///
/// Because the pump and the painting both run on the UI thread, anything that blocks the
/// UI thread (the "Freeze UI" button) stops the frames cold and the measured rate drops
/// toward zero.
/// </summary>
public sealed class FrameCanvasDrawable : IDrawable
{
    /// <summary>
    /// When &gt; 0, each frame burns this many milliseconds of busy work to simulate an
    /// expensive frame. This lowers (but does not stop) the frame rate, unlike the
    /// "Freeze UI" button which blocks the UI thread entirely.
    /// </summary>
    public int ExtraRenderLoadMilliseconds { get; set; }

    private float _phase;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        FrameRateCounter.Instance.OnFrameRendered();

        canvas.FillColor = Color.FromRgb(245, 245, 250);
        canvas.FillRectangle(dirtyRect);

        _phase += 0.06f;
        const float margin = 16f;
        float travel = Math.Max(0f, dirtyRect.Width - margin * 2f);
        float x = dirtyRect.Left + margin + (float)((Math.Sin(_phase) * 0.5 + 0.5) * travel);
        float y = dirtyRect.Center.Y;

        canvas.FillColor = Color.FromRgb(81, 43, 212); // #512BD4
        canvas.FillCircle(x, y, 14f);

        if (ExtraRenderLoadMilliseconds > 0)
        {
            BusyWait(ExtraRenderLoadMilliseconds);
        }
    }

    private static void BusyWait(int milliseconds)
    {
        // Note: System.Diagnostics.Stopwatch is NOT IDisposable, so there is nothing to
        // dispose here. Rather than allocate a Stopwatch object on every painted frame (GC
        // pressure on the render hot path), use the allocation-free high-resolution timestamp
        // API: GetTimestamp() returns a tick count and GetElapsedTime() turns the delta into a
        // TimeSpan — no object is created.
        long start = Stopwatch.GetTimestamp();
        TimeSpan budget = TimeSpan.FromMilliseconds(milliseconds);
        while (Stopwatch.GetElapsedTime(start) < budget)
        {
            // Intentional spin to emulate heavy per-frame work.
        }
    }
}

