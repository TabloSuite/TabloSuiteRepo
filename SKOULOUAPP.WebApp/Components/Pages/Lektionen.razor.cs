using HalloApp.Core.Models.Home;
using HalloApp.Core.Services;
using Microsoft.AspNetCore.Components;

namespace HalloApp.WebApp.Components.Pages;

public partial class Lektionen
{
    [Inject]
    private DashboardService DashboardService { get; set; } = default!;

    private IReadOnlyList<HomeCatalogLessonItem> _lektionen = [];
    private bool _istGeladen;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _lektionen = await DashboardService.GetCatalogLessonsAsync(ComponentToken);
            _istGeladen = true;
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }
}

