using System.Globalization;
using System.Security.Claims;
using SkoulouApp.WebApp.Components.Shared;
using SkoulouApp.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace SkoulouApp.WebApp.Components.Pages;

/// <summary>Tableau de bord der Direktion: Kennzahlen, Aufgaben und Schnellzugriffe der eigenen Schule.</summary>
public partial class DirektionsCockpit
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    [Inject] private DirektionsCockpitService CockpitService { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    private SkoulouApp.WebApp.Services.DirektionsCockpit? cockpit;
    private bool laedt = true;
    private Guid kartenKlasseID;

    private List<ModernDropdownOption<Guid>> KlassenOptionen => cockpit?.KlassenListe.Select(k => new ModernDropdownOption<Guid>(k.Id, k.Code)).ToList() ?? [];

    private static readonly (string Icon, string Titel, string Link)[] Schnellzugriffe =
    [
        ("users", "Élèves et enseignants", "schulleiter"),
        ("clipboard-check", "Notes et évaluations", "notes"),
        ("timetable", "Emplois du temps", "emplois-du-temps"),
        ("users", "Accès parents", "direction/parents")
    ];

    private string Heute => DateTime.Now.ToString("dddd d MMMM yyyy", Fr);

    private List<(string Text, string Link, string LinkText)> Aufgaben { get; } = [];

    protected override async Task OnInitializedAsync()
    {
        var benutzer = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
        if (Guid.TryParse(benutzer.FindFirstValue(ClaimTypes.NameIdentifier), out var benutzerID))
        {
            cockpit = await CockpitService.LadeAsync(benutzerID, DateOnly.FromDateTime(DateTime.Now), ComponentToken);
        }

        if (cockpit is not null)
        {
            if (cockpit.AnmeldungenOffen > 0)
                Aufgaben.Add(($"{cockpit.AnmeldungenOffen} inscription(s) d'élève attendent votre validation.", "schulleiter?onglet=eleves", "Valider"));
            if (cockpit.Schuljahr is null)
                Aufgaben.Add(("Aucune année scolaire en cours : créez-la pour les notes, les bulletins et les présences.", "direction/enseignement", "Créer"));
            if (cockpit.Klassen == 0)
                Aufgaben.Add(("Créez vos premières classes.", "schulleiter?onglet=classes", "Classes"));
            if (cockpit.Lehrkraefte == 0)
                Aufgaben.Add(("Ajoutez vos enseignants (un par un ou par fichier).", "schulleiter?onglet=equipe", "Ajouter"));
        }

        laedt = false;
    }

    private static string Betrag(decimal wert) => $"{wert.ToString("N0", Fr)} FCFA";
}
