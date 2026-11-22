using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Paedagogik;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Medien;

namespace Skoulou.WebApp.Services.Paedagogik;

/// <summary>
/// Bilder und Videos für Inhalte: Hochladen in Teilen (robust bei langsamen Netzen, je Teil höchstens 4 MB),
/// Prüfung serverseitig (Magic Bytes, Größe, Videodauer), Mediathek der Schule und Medien-Nutzungen
/// (Löschschutz und Freigabe für Schüler).
/// </summary>
public sealed class MedienService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, IMedienSpeicher speicher,
    TimeProvider zeit, ILogger<MedienService> logger)
{
    public const string NutzungLecon = "Lecon";
    public const string NutzungPruefung = "Pruefung";
    public const int TeilGroesse = 4 * 1024 * 1024;

    private sealed class Sitzung
    {
        public required Guid Id { get; init; }
        public required Guid KontoId { get; init; }
        public required Guid SchuleId { get; init; }
        public required MedienArt Art { get; init; }
        public required string Dateiname { get; init; }
        public required long Erwartet { get; init; }
        public required string Pfad { get; init; }
        public required DateTime StartUtc { get; init; }
        public long Empfangen { get; set; }
        public SemaphoreSlim Sperre { get; } = new(1, 1);
    }

    private readonly ConcurrentDictionary<Guid, Sitzung> sitzungen = new();
    private readonly string ordner = Path.Combine(Path.GetTempPath(), "skoulou-uploads");

    /// <summary>Beginnt einen Upload; liefert die Upload-Id.</summary>
    public async Task<Guid> UploadStartenAsync(Guid akteurId, MedienArt art, string? dateiname, long groesse, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchreiberAsync(zugriff, akteurId, ct);
        if (art is not (MedienArt.Bild or MedienArt.Video)) throw new FachFehler("Seules les images et les vidéos sont acceptées.");
        if (MedienPruefung.PruefeGroesse(art, groesse) is { } fehler) throw new FachFehler(fehler);
        Aufraeumen();
        Directory.CreateDirectory(ordner);
        var id = Guid.NewGuid();
        var sitzung = new Sitzung
        {
            Id = id, KontoId = akteur.Id, SchuleId = akteur.SchuleId!.Value, Art = art, Dateiname = Dateiname(dateiname, art),
            Erwartet = groesse, Pfad = Path.Combine(ordner, id.ToString("N")), StartUtc = zeit.GetUtcNow().UtcDateTime
        };
        await File.WriteAllBytesAsync(sitzung.Pfad, [], ct);
        sitzungen[id] = sitzung;
        return id;
    }

    /// <summary>Schreibt einen Teil ab <paramref name="offset"/> (Wiederholung eines Teils überschreibt); liefert die empfangene Länge.</summary>
    public async Task<long> TeilSchreibenAsync(Guid akteurId, Guid uploadId, long offset, Stream daten, CancellationToken ct = default)
    {
        var s = HoleSitzung(akteurId, uploadId);
        await s.Sperre.WaitAsync(ct);
        try
        {
            if (offset < 0 || offset > s.Empfangen) throw new FachFehler("Envoi interrompu : recommencez l'envoi du fichier.");
            await using var datei = new FileStream(s.Pfad, FileMode.Open, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            datei.SetLength(offset);
            datei.Position = offset;
            var puffer = new byte[81920];
            long geschrieben = 0;
            int n;
            while ((n = await daten.ReadAsync(puffer, ct)) > 0)
            {
                geschrieben += n;
                if (geschrieben > TeilGroesse || offset + geschrieben > Math.Min(s.Erwartet, MedienPruefung.MaxBytes(s.Art)))
                    throw new FachFehler("Le fichier dépasse la taille annoncée ou autorisée.");
                await datei.WriteAsync(puffer.AsMemory(0, n), ct);
            }
            s.Empfangen = offset + geschrieben;
            return s.Empfangen;
        }
        finally
        {
            s.Sperre.Release();
        }
    }

    /// <summary>Prüft die Datei, legt das Medium an und gibt den Speicherplatz frei.</summary>
    public async Task<MediumZeile> UploadAbschliessenAsync(Guid akteurId, Guid uploadId, CancellationToken ct = default)
    {
        var s = HoleSitzung(akteurId, uploadId);
        sitzungen.TryRemove(uploadId, out _);
        try
        {
            await using var datei = new FileStream(s.Pfad, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            if (MedienPruefung.PruefeGroesse(s.Art, datei.Length) is { } fehler) throw new FachFehler(fehler);
            var kopf = new byte[MedienPruefung.KopfLaenge];
            var gelesen = await datei.ReadAtLeastAsync(kopf, kopf.Length, throwOnEndOfStream: false, ct);
            var format = MedienPruefung.Erkenne(kopf.AsSpan(0, gelesen));
            if (format is null || format.Art != s.Art)
                throw new FachFehler(s.Art == MedienArt.Video
                    ? "Format de vidéo non accepté (MP4 ou MOV)."
                    : "Format d'image non accepté (PNG, JPEG, GIF ou WebP).");

            int? dauer = null;
            if (s.Art == MedienArt.Video)
            {
                var sekunden = MedienPruefung.Mp4Dauer(datei);
                if (sekunden is null) throw new FachFehler("La durée de la vidéo est illisible. Exportez-la en MP4.");
                if (sekunden > MedienPruefung.MaxVideoSekunden) throw new FachFehler("La vidéo dépasse 20 minutes.");
                dauer = (int)Math.Ceiling(sekunden.Value);
            }

            var medium = new Medium
            {
                SchuleId = s.SchuleId, BesitzerId = s.KontoId, Art = s.Art, Zweck = s.Art == MedienArt.Video ? MedienZweck.Video : MedienZweck.Inhalt,
                Dateiname = s.Dateiname, ContentType = format.ContentType, DauerSek = dauer
            };
            medium.BlobName = MedienPfad.Fuer(s.SchuleId, medium.Zweck, medium.Id);
            datei.Position = 0;
            medium.Bytes = await speicher.SpeichernAsync(medium.BlobName, datei, format.ContentType, ct);

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.Medien.Add(medium);
            await db.SaveChangesAsync(ct);
            var besitzer = await db.Users.Where(k => k.Id == s.KontoId).Select(k => k.Vorname + " " + k.Nachname).FirstOrDefaultAsync(ct) ?? "";
            return new MediumZeile(medium.Id, medium.Art, medium.Dateiname, medium.Bytes, medium.DauerSek, besitzer, zeit.GetUtcNow().UtcDateTime);
        }
        finally
        {
            Loesche(s.Pfad);
        }
    }

    /// <summary>Kleiner Upload in einem Stück (Tests, Seed).</summary>
    public async Task<MediumZeile> HochladenAsync(Guid akteurId, MedienArt art, string dateiname, Stream inhalt, CancellationToken ct = default)
    {
        var id = await UploadStartenAsync(akteurId, art, dateiname, inhalt.CanSeek ? inhalt.Length : MedienPruefung.MaxBytes(art), ct);
        try
        {
            long offset = 0;
            var puffer = new byte[TeilGroesse];
            int n;
            while ((n = await inhalt.ReadAtLeastAsync(puffer, puffer.Length, throwOnEndOfStream: false, ct)) > 0)
                offset = await TeilSchreibenAsync(akteurId, id, offset, new MemoryStream(puffer, 0, n), ct);
            return await UploadAbschliessenAsync(akteurId, id, ct);
        }
        catch
        {
            if (sitzungen.TryRemove(id, out var s)) Loesche(s.Pfad);
            throw;
        }
    }

    /// <summary>Mediathek der Schule (alle Lehrkräfte sehen alle Inhaltsmedien der Schule).</summary>
    public async Task<(IReadOnlyList<MediumZeile> Eintraege, bool Mehr)> BibliothekAsync(Guid akteurId, MedienArt art, string? suche,
        int ueberspringen = 0, int anzahl = 30, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.LeserAsync(zugriff, akteurId, ct);
        var schuleId = akteur.SchuleId!.Value;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Medien.AsNoTracking().Where(m => m.SchuleId == schuleId && m.Art == art
                                                    && (m.Zweck == MedienZweck.Inhalt || m.Zweck == MedienZweck.Video));
        if (!string.IsNullOrWhiteSpace(suche)) q = q.Where(m => m.Dateiname.Contains(suche.Trim()));
        var seite = await q.OrderByDescending(m => m.BesitzerId == akteur.Id).ThenByDescending(m => m.ErstelltAmUtc)
            .Skip(Math.Max(0, ueberspringen)).Take(anzahl + 1)
            .Select(m => new MediumZeile(m.Id, m.Art, m.Dateiname, m.Bytes, m.DauerSek,
                db.Users.Where(k => k.Id == m.BesitzerId).Select(k => k.Vorname + " " + k.Nachname).FirstOrDefault() ?? "", m.ErstelltAmUtc))
            .ToListAsync(ct);
        return (seite.Take(anzahl).ToList(), seite.Count > anzahl);
    }

    /// <summary>Gleicht die Nutzungen eines Inhalts ab; fremde oder unpassende Medien werden abgelehnt.</summary>
    internal static async Task NutzungAbgleichenAsync(SkoulouDbContext db, Guid schuleId, string typ, Guid zielId,
        IReadOnlyList<Guid> bilder, Guid? video, CancellationToken ct)
    {
        var gewuenscht = bilder.ToHashSet();
        if (video is { } v) gewuenscht.Add(v);
        var gueltig = await db.Medien.Where(m => gewuenscht.Contains(m.Id) && m.SchuleId == schuleId)
            .Select(m => new { m.Id, m.Art, m.Zweck }).ToListAsync(ct);
        // Nur Bilder der Médiathèque: kein Kassenbeleg, Schülerfoto oder Logo, das Schüler sonst über die Freigabe sähen.
        if (bilder.Any(b => !gueltig.Any(g => g.Id == b && g.Art == MedienArt.Bild && g.Zweck == MedienZweck.Inhalt)))
            throw new FachFehler("Une image du contenu n'est pas disponible dans votre établissement. Choisissez-la dans la médiathèque.");

        var vorhanden = await db.MedienNutzungen.Where(n => n.Typ == typ && n.ZielId == zielId).ToListAsync(ct);
        db.MedienNutzungen.RemoveRange(vorhanden.Where(n => !gewuenscht.Contains(n.MediumId)));
        foreach (var id in gewuenscht.Where(id => vorhanden.All(n => n.MediumId != id)))
            db.MedienNutzungen.Add(new MedienNutzung { MediumId = id, Typ = typ, ZielId = zielId });
        await db.SaveChangesAsync(ct);
    }

    private Sitzung HoleSitzung(Guid akteurId, Guid uploadId)
        => sitzungen.TryGetValue(uploadId, out var s) && s.KontoId == akteurId
            ? s
            : throw new FachFehler("Cet envoi a expiré. Recommencez l'envoi du fichier.");

    private void Aufraeumen()
    {
        var grenze = zeit.GetUtcNow().UtcDateTime.AddHours(-6);
        foreach (var alt in sitzungen.Values.Where(s => s.StartUtc < grenze).ToList())
            if (sitzungen.TryRemove(alt.Id, out _)) Loesche(alt.Pfad);
    }

    private void Loesche(string pfad)
    {
        try { if (File.Exists(pfad)) File.Delete(pfad); }
        catch (IOException ex) { logger.LogWarning(ex, "Temporäre Upload-Datei nicht gelöscht."); }
    }

    private static string Dateiname(string? name, MedienArt art)
    {
        var basis = Path.GetFileName(name ?? string.Empty);
        basis = new string(basis.Where(c => !char.IsControl(c) && c is not ('<' or '>' or '"' or '\\' or '/')).ToArray()).Trim();
        if (basis.Length == 0) basis = art == MedienArt.Video ? "video.mp4" : "image";
        return basis.Length <= 200 ? basis : basis[..200];
    }
}

/// <summary>Schüler sehen Medien der Inhalte, die ihrer aktuellen Klasse zugewiesen sind (oder die sie bearbeitet haben).</summary>
public sealed class PaedagogikMedienFreigabe(TimeProvider zeit, IMemoryCache cache) : IMedienFreigabe
{
    public async Task<bool> DarfSehenAsync(SkoulouDbContext db, Guid kontoId, string rolle, Guid? schuleId, Medium medium, CancellationToken ct)
    {
        if (rolle != Rollen.Schueler || schuleId is null || medium.SchuleId != schuleId) return false;
        var schluessel = $"medien-freigabe:{kontoId:N}:{medium.Id:N}";
        if (cache.TryGetValue(schluessel, out bool gemerkt)) return gemerkt;

        var ziele = db.MedienNutzungen.Where(n => n.MediumId == medium.Id && (n.Typ == MedienService.NutzungLecon || n.Typ == MedienService.NutzungPruefung))
            .Select(n => n.ZielId);
        var klasseId = (await SchulAbfragen.AktuelleEinschreibungAsync(db, kontoId, ct))?.KlasseId;
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        var erlaubt = await db.Zuweisungen.AnyAsync(z => z.SchuleId == schuleId
                && ((z.LeconId != null && ziele.Contains(z.LeconId.Value)) || (z.PruefungId != null && ziele.Contains(z.PruefungId.Value)))
                && ((z.KlasseId == klasseId && z.Status == ZuweisungsStatus.Active && z.AbUtc <= jetzt)
                    || db.Versuche.Any(v => v.ZuweisungId == z.Id && v.SchuelerId == kontoId)), ct);
        cache.Set(schluessel, erlaubt, TimeSpan.FromMinutes(5));
        return erlaubt;
    }
}
