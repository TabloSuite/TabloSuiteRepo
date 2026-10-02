namespace SkoulouApp.WebApp.Components.Shared;

public static class SearchHelper
{
    public static bool Contains(string? value, string term)
        => !string.IsNullOrEmpty(value)
           && value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
