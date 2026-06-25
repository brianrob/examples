using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExampleApp.Diagnostics;

namespace ExampleApp;

public partial class MainPage : ContentPage
{
	private const int MaxEventLines = 200;

	private readonly FrameCanvasDrawable _frameDrawable = new();

	// --- Counter snapshot pane (top half) -------------------------------------------------
	// Counters are a *current value per name*, so we keep the latest value per counter and
	// re-render the whole table when new samples arrive — i.e. the pane is replaced each
	// interval rather than scrolled. A generation counter (bumped on each sample) lets the
	// UI tick skip the rebuild when nothing changed.
	private readonly ConcurrentDictionary<string, CounterLine> _counterSnapshot = new();
	private long _counterGeneration;
	private long _renderedCounterGeneration = -1;

	// --- Raw events pane (bottom half) ----------------------------------------------------
	// Non-counter events are a *stream of discrete things*, so this pane scrolls. Lines are
	// enqueued on background threads and drained in one batch from the UI tick (a single
	// Label update, not a CollectionView, to keep the render loop smooth).
	private readonly LinkedList<string> _events = new();
	private readonly ConcurrentQueue<string> _pendingRawLines = new();

	private IDispatcherTimer? _uiRefreshTimer;
	private readonly FramePump _framePump = new();

	public MainPage()
	{
		InitializeComponent();

		PidLabel.Text = $"PID: {Environment.ProcessId}  (pass this to: CounterListener --pid {Environment.ProcessId})";

		FrameCanvas.Drawable = _frameDrawable;

		// Mirror the in-process listener's samples into the two panes.
		if (DiagnosticsBootstrapper.InProcessListener is { } listener)
		{
			listener.CounterReceived += OnCounterReceived;
			listener.RawEventReceived += OnRawEventReceived;
		}
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		// Drive the render loop from the platform's per-frame signal (vsync/compositor)
		// rather than a fixed-interval dispatcher timer. On WinUI a 16 ms dispatcher timer
		// delivers irregular, coalesced ticks, which made the frame rate swing wildly
		// (e.g. 3-40 fps) even when idle. CompositionTarget.Rendering (Windows) and
		// CADisplayLink (Mac Catalyst) fire exactly once per composed frame, so invalidating
		// on each one yields a smooth, display-locked rate. Both fire on the UI thread, so
		// the "Freeze UI" demo still drops the rate to ~0 while the thread is blocked.
		_framePump.Start(() => FrameCanvas.Invalidate());

		// Start UI-hang detection: a UI-thread heartbeat plus a background watchdog. The
		// watchdog publishes through the app's EventSource (the ui-thread-lag-ms /
		// ui-hang-count counters and the UiHangDetected/UiHangEnded events), so a hang is
		// observable both here in-process and over EventPipe. Tied to the page lifetime here;
		// a real app would start it once at startup with the main dispatcher.
		UiHangDetector.Instance.Start(Dispatcher);

		// Refresh the live numbers and drain queued counter lines a few times a second.
		_uiRefreshTimer = Dispatcher.CreateTimer();
		_uiRefreshTimer.Interval = TimeSpan.FromMilliseconds(250);
		_uiRefreshTimer.Tick += OnUiRefreshTick;
		_uiRefreshTimer.Start();
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();

		_framePump.Stop();
		UiHangDetector.Instance.Stop();

		if (_uiRefreshTimer is not null)
		{
			_uiRefreshTimer.Tick -= OnUiRefreshTick;
			_uiRefreshTimer.Stop();
			_uiRefreshTimer = null;
		}
	}

	private void OnUiRefreshTick(object? sender, EventArgs e)
	{
		// Live numbers — computed independently of any listener, so they update on their own.
		double fps = FrameRateCounter.Instance.SampleDisplayFramesPerSecond();
		ThreadPoolQueueDelayCounter queueDelay = ThreadPoolQueueDelayCounter.Instance;

		string text = $"Frame rate: {fps:0.#} fps     Thread-pool queue delay: {queueDelay.LastDelayMilliseconds:0.##} ms";

		// Surface starvation right next to the queue-delay number. Keep the warning up for a
		// few seconds after the last over-threshold sample so a brief spike is still visible.
		bool starvedRecently = queueDelay.IsStarved ||
			(DateTime.UtcNow - queueDelay.LastStarvationAtUtc) < TimeSpan.FromSeconds(3);
		if (starvedRecently)
		{
			text += $"     ⚠ THREAD-POOL STARVATION (peak {queueDelay.LastStarvationPeakMilliseconds:0} ms)";
		}

		// Surface UI hangs the same way. While the UI thread is actually hung this tick can't
		// run, so the warning appears just after recovery — the watchdog (on a background
		// thread) has by then recorded the hang's peak duration. Keep it visible for a few
		// seconds so a brief hang doesn't flash past.
		UiHangDetector hang = UiHangDetector.Instance;
		bool hungRecently = hang.IsHanging ||
			(DateTime.UtcNow - hang.LastHangAtUtc) < TimeSpan.FromSeconds(3);
		if (hungRecently)
		{
			text += $"     ⚠ UI HANG (peak {hang.LastHangPeakMilliseconds:0} ms)";
		}

		MetricsLabel.Text = text;
		MetricsLabel.TextColor = (starvedRecently || hungRecently) ? Colors.OrangeRed : Colors.Black;

		RenderCounterSnapshot();
		DrainRawEvents();
	}

