using ENote.WebApp.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ENote.WebApp.Services;

/// <summary>
/// Kassensperre je Laden: Kassenstart, Verkauf, Abschluss, Retrait (Ware und Bargeld), Einlage in die Kasse, Perte,
/// « Enregistrer sans encaisser » (und Rücknahme ins Lager) sowie Rückerstattungen eines Ladens laufen nacheinander.
/// Zwei Geräte desselben Ladens buchen dieselbe Kasse und denselben Bestand: ohne Sperre gingen Bestandsabzüge
/// verloren, ein Verkauf während des Abschlusses fehlte im Soll, zwei Bar-Entnahmen konnten zusammen mehr als das
/// Bargeld nehmen (Klick-Simulation S2, W3). Sperre = SQL-Server-Anwendungssperre (<c>sp_getapplock</c>) der
/// Transaktion, unabhängig von Tabellenzeilen – sie gilt auch ohne offene Kasse (Perte am Besitzer-Gerät).
/// Ohne relationale Datenbank (Unit-Tests) keine Transaktion.
/// </summary>
internal static class KassenSperre
{
    public const string BelegtFehler = "La caisse est occupée sur un autre appareil. Réessayez dans un instant.";

    /// <summary>Fehlernummer, wenn die Sperre nicht innerhalb der Wartezeit frei wird.</summary>
    private const int BelegtNummer = 51000;

    /// <summary>
    /// Führt <paramref name="aktion"/> unter der Sperre des Ladens aus (Retry-Strategie der App, frischer Kontext je Versuch).
    /// Die Aktion bestätigt selbst mit <see cref="BestaetigeAsync"/> nach ihrem SaveChanges; sonst wird alles verworfen.
    /// </summary>
    public static Task<T> MitLadenAsync<T>(IDbContextFactory<ApplicationDbContext> factory, Guid salonId,
        Func<ApplicationDbContext, IDbContextTransaction?, Task<T>> aktion, CancellationToken ct)
        => AusfuehrenAsync(factory, db => SperreLadenAsync(db, salonId, ct), aktion, ct);

    /// <summary>Wie <see cref="MitLadenAsync{T}"/>; der Laden kommt aus der Kassensitzung.</summary>
    public static Task<T> MitSitzungAsync<T>(IDbContextFactory<ApplicationDbContext> factory, Guid sitzungId,
        Func<ApplicationDbContext, IDbContextTransaction?, Task<T>> aktion, CancellationToken ct)
        => AusfuehrenAsync(factory, db => SperreSitzungAsync(db, sitzungId, ct), aktion, ct);

    public static async Task BestaetigeAsync(IDbContextTransaction? transaktion, CancellationToken ct)
    {
        if (transaktion is not null) await transaktion.CommitAsync(ct);
    }

    private static async Task<T> AusfuehrenAsync<T>(IDbContextFactory<ApplicationDbContext> factory,
        Func<ApplicationDbContext, Task<IDbContextTransaction?>> sperre,
        Func<ApplicationDbContext, IDbContextTransaction?, Task<T>> aktion, CancellationToken ct)
    {
        await using var vorbereitung = await factory.CreateDbContextAsync(ct);
        try
        {
            return await vorbereitung.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                await using var transaktion = await sperre(db);
                return await aktion(db, transaktion);
            });
        }
        catch (SqlException ex) when (ex.Number == BelegtNummer)
        {
            throw new InvalidOperationException(BelegtFehler, ex);
        }
    }

    /// <summary>Beginnt die Transaktion und wartet (höchstens 20 s) auf die Sperre des Ladens.</summary>
    public static async Task<IDbContextTransaction?> SperreLadenAsync(ApplicationDbContext db, Guid salonId, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return null;
        // Bei einem Fehler wird die ganze Transaktion verworfen – Savepoints unnötig (und mit MARS ohnehin aus, sonst Warnung je Verkauf).
        db.Database.AutoSavepointsEnabled = false;
        var transaktion = await db.Database.BeginTransactionAsync(ct);
        var ressource = "enote-kasse-" + salonId.ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DECLARE @r int; EXEC @r = sp_getapplock @Resource = {ressource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 20000; IF @r < 0 THROW 51000, N'Kassensperre belegt', 1;", ct);
        return transaktion;
    }

    /// <summary>Sperre des Ladens, zu dem die Kassensitzung gehört.</summary>
    public static async Task<IDbContextTransaction?> SperreSitzungAsync(ApplicationDbContext db, Guid sitzungId, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return null;
        var salonId = await db.KassenSitzungen.AsNoTracking().Where(k => k.Id == sitzungId).Select(k => (Guid?)k.SalonId).FirstOrDefaultAsync(ct);
        // Unbekannte Sitzung: eigene Sperre, der Aufrufer meldet danach « Aucune caisse ouverte ».
        return await SperreLadenAsync(db, salonId ?? sitzungId, ct);
    }
}
