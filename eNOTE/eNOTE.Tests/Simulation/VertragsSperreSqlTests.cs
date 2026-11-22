using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ENote.Tests.Simulation;

/// <summary>
/// Befund B-3 (Klicktest über zwei Jahre, 22.11.2026): Öffneten alle Läden am Monatsanfang gleichzeitig die Kasse, gab
/// <c>VertragsService.SynchronisiereAsync</c> mit <c>Serializable</c> Deadlocks 1205 (Bereichssperren über Ladengrenzen).
/// Jetzt Vertragssperre je Laden (<see cref="VertragsSperre"/>). Auf echter SQL-Server-DB (nur mit ENOTE_SIM=1): viele Läden
/// gleichzeitig, je Laden mehrere Geräte – keine doppelte Periode, kein Fehler, Verbindungen kommen mit READ COMMITTED zurück.
/// </summary>
public class VertragsSperreSqlTests
{
    private sealed class Uhr(DateTime jetzt) : TimeProvider
    {
        public DateTime Jetzt = jetzt;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Jetzt, DateTimeKind.Utc));
    }

    [EnoteSimFact]
    public async Task Alle_Laeden_gleichzeitig_am_Monatsanfang_ohne_doppelte_Periode_und_ohne_Fehler()
    {
        var datenbank = new SimDatenbank("ENOTE_SIM_" + Guid.NewGuid().ToString("N")[..12]);
        await datenbank.AnlegenAsync();
        try
        {
            // eigener Pool (Application Name), damit die Prüfung der Isolationsstufe nur Verbindungen dieses Tests sieht
            var verbindung = new SqlConnectionStringBuilder(datenbank.Verbindung) { ApplicationName = "vertrag-sperre" }.ConnectionString;
            var optionen = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(verbindung, s => s.EnableRetryOnFailure(3)).Options;
            var factory = new PooledDbContextFactory<ApplicationDbContext>(optionen);
            var start = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var uhr = new Uhr(start.AddHours(7.5));

            var rolle = new Role { Name = AppRollen.Besitzer, NormalizedName = "BESITZER" };
            var laeden = Enumerable.Range(1, 12).Select(i => new Salon
            {
                Name = $"Boutique {i:00}", IstFreigegeben = true, IstAktiv = true, Land = "Togo",
                ProbeBisUtc = start.AddTicks(-1), VertragsIntervall = i % 3 == 0 ? AboIntervall.Jaehrlich : AboIntervall.Monatlich
            }).ToList();
            var besitzer = laeden.Select((l, i) => new AppUser
            {
                UserName = $"proprio{i}", NormalizedUserName = $"PROPRIO{i}", FullName = $"Propriétaire {i}", SecurityStamp = Guid.NewGuid().ToString()
            }).ToList();
            await using (var db = await factory.CreateDbContextAsync())
            {
                db.Roles.Add(rolle);
                db.Salons.AddRange(laeden);
                db.Users.AddRange(besitzer);
                for (var i = 0; i < laeden.Count; i++)
                {
                    db.UserRoles.Add(new AppUserRole { UserId = besitzer[i].Id, RoleId = rolle.Id });
                    db.UserSalons.Add(new UserSalon { AppUserId = besitzer[i].Id, SalonId = laeden[i].Id });
                }
                db.MonatsPreisStaende.Add(new MonatsPreisStand { SalonId = null, MonatsPreis = 4000, GueltigAbUtc = start.AddYears(-1) });
                await db.SaveChangesAsync();
            }

            var dienst = new VertragsService(factory, uhr);
            var kasse = new KassenZugangService(factory, uhr);

            // 1) Monatsanfang: je Laden 4 Geräte öffnen die Kasse, dazu der Hintergrunddienst für alle Läden – alles gleichzeitig.
            var aufgaben = new List<Task>();
            foreach (var laden in laeden)
                aufgaben.AddRange(Enumerable.Range(0, 4).Select(_ => Task.Run(() => kasse.PruefeAsync(laden.Id))));
            aufgaben.Add(Task.Run(async () => Assert.Empty(await dienst.SynchronisiereAlleAsync())));
            aufgaben.Add(Task.Run(async () => Assert.Empty(await dienst.SynchronisiereAlleAsync())));
            await Task.WhenAll(aufgaben);

            await using (var db = await factory.CreateDbContextAsync())
            {
                var abos = await db.NutzungsAbos.AsNoTracking().ToListAsync();
                foreach (var laden in laeden)
                {
                    var periode = Assert.Single(abos, a => a.SalonId == laden.Id);
                    Assert.Equal(start, periode.ZeitraumVonUtc);
                    Assert.Equal(laden.VertragsIntervall, periode.Intervall);
                    Assert.Equal(VertragsRegeln.Preis(4000, laden.VertragsIntervall), periode.PreisSnapshot);
                }
            }

            // 2) Einen Monat später: Synchronisierung und Formelwechsel desselben Ladens gleichzeitig – je Laden genau eine
            //    neue Periode für die Monatskunden, keine für die Jahreskunden, und die gewählte Formel geht nicht verloren.
            uhr.Jetzt = start.AddMonths(1).AddHours(7.5);
            var wechsler = laeden[0];
            aufgaben.Clear();
            foreach (var laden in laeden)
                aufgaben.AddRange(Enumerable.Range(0, 3).Select(_ => Task.Run(() => dienst.SynchronisiereAsync(laden.Id))));
            aufgaben.Add(Task.Run(() => dienst.WechsleAsync(besitzer[0].Id, wechsler.Id, AboIntervall.DreiMonate)));
            await Task.WhenAll(aufgaben);

            await using (var db = await factory.CreateDbContextAsync())
            {
                var abos = await db.NutzungsAbos.AsNoTracking().ToListAsync();
                foreach (var laden in laeden)
                {
                    var eigene = abos.Where(a => a.SalonId == laden.Id).OrderBy(a => a.ZeitraumVonUtc).ToList();
                    Assert.Equal(laden.VertragsIntervall == AboIntervall.Jaehrlich ? 1 : 2, eigene.Count);
                    Assert.Equal(eigene.Count, eigene.Select(a => a.ZeitraumVonUtc).Distinct().Count());
                }
                // Der Wechsel kam vor oder nach der neuen Periode an: entweder umgewandelt oder ab der nächsten Periode vorgemerkt.
                var februar = abos.Single(a => a.SalonId == wechsler.Id && a.ZeitraumVonUtc == start.AddMonths(1));
                var salon = await db.Salons.AsNoTracking().SingleAsync(s => s.Id == wechsler.Id);
                Assert.True(februar.Intervall == AboIntervall.DreiMonate
                    ? salon.VertragsIntervall == AboIntervall.DreiMonate && salon.NaechstesVertragsIntervall is null
                    : salon.NaechstesVertragsIntervall == AboIntervall.DreiMonate);

                // 3) Keine Verbindung des Pools läuft mit strengerer Stufe als READ COMMITTED (2).
                var stufen = await db.Database.SqlQueryRaw<short>(
                    "SELECT transaction_isolation_level AS Value FROM sys.dm_exec_sessions WHERE program_name = 'vertrag-sperre'").ToListAsync();
                Assert.NotEmpty(stufen);
                Assert.All(stufen, s => Assert.Equal((short)2, s));
            }
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await datenbank.LoeschenAsync();
        }
    }
}
