using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Noten;

/// <summary>Klassenfach mit Klasse, Fach und Schuljahr (geladen und rechtegeprüft).</summary>
internal sealed record KlassenfachKontext(Klassenfach Kf, Klasse Klasse, Schuljahr Jahr, string Fach, bool Schreiben);

/// <summary>Rechte des Bereichs Noten: Direktion alle Fächer; Lehrkraft nur mit Lehrauftrag; Professeur principal liest seine Klasse.</summary>
internal static class NotenRechte
{
    public const string MeldungKeinAuftrag = "Vous n'enseignez pas cette matière dans cette classe.";
    public const string MeldungKeineKlasse = "Vous n'avez pas accès aux notes de cette classe.";

    /// <summary>Direktion oder Lehrkraft (Schüler/Eltern haben hier keinen Zugriff).</summary>
    public static async Task<SchulAkteur> PersonalAsync(this Zugriff zugriff, Guid akteurId, CancellationToken ct)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.NotenErfassen, schreiben: true, ct);
        if (a.Rolle is not (Rollen.Lehrer or Rollen.Direktor or Rollen.Koordinator)) throw new KeinZugriffFehler();
        return a;
    }

    /// <summary>Klassenfach laden; Lesen: Direktion, Lehrauftrag oder Professeur principal; Schreiben: Direktion oder Lehrauftrag.</summary>
    public static async Task<(SchulAkteur A, KlassenfachKontext K)> KlassenfachAsync(this Zugriff zugriff, SkoulouDbContext db, Guid akteurId,
        Guid klassenfachId, bool schreiben, CancellationToken ct)
    {
        var a = await zugriff.PersonalAsync(akteurId, ct);
        var kf = await db.Klassenfaecher.FirstOrDefaultAsync(k => k.Id == klassenfachId, ct) ?? throw new FachFehler("Cette matière est introuvable dans la classe.");
        a.PruefeSchule(kf.SchuleId);
        var klasse = await db.Klassen.FirstAsync(k => k.Id == kf.KlasseId, ct);
        var jahr = await db.Schuljahre.FirstAsync(j => j.Id == klasse.SchuljahrId, ct);
        var fach = await db.Faecher.Where(f => f.Id == kf.FachId).Select(f => f.Name).FirstAsync(ct);
        var darfSchreiben = a.IstDirektion || await SchulAbfragen.UnterrichtetKlassenfachAsync(db, a.Id, kf.Id, ct);
        if (!darfSchreiben)
        {
            if (schreiben) throw new KeinZugriffFehler(MeldungKeinAuftrag);
            if (!await SchulAbfragen.IstKlassenlehrerAsync(db, a.Id, klasse.Id, ct)) throw new KeinZugriffFehler(MeldungKeinAuftrag);
        }
        return (a, new KlassenfachKontext(kf, klasse, jahr, fach, darfSchreiben));
    }

    /// <summary>Klasse für Conseil/Bulletins: Direktion jede Klasse der Schule; Lehrkraft nur als Professeur principal.</summary>
    public static async Task<(SchulAkteur A, Klasse Klasse, bool Direktion)> KlasseFuerConseilAsync(this Zugriff zugriff, SkoulouDbContext db, Guid akteurId,
        Guid klasseId, CancellationToken ct)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenratErfassen, schreiben: false, ct);
        var klasse = await db.Klassen.FirstOrDefaultAsync(k => k.Id == klasseId, ct) ?? throw new FachFehler("Cette classe est introuvable.");
        a.PruefeSchule(klasse.SchuleId);
        if (a.Art == Zugriffsart.Eigene && klasse.KlassenlehrerId != a.Id)
            throw new KeinZugriffFehler("Seul le professeur principal de la classe prépare son conseil.");
        return (a, klasse, a.IstDirektion);
    }

    /// <summary>Periode des Schuljahres der Klasse (oder Fehler).</summary>
    public static async Task<Periode> PeriodeAsync(SkoulouDbContext db, Klasse klasse, Guid periodeId, CancellationToken ct)
    {
        var periode = await db.Perioden.FirstOrDefaultAsync(p => p.Id == periodeId, ct) ?? throw new FachFehler("Cette période est introuvable.");
        if (periode.SchuljahrId != klasse.SchuljahrId) throw new FachFehler("Cette période n'appartient pas à l'année de la classe.");
        if (klasse.PeriodeId is { } nur && nur != periode.Id) throw new FachFehler("Cette classe ne concerne qu'une autre session.");
        return periode;
    }
}

/// <summary>Sperrmatrix (Fachkonzept 04 §3.20, Bauplan §11): was ist gesperrt, und warum (Text für die Oberfläche)?</summary>
internal static class NotenSperren
{
    public static Task<VeroeffentlichungsStatus?> StatusAsync(SkoulouDbContext db, Guid klasseId, Guid periodeId, CancellationToken ct)
        => db.KlassenVeroeffentlichungen.Where(v => v.KlasseId == klasseId && v.PeriodeId == periodeId)
            .Select(v => (VeroeffentlichungsStatus?)v.Status).FirstOrDefaultAsync(ct);

