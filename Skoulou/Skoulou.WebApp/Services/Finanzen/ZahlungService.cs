using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Finanzen;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Finanzen;

/// <summary>Eingabe einer Zahlung; <paramref name="VorgangId"/> wird beim Öffnen des Dialogs erzeugt (Doppelklickschutz).</summary>
public sealed record ZahlungEingabe(Guid VorgangId, Guid SchuelerId, long? Betrag, DateOnly? Datum, ZahlungsKanal Kanal, string? Referenz,
    string? Bemerkung, bool GuthabenErlaubt = false);
public sealed record ZahlungErgebnis(Guid ZahlungId, string Quittungsnummer, long Rest, long Guthaben, int Mails, bool Wiederholt);
public sealed record ZahlungZeile(Guid Id, string Quittungsnummer, DateOnly Datum, Guid SchuelerId, string Schueler, string? Matrikel,
    long Betrag, ZahlungsKanal Kanal, string? Referenz, string ErfasstVon, bool Storniert, string? StornoGrund);
public sealed record ZahlungZaehler(int Alle, long HeuteSumme, int HeuteAnzahl, long MonatSumme, long JahrSumme, int Storniert);
public sealed record SchuelerAuswahl(Guid Id, string Name, string? Matrikel, string? Klasse, long Soll, long Offen, long EnRetard, long Guthaben);

