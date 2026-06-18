using MauiBenchDemo.Benchmarks;
using MauiBenchDemo.ViewModels;
using MauiBenchDemo.Views;

namespace MauiBenchDemo;

public partial class MainPage : ContentPage
{
    public MainViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        BindingContext = ViewModel;

        // Wire the benchmark infrastructure to the LIVE app objects, on the UI thread.
        // From here on, benchmarks can marshal work back to this thread with no IPC.
        UiDispatcher.Instance = Dispatcher;
        BenchmarkHost.Home = this;

        // If launched with --benchmark / RUN_BENCHMARKS=1, run them once loaded, then quit.
        BenchmarkLauncher.AutoRunWhenReady(this);
    }

    private void OnIncrementClicked(object? sender, EventArgs e) => ViewModel.Increment();

    private async void OnItemsClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new ItemsPage());

    private async void OnRunBenchmarksClicked(object? sender, EventArgs e)
    {
        RunBenchmarksButton.IsEnabled = false;
        ViewModel.Message = "Running benchmarks in-process… watch the console / BenchmarkDotNet.Artifacts.";

        await BenchmarkLauncher.RunInteractiveAsync();

        ViewModel.Message = "Benchmarks complete. See BenchmarkDotNet.Artifacts for the report.";
        RunBenchmarksButton.IsEnabled = true;
    }
}
