namespace HalloApp.WebApp.Components.Shared;

public sealed class DataListSort<TItem>
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public required Func<TItem, IComparable?> KeySelector { get; init; }

    public bool Descending { get; init; }
}
