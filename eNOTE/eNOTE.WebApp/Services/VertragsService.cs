using System.Data;
using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.Services;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

public class VertragsService(IDbContextFactory<ApplicationDbContext> factory, TimeProvider zeit)
{
    public record ZahlungEintrag(Guid Id, Guid SalonId, string Laden, string Besitzer, AboIntervall Intervall,
        DateTime Von, DateTime Bis, decimal Betrag, AboZahlungStatus Status,
        DateTime Faellig, DateTime SperreAb, decimal Rabatt, bool Individuell, DateTime? GemeldetAm);

    private static async Task<bool> HatRolleAsync(ApplicationDbContext db, Guid id, string rolle, CancellationToken ct)
        => await db.Users.AnyAsync(u => u.Id == id && !u.IsBlocked && db.UserRoles.Any(ur => ur.UserId == id
            && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == rolle)), ct);

    private static async Task PruefeBesitzerAsync(ApplicationDbContext db, Guid user, Guid salon, CancellationToken ct)
    {
        if (!await HatRolleAsync(db, user, AppRollen.Besitzer, ct)
            || !await db.UserSalons.AnyAsync(us => us.AppUserId == user && us.SalonId == salon && us.Salon.IstAktiv, ct))
            throw new UnauthorizedAccessException("Vous ne pouvez pas modifier ce contrat.");
    }

    public async Task<string?> GetZahlungsempfaengerAsync(Guid user, Guid salonId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeBesitzerAsync(db, user, salonId, ct);
        var betreuung = await db.BesitzerBetreuungen.AsNoTracking()
            .SingleOrDefaultAsync(b => b.BesitzerId == user && b.GueltigBisUtc == null, ct);
        if (betreuung is null) return null;
        var name = await db.Users.Where(u => u.Id == betreuung.ProduktOwnerId).Select(u => u.FullName).SingleAsync(ct);
        return string.IsNullOrWhiteSpace(name) ? "Responsable désigné" : name;
    }

    public static async Task<decimal> PreisAsync(ApplicationDbContext db, Guid salonId, AboIntervall intervall, CancellationToken ct)
    {
        _ = VertragsRegeln.Monate(intervall);
        var individuell = await db.Salons.Where(s => s.Id == salonId).Select(s => s.IndividuellerMonatsPreis).SingleAsync(ct);
        if (individuell is decimal p) return VertragsRegeln.Preis(p, intervall);
        var preis = await db.PlattformPreise.Where(p => p.Intervall == AboIntervall.Monatlich && p.IstAktuell)
            .OrderByDescending(p => p.GueltigAbUtc).Select(p => (decimal?)p.Preis).FirstOrDefaultAsync(ct);
        return preis is > 0 ? VertragsRegeln.Preis(preis.Value, intervall) : throw new InvalidOperationException("Le prix mensuel doit être configuré par l'administrateur.");
    }

    public async Task SynchronisiereAsync(Guid salonId, CancellationToken ct = default)
    {
        await using var vorbereitung = await factory.CreateDbContextAsync(ct);
        var strategie = vorbereitung.Database.CreateExecutionStrategy();
        await strategie.ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var transaktion = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var salon = await db.Salons.SingleOrDefaultAsync(s => s.Id == salonId, ct);
            var jetzt = zeit.GetUtcNow().UtcDateTime;
            if (salon is null || !salon.IstAktiv || !salon.IstFreigegeben || salon.ProbeBisUtc > jetzt) return;
            var abos = await db.NutzungsAbos.Where(a => a.SalonId == salonId).OrderBy(a => a.ZeitraumVonUtc).ToListAsync(ct);
            if (salon.VertragsBeginnUtc is null)
            {
                salon.VertragsBeginnUtc = salon.ProbeBisUtc ?? jetzt;
                var letztes = abos.Where(a => a.ZeitraumBisUtc > a.ZeitraumVonUtc).MaxBy(a => a.ZeitraumBisUtc);
                if (letztes is not null)
                {
                    salon.VertragsIntervall = letztes.Intervall;
                    if (letztes.ZeitraumBisUtc > salon.VertragsBeginnUtc) salon.VertragsBeginnUtc = letztes.ZeitraumBisUtc;
                }
            }
            var start = salon.VertragsBeginnUtc.Value;
            var letztePeriode = abos.Where(a => a.ZeitraumBisUtc > a.ZeitraumVonUtc).MaxBy(a => a.ZeitraumBisUtc);
            if (letztePeriode is not null && letztePeriode.ZeitraumBisUtc > start) start = letztePeriode.ZeitraumBisUtc;
            while (start <= jetzt)
            {
                if (salon.NaechstesVertragsIntervall is AboIntervall wechsel && salon.VertragsWechselAbUtc <= start)
                {
                    salon.VertragsIntervall = wechsel;
                    salon.NaechstesVertragsIntervall = null;
                    salon.VertragsWechselAbUtc = null;
                }
                var teil = VertragsRegeln.IstTeilperiode(start);
                var intervall = teil ? AboIntervall.Monatlich : salon.VertragsIntervall;
                var ende = VertragsRegeln.PeriodenEnde(start, intervall);
                var basisStand = await MonatsBasisAsync(db, salonId, start, ct);
                db.NutzungsAbos.Add(new NutzungsAbo
                {
                    SalonId = salonId, Intervall = intervall, ZeitraumVonUtc = start, ZeitraumBisUtc = ende,
                    PreisSnapshot = teil ? VertragsRegeln.Teilpreis(basisStand.Preis, start) : VertragsRegeln.Preis(basisStand.Preis, intervall),
                    MonatsBasisSnapshot = basisStand.Preis, IndividuellerPreisSnapshot = basisStand.Individuell,
                    RabattProzentSnapshot = teil ? 0 : VertragsRegeln.Rabatt(intervall), IstTeilperiode = teil,
                    FaelligUtc = start, KassenSperreAbUtc = VertragsRegeln.SperreAb(start),
                    ZahlungStatus = AboZahlungStatus.Offen, AutomatischErstellt = true, CreatedAtUtc = jetzt
                });
                start = ende;
            }

            await db.SaveChangesAsync(ct);
            if (transaktion is not null) await transaktion.CommitAsync(ct);
        });
    }

    private static async Task<(decimal Preis, bool Individuell)> MonatsBasisAsync(ApplicationDbContext db, Guid salonId, DateTime datum, CancellationToken ct)
    {
        var individuell = await db.MonatsPreisStaende.AsNoTracking().Where(p => p.SalonId == salonId && p.GueltigAbUtc <= datum)
            .OrderByDescending(p => p.GueltigAbUtc).FirstOrDefaultAsync(ct);
        if (individuell?.MonatsPreis is decimal eigener) return (eigener, true);
        var standard = await db.MonatsPreisStaende.AsNoTracking().Where(p => p.SalonId == null && p.GueltigAbUtc <= datum)
            .OrderByDescending(p => p.GueltigAbUtc).FirstOrDefaultAsync(ct);
        if (standard?.MonatsPreis is decimal global) return (global, false);
        var historischeBasis = await db.PlattformPreise.Where(p => p.Intervall == AboIntervall.Monatlich && p.GueltigAbUtc <= datum)
            .OrderByDescending(p => p.GueltigAbUtc).Select(p => (decimal?)p.Preis).FirstOrDefaultAsync(ct);
        if (historischeBasis is > 0) return (historischeBasis.Value, false);
        if (!await db.MonatsPreisStaende.AnyAsync(ct))
        {
            var laden = await db.Salons.SingleAsync(s => s.Id == salonId, ct);
            var basis = laden.IndividuellerMonatsPreis ?? await db.PlattformPreise.Where(p => p.Intervall == AboIntervall.Monatlich && p.IstAktuell)
                .OrderByDescending(p => p.GueltigAbUtc).Select(p => (decimal?)p.Preis).FirstOrDefaultAsync(ct);
            if (basis is > 0) return (basis.Value, laden.IndividuellerMonatsPreis is not null);
        }
        throw new InvalidOperationException("Le tarif historique de cette période doit être vérifié par l'administrateur.");
    }
    public async Task WechsleAsync(Guid user, Guid salonId, AboIntervall intervall, CancellationToken ct = default)
    {
        _ = VertragsRegeln.Monate(intervall);
        await using (var pruefung = await factory.CreateDbContextAsync(ct)) await PruefeBesitzerAsync(pruefung, user, salonId, ct);
        await SynchronisiereAsync(salonId, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeBesitzerAsync(db, user, salonId, ct);
        var salon = await db.Salons.SingleAsync(s => s.Id == salonId, ct);
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        var ende = await db.NutzungsAbos.Where(a => a.SalonId == salonId && a.ZeitraumBisUtc > jetzt)
            .Select(a => (DateTime?)a.ZeitraumBisUtc).MaxAsync(ct);
        salon.NaechstesVertragsIntervall = intervall;
        salon.VertragsWechselAbUtc = ende ?? salon.ProbeBisUtc ?? jetzt;
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetWechselHinweisAsync(Guid user, Guid salonId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeBesitzerAsync(db, user, salonId, ct);
        var salon = await db.Salons.AsNoTracking().SingleAsync(s => s.Id == salonId, ct);
        return salon.NaechstesVertragsIntervall is AboIntervall intervall
            ? $"Prochaine formule : {VertragsRegeln.Bezeichnung(intervall)}, à partir du {salon.VertragsWechselAbUtc:dd/MM/yyyy}."
            : null;
    }

    public async Task MeldeAsync(Guid user, Guid aboId, string? notiz, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var abo = await db.NutzungsAbos.SingleAsync(a => a.Id == aboId, ct);
        await PruefeBesitzerAsync(db, user, abo.SalonId, ct);
        if (abo.ZahlungStatus is not (AboZahlungStatus.Offen or AboZahlungStatus.Abgelehnt))
            throw new InvalidOperationException("Ce paiement est déjà signalé ou confirmé.");
        abo.ZahlungStatus = AboZahlungStatus.Gemeldet;
        abo.ZahlungGemeldetUtc = zeit.GetUtcNow().UtcDateTime;
        abo.ZahlungsNotiz = string.IsNullOrWhiteSpace(notiz) ? null : notiz.Trim();
        db.VertragsHistorien.Add(new VertragsHistorie { NutzungsAboId = abo.Id, BenutzerId = user,
            ZeitpunktUtc = zeit.GetUtcNow().UtcDateTime, Aktion = "Virement signalé", Begruendung = abo.ZahlungsNotiz });
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<ZahlungEintrag>> GetZahlungenAsync(Guid user, DateTime von, DateTime bis, CancellationToken ct = default)
    {
        if (!FinanzZeitraum.TryErstellen(von, bis, out var zeitraum, out var fehler)) throw new ArgumentException(fehler);
        await using var db = await factory.CreateDbContextAsync(ct);
        var admin = await HatRolleAsync(db, user, AppRollen.SuperAdmin, ct);
        var po = await HatRolleAsync(db, user, AppRollen.ProduktOwner, ct);
        var besitzer = await HatRolleAsync(db, user, AppRollen.Besitzer, ct);
        if (!admin && !po && !besitzer) throw new UnauthorizedAccessException();
        var ids = await db.Salons.Where(s => admin
            || (po && db.ProduktOwnerSalons.Any(p => p.ProduktOwnerId == user && p.SalonId == s.Id))
            || (besitzer && db.UserSalons.Any(us => us.AppUserId == user && us.SalonId == s.Id)))
            .Select(s => s.Id).ToListAsync(ct);
        foreach (var id in ids) await SynchronisiereAsync(id, ct);
        var abos = await db.NutzungsAbos.AsNoTracking().Where(a => ids.Contains(a.SalonId)
            && a.ZeitraumVonUtc < zeitraum!.BisExklusivUtc && a.ZeitraumBisUtc > zeitraum.VonUtc)
            .Include(a => a.Salon).OrderByDescending(a => a.ZeitraumVonUtc).ToListAsync(ct);
        var namen = await db.UserSalons.Where(us => ids.Contains(us.SalonId) && db.UserRoles.Any(ur => ur.UserId == us.AppUserId
            && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.Besitzer)))
            .Select(us => new { us.SalonId, us.AppUser.FullName }).ToListAsync(ct);
        return abos.Select(a => new ZahlungEintrag(a.Id, a.SalonId, a.Salon.Name,
            string.Join(", ", namen.Where(n => n.SalonId == a.SalonId).Select(n => n.FullName).Distinct()),
            a.Intervall, a.ZeitraumVonUtc, a.ZeitraumBisUtc, a.PreisSnapshot, a.ZahlungStatus,
            a.FaelligUtc ?? a.ZeitraumVonUtc, a.KassenSperreAbUtc ?? VertragsRegeln.SperreAb(a.FaelligUtc ?? a.ZeitraumVonUtc),
            a.RabattProzentSnapshot, a.IndividuellerPreisSnapshot, a.ZahlungGemeldetUtc)).ToList();
    }
}
