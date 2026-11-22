using ENote.Core.DataModels.Finanzen;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Fehlersuche 21.11.2026: Rechte wurden nur beim Seitenaufbau geprüft. Nach Entzug « Gérer les prestations »,
/// « Gérer les remises » bzw. « Voir les finances », nach dem Entfernen aus dem Laden oder dem Deaktivieren des Ladens
/// speicherte die noch offene Seite weiter. Die Dienste prüfen jetzt bei jeder schreibenden Aktion.
/// </summary>
public class RechteBeiJederAktionTests
{
    private sealed record Welt(IDbContextFactory<ApplicationDbContext> F, Guid Laden, Guid Fremd, Guid Besitzer, Guid Kossi, KatalogService Katalog, AusgabenService Ausgaben);

    private static async Task<Welt> WeltAsync()
    {
        var f = TestDbContextFactory.Create();
        var laden = new Salon { Name = "Rechte " + Guid.NewGuid().ToString("N")[..6], IstAktiv = true, IstFreigegeben = true };
        var fremd = new Salon { Name = "Fremd " + Guid.NewGuid().ToString("N")[..6], IstAktiv = true, IstFreigegeben = true };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Afi Lawson" };
        var kossi = new AppUser { Id = Guid.NewGuid(), FullName = "Kossi Adjo" };
        await using (var db = await f.CreateDbContextAsync())
        {
            var rB = new Role { Name = AppRollen.Besitzer };
            var rK = new Role { Name = AppRollen.Kassierer };
            db.Roles.AddRange(rB, rK);
            db.Users.AddRange(besitzer, kossi);
            db.UserRoles.AddRange(new AppUserRole { UserId = besitzer.Id, RoleId = rB.Id }, new AppUserRole { UserId = kossi.Id, RoleId = rK.Id });
            db.Salons.AddRange(laden, fremd);
            db.UserSalons.AddRange(new UserSalon { AppUserId = besitzer.Id, SalonId = laden.Id }, new UserSalon { AppUserId = kossi.Id, SalonId = laden.Id });
            db.AusgabenKategorien.Add(new AusgabenKategorie { SalonId = laden.Id, Name = "Loyer", IstAktiv = true });
            await db.SaveChangesAsync();
        }
        return new Welt(f, laden.Id, fremd.Id, besitzer.Id, kossi.Id, new KatalogService(f, null!, null!), new AusgabenService(f, null!));
    }

    private static async Task SetzeAsync(Welt w, Action<UserSalon> aenderung)
    {
        await using var db = await w.F.CreateDbContextAsync();
        var us = await db.UserSalons.SingleAsync(u => u.AppUserId == w.Kossi);
        aenderung(us);
        await db.SaveChangesAsync();
    }

    private static Leistung Neu(Welt w, string name) => new() { Id = Guid.Empty, SalonId = w.Laden, Name = name, Preis = 1000 };

    [Fact]
    public async Task Prestation_Nur_Mit_Recht_Und_Nach_Entzug_Nicht_Mehr()
    {
        var w = await WeltAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Katalog.SpeichereLeistungAsync(w.Kossi, Neu(w, "Sans droit"), null));
        await SetzeAsync(w, us => us.KannLeistungenVerwalten = true);
        var l = await w.Katalog.SpeichereLeistungAsync(w.Kossi, Neu(w, "Avec droit"), null);
        await SetzeAsync(w, us => us.KannLeistungenVerwalten = false);   // Besitzer entzieht das Recht, Seite bleibt offen
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Katalog.SpeichereLeistungAsync(w.Kossi, Neu(w, "Après retrait"), null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Katalog.LoescheLeistungAsync(w.Kossi, l.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Katalog.SpeichereKategorieAsync(w.Kossi, w.Laden, "Pirate"));
        // Besitzer darf weiterhin
        Assert.NotEqual(Guid.Empty, (await w.Katalog.SpeichereLeistungAsync(w.Besitzer, Neu(w, "Besitzer"), null)).Id);
    }

