using System.Collections.ObjectModel;
using MauiBenchDemo.Models;

namespace MauiBenchDemo.ViewModels;

/// <summary>
/// View model backing the <c>ItemsPage</c> CollectionView. Exercised by the
/// "mutate bound list" benchmark.
/// </summary>
public sealed class ItemsViewModel : ObservableObject
{
    private int _nextId = 1;

    public ObservableCollection<Item> Items { get; } = new();

    public ItemsViewModel(int initialCount = 20)
    {
        for (var i = 0; i < initialCount; i++)
            Add();
    }

    public Item Add()
    {
        var item = new Item
        {
            Id = _nextId,
            Name = $"Item {_nextId}",
            Detail = $"Detail text for item {_nextId}",
        };

        Items.Add(item);
        _nextId++;
        return item;
    }

    public void RemoveLast()
    {
        if (Items.Count > 0)
            Items.RemoveAt(Items.Count - 1);
    }
}
