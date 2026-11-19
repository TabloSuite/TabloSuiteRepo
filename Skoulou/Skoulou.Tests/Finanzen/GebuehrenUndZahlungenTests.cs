using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Finanzen;
using Skoulou.WebApp.Services.Finanzen;

namespace Skoulou.Tests.Finanzen;

public class GebuehrenTests
{
    [Fact]
    public async Task Raten_muessen_den_Postenbetrag_ergeben()
    {
        var w = await FinanzWelt.ErstelleAsync();
        var f = await Assert.ThrowsAsync<FachFehler>(() => w.Gebuehren.PostenSpeichernAsync(w.D, w.Org.Jahr, null,
            new PostenEingabe("APE", 5_000, null, null, true, [new RateEingabe(2_000, new(2026, 10, 1)), new RateEingabe(2_000, new(2026, 11, 1))])));
        Assert.Contains("somme des tranches", f.Message);
        await Assert.ThrowsAsync<FachFehler>(() => w.Gebuehren.PostenSpeichernAsync(w.D, w.Org.Jahr, null,
            new PostenEingabe("APE", 5_000, null, null, true, [new RateEingabe(5_000, new(2027, 8, 1))])));
        await Assert.ThrowsAsync<FachFehler>(() => w.Gebuehren.PostenSpeichernAsync(w.D, w.Org.Jahr, null, new PostenEingabe(" ", 5_000, null, null, true, [])));

        // Ohne Raten: eine Rate zum Jahresbeginn; Bearbeiten ersetzt die Raten.
        var ape = await w.Gebuehren.PostenSpeichernAsync(w.D, w.Org.Jahr, null, new PostenEingabe("APE", 5_000, null, w.Org.Klasse6B, false, []));
        var posten = (await w.Gebuehren.PostenAsync(w.D, w.Org.Jahr)).Single(p => p.Id == ape);
        Assert.Equal("Classe 6e B", posten.Geltung);
        Assert.Equal(new RateZeile(1, 5_000, new(2026, 9, 1)), Assert.Single(posten.Raten));
        await w.Gebuehren.PostenSpeichernAsync(w.D, w.Org.Jahr, ape, new PostenEingabe("APE", 6_000, null, null, true,
            [new RateEingabe(3_000, new(2026, 12, 1)), new RateEingabe(3_000, new(2026, 10, 1))]));
        posten = (await w.Gebuehren.PostenAsync(w.D, w.Org.Jahr)).Single(p => p.Id == ape);
        Assert.Equal("Tous les élèves", posten.Geltung);
        Assert.Equal([new(2026, 10, 1), new DateOnly(2026, 12, 1)], posten.Raten.Select(r => r.Faellig));
        Assert.Equal([1, 2], posten.Raten.Select(r => r.Nr));
        await w.Gebuehren.PostenLoeschenAsync(w.D, ape);
        Assert.Equal(2, (await w.Gebuehren.PostenAsync(w.D, w.Org.Jahr)).Count);
    }

    [Fact]
    public async Task Vorjahr_kopieren_verschiebt_die_Faelligkeiten_und_ueberspringt_Vorhandenes()
    {
        var w = await FinanzWelt.ErstelleAsync();
        Assert.Equal((2, 0), await w.Gebuehren.VorjahrKopierenAsync(w.D, w.Org.JahrVorb));
        var kopiert = await w.Gebuehren.PostenAsync(w.D, w.Org.JahrVorb);
        Assert.Equal([new(2027, 10, 1), new(2028, 1, 10), new DateOnly(2028, 4, 10)], kopiert.Single(p => p.Bezeichnung == "Scolarité 6e").Raten.Select(r => r.Faellig));
        Assert.Equal((0, 2), await w.Gebuehren.VorjahrKopierenAsync(w.D, w.Org.JahrVorb));
        await Assert.ThrowsAsync<FachFehler>(() => w.Gebuehren.VorjahrKopierenAsync(w.D, w.Org.Jahr));
    }

