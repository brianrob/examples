using BenchmarkDotNet.Attributes;
using MauiBenchDemo.Models;
using MauiBenchDemo.ViewModels;
using MauiBenchDemo.Views;

namespace MauiBenchDemo.Benchmarks;

/// <summary>
/// A representative mix of MAUI UI operations driven IN-PROCESS against the live app.
///
/// Every benchmark marshals its work onto the real UI thread via <see cref="UiDispatcher"/>
/// and blocks until it finishes — so we measure real view construction, binding
/// propagation, layout, collection updates and navigation, with zero cross-process cost.
///
/// To adapt this for a customer app: keep the pattern (grab live objects in
/// <see cref="Setup"/>, do work inside <c>UiDispatcher.Run(...)</c>) and replace the
/// bodies below with the operations you care about.
/// </summary>
[MemoryDiagnoser]
public class UiBenchmarks
{
    private MainPage _home = default!;
    private MainViewModel _mainViewModel = default!;
    private ItemsPage _itemsPage = default!;
    private ItemsViewModel _itemsViewModel = default!;

    [GlobalSetup]
    public void Setup()
    {
        _home = BenchmarkHost.Home
            ?? throw new InvalidOperationException("BenchmarkHost.Home was not set. Is the app window up?");
        _mainViewModel = _home.ViewModel;

        // Build an items page once and push it so its CollectionView is realized
        // (has platform handlers) for the list / layout benchmarks.
        UiDispatcher.RunAsync(async () =>
        {
            _itemsPage = new ItemsPage();
            _itemsViewModel = _itemsPage.ViewModel;
            await _home.Navigation.PushAsync(_itemsPage, animated: false);
        });
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        UiDispatcher.RunAsync(async () =>
        {
            while (_home.Navigation.NavigationStack.Count > 1)
                await _home.Navigation.PopAsync(animated: false);
        });
    }

    /// <summary>1) Construct a fresh view tree — pure view object creation cost.</summary>
    [Benchmark]
    public object BuildViewTree() => UiDispatcher.Run(() =>
    {
        var layout = new VerticalStackLayout { Spacing = 4 };
        for (var i = 0; i < 50; i++)
            layout.Add(new Label { Text = $"Row {i}" });
        return (object)layout;
    });

    /// <summary>2) Change a bound property (INotifyPropertyChanged) and run a layout pass.</summary>
    [Benchmark]
    public void UpdateBindingAndLayout() => UiDispatcher.Run(() =>
    {
        _mainViewModel.Increment();   // propagates through the data binding
        MeasureArrange(_home);        // realize the effect with a layout pass
    });

    /// <summary>3) Measure + arrange the realized home page — layout engine only.</summary>
    [Benchmark]
    public void LayoutPass() => UiDispatcher.Run(() => MeasureArrange(_home));

    /// <summary>4) Add then remove an item from the collection bound to a CollectionView.</summary>
    [Benchmark]
    public void MutateBoundList() => UiDispatcher.Run(() =>
    {
        _itemsViewModel.Add();
        _itemsViewModel.RemoveLast();
    });

    /// <summary>5) Navigate forward to a detail page and back (handler create/teardown + nav stack).</summary>
    [Benchmark]
    public void NavigatePushPop() => UiDispatcher.RunAsync(async () =>
    {
        var detail = new DetailPage(new Item { Id = -1, Name = "Bench", Detail = "Navigation benchmark" });
        await _home.Navigation.PushAsync(detail, animated: false);
        await _home.Navigation.PopAsync(animated: false);
    });

    private static void MeasureArrange(VisualElement element)
    {
        var view = (IView)element;
        const double width = 400, height = 800;
        view.Measure(width, height);
        view.Arrange(new Rect(0, 0, width, height));
    }
}
