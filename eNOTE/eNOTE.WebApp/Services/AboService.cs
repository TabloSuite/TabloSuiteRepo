using ENote.Core.DataModels.Abo;
using ENote.Core.Services;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Abo-Preise (eine Regel für Besitzer und Admin) und Zahlungsbestätigung durch den SuperAdmin.
/// Perioden entstehen ausschließlich automatisch über <see cref="VertragsService"/>.
/// </summary>
public class AboService(IDbContextFactory<ApplicationDbContext> dbContextFactory, Mails.MailBenachrichtigungen? mails = null)
{
    /// <summary>Aktuelles (letztes) Abo eines Salons.</summary>
    public async Task<NutzungsAbo?> GetAktuellesAboAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.NutzungsAbos
            .AsNoTracking()
            .Where(a => a.SalonId == salonId)
            .OrderByDescending(a => a.ZeitraumBisUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Probe-Status eines Salons: läuft die kostenlose Probezeit noch, und bis wann?
    /// </summary>
    public async Task<(bool Laeuft, DateTime? BisUtc)> GetProbeStatusAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var probeBis = await db.Salons
            .AsNoTracking()
            .Where(s => s.Id == salonId)
            .Select(s => s.ProbeBisUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var laeuft = probeBis is not null && probeBis.Value > DateTime.UtcNow;
        return (laeuft, probeBis);
    }

    /// <summary>Preis einer Formel mit Monatsbasis und Rabatt (für die Anzeige beim Besitzer).</summary>
    public record FormelPreis(AboIntervall Intervall, decimal Preis, decimal MonatsBasis, decimal RabattProzent, bool Individuell)
    {
        public int Monate => VertragsRegeln.Monate(Intervall);
        public decimal ProMonat => Math.Round(Preis / Monate, 0, MidpointRounding.AwayFromZero);
        public decimal Ersparnis => MonatsBasis * Monate - Preis;
    }

    /// <summary>
    /// Globaler Monatspreis der Plattform: letzter gültiger Stand (Historie), sonst der aktuelle
    /// Monats-Plattformpreis. Einzige Quelle für Admin-Anzeige und Besitzer-Preise.
    /// </summary>
    public static async Task<decimal> GlobalerMonatsPreisAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var jetzt = DateTime.UtcNow;
        var stand = await db.MonatsPreisStaende.AsNoTracking()
            .Where(p => p.SalonId == null && p.GueltigAbUtc <= jetzt)
            .OrderByDescending(p => p.GueltigAbUtc)
            .Select(p => p.MonatsPreis)
            .FirstOrDefaultAsync(ct);
        return stand ?? await db.PlattformPreise.AsNoTracking()
            .Where(p => p.Intervall == AboIntervall.Monatlich && p.IstAktuell)
            .OrderByDescending(p => p.GueltigAbUtc)
            .Select(p => (decimal?)p.Preis)
            .FirstOrDefaultAsync(ct) ?? 0m;
    }

    /// <summary>Monatsbasis eines Ladens: individueller Preis hat Vorrang vor dem globalen Preis.</summary>
    public static async Task<(decimal Basis, bool Individuell)> MonatsBasisAsync(ApplicationDbContext db, Guid salonId, CancellationToken ct = default)
    {
        var individuell = await db.Salons.AsNoTracking().Where(s => s.Id == salonId)
            .Select(s => s.IndividuellerMonatsPreis).FirstOrDefaultAsync(ct);
        return individuell is decimal p and > 0 ? (p, true) : (await GlobalerMonatsPreisAsync(db, ct), false);
    }

    /// <summary>
    /// Preis einer Formel nach den Vertragsregeln: Monatsbasis × Monate − Rabatt
    /// (3 Monate −5 %, 6 Monate −7,5 %, 12 Monate −10 %). Gleiche Rechnung wie im Admin-Tarifdialog.
    /// </summary>
    public async Task<FormelPreis?> GetFormelPreisAsync(Guid salonId, AboIntervall intervall, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var (basis, individuell) = await MonatsBasisAsync(db, salonId, cancellationToken);
        return basis <= 0
            ? null
            : new FormelPreis(intervall, VertragsRegeln.Preis(basis, intervall), basis, VertragsRegeln.Rabatt(intervall), individuell);
    }

