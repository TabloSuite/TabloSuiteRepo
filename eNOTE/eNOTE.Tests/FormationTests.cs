using ENote.Core.DataModels.Formation;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using ENote.WebApp.Services.Formation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Nutzerwunsch 17.11.: Formation der Responsables – Lernpfad + Prüfung in Runden (3 Versuche, der letzte zählt),
/// Intervall/Pause/Frist/Erlass/Rolle entziehen durch den Administrateur, freies Training für Administrateure.
/// </summary>
public class FormationTests
{
    private sealed class Uhr(DateTime jetzt) : TimeProvider
    {
        public DateTime Jetzt = jetzt;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Jetzt, DateTimeKind.Utc));
    }

    private sealed record Daten(IDbContextFactory<ApplicationDbContext> Factory, Uhr Uhr, FormationService Dienst, Guid Admin, Guid Po, Guid Salon);

    private static async Task<Daten> BasisAsync()
    {
        var factory = TestDbContextFactory.Create();
        var uhr = new Uhr(new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc));
        var admin = new AppUser { Id = Guid.NewGuid(), FullName = "Admin", Vorname = "Ad" };
        var po = new AppUser { Id = Guid.NewGuid(), FullName = "Koffi Responsable", Vorname = "Koffi", Email = "po@example.org", ActiveSessionId = Guid.NewGuid() };
        var salon = new Salon { Name = "Boutique A" };
        await using var db = await factory.CreateDbContextAsync();
        foreach (var (u, rolle) in new[] { (admin, AppRollen.SuperAdmin), (po, AppRollen.ProduktOwner) })
        {
            var r = new Role { Name = rolle };
            db.Roles.Add(r);
            db.Users.Add(u);
            db.UserRoles.Add(new AppUserRole { UserId = u.Id, RoleId = r.Id });
        }
        db.ProduktOwnerSalons.Add(new ProduktOwnerSalon { ProduktOwnerId = po.Id, Salon = salon });
        await db.SaveChangesAsync();
        return new(factory, uhr, new FormationService(factory, uhr), admin.Id, po.Id, salon.Id);
    }

    /// <summary>Beantwortet alle Fragen eines Versuchs: <paramref name="richtig"/> Stück richtig, der Rest falsch.</summary>
    private static async Task BeantworteAsync(FormationService dienst, Guid userId, FormationService.VersuchAnsicht v, int richtig)
    {
        var i = 0;
        foreach (var f in v.Fragen)
        {
            var frage = dienst.Lektionen.Concat(dienst.AdminLektionen).SelectMany(l => l.Pruefung).First(x => x.Id == f.Id);
            // Anzeige-Positionen der richtigen Antworten: Text vergleichen.
            var richtigeTexte = frage.Richtig.Select(r => frage.Antworten[r]).ToHashSet();
            var auswahl = i++ < richtig
                ? f.Antworten.Where(a => richtigeTexte.Contains(a.Text)).Select(a => a.Index).ToArray()
                : f.Antworten.Where(a => !richtigeTexte.Contains(a.Text)).Take(1).Select(a => a.Index).ToArray();
            Assert.Null(await dienst.SpeichereAntwortAsync(userId, v.Id, f.Id, auswahl));
        }
    }

    [Fact]
    public async Task Neuer_Responsable_Bekommt_Sofort_Eine_Runde_Mit_Frist_30_Tage()
    {
        var d = await BasisAsync();
        var stand = await d.Dienst.GetStandAsync(d.Po, istFrei: false);
        Assert.NotNull(stand.Offen);
        Assert.Equal(d.Uhr.Jetzt.AddDays(30), stand.Offen!.FristUtc);
        Assert.Equal(70, stand.Offen.Grenze);
        Assert.Equal(3, stand.IntervallMonate);
        Assert.Equal(FormationRunde.MaxVersuche, stand.Offen.Uebrig);
    }

    [Fact]
    public async Task Versuch_Zieht_Gemischte_Fragen_Ueber_Alle_Lektionen_Und_Speichert_Antworten_Sofort()
    {
        var d = await BasisAsync();
        var (v, fehler) = await d.Dienst.StarteVersuchAsync(d.Po, false);
        Assert.Null(fehler);
        Assert.Equal(20, v!.Fragen.Count);
        Assert.Equal(20, v.Fragen.Select(f => f.Id).Distinct().Count());
        // Gleichmäßig: jede Lektion mit mindestens einer Frage (10 Lektionen, 20 Fragen → je 2).
        var lektionJeFrage = d.Dienst.Lektionen.SelectMany(l => l.Pruefung.Select(f => (f.Id, l.Id))).ToDictionary(x => x.Item1, x => x.Item2);
        Assert.All(v.Fragen.GroupBy(f => lektionJeFrage[f.Id]), g => Assert.Equal(2, g.Count()));

        await d.Dienst.SpeichereAntwortAsync(d.Po, v.Id, v.Fragen[0].Id, [0]);
        // Neu laden (anderes Gerät / Netz weg): gleicher Versuch, Antwort noch da.
        var (wieder, _) = await d.Dienst.StarteVersuchAsync(d.Po, false);
        Assert.Equal(v.Id, wieder!.Id);
        Assert.Equal([0], wieder.Fragen.First(f => f.Id == v.Fragen[0].Id).Auswahl);
    }

    [Fact]
    public async Task Drei_Versuche_Nur_Der_Letzte_Zaehlt_Und_Neue_Fragen_Je_Versuch()
    {
        var d = await BasisAsync();
        var (v1, _) = await d.Dienst.StarteVersuchAsync(d.Po, false);
        await BeantworteAsync(d.Dienst, d.Po, v1!, 20);
        var (e1, _) = await d.Dienst.GibAbAsync(d.Po, v1!.Id);
        Assert.Equal(100, e1!.Prozent);
        Assert.True(e1.Bestanden);
        Assert.False(e1.RundeAbgeschlossen);
        Assert.Equal(2, e1.VersucheUebrig);

        var (v2, _) = await d.Dienst.StarteVersuchAsync(d.Po, false);
        // Pool von 62 Fragen: der zweite Versuch wiederholt keine Frage des ersten.
        Assert.Empty(v2!.Fragen.Select(f => f.Id).Intersect(v1.Fragen.Select(f => f.Id)));
        await BeantworteAsync(d.Dienst, d.Po, v2, 10);
        var (e2, _) = await d.Dienst.GibAbAsync(d.Po, v2.Id);
        Assert.Equal(50, e2!.Prozent);
        Assert.Equal(10, e2.Fragen.Count(f => !f.Richtig));
        Assert.All(e2.Fragen.Where(f => !f.Richtig), f => { Assert.NotEmpty(f.RichtigeAntworten); Assert.NotNull(f.LektionTitel); });

        var (v3, _) = await d.Dienst.StarteVersuchAsync(d.Po, false);
        await BeantworteAsync(d.Dienst, d.Po, v3!, 13);
        var (e3, _) = await d.Dienst.GibAbAsync(d.Po, v3!.Id);
        Assert.True(e3!.RundeAbgeschlossen);
        Assert.Equal(65, e3.Prozent);

        var stand = await d.Dienst.GetStandAsync(d.Po, false);
        Assert.Null(stand.Offen);
        var runde = Assert.Single(stand.Verlauf);
        // Nur der letzte Versuch zählt – obwohl der erste 100 % hatte.
        Assert.Equal(65, runde.EndNoteProzent);
        Assert.Equal(FormationRundenStatus.NichtBestanden, runde.Status);
        Assert.Equal(d.Uhr.Jetzt.AddMonths(3), stand.NaechsteRundeAbUtc);

        var (v4, f4) = await d.Dienst.StarteVersuchAsync(d.Po, false);
        Assert.Null(v4);
        Assert.NotNull(f4);
    }

    [Fact]
    public async Task Ergebnis_Behalten_Schliesst_Die_Runde_Und_Die_Naechste_Folgt_Nach_Dem_Intervall()
    {
        var d = await BasisAsync();
        var (v1, _) = await d.Dienst.StarteVersuchAsync(d.Po, false);
        await BeantworteAsync(d.Dienst, d.Po, v1!, 18);
        await d.Dienst.GibAbAsync(d.Po, v1!.Id);
        Assert.Null(await d.Dienst.BehalteErgebnisAsync(d.Po));
        var stand = await d.Dienst.GetStandAsync(d.Po, false);
        Assert.Equal(90, stand.Verlauf[0].EndNoteProzent);
        Assert.Equal(FormationRundenStatus.Bestanden, stand.Verlauf[0].Status);

        d.Uhr.Jetzt = d.Uhr.Jetzt.AddMonths(3).AddMinutes(1);
        stand = await d.Dienst.GetStandAsync(d.Po, false);
        Assert.NotNull(stand.Offen);
    }

    [Fact]
    public async Task Fristablauf_Wertet_Angefangenen_Versuch_Oder_Gilt_Als_Verpasst()
    {
        var d = await BasisAsync();
        var (v1, _) = await d.Dienst.StarteVersuchAsync(d.Po, false);
        await d.Dienst.SpeichereAntwortAsync(d.Po, v1!.Id, v1.Fragen[0].Id, [0]);
        d.Uhr.Jetzt = d.Uhr.Jetzt.AddDays(31);
        Assert.NotNull(await d.Dienst.SpeichereAntwortAsync(d.Po, v1.Id, v1.Fragen[1].Id, [0]));
        var stand = await d.Dienst.GetStandAsync(d.Po, false);
        Assert.Equal(FormationRundenStatus.NichtBestanden, stand.Verlauf[0].Status);
        Assert.NotNull(stand.Verlauf[0].EndNoteProzent);

        // Nächste Runde nach 3 Monaten, diesmal nichts getan → « manquée » und Meldung an das Team.
        d.Uhr.Jetzt = d.Uhr.Jetzt.AddMonths(3);
        await d.Dienst.AktualisiereAsync(d.Po);
        d.Uhr.Jetzt = d.Uhr.Jetzt.AddDays(31);
        await d.Dienst.AktualisiereAsync(d.Po);
        stand = await d.Dienst.GetStandAsync(d.Po, false);
        Assert.Equal(FormationRundenStatus.Verpasst, stand.Verlauf[0].Status);

        var meldungen = await d.Dienst.GetOffeneMeldungenAsync();
        Assert.Equal(2, meldungen.Count(m => m.Art == "resultat"));
    }

    [Fact]
    public async Task Meldungen_Ouverte_Und_Rappel_Je_Einmal()
    {
        var d = await BasisAsync();
        await d.Dienst.AktualisiereAsync(d.Po);
        var m = Assert.Single(await d.Dienst.GetOffeneMeldungenAsync());
        Assert.Equal("ouverte", m.Art);
        Assert.Equal(20, m.Fragen);
        await d.Dienst.MarkiereGemeldetAsync(m.RundeId, m.Art);
        Assert.Empty(await d.Dienst.GetOffeneMeldungenAsync());

        d.Uhr.Jetzt = d.Uhr.Jetzt.AddDays(24);
        m = Assert.Single(await d.Dienst.GetOffeneMeldungenAsync());
        Assert.Equal("rappel", m.Art);
        await d.Dienst.MarkiereGemeldetAsync(m.RundeId, m.Art);
        Assert.Empty(await d.Dienst.GetOffeneMeldungenAsync());
    }

    [Fact]
    public async Task Keine_Erinnerung_Wenn_Schon_Bestanden_Abgegeben()
    {
        var d = await BasisAsync();
        var (v1, _) = await d.Dienst.StarteVersuchAsync(d.Po, false);
        await BeantworteAsync(d.Dienst, d.Po, v1!, 20);
        await d.Dienst.GibAbAsync(d.Po, v1!.Id);
        foreach (var m in await d.Dienst.GetOffeneMeldungenAsync()) await d.Dienst.MarkiereGemeldetAsync(m.RundeId, m.Art);
        d.Uhr.Jetzt = d.Uhr.Jetzt.AddDays(25);
        Assert.Empty(await d.Dienst.GetOffeneMeldungenAsync());
    }

    [Fact]
    public async Task Admin_Intervall_Pause_Frist_Erlass_Und_Neue_Runde()
    {
        var d = await BasisAsync();
        await d.Dienst.AktualisiereAsync(d.Po);

        Assert.Null(await d.Dienst.VerlaengereFristAsync(d.Po, d.Uhr.Jetzt.AddDays(45)));
        Assert.Equal(d.Uhr.Jetzt.AddDays(45), (await d.Dienst.GetStandAsync(d.Po, false)).Offen!.FristUtc);

        Assert.Null(await d.Dienst.ErlasseRundeAsync(d.Po, d.Admin, "Formation faite en présentiel"));
        var stand = await d.Dienst.GetStandAsync(d.Po, false);
        Assert.Null(stand.Offen);
        Assert.Equal(FormationRundenStatus.Erlassen, stand.Verlauf[0].Status);
        Assert.Equal("Formation faite en présentiel", stand.Verlauf[0].ErlassenGrund);

        Assert.Null(await d.Dienst.SetzeIntervallAsync(d.Po, 6));
        stand = await d.Dienst.GetStandAsync(d.Po, false);
        Assert.Equal(6, stand.IntervallMonate);
        Assert.Equal(d.Uhr.Jetzt.AddMonths(6), stand.NaechsteRundeAbUtc);
        Assert.NotNull(await d.Dienst.SetzeIntervallAsync(d.Po, 0));

        d.Uhr.Jetzt = d.Uhr.Jetzt.AddDays(1);
        Assert.Null(await d.Dienst.OeffneRundeJetztAsync(d.Po));
        Assert.NotNull((await d.Dienst.GetStandAsync(d.Po, false)).Offen);
        Assert.NotNull(await d.Dienst.OeffneRundeJetztAsync(d.Po));

        // Pause: offene Runde wird erlassen, keine neue Runde bis zum Datum.
        var bis = d.Uhr.Jetzt.AddMonths(8);
        Assert.Null(await d.Dienst.SetzePauseAsync(d.Po, bis, d.Admin));
        d.Uhr.Jetzt = d.Uhr.Jetzt.AddMonths(7);
        stand = await d.Dienst.GetStandAsync(d.Po, false);
        Assert.Null(stand.Offen);
        Assert.Equal(bis, stand.PausiertBisUtc);
        Assert.StartsWith("Pause", stand.Verlauf[0].ErlassenGrund);

        // Pause vorzeitig beenden → Runde sofort.
        Assert.Null(await d.Dienst.SetzePauseAsync(d.Po, null, d.Admin));
        Assert.NotNull((await d.Dienst.GetStandAsync(d.Po, false)).Offen);
    }

    [Fact]
    public async Task Uebersicht_Markiert_Schwache_Und_Sehr_Gute_Responsables()
    {
        var d = await BasisAsync();
        for (var runde = 0; runde < 2; runde++)
        {
            var (v, _) = await d.Dienst.StarteVersuchAsync(d.Po, false);
            await BeantworteAsync(d.Dienst, d.Po, v!, 5);
            await d.Dienst.GibAbAsync(d.Po, v!.Id);
            await d.Dienst.BehalteErgebnisAsync(d.Po);
            d.Uhr.Jetzt = d.Uhr.Jetzt.AddMonths(3).AddDays(1);
        }
        var zeile = Assert.Single(await d.Dienst.GetUebersichtAsync());
        Assert.True(zeile.Schwach);
        Assert.False(zeile.SehrGut);

        for (var runde = 0; runde < 3; runde++)
        {
            var (v, _) = await d.Dienst.StarteVersuchAsync(d.Po, false);
            await BeantworteAsync(d.Dienst, d.Po, v!, 19);
            await d.Dienst.GibAbAsync(d.Po, v!.Id);
            await d.Dienst.BehalteErgebnisAsync(d.Po);
            d.Uhr.Jetzt = d.Uhr.Jetzt.AddMonths(3).AddDays(1);
        }
        zeile = Assert.Single(await d.Dienst.GetUebersichtAsync());
        Assert.False(zeile.Schwach);
        Assert.True(zeile.SehrGut);
    }

    [Fact]
    public async Task Rolle_Entziehen_Beendet_Zuweisungen_Und_Anmeldungen()
    {
        var d = await BasisAsync();
        await d.Dienst.AktualisiereAsync(d.Po);
        var (ok, fehler, laeden) = await d.Dienst.EntzieheRolleAsync(d.Po, d.Admin);
        Assert.True(ok, fehler);
        Assert.Equal(["Boutique A"], laeden);

        await using var db = await d.Factory.CreateDbContextAsync();
        Assert.False(await db.ProduktOwnerSalons.AnyAsync(z => z.ProduktOwnerId == d.Po));
        Assert.Null((await db.Users.FirstAsync(u => u.Id == d.Po)).ActiveSessionId);
        Assert.False(await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                            where ur.UserId == d.Po && r.Name == AppRollen.ProduktOwner select ur).AnyAsync());
        Assert.Equal(FormationRundenStatus.Erlassen, (await db.FormationRunden.SingleAsync(r => r.ProduktOwnerId == d.Po)).Status);
        Assert.Empty(await d.Dienst.GetUebersichtAsync());
        Assert.False((await d.Dienst.EntzieheRolleAsync(d.Po, d.Admin)).Erfolg);
    }

    [Fact]
    public async Task Admin_Trainiert_Frei_Ohne_Frist_Und_Ohne_Grenze_Der_Versuche()
    {
        var d = await BasisAsync();
        for (var i = 0; i < 5; i++)
        {
            var (v, fehler) = await d.Dienst.StarteVersuchAsync(d.Admin, istFrei: true);
            Assert.Null(fehler);
            Assert.Equal(FormationService.OhneFrist, v!.FristUtc);
            await BeantworteAsync(d.Dienst, d.Admin, v, 10);
            var (e, _) = await d.Dienst.GibAbAsync(d.Admin, v.Id);
            Assert.True(e!.IstFrei);
        }
        var stand = await d.Dienst.GetStandAsync(d.Admin, istFrei: true);
        Assert.Equal(5, stand.Verlauf.Count);
        // Nicht in der Übersicht der Responsables, keine Mails.
        Assert.DoesNotContain(await d.Dienst.GetUebersichtAsync(), z => z.PoId == d.Admin);
        Assert.DoesNotContain(await d.Dienst.GetOffeneMeldungenAsync(), m => m.PoId == d.Admin);
    }

    [Fact]
    public async Task Admin_Pfad_Zieht_Nur_Admin_Fragen_Und_Wechsel_Verwirft_Das_Andere_Training()
    {
        var d = await BasisAsync();
        var adminIds = d.Dienst.AdminLektionen.SelectMany(l => l.Pruefung).Select(f => f.Id).ToHashSet();

        var (va, _) = await d.Dienst.StarteVersuchAsync(d.Admin, istFrei: true, adminPfad: true);
        Assert.True(va!.AdminPfad);
        Assert.Equal(20, va.Fragen.Count);
        Assert.All(va.Fragen, f => Assert.Contains(f.Id, adminIds));
        // Gleichmäßig über die 6 Admin-Lektionen (20 Fragen → 3 oder 4 je Lektion).
        var lektionJeFrage = d.Dienst.AdminLektionen.SelectMany(l => l.Pruefung.Select(f => (f.Id, l.Id))).ToDictionary(x => x.Item1, x => x.Item2);
        Assert.All(va.Fragen.GroupBy(f => lektionJeFrage[f.Id]), g => Assert.InRange(g.Count(), 3, 4));

        // Wechsel zum Pfad der Responsables: das angefangene Admin-Training fällt weg.
        var (vp, _) = await d.Dienst.StarteVersuchAsync(d.Admin, istFrei: true, adminPfad: false);
        Assert.False(vp!.AdminPfad);
        Assert.DoesNotContain(vp.Fragen, f => adminIds.Contains(f.Id));
        Assert.Null(await d.Dienst.GetVersuchAsync(d.Admin, va.Id));

        await BeantworteAsync(d.Dienst, d.Admin, vp, 20);
        var (e, _) = await d.Dienst.GibAbAsync(d.Admin, vp.Id);
        Assert.False(e!.AdminPfad);
        var stand = await d.Dienst.GetStandAsync(d.Admin, true);
        Assert.False(Assert.Single(stand.Verlauf).AdminPfad);
    }

    [Fact]
    public async Task Responsable_Pruefung_Enthaelt_Nie_Admin_Fragen_Auch_Mit_AdminPfad()
    {
        var d = await BasisAsync();
        var adminIds = d.Dienst.AdminLektionen.SelectMany(l => l.Pruefung).Select(f => f.Id).ToHashSet();
        // Ein Responsable kann den Admin-Pfad nicht anfordern: echte Runde, Pool der Responsables.
        var (v, _) = await d.Dienst.StarteVersuchAsync(d.Po, istFrei: false, adminPfad: true);
        Assert.False(v!.AdminPfad);
        Assert.DoesNotContain(v.Fragen, f => adminIds.Contains(f.Id));
    }

    [Fact]
    public async Task Fremder_Versuch_Ist_Nicht_Zugaenglich()
    {
        var d = await BasisAsync();
        var (v, _) = await d.Dienst.StarteVersuchAsync(d.Po, false);
        Assert.NotNull(await d.Dienst.SpeichereAntwortAsync(d.Admin, v!.Id, v.Fragen[0].Id, [0]));
        Assert.Null(await d.Dienst.GetVersuchAsync(d.Admin, v.Id));
        Assert.Null((await d.Dienst.GibAbAsync(d.Admin, v.Id)).Ergebnis);
    }

    [Fact]
    public async Task Lektion_Gelesen_Wird_Einmal_Gemerkt()
    {
        var d = await BasisAsync();
        await d.Dienst.MarkiereGelesenAsync(d.Po, "caisse");
        await d.Dienst.MarkiereGelesenAsync(d.Po, "caisse");
        await d.Dienst.MarkiereGelesenAsync(d.Po, "gibt-es-nicht");
        var stand = await d.Dienst.GetStandAsync(d.Po, false);
        Assert.Equal(["caisse"], stand.Gelesen);
    }

    [Fact]
    public async Task Regeln_Werden_Geprueft_Und_Gelten_Fuer_Neue_Runden()
    {
        var d = await BasisAsync();
        Assert.NotNull(await d.Dienst.SpeichereRegelAsync(5, 20, 30, 3));
        Assert.NotNull(await d.Dienst.SpeichereRegelAsync(70, 500, 30, 3));
        Assert.Null(await d.Dienst.SpeichereRegelAsync(80, 10, 14, 6));
        var stand = await d.Dienst.GetStandAsync(d.Po, false);
        Assert.Equal(80, stand.Offen!.Grenze);
        Assert.Equal(10, stand.Offen.FragenAnzahl);
        Assert.Equal(d.Uhr.Jetzt.AddDays(14), stand.Offen.FristUtc);
        Assert.Equal(6, stand.IntervallMonate);
    }
}