    [Fact]
    public async Task Konto_Uebersicht_und_en_retard()
    {
        var w = await FinanzWelt.ErstelleAsync();
        await w.ZahlenAsync(w.Org.Schueler, 25_000);
        var konto = await w.Gebuehren.KontoAsync(w.D, w.Org.Jahr, w.Org.Schueler);
        Assert.Equal((70_000L, 25_000L, 45_000L, 5_000L), (konto.Konto.Soll, konto.Konto.Bezahlt, konto.Konto.Offen, konto.Konto.EnRetardBetrag));
        Assert.Equal("6e A", konto.Kopf.Klasse);
        Assert.Single(konto.Zahlungen);

        var zaehler = await w.Gebuehren.ZaehlerAsync(w.D, w.Org.Jahr, null);
        Assert.Equal((2, 2, 0), (zaehler.Alle, zaehler.EnRetard, zaehler.Soldes));
        Assert.Equal(140_000 - 25_000, zaehler.Offen);
        var retard = await w.Gebuehren.KontenAsync(w.D, w.Org.Jahr, null, GebuehrenService.FilterEnRetard, null, 0, 50);
        Assert.Equal([w.Org.SchuelerZiel, w.Org.Schueler], retard.Eintraege.Select(e => e.SchuelerId));
        Assert.Empty((await w.Gebuehren.KontenAsync(w.D, w.Org.Jahr, w.Org.Klasse6B, null, null, 0, 50)).Eintraege);
        Assert.Single((await w.Gebuehren.KontenAsync(w.D, w.Org.Jahr, null, null, "eleveziel", 0, 50)).Eintraege);
        var seite = await w.Gebuehren.KontenAsync(w.D, w.Org.Jahr, null, null, null, 0, 1);
        Assert.True(seite.Mehr);
    }

    [Fact]
    public async Task Ermaessigungen_mindern_das_Soll()
    {
        var w = await FinanzWelt.ErstelleAsync();
        var id = await w.Gebuehren.ErmaessigungAnlegenAsync(w.D, w.Org.Jahr, new ErmaessigungEingabe(w.Org.Schueler, w.Scolarite, ErmaessigungsArt.Pourcentage, 50, "Bourse"));
        Assert.Equal(40_000, (await w.Gebuehren.KontoAsync(w.D, w.Org.Jahr, w.Org.Schueler)).Konto.Soll);
        await Assert.ThrowsAsync<FachFehler>(() => w.Gebuehren.ErmaessigungAnlegenAsync(w.D, w.Org.Jahr, new(w.Org.Schueler, null, ErmaessigungsArt.Pourcentage, 120, "Fratrie")));
        await Assert.ThrowsAsync<FachFehler>(() => w.Gebuehren.ErmaessigungAnlegenAsync(w.D, w.Org.Jahr, new(w.Org.Schueler, null, ErmaessigungsArt.Montant, 10.5m, "Fratrie")));
        await Assert.ThrowsAsync<FachFehler>(() => w.Gebuehren.ErmaessigungAnlegenAsync(w.D, w.Org.Jahr, new(w.Org.Schueler, null, ErmaessigungsArt.Montant, 1_000, "")));
        await Assert.ThrowsAsync<FachFehler>(() => w.Gebuehren.PostenLoeschenAsync(w.D, w.Scolarite));
        await w.Gebuehren.ErmaessigungLoeschenAsync(w.D, id);
        Assert.Equal(70_000, (await w.Gebuehren.KontoAsync(w.D, w.Org.Jahr, w.Org.Schueler)).Konto.Soll);
        Assert.True(await w.MitDbAsync(db => db.AuditEintraege.AnyAsync(a => a.Aktion == FinanzAudit.ErmaessigungGeloescht)));
    }