	private void RenderCounterSnapshot()
	{
		// Skip the rebuild when no new samples arrived since the last render.
		long generation = Interlocked.Read(ref _counterGeneration);
		if (generation == _renderedCounterGeneration)
		{
			return;
		}

		_renderedCounterGeneration = generation;

		// Stable, readable order: this app's counters first, then System.Runtime by name.
		List<CounterLine> ordered = _counterSnapshot.Values
			.OrderBy(c => c.Provider == InProcessCounterListener.AppProviderName ? 0 : 1)
			.ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
			.ToList();

		var sb = new StringBuilder();
		sb.Append("Snapshot @ ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
		foreach (CounterLine counter in ordered)
		{
			sb.Append(counter.FormatSnapshot()).Append('\n');
		}

		CountersLabel.Text = sb.ToString();
	}

	private void DrainRawEvents()
	{
		bool added = false;
		while (_pendingRawLines.TryDequeue(out string? line))
		{
			_events.AddLast(line);
			added = true;
		}

		if (!added)
		{
			return;
		}

		while (_events.Count > MaxEventLines)
		{
			_events.RemoveFirst();
		}

		EventsLabel.Text = string.Join('\n', _events);

		// Scroll to the bottom so the newest line is visible. A large target Y is clamped.
		_ = EventsScroll.ScrollToAsync(0, double.MaxValue, animated: false);
	}

	private void OnCounterReceived(CounterLine line)
	{
		// Fires on a background thread; record the latest value per counter and bump the
		// generation so the UI tick rebuilds the snapshot.
		_counterSnapshot[$"{line.Provider}/{line.Name}"] = line;
		Interlocked.Increment(ref _counterGeneration);
	}

	private void OnRawEventReceived(RawEventLine line)
	{
		// Fires on a background thread; just enqueue. The UI timer drains the queue.
		_pendingRawLines.Enqueue(line.Format());
	}

	private void OnToggleRenderLoad(object? sender, EventArgs e)
	{
		if (_frameDrawable.ExtraRenderLoadMilliseconds > 0)
		{
			_frameDrawable.ExtraRenderLoadMilliseconds = 0;
			RenderLoadBtn.Text = "Add render load";
		}
		else
		{
			// ~25 ms of work per frame caps the frame rate well below the display refresh.
			_frameDrawable.ExtraRenderLoadMilliseconds = 25;
			RenderLoadBtn.Text = "Remove render load";
		}

		// Emit a regular (non-counter) event; it shows up in the Raw events pane.
		AppEventSource.Log.RenderLoadChanged(_frameDrawable.ExtraRenderLoadMilliseconds);
	}

	private void OnInduceStarvation(object? sender, EventArgs e)
	{
		const int durationMs = 3000;
		int workItems = Environment.ProcessorCount * 4;

		// Log the deliberate action. The starvation it causes is *detected* separately by the
		// queue-delay probe, which logs its own "starvation DETECTED" event and drives the
		// warning shown next to the queue-delay number above.
		AppEventSource.Log.ThreadPoolStarvationInduced(workItems, durationMs);
		ThreadPoolStarvation.Induce(TimeSpan.FromMilliseconds(durationMs), workItems);
	}

	private void OnFreezeUi(object? sender, EventArgs e)
	{
		// Block the UI thread on purpose. Painting stops, so the frame rate collapses to
		// ~0 fps for the duration; the in-process listener and the remote CounterListener
		// (both on background threads) will record the dip. Emit the event first so it is
		// queued before the UI thread blocks.
		AppEventSource.Log.UiFrozen(3000);
		Thread.Sleep(TimeSpan.FromSeconds(3));
	}
}
