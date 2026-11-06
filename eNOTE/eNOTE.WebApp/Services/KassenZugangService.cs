using ENote.Core.DataModels.Abo;
using ENote.Core.Services;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

public class KassenZugangService(IDbContextFactory<ApplicationDbContext> factory, TimeProvider zeit)
{
    public record Zugang(bool Gesperrt, string? Hinweis, DateTime? SperreAb);

    public async Task<Zugang> PruefeAsync(Guid salonId, CancellationToken ct = default)
    {
        await new VertragsService(factory, zeit).SynchronisiereAsync(salonId, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var salon = await db.Salons.AsNoTracking().SingleOrDefaultAsync(s => s.Id == salonId, ct);
        if (salon is null || !salon.IstAktiv || !salon.IstFreigegeben)
            return new(true, "Cet établissement n'est pas autorisé à utiliser la caisse.", null);
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        if (salon.ProbeBisUtc > jetzt) return new(false, null, null);
        var offen = await db.NutzungsAbos.AsNoTracking().Where(a => a.SalonId == salonId
            && (a.ZahlungStatus == AboZahlungStatus.Offen || a.ZahlungStatus == AboZahlungStatus.Abgelehnt)
            && (a.FaelligUtc ?? a.ZeitraumVonUtc) <= jetzt).ToListAsync(ct);
        var frist = offen.Select(a => a.KassenSperreAbUtc ?? VertragsRegeln.SperreAb(a.FaelligUtc ?? a.ZeitraumVonUtc))
            .OrderBy(d => d).Select(d => (DateTime?)d).FirstOrDefault();
        if (frist is null) return new(false, null, null);
        var gesperrt = jetzt >= frist;
        return new(gesperrt, gesperrt
            ? "Caisse suspendue : un paiement arrivé à échéance n'a pas été signalé. Veuillez contacter le propriétaire. La clôture de la caisse reste possible."
            : $"Abonnement arrivé à échéance. Sans signalement du virement, les ventes seront suspendues le {frist:dd/MM/yyyy}. Veuillez contacter le propriétaire.", frist);
    }
}
