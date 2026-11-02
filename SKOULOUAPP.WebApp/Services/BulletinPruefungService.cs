using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.WebApp.Data;

namespace SkoulouApp.WebApp.Services;

public sealed record BulletinPruefErgebnis(string Schule, string DokumentNummer, DateTime DatumUtc, string Status, string? ErsetztNummer, string? NachfolgerNummer);

public sealed class BulletinPruefungService(IDbContextFactory<ApplicationDbContext> factory)
{
    public async Task<BulletinPruefErgebnis?> PruefenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (token.Length != 64 || token.Any(x => !Uri.IsHexDigit(x))) return null;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var ausgabe = await db.Set<BulletinAusgabe>().AsNoTracking().Where(x => x.PruefToken == token)
            .Select(x => new { x.Id, Schule = x.Schule.OffizielleBezeichnung ?? x.Schule.Name, x.ErstelltAmUtc, x.ErsetztDurchID, x.ErsetztAusgabeID, x.IstVeroeffentlicht })
            .SingleOrDefaultAsync(cancellationToken);
        if (ausgabe is null) return null;
        return new(ausgabe.Schule, $"B-{ausgabe.Id:N}", ausgabe.ErstelltAmUtc,
            ausgabe.ErsetztDurchID != null ? "Remplacé par un bulletin rectificatif" : ausgabe.IstVeroeffentlicht ? "Valide" : "Non valide",
            ausgabe.ErsetztAusgabeID is Guid alt ? $"B-{alt:N}" : null,
            ausgabe.ErsetztDurchID is Guid neu ? $"B-{neu:N}" : null);
    }
}
