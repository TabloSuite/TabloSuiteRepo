using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Finanzen;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Medien;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Finanzen;

public sealed record KategorieZeile(Guid Id, string Name, BuchungsRichtung Richtung, bool IstSystem, bool Eigene, bool Benutzt);
public sealed record BuchungEingabe(DateOnly? Datum, Guid? KategorieId, long? Betrag, string? Text);
public sealed record BuchungZeile(Guid Id, string Nummer, DateOnly Datum, BuchungsRichtung Richtung, string Kategorie, long Betrag, string Text,
    BuchungsHerkunft Herkunft, Guid? HerkunftId, bool HatBeleg, string ErfasstVon, bool Aufgehoben, bool IstGegenbuchung, long? SaldoNach);
public sealed record JournalFilter(DateOnly? Von, DateOnly? Bis, BuchungsRichtung? Richtung, Guid? KategorieId, string? Suche);
public sealed record JournalSummen(long Recettes, long Depenses)
{
    public long Saldo => Recettes - Depenses;
}
public sealed record KassenStand(long Saldo, long RecettesJahr, long DepensesJahr);
public sealed record BelegInhalt(Stream Inhalt, string ContentType, string Dateiname);

/// <summary>
/// Kassenjournal der Schule (nur echte Geldbewegungen, Bauplan §5 Regel 9): manuelle Écritures mit Beleg, Kategorien,
/// Korrektur nur per Gegenbuchung, Filter und laufender Saldo, Kassenstand. Schreiben: <see cref="Faehigkeit.BuchhaltungErfassen"/>.
/// </summary>
public sealed class JournalService(
    IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit, NummernkreisService nummern,
    IMedienSpeicher speicher, TimeProvider zeit, ILogger<JournalService> logger)
{
    private static readonly Faehigkeit[] Lesen = [Faehigkeit.BuchhaltungErfassen, Faehigkeit.FinanzenEinsehen];

    private DateOnly Heute => new SchulZeit(zeit).Heute;

    public async Task<IReadOnlyList<KategorieZeile>> KategorienAsync(Guid akteurId, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, false, ct, Lesen);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Kategorien.Where(k => (k.SchuleId == null || k.SchuleId == a.SchuleId) && k.Aktiv)
            .OrderBy(k => k.Richtung).ThenBy(k => k.SystemCode != null).ThenBy(k => k.Name)
            .Select(k => new KategorieZeile(k.Id, k.Name, k.Richtung, k.SystemCode != null, k.SchuleId == a.SchuleId,
                db.Buchungen.Any(b => b.KategorieId == k.Id && b.SchuleId == a.SchuleId)))
            .ToListAsync(ct);
    }

    public async Task<Guid> KategorieAnlegenAsync(Guid akteurId, string? name, BuchungsRichtung richtung, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, true, ct, Faehigkeit.BuchhaltungErfassen);
        var text = Eingabe.Pflicht(name, 100, "le nom de la catégorie", "name");
        return await dbFactory.TransaktionAsync(async db =>
        {
            if (await db.Kategorien.AnyAsync(k => (k.SchuleId == null || k.SchuleId == a.SchuleId) && k.Richtung == richtung && k.Name == text, ct))
                throw new FachFehler("Cette catégorie existe déjà.", "name");
            var kategorie = new Kategorie { SchuleId = a.SchuleId, Name = text, Richtung = richtung };
            db.Kategorien.Add(kategorie);
            await db.SaveChangesAsync(ct);
            return kategorie.Id;
        }, ct);
    }

    public async Task KategorieLoeschenAsync(Guid akteurId, Guid kategorieId, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, true, ct, Faehigkeit.BuchhaltungErfassen);
        await dbFactory.TransaktionAsync(async db =>
        {
            var k = await db.Kategorien.FirstOrDefaultAsync(x => x.Id == kategorieId, ct) ?? throw new FachFehler("Cette catégorie est introuvable.");
            if (k.SchuleId != a.SchuleId) throw new FachFehler("Seules les catégories créées par l'établissement peuvent être supprimées.");
            if (await db.Buchungen.AnyAsync(b => b.KategorieId == kategorieId, ct)) throw new FachFehler("Cette catégorie est utilisée par des écritures.");
            db.Kategorien.Remove(k);
            await db.LoeschenSpeichernAsync(ct);
        }, ct);
    }

    public async Task<Guid> ErfassenAsync(Guid akteurId, BuchungEingabe e, BelegDatei? beleg, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, true, ct, Faehigkeit.BuchhaltungErfassen);
        var betrag = FinanzEingabe.Betrag(e.Betrag);
        var datum = FinanzEingabe.Datum(e.Datum, Heute);
        var text = Eingabe.Pflicht(e.Text, 300, "le libellé", "text");
        if (e.KategorieId is not { } kategorieId) throw new FachFehler("Choisissez une catégorie.", "kategorie");

        Medium? medium = null;
        if (beleg is not null)
        {
            var (inhalt, typ) = await BelegSpeicher.LeseAsync(beleg.Inhalt, ct);
            medium = await BelegSpeicher.SpeichereAsync(speicher, a.SchuleId, a.Id, inhalt, typ, beleg.Dateiname, ct);
        }
        try
        {
            return await dbFactory.TransaktionAsync(async db =>
            {
                var kategorie = await db.Kategorien.FirstOrDefaultAsync(k => k.Id == kategorieId && (k.SchuleId == null || k.SchuleId == a.SchuleId) && k.Aktiv, ct)
                                ?? throw new FachFehler("Cette catégorie est introuvable.", "kategorie");
                if (kategorie.SystemCode is not null)
                    throw new FachFehler("Les catégories automatiques (scolarité, facture SKOULOU) ne se saisissent pas à la main.", "kategorie");
                var buchung = new Buchung
                {
                    SchuleId = a.SchuleId, Datum = datum, Richtung = kategorie.Richtung, KategorieId = kategorie.Id, Betrag = betrag, Text = text,
                    Nummer = await nummern.NaechsteNummerAsync(db, a.SchuleId, NummernArt.Ecriture, datum.Year, ct: ct),
                    Herkunft = BuchungsHerkunft.Manuelle, BelegMediumId = medium?.Id, ErfasstVonId = a.Id
                };
                if (medium is not null)
                {
                    db.Medien.Add(medium);
                    db.MedienNutzungen.Add(new MedienNutzung { MediumId = medium.Id, Typ = "Buchung.Beleg", ZielId = buchung.Id });
                }
                db.Buchungen.Add(buchung);
                await db.SaveChangesAsync(ct);
                return buchung.Id;
            }, ct);
        }
        catch
        {
            if (medium is not null) await BlobEntfernenAsync(medium.BlobName);
            throw;
        }
    }

    private async Task BlobEntfernenAsync(string blob)
    {
        try { await speicher.LoeschenAsync(blob); }
        catch (Exception ex) { logger.LogWarning(ex, "Beleg {Blob} nach Fehler nicht gelöscht.", blob); }
    }

    /// <summary>Hebt eine manuelle Écriture durch eine Gegenbuchung auf (nie löschen).</summary>
    public async Task GegenbuchungAsync(Guid akteurId, Guid buchungId, string? grund, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, true, ct, Faehigkeit.BuchhaltungErfassen);
        var motiv = Eingabe.Pflicht(grund, 200, "le motif de la correction", "grund");
        await dbFactory.TransaktionAsync(async db =>
        {
            var b = await db.Buchungen.FirstOrDefaultAsync(x => x.Id == buchungId, ct) ?? throw new FachFehler("Cette écriture est introuvable.");
            a.PruefeSchule(b.SchuleId);
            if (b.Herkunft == BuchungsHerkunft.Paiement) throw new FachFehler("Cette écriture vient d'un paiement : annulez le paiement (avec son reçu) dans « Paiements ».");
            if (b.Herkunft != BuchungsHerkunft.Manuelle || b.StornoVonId is not null) throw new FachFehler("Seules les écritures saisies à la main peuvent être corrigées ici.");
            if (await db.Buchungen.AnyAsync(x => x.StornoVonId == b.Id, ct)) throw new FachFehler("Cette écriture est déjà annulée.");
            var heute = Heute;
            db.Buchungen.Add(new Buchung
            {
                SchuleId = b.SchuleId, Datum = heute, KategorieId = b.KategorieId, Betrag = b.Betrag,
                Richtung = b.Richtung == BuchungsRichtung.Recette ? BuchungsRichtung.Depense : BuchungsRichtung.Recette,
                Text = Kassierer.Kuerze($"Annulation de {b.Nummer} : {motiv}"), Herkunft = BuchungsHerkunft.Annulation, HerkunftId = b.Id,
                StornoVonId = b.Id, ErfasstVonId = a.Id,
                Nummer = await nummern.NaechsteNummerAsync(db, b.SchuleId, NummernArt.Ecriture, heute.Year, ct: ct)
            });
            audit.Erfasse(db, a.Id, a.SchuleId, FinanzAudit.BuchungStorniert, nameof(Buchung), b.Id,
                $"Écriture {b.Nummer} ({SkoulouKultur.Fcfa(b.Betrag)}) annulée : {motiv}");
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task<Seite<BuchungZeile>> SucheAsync(Guid akteurId, JournalFilter f, int ueberspringen, int anzahl, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, false, ct, Lesen);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var basis = Gefiltert(db, a.SchuleId, f);
        var seite = await Seite<BuchungZeile>.LadeAsync(
            from b in basis.OrderByDescending(b => b.Datum).ThenByDescending(b => b.Nummer)
            join k in db.Kategorien on b.KategorieId equals k.Id
            join e in db.Users on b.ErfasstVonId equals e.Id
            select new BuchungZeile(b.Id, b.Nummer, b.Datum, b.Richtung, k.Name, b.Betrag, b.Text, b.Herkunft, b.HerkunftId,
                b.BelegMediumId != null, e.Vorname + " " + e.Nachname, db.Buchungen.Any(g => g.StornoVonId == b.Id), b.StornoVonId != null, null),
            ueberspringen, anzahl, ct);

        // Laufender Saldo nur ohne Richtungs-, Kategorie- und Textfilter (sonst wäre er irreführend).
        if (seite.Eintraege.Count == 0 || f.Richtung is not null || f.KategorieId is not null || !string.IsNullOrWhiteSpace(f.Suche)) return seite;
        var erste = seite.Eintraege[0];
        var saldo = await SaldoAsync(db.Buchungen.Where(b => b.SchuleId == a.SchuleId
            && (b.Datum < erste.Datum || (b.Datum == erste.Datum && string.Compare(b.Nummer, erste.Nummer) <= 0))), ct);
        var zeilen = new List<BuchungZeile>(seite.Eintraege.Count);
        foreach (var z in seite.Eintraege)
        {
            zeilen.Add(z with { SaldoNach = saldo });
            saldo -= Kasse.MitVorzeichen(z.Richtung, z.Betrag);
        }
        return new Seite<BuchungZeile>(zeilen, seite.Mehr);
    }

    public async Task<JournalSummen> SummenAsync(Guid akteurId, JournalFilter f, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, false, ct, Lesen);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var basis = Gefiltert(db, a.SchuleId, f);
        return new JournalSummen(
            await basis.Where(b => b.Richtung == BuchungsRichtung.Recette).SumAsync(b => (long?)b.Betrag, ct) ?? 0,
            await basis.Where(b => b.Richtung == BuchungsRichtung.Depense).SumAsync(b => (long?)b.Betrag, ct) ?? 0);
    }

    /// <summary>Kassenstand (alle Buchungen) und Bewegungen im Zeitraum des Schuljahres.</summary>
    public async Task<KassenStand> KasseAsync(Guid akteurId, Guid jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.DirektionAsync(akteurId, false, ct, Lesen);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        var alle = db.Buchungen.Where(b => b.SchuleId == a.SchuleId);
        var imJahr = alle.Where(b => b.Datum >= jahr.Beginn && b.Datum <= jahr.Ende);
        return new KassenStand(await SaldoAsync(alle, ct),
            await imJahr.Where(b => b.Richtung == BuchungsRichtung.Recette).SumAsync(b => (long?)b.Betrag, ct) ?? 0,
            await imJahr.Where(b => b.Richtung == BuchungsRichtung.Depense).SumAsync(b => (long?)b.Betrag, ct) ?? 0);
    }

    internal static async Task<long> SaldoAsync(IQueryable<Buchung> buchungen, CancellationToken ct)
        => (await buchungen.Where(b => b.Richtung == BuchungsRichtung.Recette).SumAsync(b => (long?)b.Betrag, ct) ?? 0)
           - (await buchungen.Where(b => b.Richtung == BuchungsRichtung.Depense).SumAsync(b => (long?)b.Betrag, ct) ?? 0);

    private static IQueryable<Buchung> Gefiltert(SkoulouDbContext db, Guid schuleId, JournalFilter f)
    {
        var q = db.Buchungen.Where(b => b.SchuleId == schuleId);
        if (f.Von is { } von) q = q.Where(b => b.Datum >= von);
        if (f.Bis is { } bis) q = q.Where(b => b.Datum <= bis);
        if (f.Richtung is { } r) q = q.Where(b => b.Richtung == r);
        if (f.KategorieId is { } k) q = q.Where(b => b.KategorieId == k);
        if (Eingabe.Suche(f.Suche) is { } s)
        {
            var muster = $"%{s}%";
            q = q.Where(b => EF.Functions.Like(b.Nummer, muster) || EF.Functions.Like(b.Text, muster));
        }
        return q;
    }

    /// <summary>Beleg einer Buchung: Direktion mit Finanzrecht, Responsable (eigene Schulen) und Admin lesend.</summary>
    public async Task<BelegInhalt> BelegAsync(Guid akteurId, Guid buchungId, CancellationToken ct = default)
    {
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var b = await db.Buchungen.AsNoTracking().Where(x => x.Id == buchungId).Select(x => new { x.SchuleId, x.BelegMediumId, x.Herkunft }).FirstOrDefaultAsync(ct)
                ?? throw new FachFehler("Ce justificatif est introuvable.");
        if (akteur.IstDirektion)
        {
            if (akteur.SchuleId != b.SchuleId) throw new KeinZugriffFehler(Zugriff.MeldungAndereSchule);
            var darf = FinanzRechte.Hat(akteur, Faehigkeit.FinanzenEinsehen) || FinanzRechte.Hat(akteur, Faehigkeit.BuchhaltungErfassen)
                       || (b.Herkunft == BuchungsHerkunft.FactureSkoulou && FinanzRechte.Hat(akteur, Faehigkeit.RechnungSkoulouBearbeiten));
            if (!darf) throw new KeinZugriffFehler();
        }
        else if (akteur.Rolle is Rollen.Responsable or Rollen.SuperAdmin)
        {
            Zugriff.Pruefe(akteur, Faehigkeit.FinanzenEinsehen);
            await zugriff.PruefeSchuleAsync(akteur, b.SchuleId, ct);
        }
        else throw new KeinZugriffFehler();

        if (b.BelegMediumId is not { } mediumId) throw new FachFehler("Cette écriture n'a pas de justificatif.");
        var m = await db.Medien.AsNoTracking().FirstAsync(x => x.Id == mediumId, ct);
        var strom = await speicher.OeffnenAsync(m.BlobName, ct) ?? throw new FachFehler("Le fichier du justificatif est introuvable.");
        return new BelegInhalt(strom, m.ContentType, m.Dateiname);
    }
}
