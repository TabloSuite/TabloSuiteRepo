using Microsoft.AspNetCore.Components;

namespace SkoulouApp.WebApp.Components.Shared;

/// <summary>
/// Einheitliche Listenansicht: Suche, Chips, Sortierung, Seiten und Scrollbox (.sk-liste-scroll).
/// Standard: Filter und Seiten im Speicher (für Daten, die pro Schule begrenzt sind).
/// <see cref="ServerPaging"/>: Suche und Chips gehen (verzögert) an die Seite, die serverseitig filtert
/// und mit „Afficher plus“ nachlädt (für Daten, die unbegrenzt wachsen).
/// </summary>
public partial class DataListPanel<TItem> : ComponentBase, IDisposable
{
    private string searchText = string.Empty;
    private int currentPage;
    private string? activeSortKey;
    private bool mehrLaedt;
    private readonly SuchVerzoegerung suchVerzoegerung = new();

    [Parameter, EditorRequired]
    public IReadOnlyList<TItem> Items { get; set; } = Array.Empty<TItem>();

    /// <summary>Einträge in einer Scrollbox (.sk-liste-scroll) anzeigen. Standard: an.</summary>
    [Parameter] public bool Scroll { get; set; } = true;

    /// <summary>
    /// Serverseitige Suche/Seiten: <see cref="Items"/> wird unverändert angezeigt, die Suche (verzögert)
    /// und die Chip-Auswahl werden über <see cref="SearchChanged"/>/<see cref="ChipChanged"/> gemeldet,
    /// statt Précédent/Suivant gibt es „Afficher plus“ (<see cref="LoadMore"/>).
    /// </summary>
    [Parameter] public bool ServerPaging { get; set; }

    [Parameter] public EventCallback<string> SearchChanged { get; set; }

    [Parameter] public EventCallback<string?> ChipChanged { get; set; }

    /// <summary>Gesamtzahl der Treffer auf dem Server (nur Anzeige im Server-Modus).</summary>
    [Parameter] public int? TotalCount { get; set; }

    [Parameter] public bool HasMore { get; set; }

    [Parameter] public EventCallback LoadMore { get; set; }

    [Parameter] public RenderFragment? HeaderContent { get; set; }

    [Parameter, EditorRequired]
    public RenderFragment<IReadOnlyList<TItem>> ChildContent { get; set; } = default!;

    [Parameter]
    public Func<TItem, string, bool>? Filter { get; set; }

    [Parameter]
    public int PageSize { get; set; } = 10;

    [Parameter]
    public string SearchPlaceholder { get; set; } = "Rechercher...";

    [Parameter]
    public string EmptyText { get; set; } = "Aucun résultat.";

    [Parameter]
    public string ResultLabelSingular { get; set; } = "résultat";

    [Parameter]
    public string ResultLabelPlural { get; set; } = "résultats";

    [Parameter]
    public IReadOnlyList<DataListChip<TItem>>? Chips { get; set; }

    [Parameter]
    public IReadOnlyList<DataListSort<TItem>>? SortOptions { get; set; }

    private string? activeChipKey;

    private DataListSort<TItem>? ActiveSort
        => SortOptions?.FirstOrDefault(s => s.Key == activeSortKey)
           ?? SortOptions?.FirstOrDefault();

    private IReadOnlyList<ModernDropdownOption<string>> sortDropdownOptions
        => (SortOptions ?? Array.Empty<DataListSort<TItem>>())
            .Select(option => new ModernDropdownOption<string>(option.Key, option.Label))
            .ToList();

    private void OnSortValueChanged(string? value)
    {
        activeSortKey = value;
        currentPage = 0;
    }

    private int CurrentPage => currentPage;

    private DataListChip<TItem>? ActiveChip
        => Chips?.FirstOrDefault(c => c.Key == activeChipKey);

