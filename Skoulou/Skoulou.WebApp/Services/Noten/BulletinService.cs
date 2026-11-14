using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Noten;

public sealed record VeroeffentlichungsErgebnis(int Bulletins, int Mails);

/// <summary>Wirkung einer Rectification auf ein Bulletin (alt → neu).</summary>
public sealed record RectificationAenderung(Guid EinschreibungId, string Schueler, string Periode, string? AltNummer, string AltMoyenne, string NeuMoyenne,
    string AltRang, string NeuRang, bool Neu);

public sealed record RectificationVorschau(IReadOnlyList<RectificationAenderung> Aenderungen, string Hash);

public sealed record UebergabeZeile(DateTime ZeitUtc, UebergabeArt Art, string? Vermerk, string Von);

/// <summary>
/// Veröffentlichung (Bauplan §4.3, §11.16): Klasse und Periode auf einmal, Snapshot ohne Bildbytes + Hash, fortlaufende Nummer
/// « BUL-JJJJ-00001 » (Jahr = Beginn des Schuljahres), 64-Hex-Prüftoken, Mail an die Familien. Danach Berichtigung
/// (Rectification: neues Bulletin « rectificatif », altes « remplacé »), Rückzug mit Grund, Übergabeprotokoll.
/// </summary>
public sealed class BulletinService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit,
    NummernkreisService nummern, NotenMails mails, TimeProvider zeit)
{
    private sealed record Neu(Guid Id, string Periode, string Jahr, string Schule, bool Rectificatif);

    public async Task<VeroeffentlichungsErgebnis> VeroeffentlichenAsync(Guid akteurId, Guid klasseId, Guid periodeId, string erwarteterHash,
        bool trotzNichtKlassierter, CancellationToken ct = default)
        => await VeroeffentlichenKernAsync(await zugriff.SchuleAsync(akteurId, Faehigkeit.BulletinsVeroeffentlichen, ct: ct), klasseId, periodeId,
            erwarteterHash, trotzNichtKlassierter, mitMails: true, ct);

    /// <summary>Kern der Veröffentlichung nach der Rechteprüfung (auch für die Demo-Daten; Hash null = ohne Vergleich).</summary>
    internal async Task<VeroeffentlichungsErgebnis> VeroeffentlichenKernAsync(SchulAkteur a, Guid klasseId, Guid periodeId, string? erwarteterHash,
        bool trotzNichtKlassierter, bool mitMails, CancellationToken ct)
    {
        var (neue, empfaenger, anzahl) = await dbFactory.TransaktionAsync(async db =>
        {
            var (klasse, periode, jahr) = await KlassePeriodeAsync(db, a, klasseId, periodeId, ct);
            if (await db.KlassenVeroeffentlichungen.AnyAsync(v => v.KlasseId == klasse.Id && v.PeriodeId == periode.Id, ct))
                throw new FachFehler("Les bulletins de cette classe sont déjà publiés pour cette période.");
            var b = await BulletinBerechnung.LadeAsync(db, klasse, periode, ct);
            if (b.Kohorte.Count == 0) throw new FachFehler("Aucun élève n'est inscrit dans cette classe à la fin de la période.");
            var (inhalte, hash) = b.AlleInhalte();
            if (erwarteterHash is not null && hash != erwarteterHash)
                throw new FachFehler("Les données ont changé depuis votre vérification (notes ou conseil). Vérifiez à nouveau la classe.");
            var nichtKlassiert = b.Kohorte.Count(s => !b.ErgebnisVon(s.SchuelerId).Rang.Klassiert);
            if (nichtKlassiert > 0 && !trotzNichtKlassierter)
                throw new FachFehler($"{nichtKlassiert} élève(s) ont des notes incomplètes : complétez-les ou confirmez la publication avec des élèves non classés.");

            var ids = new List<Guid>();
            foreach (var (s, inhalt, h) in inhalte)
                ids.Add(await NeuesBulletinAsync(db, b, s, inhalt, h, null, null, ct));
            db.KlassenVeroeffentlichungen.Add(new KlassenVeroeffentlichung
            {
                SchuleId = a.SchuleId, KlasseId = klasse.Id, PeriodeId = periode.Id, Status = VeroeffentlichungsStatus.Publiee,
                VeroeffentlichtAmUtc = zeit.GetUtcNow().UtcDateTime, VeroeffentlichtVonId = a.Id
            });
            audit.Erfasse(db, a.Id, a.SchuleId, AuditAktionen.Veroeffentlicht, nameof(Klasse), klasse.Id,
                $"{ids.Count} bulletin(s) publié(s) : {klasse.Name}, {periode.Titel} ({jahr.Bezeichnung}){(nichtKlassiert > 0 ? $", dont {nichtKlassiert} non classé(s)" : "")}.");
            await db.SaveChangesAsync(ct);
            var empf = await ProtokolliereBenachrichtigungAsync(db, a, ids, ct);
            return (new Neu(klasse.Id, periode.Titel, jahr.Bezeichnung, b.Schule.OffizielleBezeichnung ?? b.Schule.Name, false), empf, ids.Count);
        }, ct);
        var gesendet = mitMails ? await mails.BulletinVerfuegbarAsync(empfaenger, neue.Periode, neue.Jahr, neue.Schule, false, ct) : 0;
        return new VeroeffentlichungsErgebnis(anzahl, gesendet);
    }

    /// <summary>Legt ein Bulletin an (Nummer, Token, Snapshot, Hash) und liefert seine Id.</summary>
    private async Task<Guid> NeuesBulletinAsync(SkoulouDbContext db, BulletinBerechnung b, KohortenSchueler s, BulletinInhalt inhalt, string hash,
        Bulletin? ersetzt, string? grund, CancellationToken ct)
    {
        var nummer = await nummern.NaechsteNummerAsync(db, b.Schule.Id, NummernArt.Bulletin, b.Jahr.Beginn.Year, ct: ct);
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var heute = new SchulZeit(zeit, b.Schule.Zeitzone).Heute;
        var dokument = new SnapshotDokument(nummer, SkoulouKultur.Datum(heute), token, ersetzt?.Nummer, ersetzt is not null, grund, false);
        var bulletin = new Bulletin
        {
            SchuleId = b.Schule.Id, PeriodeId = b.Periode.Id, EinschreibungId = s.EinschreibungId, Nummer = nummer, PruefToken = token,
            Status = BulletinStatus.Publie, ErsetztId = ersetzt?.Id, IstRectificatif = ersetzt is not null, Grund = grund,
            SnapshotJson = new BulletinSnapshot(1, inhalt, dokument).AlsJson(), Hash = hash,
            LogoMediumId = b.Schule.LogoMediumId, FotoMediumId = inhalt.Layout.ZeigeFoto ? s.FotoMediumId : null,
            VeroeffentlichtAmUtc = zeit.GetUtcNow().UtcDateTime
        };
        db.Bulletins.Add(bulletin);
        return bulletin.Id;
    }

    private async Task<List<BulletinEmpfaenger>> ProtokolliereBenachrichtigungAsync(SkoulouDbContext db, SchulAkteur a, List<Guid> ids, CancellationToken ct)
    {
        var empfaenger = await NotenMails.EmpfaengerAsync(db, ids, ct);
        foreach (var g in empfaenger.GroupBy(e => e.BulletinId))
            db.BulletinUebergaben.Add(new BulletinUebergabe
            {
                SchuleId = a.SchuleId, BulletinId = g.Key, Art = UebergabeArt.Notification, VonId = a.Id,
                Vermerk = $"Avis envoyé par e-mail ({g.Count()} adresse{(g.Count() > 1 ? "s" : "")})."
            });
        await db.SaveChangesAsync(ct);
        return empfaenger;
    }

    // ---------- Rectification ----------

    public async Task RectificationOeffnenAsync(Guid akteurId, Guid klasseId, Guid periodeId, string? grund, CancellationToken ct = default)
        => await RectificationOeffnenKernAsync(await zugriff.SchuleAsync(akteurId, Faehigkeit.BulletinsVeroeffentlichen, ct: ct), klasseId, periodeId, grund, ct);

    internal async Task RectificationOeffnenKernAsync(SchulAkteur a, Guid klasseId, Guid periodeId, string? grund, CancellationToken ct)
    {
        var text = NotenFormat.Text(grund, 300, "Le motif") ?? throw new FachFehler("Indiquez le motif de la rectification.", "grund");
        await dbFactory.TransaktionAsync(async db =>
        {
            var (klasse, periode, _) = await KlassePeriodeAsync(db, a, klasseId, periodeId, ct);
            var v = await db.KlassenVeroeffentlichungen.FirstOrDefaultAsync(x => x.KlasseId == klasse.Id && x.PeriodeId == periode.Id, ct);
            if (v is null) throw new FachFehler("Les bulletins de cette classe ne sont pas encore publiés pour cette période.");
            if (v.Status == VeroeffentlichungsStatus.EnRectification) throw new FachFehler("Une rectification est déjà ouverte pour cette classe.");
            v.Status = VeroeffentlichungsStatus.EnRectification;
            v.RectificationGrund = text;
            audit.Erfasse(db, a.Id, a.SchuleId, NotenAudit.RectificationGeoeffnet, nameof(Klasse), klasse.Id, $"Rectification ouverte : {klasse.Name}, {periode.Titel}. Motif : {text}");
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task<RectificationVorschau> RectificationVorschauAsync(Guid akteurId, Guid klasseId, Guid periodeId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.BulletinsVeroeffentlichen, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (klasse, periode, _) = await KlassePeriodeAsync(db, a, klasseId, periodeId, ct);
        await RectificationOffenAsync(db, klasse.Id, periode.Id, ct);
        var wirkungen = await WirkungenAsync(db, klasse, periode, ct);
        return new RectificationVorschau(wirkungen.Select(w => w.Aenderung).ToList(), BulletinSnapshot.HashVon(wirkungen.Select(w => w.Hash)));
    }

    public async Task<int> RectificationVeroeffentlichenAsync(Guid akteurId, Guid klasseId, Guid periodeId, string erwarteterHash, CancellationToken ct = default)
        => await RectificationVeroeffentlichenKernAsync(await zugriff.SchuleAsync(akteurId, Faehigkeit.BulletinsVeroeffentlichen, ct: ct), klasseId, periodeId, erwarteterHash, true, ct);

    /// <summary>Kern nach der Rechteprüfung (Hash null = ohne Vergleich, nur Demo-Daten).</summary>
    internal async Task<int> RectificationVeroeffentlichenKernAsync(SchulAkteur a, Guid klasseId, Guid periodeId, string? erwarteterHash, bool mitMails, CancellationToken ct)
    {
        var (info, empfaenger, anzahl) = await dbFactory.TransaktionAsync(async db =>
        {
            var (klasse, periode, jahr) = await KlassePeriodeAsync(db, a, klasseId, periodeId, ct);
            var v = await RectificationOffenAsync(db, klasse.Id, periode.Id, ct);
            var wirkungen = await WirkungenAsync(db, klasse, periode, ct);
            if (erwarteterHash is not null && BulletinSnapshot.HashVon(wirkungen.Select(w => w.Hash)) != erwarteterHash)
                throw new FachFehler("Les données ont changé depuis l'aperçu. Recalculez les bulletins concernés.");
            if (wirkungen.Count == 0) throw new FachFehler("Aucun bulletin n'a changé : abandonnez la rectification ou corrigez d'abord les notes.");
            var ids = new List<Guid>();
            foreach (var w in wirkungen)
            {
                Bulletin? alt = null;
                if (w.AltId is { } altId)
                {
                    alt = await db.Bulletins.FirstAsync(x => x.Id == altId, ct);
                    alt.Status = BulletinStatus.Remplace;
                    await db.SaveChangesAsync(ct);
                }
                ids.Add(await NeuesBulletinAsync(db, w.Berechnung, w.Schueler, w.Inhalt, w.Hash, alt, v.RectificationGrund, ct));
            }
            v.Status = VeroeffentlichungsStatus.Publiee;
            audit.Erfasse(db, a.Id, a.SchuleId, AuditAktionen.Berichtigt, nameof(Klasse), klasse.Id,
                $"Rectification publiée : {klasse.Name}, {periode.Titel} – {ids.Count} bulletin(s) remplacé(s) ou ajouté(s). Motif : {v.RectificationGrund}");
            await db.SaveChangesAsync(ct);
            var empf = await ProtokolliereBenachrichtigungAsync(db, a, ids, ct);
            var schule = await db.Schulen.AsNoTracking().Where(s => s.Id == a.SchuleId).Select(s => s.OffizielleBezeichnung ?? s.Name).FirstAsync(ct);
            return (new Neu(klasse.Id, periode.Titel, jahr.Bezeichnung, schule, true), empf, ids.Count);
        }, ct);
        if (mitMails) await mails.BulletinVerfuegbarAsync(empfaenger, info.Periode, info.Jahr, info.Schule, true, ct);
        return anzahl;
    }

    /// <summary>Abbrechen nur, solange sich kein Bulletin ändern würde (sonst weichen Noten und Dokumente still voneinander ab).</summary>
    public async Task RectificationAbbrechenAsync(Guid akteurId, Guid klasseId, Guid periodeId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.BulletinsVeroeffentlichen, ct: ct);
        await dbFactory.TransaktionAsync(async db =>
        {
            var (klasse, periode, _) = await KlassePeriodeAsync(db, a, klasseId, periodeId, ct);
            var v = await RectificationOffenAsync(db, klasse.Id, periode.Id, ct);
            var wirkungen = await WirkungenAsync(db, klasse, periode, ct);
            if (wirkungen.Count > 0)
                throw new FachFehler($"Des modifications concernent déjà {wirkungen.Count} bulletin(s) : publiez la rectification ou rétablissez d'abord les notes.");
            v.Status = VeroeffentlichungsStatus.Publiee;
            audit.Erfasse(db, a.Id, a.SchuleId, NotenAudit.RectificationAbgebrochen, nameof(Klasse), klasse.Id, $"Rectification abandonnée : {klasse.Name}, {periode.Titel}.");
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    private sealed record Wirkung(BulletinBerechnung Berechnung, KohortenSchueler Schueler, BulletinInhalt Inhalt, string Hash, Guid? AltId,
        RectificationAenderung Aenderung);

    /// <summary>Betroffene Bulletins: die Periode selbst und spätere veröffentlichte Perioden der Klasse (Vorperioden, Bilan).</summary>
    private static async Task<List<Wirkung>> WirkungenAsync(SkoulouDbContext db, Klasse klasse, Periode periode, CancellationToken ct)
    {
        var spaetere = await (from v in db.KlassenVeroeffentlichungen
                              join p in db.Perioden on v.PeriodeId equals p.Id
                              where v.KlasseId == klasse.Id && p.SchuljahrId == klasse.SchuljahrId && p.Ende > periode.Ende && p.ZaehltFuerJahr && periode.ZaehltFuerJahr
                              orderby p.Ende
                              select p).AsNoTracking().ToListAsync(ct);
        var liste = new List<Wirkung>();
        foreach (var p in new[] { periode }.Concat(spaetere))
        {
            var b = await BulletinBerechnung.LadeAsync(db, klasse, p, ct);
            var ids = b.Kohorte.Select(s => s.EinschreibungId).ToList();
            var gueltige = await db.Bulletins.AsNoTracking().Where(x => x.PeriodeId == p.Id && ids.Contains(x.EinschreibungId) && x.Status == BulletinStatus.Publie)
                .ToDictionaryAsync(x => x.EinschreibungId, ct);
            foreach (var (s, inhalt, hash) in b.AlleInhalte().Liste)
            {
                gueltige.TryGetValue(s.EinschreibungId, out var alt);
                if (alt is not null && alt.Hash == hash) continue;
                var altInhalt = alt is null ? null : BulletinSnapshot.AusJson(alt.SnapshotJson).Inhalt;
                liste.Add(new Wirkung(b, s, inhalt, hash, alt?.Id, new RectificationAenderung(s.EinschreibungId, s.Name, p.Titel, alt?.Nummer,
                    altInhalt?.MoyenneText ?? "—", inhalt.MoyenneText, altInhalt?.Rang ?? "—", inhalt.Rang, alt is null)));
            }
        }
        return liste;
    }

    private static async Task<KlassenVeroeffentlichung> RectificationOffenAsync(SkoulouDbContext db, Guid klasseId, Guid periodeId, CancellationToken ct)
    {
        var v = await db.KlassenVeroeffentlichungen.FirstOrDefaultAsync(x => x.KlasseId == klasseId && x.PeriodeId == periodeId, ct);
        if (v?.Status != VeroeffentlichungsStatus.EnRectification) throw new FachFehler("Aucune rectification n'est ouverte pour cette classe et cette période.");
        return v;
    }

    // ---------- Rückzug, Übergaben ----------

    public async Task ZurueckziehenAsync(Guid akteurId, Guid bulletinId, string? grund, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.BulletinsVeroeffentlichen, ct: ct);
        var text = NotenFormat.Text(grund, 300, "Le motif") ?? throw new FachFehler("Indiquez le motif du retrait.", "grund");
        await dbFactory.TransaktionAsync(async db =>
        {
            var b = await db.Bulletins.FirstOrDefaultAsync(x => x.Id == bulletinId, ct) ?? throw new FachFehler("Ce bulletin est introuvable.");
            a.PruefeSchule(b.SchuleId);
            if (b.Status != BulletinStatus.Publie) throw new FachFehler("Seul un bulletin valide peut être retiré.");
            b.Status = BulletinStatus.Retire;
            b.Grund = text;
            audit.Erfasse(db, a.Id, a.SchuleId, NotenAudit.BulletinZurueckgezogen, nameof(Bulletin), b.Id, $"Bulletin n° {b.Nummer} retiré. Motif : {text}");
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task UebergabeErfassenAsync(Guid akteurId, Guid bulletinId, UebergabeArt art, string? vermerk, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.BulletinsVeroeffentlichen, ct: ct);
        var text = NotenFormat.Text(vermerk, 300, "La remarque") ?? throw new FachFehler("Indiquez à qui et comment (ex. « remis au père en main propre »).", "vermerk");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var b = await db.Bulletins.AsNoTracking().FirstOrDefaultAsync(x => x.Id == bulletinId, ct) ?? throw new FachFehler("Ce bulletin est introuvable.");
        a.PruefeSchule(b.SchuleId);
        db.BulletinUebergaben.Add(new BulletinUebergabe { SchuleId = a.SchuleId, BulletinId = b.Id, Art = art, Vermerk = text, VonId = a.Id });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<UebergabeZeile>> UebergabenAsync(Guid akteurId, Guid bulletinId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.BulletinsVeroeffentlichen, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var schule = await db.Bulletins.Where(x => x.Id == bulletinId).Select(x => (Guid?)x.SchuleId).FirstOrDefaultAsync(ct) ?? throw new FachFehler("Ce bulletin est introuvable.");
        a.PruefeSchule(schule);
        return await (from u in db.BulletinUebergaben.AsNoTracking()
                      join k in db.Users on u.VonId equals k.Id
                      where u.BulletinId == bulletinId
                      orderby u.ErstelltAmUtc descending
                      select new UebergabeZeile(u.ErstelltAmUtc, u.Art, u.Vermerk, k.Vorname + " " + k.Nachname)).ToListAsync(ct);
    }

    private static async Task<(Klasse Klasse, Periode Periode, Schuljahr Jahr)> KlassePeriodeAsync(SkoulouDbContext db, SchulAkteur a, Guid klasseId, Guid periodeId, CancellationToken ct)
    {
        var klasse = await OrgAbfragen.KlasseAsync(db, a, klasseId, ct);
        var periode = await NotenRechte.PeriodeAsync(db, klasse, periodeId, ct);
        var jahr = await db.Schuljahre.AsNoTracking().FirstAsync(j => j.Id == klasse.SchuljahrId, ct);
        if (jahr.Status == SchuljahrStatus.Abgeschlossen)
            throw new FachFehler($"L'année scolaire {jahr.Bezeichnung} est clôturée : le directeur peut la rouvrir (avec un motif) pour publier ou rectifier.");
        return (klasse, periode, jahr);
    }
}
