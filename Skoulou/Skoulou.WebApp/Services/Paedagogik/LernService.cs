using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Paedagogik;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Paedagogik;

/// <summary>
/// Schülerseite der Leçons: Liste je Fach, Leser (Seiten, Video, Übungen), Antworten mit sofortiger
/// Rückmeldung (jede Frage einmal je Versuch), neue Versuche gemäß Versuchsregel, Stimme an/aus.
/// </summary>
public sealed class LernService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, TimeProvider zeit)
{
    private DateTime Jetzt => zeit.GetUtcNow().UtcDateTime;

    /// <summary>Fächer mit zugewiesenen Leçons der aktuellen Klasse (für die Filter-Chips).</summary>
    public async Task<IReadOnlyList<FachAnzahl>> FaecherAsync(Guid schuelerId, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchuelerAsync(zugriff, schuelerId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var klasse = (await SchulAbfragen.AktuelleEinschreibungAsync(db, akteur.Id, ct))?.KlasseId;
        if (klasse is null) return [];
        var jetzt = Jetzt;
        var roh = await (from z in db.Zuweisungen
                         where z.KlasseId == klasse && z.Status == ZuweisungsStatus.Active && z.AbUtc <= jetzt && z.LeconId != null
                         join l in db.Lecons on z.LeconId equals l.Id
                         join f in db.Faecher on l.FachId equals f.Id
                         group f by new { f.Id, f.Name, f.Reihenfolge } into g
                         select new { g.Key.Id, g.Key.Name, g.Key.Reihenfolge, Anzahl = g.Count() }).ToListAsync(ct);
        return roh.OrderBy(x => x.Reihenfolge).ThenBy(x => x.Name).Select(x => new FachAnzahl(x.Id, x.Name, x.Anzahl)).ToList();
    }

    /// <summary>Leçons der aktuellen Klasse, nach Fach und Frist sortiert, gesucht und geblättert.</summary>
    public async Task<(IReadOnlyList<LeconKarte> Eintraege, bool Mehr)> LeconsAsync(Guid schuelerId, string? suche, Guid? fachId,
        int ueberspringen = 0, int anzahl = 50, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchuelerAsync(zugriff, schuelerId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var klasse = (await SchulAbfragen.AktuelleEinschreibungAsync(db, akteur.Id, ct))?.KlasseId;
        if (klasse is null) return ([], false);
        var jetzt = Jetzt;
        var q = from z in db.Zuweisungen
                where z.KlasseId == klasse && z.Status == ZuweisungsStatus.Active && z.AbUtc <= jetzt && z.LeconId != null
                join l in db.Lecons on z.LeconId equals l.Id
                join f in db.Faecher on l.FachId equals f.Id
                select new { z, l, f };
        if (fachId is { } fid) q = q.Where(x => x.f.Id == fid);
        if (!string.IsNullOrWhiteSpace(suche)) q = q.Where(x => x.l.Titel.Contains(suche.Trim()) || x.f.Name.Contains(suche.Trim()));
        var seite = await q.OrderBy(x => x.f.Reihenfolge).ThenBy(x => x.f.Name).ThenBy(x => x.z.FristUtc == null).ThenBy(x => x.z.FristUtc)
            .ThenByDescending(x => x.z.AbUtc)
            .Skip(Math.Max(0, ueberspringen)).Take(anzahl + 1)
            .Select(x => new LeconKarte(x.z.Id, x.l.Id, x.l.Titel, x.l.Zusammenfassung, x.f.Name,
                db.Users.Where(k => k.Id == x.z.LehrerId).Select(k => k.Vorname + " " + k.Nachname).FirstOrDefault() ?? "",
                x.z.AbUtc, x.z.FristUtc,
                db.Fragen.Count(fr => !fr.Archiviert && db.AufgabenBloecke.Any(b => b.Id == fr.BlockId && b.LeconId == x.l.Id && !b.Archiviert)),
                db.LernStaende.Any(s => s.ZuweisungId == x.z.Id && s.SchuelerId == akteur.Id && s.Abgeschlossen),
                db.LernStaende.Where(s => s.ZuweisungId == x.z.Id && s.SchuelerId == akteur.Id && s.Abgeschlossen && s.MaxPunkte > 0)
                    .Select(s => (decimal?)s.Prozent).FirstOrDefault(),
                db.LernStaende.Any(s => s.ZuweisungId == x.z.Id && s.SchuelerId == akteur.Id && s.KorrekturOffen),
                x.l.VideoMediumId != null))
            .ToListAsync(ct);
        return (seite.Take(anzahl).ToList(), seite.Count > anzahl);
    }

    /// <summary>Leser einer Leçon: Inhalt, Fragen des laufenden (oder letzten) Versuchs, Rückmeldungen.</summary>
    public async Task<SchuelerLecon> OeffnenAsync(Guid schuelerId, Guid zuweisungId, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchuelerAsync(zugriff, schuelerId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var jetzt = Jetzt;
        var (z, offen) = await VersuchsLogik.ZuweisungAsync(db, akteur, zuweisungId, jetzt, auchFruehere: true, ct);
        if (z.LeconId is not { } leconId) throw new FachFehler("Ce contenu n'est pas une leçon.");
        var lecon = await db.Lecons.AsNoTracking().Include(l => l.Seiten).FirstAsync(l => l.Id == leconId, ct);
        var fach = await db.Faecher.Where(f => f.Id == lecon.FachId).Select(f => f.Name).FirstOrDefaultAsync(ct) ?? "";
        var lehrer = await db.Users.Where(k => k.Id == z.LehrerId).Select(k => k.Vorname + " " + k.Nachname).FirstOrDefaultAsync(ct) ?? "";

        var versuche = await db.Versuche.AsNoTracking().Where(v => v.ZuweisungId == z.Id && v.SchuelerId == akteur.Id).OrderBy(v => v.Nr).ToListAsync(ct);
        var aktuell = versuche.FirstOrDefault(v => v.Status == VersuchsStatus.EnCours) ?? versuche.LastOrDefault();
        var (bloecke, fragen) = await FragenFuerAsync(db, z, aktuell, ct);
        var antworten = aktuell is null ? [] : await AntwortStaendeAsync(db, z, aktuell, fragen, jetzt, ct);

        var stand = await db.LernStaende.AsNoTracking().FirstOrDefaultAsync(s => s.ZuweisungId == z.Id && s.SchuelerId == akteur.Id, ct);
        var stimmeAus = await db.SchuelerProfile.Where(p => p.KontoId == akteur.Id).Select(p => p.StimmeAus).FirstOrDefaultAsync(ct);
        var sperre = !offen ? "Cette leçon n'est plus attribuée à votre classe : vous pouvez seulement la relire."
            : z.FristUtc is { } frist && jetzt >= frist ? "La date limite est dépassée : vous pouvez relire la leçon et la correction."
            : null;
        return new SchuelerLecon(z.Id, lecon.Id, lecon.Titel, fach, lehrer, lecon.Zusammenfassung, lecon.Seiten.OrderBy(s => s.Nr).Select(s => s.Html).ToList(),
            lecon.VideoMediumId, bloecke, z.FristUtc, sperre, VersuchsRegel.Bezeichnung(z.MaxVersuche, z.Wertung), z.LoesungenSichtbar,
            aktuell is null ? null : VersuchsLogik.Info(aktuell), antworten,
            sperre is null && VersuchsRegel.DarfNeuenVersuch(z.MaxVersuche, versuche.Count), VersuchsRegel.Verbleibend(z.MaxVersuche, versuche.Count),
            versuche.Count, stand is { Abgeschlossen: true, MaxPunkte: > 0 } ? stand.Prozent : null, stand?.Abgeschlossen ?? false, stimmeAus);
    }

    /// <summary>Beantwortet eine Frage im laufenden Versuch (legt bei Bedarf einen neuen an) und bewertet sofort.</summary>
    public async Task<FrageRueckmeldung> AntwortenAsync(Guid schuelerId, Guid zuweisungId, Guid frageId, AntwortEingabe eingabe, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchuelerAsync(zugriff, schuelerId, ct);
        var jetzt = Jetzt;
        if (eingabe.IstLeer) throw new FachFehler("Donnez une réponse avant de vérifier.");
        if ((eingabe.Text?.Length ?? 0) > 4000 || eingabe.Luecken.Any(l => (l?.Length ?? 0) > 500))
            throw new FachFehler("La réponse est trop longue.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (z, _) = await VersuchsLogik.ZuweisungAsync(db, akteur, zuweisungId, jetzt, auchFruehere: false, ct);
        if (z.LeconId is null) throw new FachFehler("Ce contenu n'est pas une leçon.");
        if (z.FristUtc is { } frist && jetzt >= frist) throw new FachFehler("La date limite est dépassée : vous ne pouvez plus répondre.");

        Versuch versuch;
        Antwort antwort;
        Frage frage;
        try
        {
            (versuch, antwort, frage) = await DbTransaktion.AusfuehrenAsync(db, async () =>
            {
                var v = await db.Versuche.FirstOrDefaultAsync(x => x.ZuweisungId == z.Id && x.SchuelerId == akteur.Id && x.Status == VersuchsStatus.EnCours, ct);
                if (v is null)
                {
                    var bisher = await db.Versuche.CountAsync(x => x.ZuweisungId == z.Id && x.SchuelerId == akteur.Id, ct);
                    if (!VersuchsRegel.DarfNeuenVersuch(z.MaxVersuche, bisher)) throw new FachFehler("Vous avez utilisé tous vos essais pour cette leçon.");
                    v = await VersuchsLogik.NeuerVersuchAsync(db, z, akteur.Id, jetzt, ct);
                }
                if (!v.FragenIds.Contains(frageId)) throw new FachFehler("Cette question ne fait pas partie de votre essai en cours.");
                var bisherige = await db.Antworten.Where(a => a.VersuchId == v.Id).ToListAsync(ct);
                if (bisherige.Any(a => a.FrageId == frageId)) throw new FachFehler("Vous avez déjà répondu à cette question dans cet essai.");

                var f = await db.Fragen.AsNoTracking().Include(x => x.Optionen).FirstAsync(x => x.Id == frageId, ct);
                var ergebnis = BewertungsEngine.Bewerte(FragenAbbildung.Definition(f), eingabe);
                var a = new Antwort
                {
                    VersuchId = v.Id, FrageId = f.Id, AntwortJson = eingabe.AlsJson(), Punkte = ergebnis.Punkte,
                    Korrektur = ergebnis.BrauchtKorrektur ? KorrekturArt.Manuelle : KorrekturArt.Auto
                };
                db.Antworten.Add(a);
                bisherige.Add(a);
                var fertig = v.FragenIds.All(id => bisherige.Any(x => x.FrageId == id));
                if (fertig) v.AbgabeUtc = jetzt;
                VersuchsLogik.Aufsummieren(v, bisherige, abgegeben: fertig);
                await db.SaveChangesAsync(ct);
                return (v, a, f);
            }, ct);
        }
        catch (DbUpdateException ex) when (DbTransaktion.IstEindeutigkeitsFehler(ex))
        {
            throw new FachFehler("Vous avez déjà répondu à cette question dans cet essai.");
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new FachFehler("Votre essai vient d'être clôturé. Rechargez la page.");
        }

        await VersuchsLogik.LernStandAsync(db, z, akteur.Id, jetzt, ct);
        var abgeschlossen = versuch.Status != VersuchsStatus.EnCours;
        var sichtbar = LoesungsRegel.Sichtbar(z.LoesungenSichtbar, jetzt, z.FristUtc, frageBeantwortet: true, abgeschlossen);
        var hatNaechste = !abgeschlossen;
        return new FrageRueckmeldung(VersuchsLogik.Stand(frage, antwort, sichtbar), VersuchsLogik.Info(versuch), abgeschlossen, hatNaechste);
    }

    /// <summary>Neuen Versuch beginnen (alle Fragen wieder offen), wenn die Regel es erlaubt.</summary>
    public async Task<VersuchInfo> NeuerVersuchAsync(Guid schuelerId, Guid zuweisungId, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchuelerAsync(zugriff, schuelerId, ct);
        var jetzt = Jetzt;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (z, _) = await VersuchsLogik.ZuweisungAsync(db, akteur, zuweisungId, jetzt, auchFruehere: false, ct);
        if (z.LeconId is null) throw new FachFehler("Ce contenu n'est pas une leçon.");
        if (z.FristUtc is { } frist && jetzt >= frist) throw new FachFehler("La date limite est dépassée.");
        try
        {
            var v = await DbTransaktion.AusfuehrenAsync(db, async () =>
            {
                if (await db.Versuche.FirstOrDefaultAsync(x => x.ZuweisungId == z.Id && x.SchuelerId == akteur.Id && x.Status == VersuchsStatus.EnCours, ct) is { } laufend)
                    return laufend;
                var bisher = await db.Versuche.CountAsync(x => x.ZuweisungId == z.Id && x.SchuelerId == akteur.Id, ct);
                if (!VersuchsRegel.DarfNeuenVersuch(z.MaxVersuche, bisher)) throw new FachFehler("Vous avez utilisé tous vos essais pour cette leçon.");
                var neu = await VersuchsLogik.NeuerVersuchAsync(db, z, akteur.Id, jetzt, ct);
                if (neu.FragenIds.Count == 0) throw new FachFehler("Cette leçon n'a pas d'exercices.");
                await db.SaveChangesAsync(ct);
                return neu;
            }, ct);
            await VersuchsLogik.LernStandAsync(db, z, akteur.Id, jetzt, ct);
            return VersuchsLogik.Info(v);
        }
        catch (DbUpdateException ex) when (DbTransaktion.IstEindeutigkeitsFehler(ex))
        {
            throw new FachFehler("Un nouvel essai vient d'être commencé. Rechargez la page.");
        }
    }

    /// <summary>Laufenden Versuch vorzeitig abgeben (offene Fragen zählen 0).</summary>
    public async Task<VersuchInfo> VersuchBeendenAsync(Guid schuelerId, Guid zuweisungId, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchuelerAsync(zugriff, schuelerId, ct);
        var jetzt = Jetzt;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (z, _) = await VersuchsLogik.ZuweisungAsync(db, akteur, zuweisungId, jetzt, auchFruehere: true, ct);
        var v = await db.Versuche.FirstOrDefaultAsync(x => x.ZuweisungId == z.Id && x.SchuelerId == akteur.Id && x.Status == VersuchsStatus.EnCours, ct)
                ?? throw new FachFehler("Aucun essai en cours.");
        await VersuchsLogik.AbschliessenAsync(db, v, jetzt, ct);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
        await VersuchsLogik.LernStandAsync(db, z, akteur.Id, jetzt, ct);
        return VersuchsLogik.Info(await db.Versuche.AsNoTracking().FirstAsync(x => x.Id == v.Id, ct));
    }

    /// <summary>Leçon ohne Übungen als gelesen markieren.</summary>
    public async Task GelesenAsync(Guid schuelerId, Guid zuweisungId, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchuelerAsync(zugriff, schuelerId, ct);
        var jetzt = Jetzt;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (z, _) = await VersuchsLogik.ZuweisungAsync(db, akteur, zuweisungId, jetzt, auchFruehere: false, ct);
        if (z.LeconId is null) throw new FachFehler("Ce contenu n'est pas une leçon.");
        if ((await FragenAbbildung.AktuelleAsync(db, z.LeconId, null, ct)).Any(b => b.Fragen.Count > 0))
            throw new FachFehler("Terminez les exercices pour achever cette leçon.");
        var stand = await db.LernStaende.FirstOrDefaultAsync(s => s.ZuweisungId == z.Id && s.SchuelerId == akteur.Id, ct);
        if (stand is null) db.LernStaende.Add(stand = new LernStand { SchuleId = z.SchuleId, ZuweisungId = z.Id, SchuelerId = akteur.Id });
        stand.Abgeschlossen = true;
        stand.LetzteAktivitaetUtc = jetzt;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (DbTransaktion.IstEindeutigkeitsFehler(ex)) { }
    }

    /// <summary>Gesprochene Rückmeldung (MP3) für dieses Schülerkonto aus- oder einschalten.</summary>
    public async Task StimmeSetzenAsync(Guid schuelerId, bool aus, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchuelerAsync(zugriff, schuelerId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var profil = await db.SchuelerProfile.FirstOrDefaultAsync(p => p.KontoId == akteur.Id, ct)
                     ?? throw new FachFehler("Votre profil d'élève est introuvable.");
        profil.StimmeAus = aus;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Text einer Audio-Frage – nur auf Abruf zum Vorlesen (steht nie im Seiteninhalt).</summary>
    public async Task<(string Text, string Sprache)> AudioTextAsync(Guid schuelerId, Guid zuweisungId, Guid frageId, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchuelerAsync(zugriff, schuelerId, ct);
        var jetzt = Jetzt;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (z, _) = await VersuchsLogik.ZuweisungAsync(db, akteur, zuweisungId, jetzt, auchFruehere: true, ct);
        var versuche = await db.Versuche.AsNoTracking().Where(v => v.ZuweisungId == z.Id && v.SchuelerId == akteur.Id).ToListAsync(ct);
        var erlaubt = z.PruefungId is not null
            ? versuche.Any(v => v.FragenIds.Contains(frageId) && (v.Status == VersuchsStatus.EnCours || (z.FristUtc is { } f && jetzt >= f)))
            : versuche.Any(v => v.FragenIds.Contains(frageId))
              || await db.Fragen.AnyAsync(fr => fr.Id == frageId && !fr.Archiviert && db.AufgabenBloecke.Any(b => b.Id == fr.BlockId && b.LeconId == z.LeconId), ct);
        if (!erlaubt) throw new KeinZugriffFehler(VersuchsLogik.MeldungNichtZugewiesen);
        var frage = await db.Fragen.AsNoTracking().Where(f => f.Id == frageId).Select(f => new { f.AudioText, f.Sprache }).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(frage?.AudioText)) throw new FachFehler("Cette question n'a pas d'audio.");
        return (frage.AudioText, frage.Sprache);
    }

    /// <summary>Blöcke/Fragen des Versuchs (eingefroren) bzw. die aktuellen, wenn noch kein Versuch existiert.</summary>
    internal static async Task<(IReadOnlyList<SchuelerBlock>, Dictionary<Guid, Frage>)> FragenFuerAsync(SkoulouDbContext db, Zuweisung z,
        Versuch? versuch, CancellationToken ct)
    {
        if (versuch is null)
        {
            var aktuelle = await FragenAbbildung.AktuelleAsync(db, z.LeconId, z.PruefungId, ct);
            var nr = 0;
            var bloecke = aktuelle.Where(a => a.Fragen.Count > 0)
                .Select(a => new SchuelerBlock(a.Block.Id, a.Block.Nr, a.Block.Titel, a.Block.Consigne,
                    a.Fragen.Select(f => FragenAbbildung.Schueler(f, ++nr)).ToList())).ToList();
            return (bloecke, aktuelle.SelectMany(a => a.Fragen).ToDictionary(f => f.Id));
        }
        var fragen = await FragenAbbildung.LadeAsync(db, versuch.FragenIds, ct);
        var blockIds = fragen.Values.Select(f => f.BlockId).Distinct().ToList();
        var bloeckeDb = await db.AufgabenBloecke.AsNoTracking().Where(b => blockIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        var laufend = 0;
        var gruppen = versuch.FragenIds.Where(fragen.ContainsKey).Select(id => fragen[id])
            .GroupBy(f => f.BlockId).OrderBy(g => bloeckeDb.TryGetValue(g.Key, out var b) ? b.Nr : int.MaxValue)
            .Select(g =>
            {
                var b = bloeckeDb[g.Key];
                return new SchuelerBlock(b.Id, b.Nr, b.Titel, b.Consigne, g.OrderBy(f => f.Nr).Select(f => FragenAbbildung.Schueler(f, ++laufend)).ToList());
            }).ToList();
        return (gruppen, fragen);
    }

    /// <summary>Antworten eines Versuchs mit Rückmeldung; Lösungen nach der Lösungsregel der Zuweisung.</summary>
    internal static async Task<IReadOnlyList<AntwortStand>> AntwortStaendeAsync(SkoulouDbContext db, Zuweisung z, Versuch v,
        Dictionary<Guid, Frage> fragen, DateTime jetzt, CancellationToken ct)
    {
        var antworten = await db.Antworten.AsNoTracking().Where(a => a.VersuchId == v.Id).ToListAsync(ct);
        var abgeschlossen = v.Status != VersuchsStatus.EnCours;
        return antworten.Where(a => fragen.ContainsKey(a.FrageId)).Select(a =>
        {
            var f = fragen[a.FrageId];
            var sichtbar = LoesungsRegel.Sichtbar(z.LoesungenSichtbar, jetzt, z.FristUtc,
                frageBeantwortet: z.PruefungId is null, versuchAbgeschlossen: abgeschlossen);
            return VersuchsLogik.Stand(f, a, sichtbar);
        }).ToList();
    }
}