    /// <summary>Alle Formeln (Mensuel, Trimestriel, Semestriel, Annuel) eines Ladens.</summary>
    public async Task<List<FormelPreis>> GetFormelPreiseAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        var liste = new List<FormelPreis>();
        foreach (var intervall in VertragsRegeln.Intervalle)
        {
            if (await GetFormelPreisAsync(salonId, intervall, cancellationToken) is { } preis)
                liste.Add(preis);
        }
        return liste;
    }

    /// <summary>Geplanter neuer Monatspreis des Ladens (Tarifänderung durch den Admin), sonst null.</summary>
    public async Task<(DateTime AbUtc, decimal Preis)?> GetKommenderPreisAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await PreisAnpassungService.KommenderPreisAsync(db, salonId, DateTime.UtcNow, cancellationToken);
    }

    /// <summary>Effektiver Preis einer Formel (0 = kein Preis konfiguriert).</summary>
    public async Task<decimal> GetPreisAsync(Guid salonId, AboIntervall intervall, CancellationToken cancellationToken = default)
        => (await GetFormelPreisAsync(salonId, intervall, cancellationToken))?.Preis ?? 0m;

    /// <summary>SuperAdmin bestätigt oder lehnt eine gemeldete Zahlung.</summary>
    public async Task<bool> SetzeZahlungsStatusAsync(
        Guid bearbeiter, Guid aboId, AboZahlungStatus status,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await AdminKontenService.PruefeAsync(db, bearbeiter, false, cancellationToken);
        return await SetzeZahlungsStatusAsync(aboId, status, cancellationToken);
    }

    public async Task<bool> SetzeZahlungsStatusAsync(Guid aboId, AboZahlungStatus status, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var abo = await db.NutzungsAbos.FirstOrDefaultAsync(a => a.Id == aboId, cancellationToken);
        if (abo is null)
        {
            return false;
        }

        var vorher = abo.ZahlungStatus;
        abo.ZahlungStatus = status;
        if (status == AboZahlungStatus.Bestaetigt)
        {
            abo.ZahlungBestaetigtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (mails is not null && vorher != status)
            await mails.ZahlungsStatusAsync(abo.Id, status, cancellationToken);
        return true;
    }

    /// <summary>Gemeldete (zu bestätigende) Zahlungen aller Salons für den SuperAdmin.</summary>
    /// <summary>
    /// Sichtbare Abos für Facturation: SuperAdmin alle, ProduktOwner nur die seiner zugewiesenen
    /// Läden, sonst keine (serverseitig gefiltert).
    /// </summary>
    private static async Task<IQueryable<NutzungsAbo>> SichtbareAbosAsync(ApplicationDbContext db, Guid benutzerId, CancellationToken ct)
    {
        var istAdmin = await db.Users.AnyAsync(u => u.Id == benutzerId && !u.IsBlocked && db.UserRoles.Any(ur => ur.UserId == benutzerId
            && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == ENote.Core.DataModels.Identity.AppRollen.SuperAdmin)), ct);
        if (istAdmin)
            return db.NutzungsAbos.AsNoTracking();
        var istPo = await db.Users.AnyAsync(u => u.Id == benutzerId && !u.IsBlocked && db.UserRoles.Any(ur => ur.UserId == benutzerId
            && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == ENote.Core.DataModels.Identity.AppRollen.ProduktOwner)), ct);
        return istPo
            ? db.NutzungsAbos.AsNoTracking().Where(a => db.ProduktOwnerSalons.Any(p => p.ProduktOwnerId == benutzerId && p.SalonId == a.SalonId && p.GueltigBisUtc == null))
            : db.NutzungsAbos.AsNoTracking().Where(_ => false);
    }

    public async Task<List<NutzungsAbo>> GetGemeldeteZahlungenAsync(Guid benutzerId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await (await SichtbareAbosAsync(db, benutzerId, cancellationToken))
            .Include(a => a.Salon)
            .Where(a => a.ZahlungStatus == AboZahlungStatus.Gemeldet)
            .OrderBy(a => a.ZahlungGemeldetUtc)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Sichtbare Abos (neueste zuerst) für die Facturation-Übersicht.</summary>
    public async Task<List<NutzungsAbo>> GetAlleAbosAsync(Guid benutzerId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await (await SichtbareAbosAsync(db, benutzerId, cancellationToken))
            .Include(a => a.Salon)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);
    }
}
