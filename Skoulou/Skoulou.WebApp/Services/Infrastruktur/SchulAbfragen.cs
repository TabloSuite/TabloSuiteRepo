using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Data;

namespace Skoulou.WebApp.Services.Infrastruktur;

/// <summary>
/// Gemeinsame Abfragen aller Bereiche (eine Wahrheit statt Varianten je Bereich):
/// laufendes Schuljahr, aktuelle Klasse eines Schülers, Klassen eines Lehrers, Kinder der Eltern.
/// Arbeitet im DbContext des aufrufenden Dienstes; prüft keine Rechte (das tut <see cref="Zugriff"/>).
/// </summary>
public static class SchulAbfragen
{
    /// <summary>Laufendes Schuljahr der Schule; ohne laufendes Jahr das zuletzt begonnene (auch in Vorbereitung).</summary>
    public static async Task<Schuljahr?> LaufendesSchuljahrAsync(SkoulouDbContext db, Guid schuleId, CancellationToken ct = default)
        => await db.Schuljahre.AsNoTracking()
               .Where(j => j.SchuleId == schuleId && j.Status == SchuljahrStatus.Laufend)
               .FirstOrDefaultAsync(ct)
           ?? await db.Schuljahre.AsNoTracking()
               .Where(j => j.SchuleId == schuleId)
               .OrderByDescending(j => j.Beginn)
               .FirstOrDefaultAsync(ct);

    /// <summary>Gültige Einschreibungen (nicht storniert) – Basis für Listen, Effectif und Abrechnung.</summary>
    public static IQueryable<Einschreibung> GueltigeEinschreibungen(SkoulouDbContext db)
        => db.Einschreibungen.Where(e => e.StorniertAmUtc == null);

    /// <summary>Aktive Einschreibungen (Schüler sitzt jetzt in der Klasse).</summary>
    public static IQueryable<Einschreibung> AktiveEinschreibungen(SkoulouDbContext db)
        => GueltigeEinschreibungen(db).Where(e => e.Status == EinschreibungsStatus.Active);

    /// <summary>Einschreibungen, deren Zeitraum [von, bis] schneidet (Periode, Abrechnung, Effectif am Stichtag).</summary>
    public static IQueryable<Einschreibung> EinschreibungenImZeitraum(SkoulouDbContext db, Guid schuljahrId, DateOnly von, DateOnly bis)
        => GueltigeEinschreibungen(db)
            .Where(e => e.SchuljahrId == schuljahrId && e.Von <= bis && (e.Bis == null || e.Bis >= von));

    /// <summary>Aktuelle Einschreibung eines Schülers (aktiv, neueste zuerst); null = ohne Klasse.</summary>
    public static Task<Einschreibung?> AktuelleEinschreibungAsync(SkoulouDbContext db, Guid schuelerId, CancellationToken ct = default)
        => AktiveEinschreibungen(db).AsNoTracking()
            .Where(e => e.SchuelerId == schuelerId)
            .OrderByDescending(e => e.Von)
            .FirstOrDefaultAsync(ct);

    /// <summary>Klassen, in denen die Lehrkraft einen Lehrauftrag hat (optional nur eines Schuljahres).</summary>
    public static IQueryable<Guid> KlassenDesLehrers(SkoulouDbContext db, Guid lehrerId, Guid? schuljahrId = null)
    {
        var klassenIds = from l in db.Lehrauftraege
                         join kf in db.Klassenfaecher on l.KlassenfachId equals kf.Id
                         where l.LehrerId == lehrerId
                         select kf.KlasseId;
        if (schuljahrId is { } jahr)
            klassenIds = from id in klassenIds
                         join k in db.Klassen on id equals k.Id
                         where k.SchuljahrId == jahr
                         select id;
        return klassenIds.Distinct();
    }

    /// <summary>Klassenfächer, die die Lehrkraft unterrichtet (Notenrecht, Inhalte, Appell).</summary>
    public static IQueryable<Guid> KlassenfaecherDesLehrers(SkoulouDbContext db, Guid lehrerId)
        => db.Lehrauftraege.Where(l => l.LehrerId == lehrerId).Select(l => l.KlassenfachId).Distinct();

    /// <summary>Unterrichtet die Lehrkraft in dieser Klasse (irgendein Fach)?</summary>
    public static Task<bool> UnterrichtetKlasseAsync(SkoulouDbContext db, Guid lehrerId, Guid klasseId, CancellationToken ct = default)
        => (from l in db.Lehrauftraege
            join kf in db.Klassenfaecher on l.KlassenfachId equals kf.Id
            where l.LehrerId == lehrerId && kf.KlasseId == klasseId
            select l.Id).AnyAsync(ct);

    /// <summary>Unterrichtet die Lehrkraft genau dieses Klassenfach?</summary>
    public static Task<bool> UnterrichtetKlassenfachAsync(SkoulouDbContext db, Guid lehrerId, Guid klassenfachId, CancellationToken ct = default)
        => db.Lehrauftraege.AnyAsync(l => l.LehrerId == lehrerId && l.KlassenfachId == klassenfachId, ct);

    /// <summary>Ist die Lehrkraft Professeur principal der Klasse?</summary>
    public static Task<bool> IstKlassenlehrerAsync(SkoulouDbContext db, Guid lehrerId, Guid klasseId, CancellationToken ct = default)
        => db.Klassen.AnyAsync(k => k.Id == klasseId && k.KlassenlehrerId == lehrerId, ct);

    /// <summary>Darf die Lehrkraft diesen Schüler sehen (aktive Einschreibung in einer ihrer Klassen)?</summary>
    public static Task<bool> UnterrichtetSchuelerAsync(SkoulouDbContext db, Guid lehrerId, Guid schuelerId, CancellationToken ct = default)
    {
        var klassen = KlassenDesLehrers(db, lehrerId);
        return AktiveEinschreibungen(db).AnyAsync(e => e.SchuelerId == schuelerId && klassen.Contains(e.KlasseId), ct);
    }

    /// <summary>Kinder (Schüler-Konto-IDs) eines Elternkontos.</summary>
    public static IQueryable<Guid> KinderDerEltern(SkoulouDbContext db, Guid elternId)
        => db.ElternKinder.Where(ek => ek.ElternId == elternId).Select(ek => ek.SchuelerId);

    /// <summary>Ist der Schüler ein verknüpftes Kind dieses Elternkontos?</summary>
    public static Task<bool> IstKindAsync(SkoulouDbContext db, Guid elternId, Guid schuelerId, CancellationToken ct = default)
        => db.ElternKinder.AnyAsync(ek => ek.ElternId == elternId && ek.SchuelerId == schuelerId, ct);
}