/// <summary>Der fest im Code geschriebene Lernpfad bleibt gültig (Nutzer: « immer anpassen, falls sich was geändert hat »).</summary>
public class FormationKatalogTests
{
    private static IEnumerable<Frage> AlleFragen => FormationKatalog.AlleLektionen.SelectMany(l => l.Uebungen.Concat(l.Pruefung));

    [Fact]
    public void Ids_Sind_Eindeutig_Und_Antworten_Gueltig()
    {
        Assert.Equal(FormationKatalog.AlleLektionen.Count(), FormationKatalog.AlleLektionen.Select(l => l.Id).Distinct().Count());
        // Pfad an der ID erkennbar (Admin-Lektionen « admin- », Admin-Fragen « a- »).
        Assert.All(FormationKatalog.AdminLektionen, l => Assert.StartsWith("admin-", l.Id));
        Assert.All(FormationKatalog.AdminLektionen.SelectMany(l => l.Uebungen.Concat(l.Pruefung)), f => Assert.StartsWith("a-", f.Id));
        Assert.All(FormationKatalog.Lektionen.SelectMany(l => l.Uebungen.Concat(l.Pruefung)), f => Assert.False(f.Id.StartsWith("a-")));
        Assert.Equal(AlleFragen.Count(), AlleFragen.Select(f => f.Id).Distinct().Count());
        Assert.All(AlleFragen, f =>
        {
            Assert.InRange(f.Antworten.Count, 2, 6);
            Assert.NotEmpty(f.Richtig);
            Assert.All(f.Richtig, r => Assert.InRange(r, 0, f.Antworten.Count - 1));
            Assert.Equal(f.Richtig.Count, f.Richtig.Distinct().Count());
            Assert.Equal(f.Antworten.Count, f.Antworten.Distinct().Count());
            Assert.False(string.IsNullOrWhiteSpace(f.Erklaerung));
            // Der Hinweis « Plusieurs réponses possibles » kommt von der Seite, nicht aus dem Fragetext.
            Assert.DoesNotContain("plusieurs réponses", f.Text);
        });
    }

