using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Gültigkeit des Kunden-Display-Links (22.11.2026, Nutzerentscheidung « un nouveau lien remplace l'ancien »; vorher galt ein
/// Link unbegrenzt – im Klicktest noch nach zwei Jahren). Der Link <c>/display/{Laden}/{Gerät}</c> gehört zum Kassengerät.
/// Gültig ist nur der Link des Geräts, an dem die jüngste Kasse des Ladens läuft bzw. zuletzt lief – offen oder schon
/// geschlossen, damit das Display « aussi les jours suivants » verbunden bleibt. Läuft die Kasse auf einem anderen Gerät
/// (« Reprendre la caisse ici » oder die nächste Kasse dort geöffnet), zeigt dieses Gerät im Dialog « Écran client » einen
/// neuen Link; ein Display mit dem alten Link zeigt « Ce lien n'est plus valide » statt still weiterzulaufen.
/// Ohne Migration: maßgeblich ist <c>KassenSitzung.GeraetSchluessel</c>.
/// </summary>
public static class KundenDisplayLink
{
    public const string UngueltigTitel = "Ce lien n'est plus valide";
    public const string UngueltigText = "Demandez le nouveau lien en caisse : touchez « Écran client » et scannez le code QR.";

    /// <summary>So oft prüft ein offenes Display, ob sein Link noch gilt (zusätzlich sofort bei « Reprendre la caisse ici »).</summary>
    public static readonly TimeSpan PruefIntervall = TimeSpan.FromSeconds(30);

    public static async Task<bool> IstGueltigAsync(ApplicationDbContext db, Guid salonId, string? geraet, CancellationToken ct = default)
    {
        if (!UmsatzHub.IstGeraetSchluessel(geraet)) return false;
        var geraetDerKasse = await db.KassenSitzungen.AsNoTracking()
            .Where(k => k.SalonId == salonId)
            .OrderByDescending(k => k.GestartetUtc)
            .Select(k => k.GeraetSchluessel)
            .FirstOrDefaultAsync(ct);
        return geraetDerKasse is not null && string.Equals(geraetDerKasse, geraet, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<bool> IstGueltigAsync(IDbContextFactory<ApplicationDbContext> factory, Guid salonId, string? geraet, CancellationToken ct = default)
    {
        if (!UmsatzHub.IstGeraetSchluessel(geraet)) return false;
        await using var db = await factory.CreateDbContextAsync(ct);
        return await IstGueltigAsync(db, salonId, geraet, ct);
    }
}
