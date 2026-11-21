using ENote.Core.DataModels.Infrastruktur;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Plattform-Schalter der Administration (Tabelle <c>PlattformEinstellungen</c>, eine Zeile) – Freigaben ohne Deployment
/// (21.11.2026: « Assistant (Poser une question) pour tous les rôles »). Der Wert steht 60 s im Speicher (das Menü fragt
/// bei jedem Rendern); eine Änderung gilt sofort. Ist die Tabelle (noch) nicht da oder die Datenbank kurz weg, gilt der
/// letzte bekannte Wert bzw. der Standard (aus). Nur der Administrateur (SuperAdmin) darf schalten – serverseitig geprüft.
/// </summary>
public sealed class PlattformEinstellungen(IDbContextFactory<ApplicationDbContext> factory, ILogger<PlattformEinstellungen> logger,
    TimeProvider? zeit = null)
{
    public static readonly TimeSpan CacheDauer = TimeSpan.FromSeconds(60);

    private readonly TimeProvider uhr = zeit ?? ENote.Core.Services.AppUhr.Provider;
    private readonly object sperre = new();
    private Stand? stand;
    private Task? laufend;

    private sealed record Stand(bool KiFuerAlleRollen, DateTimeOffset GeladenUtc);

    private bool IstFrisch(Stand? s) => s is not null && uhr.GetUtcNow() - s.GeladenUtc < CacheDauer;

    /// <summary>Letzter bekannter Wert ohne Warten (Menü); ist er älter als 60 s, wird im Hintergrund neu geladen.</summary>
    public bool KiFuerAlleRollen
    {
        get
        {
            var s = Volatile.Read(ref stand);
            if (!IstFrisch(s)) LadeImHintergrund();
            return s?.KiFuerAlleRollen ?? false;
        }
    }

    /// <summary>Aktueller Wert (höchstens 60 s alt).</summary>
    public async Task<bool> GetKiFuerAlleRollenAsync(CancellationToken ct = default)
    {
        var s = Volatile.Read(ref stand);
        return IstFrisch(s) ? s!.KiFuerAlleRollen : (await LadeAsync(ct)).KiFuerAlleRollen;
    }

    /// <summary>Schalter « Assistant pour tous les rôles » setzen – nur Administrateur.</summary>
    public async Task SetzeKiFuerAlleRollenAsync(Guid bearbeiter, bool wert, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await AdminKontenService.PruefeAsync(db, bearbeiter, false, ct);
        var e = await db.PlattformEinstellungen.FirstOrDefaultAsync(x => x.Id == PlattformEinstellung.FesteId, ct);
        if (e is null) db.PlattformEinstellungen.Add(e = new PlattformEinstellung());
        e.KiFuerAlleRollen = wert;
        e.GeaendertUtc = uhr.GetUtcNow().UtcDateTime;
        e.GeaendertVonId = bearbeiter;
        await db.SaveChangesAsync(ct);
        Volatile.Write(ref stand, new Stand(wert, uhr.GetUtcNow()));
        logger.LogInformation("Plattform-Einstellung « KI für alle Rollen » = {Wert} (durch {Bearbeiter}).", wert, bearbeiter);
    }

    private async Task<Stand> LadeAsync(CancellationToken ct)
    {
        Stand neu;
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var wert = await db.PlattformEinstellungen.AsNoTracking().Where(x => x.Id == PlattformEinstellung.FesteId)
                .Select(x => (bool?)x.KiFuerAlleRollen).FirstOrDefaultAsync(ct);
            neu = new Stand(wert ?? false, uhr.GetUtcNow());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Plattform-Einstellungen nicht lesbar (Migration eingespielt?) – bisheriger Wert bzw. Standard gilt.");
            neu = new Stand(Volatile.Read(ref stand)?.KiFuerAlleRollen ?? false, uhr.GetUtcNow());
        }
        Volatile.Write(ref stand, neu);
        return neu;
    }

    private void LadeImHintergrund()
    {
        lock (sperre)
        {
            if (laufend is { IsCompleted: false }) return;
            laufend = Task.Run(() => LadeAsync(CancellationToken.None));
        }
    }
}
