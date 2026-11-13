using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Paedagogik;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Paedagogik;

/// <summary>
/// Zuweisungen von Leçons/Examens an Klassen eines Schuljahres. Erlaubt sind Klassen, in denen die
/// Autor-Lehrkraft das Fach des Inhalts unterrichtet (Lehrauftrag). Erneutes Zuweisen an dieselbe Klasse
/// startet eine neue Runde und archiviert die alte. Examen: ein Versuch, Lösungen nach der Frist.
/// </summary>
public sealed class ZuweisungService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, TimeProvider zeit)
{
    private sealed record InhaltInfo(Guid SchuleId, Guid AutorId, Guid FachId, Guid StufeId, InhaltsStatus Status, string Titel);

    private static async Task<InhaltInfo> InhaltAsync(SkoulouDbContext db, InhaltsArt art, Guid id, CancellationToken ct)
        => (art == InhaltsArt.Lecon
               ? await db.Lecons.Where(l => l.Id == id).Select(l => new InhaltInfo(l.SchuleId, l.AutorId, l.FachId, l.StufeId, l.Status, l.Titel)).FirstOrDefaultAsync(ct)
               : await db.Pruefungen.Where(p => p.Id == id).Select(p => new InhaltInfo(p.SchuleId, p.AutorId, p.FachId, p.StufeId, p.Status, p.Titel)).FirstOrDefaultAsync(ct))
           ?? throw new FachFehler(InhaltsRechte.MeldungNichtGefunden);

