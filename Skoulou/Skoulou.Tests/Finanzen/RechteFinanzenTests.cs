using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Services.Finanzen;

namespace Skoulou.Tests.Finanzen;

/// <summary>
/// Rechte-Matrix je Dienstmethode. Buchstaben: D Direktor, K Koordinator mit Finanzrechten, O Koordinator ohne Rechte,
/// L Lehrer, S Schüler, E Elternteil (des Schülers), R Responsable der Schule, A Admin, X Direktor einer anderen Schule.
/// « Darf » = keine <see cref="KeinZugriffFehler"/> (ein fachlicher Fehler danach zählt als erlaubt).
/// </summary>
public class RechteFinanzenTests
{
    private const string Alle = "DKOLSERAX";

    private static Guid Akteur(FinanzWelt w, char c) => c switch
    {
        'K' => w.KMit,
        'O' => w.KOhne,
        _ => w.Org.Akteur(c)
    };

    private static async Task MatrixAsync(FinanzWelt w, string erlaubt, Func<Guid, Task> aktion)
    {
        foreach (var c in Alle)
        {
            bool darf;
            try
            {
                await aktion(Akteur(w, c));
                darf = true;
            }
            catch (KeinZugriffFehler)
            {
                darf = false;
            }
            catch (FachFehler)
            {
                darf = true;
            }
            Assert.True(darf == erlaubt.Contains(c), $"Rolle {c}: erwartet {(erlaubt.Contains(c) ? "erlaubt" : "verboten")}.");
        }
    }

    [Fact]
    public async Task Gebuehren_Zahlungen_Journal_Bericht_nur_fuer_die_eigene_Direktion_mit_Recht()
    {
        var w = await FinanzWelt.ErstelleAsync();
        var j = w.Org.Jahr;
        await MatrixAsync(w, "DK", a => w.Gebuehren.PostenAsync(a, j));
        await MatrixAsync(w, "DK", a => w.Gebuehren.PostenSpeichernAsync(a, j, null, new PostenEingabe("Tenue", 8_000, null, null, false, [])));
        await MatrixAsync(w, "DK", a => w.Gebuehren.ZaehlerAsync(a, j, null));
        await MatrixAsync(w, "DK", a => w.Gebuehren.KontoAsync(a, j, w.Org.Schueler));
        await MatrixAsync(w, "DK", a => w.Gebuehren.ErmaessigungAnlegenAsync(a, j, new ErmaessigungEingabe(w.Org.Schueler, null, ErmaessigungsArt.Montant, 500, "Fratrie")));
        await MatrixAsync(w, "DK", a => w.Zahlungen.ErfassenAsync(a, j, new ZahlungEingabe(Guid.NewGuid(), w.Org.Schueler, 1_000, new(2026, 11, 12), ZahlungsKanal.Especes, null, null)));
        await MatrixAsync(w, "DK", a => w.Zahlungen.SucheAsync(a, j, null, null, 0, 50));
        await MatrixAsync(w, "DK", a => w.Zahlungen.SchuelerSuchenAsync(a, j, "eleve"));
        await MatrixAsync(w, "DKX", a => w.Journal.KategorienAsync(a));
        await MatrixAsync(w, "DKX", a => w.Journal.SucheAsync(a, new JournalFilter(null, null, null, null, null), 0, 50));
        await MatrixAsync(w, "DKX", a => w.Journal.ErfassenAsync(a, new BuchungEingabe(new(2026, 11, 2), Guid.NewGuid(), 1_000, "Essai"), null));
        await MatrixAsync(w, "DK", a => w.Journal.KasseAsync(a, j));
        await MatrixAsync(w, "DKX", a => w.Bericht.BerichtAsync(a, new(2026, 9, 1), new(2026, 11, 13)));
    }

