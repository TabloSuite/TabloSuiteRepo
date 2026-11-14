using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Audit-Aktionen des Bereichs Plattform (zusätzlich zu <see cref="AuditAktionen"/>).</summary>
public static class PlattformAudit
{
    public const string SchuleValidiert = "schule.validiert";
    public const string SchuleAbgelehnt = "schule.abgelehnt";
    public const string SchuleSuspendiert = "schule.suspendiert";
    public const string SchuleReaktiviert = "schule.reaktiviert";
    public const string ResponsableZugewiesen = "schule.responsable";
    public const string KontoEntsperrt = "konto.entsperrt";
    public const string SchuleRegistriert = "schule.registriert";
    public const string MediumGeloescht = "medium.geloescht";
    public const string NachrichtZugeordnet = "nachricht.zugeordnet";
}

/// <summary>Rechteprüfung des Bereichs: Administration (SuperAdmin) bzw. Betreuung (SuperAdmin + Responsable).</summary>
internal static class PlattformRechte
{
    /// <summary>Nur der SuperAdmin (Plattform verwalten).</summary>
    public static async Task<Akteur> AdminAsync(this Zugriff zugriff, Guid akteurId, CancellationToken ct)
        => (await zugriff.PruefeAsync(akteurId, Faehigkeit.PlattformVerwalten, schreiben: true, ct: ct)).Akteur;

    /// <summary>SuperAdmin (alle Schulen) oder Responsable (nur seine Schulen).</summary>
    public static async Task<Akteur> BetreuerAsync(this Zugriff zugriff, Guid akteurId, CancellationToken ct)
        => (await zugriff.PruefeAsync(akteurId, Faehigkeit.SchulenBetreuen, ct: ct)).Akteur;

    /// <summary>Schulen im Blick des Akteurs (Admin alle, Responsable seine).</summary>
    public static IQueryable<Schule> SchulenVon(SkoulouDbContext db, Akteur akteur)
        => akteur.IstAdmin ? db.Schulen : db.Schulen.Where(s => s.ResponsableId == akteur.Id);

    /// <summary>Darf der Akteur diese Schule betreuen?</summary>
    public static async Task PruefeBetreuteSchuleAsync(SkoulouDbContext db, Akteur akteur, Guid schuleId, CancellationToken ct)
    {
        if (!await SchulenVon(db, akteur).AnyAsync(s => s.Id == schuleId, ct))
            throw new KeinZugriffFehler(Zugriff.MeldungAndereSchule);
    }
}

/// <summary>Kleine Hilfen für Listen des Bereichs (Muster Take(anzahl + 1)).</summary>
internal static class PlattformListen
{
    public static async Task<(IReadOnlyList<T> Eintraege, bool Mehr)> SeiteAsync<T>(IQueryable<T> abfrage, int ueberspringen, int anzahl, CancellationToken ct)
    {
        anzahl = Math.Clamp(anzahl, 1, 200);
        var liste = await abfrage.Skip(Math.Max(0, ueberspringen)).Take(anzahl + 1).ToListAsync(ct);
        var mehr = liste.Count > anzahl;
        if (mehr) liste.RemoveAt(anzahl);
        return (liste, mehr);
    }

    /// <summary>Suchtext für LIKE (getrimmt, höchstens 100 Zeichen); null = keine Suche.</summary>
    public static string? Suchtext(string? suche)
    {
        var t = suche?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length > 100 ? t[..100] : t;
    }
}
