using Microsoft.EntityFrameworkCore;
using Skoulou.WebApp.Components.Shared;
using Skoulou.WebApp.Services.Finanzen;

namespace Skoulou.Tests.Finanzen;

/// <summary>
/// Nutzerentscheidung 22.11.2026 (Normaltest Befund 6): « Facturation » des Admins startet mit dem Filter « À confirmer »; die Suche
/// nach einem Schulnamen lieferte darin « Aucun résultat ». Eine Suche gilt jetzt immer für alle Rechnungen – sichtbar, weil der
/// Chip auf « Toutes » springt und ein Hinweis erscheint.
/// </summary>
public class RechnungSucheTests
{
    [Fact]
    public void Beginn_einer_Suche_setzt_den_Filter_auf_Toutes()
    {
        Assert.Null(ListenSuche.FilterBeiSuche(RechnungService.FilterDeclarees, null, "Alpha", ueberAlle: true));
        Assert.Null(ListenSuche.FilterBeiSuche(RechnungService.FilterDeclarees, "", "  Al", ueberAlle: true));
        // Während der Suche selbst gewählter Chip bleibt; ohne Schalter (andere Listen) wie bisher.
        Assert.Equal(RechnungService.FilterPayees, ListenSuche.FilterBeiSuche(RechnungService.FilterPayees, "Al", "Alpha", ueberAlle: true));
        Assert.Equal(RechnungService.FilterDeclarees, ListenSuche.FilterBeiSuche(RechnungService.FilterDeclarees, null, "Alpha", ueberAlle: false));
        Assert.Equal(RechnungService.FilterDeclarees, ListenSuche.FilterBeiSuche(RechnungService.FilterDeclarees, null, " ", ueberAlle: true));
    }

    [Fact]
    public async Task Admin_findet_eine_bezahlte_Rechnung_ueber_den_Schulnamen()
    {
        var w = await FinanzWelt.ErstelleAsync();
        w.Heute(new DateOnly(2026, 12, 19));
        await w.Abrechnung.FaelligeAbrechnenAsync();
        var r = await w.MitDbAsync(db => db.Plattformrechnungen.SingleAsync());
        await w.Rechnungen.BestaetigenAsync(w.Org.Admin, r.Id);

        // Im Startfilter « À confirmer » nichts – mit dem Filter, den die Liste beim Suchen setzt, die Rechnung.
        Assert.Empty((await w.Rechnungen.ListeAsync(w.Org.Admin, RechnungService.FilterDeclarees, null, w.Org.SchulNameA, 0, 50)).Eintraege);
        var filter = ListenSuche.FilterBeiSuche(RechnungService.FilterDeclarees, null, w.Org.SchulNameA, ueberAlle: true);
        Assert.Equal(r.Id, Assert.Single((await w.Rechnungen.ListeAsync(w.Org.Admin, filter, null, w.Org.SchulNameA, 0, 50)).Eintraege).Id);
    }

    [Fact]
    public void Rechnungsliste_sucht_ueber_alle_Status()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "Skoulou.WebApp"))) d = d.Parent;
        var liste = File.ReadAllText(Path.Combine(d!.FullName, "Skoulou.WebApp", "Components", "Pages", "Finances", "RechnungTeile", "RechnungListe.razor"));
        Assert.Contains("SucheUeberAlleFilter=\"true\"", liste);
        Assert.Contains("Recherche dans toutes les factures", liste);
    }
}
