using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.KiHilfe;

/// <summary>
/// Schalter « Assistant (Poser une question) pour tous les rôles » im Admin-Bereich (21.11.2026): gibt die KI-Hilfe ohne Deploy
/// für alle Rollen frei (<see cref="PlattformEinstellung.KiFuerAlleRollen"/>). Menü und Seiten fragen den Wert bei jedem Zeichnen –
/// deshalb höchstens einmal je <see cref="Gueltigkeit"/> aus der Datenbank (beim Schalten sofort neu). Nur der Administrateur
/// schaltet (serverseitig über <see cref="Faehigkeit.PlattformVerwalten"/>); jede Änderung steht im Journal.
/// </summary>
public sealed class KiFreigabe(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit, TimeProvider zeit,
    ILogger<KiFreigabe> log)
{
    public static readonly TimeSpan Gueltigkeit = TimeSpan.FromMinutes(1);
    public const string AuditCode = "plattform.ki-freigabe";

    private sealed record Stand(bool Wert, DateTimeOffset Bis);

    private readonly Lock sperre = new();
    private volatile Stand? stand;

    /// <summary>Zwischengespeicherter Schalter (höchstens eine kurze Datenbankabfrage je Minute; bei einem Fehler der letzte Wert bzw. aus).</summary>
    public bool FuerAlleRollen
    {
        get
        {
            if (stand is { } s && zeit.GetUtcNow() < s.Bis) return s.Wert;
            lock (sperre)
            {
                if (stand is { } t && zeit.GetUtcNow() < t.Bis) return t.Wert;
                var wert = Lesen();
                stand = new Stand(wert, zeit.GetUtcNow() + Gueltigkeit);
                return wert;
            }
        }
    }

    private bool Lesen()
    {
        try
        {
            using var db = dbFactory.CreateDbContext();
            return db.PlattformEinstellungen.AsNoTracking().Select(e => e.KiFuerAlleRollen).FirstOrDefault();
        }
        catch (Exception e)
        {
            log.LogWarning("KI-Freigabe nicht lesbar ({Typ}) – bisheriger Wert bleibt.", e.GetType().Name);
            return stand?.Wert ?? false;
        }
    }

    /// <summary>Aktueller Stand für die Seite « Tarifs » (nur Administrateur).</summary>
    public async Task<bool> LesenAsync(Guid akteurId, CancellationToken ct = default)
    {
        await zugriff.PruefeAsync(akteurId, Faehigkeit.PlattformVerwalten, null, false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PlattformEinstellungen.AsNoTracking().Select(e => e.KiFuerAlleRollen).FirstOrDefaultAsync(ct);
    }

    /// <summary>Schalten (nur Administrateur, mit Journal); wirkt in dieser Instanz sofort, in weiteren spätestens nach einer Minute.</summary>
    public async Task SetzenAsync(Guid akteurId, bool an, CancellationToken ct = default)
    {
        var admin = (await zugriff.PruefeAsync(akteurId, Faehigkeit.PlattformVerwalten, null, true, ct)).Akteur;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var e = await db.PlattformEinstellungen.FirstOrDefaultAsync(ct);
            if (e is null)
            {
                e = new PlattformEinstellung { Id = PlattformEinstellung.EinzigeId };
                db.PlattformEinstellungen.Add(e);
            }
            if (e.KiFuerAlleRollen != an)
            {
                e.KiFuerAlleRollen = an;
                audit.Erfasse(db, admin.Id, null, AuditCode, nameof(PlattformEinstellung), e.Id,
                    an ? "Assistant (Poser une question) activé pour tous les rôles." : "Assistant (Poser une question) réservé à l'équipe SKOULOU.");
                await db.SaveChangesAsync(ct);   // ein SaveChanges: Schalter und Journal gemeinsam
            }
        }
        stand = new Stand(an, zeit.GetUtcNow() + Gueltigkeit);
    }
}