    [Fact]
    public async Task Rest_des_Vorjahres_ist_eine_reine_Abfrage()
    {
        var w = await FinanzWelt.ErstelleAsync();
        await using (var db = await w.Db.CreateDbContextAsync())
        {
            var vorjahr = new Schuljahr { SchuleId = w.Org.SchuleA, Bezeichnung = "2025-2026", Beginn = new(2025, 9, 1), Ende = new(2026, 6, 30), Status = SchuljahrStatus.Abgeschlossen, PlattformMonatspreis = 1_200 };
            var klasse = new Klasse { SchuleId = w.Org.SchuleA, SchuljahrId = vorjahr.Id, Name = "CM2" };
            var posten = new GebuehrenPosten { SchuleId = w.Org.SchuleA, SchuljahrId = vorjahr.Id, Bezeichnung = "Scolarité", Betrag = 50_000 };
            db.AddRange(vorjahr, klasse, posten);
            db.GebuehrenRaten.Add(new GebuehrenRate { PostenId = posten.Id, Nr = 1, Betrag = 50_000, Faellig = new(2025, 10, 1) });
            db.Einschreibungen.Add(new Einschreibung { SchuleId = w.Org.SchuleA, SchuelerId = w.Org.Schueler, SchuljahrId = vorjahr.Id, KlasseId = klasse.Id, Von = vorjahr.Beginn, Bis = vorjahr.Ende, Status = EinschreibungsStatus.Terminee });
            db.Zahlungen.Add(new Zahlung { SchuleId = w.Org.SchuleA, SchuelerId = w.Org.Schueler, SchuljahrId = vorjahr.Id, Betrag = 30_000, Datum = new(2025, 10, 2), Quittungsnummer = "REC-2025-00001", ErfasstVonId = w.D });
            await db.SaveChangesAsync();
        }
        var konto = (await w.Gebuehren.KontoAsync(w.D, w.Org.Jahr, w.Org.Schueler)).Konto;
        Assert.Equal(20_000, konto.VortragRest);
        Assert.Equal(GebuehrenRechner.VortragText, konto.Zeilen[0].Bezeichnung);
        Assert.Equal(90_000, konto.Soll);
        await w.ZahlenAsync(w.Org.Schueler, 25_000);
        konto = (await w.Gebuehren.KontoAsync(w.D, w.Org.Jahr, w.Org.Schueler)).Konto;
        Assert.Equal([20_000L, 5_000], konto.Zeilen.Take(2).Select(z => z.Bezahlt));
        // Das Vorjahr selbst zeigt den offenen Rest ebenfalls.
        var vorjahrId = await w.MitDbAsync(db => db.Schuljahre.Where(j => j.Bezeichnung == "2025-2026").Select(j => j.Id).FirstAsync());
        Assert.Equal(20_000, (await w.Gebuehren.KontoAsync(w.D, vorjahrId, w.Org.Schueler)).Konto.Offen);
        // Geschlossenes Jahr: keine Änderungen.
        await Assert.ThrowsAsync<FachFehler>(() => w.Gebuehren.PostenSpeichernAsync(w.D, vorjahrId, null, new PostenEingabe("Tenue", 5_000, null, null, false, [])));
    }
}

public class ZahlungTests
{
    [Fact]
    public async Task Fortlaufende_Quittungen_mit_Journalbuchung_in_einem_Schritt()
    {
        var w = await FinanzWelt.ErstelleAsync();
        var erste = await w.ZahlenAsync(w.Org.Schueler, 10_000);
        var zweite = await w.ZahlenAsync(w.Org.SchuelerZiel, 20_000, ZahlungsKanal.MobileMoney);
        Assert.Equal(("REC-2026-00001", "REC-2026-00002"), (erste.Quittungsnummer, zweite.Quittungsnummer));
        Assert.Equal(60_000, erste.Rest);
        var buchungen = await w.MitDbAsync(db => db.Buchungen.OrderBy(b => b.Nummer).ToListAsync());
        Assert.Equal(["ECR-2026-00001", "ECR-2026-00002"], buchungen.Select(b => b.Nummer));
        Assert.All(buchungen, b => Assert.Equal((BuchungsHerkunft.Paiement, BuchungsRichtung.Recette), (b.Herkunft, b.Richtung)));
        Assert.Equal(erste.ZahlungId, buchungen[0].HerkunftId);
        var kategorie = await w.MitDbAsync(db => db.Kategorien.SingleAsync(k => k.Id == buchungen[0].KategorieId));
        Assert.Equal(FinanzKategorien.Scolarite, kategorie.SystemCode);
        Assert.True(await w.MitDbAsync(db => db.AuditEintraege.AnyAsync(a => a.ZielId == erste.ZahlungId)));
    }

