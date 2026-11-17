using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.Tests.Organisation;
using Skoulou.Tests.Sql;
using Skoulou.WebApp.Components.Layout;
using Skoulou.WebApp.Services.Formation;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Plattform;

/// <summary>
/// Nutzerwunsch 17.11.2026 (wie eNOTE): Formation der Responsables – Lernpfad + Prüfung in Runden (3 Versuche, der letzte zählt),
/// Admin-Aktionen, Entziehen der Rolle mit Neuzuweisung der Schulen, freies Training der Administrateure in beiden Pfaden.
/// </summary>
public class FormationTests
{
    private sealed record Daten(TestUmgebung U, FormationService Dienst, Konto Admin, Konto Po, Konto Po2, Schule Schule);

    private static async Task<Daten> BasisAsync()
    {
        var u = new TestUmgebung();
        await u.SeedAsync();
        var admin = await u.KontoAsync("admin", Rollen.SuperAdmin, email: "admin@exemple.tg");
        var po = await u.KontoAsync("kossi.responsable", Rollen.Responsable, email: "po@exemple.tg");
        var po2 = await u.KontoAsync("ama.responsable", Rollen.Responsable, email: "po2@exemple.tg");
        var schule = await u.SchuleAsync("Collège Horizon", responsable: po.Id);
        var dienst = ActivatorUtilities.CreateInstance<FormationService>(u.Dienste);
        return new(u, dienst, admin, po, po2, schule);
    }

    private static async Task BeantworteAsync(FormationService dienst, Guid kontoId, FormationService.VersuchAnsicht v, int richtig)
    {
        var i = 0;
        foreach (var f in v.Fragen)
        {
            var frage = dienst.Lektionen.Concat(dienst.AdminLektionen).SelectMany(l => l.Pruefung).First(x => x.Id == f.Id);
            var richtigeTexte = frage.Richtig.Select(r => frage.Antworten[r]).ToHashSet();
            var auswahl = i++ < richtig
                ? f.Antworten.Where(a => richtigeTexte.Contains(a.Text)).Select(a => a.Index).ToArray()
                : f.Antworten.Where(a => !richtigeTexte.Contains(a.Text)).Take(1).Select(a => a.Index).ToArray();
            Assert.Null(await dienst.SpeichereAntwortAsync(kontoId, v.Id, f.Id, auswahl));
        }
    }

    [Fact]
    public async Task Responsable_Bekommt_Runde_Drei_Versuche_Der_Letzte_Zaehlt()
    {
        var d = await BasisAsync();
        var stand = await d.Dienst.GetStandAsync(d.Po.Id);
        Assert.False(stand.IstFrei);
        Assert.NotNull(stand.Offen);
        Assert.Equal(d.U.Zeit.Jetzt.UtcDateTime.AddDays(30), stand.Offen!.FristUtc);

        var (v1, _) = await d.Dienst.StarteVersuchAsync(d.Po.Id);
        Assert.Equal(20, v1!.Fragen.Count);
        await BeantworteAsync(d.Dienst, d.Po.Id, v1, 20);
        var (e1, _) = await d.Dienst.GibAbAsync(d.Po.Id, v1.Id);
        Assert.Equal(100, e1!.Prozent);

        var (v2, _) = await d.Dienst.StarteVersuchAsync(d.Po.Id);
        Assert.Empty(v2!.Fragen.Select(f => f.Id).Intersect(v1.Fragen.Select(f => f.Id)));
        await BeantworteAsync(d.Dienst, d.Po.Id, v2, 10);
        await d.Dienst.GibAbAsync(d.Po.Id, v2.Id);
        var (v3, _) = await d.Dienst.StarteVersuchAsync(d.Po.Id);
        await BeantworteAsync(d.Dienst, d.Po.Id, v3!, 13);
        var (e3, _) = await d.Dienst.GibAbAsync(d.Po.Id, v3!.Id);
        Assert.True(e3!.RundeAbgeschlossen);

        stand = await d.Dienst.GetStandAsync(d.Po.Id);
        Assert.Equal(65, Assert.Single(stand.Verlauf).EndNoteProzent);
        Assert.Equal(FormationRundenStatus.NichtBestanden, stand.Verlauf[0].Status);
        Assert.Null((await d.Dienst.StarteVersuchAsync(d.Po.Id)).Versuch);
        await d.U.DisposeAsync();
    }

