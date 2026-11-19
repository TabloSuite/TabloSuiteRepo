using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Recht « Dossiers clients » (Nutzer 19.11.2026, Arztpraxis): ein angestellter Arzt mit eigenem Konto sieht und bearbeitet
/// Kundenkarten und Notizen, während die Kasse am Empfang läuft – ohne das Recht nicht; löschen darf weiter nur der Besitzer.
/// </summary>
public class KundenAktenRechtTests
{
    private static async Task<(IDbContextFactory<ApplicationDbContext> F, Guid Laden, Guid Besitzer, Guid Arzt, Guid UserSalonArzt, Guid Kunde)> WeltAsync()
    {
        var f = TestDbContextFactory.Create();
        var laden = new Salon { Name = "Cabinet", IstAktiv = true, IstFreigegeben = true };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Dr Ama" };
        var arzt = new AppUser { Id = Guid.NewGuid(), FullName = "Dr Kofi" };
        var zuordnungArzt = new UserSalon { AppUser = arzt, Salon = laden, KannSichAnmelden = true };
        await using var db = await f.CreateDbContextAsync();
        var rB = new Role { Name = AppRollen.Besitzer };
        var rK = new Role { Name = AppRollen.Kassierer };
        db.Roles.AddRange(rB, rK);
        db.UserRoles.AddRange(new AppUserRole { UserId = besitzer.Id, RoleId = rB.Id }, new AppUserRole { UserId = arzt.Id, RoleId = rK.Id });
        db.UserSalons.AddRange(new UserSalon { AppUser = besitzer, Salon = laden }, zuordnungArzt);
        await db.SaveChangesAsync();
        var kunde = await new KundenService(f, TimeProvider.System).LegeAnAsync(besitzer.Id, laden.Id, "Patient Mensah", null, false);
        return (f, laden.Id, besitzer.Id, arzt.Id, zuordnungArzt.Id, kunde.Id);
    }

    [Fact]
    public async Task Ohne_Recht_Keine_Kundenliste_Mit_Recht_Liste_Karte_Und_Notiz_Aber_Nie_Loeschen()
    {
        var w = await WeltAsync();
        var kunden = new KundenService(w.F, TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => kunden.SucheListeAsync(w.Arzt, w.Laden, null, 0));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => kunden.GetDetailAsync(w.Arzt, w.Kunde));

        Assert.True(await new TeamService(w.F, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<TeamService>.Instance).SetzeRechteAsync(w.UserSalonArzt, false, false, false, kannKundenAktenFuehren: true));
        var team = await new TeamService(w.F, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<TeamService>.Instance).GetTeamAsync(w.Laden);
        Assert.True(team.Single(m => m.UserId == w.Arzt).KannKundenAktenFuehren);

        var (liste, _) = await kunden.SucheListeAsync(w.Arzt, w.Laden, null, 0);
        Assert.Contains(liste, k => k.Id == w.Kunde);
        await kunden.AktualisiereAsync(w.Arzt, w.Kunde, "Patient Mensah", null, false, "Allergie : pénicilline");
        Assert.Equal("Allergie : pénicilline", (await kunden.GetDetailAsync(w.Arzt, w.Kunde)).Notiz);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => kunden.LoescheAsync(w.Arzt, w.Kunde));
        await kunden.LoescheAsync(w.Besitzer, w.Kunde);
    }
}