/// <summary>
/// Kasse der Schule: Zahlung erfassen (Quittungsnummer REC-JJJJ-00001 und Journalbuchung in derselben Transaktion,
/// Doppelklick über die Vorgangs-Id abgefangen), stornieren mit Pflichtgrund (Gegenbuchung, nie löschen), Liste, Schülersuche.
/// </summary>
public sealed class ZahlungService(
    IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit, NummernkreisService nummern,
    FinanzMails mails, TimeProvider zeit)
{
    public const string FilterEspeces = "especes";
    public const string FilterMobile = "mobile";
    public const string FilterBanque = "banque";
    public const string FilterAnnules = "annules";

    private static readonly Faehigkeit[] Lesen = [Faehigkeit.ZahlungenErfassen, Faehigkeit.FinanzenEinsehen];

    private DateOnly Heute => new SchulZeit(zeit).Heute;

    public async Task<ZahlungErgebnis> ErfassenAsync(Guid akteurId, Guid jahrId, ZahlungEingabe e, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, true, ct, Faehigkeit.ZahlungenErfassen);
        if (e.VorgangId == Guid.Empty) throw new FachFehler("Rechargez la page avant d'enregistrer le paiement.");
        if (await VorhandenAsync(a, e.VorgangId, ct) is { } schon) return schon;

        var betrag = FinanzEingabe.Betrag(e.Betrag);
        var datum = FinanzEingabe.Datum(e.Datum, Heute);
        var referenz = Eingabe.Text(e.Referenz, 100, "la référence", e.Kanal != ZahlungsKanal.Especes, "referenz");
        var bemerkung = Eingabe.Text(e.Bemerkung, 300, "la remarque", feld: "bemerkung");

        (Zahlung Zahlung, long Offen, long Guthaben, string Schule, string Vorname) ergebnis;
        try
        {
            ergebnis = await dbFactory.TransaktionAsync(async db =>
            {
                var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
                OrgAbfragen.PruefeOffen(jahr);
                var schueler = await OrgAbfragen.KontoAsync(db, a, e.SchuelerId, ct, Rollen.Schueler);
                var konto = await KontoLader.KontoAsync(db, a.SchuleId, jahrId, e.SchuelerId, Heute, ct);
                if (betrag > konto.Offen && !e.GuthabenErlaubt)
                    throw new FachFehler(konto.Offen == 0
                        ? "Cet élève n'a plus rien à payer pour cette année. Confirmez pour enregistrer le montant comme avoir."
                        : $"Le montant dépasse le reste à payer ({SkoulouKultur.Fcfa(konto.Offen)}). Confirmez pour enregistrer la différence comme avoir.", "guthaben");
                var zahlung = await Kassierer.ZahlungBuchenAsync(db, nummern, a.SchuleId, jahrId, a.Id, e.VorgangId, schueler, betrag, datum,
                    e.Kanal, referenz, bemerkung, ct);
                audit.Erfasse(db, a.Id, a.SchuleId, AuditAktionen.Zahlung, nameof(Zahlung), zahlung.Id,
                    $"Paiement {zahlung.Quittungsnummer} : {SkoulouKultur.Fcfa(betrag)} ({FinanzEingabe.KanalText(e.Kanal)}) pour {schueler.VollerName}.");
                await db.SaveChangesAsync(ct);
                var schule = await db.Schulen.Where(s => s.Id == a.SchuleId).Select(s => s.OffizielleBezeichnung ?? s.Name).FirstAsync(ct);
                return (zahlung, Math.Max(0, konto.Offen - betrag), Math.Max(0, betrag - konto.Offen) + konto.Guthaben, schule, schueler.Vorname);
            }, ct);
        }
        catch (FachFehler)
        {
            // Zweiter Klick oder paralleler Aufruf (Eindeutigkeit der Id): die erste Zahlung gilt.
            if (await VorhandenAsync(a, e.VorgangId, ct) is { } erste) return erste;
            throw;
        }

        var z = ergebnis.Zahlung;
        var gesendet = await mails.QuittungAsync(await EmpfaengerAsync(z.SchuelerId, ct), ergebnis.Vorname, ergebnis.Schule, z.Quittungsnummer,
            z.Betrag, z.Datum, FinanzEingabe.KanalText(z.Kanal), ergebnis.Offen, ct);
        return new ZahlungErgebnis(z.Id, z.Quittungsnummer, ergebnis.Offen, ergebnis.Guthaben, gesendet, false);
    }

    private async Task<ZahlungErgebnis?> VorhandenAsync(SchulAkteur a, Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var z = await db.Zahlungen.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.SchuleId, x.Quittungsnummer }).FirstOrDefaultAsync(ct);
        if (z is null) return null;
        a.PruefeSchule(z.SchuleId);
        return new ZahlungErgebnis(id, z.Quittungsnummer, 0, 0, 0, true);
    }

    /// <summary>Adressen für die Quittung: Schüler, verknüpfte Eltern, Elternmail des Profils (Bauplan §11.12).</summary>
    private async Task<List<string?>> EmpfaengerAsync(Guid schuelerId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var liste = await db.Users.Where(k => k.Id == schuelerId).Select(k => k.Email).ToListAsync(ct);
        liste.AddRange(await (from ek in db.ElternKinder
                              where ek.SchuelerId == schuelerId
                              join k in db.Users on ek.ElternId equals k.Id
                              where k.Status == KontoStatus.Actif
                              select k.Email).ToListAsync(ct));
        liste.AddRange(await db.SchuelerProfile.Where(p => p.KontoId == schuelerId).Select(p => p.ElternEmail).ToListAsync(ct));
        return liste;
    }

    public async Task StornierenAsync(Guid akteurId, Guid zahlungId, string? grund, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, true, ct, Faehigkeit.ZahlungenErfassen);
        var motiv = Eingabe.Pflicht(grund, 300, "le motif de l'annulation", "grund");
        await dbFactory.TransaktionAsync(async db =>
        {
            var z = await db.Zahlungen.FirstOrDefaultAsync(x => x.Id == zahlungId, ct) ?? throw new FachFehler("Ce paiement est introuvable.");
            a.PruefeSchule(z.SchuleId);
            if (z.StorniertAmUtc is not null) throw new FachFehler("Ce paiement est déjà annulé.");
            OrgAbfragen.PruefeOffen(await OrgAbfragen.SchuljahrAsync(db, a, z.SchuljahrId, ct));
            await Kassierer.ZahlungStornierenAsync(db, nummern, z, a.Id, motiv, Heute, zeit.GetUtcNow().UtcDateTime, ct);
            audit.Erfasse(db, a.Id, a.SchuleId, AuditAktionen.ZahlungStorniert, nameof(Zahlung), z.Id,
                $"Paiement {z.Quittungsnummer} ({SkoulouKultur.Fcfa(z.Betrag)}) annulé : {motiv}");
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task<Seite<ZahlungZeile>> SucheAsync(Guid akteurId, Guid jahrId, string? filter, string? suche, int ueberspringen, int anzahl,
        CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, false, ct, Lesen);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        var basis = db.Zahlungen.Where(z => z.SchuleId == a.SchuleId && z.SchuljahrId == jahrId);
        basis = filter switch
        {
            FilterEspeces => basis.Where(z => z.Kanal == ZahlungsKanal.Especes && z.StorniertAmUtc == null),
            FilterMobile => basis.Where(z => z.Kanal == ZahlungsKanal.MobileMoney && z.StorniertAmUtc == null),
            FilterBanque => basis.Where(z => (z.Kanal == ZahlungsKanal.Virement || z.Kanal == ZahlungsKanal.Cheque) && z.StorniertAmUtc == null),
            FilterAnnules => basis.Where(z => z.StorniertAmUtc != null),
            _ => basis
        };
        var abfrage = from z in basis
                      join k in db.Users on z.SchuelerId equals k.Id
                      join e in db.Users on z.ErfasstVonId equals e.Id
                      join p in db.SchuelerProfile on k.Id equals p.KontoId into pj
                      from p in pj.DefaultIfEmpty()
                      select new { z, k, e, Matrikel = p == null ? null : p.Matrikel };
        if (Eingabe.Suche(suche) is { } s)
        {
            var muster = $"%{s}%";
            abfrage = abfrage.Where(x => EF.Functions.Like(x.z.Quittungsnummer, muster) || EF.Functions.Like(x.k.Nachname + " " + x.k.Vorname, muster)
                                         || EF.Functions.Like(x.k.Vorname + " " + x.k.Nachname, muster)
                                         || (x.Matrikel != null && EF.Functions.Like(x.Matrikel, muster))
                                         || (x.z.Referenz != null && EF.Functions.Like(x.z.Referenz, muster)));
        }
        var projektion = abfrage.OrderByDescending(x => x.z.Datum).ThenByDescending(x => x.z.ErstelltAmUtc)
            .Select(x => new ZahlungZeile(x.z.Id, x.z.Quittungsnummer, x.z.Datum, x.z.SchuelerId, x.k.Nachname + " " + x.k.Vorname, x.Matrikel,
                x.z.Betrag, x.z.Kanal, x.z.Referenz, x.e.Vorname + " " + x.e.Nachname, x.z.StorniertAmUtc != null, x.z.StornoGrund));
        return await Seite<ZahlungZeile>.LadeAsync(projektion, ueberspringen, anzahl, ct);
    }

    public async Task<ZahlungZaehler> ZaehlerAsync(Guid akteurId, Guid jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, false, ct, Lesen);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        var heute = Heute;
        var monat = new DateOnly(heute.Year, heute.Month, 1);
        var jahr = db.Zahlungen.Where(z => z.SchuleId == a.SchuleId && z.SchuljahrId == jahrId);
        var gueltig = jahr.Where(z => z.StorniertAmUtc == null);
        return new ZahlungZaehler(
            await jahr.CountAsync(ct),
            await gueltig.Where(z => z.Datum == heute).SumAsync(z => (long?)z.Betrag, ct) ?? 0,
            await gueltig.CountAsync(z => z.Datum == heute, ct),
            await gueltig.Where(z => z.Datum >= monat).SumAsync(z => (long?)z.Betrag, ct) ?? 0,
            await gueltig.SumAsync(z => (long?)z.Betrag, ct) ?? 0,
            await jahr.CountAsync(z => z.StorniertAmUtc != null, ct));
    }

    /// <summary>Schüler für den Zahlungsdialog (Name, Identifiant, Matrikel) mit Reste à payer im gewählten Jahr.</summary>
    public async Task<IReadOnlyList<SchuelerAuswahl>> SchuelerSuchenAsync(Guid akteurId, Guid jahrId, string? suche, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, false, ct, Lesen);
        if (Eingabe.Suche(suche) is not { Length: >= 2 } s) return [];
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        var muster = $"%{s}%";
        var ids = await (from k in db.Users
                         where k.SchuleId == a.SchuleId && k.Rolle == Rollen.Schueler
                         join p in db.SchuelerProfile on k.Id equals p.KontoId into pj
                         from p in pj.DefaultIfEmpty()
                         where EF.Functions.Like(k.Nachname + " " + k.Vorname, muster) || EF.Functions.Like(k.Vorname + " " + k.Nachname, muster)
                               || EF.Functions.Like(k.UserName!, muster) || (p != null && p.Matrikel != null && EF.Functions.Like(p.Matrikel, muster))
                         orderby k.Nachname, k.Vorname
                         select k.Id).Take(12).ToListAsync(ct);
        var konten = await KontoLader.KontenAsync(db, a.SchuleId, jahrId, ids, Heute, ct);
        var koepfe = await KontoLader.KoepfeAsync(db, jahrId, ids, ct);
        return ids.Where(koepfe.ContainsKey).Select(id =>
        {
            var k = koepfe[id];
            var konto = konten.GetValueOrDefault(id) ?? GebuehrenKonto.Leer(Heute);
            return new SchuelerAuswahl(id, k.Name, k.Matrikel, k.Klasse, konto.Soll, konto.Offen, konto.EnRetardBetrag, konto.Guthaben);
        }).ToList();
    }
}

