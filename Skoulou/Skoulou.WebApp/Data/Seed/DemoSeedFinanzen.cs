using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Finanzen;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Services.Finanzen;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Data.Seed;

/// <summary>
/// Demo-Daten des Bereichs E6 Finanzen (nur erfundene Daten): Frais mit Tranchen für das laufende Jahr, Zahlungen
/// (bar, Mobile Money, Überweisung), eine stornierte Zahlung, Ermäßigungen, Journal-Écritures, und – falls die Schule
/// noch kein Vorjahr hat – das abgeschlossene Jahr 2025-2026 mit einem Rest aus dem Vorjahr und Plattformrechnungen
/// (bezahlt und bestätigt mit Responsable-Anteil, eine gemeldet).
/// </summary>
public static class DemoSeedFinanzen
{
    /// <param name="dienste">Dienste der App (DbContextFactory, UserManager, Bereichsdienste …).</param>
    /// <param name="schuleId">Demo-Schule « Collège Démo Les Palmiers ».</param>
    /// <param name="neueKonten">Neu angelegte Demo-Konten (Passwörter landen nur in demo-konten.json).</param>
    public static async Task ErgaenzenAsync(IServiceProvider dienste, Guid schuleId, List<DemoKonto> neueKonten, CancellationToken ct)
    {
        var dbFactory = dienste.GetRequiredService<IDbContextFactory<SkoulouDbContext>>();
        var nummern = dienste.GetRequiredService<NummernkreisService>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await FinanzKategorien.SicherstellenAsync(db, ct);
        var jahr = await db.Schuljahre.FirstOrDefaultAsync(j => j.SchuleId == schuleId && j.Status == SchuljahrStatus.Laufend, ct);
        if (jahr is null || await db.GebuehrenPosten.AnyAsync(p => p.SchuleId == schuleId, ct)) return;

        var direktor = await db.Users.Where(k => k.SchuleId == schuleId && k.Rolle == Rollen.Direktor).Select(k => k.Id).FirstAsync(ct);
        var koordinator = await db.Users.Where(k => k.SchuleId == schuleId && k.Rolle == Rollen.Koordinator).Select(k => (Guid?)k.Id).FirstOrDefaultAsync(ct) ?? direktor;
        var schueler = await db.Users.Where(k => k.SchuleId == schuleId && k.Rolle == Rollen.Schueler).ToDictionaryAsync(k => k.Nachname, ct);
        var stufen = await db.Stufen.Where(s => s.SchuleId == schuleId).ToDictionaryAsync(s => s.Name, s => s.Id, ct);

        var posten = new Dictionary<string, GebuehrenPosten>
        {
            ["Inscription"] = Posten(db, schuleId, jahr.Id, "Inscription", null, true, 1, (15_000, jahr.Beginn)),
            ["Tenue"] = Posten(db, schuleId, jahr.Id, "Tenue scolaire", null, false, 2, (8_000, new(2026, 9, 30))),
            ["6e"] = Posten(db, schuleId, jahr.Id, "Scolarité 6e", stufen.GetValueOrDefault("6e"), true, 3,
                (30_000, new(2026, 10, 5)), (30_000, new(2027, 1, 11)), (30_000, new(2027, 4, 12))),
            ["5e"] = Posten(db, schuleId, jahr.Id, "Scolarité 5e", stufen.GetValueOrDefault("5e"), true, 4,
                (40_000, new(2026, 10, 5)), (30_000, new(2027, 1, 11)), (30_000, new(2027, 4, 12))),
            ["APE"] = Posten(db, schuleId, jahr.Id, "Cotisation APE", null, true, 5, (5_000, new(2026, 10, 31))),
        };
        await db.SaveChangesAsync(ct);

        void Ermaessigung(string name, string postenKey, decimal prozent, string grund)
        {
            if (schueler.TryGetValue(name, out var s))
                db.Ermaessigungen.Add(new Ermaessigung { SchuleId = schuleId, SchuelerId = s.Id, SchuljahrId = jahr.Id, PostenId = posten[postenKey].Id, Art = ErmaessigungsArt.Pourcentage, Wert = prozent, Grund = grund, ErfasstVonId = direktor });
        }
        Ermaessigung("Dossou", "6e", 10, "Fratrie");
        Ermaessigung("Atsu", "6e", 50, "Bourse d'excellence");

        async Task<Zahlung?> ZahlenAsync(Guid jahrId, string name, long betrag, DateOnly datum, ZahlungsKanal kanal = ZahlungsKanal.Especes, string? referenz = null, Guid? von = null)
        {
            if (!schueler.TryGetValue(name, out var s)) return null;
            var z = await Kassierer.ZahlungBuchenAsync(db, nummern, schuleId, jahrId, von ?? direktor, NeueId.Erzeuge(), s, betrag, datum, kanal, referenz, null, ct);
            await db.SaveChangesAsync(ct);
            return z;
        }

        // Vorjahr zuerst (Nummern in zeitlicher Reihenfolge), dann das laufende Jahr.
        await VorjahrAsync(db, nummern, schuleId, jahr, stufen, schueler, direktor, ZahlenAsync, ct);

        var j = jahr.Id;
        await ZahlenAsync(j, "Adjo", 15_000, new(2026, 9, 14));
        await ZahlenAsync(j, "Amegah", 118_000, new(2026, 9, 14), ZahlungsKanal.Virement, "VIR ECOBANK 0914-221");
        await ZahlenAsync(j, "Ekué", 15_000, new(2026, 9, 15));
        await ZahlenAsync(j, "Dossou", 15_000, new(2026, 9, 15));
        await ZahlenAsync(j, "Agbodjan", 15_000, new(2026, 9, 15));
        await ZahlenAsync(j, "Sossou", 15_000, new(2026, 9, 16), ZahlungsKanal.MobileMoney, "TMONEY 70412233", koordinator);
        await ZahlenAsync(j, "Tchalla", 20_000, new(2026, 9, 16), ZahlungsKanal.MobileMoney, "FLOOZ 99231877", koordinator);
        await ZahlenAsync(j, "Atsu", 15_000, new(2026, 9, 17));
        await ZahlenAsync(j, "Ahadji", 25_000, new(2026, 9, 18));
        await ZahlenAsync(j, "Kponton", 60_000, new(2026, 9, 21), ZahlungsKanal.Virement, "VIR ORABANK 2109");
        await ZahlenAsync(j, "Dossou", 27_000, new(2026, 10, 2), ZahlungsKanal.MobileMoney, "TMONEY 70588120");
        await ZahlenAsync(j, "Adjo", 30_000, new(2026, 10, 3), ZahlungsKanal.MobileMoney, "FLOOZ 99417735");
        var storno = await ZahlenAsync(j, "Agbodjan", 30_000, new(2026, 10, 5), ZahlungsKanal.Cheque, "CHQ 0012457");
        await ZahlenAsync(j, "Sossou", 40_000, new(2026, 10, 5), von: koordinator);
        await ZahlenAsync(j, "Tchalla", 30_000, new(2026, 10, 6), ZahlungsKanal.MobileMoney, "FLOOZ 99502214", koordinator);
        await ZahlenAsync(j, "Atsu", 15_000, new(2026, 10, 9));
        await ZahlenAsync(j, "Adjo", 8_000, new(2026, 10, 20));
        await ZahlenAsync(j, "Dossou", 5_000, new(2026, 10, 30));
        await ZahlenAsync(j, "Sossou", 5_000, new(2026, 11, 3), ZahlungsKanal.MobileMoney, "TMONEY 71003318", koordinator);
        if (storno is not null)
        {
            await Kassierer.ZahlungStornierenAsync(db, nummern, storno, direktor, "Chèque rejeté par la banque (sans provision)", new(2026, 10, 12), DateTime.UtcNow, ct);
            await db.SaveChangesAsync(ct);
        }

        // Journal: weitere Einnahmen und Ausgaben der Schule.
        var kategorien = await db.Kategorien.Where(k => k.SchuleId == null && k.SystemCode == null).ToDictionaryAsync(k => k.Name, ct);
        foreach (var (datum, kategorie, betrag, text) in new (DateOnly, string, long, string)[]
                 {
                     (new(2026, 9, 10), "Fournitures et matériel", 45_000, "Craies, registres d'appel et marqueurs"),
                     (new(2026, 9, 25), "Vente de fournitures et tenues", 24_000, "Vente de trois tenues scolaires"),
                     (new(2026, 10, 8), "Eau, électricité, internet", 38_500, "Facture d'électricité de septembre"),
                     (new(2026, 10, 15), "Dons et subventions", 100_000, "Don de l'association des anciens élèves"),
                     (new(2026, 10, 31), "Salaires et primes", 350_000, "Salaires des surveillants – octobre"),
                     (new(2026, 11, 6), "Entretien et réparations", 27_500, "Réparation de deux tables-bancs"),
                 })
        {
            var k = kategorien[kategorie];
            db.Buchungen.Add(new Buchung
            {
                SchuleId = schuleId, Datum = datum, Richtung = k.Richtung, KategorieId = k.Id, Betrag = betrag, Text = text, Herkunft = BuchungsHerkunft.Manuelle,
                ErfasstVonId = direktor, Nummer = await nummern.NaechsteNummerAsync(db, schuleId, NummernArt.Ecriture, datum.Year, ct: ct)
            });
            await db.SaveChangesAsync(ct);
        }
    }