    [Fact]
    public async Task Remise_Nach_Entzug_Abgelehnt()
    {
        var w = await WeltAsync();
        await SetzeAsync(w, us => us.KannRabatteVerwalten = true);
        await w.Katalog.SpeichereRabattAsync(w.Kossi, new Rabatt { Id = Guid.Empty, SalonId = w.Laden, Name = "Fidélité", Art = RabattArt.Prozent, Wert = 10, IstAktiv = true });
        await SetzeAsync(w, us => us.KannRabatteVerwalten = false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Katalog.SpeichereRabattAsync(w.Kossi,
            new Rabatt { Id = Guid.Empty, SalonId = w.Laden, Name = "Pirate 90", Art = RabattArt.Prozent, Wert = 90, IstAktiv = true }));
    }

    [Fact]
    public async Task Depense_Nach_Entzug_Finances_Abgelehnt()
    {
        var w = await WeltAsync();
        Guid kategorieId;
        await using (var db = await w.F.CreateDbContextAsync()) kategorieId = (await db.AusgabenKategorien.SingleAsync()).Id;
        await SetzeAsync(w, us => us.KannFinanzenSehen = true);
        var ok = await w.Ausgaben.ErfasseAusgabeAsync(w.Laden, kategorieId, 5000, DateTime.UtcNow.Date, null, w.Kossi, null);
        Assert.True(ok.Erfolg);
        await SetzeAsync(w, us => us.KannFinanzenSehen = false);
        var abgelehnt = await w.Ausgaben.ErfasseAusgabeAsync(w.Laden, kategorieId, 7777, DateTime.UtcNow.Date, null, w.Kossi, null);
        Assert.False(abgelehnt.Erfolg);
        Assert.Equal(LadenZugriff.RechtEntzogenText, abgelehnt.Fehler);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Ausgaben.LoescheAusgabeAsync(w.Kossi, ok.Ausgabe!.Id, w.Laden));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Ausgaben.SpeichereKategorieAsync(w.Kossi, w.Laden, null, "Pirate", true));
    }

    [Fact]
    public async Task Entfernt_Gesperrt_Oder_Laden_Deaktiviert_Heisst_Kein_Speichern()
    {
        var w = await WeltAsync();
        await SetzeAsync(w, us => us.KannLeistungenVerwalten = true);
        await using (var db = await w.F.CreateDbContextAsync())
        {
            (await db.Users.SingleAsync(u => u.Id == w.Kossi)).IsBlocked = true;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Katalog.SpeichereLeistungAsync(w.Kossi, Neu(w, "Gesperrt"), null));

        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.UserSalons.Remove(await db.UserSalons.SingleAsync(u => u.AppUserId == w.Kossi));
            (await db.Users.SingleAsync(u => u.Id == w.Kossi)).IsBlocked = false;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Katalog.SpeichereLeistungAsync(w.Kossi, Neu(w, "Entfernt"), null));

        await using (var db = await w.F.CreateDbContextAsync())
        {
            (await db.Salons.SingleAsync(s => s.Id == w.Laden)).IstAktiv = false;   // Admin deaktiviert den Laden
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Katalog.SpeichereLeistungAsync(w.Besitzer, Neu(w, "Laden aus"), null));
    }

    [Fact]
    public async Task Leistung_Eines_Fremden_Ladens_Laesst_Sich_Nicht_Umhaengen()
    {
        var w = await WeltAsync();
        Leistung fremde;
        await using (var db = await w.F.CreateDbContextAsync())
        {
            fremde = new Leistung { SalonId = w.Fremd, Name = "Fremde", Preis = 1000, IstAktiv = true };
            db.Leistungen.Add(fremde);
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Katalog.SpeichereLeistungAsync(w.Besitzer,
            new Leistung { Id = fremde.Id, SalonId = w.Laden, Name = "Gekapert", Preis = 1 }, null));
    }
}
