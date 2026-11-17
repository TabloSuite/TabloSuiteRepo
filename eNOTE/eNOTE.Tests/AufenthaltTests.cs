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
/// « Enregistrer sans encaisser »: Leistungen eines anwesenden Kunden über mehrere Tage fest erfassen und beim Gehen alles
/// zusammen kassieren (Praxis mit Übernachtung). Kopie von Name/Preis/Tag, Lager sofort, kein doppelter Abzug, Rücknahme mit Grund/Freigabe,
/// Départ/Löschen gesperrt, solange etwas offen ist.
/// </summary>
public class AufenthaltTests
{
    private sealed class Uhr(DateTime jetzt) : TimeProvider
    {
        public DateTime Jetzt { get; set; } = jetzt;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Jetzt, DateTimeKind.Utc));
    }

    private sealed record Welt(IDbContextFactory<ApplicationDbContext> F, Salon Laden, Guid Besitzer, Guid Awa, Guid Kofi, Guid Fremder,
        KassenSitzung Sitzung, Leistung Consultation, Leistung Injection, Leistung Paracetamol, Leistung Divers, Guid Kunde, Uhr Uhr,
        AnwesenheitsSignal Signal, KundenBesuchService Besuche, AufenthaltService Konto, KassenService Kasse);

    private static async Task<Welt> BasisAsync(bool besitzerPin = false)
    {
        var f = TestDbContextFactory.Create();
        var laden = new Salon { Name = "Clinique Espoir", IstAktiv = true, IstFreigegeben = true };
        var fremd = new Salon { Name = "Autre", IstAktiv = true, IstFreigegeben = true };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Dr Ama Mensah" };
        var awa = new AppUser { Id = Guid.NewGuid(), FullName = "Awa Koffi" };
        var kofi = new AppUser { Id = Guid.NewGuid(), FullName = "Kofi Agbeko" };
        var fremder = new AppUser { Id = Guid.NewGuid(), FullName = "Fremd" };
        var consultation = new Leistung { SalonId = laden.Id, Name = "Consultation", Preis = 5000m, IstAktiv = true };
        var injection = new Leistung { SalonId = laden.Id, Name = "Injection", Preis = 1500m, IstAktiv = true };
        var paracetamol = new Leistung { SalonId = laden.Id, Name = "Paracétamol 500 mg", Preis = 1000m, IstAktiv = true, Typ = LeistungsTyp.Produit, HatStueckzahl = true, Stueckzahl = 10 };
        var divers = new Leistung { SalonId = laden.Id, Name = "Divers", Preis = 0m, IstAktiv = true, IstStandardArtikel = true };
        var kunde = new Kunde { SalonId = laden.Id, Name = "Yao Patient" };
        await using (var db = await f.CreateDbContextAsync())
        {
            var rB = new Role { Name = AppRollen.Besitzer };
            var rK = new Role { Name = AppRollen.Kassierer };
            db.Roles.AddRange(rB, rK);
            db.Users.AddRange(besitzer, awa, kofi, fremder);
            db.UserRoles.AddRange(new AppUserRole { UserId = besitzer.Id, RoleId = rB.Id }, new AppUserRole { UserId = awa.Id, RoleId = rK.Id },
                new AppUserRole { UserId = kofi.Id, RoleId = rK.Id }, new AppUserRole { UserId = fremder.Id, RoleId = rB.Id });
            db.Salons.AddRange(laden, fremd);
            db.UserSalons.AddRange(
                new UserSalon { AppUserId = besitzer.Id, SalonId = laden.Id, BesitzerPinHash = besitzerPin ? KassenPinService.HashPin("4826") : null },
                new UserSalon { AppUserId = awa.Id, SalonId = laden.Id },
                new UserSalon { AppUserId = kofi.Id, SalonId = laden.Id }, new UserSalon { AppUserId = fremder.Id, SalonId = fremd.Id });
            db.Leistungen.AddRange(consultation, injection, paracetamol, divers);
            db.Kunden.Add(kunde);
            await db.SaveChangesAsync();
        }
        KassenSitzung sitzung;
        await using (var db = await f.CreateDbContextAsync())
        {
            sitzung = new KassenSitzung { SalonId = laden.Id, KassiererId = awa.Id, Status = KassenSitzungStatus.Offen };
            db.KassenSitzungen.Add(sitzung);
            await db.SaveChangesAsync();
        }
        var uhr = new Uhr(new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc));
        var signal = new AnwesenheitsSignal();
        var kasse = new KassenService(f, new FakeUmsatzBenachrichtigungService(), uhr, signal);
        return new Welt(f, laden, besitzer.Id, awa.Id, kofi.Id, fremder.Id, sitzung, consultation, injection, paracetamol, divers, kunde.Id, uhr, signal,
            new KundenBesuchService(f, uhr, signal), new AufenthaltService(f, uhr, signal, kasse), kasse);
    }

    private static async Task<Welt> MitAnkunftAsync(bool besitzerPin = false)
    {
        var w = await BasisAsync(besitzerPin);
        await w.Besuche.AnkunftAsync(w.Awa, w.Laden.Id, w.Kunde, "Hospitalisation", "Chambre 2");
        return w;
    }

    private static async Task<decimal> BestandAsync(Welt w, Guid leistungId)
    {
        await using var db = await w.F.CreateDbContextAsync();
        return await db.Leistungen.Where(l => l.Id == leistungId).Select(l => l.Stueckzahl).SingleAsync();
    }

    private static List<KassenService.VerkaufsPositionInput> Alle(AufenthaltService.Konto konto, params KassenService.VerkaufsPositionInput[] neue)
        => konto.Offene.Select(z => new KassenService.VerkaufsPositionInput(z.LeistungId!.Value, z.Menge, null, false, null, z.Id)).Concat(neue).ToList();

    [Fact]
    public async Task Erfassen_Nur_Fuer_Anwesende_Kunden()
    {
        var w = await BasisAsync();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => w.Konto.ErfasseAsync(w.Awa, w.Laden.Id, w.Kunde, [new(w.Consultation.Id, 1)]));
        Assert.Contains("pas enregistré comme présent", ex.Message);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Konto.ErfasseAsync(w.Fremder, w.Laden.Id, w.Kunde, [new(w.Consultation.Id, 1)]));
    }

    [Fact]
    public async Task Mehrere_Tage_Erfassen_Und_Am_Ende_Alles_Zusammen_Kassieren()
    {
        var w = await MitAnkunftAsync();
        var meldungen = 0;
        w.Signal.Geaendert += id => { if (id == w.Laden.Id) meldungen++; };

        // Tag 1: Consultation + 2 Tabletten (Lager sofort)
        var tag1 = await w.Konto.ErfasseAsync(w.Awa, w.Laden.Id, w.Kunde, [new(w.Consultation.Id, 1), new(w.Paracetamol.Id, 2)]);
        Assert.Equal(new AufenthaltService.Stand(3, 7000m), tag1.Gesamt);
        Assert.Equal(8, await BestandAsync(w, w.Paracetamol.Id));
        Assert.True(meldungen > 0);

        // Tag 2 (nach > 12 h kein « départ oublié », weil Leistungen erfasst sind); Preis ändert sich danach
        w.Uhr.Jetzt = w.Uhr.Jetzt.AddDays(1);
        await w.Konto.ErfasseAsync(w.Kofi, w.Laden.Id, w.Kunde, [new(w.Injection.Id, 2)]);
        await using (var db = await w.F.CreateDbContextAsync())
        {
            (await db.Leistungen.SingleAsync(l => l.Id == w.Injection.Id)).Preis = 2000m;
            await db.SaveChangesAsync();
        }
        var anwesend = Assert.Single(await w.Besuche.GetAnwesendeAsync(w.Besitzer, w.Laden.Id));
        Assert.False(anwesend.Vergessen);
        Assert.Equal(new AufenthaltService.Stand(5, 10000m), anwesend.Offen);

        var konto = await w.Konto.GetKontoAsync(w.Besitzer, w.Laden.Id, w.Kunde);
        Assert.Equal([new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 12)], konto.Zeilen.Select(z => z.ErbrachtAm));
        Assert.Equal(("Kofi Agbeko", 1500m), (konto.Zeilen[2].ErfasstVon, konto.Zeilen[2].EinzelPreis));
        Assert.Equal(10000m, konto.Summe);

        // Tag 3: Départ ohne Zahlung gesperrt; Kassieren mit allen erfassten + neuer Leistung
        w.Uhr.Jetzt = w.Uhr.Jetzt.AddDays(1);
        var besuchId = konto.BesuchId!.Value;
        var gesperrt = await Assert.ThrowsAsync<ArgumentException>(() => w.Besuche.BeendeAsync(w.Awa, besuchId));
        Assert.Contains("5 prestation(s)", gesperrt.Message);

        var (ok, fehler, verkauf, _) = await w.Kasse.BucheVerkaufAsync(w.Sitzung.Id,
            Alle(konto, new KassenService.VerkaufsPositionInput(w.Consultation.Id, 1, null, false)),
            [new KassenService.ZahlungInput(ZahlungsArt.Bar, Gegeben: 20000)], kundeId: w.Kunde);
        Assert.True(ok, fehler);
        Assert.Equal(15000m, verkauf!.GesamtSumme);                                    // Injection zum alten Preis 1 500
        Assert.Equal(3, verkauf.Positionen.Count(p => p.ErbrachtAm is not null));
        Assert.Null(verkauf.Positionen.Last().ErbrachtAm);
        Assert.Equal(8, await BestandAsync(w, w.Paracetamol.Id));                      // kein zweiter Abzug

        await using (var db = await w.F.CreateDbContextAsync())
        {
            Assert.All(await db.AufenthaltsPositionen.ToListAsync(), a => Assert.Equal(verkauf.Id, a.VerkaufId));
            Assert.NotNull((await db.KundenBesuche.SingleAsync()).EndeUtc);           // Besuch endet mit dem Encaissement
        }
        Assert.Empty(await w.Konto.GetStaendeAsync(w.Awa, w.Laden.Id));
    }

    [Fact]
    public async Task Kassieren_Ohne_Alle_Erfassten_Wird_Abgelehnt_Und_Nie_Doppelt()
    {
        var w = await MitAnkunftAsync();
        await w.Konto.ErfasseAsync(w.Awa, w.Laden.Id, w.Kunde, [new(w.Consultation.Id, 1)]);
        var konto = await w.Konto.GetKontoAsync(w.Awa, w.Laden.Id, w.Kunde);
        // Zwischendurch erfasst ein anderes Gerät noch etwas
        await w.Konto.ErfasseAsync(w.Kofi, w.Laden.Id, w.Kunde, [new(w.Injection.Id, 1)]);

        var zahlung = new KassenService.ZahlungInput(ZahlungsArt.Bar, Gegeben: 10000);
        var veraltet = await w.Kasse.BucheVerkaufAsync(w.Sitzung.Id, Alle(konto), [zahlung], kundeId: w.Kunde);
        Assert.False(veraltet.Erfolg);
        Assert.Equal(KassenService.AufenthaltGeaendertFehler, veraltet.Fehler);

        // Ohne erfasste Zeilen kassieren ginge sonst am Konto vorbei
        var ohne = await w.Kasse.BucheVerkaufAsync(w.Sitzung.Id, [new KassenService.VerkaufsPositionInput(w.Injection.Id, 1, null, false)], [zahlung], kundeId: w.Kunde);
        Assert.Equal(KassenService.AufenthaltGeaendertFehler, ohne.Fehler);
        // Erfasste Zeile ohne Kunde
        var ohneKunde = await w.Kasse.BucheVerkaufAsync(w.Sitzung.Id, Alle(konto), [zahlung]);
        Assert.Equal(KassenService.AufenthaltGeaendertFehler, ohneKunde.Fehler);

        var aktuell = await w.Konto.GetKontoAsync(w.Awa, w.Laden.Id, w.Kunde);
        var erst = await w.Kasse.BucheVerkaufAsync(w.Sitzung.Id, Alle(aktuell), [zahlung], kundeId: w.Kunde);
        Assert.True(erst.Erfolg, erst.Fehler);
        Assert.Equal(6500m, erst.Verkauf!.GesamtSumme);
        var zweit = await w.Kasse.BucheVerkaufAsync(w.Sitzung.Id, Alle(aktuell), [zahlung], kundeId: w.Kunde);
        Assert.False(zweit.Erfolg);
    }

    [Fact]
    public async Task Inaktive_Leistung_Und_Remise_Beim_Kassieren()
    {
        var w = await MitAnkunftAsync();
        var rabatt = new Rabatt { SalonId = w.Laden.Id, Name = "Assurance 10 %", Art = RabattArt.Prozent, Wert = 10m, IstAktiv = true };
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.Rabatte.Add(rabatt);
            await db.SaveChangesAsync();
        }
        await w.Konto.ErfasseAsync(w.Awa, w.Laden.Id, w.Kunde, [new(w.Consultation.Id, 2)]);
        await using (var db = await w.F.CreateDbContextAsync())
        {
            (await db.Leistungen.SingleAsync(l => l.Id == w.Consultation.Id)).IstAktiv = false;   // nicht mehr im Katalog
            await db.SaveChangesAsync();
        }
        var konto = await w.Konto.GetKontoAsync(w.Awa, w.Laden.Id, w.Kunde);
        var pos = konto.Offene.Select(z => new KassenService.VerkaufsPositionInput(z.LeistungId!.Value, z.Menge, rabatt.Id, false, null, z.Id)).ToList();
        var (ok, fehler, verkauf, _) = await w.Kasse.BucheVerkaufAsync(w.Sitzung.Id, pos, [new KassenService.ZahlungInput(ZahlungsArt.Karte)], kundeId: w.Kunde);
        Assert.True(ok, fehler);
        Assert.Equal((10000m, 1000m, 9000m), (verkauf!.Zwischensumme, verkauf.RabattSumme, verkauf.GesamtSumme));
    }

    [Fact]
    public async Task Divers_Mit_Freiem_Betrag_Meldet_Sich_Beim_Besitzer()
    {
        var w = await MitAnkunftAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => w.Konto.ErfasseAsync(w.Awa, w.Laden.Id, w.Kunde, [new(w.Divers.Id, 1)]));
        await Assert.ThrowsAsync<ArgumentException>(() => w.Konto.ErfasseAsync(w.Awa, w.Laden.Id, w.Kunde, [new(w.Injection.Id, 1, 999m)]));
        await w.Konto.ErfasseAsync(w.Awa, w.Laden.Id, w.Kunde, [new(w.Divers.Id, 1, 3500m)]);
        var konto = await w.Konto.GetKontoAsync(w.Awa, w.Laden.Id, w.Kunde);
        Assert.Equal(3500m, konto.Summe);
        await using var db = await w.F.CreateDbContextAsync();
        Assert.Contains("client présent", (await db.BesitzerHinweise.SingleAsync()).Text);
    }

    [Fact]
    public async Task Eigene_Erfassung_Sofort_Zuruecknehmen_Mit_Grund_Und_Lager()
    {
        var w = await MitAnkunftAsync();
        await w.Konto.ErfasseAsync(w.Awa, w.Laden.Id, w.Kunde, [new(w.Paracetamol.Id, 3)]);
        var zeile = Assert.Single((await w.Konto.GetKontoAsync(w.Awa, w.Laden.Id, w.Kunde)).Zeilen);
        Assert.True(zeile.DarfSofortZuruecknehmen);
        Assert.Equal(7, await BestandAsync(w, w.Paracetamol.Id));

        await Assert.ThrowsAsync<ArgumentException>(() => w.Konto.NimmZurueckAsync(w.Awa, zeile.Id, " ", true));   // Grund Pflicht
        w.Uhr.Jetzt = w.Uhr.Jetzt.AddMinutes(10);
        await w.Konto.NimmZurueckAsync(w.Awa, zeile.Id, "Erreur de saisie", insLager: true);

        Assert.Equal(10, await BestandAsync(w, w.Paracetamol.Id));
        var konto = await w.Konto.GetKontoAsync(w.Besitzer, w.Laden.Id, w.Kunde);
        var weg = Assert.Single(konto.Zeilen);
        Assert.False(weg.Offen);
        Assert.Equal(("Erreur de saisie", "Awa Koffi"), (weg.Grund, weg.ZurueckgenommenVon));
        Assert.Equal(0m, konto.Summe);
        await using (var db = await w.F.CreateDbContextAsync())
        {
            Assert.Contains("Erreur de saisie", (await db.BesitzerHinweise.SingleAsync()).Text);   // Besitzer erfährt es
        }

        // Nichts mehr offen → Départ möglich; Annulation nicht mehr (es wurde etwas erfasst)
        var besuchId = konto.BesuchId!.Value;
        var annul = await Assert.ThrowsAsync<ArgumentException>(() => w.Besuche.AnnuliereAsync(w.Awa, besuchId));
        Assert.Contains("ne peut plus être annulé", annul.Message);
        await w.Besuche.BeendeAsync(w.Awa, besuchId);
        await Assert.ThrowsAsync<ArgumentException>(() => w.Konto.NimmZurueckAsync(w.Awa, zeile.Id, "Encore", true));
    }

    [Fact]
    public async Task Spaetere_Ruecknahme_Braucht_Freigabe_Des_Besitzers()
    {
        var w = await MitAnkunftAsync();
        await w.Konto.ErfasseAsync(w.Awa, w.Laden.Id, w.Kunde, [new(w.Paracetamol.Id, 2)]);
        var zeile = Assert.Single((await w.Konto.GetKontoAsync(w.Awa, w.Laden.Id, w.Kunde)).Zeilen);

        // Andere Mitarbeiterin sofort, oder Verfasserin nach 15 Minuten: ohne PIN-Einrichtung nur der Besitzer
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Konto.NimmZurueckAsync(w.Kofi, zeile.Id, "Erreur de saisie", true));
        w.Uhr.Jetzt = w.Uhr.Jetzt.AddMinutes(16);
        Assert.False((await w.Konto.GetKontoAsync(w.Awa, w.Laden.Id, w.Kunde)).Zeilen[0].DarfSofortZuruecknehmen);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Konto.NimmZurueckAsync(w.Awa, zeile.Id, "Erreur de saisie", true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Konto.NimmZurueckAsync(w.Fremder, zeile.Id, "Erreur de saisie", true));

        // Kunde ohne Zahlung gegangen: Besitzer nimmt zurück, Ware bleibt verbraucht; kein Hinweis an sich selbst
        await w.Konto.NimmZurueckAsync(w.Besitzer, zeile.Id, "Client parti sans payer", insLager: false);
        Assert.Equal(8, await BestandAsync(w, w.Paracetamol.Id));
        await using var db = await w.F.CreateDbContextAsync();
        Assert.Empty(await db.BesitzerHinweise.ToListAsync());
    }

    [Fact]
    public async Task Mit_Besitzer_Pin_Am_Geraet_Des_Besitzers_Zuruecknehmen()
    {
        var w = await MitAnkunftAsync(besitzerPin: true);
        await w.Konto.ErfasseAsync(w.Awa, w.Laden.Id, w.Kunde, [new(w.Consultation.Id, 1)]);
        var konto = await w.Konto.GetKontoAsync(w.Kofi, w.Laden.Id, w.Kunde);
        Assert.True(konto.BrauchtPin);
        var zeile = Assert.Single(konto.Zeilen);

        // 17.11.2026 « Connexion personnelle »: Vom eigenen Mitarbeiter-Konto aus gilt die Besitzer-PIN nie (Schutz vor
        // Durchprobieren von zu Hause) – auch nicht die richtige.
        var mitarbeiter = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Konto.NimmZurueckAsync(w.Kofi, zeile.Id, "Prestation non réalisée", false, "4826"));
        Assert.Equal(LadenZugriff.BesitzerPinNurBeimBesitzerText, mitarbeiter.Message);

        // Am Gerät, an dem der Besitzer angemeldet ist, gibt die Mitarbeiterin die Besitzer-PIN ein.
        var falsch = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Konto.NimmZurueckAsync(w.Besitzer, zeile.Id, "Prestation non réalisée", false, "1111"));
        Assert.Contains("incorrect", falsch.Message);
        await w.Konto.NimmZurueckAsync(w.Besitzer, zeile.Id, "Prestation non réalisée", false, "4826");
        Assert.Equal(0m, (await w.Konto.GetKontoAsync(w.Kofi, w.Laden.Id, w.Kunde)).Summe);
    }

    [Fact]
    public async Task Kunde_Und_Leistung_Loeschen_Gesperrt_Solange_Offen()
    {
        var w = await MitAnkunftAsync();
        await w.Konto.ErfasseAsync(w.Awa, w.Laden.Id, w.Kunde, [new(w.Injection.Id, 1)]);

        var kunden = new KundenService(w.F, w.Uhr);
        var k = await Assert.ThrowsAsync<ArgumentException>(() => kunden.LoescheAsync(w.Besitzer, w.Kunde));
        Assert.Contains("à payer", k.Message);

        var katalog = new KatalogService(w.F, null!, null!);
        var l = await Assert.ThrowsAsync<ArgumentException>(() => katalog.LoescheLeistungAsync(w.Injection.Id));
        Assert.Contains("client présent", l.Message);

        // Zurückgenommen → beides möglich; die Zeile verliert nur den Katalogbezug
        var zeile = Assert.Single((await w.Konto.GetKontoAsync(w.Awa, w.Laden.Id, w.Kunde)).Zeilen);
        await w.Konto.NimmZurueckAsync(w.Awa, zeile.Id, "Erreur de saisie", true);
        await katalog.LoescheLeistungAsync(w.Injection.Id);
        await kunden.LoescheAsync(w.Besitzer, w.Kunde);
        await using var db = await w.F.CreateDbContextAsync();
        Assert.Empty(await db.AufenthaltsPositionen.ToListAsync());
    }
}
