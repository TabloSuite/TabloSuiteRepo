using Microsoft.AspNetCore.Components;

namespace HalloApp.WebApp.Components.Shared;

public partial class DataListPanel<TItem> : ComponentBase
{
    private string searchText = string.Empty;
    private int currentPage;
    private string? activeSortKey;

    [Parameter, EditorRequired]
    public IReadOnlyList<TItem> Items { get; set; } = Array.Empty<TItem>();

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

    private void SelectChip(string key)
    {
        activeChipKey = activeChipKey == key ? null : key;
        currentPage = 0;
    }

    private static string ChipClass(bool isActive, string accent)
    {
        var baseClass = "inline-flex items-center gap-2 rounded-full px-4 py-2 text-sm font-semibold transition duration-200 ";
        if (!isActive)
        {
            return baseClass + "bg-slate-100 text-slate-600 hover:bg-slate-200";
        }

        return baseClass + accent switch
        {
            "amber" => "bg-amber-600 text-white shadow-md shadow-amber-200",
            "rose" => "bg-rose-600 text-white shadow-md shadow-rose-200",
            "emerald" => "bg-emerald-600 text-white shadow-md shadow-emerald-200",
            "blue" => "bg-blue-600 text-white shadow-md shadow-blue-200",
            "indigo" => "bg-indigo-600 text-white shadow-md shadow-indigo-200",
            _ => "bg-slate-900 text-white shadow-md shadow-slate-200"
        };
    }

    private static string ChipCountClass(bool isActive)
        => "rounded-full px-2 py-0.5 text-xs font-bold " + (isActive ? "bg-white/25 text-white" : "bg-white text-slate-700");


    private int FilteredCount => FilteredItems.Count;

    private int PageCount => Math.Max(1, (int)Math.Ceiling(FilteredCount / (double)Math.Max(1, PageSize)));

    private IReadOnlyList<TItem> PageItems
    {
        get
        {
            var filtered = FilteredItems;
            var page = Math.Clamp(currentPage, 0, PageCount - 1);
            return filtered
                .Skip(page * PageSize)
                .Take(PageSize)
                .ToList();
        }
    }

    private void OnSearchChanged(ChangeEventArgs args)
    {
        searchText = args.Value?.ToString() ?? string.Empty;
        currentPage = 0;
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
}
