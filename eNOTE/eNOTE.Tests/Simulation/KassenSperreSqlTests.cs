using ENote.Core.DataModels.Finanzen;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services.Kasse;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ENote.Tests.Simulation;

/// <summary>
/// W3 (offene Punkte der Klick-Simulation S2) auf echter SQL-Server-DB (nur mit ENOTE_SIM=1): Die Kassensperre gilt
/// jetzt für alle Kassen- und Lagerbuchungen eines Ladens – Perte, Retrait (Ware und Bargeld), Einlage in die Kasse,
/// « Enregistrer sans encaisser », Erstattung (ins Lager, bar) und Kassenstart –, nicht nur für Verkauf und Abschluss.
/// Zwei Geräte desselben Ladens verlieren dadurch keine Buchung.
/// </summary>
public class KassenSperreSqlTests
{
    private sealed record Welt(IDbContextFactory<ApplicationDbContext> F, Salon Salon, AppUser Besitzer, Leistung Savon, Kunde Kunde,
        KassenService Kasse, BestandsService Bestand, StornoService Storno, AufenthaltService Aufenthalt, AnwesenheitsSignal Signal);

    private static async Task<Welt> WeltAsync(SimDatenbank datenbank)
    {
        // wie die App: SQL Server mit Wiederholungsstrategie; der TOP-ohne-ORDER-BY-Hinweis wird hier zum Fehler.
        var optionen = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(datenbank.Verbindung, s => s.EnableRetryOnFailure(3))
            .ConfigureWarnings(w => w.Throw(CoreEventId.RowLimitingOperationWithoutOrderByWarning)).Options;
        var factory = new PooledDbContextFactory<ApplicationDbContext>(optionen);
        var rolle = new Role { Name = AppRollen.Besitzer, NormalizedName = "BESITZER" };
        var besitzer = new AppUser { UserName = "proprio.sql", NormalizedUserName = "PROPRIO.SQL", FullName = "Propriétaire SQL", SecurityStamp = Guid.NewGuid().ToString() };
        var salon = new Salon { Name = "Superette SQL", IstFreigegeben = true, IstAktiv = true, Land = "Togo" };
        var savon = new Leistung { SalonId = salon.Id, Name = "Savon", Typ = LeistungsTyp.Produit, Preis = 400, IstAktiv = true, HatStueckzahl = true, Stueckzahl = 200 };
        var kunde = new Kunde { SalonId = salon.Id, Name = "Kossi Patient" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Roles.Add(rolle);
            db.Users.Add(besitzer);
            db.UserRoles.Add(new AppUserRole { UserId = besitzer.Id, RoleId = rolle.Id });
            db.Salons.Add(salon);
            db.Leistungen.Add(savon);
            db.UserSalons.Add(new UserSalon { AppUserId = besitzer.Id, SalonId = salon.Id, KassenPinHash = KassenPinService.HashPin("2001"), KannLeistungenVerwalten = true });
            db.Kunden.Add(kunde);
            db.KundenBesuche.Add(new KundenBesuch { SalonId = salon.Id, KundeId = kunde.Id, Grund = "Hospitalisation", ErfasstVonName = "Accueil" });
            await db.SaveChangesAsync();
        }
        var signal = new AnwesenheitsSignal();
        var umsatz = new FakeUmsatzBenachrichtigungService();
        var kasse = new KassenService(factory, umsatz, null, signal);
        return new Welt(factory, salon, besitzer, savon, kunde, kasse, new BestandsService(factory, kasse, signal),
            new StornoService(factory, kasse, umsatz, null, signal), new AufenthaltService(factory, TimeProvider.System, signal, kasse), signal);
    }