    [Fact]
    public async Task Nur_Responsables_Und_Admins_Nehmen_Teil()
    {
        var d = await BasisAsync();
        var lehrer = await d.U.KontoAsync("yao.enseignant", Rollen.Lehrer, d.Schule.Id);
        await Assert.ThrowsAsync<Skoulou.Core.Fehler.KeinZugriffFehler>(() => d.Dienst.GetStandAsync(lehrer.Id));
        Assert.True((await d.Dienst.GetStandAsync(d.Admin.Id)).IstFrei);
        await d.U.DisposeAsync();
    }

    [Fact]
    public async Task Admin_Aktionen_Und_Uebersicht()
    {
        var d = await BasisAsync();
        var zeilen = await d.Dienst.GetUebersichtAsync(d.Admin.Id);
        Assert.Equal(2, zeilen.Count);
        Assert.Equal(1, zeilen.Single(z => z.KontoId == d.Po.Id).Schulen);

        Assert.Null(await d.Dienst.ErlasseRundeAsync(d.Admin.Id, d.Po.Id, "Formation en présentiel"));
        Assert.Null(await d.Dienst.SetzeIntervallAsync(d.Admin.Id, d.Po.Id, 6));
        var stand = await d.Dienst.GetStandAsync(d.Po.Id);
        Assert.Null(stand.Offen);
        Assert.Equal(6, stand.IntervallMonate);
        Assert.Equal(FormationRundenStatus.Erlassen, stand.Verlauf[0].Status);

        d.U.Zeit.Vor(TimeSpan.FromDays(1));
        Assert.Null(await d.Dienst.OeffneRundeJetztAsync(d.Admin.Id, d.Po.Id));
        Assert.NotNull((await d.Dienst.GetStandAsync(d.Po.Id)).Offen);
        Assert.Null(await d.Dienst.SetzePauseAsync(d.Admin.Id, d.Po.Id, d.U.Zeit.Jetzt.UtcDateTime.AddMonths(4)));
        Assert.Null((await d.Dienst.GetStandAsync(d.Po.Id)).Offen);

        // Nur der Administrateur
        await Assert.ThrowsAsync<Skoulou.Core.Fehler.KeinZugriffFehler>(() => d.Dienst.GetUebersichtAsync(d.Po.Id));
        await d.U.DisposeAsync();
    }

    [Fact]
    public async Task Rolle_Entziehen_Weist_Schulen_Neu_Zu_Und_Sperrt_Das_Konto()
    {
        var d = await BasisAsync();
        var zweite = await d.U.SchuleAsync("Lycée Étoile", responsable: d.Po.Id);
        await d.Dienst.AktualisiereAsync(d.Po.Id);
        var (schulen, andere) = await d.Dienst.GetNeuzuweisungAsync(d.Admin.Id, d.Po.Id);
        Assert.Equal(2, schulen.Count);
        Assert.Equal(d.Po2.Id, Assert.Single(andere).Id);

        Assert.False((await d.Dienst.EntzieheRolleAsync(d.Admin.Id, d.Po.Id, new Dictionary<Guid, Guid?> { [d.Schule.Id] = d.Po.Id })).Erfolg);
        var (ok, _, ohne) = await d.Dienst.EntzieheRolleAsync(d.Admin.Id, d.Po.Id, new Dictionary<Guid, Guid?> { [d.Schule.Id] = d.Po2.Id, [zweite.Id] = null });
        Assert.True(ok);
        Assert.Equal(["Lycée Étoile"], ohne);

        await using var db = await d.U.Db.CreateDbContextAsync();
        Assert.Equal(d.Po2.Id, (await db.Schulen.SingleAsync(s => s.Id == d.Schule.Id)).ResponsableId);
        Assert.Null((await db.Schulen.SingleAsync(s => s.Id == zweite.Id)).ResponsableId);
        Assert.Equal(KontoStatus.Bloque, (await db.Users.SingleAsync(k => k.Id == d.Po.Id)).Status);
        Assert.Equal(FormationRundenStatus.Erlassen, (await db.FormationRunden.SingleAsync(r => r.KontoId == d.Po.Id)).Status);
        Assert.True(await db.AuditEintraege.AnyAsync(a => a.Aktion == FormationService.AuditRolleEntzogen));
        // Gesperrte Responsables nehmen nicht mehr teil
        Assert.DoesNotContain(await d.Dienst.GetUebersichtAsync(d.Admin.Id), z => z.KontoId == d.Po.Id);
        await d.U.DisposeAsync();
    }