    private IReadOnlyList<TItem> FilteredItems
    {
        get
        {
            // Server-Modus: Die Seite liefert bereits gefilterte, sortierte Einträge.
            if (ServerPaging)
            {
                return Items;
            }

            var term = searchText?.Trim() ?? string.Empty;
            var chip = ActiveChip;
            var sort = ActiveSort;

            IEnumerable<TItem> source = Items;

            if (term.Length > 0 || chip is not null)
            {
                var result = new List<TItem>();
                foreach (var item in Items)
                {
                    if (chip is not null && !chip.Predicate(item))
                    {
                        continue;
                    }

                    if (term.Length > 0 && Filter is not null && !Filter(item, term))
                    {
                        continue;
                    }

                    result.Add(item);
                }

                source = result;
            }

            if (sort is not null)
            {
                var ordered = sort.Descending
                    ? source.OrderByDescending(sort.KeySelector)
                    : source.OrderBy(sort.KeySelector);

                source = sort.SecondaryKeySelector is not null
                    ? ordered.ThenBy(sort.SecondaryKeySelector)
                    : ordered;
            }
            else if (ReferenceEquals(source, Items))
            {
                return Items;
            }

            return source as IReadOnlyList<TItem> ?? source.ToList();
        }
    }

    private async Task SelectChip(string key)
    {
        activeChipKey = activeChipKey == key ? null : key;
        currentPage = 0;
        if (ServerPaging && ChipChanged.HasDelegate)
        {
            await ChipChanged.InvokeAsync(activeChipKey);
        }
    }

    private static string ChipClass(bool isActive, string accent)
    {
        var baseClass = "inline-flex items-center gap-2 rounded-full border px-3.5 py-1.5 text-sm font-semibold transition duration-200 ";
        if (!isActive)
        {
            return baseClass + "border-slate-200 bg-white text-slate-600 hover:bg-slate-50 hover:text-slate-900";
        }

        return baseClass + accent switch
        {
            "amber" => "border-amber-300 bg-amber-50 text-amber-900",
            "rose" => "border-rose-300 bg-rose-50 text-rose-800",
            "emerald" => "border-emerald-300 bg-emerald-50 text-emerald-800",
            _ => "border-blue-200 bg-blue-50 text-blue-800"
        };
    }

    private static string ChipCountClass(bool isActive)
        => "rounded-full px-2 py-0.5 text-xs font-bold tabular-nums " + (isActive ? "bg-white text-slate-800" : "bg-slate-100 text-slate-700");


    private int FilteredCount => FilteredItems.Count;

    private int DisplayCount => ServerPaging ? TotalCount ?? Items.Count : FilteredCount;

    private string AnzeigeAnzahl => ServerPaging && TotalCount is null && HasMore
        ? $"{Items.Count:N0}+"
        : DisplayCount.ToString("N0");

    private int PageCount => Math.Max(1, (int)Math.Ceiling(FilteredCount / (double)Math.Max(1, PageSize)));

    private IReadOnlyList<TItem> PageItems
    {
        get
        {
            var filtered = FilteredItems;
            if (ServerPaging)
            {
                return filtered;
            }

            var page = Math.Clamp(currentPage, 0, PageCount - 1);
            return filtered
                .Skip(page * PageSize)
                .Take(PageSize)
                .ToList();
        }
    }

    private async Task OnSearchChanged(ChangeEventArgs args)
    {
        searchText = args.Value?.ToString() ?? string.Empty;
        currentPage = 0;
        if (ServerPaging && SearchChanged.HasDelegate)
        {
            var suche = searchText;
            await suchVerzoegerung.AusfuehrenAsync(() => SearchChanged.InvokeAsync(suche));
        }
    }

    private async Task LoadMoreAsync()
    {
        if (mehrLaedt || !LoadMore.HasDelegate)
        {
            return;
        }

        mehrLaedt = true;
        try
        {
            await LoadMore.InvokeAsync();
        }
        finally
        {
            mehrLaedt = false;
        }
    }

    private void PreviousPage()
    {
        if (currentPage > 0)
        {
            currentPage--;
        }
    }

    private void NextPage()
    {
        if (currentPage < PageCount - 1)
        {
            currentPage++;
        }
    }

    public void Dispose() => suchVerzoegerung.Dispose();
}
