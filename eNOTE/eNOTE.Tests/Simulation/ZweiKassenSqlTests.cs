using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ENote.Tests.Simulation;

/// <summary>
/// Klick-Simulation S2, zwei Geräte desselben Ladens auf echter SQL-Server-DB (nur mit ENOTE_SIM=1): gleichzeitige
/// Verkäufe desselben Lagerartikels verloren Bestandsabzüge (Savon: −15 erwartet, −14 gebucht), und ein Verkauf während
/// des Abschlusses durfte nicht im geschlossenen Kassenstand fehlen.
/// </summary>
public class ZweiKassenSqlTests
{
    [EnoteSimFact]
    public async Task Gleichzeitige_Verkaeufe_und_Abschluss_verlieren_keinen_Bestand_und_kein_Bargeld()
    {
        var datenbank = new SimDatenbank("ENOTE_SIM_" + Guid.NewGuid().ToString("N")[..12]);
        await datenbank.AnlegenAsync();
        try
        {
            // wie die App: SQL Server mit Wiederholungsstrategie
            var optionen = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(datenbank.Verbindung, s => s.EnableRetryOnFailure(3)).Options;
            var factory = new PooledDbContextFactory<ApplicationDbContext>(optionen);
            var kassierer = new AppUser { UserName = "kasse.sql", NormalizedUserName = "KASSE.SQL", FullName = "Kassière SQL", SecurityStamp = Guid.NewGuid().ToString() };
            var salon = new Salon { Name = "Superette SQL", IstFreigegeben = true, IstAktiv = true };
            var savon = new Leistung { SalonId = salon.Id, Name = "Savon", Typ = LeistungsTyp.Produit, Preis = 400, IstAktiv = true, HatStueckzahl = true, Stueckzahl = 100 };
            await using (var db = await factory.CreateDbContextAsync())
            {
                db.Users.Add(kassierer);
                db.Salons.Add(salon);
                db.Leistungen.Add(savon);
                db.UserSalons.Add(new UserSalon { AppUserId = kassierer.Id, SalonId = salon.Id, KassenPinHash = ENote.Core.Services.Kasse.KassenPinService.HashPin("2001") });
                await db.SaveChangesAsync();
            }

            var kassen = new KassenService(factory, new FakeUmsatzBenachrichtigungService());
            var start = await kassen.StarteSitzungAsync(salon.Id, kassierer.Id, 10000m);
            Assert.True(start.Erfolg, start.Fehler);
            var sitzungId = start.Sitzung!.Id;
            Task<(bool Erfolg, string? Fehler, Verkauf? Verkauf, List<string> Warnungen)> Verkaufe()
                => Task.Run(() => kassen.BucheVerkaufAsync(sitzungId, [new KassenService.VerkaufsPositionInput(savon.Id, 1, null, false)],
                    ZahlungsArt.Bar, gegeben: 400m, karteBestaetigt: false));

            // 1) 40 gleichzeitige Verkäufe (zwei Geräte × 20): jeder Abzug zählt.
            var erste = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Verkaufe()));
            Assert.All(erste, e => Assert.True(e.Erfolg, e.Fehler));
            await using (var db = await factory.CreateDbContextAsync())
                Assert.Equal(60m, (await db.Leistungen.SingleAsync(l => l.Id == savon.Id)).Stueckzahl);

            // 2) Abschluss mitten in 20 weiteren Verkäufen: was gebucht ist, steht im Soll; danach bucht nichts mehr.
            var weitere = Enumerable.Range(0, 20).Select(_ => Verkaufe()).ToList();
            var abschluss = Task.Run(() => kassen.SchliesseSitzungAsync(sitzungId, 0m, null));
            await Task.WhenAll(weitere);
            var geschlossen = await abschluss;
            Assert.True(geschlossen.Erfolg, geschlossen.Fehler);
            var erfolgreich = weitere.Count(t => t.Result.Erfolg);
            Assert.All(weitere.Where(t => !t.Result.Erfolg), t => Assert.Equal(KassenService.KeineOffeneKasseFehler, t.Result.Fehler));

            await using (var db = await factory.CreateDbContextAsync())
            {
                var sitzung = await db.KassenSitzungen.SingleAsync(k => k.Id == sitzungId);
                var verkaeufe = await db.Verkaeufe.CountAsync(v => v.KassenSitzungId == sitzungId);
                Assert.Equal(40 + erfolgreich, verkaeufe);
                Assert.Equal(10000m + 400m * verkaeufe, sitzung.BareinnahmenSoll);
                Assert.Equal(100m - verkaeufe, (await db.Leistungen.SingleAsync(l => l.Id == savon.Id)).Stueckzahl);
            }
        }
        finally
        {
            await datenbank.LoeschenAsync();
        }
    }
}