    /// <summary>Klassen des Schuljahres, in denen die Autor-Lehrkraft das Fach des Inhalts unterrichtet.</summary>
    public async Task<IReadOnlyList<KlassenOption>> KlassenAsync(Guid akteurId, InhaltsArt art, Guid inhaltId, Guid? schuljahrId, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchreiberAsync(zugriff, akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var inhalt = await InhaltAsync(db, art, inhaltId, ct);
        InhaltsRechte.PruefeInhalt(akteur, inhalt.SchuleId, inhalt.AutorId);
        var jahr = schuljahrId ?? (await SchulAbfragen.LaufendesSchuljahrAsync(db, inhalt.SchuleId, ct))?.Id;
        var klassenfaecher = SchulAbfragen.KlassenfaecherDesLehrers(db, inhalt.AutorId);
        var klassen = await (from kf in db.Klassenfaecher
                             where klassenfaecher.Contains(kf.Id) && kf.FachId == inhalt.FachId
                             join k in db.Klassen on kf.KlasseId equals k.Id
                             where k.SchuljahrId == jahr && k.SchuleId == inhalt.SchuleId && k.Aktiv
                             select new { k.Id, k.Name, k.StufeId })
            .Distinct().ToListAsync(ct);
        var stufenIds = klassen.Where(k => k.StufeId != null).Select(k => k.StufeId!.Value).Distinct().ToList();
        var stufen = await db.Stufen.Where(s => stufenIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return klassen.OrderBy(k => k.Name)
            .Select(k => new KlassenOption(k.Id, k.Name, k.StufeId is { } s ? stufen.GetValueOrDefault(s) : null, k.StufeId == inhalt.StufeId))
            .ToList();
    }

    /// <summary>Weist zu; eine aktive Zuweisung desselben Inhalts an dieselbe Klasse wird archiviert (neue Runde).</summary>
    public async Task<Guid> ZuweisenAsync(Guid akteurId, ZuweisungFormular f, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchreiberAsync(zugriff, akteurId, ct);
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        if (f.KlasseId is not { } klasseId) throw new FachFehler("Choisissez une classe.", "Klasse");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await DbTransaktion.AusfuehrenAsync(db, async () =>
        {
            var inhalt = await InhaltAsync(db, f.Art, f.InhaltId, ct);
            InhaltsRechte.PruefeInhalt(akteur, inhalt.SchuleId, inhalt.AutorId);
            if (inhalt.Status != InhaltsStatus.Prete)
                throw new FachFehler("Marquez d'abord le contenu comme prêt avant de l'attribuer.");
            var klasse = await db.Klassen.FirstOrDefaultAsync(k => k.Id == klasseId && k.SchuleId == inhalt.SchuleId, ct)
                         ?? throw new FachFehler("Cette classe est introuvable.", "Klasse");
            var unterrichtet = await db.Lehrauftraege.AnyAsync(l => l.LehrerId == inhalt.AutorId
                && db.Klassenfaecher.Any(kf => kf.Id == l.KlassenfachId && kf.KlasseId == klasse.Id && kf.FachId == inhalt.FachId), ct);
            if (!unterrichtet) throw new FachFehler("L'enseignant n'enseigne pas cette matière dans cette classe.", "Klasse");

            var z = new Zuweisung
            {
                SchuleId = inhalt.SchuleId, KlasseId = klasse.Id, LehrerId = inhalt.AutorId,
                LeconId = f.Art == InhaltsArt.Lecon ? f.InhaltId : null, PruefungId = f.Art == InhaltsArt.Pruefung ? f.InhaltId : null,
                AbUtc = f.AbUtc == default ? jetzt : f.AbUtc, FristUtc = f.FristUtc, Status = ZuweisungsStatus.Active
            };
            if (f.Art == InhaltsArt.Pruefung)
            {
                if (z.FristUtc is null) throw new FachFehler("Un examen a besoin d'une date limite.", "Frist");
                z.MaxVersuche = 1;
                z.Wertung = VersuchsWertung.Premier;
                z.LoesungenSichtbar = LoesungsSichtbarkeit.ApresFrist;
            }
            else
            {
                if (f.MaxVersuche is < 1 or > VersuchsRegel.MaxVersucheObergrenze)
                    throw new FachFehler($"Le nombre d'essais doit être compris entre 1 et {VersuchsRegel.MaxVersucheObergrenze}.", "MaxVersuche");
                z.MaxVersuche = f.MaxVersuche;
                z.Wertung = f.Wertung;
                z.LoesungenSichtbar = f.Loesungen;
            }
            if (z.FristUtc is { } frist)
            {
                if (frist <= jetzt) throw new FachFehler("La date limite doit être dans le futur.", "Frist");
                if (frist <= z.AbUtc) throw new FachFehler("La date limite doit être après le début.", "Frist");
            }

            foreach (var alt in await db.Zuweisungen.Where(x => x.KlasseId == klasse.Id && x.Status == ZuweisungsStatus.Active
                         && (f.Art == InhaltsArt.Lecon ? x.LeconId == f.InhaltId : x.PruefungId == f.InhaltId)).ToListAsync(ct))
            {
                if (f.Art == InhaltsArt.Pruefung && await db.Versuche.AnyAsync(v => v.ZuweisungId == alt.Id && v.Status == VersuchsStatus.EnCours, ct))
                    throw new FachFehler("Des élèves composent encore cet examen : attendez la fin avant de le réattribuer.");
                alt.Status = ZuweisungsStatus.Archivee;
            }
            db.Zuweisungen.Add(z);
            await db.SaveChangesAsync(ct);
            return z.Id;
        }, ct);
    }

    /// <summary>Frist ändern (laufende Versuche bekommen die neue Grenze; Examen höchstens Start + Dauer).</summary>
    public async Task FristAendernAsync(Guid akteurId, Guid zuweisungId, DateTime? neueFristUtc, CancellationToken ct = default)
    {
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await DbTransaktion.AusfuehrenAsync(db, async () =>
        {
            var (z, akteur) = await LadeMitRechtAsync(db, akteurId, zuweisungId, ct);
            _ = akteur;
            if (z.PruefungId is not null && neueFristUtc is null) throw new FachFehler("Un examen a besoin d'une date limite.", "Frist");
            if (neueFristUtc is { } f && (f <= jetzt || f <= z.AbUtc)) throw new FachFehler("La date limite doit être dans le futur et après le début.", "Frist");
            z.FristUtc = neueFristUtc;
            var dauer = z.PruefungId is { } pid ? await db.Pruefungen.Where(p => p.Id == pid).Select(p => p.DauerMinuten).FirstAsync(ct) : 0;
            foreach (var v in await db.Versuche.Where(v => v.ZuweisungId == z.Id && v.Status == VersuchsStatus.EnCours).ToListAsync(ct))
                v.DeadlineUtc = z.PruefungId is not null ? PruefungsZeit.Deadline(v.StartUtc, dauer, neueFristUtc) : neueFristUtc;
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task ArchivierenAsync(Guid akteurId, Guid zuweisungId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (z, _) = await LadeMitRechtAsync(db, akteurId, zuweisungId, ct);
        if (z.PruefungId is not null && await db.Versuche.AnyAsync(v => v.ZuweisungId == z.Id && v.Status == VersuchsStatus.EnCours, ct))
            throw new FachFehler("Des élèves composent encore cet examen : attendez la fin avant de l'archiver.");
        z.Status = ZuweisungsStatus.Archivee;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Alle Zuweisungen eines Inhalts mit Kennzahlen (COUNT/AVG in der DB).</summary>
    public async Task<IReadOnlyList<ZuweisungZeile>> ListeAsync(Guid akteurId, InhaltsArt art, Guid inhaltId, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.LeserAsync(zugriff, akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var inhalt = await InhaltAsync(db, art, inhaltId, ct);
        InhaltsRechte.PruefeInhalt(akteur, inhalt.SchuleId, inhalt.AutorId);
        var aktive = SchulAbfragen.AktiveEinschreibungen(db);
        return await db.Zuweisungen.AsNoTracking()
            .Where(z => art == InhaltsArt.Lecon ? z.LeconId == inhaltId : z.PruefungId == inhaltId)
            .OrderBy(z => z.Status).ThenByDescending(z => z.AbUtc)
            .Select(z => new ZuweisungZeile(z.Id, z.KlasseId,
                db.Klassen.Where(k => k.Id == z.KlasseId).Select(k => k.Name).FirstOrDefault() ?? "",
                db.Klassen.Where(k => k.Id == z.KlasseId).Select(k => db.Schuljahre.Where(j => j.Id == k.SchuljahrId).Select(j => j.Bezeichnung).FirstOrDefault()).FirstOrDefault() ?? "",
                z.AbUtc, z.FristUtc, z.MaxVersuche, z.Wertung, z.LoesungenSichtbar, z.Status,
                aktive.Count(e => e.KlasseId == z.KlasseId),
                db.LernStaende.Count(s => s.ZuweisungId == z.Id && s.Abgeschlossen),
                db.LernStaende.Where(s => s.ZuweisungId == z.Id && s.Abgeschlossen && s.MaxPunkte > 0).Average(s => (decimal?)s.Prozent),
                db.Antworten.Count(a => a.Punkte == null && db.Versuche.Any(v => v.Id == a.VersuchId && v.ZuweisungId == z.Id
                    && (v.Status == VersuchsStatus.ACorriger || (z.LeconId != null && v.Status == VersuchsStatus.EnCours))))))
            .ToListAsync(ct);
    }

    /// <summary>Zuweisung laden und Recht prüfen: Lehrkraft = eigene Zuweisung, Direktion = Schule.</summary>
    private async Task<(Zuweisung, Akteur)> LadeMitRechtAsync(SkoulouDbContext db, Guid akteurId, Guid zuweisungId, CancellationToken ct)
    {
        var akteur = await InhaltsRechte.SchreiberAsync(zugriff, akteurId, ct);
        var z = await db.Zuweisungen.FirstOrDefaultAsync(x => x.Id == zuweisungId, ct) ?? throw new FachFehler("Cette attribution est introuvable.");
        InhaltsRechte.PruefeInhalt(akteur, z.SchuleId, z.LehrerId);
        return (z, akteur);
    }
}
