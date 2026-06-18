using MauiBenchDemo.Models;

namespace MauiBenchDemo.Views;

public partial class DetailPage : ContentPage
{
    public DetailPage(Item item)
    {
        InitializeComponent();
        BindingContext = item;
    }
}
