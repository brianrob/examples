namespace MauiBenchDemo.Models;

/// <summary>
/// A trivial data record shown in the demo list and detail pages.
/// Replace this with your own model when adapting the repro.
/// </summary>
public sealed class Item
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}
