using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MauiBenchDemo.ViewModels;

/// <summary>
/// Minimal <see cref="INotifyPropertyChanged"/> base class so the demo has no
/// dependency on an MVVM framework. Swap for the CommunityToolkit.Mvvm
/// <c>ObservableObject</c> in your real app if you prefer.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        Raise(propertyName);
        return true;
    }

    protected void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
