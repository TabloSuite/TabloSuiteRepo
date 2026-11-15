using ENote.Core.DataModels.Salons;
using ENote.Core.Services;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Land eines Ladens: Auswahl bei der Registrierung, in Paramètres (Besitzer) und in Administration › Établissements
/// (Admin). Angeboten werden die Länder, für die eNOTE eingerichtet ist (Ortszeit in <see cref="Zeitzonen"/> oder
/// aktive Mobile-Money-Anbieter), Hauptmarkt zuerst. Läden ohne Land gelten weiter als Togo (Lomé, Togo-Anbieter).
/// </summary>
public static class LaenderAuswahl
{
    public const string Standard = MobileMoneyService.StandardLand;

    public const string Hinweis = "Le pays détermine l'heure locale (caisse, reçus, finances) et les opérateurs Mobile Money proposés à la caisse.";

    /// <summary>Wählbare Länder (Reihenfolge wie <see cref="Laender.Alle"/>: Westafrika zuerst).</summary>
    public static async Task<List<string>> GetAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var mitAnbieter = await db.MobileMoneyAnbieter.AsNoTracking().Where(a => a.IstAktiv).Select(a => a.Land).Distinct().ToListAsync(ct);
        return Laender.Alle.Where(l => Zeitzonen.Kennt(l) || mitAnbieter.Contains(l)).ToList();
    }

    /// <summary>Prüft die Auswahl (leer = Togo) und liefert den gespeicherten Namen; sonst <see cref="ArgumentException"/>.</summary>
    public static async Task<string> PruefeAsync(ApplicationDbContext db, string? land, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(land)) return Standard;
        var name = land.Trim();
        return (await GetAsync(db, ct)).FirstOrDefault(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Choisissez un pays dans la liste.");
    }

    /// <summary>Folgen eines Wechsels für den Hinweis im Formular (Ortszeit, Mobile Money).</summary>
    public static string Folgen(string land, DateTime utc, bool fuerBesitzer = true)
        => $"En passant à « {land} » : l'heure locale devient {Zeitzonen.VersatzText(land, utc)} pour la caisse, les reçus et les finances, "
           + "et la caisse propose tous les opérateurs Mobile Money de ce pays ("
           + (fuerBesitzer ? "vous pourrez ensuite en retirer" : "le propriétaire peut ensuite en retirer dans « Paramètres »")
           + "). Les ventes déjà enregistrées ne changent pas.";

    /// <summary>
    /// Setzt das Land eines Ladens (Rechte prüft der Aufrufer). Bei einem echten Wechsel gilt für Mobile Money wieder die
    /// Standardauswahl (alle aktiven Anbieter des neuen Landes) statt einer Auswahl aus dem alten Land. Liefert true bei Änderung.
    /// </summary>
    internal static async Task<bool> SetzeAsync(ApplicationDbContext db, Guid salonId, string? land, DateTime jetzt, CancellationToken ct)
    {
        var neu = await PruefeAsync(db, land, ct);
        var salon = await db.Salons.FirstOrDefaultAsync(s => s.Id == salonId, ct) ?? throw new ArgumentException("Établissement introuvable.");
        if (salon.Land == neu) return false;
        var wechsel = (salon.Land ?? Standard) != neu;
        salon.Land = neu;
        salon.UpdatedAtUtc = jetzt;
        if (wechsel && salon.MobileMoneyIndividuell)
        {
            salon.MobileMoneyIndividuell = false;
            db.SalonMobileMoneyAnbieter.RemoveRange(await db.SalonMobileMoneyAnbieter.Where(x => x.SalonId == salonId).ToListAsync(ct));
        }
        await db.SaveChangesAsync(ct);
        return true;
    }
}
