using Microsoft.AspNetCore.Components;

namespace SkoulouApp.WebApp.Components.Shared;

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

    [Parameter]
    public bool OpenUpward { get; set; }

    /// <summary>
    /// Gibt es genau eine echte Option (z. B. die einzige Schule eines Kontos), wird sie
    /// automatisch gewählt und als feste Angabe statt als Auswahlliste angezeigt.
    /// Platzhalter wie « Toutes les écoles » (null bzw. Standardwert) zählen nicht als echte Option.
    /// </summary>
    [Parameter]
    public bool EinzigeOptionFest { get; set; }

    private bool autoAuswahlLaeuft;

    protected ModernDropdownOption<TValue?>? EinzigeOption
    {
        get
        {
            if (!EinzigeOptionFest)
                return null;
            var echte = Options.Where(option => option.Value is not null && !(option.Value is string text && text.Length == 0)
                && !EqualityComparer<TValue?>.Default.Equals(option.Value, default)).Take(2).ToList();
            return echte.Count == 1 ? echte[0] : null;
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (EinzigeOption is not { } einzige || Disabled || autoAuswahlLaeuft
            || EqualityComparer<TValue?>.Default.Equals(einzige.Value, Value))
            return;

        autoAuswahlLaeuft = true;
        try
        {
            Value = einzige.Value;
            await ValueChanged.InvokeAsync(Value);
            await Changed.InvokeAsync(Value);
        }
        finally
        {
            autoAuswahlLaeuft = false;
        }
    }

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