    [Fact]
    public async Task Admin_Trainiert_Frei_In_Beiden_Pfaden()
    {
        var d = await BasisAsync();
        var adminIds = d.Dienst.AdminLektionen.SelectMany(l => l.Pruefung).Select(f => f.Id).ToHashSet();
        var (va, _) = await d.Dienst.StarteVersuchAsync(d.Admin.Id, adminPfad: true);
        Assert.True(va!.AdminPfad);
        Assert.All(va.Fragen, f => Assert.Contains(f.Id, adminIds));
        var (vp, _) = await d.Dienst.StarteVersuchAsync(d.Admin.Id, adminPfad: false);
        Assert.False(vp!.AdminPfad);
        await BeantworteAsync(d.Dienst, d.Admin.Id, vp, 20);
        var (e, _) = await d.Dienst.GibAbAsync(d.Admin.Id, vp.Id);
        Assert.True(e!.IstFrei);
        Assert.DoesNotContain(await d.Dienst.GetOffeneMeldungenAsync(), m => m.KontoId == d.Admin.Id);
        // Ein Responsable bekommt nie den Admin-Pfad
        var (vr, _) = await d.Dienst.StarteVersuchAsync(d.Po.Id, adminPfad: true);
        Assert.DoesNotContain(vr!.Fragen, f => adminIds.Contains(f.Id));
        await d.U.DisposeAsync();
    }

    [Fact]
    public async Task Meldungen_Ouverte_Rappel_Resultat()
    {
        var d = await BasisAsync();
        await d.Dienst.AktualisiereAlleAsync();
        var m = await d.Dienst.GetOffeneMeldungenAsync();
        Assert.Equal(2, m.Count(x => x.Art == "ouverte"));
        foreach (var x in m) await d.Dienst.MarkiereGemeldetAsync(x.RundeId, x.Art);
        d.U.Zeit.Vor(TimeSpan.FromDays(24));
        Assert.Equal(2, (await d.Dienst.GetOffeneMeldungenAsync()).Count(x => x.Art == "rappel"));
        d.U.Zeit.Vor(TimeSpan.FromDays(10));
        await d.Dienst.AktualisiereAlleAsync();
        Assert.Equal(2, (await d.Dienst.GetOffeneMeldungenAsync()).Count(x => x.Art == "resultat"));
        await d.U.DisposeAsync();
    }
}

/// <summary>Der fest im Code geschriebene Lernpfad bleibt gültig (Nutzer: « immer anpassen, falls sich was geändert hat »).</summary>
public class FormationKatalogTests
{
    private static IEnumerable<LernFrage> AlleFragen => FormationKatalog.AlleLektionen.SelectMany(l => l.Uebungen.Concat(l.Pruefung));

    [Fact]
    public void Ids_Antworten_Und_Pfade_Sind_Gueltig()
    {
        Assert.Equal(FormationKatalog.AlleLektionen.Count(), FormationKatalog.AlleLektionen.Select(l => l.Id).Distinct().Count());
        Assert.Equal(AlleFragen.Count(), AlleFragen.Select(f => f.Id).Distinct().Count());
        Assert.All(FormationKatalog.AdminLektionen, l => Assert.StartsWith("admin-", l.Id));
        Assert.All(FormationKatalog.AdminLektionen.SelectMany(l => l.Uebungen.Concat(l.Pruefung)), f => Assert.StartsWith("a-", f.Id));
        Assert.All(FormationKatalog.Lektionen.SelectMany(l => l.Uebungen.Concat(l.Pruefung)), f => Assert.False(f.Id.StartsWith("a-")));
        Assert.All(AlleFragen, f =>
        {
            Assert.InRange(f.Antworten.Count, 2, 6);
            Assert.NotEmpty(f.Richtig);
            Assert.All(f.Richtig, r => Assert.InRange(r, 0, f.Antworten.Count - 1));
            Assert.Equal(f.Richtig.Count, f.Richtig.Distinct().Count());
            Assert.Equal(f.Antworten.Count, f.Antworten.Distinct().Count());
            Assert.False(string.IsNullOrWhiteSpace(f.Erklaerung));
            Assert.DoesNotContain("plusieurs réponses", f.Text);
        });
    }

