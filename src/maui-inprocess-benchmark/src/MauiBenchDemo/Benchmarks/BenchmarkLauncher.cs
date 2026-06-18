using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;

namespace MauiBenchDemo.Benchmarks;

/// <summary>
/// Starts BenchmarkDotNet IN-PROCESS (no child process is spawned), so the
/// benchmarks run inside the live MAUI app and can drive the real UI.
///
/// Why in-process matters here:
///   * BenchmarkDotNet normally generates and launches a separate console host
///     per job. That host could not bootstrap WinUI / Mac Catalyst, and a second
///     process would reintroduce exactly the cross-process latency we want to avoid.
///   * <see cref="InProcessNoEmitToolchain"/> keeps everything in this one process
///     without relying on Reflection.Emit, which is unavailable on AOT platforms.
/// </summary>
public static class BenchmarkLauncher
{
    /// <summary>
    /// True when the app was launched in benchmark mode, via either the
    /// <c>--benchmark</c> command-line argument or the <c>RUN_BENCHMARKS=1</c> env var.
    /// </summary>
    public static bool ShouldAutoRun() =>
        Environment.GetCommandLineArgs()
            .Any(a => a.Equals("--benchmark", StringComparison.OrdinalIgnoreCase))
        || string.Equals(Environment.GetEnvironmentVariable("RUN_BENCHMARKS"), "1", StringComparison.Ordinal);

    /// <summary>
    /// If launched in benchmark mode, run the benchmarks once the page is loaded and
    /// then quit the app. Called from <c>MainPage</c>'s constructor.
    /// </summary>
    public static void AutoRunWhenReady(Page home)
    {
        if (!ShouldAutoRun())
            return;

        WhenLoaded(home, () => _ = Task.Run(() =>
        {
            var exitCode = 0;
            try
            {
                RunCore();
            }
            catch (Exception ex)
            {
                exitCode = 1;
                Console.Error.WriteLine(ex);
            }
            finally
            {
                UiDispatcher.Instance.Dispatch(() =>
                {
                    Application.Current?.Quit();
                    if (exitCode != 0)
                        Environment.Exit(exitCode);
                });
            }
        }));
    }

    /// <summary>
    /// Runs the benchmarks on a background thread while the app keeps running.
    /// Wired to the "Run UI benchmarks" button so you can trigger them interactively.
    /// </summary>
    public static Task RunInteractiveAsync() => Task.Run(RunCore);

    private static void RunCore()
    {
#if WINDOWS
        EnsureConsole();
#endif
        BenchmarkRunner.Run<UiBenchmarks>(CreateConfig());
    }

    /// <summary>
    /// The config: an in-process job. Uses <see cref="Job.ShortRun"/> so the demo
    /// finishes quickly — switch to <c>Job.Default</c> for higher-precision numbers.
    /// <see cref="ConfigOptions.DisableOptimizationsValidator"/> lets it also run from
    /// a Debug build (Release is still recommended for real measurements).
    /// </summary>
    public static IConfig CreateConfig() =>
        ManualConfig.Create(DefaultConfig.Instance)
            .AddJob(Job.ShortRun.WithToolchain(InProcessNoEmitToolchain.Instance))
            .WithArtifactsPath(GetArtifactsPath())
            .WithOptions(ConfigOptions.DisableOptimizationsValidator);

    private static string GetArtifactsPath()
    {
        var launchDirectory = Environment.GetEnvironmentVariable("PWD");
        var baseDirectory = string.IsNullOrWhiteSpace(launchDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : launchDirectory;

        return Path.Combine(baseDirectory, "BenchmarkDotNet.Artifacts");
    }

    private static void WhenLoaded(Page page, Action onLoaded)
    {
        if (page.IsLoaded)
        {
            onLoaded();
            return;
        }

        void Handler(object? sender, EventArgs e)
        {
            page.Loaded -= Handler;
            onLoaded();
        }

        page.Loaded += Handler;
    }

#if WINDOWS
    private static bool _consoleAllocated;

    // A MAUI WinUI app is a windowed (no-console) process, so BenchmarkDotNet's
    // console output would go nowhere. Allocate a console so results are visible
    // live. (Results are ALSO always written to BenchmarkDotNet.Artifacts/.)
    private static void EnsureConsole()
    {
        if (_consoleAllocated)
            return;
        _consoleAllocated = true;
        AllocConsole();
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AllocConsole();
#endif
}
