using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

public class BesitzerFinanzService(IDbContextFactory<ApplicationDbContext> factory)
{
    public record BesitzerEintrag(Guid Id, string Name, string? Email);
    public record LadenEintrag(Guid Id, string Name, string Vertragsart);
    public record MonatsFinanzen(DateTime Monat, int Verkaeufe, decimal Bar, decimal Karte, decimal MobileMoney, decimal Ausgaben)
    {
        public decimal Umsatz => Bar + Karte + MobileMoney;
        public decimal Ergebnis => Umsatz - Ausgaben;
    }
    public record Bericht(string LadenName, string Vertragsart, DateTime Von, DateTime Bis,
        List<MonatsFinanzen> Monate, List<NutzungsAbo> Abos, decimal Bestaetigt, decimal Gemeldet, decimal Offen);

    private static async Task PruefeAdminAsync(ApplicationDbContext db, Guid id, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id && !u.IsBlocked
            && db.UserRoles.Any(ur => ur.UserId == u.Id
                && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.SuperAdmin)), ct))
            throw new UnauthorizedAccessException("Vous ne pouvez pas consulter cette vue financière.");
    }

    private static IQueryable<ENote.Core.DataModels.Salons.UserSalon> BesitzerZuordnungen(ApplicationDbContext db)
        => db.UserSalons.AsNoTracking().Where(us => db.UserRoles.Any(ur => ur.UserId == us.AppUserId
            && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.Besitzer)));

    public async Task<List<BesitzerEintrag>> GetBesitzerAsync(Guid adminId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeAdminAsync(db, adminId, ct);
        return await db.Users.AsNoTracking().Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id
                && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.Besitzer)))
            .OrderBy(u => u.FullName).Select(u => new BesitzerEintrag(u.Id, u.FullName, u.Email)).ToListAsync(ct);
    }

    public static string VertragsText(IEnumerable<NutzungsAbo> abos, DateTime? probeBis, DateTime jetzt)
    {
        if (probeBis > jetzt) return "Essai gratuit";
        var aktiv = abos.Where(a => a.ZeitraumVonUtc <= jetzt && a.ZeitraumBisUtc > jetzt)
            .OrderByDescending(a => a.ZeitraumVonUtc).FirstOrDefault();
        return "Contrat actif · " + IntervallText(aktiv?.Intervall ?? AboIntervall.Monatlich);
    }

    public static string IntervallText(AboIntervall intervall) => intervall switch
    {
        AboIntervall.Monatlich => "Mensuel",
        AboIntervall.DreiMonate => "Trimestriel",
        AboIntervall.SechsMonate => "Semestriel",
        AboIntervall.Jaehrlich => "Annuel",
        _ => "Non défini"
    };

    public async Task<List<LadenEintrag>> GetLaedenAsync(Guid adminId, Guid besitzerId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeAdminAsync(db, adminId, ct);
        var laeden = await BesitzerZuordnungen(db).Where(us => us.AppUserId == besitzerId)
            .Select(us => us.Salon).Distinct().OrderBy(s => s.Name).ToListAsync(ct);
        var ids = laeden.Select(s => s.Id).ToList();
        var abos = await db.NutzungsAbos.AsNoTracking().Where(a => ids.Contains(a.SalonId)).ToListAsync(ct);
        var jetzt = DateTime.UtcNow;
        return laeden.Select(s => new LadenEintrag(s.Id, s.Name, VertragsText(abos.Where(a => a.SalonId == s.Id), s.ProbeBisUtc, jetzt))).ToList();
    }

    public async Task<Bericht> GetBerichtAsync(Guid adminId, Guid besitzerId, Guid salonId,
        DateTime von, DateTime bis, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeAdminAsync(db, adminId, ct);
        if (!FinanzZeitraum.TryErstellen(von, bis, out var zeitraum, out var fehler)) throw new ArgumentException(fehler);
        var salon = await BesitzerZuordnungen(db).Where(us => us.AppUserId == besitzerId && us.SalonId == salonId)
            .Select(us => us.Salon).FirstOrDefaultAsync(ct)
            ?? throw new UnauthorizedAccessException("Cet établissement n'appartient pas au propriétaire sélectionné.");
        var start = zeitraum!.VonUtc;
        var ende = zeitraum.BisExklusivUtc;
        var verkaeufe = (await db.Verkaeufe.AsNoTracking().Where(v => v.SalonId == salonId && v.ZeitUtc >= start && v.ZeitUtc < ende)
            .Select(v => new { v.ZeitUtc, v.ZahlungsArt, v.GesamtSumme, Zahlungen = v.Zahlungen.Select(z => new { z.Art, z.Betrag }).ToList() })
            .ToListAsync(ct))
            .Select(v => new
            {
                v.ZeitUtc,
                Anteile = ENote.Core.Services.Kasse.KassenRechner.Anteile(v.ZahlungsArt, v.GesamtSumme,
                    v.Zahlungen.Select(z => new ENote.Core.Services.Kasse.ZahlungsAnteil(z.Art, z.Betrag)))
            }).ToList();
        decimal Summe(DateTime monat, ZahlungsArt art) => verkaeufe.Where(v => Monat(v.ZeitUtc) == monat)
            .SelectMany(v => v.Anteile).Where(a => a.Art == art).Sum(a => a.Betrag);
        var ausgaben = await db.Ausgaben.AsNoTracking().Where(a => a.SalonId == salonId && a.DatumUtc >= start && a.DatumUtc < ende)
            .Select(a => new { a.DatumUtc, a.Betrag }).ToListAsync(ct);
        var alleAbos = await db.NutzungsAbos.AsNoTracking().Where(a => a.SalonId == salonId).ToListAsync(ct);
        var abos = alleAbos.Where(a =>
        {
            var datum = a.ZahlungStatus == AboZahlungStatus.Bestaetigt ? a.ZahlungBestaetigtUtc : a.ZahlungGemeldetUtc ?? a.CreatedAtUtc;
            return datum >= start && datum < ende;
        }).OrderByDescending(a => a.ZahlungBestaetigtUtc ?? a.ZahlungGemeldetUtc ?? a.CreatedAtUtc).ToList();
        static DateTime Monat(DateTime d) => new(d.Year, d.Month, 1);
        var monate = verkaeufe.Select(v => Monat(v.ZeitUtc)).Concat(ausgaben.Select(a => Monat(a.DatumUtc))).Distinct().OrderBy(d => d)
            .Select(m => new MonatsFinanzen(m, verkaeufe.Count(v => Monat(v.ZeitUtc) == m),
                Summe(m, ZahlungsArt.Bar), Summe(m, ZahlungsArt.Karte), Summe(m, ZahlungsArt.MobileMoney),
                ausgaben.Where(a => Monat(a.DatumUtc) == m).Sum(a => a.Betrag))).ToList();
        return new Bericht(salon.Name, VertragsText(alleAbos, salon.ProbeBisUtc, DateTime.UtcNow), von.Date, bis.Date, monate, abos,
            abos.Where(a => a.ZahlungStatus == AboZahlungStatus.Bestaetigt).Sum(a => a.PreisSnapshot),
            abos.Where(a => a.ZahlungStatus == AboZahlungStatus.Gemeldet).Sum(a => a.PreisSnapshot),
            abos.Where(a => a.ZahlungStatus == AboZahlungStatus.Offen).Sum(a => a.PreisSnapshot));
    }
}
