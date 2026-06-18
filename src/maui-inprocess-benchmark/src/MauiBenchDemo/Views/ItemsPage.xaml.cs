using MauiBenchDemo.ViewModels;

namespace MauiBenchDemo.Views;

public partial class ItemsPage : ContentPage
{
    public ItemsViewModel ViewModel { get; } = new();

    public ItemsPage()
    {
        InitializeComponent();
        BindingContext = ViewModel;
    }

    private void OnAddClicked(object? sender, EventArgs e) => ViewModel.Add();

    private void OnRemoveClicked(object? sender, EventArgs e) => ViewModel.RemoveLast();

    private async void OnOpenFirstClicked(object? sender, EventArgs e)
    {
        if (ViewModel.Items.Count > 0)
            await Navigation.PushAsync(new DetailPage(ViewModel.Items[0]));
    }
}