/// <summary>Buchungslogik der Kasse ohne Rechteprüfung (Dienste nach der Prüfung, Demo-Seed); der Aufrufer speichert in seiner Transaktion.</summary>
internal static class Kassierer
{
    public static async Task<Zahlung> ZahlungBuchenAsync(SkoulouDbContext db, NummernkreisService nummern, Guid schuleId, Guid jahrId, Guid erfasstVon,
        Guid zahlungId, Konto schueler, long betrag, DateOnly datum, ZahlungsKanal kanal, string? referenz, string? bemerkung, CancellationToken ct)
    {
        var kategorie = await FinanzKategorien.SystemAsync(db, FinanzKategorien.Scolarite, ct);
        var zahlung = new Zahlung
        {
            Id = zahlungId, SchuleId = schuleId, SchuelerId = schueler.Id, SchuljahrId = jahrId, Betrag = betrag, Datum = datum, Kanal = kanal,
            Referenz = referenz, Bemerkung = bemerkung, ErfasstVonId = erfasstVon,
            Quittungsnummer = await nummern.NaechsteNummerAsync(db, schuleId, NummernArt.Quittung, datum.Year, ct: ct)
        };
        db.Zahlungen.Add(zahlung);
        db.Buchungen.Add(new Buchung
        {
            SchuleId = schuleId, Datum = datum, Richtung = BuchungsRichtung.Recette, KategorieId = kategorie.Id, Betrag = betrag,
            Text = Kuerze($"Paiement {zahlung.Quittungsnummer} – {schueler.Nachname} {schueler.Vorname} ({FinanzEingabe.KanalText(kanal)})"),
            Nummer = await nummern.NaechsteNummerAsync(db, schuleId, NummernArt.Ecriture, datum.Year, ct: ct),
            Herkunft = BuchungsHerkunft.Paiement, HerkunftId = zahlung.Id, ErfasstVonId = erfasstVon
        });
        return zahlung;
    }

