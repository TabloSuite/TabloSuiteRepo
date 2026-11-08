using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services.Kasse;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// End-to-End-Test des Kassen-Ablaufs in der echten Reihenfolge:
/// Anlegen → Kasse öffnen → mehrere Verkäufe → Kasse schließen.
/// </summary>
public class KassenAblaufEndToEndTests
{
    private static async Task<(IDbContextFactory<ApplicationDbContext> factory, Salon salon, AppUser besitzer, KassenService kassen, KatalogService katalog)>
        BasisAsync()
    {
        var factory = TestDbContextFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        var besitzerRolle = new Role { Name = AppRollen.Besitzer };
        var kassiererRolle = new Role { Name = AppRollen.Kassierer };
        db.Roles.AddRange(besitzerRolle, kassiererRolle);

        var besitzer = new AppUser { UserName = "owner", FullName = "Propriétaire Test" };
        db.Users.Add(besitzer);
        await db.SaveChangesAsync();
        db.UserRoles.Add(new AppUserRole { UserId = besitzer.Id, RoleId = besitzerRolle.Id });

        var salon = new Salon { Name = "Salon Test", IstFreigegeben = true, IstAktiv = true };
        db.Salons.Add(salon);
        await db.SaveChangesAsync();

        db.UserSalons.Add(new UserSalon
        {
            AppUserId = besitzer.Id,
            SalonId = salon.Id,
            BesitzerPinHash = KassenPinService.HashPin("1234"),
            KannFinanzenSehen = true,
            KannLeistungenVerwalten = true
        });
        await db.SaveChangesAsync();

        var kassen = new KassenService(factory, new FakeUmsatzBenachrichtigungService());
        var katalog = new KatalogService(factory, null!, null!);
        return (factory, salon, besitzer, kassen, katalog);
    }

    [Fact]
    public async Task KompletterKassenAblauf_Anlegen_Verkauf_Oeffnen_Schliessen()
    {
        var (factory, salon, besitzer, kassen, katalog) = await BasisAsync();

        // 1) ANLEGEN: Prestation (Dienstleistung) anlegen. Explizit Id = Empty für den Anlegen-Pfad.
        var prestation = await katalog.SpeichereLeistungAsync(new Leistung
        {
            Id = Guid.Empty,
            SalonId = salon.Id,
            Name = "Coiffure Enfant",
            Typ = LeistungsTyp.Prestation,
            Preis = 5000,
            IstAktiv = true
        });

        // Prestation hat keinen Kaufpreis, keinen Stock.
        Assert.Equal(LeistungsTyp.Prestation, prestation.Typ);
        Assert.Null(prestation.Kaufpreis);
        Assert.False(prestation.HatStueckzahl);
        Assert.NotNull(prestation.StrichCode); // Barcode automatisch generiert
        Assert.NotEmpty(prestation.StrichCode);

        // 2) KASSE ÖFFNEN: Sitzung mit Startbestand 10.000 F starten.
        var start = await kassen.StarteSitzungAsync(salon.Id, besitzer.Id, 10000m);
        Assert.True(start.Erfolg, start.Fehler);
        Assert.NotNull(start.Sitzung);
        var sitzungId = start.Sitzung!.Id;

        // 3) VERKAUF 1: 1× Prestation (5000 F), Bar, gegeben 5000.
        var verkauf1 = await kassen.BucheVerkaufAsync(
            sitzungId,
            [new KassenService.VerkaufsPositionInput(prestation.Id, 1, null, false)],
            ZahlungsArt.Bar,
            gegeben: 5000m,
            karteBestaetigt: false);
        Assert.True(verkauf1.Erfolg, verkauf1.Fehler);

        // 4) VERKAUF 2: 2× Prestation (10000 F), Bar, gegeben 20000 → Rückgeld 10000.
        var verkauf2 = await kassen.BucheVerkaufAsync(
            sitzungId,
            [new KassenService.VerkaufsPositionInput(prestation.Id, 2, null, false)],
            ZahlungsArt.Bar,
            gegeben: 20000m,
            karteBestaetigt: false);
        Assert.True(verkauf2.Erfolg, verkauf2.Fehler);

        // 5) VERKAUF 3: 1× Prestation (5000 F), Karte (bestätigt).
        var verkauf3 = await kassen.BucheVerkaufAsync(
            sitzungId,
            [new KassenService.VerkaufsPositionInput(prestation.Id, 1, null, false)],
            ZahlungsArt.Karte,
            gegeben: null,
            karteBestaetigt: true);
        Assert.True(verkauf3.Erfolg, verkauf3.Fehler);

        // 6) KASSE SCHLIESSEN: Endbestand 25000 F (10000 Start + 5000 + 10000 Bar; Karte zählt nicht zur Bar-Soll).
        // Bar-Soll = 10000 + 5000 + 10000 = 25000; Karte (5000) ist separat.
        var abschluss = await kassen.SchliesseSitzungAsync(sitzungId, 25000m, "Clôture de test");
        Assert.True(abschluss.Erfolg, abschluss.Fehler);
        Assert.Equal(25000m, abschluss.Sitzung!.BareinnahmenSoll);
        Assert.Equal(25000m, abschluss.Sitzung.EndbestandGezaehlt);
        Assert.Equal(0m, abschluss.Sitzung.Differenz);

        // 7) Prüfen: Sitzung ist abgeschlossen, Verkäufe sind gespeichert.
        await using var pruefDb = await factory.CreateDbContextAsync();
        var sitzungNeu = await pruefDb.KassenSitzungen
            .Include(k => k.Verkaeufe)
            .FirstAsync(k => k.Id == sitzungId);
        Assert.Equal(KassenSitzungStatus.Abgeschlossen, sitzungNeu.Status);
        Assert.Equal(3, sitzungNeu.Verkaeufe.Count);

        // Gesamtumsatz: 5000 + 10000 + 5000 = 20000.
        Assert.Equal(20000m, sitzungNeu.Verkaeufe.Sum(v => v.GesamtSumme));
    }
}
