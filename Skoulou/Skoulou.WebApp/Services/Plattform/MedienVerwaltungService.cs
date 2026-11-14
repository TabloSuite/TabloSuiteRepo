using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Medien;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Zeile der Medienliste (ohne internen Speicherpfad).</summary>
public sealed record MedienEintrag(Guid Id, string Dateiname, MedienArt Art, MedienZweck Zweck, long Bytes, DateTime ErstelltAmUtc,
    string? Schule, string? Besitzer, bool Genutzt);

/// <summary>Summen der Medien (gesamt, ungenutzt, je Schule).</summary>
public sealed record MedienSummen(int Anzahl, long Bytes, int Ungenutzt, long UngenutztBytes, IReadOnlyList<MedienJeSchule> JeSchule);

public sealed record MedienJeSchule(Guid? SchuleId, string Schule, int Anzahl, long Bytes);

/// <summary>
/// Medienübersicht (nur SuperAdmin): Liste mit Nutzung, Größensummen, Löschen ungenutzter Dateien.
/// « Genutzt » = Medien-Nutzung (Inhalte, Logo, Foto) oder direkte Referenz (Schullogo, Profilfoto, Video einer
/// Leçon, Beleg, Zahlungsmeldung, Bulletin-Snapshot) – solche Medien sind gegen Löschen geschützt.
/// </summary>
public sealed class MedienVerwaltungService(
    IDbContextFactory<SkoulouDbContext> dbFactory,
    Zugriff zugriff,
    AuditService audit,
    IMedienSpeicher speicher)
{
    public const string FilterUngenutzt = "non-utilises";
    public const string FilterBilder = "images";
    public const string FilterVideos = "videos";
    public const string FilterDokumente = "documents";

    /// <summary>Alle referenzierten Medien-Ids (eine Unterabfrage für SQL).</summary>
    internal static IQueryable<Guid> GenutzteIds(SkoulouDbContext db)
        => db.MedienNutzungen.Select(n => n.MediumId)
            .Concat(db.Schulen.Where(s => s.LogoMediumId != null).Select(s => s.LogoMediumId!.Value))
            .Concat(db.Users.Where(k => k.FotoMediumId != null).Select(k => k.FotoMediumId!.Value))
            .Concat(db.Lecons.Where(l => l.VideoMediumId != null).Select(l => l.VideoMediumId!.Value))
            .Concat(db.Buchungen.Where(b => b.BelegMediumId != null).Select(b => b.BelegMediumId!.Value))
            .Concat(db.Zahlungsmeldungen.Where(z => z.BelegMediumId != null).Select(z => z.BelegMediumId!.Value))
            .Concat(db.Bulletins.Where(b => b.LogoMediumId != null).Select(b => b.LogoMediumId!.Value))
            .Concat(db.Bulletins.Where(b => b.FotoMediumId != null).Select(b => b.FotoMediumId!.Value));

    public async Task<MedienSummen> SummenAsync(Guid akteurId, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var genutzt = GenutzteIds(db);
        var gesamt = await db.Medien.GroupBy(_ => 1).Select(g => new { Anzahl = g.Count(), Bytes = g.Sum(m => m.Bytes) }).FirstOrDefaultAsync(ct);
        var frei = await db.Medien.Where(m => !genutzt.Contains(m.Id)).GroupBy(_ => 1)
            .Select(g => new { Anzahl = g.Count(), Bytes = g.Sum(m => m.Bytes) }).FirstOrDefaultAsync(ct);
        var je = await db.Medien.GroupBy(m => m.SchuleId).Select(g => new { g.Key, Anzahl = g.Count(), Bytes = g.Sum(m => m.Bytes) })
            .OrderByDescending(x => x.Bytes).Take(12).ToListAsync(ct);
        var ids = je.Where(x => x.Key != null).Select(x => x.Key!.Value).ToList();
        var namen = await db.Schulen.Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return new MedienSummen(gesamt?.Anzahl ?? 0, gesamt?.Bytes ?? 0, frei?.Anzahl ?? 0, frei?.Bytes ?? 0,
            je.Select(x => new MedienJeSchule(x.Key, x.Key is { } k && namen.TryGetValue(k, out var n) ? n : "Plateforme", x.Anzahl, x.Bytes)).ToList());
    }

    public async Task<(IReadOnlyList<MedienEintrag> Eintraege, bool Mehr)> SucheAsync(Guid akteurId, string? suche, string? filter, Guid? schuleId,
        int ueberspringen = 0, int anzahl = 50, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var genutzt = GenutzteIds(db);
        var q = db.Medien.AsNoTracking();
        q = filter switch
        {
            FilterUngenutzt => q.Where(m => !genutzt.Contains(m.Id)),
            FilterBilder => q.Where(m => m.Art == MedienArt.Bild),
            FilterVideos => q.Where(m => m.Art == MedienArt.Video),
            FilterDokumente => q.Where(m => m.Art == MedienArt.Dokument),
            _ => q
        };
        if (schuleId is { } s) q = q.Where(m => m.SchuleId == s);
        if (PlattformListen.Suchtext(suche) is { } t) q = q.Where(m => m.Dateiname.Contains(t));
        var abfrage = q.OrderByDescending(m => m.Bytes).ThenByDescending(m => m.ErstelltAmUtc).Select(m => new MedienEintrag(
            m.Id, m.Dateiname, m.Art, m.Zweck, m.Bytes, m.ErstelltAmUtc,
            db.Schulen.Where(sc => sc.Id == m.SchuleId).Select(sc => sc.Name).FirstOrDefault(),
            db.Users.Where(k => k.Id == m.BesitzerId).Select(k => k.Vorname + " " + k.Nachname).FirstOrDefault(),
            genutzt.Contains(m.Id)));
        return await PlattformListen.SeiteAsync(abfrage, ueberspringen, anzahl, ct);
    }

    /// <summary>Löscht ein ungenutztes Medium (Datensatz zuerst, dann die Datei).</summary>
    public async Task LoeschenAsync(Guid akteurId, Guid mediumId, CancellationToken ct = default)
    {
        var akteur = await zugriff.AdminAsync(akteurId, ct);
        string blob;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var m = await db.Medien.FirstOrDefaultAsync(x => x.Id == mediumId, ct) ?? throw new FachFehler("Fichier introuvable.");
            if (await GenutzteIds(db).ContainsAsync(m.Id, ct)) throw new FachFehler("Ce fichier est encore utilisé et ne peut pas être supprimé.");
            blob = m.BlobName;
            db.Medien.Remove(m);
            audit.Erfasse(db, akteur.Id, m.SchuleId, PlattformAudit.MediumGeloescht, nameof(Medium), m.Id,
                $"Fichier non utilisé « {m.Dateiname} » supprimé ({m.Bytes} octets).");
            await db.SaveChangesAsync(ct);
        }
        await speicher.LoeschenAsync(blob, ct);
    }

    /// <summary>Größe lesbar (« 1,4 Mo »).</summary>
    public static string Groesse(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} o",
        < 1024 * 1024 => $"{bytes / 1024d:0} Ko",
        < 1024L * 1024 * 1024 => $"{bytes / 1024d / 1024:0.#} Mo",
        _ => $"{bytes / 1024d / 1024 / 1024:0.##} Go"
    };
}
