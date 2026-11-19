using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Betreuung eines Besitzers durch einen Responsable: neue Läden des Besitzers bekommen denselben Responsable.
/// Läuft im Hintergrunddienst <see cref="VertragsAktualisierung"/>. Die frühere Verwaltungsseite (Zuweisen je Besitzer)
/// wurde nie eingebunden und am 18.11.2026 entfernt; gelesen wird die Betreuung weiter von VertragsService und KontaktService.
/// </summary>
public static class BesitzerBetreuungsService
{
    public static async Task ErgaenzeNeueLaedenAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var betreuungen = await db.BesitzerBetreuungen.Where(b => b.GueltigBisUtc == null).ToListAsync(ct);
        foreach (var b in betreuungen)
        {
            var ids = await db.UserSalons.Where(us => us.AppUserId == b.BesitzerId).Select(us => us.SalonId).ToListAsync(ct);
            foreach (var id in ids)
            {
                var vorhanden = await db.ProduktOwnerSalons.Where(p => p.SalonId == id && p.GueltigBisUtc == null).ToListAsync(ct);
                if (vorhanden.Any(p => p.ProduktOwnerId != b.ProduktOwnerId))
                    throw new InvalidOperationException("Conflit de responsable pour un établissement.");
                if (!vorhanden.Any()) db.ProduktOwnerSalons.Add(new ProduktOwnerSalon { SalonId = id, ProduktOwnerId = b.ProduktOwnerId,
                    KannLadenUmsatzSehen = false, KannPlattformUmsatzSehen = false });
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
