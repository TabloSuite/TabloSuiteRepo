using System.Text.RegularExpressions;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Mobile-Money-Anbieter: Plattform-Stammdaten (SuperAdmin) und Auswahl pro Laden (Besitzer).
/// Ohne explizite Auswahl akzeptiert ein Laden alle aktiven Anbieter seines Landes.
/// </summary>
public partial class MobileMoneyService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    /// <summary>
    /// Land eines Ladens ohne Angabe (Selbstregistrierung fragt kein Land ab): Togo – wie die Ortszeit (Lomé, <see cref="ENote.Core.Services.Zeitzonen.Standard"/>).
    /// Sonst hätte ein selbst registrierter Laden keinen einzigen Mobile-Money-Anbieter.
    /// </summary>
    public const string StandardLand = "Togo";

    public record AnbieterAuswahl(MobileMoneyAnbieter Anbieter, bool Akzeptiert);

    /// <summary>
    /// Aktive Anbieter, die der Laden akzeptiert: explizite Auswahl, sonst alle aktiven
    /// Anbieter des Laden-Landes. Wird auch beim Buchen zur Validierung genutzt.
    /// </summary>
    public static IQueryable<MobileMoneyAnbieter> ErlaubteAnbieterAbfrage(ApplicationDbContext db, Guid salonId)
    {
        var salon = db.Salons.Where(s => s.Id == salonId);
        var explizit = db.SalonMobileMoneyAnbieter.Where(x => x.SalonId == salonId).Select(x => x.AnbieterId);
        return db.MobileMoneyAnbieter
            .Where(a => a.IstAktiv && salon.Any(s => s.MobileMoneyIndividuell
                ? explizit.Contains(a.Id)
                : (s.Land ?? StandardLand) == a.Land))
            .OrderBy(a => a.Reihenfolge).ThenBy(a => a.Name);
    }

    /// <summary>Anbieter für die Kasse des Ladens (sortiert).</summary>
    public async Task<List<MobileMoneyAnbieter>> GetErlaubteAnbieterAsync(Guid salonId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        return await ErlaubteAnbieterAbfrage(db, salonId).AsNoTracking().ToListAsync(ct);
    }

    /// <summary>Alle aktiven Anbieter des Laden-Landes mit Kennzeichnung, ob der Laden sie akzeptiert.</summary>
    public async Task<List<AnbieterAuswahl>> GetAuswahlFuerLadenAsync(Guid salonId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var land = await db.Salons.Where(s => s.Id == salonId).Select(s => s.Land).FirstOrDefaultAsync(ct);
        var erlaubt = await ErlaubteAnbieterAbfrage(db, salonId).Select(a => a.Id).ToListAsync(ct);
        var imLand = await db.MobileMoneyAnbieter.AsNoTracking()
            .Where(a => a.IstAktiv && (a.Land == (land ?? StandardLand) || erlaubt.Contains(a.Id)))
            .OrderBy(a => a.Reihenfolge).ThenBy(a => a.Name)
            .ToListAsync(ct);
        return imLand.Select(a => new AnbieterAuswahl(a, erlaubt.Contains(a.Id))).ToList();
    }

    /// <summary>Besitzer legt fest, welche Anbieter sein Laden akzeptiert (leere Liste = kein Mobile Money).</summary>
    public async Task<(bool Erfolg, string? Fehler)> SetzeAuswahlAsync(
        Guid benutzerId, Guid salonId, IReadOnlyCollection<Guid> anbieterIds, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        if (!await LadenZugriff.IstBesitzerAsync(db, benutzerId, salonId, ct))
            return (false, "Seul le propriétaire peut modifier les modes de paiement.");

        var gueltig = await db.MobileMoneyAnbieter.Where(a => anbieterIds.Contains(a.Id) && a.IstAktiv).Select(a => a.Id).ToListAsync(ct);
        var vorhanden = await db.SalonMobileMoneyAnbieter.Where(x => x.SalonId == salonId).ToListAsync(ct);
        db.SalonMobileMoneyAnbieter.RemoveRange(vorhanden.Where(x => !gueltig.Contains(x.AnbieterId)));
        db.SalonMobileMoneyAnbieter.AddRange(gueltig.Where(id => vorhanden.All(x => x.AnbieterId != id))
            .Select(id => new SalonMobileMoneyAnbieter { SalonId = salonId, AnbieterId = id }));
        var salon = await db.Salons.FirstAsync(s => s.Id == salonId, ct);
        salon.MobileMoneyIndividuell = true;
        await db.SaveChangesAsync(ct); // eine Transaktion
        return (true, null);
    }

    // --- Plattform-Stammdaten (SuperAdmin) ---

    public async Task<List<MobileMoneyAnbieter>> GetAlleAsync(CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        return await db.MobileMoneyAnbieter.AsNoTracking()
            .OrderBy(a => a.Land).ThenBy(a => a.Reihenfolge).ThenBy(a => a.Name).ToListAsync(ct);
    }

    /// <summary>Legt einen Anbieter an oder aktualisiert ihn (nur SuperAdmin).</summary>
    public async Task<(bool Erfolg, string? Fehler)> SpeichereAsync(Guid adminId, MobileMoneyAnbieter eingabe, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var istSuperAdmin = await db.UserRoles.AnyAsync(ur => ur.UserId == adminId
            && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.SuperAdmin), ct);
        if (!istSuperAdmin)
            return (false, "Action réservée au super-administrateur.");

        var name = eingabe.Name.Trim();
        var land = eingabe.Land.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
            return (false, "Veuillez saisir un nom (80 caractères max.).");
        if (!Laender.Alle.Contains(land))
            return (false, "Veuillez choisir un pays valide.");
        if (!FarbMuster().IsMatch(eingabe.Farbe))
            return (false, "La couleur doit être au format #RRGGBB.");
        if (await db.MobileMoneyAnbieter.AnyAsync(a => a.Id != eingabe.Id && a.Land == land && a.Name == name, ct))
            return (false, "Cet opérateur existe déjà pour ce pays.");

        var anbieter = await db.MobileMoneyAnbieter.FirstOrDefaultAsync(a => a.Id == eingabe.Id, ct);
        if (anbieter is null)
        {
            anbieter = new MobileMoneyAnbieter { Id = eingabe.Id == Guid.Empty ? Guid.NewGuid() : eingabe.Id };
            db.MobileMoneyAnbieter.Add(anbieter);
        }

        anbieter.Name = name;
        anbieter.Land = land;
        anbieter.Farbe = eingabe.Farbe.ToLowerInvariant();
        anbieter.Reihenfolge = eingabe.Reihenfolge;
        anbieter.IstAktiv = eingabe.IstAktiv;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>Lesbare Textfarbe (dunkel/hell) für eine Markenfarbe als Kachel-Hintergrund.</summary>
    public static string TextFarbe(string hex)
    {
        if (!FarbMuster().IsMatch(hex)) return "#ffffff";
        var r = Convert.ToInt32(hex[1..3], 16);
        var g = Convert.ToInt32(hex[3..5], 16);
        var b = Convert.ToInt32(hex[5..7], 16);
        return (0.299 * r + 0.587 * g + 0.114 * b) > 170 ? "#1b2640" : "#ffffff";
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex FarbMuster();
}
