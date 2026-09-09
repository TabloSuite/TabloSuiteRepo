using Microsoft.AspNetCore.Components;

namespace HalloApp.WebApp.Components.Shared;

public class ModernDropdownBase<TValue> : ComponentBase
{
    protected bool isOpen;
    protected string searchTerm = string.Empty;

    [Parameter]
    public TValue? Value { get; set; }

    [Parameter]
    public EventCallback<TValue?> ValueChanged { get; set; }

    [Parameter]
    public EventCallback<TValue?> Changed { get; set; }

    [Parameter]
    public IReadOnlyList<ModernDropdownOption<TValue?>> Options { get; set; } = [];

    [Parameter]
    public string? Label { get; set; }

    [Parameter]
    public string Placeholder { get; set; } = "Choisir...";

    [Parameter]
    public string EmptyText { get; set; } = "Aucune option disponible.";

    [Parameter]
    public string? ContainerClass { get; set; }

    [Parameter]
    public string? ButtonClass { get; set; }

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public bool Searchable { get; set; }

    [Parameter]
    public string SearchPlaceholder { get; set; } = "Rechercher...";

    protected ModernDropdownOption<TValue?>? SelectedOption => Options
        .FirstOrDefault(option => EqualityComparer<TValue?>.Default.Equals(option.Value, Value));

    protected IEnumerable<ModernDropdownOption<TValue?>> FilteredOptions => string.IsNullOrWhiteSpace(searchTerm)
        ? Options
        : Options.Where(option => MatchesSearch(option, searchTerm));

    protected Task ToggleAsync()
    {
        if (!Disabled)
        {
            isOpen = !isOpen;
            if (!isOpen)
            {
                searchTerm = string.Empty;
            }
        }

        return Task.CompletedTask;
    }

    protected Task CloseAsync()
    {
        isOpen = false;
        searchTerm = string.Empty;
        return Task.CompletedTask;
    }

    protected async Task SelectAsync(ModernDropdownOption<TValue?> option)
    {
        Value = option.Value;
        isOpen = false;
        searchTerm = string.Empty;
        await ValueChanged.InvokeAsync(Value);
        await Changed.InvokeAsync(Value);
    }

    private static bool MatchesSearch(ModernDropdownOption<TValue?> option, string term)
        => option.Label.Contains(term, StringComparison.CurrentCultureIgnoreCase)
           || (!string.IsNullOrWhiteSpace(option.Description)
               && option.Description.Contains(term, StringComparison.CurrentCultureIgnoreCase));
}
