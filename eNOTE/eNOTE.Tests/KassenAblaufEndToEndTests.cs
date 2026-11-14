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

        // 8) Zweites Gerät (S2): Verkauf und zweiter Abschluss auf der geschlossenen Sitzung liefern den festen Fehler,
        //    an dem die Kasse erkennt, dass sie ihre Ansicht nachziehen muss (kein Verkauf in geschlossener Sitzung).
        var spaet = await kassen.BucheVerkaufAsync(sitzungId, [new KassenService.VerkaufsPositionInput(prestation.Id, 1, null, false)],
            ZahlungsArt.Karte, gegeben: null, karteBestaetigt: true);
        Assert.False(spaet.Erfolg);
        Assert.Equal(KassenService.KeineOffeneKasseFehler, spaet.Fehler);
        var zweiterAbschluss = await kassen.SchliesseSitzungAsync(sitzungId, 30000m, "Zweites Gerät");
        Assert.False(zweiterAbschluss.Erfolg);
        Assert.Equal(KassenService.KeineOffeneKasseFehler, zweiterAbschluss.Fehler);
        await using var pruefDb2 = await factory.CreateDbContextAsync();
        Assert.Equal(3, await pruefDb2.Verkaeufe.CountAsync(v => v.KassenSitzungId == sitzungId));
        Assert.Equal(25000m, (await pruefDb2.KassenSitzungen.FirstAsync(k => k.Id == sitzungId)).EndbestandGezaehlt);
    }

    [Fact]
    public async Task Preis_aendern_ueberschreibt_den_Bestand_nicht_wenn_dazwischen_verkauft_wurde()
    {
        // Simulation S1: Besitzer öffnet « Modifier » (Formular zeigt Bestand 20), die Kasse verkauft 2 Stück,
        // dann speichert er nur einen neuen Preis – vorher kam die verkaufte Ware dabei « zurück » ins Lager.
        var (factory, salon, besitzer, kassen, katalog) = await BasisAsync();
        var produkt = await katalog.SpeichereLeistungAsync(new Leistung
        {
            Id = Guid.Empty, SalonId = salon.Id, Name = "Huile de coco", Typ = LeistungsTyp.Produit, Preis = 2000,
            HatStueckzahl = true, Stueckzahl = 20, IstAktiv = true
        });
        var formular = (await katalog.GetLeistungAsync(produkt.Id))!;
        var beimOeffnen = formular.Stueckzahl;

        var sitzung = (await kassen.StarteSitzungAsync(salon.Id, besitzer.Id, 0m)).Sitzung!;
        var verkauf = await kassen.BucheVerkaufAsync(sitzung.Id, [new KassenService.VerkaufsPositionInput(produkt.Id, 2, null, false)],
            ZahlungsArt.Karte, gegeben: null, karteBestaetigt: true);
        Assert.True(verkauf.Erfolg, verkauf.Fehler);

        formular.Preis = 2200;
        var gespeichert = await katalog.SpeichereLeistungAsync(formular, default, beimOeffnen);
        Assert.Equal(2200m, gespeichert.Preis);
        Assert.Equal(18m, (await katalog.GetLeistungAsync(produkt.Id))!.Stueckzahl);

        // Lieferung im Formular (+30) während eines weiteren Verkaufs: nur die Änderung zählt.
        formular = (await katalog.GetLeistungAsync(produkt.Id))!;
        beimOeffnen = formular.Stueckzahl;
        await kassen.BucheVerkaufAsync(sitzung.Id, [new KassenService.VerkaufsPositionInput(produkt.Id, 1, null, false)],
            ZahlungsArt.Karte, gegeben: null, karteBestaetigt: true);
        formular.Stueckzahl += 30;
        await katalog.SpeichereLeistungAsync(formular, default, beimOeffnen);
        Assert.Equal(47m, (await katalog.GetLeistungAsync(produkt.Id))!.Stueckzahl);
    }

    [Fact]
    public async Task Ticket_offert_mit_Remise_ueber_dem_Preis_laesst_sich_bar_mit_0_F_buchen()
    {
        // Simulation S1: Festbetrag-Remise (200 F) auf eine Photocopie (25 F) → Ticket 0 F; vorher « Chaque paiement doit avoir un montant positif ».
        var (factory, salon, besitzer, kassen, katalog) = await BasisAsync();
        var kopie = await katalog.SpeichereLeistungAsync(new Leistung { Id = Guid.Empty, SalonId = salon.Id, Name = "Photocopie", Preis = 25, IstAktiv = true });
        var geste = await katalog.SpeichereRabattAsync(new Rabatt { Id = Guid.Empty, SalonId = salon.Id, Name = "Geste commercial", Art = RabattArt.Festbetrag, Wert = 200, IstAktiv = true });
        var sitzung = (await kassen.StarteSitzungAsync(salon.Id, besitzer.Id, 1000m)).Sitzung!;

        var r = await kassen.BucheVerkaufAsync(sitzung.Id, [new KassenService.VerkaufsPositionInput(kopie.Id, 1, geste.Id, false)],
            [new KassenService.ZahlungInput(ZahlungsArt.Bar, Gegeben: 0m)]);

        Assert.True(r.Erfolg, r.Fehler);
        Assert.Equal(0m, r.Verkauf!.GesamtSumme);
        Assert.Equal(25m, r.Verkauf.RabattSumme);
        Assert.Equal(1000m, (await kassen.SchliesseSitzungAsync(sitzung.Id, 1000m, null)).Sitzung!.BareinnahmenSoll);
        Assert.Null(KassenRechner.PruefeZahlungen(0m, [(ZahlungsArt.Bar, 0m)]));
        Assert.NotNull(KassenRechner.PruefeZahlungen(0m, [(ZahlungsArt.Bar, 0m), (ZahlungsArt.Karte, 0m)]));
        Assert.NotNull(KassenRechner.PruefeZahlungen(100m, [(ZahlungsArt.Bar, 0m)]));
    }

    [Fact]
    public async Task Festbetrag_Remise_gilt_einmal_fuer_den_ganzen_Bon_wie_an_der_Kasse_angezeigt()
    {
        // Klick-Simulation S2 (Boutique Yopougon): Kasse zeigte 30.500 − 200 = 30.300 F, gebucht wurden 29.900 F (200 F je Zeile).
        var (factory, salon, besitzer, kassen, katalog) = await BasisAsync();
        var tee = await katalog.SpeichereLeistungAsync(new Leistung { Id = Guid.Empty, SalonId = salon.Id, Name = "Tee-shirt", Preis = 3500, IstAktiv = true });
        var chemise = await katalog.SpeichereLeistungAsync(new Leistung { Id = Guid.Empty, SalonId = salon.Id, Name = "Chemise homme", Preis = 7500, IstAktiv = true });
        var robe = await katalog.SpeichereLeistungAsync(new Leistung { Id = Guid.Empty, SalonId = salon.Id, Name = "Robe en pagne", Preis = 12000, IstAktiv = true });
        var geste = await katalog.SpeichereRabattAsync(new Rabatt { Id = Guid.Empty, SalonId = salon.Id, Name = "Geste commercial", Art = RabattArt.Festbetrag, Wert = 200, IstAktiv = true });
        var sitzung = (await kassen.StarteSitzungAsync(salon.Id, besitzer.Id, 1000m)).Sitzung!;

        var r = await kassen.BucheVerkaufAsync(sitzung.Id,
            [new KassenService.VerkaufsPositionInput(tee.Id, 1, geste.Id, false), new KassenService.VerkaufsPositionInput(chemise.Id, 2, geste.Id, false),
             new KassenService.VerkaufsPositionInput(robe.Id, 1, geste.Id, false)],
            [new KassenService.ZahlungInput(ZahlungsArt.Bar, Gegeben: 30300m)]);

        Assert.True(r.Erfolg, r.Fehler);
        Assert.Equal(30500m, r.Verkauf!.Zwischensumme);
        Assert.Equal(200m, r.Verkauf.RabattSumme);
        Assert.Equal(30300m, r.Verkauf.GesamtSumme);
        Assert.Equal(30300m, r.Verkauf.Positionen.Sum(p => p.ZeilenSumme));
        Assert.Equal(200m, r.Verkauf.Positionen.Sum(p => p.RabattBetrag));
        Assert.Equal(0m, r.Verkauf.Rueckgeld ?? 0m);
        Assert.Equal(31300m, (await kassen.SchliesseSitzungAsync(sitzung.Id, 31300m, null)).Sitzung!.BareinnahmenSoll);
    }
}
