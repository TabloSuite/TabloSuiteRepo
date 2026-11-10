using System.Globalization;
using System.Security.Claims;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace SkoulouApp.WebApp.Components.Pages;

/// <summary>Espace parents: nur Daten der verknüpften Kinder (Prüfung im ElternService und in den PDF-Endpunkten).</summary>
public partial class ElternBereich
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    [Inject] private ElternService Eltern { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    private List<KindUebersicht> kinder = [];
    private Guid aktivID;
    private bool laedt = true;

    private KindUebersicht? Aktiv => kinder.FirstOrDefault(k => k.SchuelerID == aktivID) ?? kinder.FirstOrDefault();

    protected override async Task OnInitializedAsync()
    {
        var user = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
        if (Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var elternID))
            kinder = await Eltern.KinderAsync(elternID, ComponentToken);
        aktivID = kinder.FirstOrDefault()?.SchuelerID ?? Guid.Empty;
        laedt = false;
    }

    private static string Betrag(decimal wert) => $"{wert.ToString("N0", Fr)} FCFA";

    private static string AnwesenheitText(KindAnwesenheit e) => e.Status switch
    {
        AnwesenheitsStatus.Abwesend => e.Entschuldigt ? "Absence justifiée" : "Absence non justifiée",
        AnwesenheitsStatus.Verspaetet => e.Minuten > 0 ? $"Retard ({e.Minuten} min)" : "Retard",
        _ => "Présent(e)"
    };
}
