using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ENote.Tests.Simulation;

/// <summary>
/// Testabend 18.11.2026 (fataler Fehler 1): Poa legt einen Laden an, der Admin gibt ihn an Pob, danach zurück an Poa →
/// « Cannot insert duplicate key … IX_ProduktOwnerSalons_ProduktOwnerId_SalonId », die Blazor-Verbindung brach ab.
/// Auf echter SQL-Server-DB (nur mit ENOTE_SIM=1), weil nur dort der gefilterte eindeutige Index greift.
/// </summary>
public class PoZuweisungSqlTests
{
    [EnoteSimFact]
    public async Task Laden_kann_zu_einem_frueheren_Responsable_zurueck_und_Decocher_archiviert()
    {
        var datenbank = new SimDatenbank("ENOTE_SIM_" + Guid.NewGuid().ToString("N")[..12]);
        await datenbank.AnlegenAsync();
        try
        {
            var optionen = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(datenbank.Verbindung, s => s.EnableRetryOnFailure(3)).Options;
            var factory = new PooledDbContextFactory<ApplicationDbContext>(optionen);
            var poa = new AppUser { UserName = "poa", NormalizedUserName = "POA", FullName = "Poa", SecurityStamp = Guid.NewGuid().ToString() };
            var pob = new AppUser { UserName = "pob", NormalizedUserName = "POB", FullName = "Pob", SecurityStamp = Guid.NewGuid().ToString() };
            var salon = new Salon { Name = "Boutique Testabend", IstFreigegeben = true, IstAktiv = true, Land = "Togo" };
            await using (var db = await factory.CreateDbContextAsync())
            {
                db.Users.AddRange(poa, pob);
                db.Salons.Add(salon);
                await db.SaveChangesAsync();
            }

            var dienst = new ProduktOwnerService(factory);
            await dienst.WeiseSalonZuAsync(poa.Id, salon.Id, true, false, selbstAngelegt: true); // Poa hat ihn angelegt
            await dienst.WeiseSalonZuAsync(pob.Id, salon.Id, true, false);                      // Admin gibt ihn an Pob
            await dienst.WeiseSalonZuAsync(poa.Id, salon.Id, true, false);                      // … und zurück an Poa (vorher: Absturz)
            Assert.Equal(salon.Id, Assert.Single(await dienst.GetZuweisungenAsync(poa.Id)).SalonId);
            Assert.Empty(await dienst.GetZuweisungenAsync(pob.Id));

            // « Décocher » und wieder « Cocher » beim selben PO: archiviert, danach neu aktiv.
            var aktiv = Assert.Single(await dienst.GetZuweisungenAsync(poa.Id));
            Assert.True(await dienst.EntferneZuweisungAsync(aktiv.ZuweisungId));
            Assert.Empty(await dienst.GetZuweisungenAsync(poa.Id));
            await dienst.WeiseSalonZuAsync(poa.Id, salon.Id, true, false);
            Assert.Single(await dienst.GetZuweisungenAsync(poa.Id));

            await using (var db = await factory.CreateDbContextAsync())
            {
                var alle = await db.ProduktOwnerSalons.Where(p => p.SalonId == salon.Id).ToListAsync();
                Assert.Equal(4, alle.Count);                                   // Verlauf bleibt erhalten
                Assert.Single(alle, p => p.GueltigBisUtc == null);             // genau ein aktiver Responsable
                Assert.Equal(3, alle.Count(p => p.ProduktOwnerId == poa.Id));
            }
        }
        finally
        {
            await datenbank.LoeschenAsync();
        }
    }
}
