using System.Security.Claims;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace SkoulouApp.WebApp.Components.Pages;

public partial class FinanzBericht
{
    [Inject] private FinanzService FinanzService { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "schoolId")] public Guid SchoolId { get; set; }
    [SupplyParameterFromQuery(Name = "from")] public DateTime? From { get; set; }
    [SupplyParameterFromQuery(Name = "to")] public DateTime? To { get; set; }
    [SupplyParameterFromQuery(Name = "direction")] public string? DirectionValue { get; set; }
    [SupplyParameterFromQuery(Name = "sort")] public string? SortValue { get; set; }
    [SupplyParameterFromQuery(Name = "withSubscriptions")] public bool WithSubscriptions { get; set; }

    private SkoulouApp.Core.Services.FinanzBericht? bericht;
    private string? error;

    private string PdfUrl => $"/finanzen/rapport/pdf-file?{Query}";
    private string Query => $"schoolId={SchoolId:D}&sort={ParseSort(SortValue) ?? FinanzSortierung.WirkungsdatumAbsteigend}&withSubscriptions={WithSubscriptions.ToString().ToLowerInvariant()}"
        + (From.HasValue ? $"&from={From:yyyy-MM-dd}" : string.Empty)
        + (To.HasValue ? $"&to={To:yyyy-MM-dd}" : string.Empty)
        + (ParseDirection(DirectionValue) is { } direction ? $"&direction={direction}" : string.Empty);

    internal static FinanzRichtung? ParseDirection(string? value) =>
        Enum.TryParse<FinanzRichtung>(value, true, out var direction) && Enum.IsDefined(direction) ? direction : null;

    internal static FinanzSortierung? ParseSort(string? value) =>
        Enum.TryParse<FinanzSortierung>(value, true, out var sort) && Enum.IsDefined(sort)
            ? sort : null;

    protected override async Task OnParametersSetAsync()
    {
        bericht = null;
        if ((!string.IsNullOrWhiteSpace(DirectionValue) && ParseDirection(DirectionValue) is null)
            || (!string.IsNullOrWhiteSpace(SortValue) && ParseSort(SortValue) is null))
        {
            error = "Les filtres du rapport ne sont pas valides.";
            return;
        }

        try
        {
            var user = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
            if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                error = "Connectez-vous pour consulter le rapport.";
                return;
            }

            bericht = await FinanzService.GetFinanzBerichtAsync(userId, user.IsInRole("SuperAdmin"), new FinanzFilter
            {
                SchulID = SchoolId,
                Von = From,
                Bis = To,
                Richtung = ParseDirection(DirectionValue),
                Sortierung = ParseSort(SortValue) ?? FinanzSortierung.WirkungsdatumAbsteigend
            }, ComponentToken, WithSubscriptions);
            error = bericht is null ? "Ce rapport n'est pas disponible pour cette école ou ces filtres." : null;
        }
        catch (Exception ex) when (IstAbbruch(ex)) { }
    }
}