    private static GebuehrenPosten Posten(SkoulouDbContext db, Guid schuleId, Guid jahrId, string name, Guid? stufe, bool pflicht, int reihenfolge,
        params (long Betrag, DateOnly Faellig)[] raten)
    {
        var p = new GebuehrenPosten
        {
            SchuleId = schuleId, SchuljahrId = jahrId, Bezeichnung = name, Betrag = raten.Sum(r => r.Betrag), StufeId = stufe, Pflicht = pflicht, Reihenfolge = reihenfolge
        };
        db.GebuehrenPosten.Add(p);
        for (var i = 0; i < raten.Length; i++) db.GebuehrenRaten.Add(new GebuehrenRate { PostenId = p.Id, Nr = i + 1, Betrag = raten[i].Betrag, Faellig = raten[i].Faellig });
        return p;
    }

    private delegate Task<Zahlung?> Zahler(Guid jahrId, string name, long betrag, DateOnly datum, ZahlungsKanal kanal = ZahlungsKanal.Especes, string? referenz = null, Guid? von = null);

    /// <summary>Abgeschlossenes Jahr 2025-2026 (nur wenn die Schule noch kein früheres Jahr hat): 6e mit den heutigen 5e-Schülern.</summary>
    private static async Task VorjahrAsync(SkoulouDbContext db, NummernkreisService nummern, Guid schuleId, Schuljahr laufend,
        Dictionary<string, Guid> stufen, Dictionary<string, Konto> schueler, Guid direktor, Zahler zahlen, CancellationToken ct)
    {
        if (await db.Schuljahre.AnyAsync(j => j.SchuleId == schuleId && j.Beginn < laufend.Beginn, ct)) return;
        var vorjahr = new Schuljahr
        {
            SchuleId = schuleId, Bezeichnung = "2025-2026", Beginn = new(2025, 9, 15), Ende = new(2026, 6, 30), Status = SchuljahrStatus.Abgeschlossen,
            PeriodenArt = PeriodenArt.Trimestre, PlattformJahrespreis = 12_000, AbgeschlossenAmUtc = new DateTime(2026, 7, 10, 10, 0, 0, DateTimeKind.Utc)
        };
        db.Schuljahre.Add(vorjahr);
        var perioden = new[]
        {
            new Periode { SchuleId = schuleId, SchuljahrId = vorjahr.Id, Nr = 1, Titel = "1er trimestre", Beginn = new(2025, 9, 15), Ende = new(2025, 12, 19), Status = PeriodenStatus.Geschlossen },
            new Periode { SchuleId = schuleId, SchuljahrId = vorjahr.Id, Nr = 2, Titel = "2e trimestre", Beginn = new(2026, 1, 5), Ende = new(2026, 3, 27), Status = PeriodenStatus.Geschlossen },
            new Periode { SchuleId = schuleId, SchuljahrId = vorjahr.Id, Nr = 3, Titel = "3e trimestre", Beginn = new(2026, 4, 7), Ende = new(2026, 6, 30), Status = PeriodenStatus.Geschlossen },
        };
        db.Perioden.AddRange(perioden);
        var klasse = new Klasse { SchuleId = schuleId, SchuljahrId = vorjahr.Id, StufeId = stufen.GetValueOrDefault("6e"), Name = "6e A", Raum = "Salle 1", Kapazitaet = 45, Aktiv = false };
        db.Klassen.Add(klasse);
        var einschreibungen = new Dictionary<Guid, Guid>();
        foreach (var name in new[] { "Sossou", "Ahadji", "Kponton" })
            if (schueler.TryGetValue(name, out var s))
            {
                var e = new Einschreibung { SchuleId = schuleId, SchuelerId = s.Id, SchuljahrId = vorjahr.Id, KlasseId = klasse.Id, Von = vorjahr.Beginn, Bis = vorjahr.Ende, Status = EinschreibungsStatus.Terminee };
                db.Einschreibungen.Add(e);
                einschreibungen[s.Id] = e.Id;
            }
        Posten(db, schuleId, vorjahr.Id, "Scolarité", null, true, 1, (40_000, new(2025, 10, 6)), (40_000, new(2026, 1, 12)));
        await db.SaveChangesAsync(ct);

        await zahlen(vorjahr.Id, "Sossou", 40_000, new(2025, 10, 2));
        await zahlen(vorjahr.Id, "Kponton", 80_000, new(2025, 10, 3), ZahlungsKanal.Virement, "VIR ORABANK 1003");
        await zahlen(vorjahr.Id, "Ahadji", 40_000, new(2025, 10, 6), ZahlungsKanal.MobileMoney, "TMONEY 70117744");
        await zahlen(vorjahr.Id, "Sossou", 40_000, new(2026, 1, 9));
        await zahlen(vorjahr.Id, "Ahadji", 20_000, new(2026, 3, 2)); // 20.000 bleiben offen → « Reste année précédente »

        // Plattformrechnungen je Trimester: 3 Schüler × 4.000 (12.000 / 3), T1 und T2 bezahlt und bestätigt, T3 gemeldet.
        var schule = await db.Schulen.FirstAsync(s => s.Id == schuleId, ct);
        schule.ResponsableProzent ??= 10;
        var admin = await db.Users.Where(k => k.Rolle == Rollen.SuperAdmin).Select(k => (Guid?)k.Id).FirstOrDefaultAsync(ct);
        var kategorie = await FinanzKategorien.SystemAsync(db, FinanzKategorien.FactureSkoulou, ct);
        var daten = new (DateOnly Datum, RechnungsStatus Status, DateOnly Meldung, DateTime? Ausgezahlt)[]
        {
            (new(2025, 12, 20), RechnungsStatus.Payee, new(2026, 1, 8), new DateTime(2026, 2, 5, 12, 0, 0, DateTimeKind.Utc)),
            (new(2026, 3, 28), RechnungsStatus.Payee, new(2026, 4, 15), null),
            (new(2026, 7, 1), RechnungsStatus.PaiementDeclare, new(2026, 7, 20), null),
        };
        for (var i = 0; i < perioden.Length; i++)
        {
            var (datum, status, meldung, ausgezahlt) = daten[i];
            var r = new Plattformrechnung
            {
                Nummer = await nummern.NaechsteNummerAsync(db, null, NummernArt.Plattformrechnung, datum.Year, ct: ct),
                SchuleId = schuleId, SchuljahrId = vorjahr.Id, PeriodeId = perioden[i].Id, Datum = datum, Faellig = datum.AddDays(30),
                Anzahl = einschreibungen.Count, Betrag = 4_000L * einschreibungen.Count, Status = status, Erinnerungen = 0
            };
            db.Plattformrechnungen.Add(r);
            foreach (var (schuelerId, einschreibungId) in einschreibungen)
                db.Rechnungspositionen.Add(new Rechnungsposition { RechnungId = r.Id, PeriodeId = perioden[i].Id, SchuelerId = schuelerId, EinschreibungId = einschreibungId, Betrag = 4_000 });
            var buchung = new Buchung
            {
                SchuleId = schuleId, Datum = meldung, Richtung = BuchungsRichtung.Depense, KategorieId = kategorie.Id, Betrag = r.Betrag,
                Text = $"Facture SKOULOU {r.Nummer} – virement VIR-SKL-{i + 1:00}", Herkunft = BuchungsHerkunft.FactureSkoulou, HerkunftId = r.Id, ErfasstVonId = direktor,
                Nummer = await nummern.NaechsteNummerAsync(db, schuleId, NummernArt.Ecriture, meldung.Year, ct: ct)
            };
            db.Buchungen.Add(buchung);
            db.Zahlungsmeldungen.Add(new Zahlungsmeldung { RechnungId = r.Id, Betrag = r.Betrag, Datum = meldung, Referenz = $"VIR-SKL-{i + 1:00}", GemeldetVonId = direktor, BuchungId = buchung.Id });
            if (status == RechnungsStatus.Payee)
            {
                (r.BestaetigtVonId, r.BestaetigtAmUtc) = (admin, meldung.AddDays(3).ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc));
                if (schule.ResponsableId is { } responsable)
                    db.ResponsableAnteile.Add(new ResponsableAnteil
                    {
                        ResponsableId = responsable, RechnungId = r.Id, SchuleId = schuleId, Prozent = schule.ResponsableProzent!.Value,
                        Betrag = ResponsableVerguetung.Betrag(r.Betrag, schule.ResponsableProzent!.Value),
                        AusgezahltAmUtc = ausgezahlt, AuszahlungsReferenz = ausgezahlt is null ? null : "OM-2026-02-05", AusgezahltVonId = ausgezahlt is null ? null : admin
                    });
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