    [Fact]
    public async Task Koordinatorrechte_einzeln()
    {
        var w = await FinanzWelt.ErstelleAsync();
        await using (var db = await w.Db.CreateDbContextAsync())
        {
            var k = await db.Users.FindAsync(w.KOhne);
            k!.KoordinatorRechte = Skoulou.Core.Rechte.KoordinatorRechte.ZahlungenErfassen;
            await db.SaveChangesAsync();
        }
        // Nur « Paiements » (inkl. Frais): kassieren und Gebühren ja, Journal-Schreiben, Bericht und SKOULOU-Rechnung nein.
        await w.ZahlenAsync(w.Org.Schueler, 1_000, akteur: w.KOhne);
        await w.Gebuehren.PostenAsync(w.KOhne, w.Org.Jahr);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Journal.ErfassenAsync(w.KOhne, new BuchungEingabe(new(2026, 11, 2), Guid.NewGuid(), 1, "x"), null));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Journal.SucheAsync(w.KOhne, new JournalFilter(null, null, null, null, null), 0, 50));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Bericht.BerichtAsync(w.KOhne, new(2026, 9, 1), new(2026, 11, 13)));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Rechnungen.ListeAsync(w.KOhne, null, null, null, 0, 50));
    }

    [Fact]
    public async Task Plattformrechnungen_Schule_Responsable_Admin()
    {
        var w = await FinanzWelt.ErstelleAsync();
        var id = (await w.Rechnungen.JetztAbrechnenAsync(w.Org.Admin, w.Org.P1))!.Value;
        await MatrixAsync(w, "DKRAX", a => w.Rechnungen.ListeAsync(a, null, null, null, 0, 50));
        await MatrixAsync(w, "DKRA", a => w.Rechnungen.DetailAsync(a, id));
        await MatrixAsync(w, "DKRA", a => w.Rechnungen.PdfAsync(a, id));
        await MatrixAsync(w, "DK", a => w.Rechnungen.AnfechtenAsync(a, id, "Motif"));
        await MatrixAsync(w, "A", a => w.Rechnungen.AufrechterhaltenAsync(a, id, "Réponse"));
        await MatrixAsync(w, "DK", a => w.Rechnungen.MeldenAsync(a, id, new MeldungEingabe(10_000, new(2026, 11, 13), "VIR"), null));
        await MatrixAsync(w, "A", a => w.Rechnungen.BestaetigenAsync(a, id));
        await MatrixAsync(w, "A", a => w.Rechnungen.StornierenAsync(a, id, "Motif"));
        await MatrixAsync(w, "A", a => w.Rechnungen.JetztAbrechnenAsync(a, w.Org.P2));
        await MatrixAsync(w, "A", a => w.Rechnungen.AbrechenbareAsync(a, w.Org.SchuleA));
        await MatrixAsync(w, "RA", a => w.Anteile.ListeAsync(a, null, null, 0, 50));
        await MatrixAsync(w, "A", a => w.Anteile.AuszahlenAsync(a, [Guid.NewGuid()], new(2026, 11, 13), "OM"));
        await MatrixAsync(w, "A", a => w.Tarife.EinstellungAsync(a));
        await MatrixAsync(w, "A", a => w.Tarife.SchulTarifSpeichernAsync(a, w.Org.SchuleA, null, null));

        // Responsable sieht nur seine Schulen: ohne Zuordnung keine Rechnung mehr.
        await using (var db = await w.Db.CreateDbContextAsync())
        {
            (await db.Schulen.FindAsync(w.Org.SchuleA))!.ResponsableId = null;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Rechnungen.DetailAsync(w.Org.Responsable, id));
        Assert.Empty((await w.Rechnungen.ListeAsync(w.Org.Responsable, null, null, null, 0, 50)).Eintraege);
    }

    [Fact]
    public async Task Schueler_und_Eltern_sehen_nur_das_eigene_Konto()
    {
        var w = await FinanzWelt.ErstelleAsync();
        await w.ZahlenAsync(w.Org.Schueler, 10_000);
        var s = await w.Familie.KontoAsync(w.Org.Schueler, null);
        Assert.Equal(60_000, s!.Konto.Offen);
        var e = await w.Familie.KontoAsync(w.Org.Eltern, w.Org.Schueler);
        Assert.Equal(s.Konto.Offen, e!.Konto.Offen);
        Assert.Single(e.Zahlungen);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Familie.KontoAsync(w.Org.Eltern, w.Org.SchuelerZiel));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Familie.KontoAsync(w.Org.ElternZiel, w.Org.Schueler));
        foreach (var c in "DKOLRAX")
            await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Familie.KontoAsync(Akteur(w, c), w.Org.Schueler));
    }
}