    [Fact]
    public void Jede_Lektion_Hat_Inhalt_Uebung_Und_Genug_Pruefungsfragen()
    {
        Assert.All(FormationKatalog.AlleLektionen, l =>
        {
            Assert.NotEmpty(l.Abschnitte);
            Assert.NotEmpty(l.Uebungen);
            Assert.True(l.Pruefung.Count >= 5, $"{l.Id}: zu wenige Prüfungsfragen");
            // « Lesen mit Bildern » (Nutzer): jede Lektion mit mindestens einem Bildschirmfoto der App.
            Assert.Contains(l.Abschnitte, a => a.Art == AbschnittArt.Bild);
        });
        // Drei Versuche à 20 Fragen ohne Wiederholung.
        Assert.True(FormationKatalog.Lektionen.Sum(l => l.Pruefung.Count) >= 60);
        Assert.True(FormationKatalog.AdminLektionen.Sum(l => l.Pruefung.Count) >= 30);
    }

    [Fact]
    public void Videos_Stehen_Im_VideoKatalog_Und_Bilder_Existieren()
    {
        Assert.All(FormationKatalog.AlleLektionen.SelectMany(l => l.Videos), v => Assert.NotNull(VideoKatalog.Finde(v)));
        var wwwroot = Path.Combine(FindeWebApp(), "wwwroot");
        Assert.All(FormationKatalog.AlleLektionen.SelectMany(l => l.Abschnitte).Where(a => a.Art == AbschnittArt.Bild),
            a => Assert.True(File.Exists(Path.Combine(wwwroot, a.Datei!.Replace('/', Path.DirectorySeparatorChar))), a.Datei));
    }

    [Fact]
    public void Videos_Der_Lektionen_Sind_Fuer_Responsables_Sichtbar()
    {
        var po = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, AppRollen.ProduktOwner)], "test"));
        Assert.All(FormationKatalog.Lektionen.SelectMany(l => l.Videos), v => Assert.True(VideoKatalog.Finde(v)!.DarfSehen(po), v));
        var admin = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, AppRollen.SuperAdmin)], "test"));
        Assert.All(FormationKatalog.AdminLektionen.SelectMany(l => l.Videos), v => Assert.True(VideoKatalog.Finde(v)!.DarfSehen(admin), v));
    }

    private static string FindeWebApp()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "eNOTE.WebApp"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "eNOTE.WebApp");
    }
}