    [Fact]
    public void Jede_Lektion_Hat_Inhalt_Bild_Uebung_Und_Genug_Fragen()
    {
        Assert.All(FormationKatalog.AlleLektionen, l =>
        {
            Assert.NotEmpty(l.Abschnitte);
            Assert.NotEmpty(l.Uebungen);
            Assert.True(l.Pruefung.Count >= 5, $"{l.Id}: zu wenige Prüfungsfragen");
            Assert.Contains(l.Abschnitte, a => a.Art == LernAbschnittArt.Bild);
        });
        Assert.True(FormationKatalog.Lektionen.Sum(l => l.Pruefung.Count) >= 60);
        Assert.True(FormationKatalog.AdminLektionen.Sum(l => l.Pruefung.Count) >= 30);
    }

    [Fact]
    public void Videos_Und_Bilder_Existieren_Und_Sind_Sichtbar()
    {
        System.Security.Claims.ClaimsPrincipal Als(string rolle) => new(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, rolle)], "test"));
        Assert.All(FormationKatalog.Lektionen.SelectMany(l => l.Videos), v => Assert.True(VideoKatalog.Finde(v)?.DarfSehen(Als(Rollen.Responsable)) == true, v));
        Assert.All(FormationKatalog.AdminLektionen.SelectMany(l => l.Videos), v => Assert.True(VideoKatalog.Finde(v)?.DarfSehen(Als(Rollen.SuperAdmin)) == true, v));
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Skoulou.WebApp"))) dir = dir.Parent;
        var wwwroot = Path.Combine(dir!.FullName, "Skoulou.WebApp", "wwwroot");
        Assert.All(FormationKatalog.AlleLektionen.SelectMany(l => l.Abschnitte).Where(a => a.Art == LernAbschnittArt.Bild),
            a => Assert.True(File.Exists(Path.Combine(wwwroot, a.Datei!.Replace('/', Path.DirectorySeparatorChar))), a.Datei));
    }

    [Fact]
    public void Navigation_Fuehrt_Zur_Formation()
    {
        Assert.Contains(NavKarte.Fuer(Rollen.Responsable, _ => false).SelectMany(g => g.Eintraege), e => e.Href == "/formation");
        Assert.Contains(NavKarte.Fuer(Rollen.SuperAdmin, _ => false).SelectMany(g => g.Eintraege), e => e.Href == "/admin/formation");
        Assert.DoesNotContain(NavKarte.Fuer(Rollen.Direktor, _ => true).SelectMany(g => g.Eintraege), e => e.Href.StartsWith("/formation"));
    }
}

/// <summary>Nur mit SKOULOU_SQL_TESTS=1: die Abfragen der Formation laufen auf SQL Server (eNOTE: zyklisches Include fiel nur dort auf).</summary>
public class FormationSqlTests(SqlDatenbank datenbank) : IClassFixture<SqlDatenbank>
{
    [SqlFact]
    public async Task Abfragen_der_Formation_sind_uebersetzbar()
    {
        var fabrik = new SqlFabrik(datenbank);
        var zeit = new TestZeit();
        var kennung = Guid.NewGuid().ToString("N")[..8];
        Konto admin, po;
        await using (var db = datenbank.Kontext())
        {
            admin = new Konto { Id = Guid.NewGuid(), UserName = "adm" + kennung, NormalizedUserName = "ADM" + kennung.ToUpperInvariant(), Rolle = Rollen.SuperAdmin, SecurityStamp = "x" };
            po = new Konto { Id = Guid.NewGuid(), UserName = "po" + kennung, NormalizedUserName = "PO" + kennung.ToUpperInvariant(), Rolle = Rollen.Responsable, SecurityStamp = "x",
                Email = "po@exemple.tg", NormalizedEmail = "PO@EXEMPLE.TG" };
            db.Users.AddRange(admin, po);
            await db.SaveChangesAsync();
        }
        var dienst = new FormationService(fabrik, new Zugriff(fabrik), new AuditService(zeit), new SitzungsPruefung(fabrik, new MemoryCache(new MemoryCacheOptions())), zeit);
        var (v, _) = await dienst.StarteVersuchAsync(po.Id);
        await dienst.SpeichereAntwortAsync(po.Id, v!.Id, v.Fragen[0].Id, [0]);
        await dienst.GibAbAsync(po.Id, v.Id);
        Assert.NotNull(await dienst.GetErgebnisAsync(po.Id, v.Id));
        Assert.NotNull((await dienst.GetErgebnisFuerAdminAsync(admin.Id, v.Id)).Ergebnis);
        Assert.Contains(await dienst.GetUebersichtAsync(admin.Id), z => z.KontoId == po.Id);
        Assert.NotNull(await dienst.GetOffeneMeldungenAsync());
        Assert.NotNull((await dienst.GetStandAsync(po.Id)).Verlauf);
    }
}
