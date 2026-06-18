namespace MauiBenchDemo.ViewModels;

/// <summary>
/// View model for <c>MainPage</c>. Exercised by the "update binding" benchmark.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private string _message = "Ready. Press a button, or run the in-process benchmarks.";
    private int _counter;

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public int Counter
    {
        get => _counter;
        set
        {
            if (SetProperty(ref _counter, value))
                Raise(nameof(CounterText));
        }
    }

    public string CounterText => $"Counter: {_counter}";

    public void Increment() => Counter++;
}