    public static async Task ZahlungStornierenAsync(SkoulouDbContext db, NummernkreisService nummern, Zahlung z, Guid akteurId, string grund,
        DateOnly heute, DateTime jetztUtc, CancellationToken ct)
    {
        (z.StorniertAmUtc, z.StorniertVonId, z.StornoGrund) = (jetztUtc, akteurId, grund);
        var original = await db.Buchungen.FirstOrDefaultAsync(b => b.Herkunft == BuchungsHerkunft.Paiement && b.HerkunftId == z.Id, ct);
        if (original is null) return;
        db.Buchungen.Add(new Buchung
        {
            SchuleId = z.SchuleId, Datum = heute, Richtung = BuchungsRichtung.Depense, KategorieId = original.KategorieId, Betrag = original.Betrag,
            Text = Kuerze($"Annulation du paiement {z.Quittungsnummer} : {grund}"),
            Nummer = await nummern.NaechsteNummerAsync(db, z.SchuleId, NummernArt.Ecriture, heute.Year, ct: ct),
            Herkunft = BuchungsHerkunft.AnnulationPaiement, HerkunftId = z.Id, StornoVonId = original.Id, ErfasstVonId = akteurId
        });
    }

    public static string Kuerze(string text) => text.Length > 300 ? text[..300] : text;
}