    [Fact]
    public async Task Doppelklick_ergibt_eine_Zahlung()
    {
        var w = await FinanzWelt.ErstelleAsync();
        var eingabe = new ZahlungEingabe(Guid.NewGuid(), w.Org.Schueler, 10_000, new(2026, 11, 12), ZahlungsKanal.Especes, null, null);
        var a = await w.Zahlungen.ErfassenAsync(w.D, w.Org.Jahr, eingabe);
        var b = await w.Zahlungen.ErfassenAsync(w.D, w.Org.Jahr, eingabe);
        Assert.Equal(a.Quittungsnummer, b.Quittungsnummer);
        Assert.True(b.Wiederholt);
        Assert.Equal(1, await w.MitDbAsync(db => db.Zahlungen.CountAsync()));
        Assert.Equal(1, await w.MitDbAsync(db => db.Buchungen.CountAsync()));
        // Fremde Schule kann eine bekannte Vorgangs-Id nicht abfragen.
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Zahlungen.ErfassenAsync(w.Org.DirektorB, w.Org.Jahr, eingabe));
    }

    [Fact]
    public async Task Eingaben_und_Ueberzahlung()
    {
        var w = await FinanzWelt.ErstelleAsync();
        await Assert.ThrowsAsync<FachFehler>(() => w.ZahlenAsync(w.Org.Schueler, 0));
        await Assert.ThrowsAsync<FachFehler>(() => w.ZahlenAsync(w.Org.Schueler, 1_000, datum: new(2026, 11, 14)));
        await Assert.ThrowsAsync<FachFehler>(() => w.Zahlungen.ErfassenAsync(w.D, w.Org.Jahr,
            new ZahlungEingabe(Guid.NewGuid(), w.Org.Schueler, 1_000, new(2026, 11, 12), ZahlungsKanal.MobileMoney, " ", null)));
        await Assert.ThrowsAsync<FachFehler>(() => w.ZahlenAsync(w.Org.Lehrer, 1_000));
        var f = await Assert.ThrowsAsync<FachFehler>(() => w.ZahlenAsync(w.Org.Schueler, 80_000));
        Assert.Equal("guthaben", f.Feld);
        var ok = await w.ZahlenAsync(w.Org.Schueler, 80_000, guthaben: true);
        Assert.Equal((0L, 10_000L), (ok.Rest, ok.Guthaben));
        Assert.Equal(10_000, (await w.Gebuehren.KontoAsync(w.D, w.Org.Jahr, w.Org.Schueler)).Konto.Guthaben);
    }

    [Fact]
    public async Task Storno_mit_Grund_bucht_gegen_und_loescht_nichts()
    {
        var w = await FinanzWelt.ErstelleAsync();
        var z = await w.ZahlenAsync(w.Org.Schueler, 30_000);
        await Assert.ThrowsAsync<FachFehler>(() => w.Zahlungen.StornierenAsync(w.D, z.ZahlungId, "  "));
        await w.Zahlungen.StornierenAsync(w.KMit, z.ZahlungId, "Erreur de saisie");
        await Assert.ThrowsAsync<FachFehler>(() => w.Zahlungen.StornierenAsync(w.D, z.ZahlungId, "Encore"));
        var buchungen = await w.MitDbAsync(db => db.Buchungen.OrderBy(b => b.Nummer).ToListAsync());
        Assert.Equal(2, buchungen.Count);
        Assert.Equal((BuchungsHerkunft.AnnulationPaiement, BuchungsRichtung.Depense, buchungen[0].Id), (buchungen[1].Herkunft, buchungen[1].Richtung, buchungen[1].StornoVonId));
        Assert.Equal(0, (await w.Journal.KasseAsync(w.D, w.Org.Jahr)).Saldo);
        Assert.Equal(0, (await w.Gebuehren.KontoAsync(w.D, w.Org.Jahr, w.Org.Schueler)).Konto.Bezahlt);
        Assert.True(await w.MitDbAsync(db => db.Zahlungen.AnyAsync(x => x.Id == z.ZahlungId && x.StornoGrund == "Erreur de saisie")));
        Assert.Single((await w.Zahlungen.SucheAsync(w.D, w.Org.Jahr, ZahlungService.FilterAnnules, null, 0, 50)).Eintraege);
        Assert.Equal(1, (await w.Zahlungen.ZaehlerAsync(w.D, w.Org.Jahr)).Storniert);
        // Gegenbuchung im Journal ist nicht direkt korrigierbar.
        await Assert.ThrowsAsync<FachFehler>(() => w.Journal.GegenbuchungAsync(w.D, buchungen[0].Id, "x"));
    }

    [Fact]
    public async Task Quittung_als_PDF_fuer_Berechtigte()
    {
        var w = await FinanzWelt.ErstelleAsync();
        await w.ZahlenAsync(w.Org.Schueler, 5_000, datum: new(2026, 10, 1));
        var z = await w.ZahlenAsync(w.Org.Schueler, 25_000);
        var daten = await w.Quittungen.DatenAsync(w.D, z.ZahlungId);
        Assert.Equal((30_000L, 40_000L, 70_000L), (daten.BezahltBisher, daten.RestNachher, daten.SollJahr));
        Assert.Equal([5_000L, 20_000], daten.Verteilung.Select(v => v.Betrag));
        Assert.Equal("Tranche 1/3", daten.Verteilung[1].Tranche);
        Assert.Equal("CA260001", daten.Matrikel);
        var pdf = await w.Quittungen.PdfAsync(w.D, z.ZahlungId);
        Assert.Equal("%PDF"u8.ToArray(), pdf.Inhalt[..4]);

        foreach (var erlaubt in new[] { w.D, w.KMit, w.Org.Schueler, w.Org.Eltern, w.Org.Responsable, w.Org.Admin })
            await w.Quittungen.DatenAsync(erlaubt, z.ZahlungId);
        foreach (var verboten in new[] { w.KOhne, w.Org.Lehrer, w.Org.SchuelerZiel, w.Org.ElternZiel, w.Org.DirektorB })
            await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Quittungen.DatenAsync(verboten, z.ZahlungId));

        await w.Zahlungen.StornierenAsync(w.D, z.ZahlungId, "Chèque sans provision");
        var storniert = await w.Quittungen.DatenAsync(w.D, z.ZahlungId);
        Assert.True(storniert.Storniert);
        Assert.Equal([5_000L, 20_000], storniert.Verteilung.Select(v => v.Betrag));
        Assert.Equal("%PDF"u8.ToArray(), (await w.Quittungen.PdfAsync(w.Org.Eltern, z.ZahlungId)).Inhalt[..4]);
    }

    [Fact]
    public async Task Quittung_per_Mail_an_Familie_ueber_die_Outbox()
    {
        var w = await FinanzWelt.ErstelleAsync();
        await using (var db = await w.Db.CreateDbContextAsync())
        {
            (await db.Users.FirstAsync(k => k.Id == w.Org.Eltern)).Email = "maman@exemple.tg";
            (await db.Users.FirstAsync(k => k.Id == w.Org.Schueler)).Email = "eleve@exemple.tg";
            (await db.SchuelerProfile.FirstAsync(p => p.KontoId == w.Org.Schueler)).ElternEmail = "MAMAN@exemple.tg";
            await db.SaveChangesAsync();
        }
        var z = await w.ZahlenAsync(w.Org.Schueler, 10_000);
        Assert.Equal(2, z.Mails);
        var mails = await w.MitDbAsync(db => db.MailAusgaenge.Where(m => m.Bezug == "finances-recu").ToListAsync());
        Assert.Equal(2, mails.Count);
        Assert.All(mails, m => Assert.Contains("REC-2026-00001", m.Betreff));
        Assert.Empty(w.Org.D.Versender.Gesendet);
    }

    [Fact]
    public async Task Liste_Suche_und_Schuelerauswahl()
    {
        var w = await FinanzWelt.ErstelleAsync();
        await w.ZahlenAsync(w.Org.Schueler, 10_000);
        await w.ZahlenAsync(w.Org.SchuelerZiel, 15_000, ZahlungsKanal.MobileMoney, datum: new(2026, 11, 13));
        Assert.Equal(2, (await w.Zahlungen.SucheAsync(w.D, w.Org.Jahr, null, null, 0, 50)).Eintraege.Count);
        Assert.Single((await w.Zahlungen.SucheAsync(w.D, w.Org.Jahr, ZahlungService.FilterMobile, null, 0, 50)).Eintraege);
        Assert.Single((await w.Zahlungen.SucheAsync(w.D, w.Org.Jahr, null, "REC-2026-00002", 0, 50)).Eintraege);
        var zaehler = await w.Zahlungen.ZaehlerAsync(w.D, w.Org.Jahr);
        Assert.Equal((15_000L, 1, 25_000L), (zaehler.HeuteSumme, zaehler.HeuteAnzahl, zaehler.JahrSumme));
        var auswahl = await w.Zahlungen.SchuelerSuchenAsync(w.D, w.Org.Jahr, "eleve");
        Assert.Contains(auswahl, s => s.Id == w.Org.Schueler && s.Offen == 60_000 && s.Klasse == "6e A");
        Assert.Empty(await w.Zahlungen.SchuelerSuchenAsync(w.D, w.Org.Jahr, "e"));
    }
}
