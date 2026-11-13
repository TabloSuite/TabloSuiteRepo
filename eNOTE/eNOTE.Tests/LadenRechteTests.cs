using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Gestohlene Responsable-Zugangsdaten: ein Responsable darf nichts Kritisches abschalten oder kürzen –
/// serverseitig geprüft, abgelaufene Zuweisungen zählen nicht.
/// </summary>
public class LadenRechteTests
{
    /// <summary>Legt ein Konto mit Rolle an (Rolle wird bei Bedarf erzeugt) und liefert die Id.</summary>
    internal static Guid LegeKontoAn(ApplicationDbContext db, string name, string rolle)
    {
        var r = db.Roles.Local.FirstOrDefault(x => x.Name == rolle) ?? db.Roles.FirstOrDefault(x => x.Name == rolle);
        if (r is null)
        {
            r = new Role { Id = Guid.NewGuid(), Name = rolle, NormalizedName = rolle.ToUpperInvariant() };
            db.Roles.Add(r);
        }
        var konto = new AppUser { Id = Guid.NewGuid(), UserName = name, FullName = name, Email = name + "@example.test" };
        db.Users.Add(konto);
        db.UserRoles.Add(new AppUserRole { UserId = konto.Id, RoleId = r.Id });
        return konto.Id;
    }

    private sealed record Welt(IDbContextFactory<ApplicationDbContext> Factory, AdminService Admin,
        Guid AdminId, Guid PoId, Guid AlterPoId, Guid BesitzerId, Guid SalonId);

    private static async Task<Welt> BaueAsync(DateTime? probeBis = null)
    {
        var factory = TestDbContextFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        var salon = new Salon { Name = "Boutique Test", IstAktiv = true, IstFreigegeben = false, ProbeBisUtc = probeBis };
        db.Salons.Add(salon);
        var admin = LegeKontoAn(db, "admin", AppRollen.SuperAdmin);
        var po = LegeKontoAn(db, "responsable", AppRollen.ProduktOwner);
        var alterPo = LegeKontoAn(db, "ancien", AppRollen.ProduktOwner);
        var besitzer = LegeKontoAn(db, "proprietaire", AppRollen.Besitzer);
        db.ProduktOwnerSalons.Add(new ProduktOwnerSalon { ProduktOwnerId = po, SalonId = salon.Id });
        db.ProduktOwnerSalons.Add(new ProduktOwnerSalon { ProduktOwnerId = alterPo, SalonId = salon.Id, GueltigBisUtc = DateTime.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();
        var service = new AdminService(factory, null!, NullLogger<AdminService>.Instance, null!);
        return new Welt(factory, service, admin, po, alterPo, besitzer, salon.Id);
    }

    private static async Task<Salon> LadeAsync(Welt w)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        return await db.Salons.AsNoTracking().SingleAsync(s => s.Id == w.SalonId);
    }

    [Fact]
    public async Task Responsable_darf_freigeben_aber_nicht_entziehen()
    {
        var w = await BaueAsync();

        Assert.True(await w.Admin.SetzeFreigabeAsync(w.PoId, w.SalonId, true));
        Assert.True((await LadeAsync(w)).IstFreigegeben);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Admin.SetzeFreigabeAsync(w.PoId, w.SalonId, false));
        Assert.True((await LadeAsync(w)).IstFreigegeben);
    }

    [Fact]
    public async Task Responsable_kann_Laden_weder_abschalten_noch_einschalten()
    {
        var w = await BaueAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Admin.SetzeAktivAsync(w.PoId, w.SalonId, false));
        Assert.True((await LadeAsync(w)).IstAktiv);

        Assert.True(await w.Admin.SetzeAktivAsync(w.AdminId, w.SalonId, false));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Admin.SetzeAktivAsync(w.PoId, w.SalonId, true));
        Assert.False((await LadeAsync(w)).IstAktiv);
    }

    [Fact]
    public async Task Abgelaufene_Zuweisung_gibt_keine_Rechte_und_keine_Sicht()
    {
        var w = await BaueAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Admin.SetzeFreigabeAsync(w.AlterPoId, w.SalonId, true));
        Assert.Empty(await w.Admin.GetSalonUebersichtFuerAsync(w.AlterPoId, istSuperAdmin: false));
        Assert.Single(await w.Admin.GetSalonUebersichtFuerAsync(w.PoId, istSuperAdmin: false));
    }

    [Fact]
    public async Task Andere_Rollen_duerfen_nichts()
    {
        var w = await BaueAsync(DateTime.UtcNow.AddDays(5));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Admin.SetzeFreigabeAsync(w.BesitzerId, w.SalonId, true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Admin.SetzeProbezeitAsync(w.BesitzerId, w.SalonId, DateTime.UtcNow.Date.AddDays(10)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Admin.MeldeAnfrageAsync(w.BesitzerId, w.SalonId, "Autre demande", null));
    }

    [Fact]
    public async Task Responsable_darf_Probezeit_nur_massvoll_verlaengern()
    {
        var bisher = DateTime.UtcNow.AddDays(5);
        var w = await BaueAsync(bisher);

        // kürzen, entfernen, über 30 Tage hinaus: verboten – nichts ändert sich
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Admin.SetzeProbezeitAsync(w.PoId, w.SalonId, DateTime.UtcNow.Date.AddDays(1)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Admin.SetzeProbezeitAsync(w.PoId, w.SalonId, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Admin.SetzeProbezeitAsync(w.PoId, w.SalonId, DateTime.UtcNow.Date.AddDays(AdminService.PoProbeMaxTage + 1)));
        Assert.Equal(bisher, (await LadeAsync(w)).ProbeBisUtc);

        // verlängern bis zur Grenze: erlaubt
        Assert.True(await w.Admin.SetzeProbezeitAsync(w.PoId, w.SalonId, DateTime.UtcNow.Date.AddDays(AdminService.PoProbeMaxTage)));
        Assert.True((await LadeAsync(w)).ProbeBisUtc > bisher);
    }

    [Fact]
    public async Task Responsable_kann_keine_Probezeit_neu_vergeben()
    {
        var w = await BaueAsync(probeBis: null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Admin.SetzeProbezeitAsync(w.PoId, w.SalonId, DateTime.UtcNow.Date.AddDays(10)));
        Assert.Null((await LadeAsync(w)).ProbeBisUtc);
    }

    [Fact]
    public async Task Admin_darf_Probezeit_frei_aendern_und_entfernen()
    {
        var w = await BaueAsync(DateTime.UtcNow.AddDays(5));

        Assert.True(await w.Admin.SetzeProbezeitAsync(w.AdminId, w.SalonId, DateTime.UtcNow.Date.AddDays(200)));
        Assert.True(await w.Admin.SetzeProbezeitAsync(w.AdminId, w.SalonId, null));
        Assert.Null((await LadeAsync(w)).ProbeBisUtc);
    }
}
