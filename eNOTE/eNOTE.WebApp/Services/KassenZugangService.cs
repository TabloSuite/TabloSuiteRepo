using ENote.Core.DataModels.Abo;
using ENote.Core.Services;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

public class KassenZugangService(IDbContextFactory<ApplicationDbContext> factory, TimeProvider zeit)
{
    public record Zugang(bool Gesperrt, string? Hinweis, DateTime? SperreAb);

    /// <param name="synchronisieren">
    /// true: fällige Perioden vorher anlegen (Kassenstart, Hinweise). false: nur vorhandene Perioden
    /// prüfen – schnell genug für jeden einzelnen Verkauf.
    /// </param>
    public async Task<Zugang> PruefeAsync(Guid salonId, CancellationToken ct = default, bool synchronisieren = true)
    {
        if (synchronisieren)
        {
            try
            {
                await new VertragsService(factory, zeit).SynchronisiereAsync(salonId, ct);
            }
            catch (InvalidOperationException)
            {
                // Kein Tarif konfiguriert: es kann nichts in Rechnung gestellt werden – das darf
                // die Kasse nicht blockieren (Admin-Aufgabe). Vorhandene Perioden zählen weiter.
            }
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var salon = await db.Salons.AsNoTracking().SingleOrDefaultAsync(s => s.Id == salonId, ct);
        if (salon is null || !salon.IstAktiv || !salon.IstFreigegeben)
            return new(true, "Cet établissement n'est pas autorisé à utiliser la caisse.", null);
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        if (salon.LoeschungBeantragtUtc is not null)
            return new(true, $"Suppression du compte demandée : l'établissement est fermé. Le propriétaire peut l'annuler dans « Abonnement » jusqu'au {salon.LoeschungAbUtc:dd'/'MM'/'yyyy}.", salon.LoeschungBeantragtUtc);
        if (salon.GekuendigtZumUtc is DateTime zum && zum <= jetzt)
            return new(true, $"Abonnement résilié depuis le {zum:dd'/'MM'/'yyyy} : la caisse est fermée. Le propriétaire peut le réactiver dans « Abonnement ».", zum);
        var kuendigungsHinweis = salon.GekuendigtZumUtc is DateTime ende
            ? $"Abonnement résilié : la caisse reste disponible jusqu'au {ende.AddTicks(-1):dd'/'MM'/'yyyy} inclus."
            : null;
        if (salon.ProbeBisUtc > jetzt) return new(false, kuendigungsHinweis, salon.GekuendigtZumUtc);

        // Unbezahlt = alles außer "Bestaetigt". Auch "Gemeldet" (vom Besitzer als
        // überwiesen markiert) sperrt weiter, bis der Admin den Eingang bestätigt hat.
        var offen = await db.NutzungsAbos.AsNoTracking().Where(a => a.SalonId == salonId
            && a.ZahlungStatus != AboZahlungStatus.Bestaetigt
            && (a.FaelligUtc ?? a.ZeitraumVonUtc) <= jetzt).ToListAsync(ct);
        var frist = offen.Select(a => a.KassenSperreAbUtc ?? VertragsRegeln.SperreAb(a.FaelligUtc ?? a.ZeitraumVonUtc))
            .OrderBy(d => d).Select(d => (DateTime?)d).FirstOrDefault();
        if (frist is null) return new(false, kuendigungsHinweis, salon.GekuendigtZumUtc);

        var gesperrt = jetzt >= frist;
        var nurGemeldet = offen.All(a => a.ZahlungStatus == AboZahlungStatus.Gemeldet);
        var hinweis = gesperrt
            ? (nurGemeldet
                ? "Caisse suspendue : le paiement signalé n'a pas encore été confirmé par l'administrateur. La clôture de la caisse reste possible."
                : "Caisse suspendue : un paiement arrivé à échéance n'a pas été signalé. Veuillez contacter le propriétaire. La clôture de la caisse reste possible.")
            : (nurGemeldet
                ? $"Paiement signalé, en attente de confirmation par l'administrateur. Sans confirmation, les ventes seront suspendues le {frist:dd/MM/yyyy}."
                : $"Abonnement arrivé à échéance. Sans signalement du virement, les ventes seront suspendues le {frist:dd/MM/yyyy}. Veuillez contacter le propriétaire.");
        return new(gesperrt, hinweis, frist);
    }
}