    /// <summary>Noten, Appréciations: Jahr offen; veröffentlicht → nur in Rectification; geschlossene Periode → gesperrt (außer Rectification).</summary>
    public static string? Noten(Schuljahr jahr, Periode periode, VeroeffentlichungsStatus? status)
    {
        if (jahr.Status == SchuljahrStatus.Abgeschlossen) return $"L'année scolaire {jahr.Bezeichnung} est clôturée : les notes ne peuvent plus être modifiées.";
        if (status == VeroeffentlichungsStatus.EnRectification) return null;
        if (status == VeroeffentlichungsStatus.Publiee)
            return $"Les bulletins du « {periode.Titel} » sont publiés pour cette classe : seule une rectification ouverte par la direction permet de corriger.";
        if (periode.Status == PeriodenStatus.Geschlossen) return $"La période « {periode.Titel} » est fermée : la direction peut la rouvrir dans Année scolaire.";
        return null;
    }

    /// <summary>Bewertungen anlegen/ändern/löschen: wie Noten, aber auch während einer Rectification gesperrt.</summary>
    public static string? Struktur(Schuljahr jahr, Periode periode, VeroeffentlichungsStatus? status)
        => status == VeroeffentlichungsStatus.EnRectification
            ? "Une rectification est en cours : seules les notes existantes peuvent être corrigées."
            : Noten(jahr, periode, status);

    /// <summary>Conseil de classe: nicht nach Veröffentlichung (außer Rectification), nicht im abgeschlossenen Jahr.</summary>
    public static string? Conseil(Schuljahr jahr, Periode periode, VeroeffentlichungsStatus? status)
    {
        if (jahr.Status == SchuljahrStatus.Abgeschlossen) return $"L'année scolaire {jahr.Bezeichnung} est clôturée.";
        return status == VeroeffentlichungsStatus.Publiee
            ? $"Les bulletins du « {periode.Titel} » sont publiés : le conseil ne peut plus être modifié (sauf rectification)."
            : null;
    }
}

/// <summary>Schreibvorgänge mit Transaktion und Wiederholung bei gleichzeitiger Änderung (RowVersion, Unique-Index).</summary>
internal static class NotenDb
{
    public const string MeldungKonflikt = "Cette donnée vient d'être modifiée par une autre personne. Rechargez la page et réessayez.";

    public static async Task<T> TransaktionAsync<T>(this IDbContextFactory<SkoulouDbContext> fabrik, Func<SkoulouDbContext, Task<T>> arbeit,
        CancellationToken ct = default, int versuche = 6)
    {
        for (var versuch = 1; ; versuch++)
        {
            try
            {
                await using var probe = await fabrik.CreateDbContextAsync(ct);
                var strategie = probe.Database.CreateExecutionStrategy();
                return await strategie.ExecuteAsync(async () =>
                {
                    await using var db = await fabrik.CreateDbContextAsync(ct);
                    if (!db.Database.IsRelational()) return await arbeit(db);
                    await using var tx = await db.Database.BeginTransactionAsync(ct);
                    var ergebnis = await arbeit(db);
                    await tx.CommitAsync(ct);
                    return ergebnis;
                });
            }
            catch (Exception ex) when (IstKonflikt(ex))
            {
                if (versuch >= versuche) throw new FachFehler(MeldungKonflikt);
            }
        }
    }

    public static Task TransaktionAsync(this IDbContextFactory<SkoulouDbContext> fabrik, Func<SkoulouDbContext, Task> arbeit, CancellationToken ct = default)
        => fabrik.TransaktionAsync<bool>(async db => { await arbeit(db); return true; }, ct);

    private static bool IstKonflikt(Exception ex)
        => ex is DbUpdateConcurrencyException || (ex is DbUpdateException d && DbArbeit.IstEindeutigkeitsFehler(d));
}

/// <summary>Audit-Codes des Bereichs (ergänzen <see cref="AuditAktionen"/>).</summary>
public static class NotenAudit
{
    public const string BulletinZurueckgezogen = "bulletin.retire";
    public const string RectificationGeoeffnet = "bulletin.rectification-ouverte";
    public const string RectificationAbgebrochen = "bulletin.rectification-abandonnee";
}

/// <summary>Kleine Formatierungen des Bereichs.</summary>
internal static class NotenFormat
{
    public static string Name(string vorname, string nachname) => $"{nachname.ToUpperInvariant()} {vorname}".Trim();

    public static string? Text(string? wert, int max, string bezeichnung)
    {
        var t = string.IsNullOrWhiteSpace(wert) ? null : string.Join(' ', wert.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (t is not null && t.Length > max) throw new FachFehler($"{bezeichnung} est trop long ({max} caractères au maximum).");
        return t;
    }
}