    [EnoteSimFact]
    public async Task Alle_Kassen_und_Lagerbuchungen_zweier_Geraete_verlieren_nichts()
    {
        var datenbank = new SimDatenbank("ENOTE_SIM_" + Guid.NewGuid().ToString("N")[..12]);
        await datenbank.AnlegenAsync();
        try
        {
            var w = await WeltAsync(datenbank);
            var meldungen = 0;
            w.Signal.KasseGeaendert += id => { if (id == w.Salon.Id) Interlocked.Increment(ref meldungen); };

            // Kassenstart auf zwei Geräten gleichzeitig: genau eine offene Kasse.
            var starts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => w.Kasse.StarteSitzungAsync(w.Salon.Id, w.Besitzer.Id, 10000m))));
            Assert.Equal(1, starts.Count(s => s.Erfolg));
            var sitzungId = starts.Single(s => s.Erfolg).Sitzung!.Id;

            Task<(bool Erfolg, string? Fehler, Verkauf? Verkauf, List<string> Warnungen)> Verkaufe()
                => Task.Run(() => w.Kasse.BucheVerkaufAsync(sitzungId, [new KassenService.VerkaufsPositionInput(w.Savon.Id, 1, null, false)],
                    ZahlungsArt.Bar, gegeben: 400m, karteBestaetigt: false));
            var vorher = new List<Verkauf>();
            for (var i = 0; i < 10; i++) vorher.Add((await Verkaufe()).Verkauf!);

            // 1) Bestand: 20 Verkäufe, 20 Pertes, 10 Retraits (Ware), 10× « Enregistrer sans encaisser », 10 Erstattungen ins Lager – gleichzeitig.
            var aufgaben = new List<Task<bool>>();
            aufgaben.AddRange(Enumerable.Range(0, 20).Select(async _ => (await Verkaufe()).Erfolg));
            aufgaben.AddRange(Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
                (await w.Bestand.SchreibeAbAsync(w.Besitzer.Id, w.Savon.Id, 1, AbschreibungsGrund.Casse, null)).Erfolg)));
            aufgaben.AddRange(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
                (await w.Bestand.EntnehmeWareAsync(w.Besitzer.Id, sitzungId, null, w.Savon.Id, 1, null)).Erfolg)));
            aufgaben.AddRange(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
                (await w.Aufenthalt.ErfasseAsync(w.Besitzer.Id, w.Salon.Id, w.Kunde.Id, [new AufenthaltService.PositionInput(w.Savon.Id, 1)])).Zeilen == 1)));
            aufgaben.AddRange(vorher.Select(v => Task.Run(async () =>
                (await w.Storno.ErstatteAsync(w.Besitzer.Id, sitzungId, v.Id, [new StornoService.StornoPositionInput(v.Positionen.Single().Id, 1)],
                    $"{(int)ZahlungsArt.Bar}:", zurueckInsLager: true, "Erreur de caisse", null)).Erfolg)));
            Assert.All(await Task.WhenAll(aufgaben), Assert.True);
            await using (var db = await w.F.CreateDbContextAsync())
                Assert.Equal(200m - 10 - 20 - 20 - 10 - 10 + 10, (await db.Leistungen.SingleAsync(l => l.Id == w.Savon.Id)).Stueckzahl);

            // 2) Bargeld: Soll 10.000 + 30 × 400 − 10 × 400 = 18.000 F. Zwei Geräte entnehmen gleichzeitig je 10.000 F → nur eines.
            Assert.Equal(18000m, await w.Kasse.GetBarSollAsync(sitzungId));
            var entnahmen = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => w.Bestand.EntnehmeBargeldAsync(w.Besitzer.Id, sitzungId, null, 10000m, null))));
            Assert.Equal(1, entnahmen.Count(e => e.Erfolg));
            Assert.Contains("attendus en caisse", entnahmen.Single(e => !e.Erfolg).Fehler);

            // Derselbe Bon wird auf zwei Geräten gleichzeitig erstattet → nur einmal.
            await using (var db = await w.F.CreateDbContextAsync())
            {
                var bon = await db.Verkaeufe.Include(v => v.Positionen)
                    .Where(v => v.KassenSitzungId == sitzungId && v.StornoVonVerkaufId == null && !db.Verkaeufe.Any(s => s.StornoVonVerkaufId == v.Id))
                    .OrderBy(v => v.ZeitUtc).FirstAsync();
                var doppelt = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => w.Storno.ErstatteAsync(w.Besitzer.Id, sitzungId, bon.Id,
                    [new StornoService.StornoPositionInput(bon.Positionen.Single().Id, 1)], $"{(int)ZahlungsArt.Bar}:", false, "Client mécontent", null))));
                Assert.Equal(1, doppelt.Count(e => e.Erfolg));
            }

            // 3) Abschluss mitten in Einlagen und Entnahmen: alles, was gebucht ist, steht im Soll; danach bucht nichts mehr.
            var bewegungen = new List<Task<BestandsService.Ergebnis>>();
            bewegungen.AddRange(Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
                w.Bestand.ErfasseEinlageAsync(w.Besitzer.Id, w.Salon.Id, 100m, GeldOrt.Kasse, "Aide familiale", null))));
            bewegungen.AddRange(Enumerable.Range(0, 10).Select(_ => Task.Run(() => w.Bestand.EntnehmeBargeldAsync(w.Besitzer.Id, sitzungId, null, 50m, null))));
            var abschluss = Task.Run(() => w.Kasse.SchliesseSitzungAsync(sitzungId, 0m, null));
            await Task.WhenAll(bewegungen);
            var geschlossen = await abschluss;
            Assert.True(geschlossen.Erfolg, geschlossen.Fehler);
            await using (var db = await w.F.CreateDbContextAsync())
            {
                var sitzung = await db.KassenSitzungen.Include(k => k.Verkaeufe).ThenInclude(v => v.Zahlungen).SingleAsync(k => k.Id == sitzungId);
                var geld = await db.GeldBewegungen.Where(g => g.KassenSitzungId == sitzungId && g.Ort == GeldOrt.Kasse).ToListAsync();
                Assert.Equal(KassenRechner.BerechneBarSoll(sitzung.Startbestand, sitzung.Verkaeufe, geld), sitzung.BareinnahmenSoll);
                Assert.Equal(bewegungen.Count(b => b.Result.Erfolg) + 1, geld.Count); // + die Entnahme aus 2)
                Assert.False(await db.GeldBewegungen.AnyAsync(g => g.KassenSitzungId == null && g.Ort == GeldOrt.Kasse));
            }
            Assert.True(meldungen > 0); // Kassen der anderen Geräte hören « KasseGeaendert »

            // 4) Kundensuche mit fester Reihenfolge (EF-Hinweis wäre hier ein Fehler).
            var kunden = new KundenService(w.F, TimeProvider.System);
            Assert.Equal("Kossi Patient", Assert.Single(await kunden.SucheAsync(w.Besitzer.Id, w.Salon.Id, "kossi")).Name);
        }
        finally
        {
            await datenbank.LoeschenAsync();
        }
    }
}
