namespace HalloApp.WebApp.Components.Shared;

public sealed class DataListChip<TItem>
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public required Func<TItem, bool> Predicate { get; init; }

    public string Accent { get; init; } = "slate";

    public int? Count { get; init; }
}
