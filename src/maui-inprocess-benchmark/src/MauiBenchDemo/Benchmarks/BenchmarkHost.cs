namespace MauiBenchDemo.Benchmarks;

/// <summary>
/// Holds references to the live, running application objects so the benchmark
/// class can poke at the real UI. Populated by <c>MainPage</c> as soon as it is
/// constructed (i.e. once the app and its UI thread are up).
/// </summary>
public static class BenchmarkHost
{
    /// <summary>The live home page (real, handler-backed view that is on screen).</summary>
    public static MainPage Home { get; set; } = default!;
}
