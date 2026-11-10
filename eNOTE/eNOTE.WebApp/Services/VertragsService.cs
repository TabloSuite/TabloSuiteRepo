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

    /// <summary>Aktueller Preis einer Formel – dieselbe Quelle wie Besitzer-Seite und Admin-Tarif.</summary>
    public static async Task<decimal> PreisAsync(ApplicationDbContext db, Guid salonId, AboIntervall intervall, CancellationToken ct)
    {
        _ = VertragsRegeln.Monate(intervall);
        var (basis, _) = await AboService.MonatsBasisAsync(db, salonId, ct);
        return basis > 0 ? VertragsRegeln.Preis(basis, intervall) : throw new InvalidOperationException("Le prix mensuel doit être configuré par l'administrateur.");
    }

    /// <summary>Vertragsstand eines Ladens für die Besitzer-Seite.</summary>
    public record LadenVertrag(
        bool ProbeLaeuft, DateTime? ProbeBisUtc, AboIntervall Intervall, AboIntervall? NaechstesIntervall,
        DateTime? WechselAbUtc, string? Zahlungsempfaenger, IReadOnlyList<ZahlungEintrag> Perioden)
    {
        /// <summary>Formel, die ab der nächsten Periode gilt.</summary>
        public AboIntervall KuenftigesIntervall => NaechstesIntervall ?? Intervall;
    }

    /// <summary>
    /// Synchronisiert die Perioden und liefert den Vertragsstand (aktuelle/vorgemerkte Formel,
    /// letzte Perioden) – nur für den Besitzer des Ladens.
    /// </summary>
    public async Task<LadenVertrag> GetLadenVertragAsync(Guid user, Guid salonId, CancellationToken ct = default)
    {
        await using (var pruefung = await factory.CreateDbContextAsync(ct)) await PruefeBesitzerAsync(pruefung, user, salonId, ct);
        await SynchronisiereAsync(salonId, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var salon = await db.Salons.AsNoTracking().SingleAsync(s => s.Id == salonId, ct);
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        var perioden = await db.NutzungsAbos.AsNoTracking().Where(a => a.SalonId == salonId)
            .OrderByDescending(a => a.ZeitraumVonUtc).Take(12).ToListAsync(ct);
        var empfaenger = await GetZahlungsempfaengerAsync(user, salonId, ct);
        return new LadenVertrag(salon.ProbeBisUtc > jetzt, salon.ProbeBisUtc, salon.VertragsIntervall,
            salon.NaechstesVertragsIntervall, salon.VertragsWechselAbUtc, empfaenger,
            perioden.Select(a => new ZahlungEintrag(a.Id, a.SalonId, salon.Name, string.Empty, a.Intervall,
                a.ZeitraumVonUtc, a.ZeitraumBisUtc, a.PreisSnapshot, a.ZahlungStatus,
                a.FaelligUtc ?? a.ZeitraumVonUtc, a.KassenSperreAbUtc ?? VertragsRegeln.SperreAb(a.FaelligUtc ?? a.ZeitraumVonUtc),
                a.RabattProzentSnapshot, a.IndividuellerPreisSnapshot, a.ZahlungGemeldetUtc)).ToList());
    }

    /// <summary>Synchronisiert alle abrechenbaren Läden (Hintergrunddienst). Fehler je Laden werden gemeldet, nicht geworfen.</summary>
    public async Task<List<(Guid SalonId, Exception Fehler)>> SynchronisiereAlleAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        var ids = await db.Salons.AsNoTracking()
            .Where(s => s.IstAktiv && s.IstFreigegeben && (s.ProbeBisUtc == null || s.ProbeBisUtc <= jetzt))
            .Select(s => s.Id).ToListAsync(ct);
        var fehler = new List<(Guid, Exception)>();
        foreach (var id in ids)
        {
            try { await SynchronisiereAsync(id, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { fehler.Add((id, ex)); }
        }
        return fehler;
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
                salon.VertragsBeginnUtc = salon.ProbeBisUtc is DateTime probe ? VertragsRegeln.BeginnNachProbe(probe) : jetzt;
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
                // Regel: Sperre am 15. Tag nach Fälligkeit. Wäre diese Frist beim (verspäteten)
                // Erzeugen schon abgelaufen – z. B. erste Aktivierung lange nach Probezeitende –,
                // gibt es stattdessen 15 Tage ab heute: nie eine Sperre ohne Vorwarnung.
                var sperreAb = VertragsRegeln.SperreAb(start);
                if (sperreAb <= jetzt) sperreAb = VertragsRegeln.SperreAb(jetzt.Date);
                var preis = teil ? VertragsRegeln.Teilpreis(basisStand.Preis, start) : VertragsRegeln.Preis(basisStand.Preis, intervall);
                if (preis <= 0)
                {
                    // Nie eine Rechnung über 0 F: sie würde nie „bezahlt“ und die Kasse sperren.
                    start = ende;
                    continue;
                }

                db.NutzungsAbos.Add(new NutzungsAbo
                {
                    SalonId = salonId, Intervall = intervall, ZeitraumVonUtc = start, ZeitraumBisUtc = ende,
                    PreisSnapshot = preis,
                    MonatsBasisSnapshot = basisStand.Preis, IndividuellerPreisSnapshot = basisStand.Individuell,
                    RabattProzentSnapshot = teil ? 0 : VertragsRegeln.Rabatt(intervall), IstTeilperiode = teil,
                    FaelligUtc = start, KassenSperreAbUtc = sperreAb,
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
        // Periode beginnt vor dem ersten erfassten Preis (z. B. Probezeit endete vor der Tarif-Einrichtung):
        // frühester bekannter globaler Preis – nicht rückwirkend der heutige, und ein erst später
        // vergebener individueller Preis gilt nicht rückwirkend.
        var fruehesterStand = await db.MonatsPreisStaende.AsNoTracking().Where(p => p.SalonId == null && p.MonatsPreis != null)
            .OrderBy(p => p.GueltigAbUtc).Select(p => p.MonatsPreis).FirstOrDefaultAsync(ct);
        var fruehesterPreis = fruehesterStand ?? await db.PlattformPreise.AsNoTracking().Where(p => p.Intervall == AboIntervall.Monatlich)
            .OrderBy(p => p.GueltigAbUtc).Select(p => (decimal?)p.Preis).FirstOrDefaultAsync(ct);
        if (fruehesterPreis is > 0) return (fruehesterPreis.Value, false);

        // Gar kein globaler Preis: nur der individuelle Preis des Ladens kommt in Frage.
        var laden = await db.Salons.AsNoTracking().SingleAsync(s => s.Id == salonId, ct);
        if (laden.IndividuellerMonatsPreis is > 0) return (laden.IndividuellerMonatsPreis.Value, true);
        throw new InvalidOperationException("Le tarif mensuel doit être configuré par l'administrateur.");
    }
    public enum WechselArt
    {
        /// <summary>Die laufende, noch offene Rechnung wurde in die neue Formel umgewandelt.</summary>
        SofortUmgewandelt,

        /// <summary>Die neue Formel gilt ab der nächsten Periode (bzw. nach der Probezeit).</summary>
        AbNaechsterPeriode,

        /// <summary>Formel entspricht bereits der laufenden Periode – ein vorgemerkter Wechsel wurde aufgehoben.</summary>
        Unveraendert
    }

    /// <summary>Was ein Formelwechsel bewirken würde (für die Bestätigung beim Besitzer).</summary>
    public record WechselVorschau(WechselArt Art, decimal? NeuerBetrag, DateTime? AbUtc, DateTime? Faellig, DateTime? SperreAb);

    /// <summary>
    /// Laufende Periode, die sofort in eine andere Formel umgewandelt werden darf: volle Periode
    /// (keine anteilige Startrechnung), noch nicht bezahlt und nicht als bezahlt gemeldet
    /// (offen oder vom Admin abgelehnt), und die jüngste Periode des Ladens.
    /// </summary>
    private static async Task<NutzungsAbo?> UmwandelbarePeriodeAsync(ApplicationDbContext db, Guid salonId, AboIntervall intervall, DateTime jetzt, CancellationToken ct)
    {
        var aktuell = await db.NutzungsAbos.Where(a => a.SalonId == salonId && a.ZeitraumVonUtc <= jetzt && a.ZeitraumBisUtc > jetzt)
            .OrderByDescending(a => a.ZeitraumVonUtc).FirstOrDefaultAsync(ct);
        if (aktuell is null || aktuell.IstTeilperiode || aktuell.Intervall == intervall
            || aktuell.ZahlungStatus is not (AboZahlungStatus.Offen or AboZahlungStatus.Abgelehnt))
            return null;
        var spaetere = await db.NutzungsAbos.AnyAsync(a => a.SalonId == salonId && a.ZeitraumVonUtc > aktuell.ZeitraumVonUtc, ct);
        return spaetere ? null : aktuell;
    }

    /// <summary>Basis der umgewandelten Rechnung: Monatspreis vom Periodenbeginn (nicht ein späterer).</summary>
    private static async Task<decimal> BasisFuerPeriodeAsync(ApplicationDbContext db, NutzungsAbo periode, CancellationToken ct)
        => periode.MonatsBasisSnapshot is > 0 ? periode.MonatsBasisSnapshot.Value : (await MonatsBasisAsync(db, periode.SalonId, periode.ZeitraumVonUtc, ct)).Preis;

    public async Task<WechselVorschau> GetWechselVorschauAsync(Guid user, Guid salonId, AboIntervall intervall, CancellationToken ct = default)
    {
        _ = VertragsRegeln.Monate(intervall);
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeBesitzerAsync(db, user, salonId, ct);
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        if (await UmwandelbarePeriodeAsync(db, salonId, intervall, jetzt, ct) is { } periode)
        {
            var basis = await BasisFuerPeriodeAsync(db, periode, ct);
            return new(WechselArt.SofortUmgewandelt, VertragsRegeln.Preis(basis, intervall), periode.ZeitraumVonUtc,
                periode.FaelligUtc ?? periode.ZeitraumVonUtc, periode.KassenSperreAbUtc);
        }

        var salon = await db.Salons.AsNoTracking().SingleAsync(s => s.Id == salonId, ct);
        var ende = await db.NutzungsAbos.Where(a => a.SalonId == salonId && a.ZeitraumBisUtc > jetzt)
            .Select(a => (DateTime?)a.ZeitraumBisUtc).MaxAsync(ct);
        return new(WechselArt.AbNaechsterPeriode, await PreisAsync(db, salonId, intervall, ct),
            ende ?? (salon.ProbeBisUtc is DateTime probe ? VertragsRegeln.BeginnNachProbe(probe) : jetzt), null, null);
    }

    /// <summary>
    /// Formel wechseln. Ist die laufende Rechnung noch offen (nicht gemeldet/bezahlt) und eine volle
    /// Periode, wird sie sofort in die neue Formel umgewandelt (gleicher Start, Monatspreis vom
    /// Periodenbeginn, Zahlungsfrist unverändert). Sonst gilt die Formel ab der nächsten Periode.
    /// </summary>
    public async Task<WechselArt> WechsleAsync(Guid user, Guid salonId, AboIntervall intervall, CancellationToken ct = default)
    {
        _ = VertragsRegeln.Monate(intervall);
        await using (var pruefung = await factory.CreateDbContextAsync(ct)) await PruefeBesitzerAsync(pruefung, user, salonId, ct);
        await SynchronisiereAsync(salonId, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeBesitzerAsync(db, user, salonId, ct);
        var salon = await db.Salons.SingleAsync(s => s.Id == salonId, ct);
        var jetzt = zeit.GetUtcNow().UtcDateTime;

        if (await UmwandelbarePeriodeAsync(db, salonId, intervall, jetzt, ct) is { } periode)
        {
            var basis = await BasisFuerPeriodeAsync(db, periode, ct);
            var vorher = periode.Intervall;
            var betragVorher = periode.PreisSnapshot;
            periode.Intervall = intervall;
            periode.ZeitraumBisUtc = periode.ZeitraumVonUtc.AddMonths(VertragsRegeln.Monate(intervall));
            periode.MonatsBasisSnapshot = basis;
            periode.RabattProzentSnapshot = VertragsRegeln.Rabatt(intervall);
            periode.PreisSnapshot = VertragsRegeln.Preis(basis, intervall);
            // Fälligkeit und Sperrfrist bleiben bewusst unverändert (kein Aufschub durch Hin- und Herwechseln).
            salon.VertragsIntervall = intervall;
            salon.NaechstesVertragsIntervall = null;
            salon.VertragsWechselAbUtc = null;
            db.VertragsHistorien.Add(new VertragsHistorie
            {
                NutzungsAboId = periode.Id, BenutzerId = user, ZeitpunktUtc = jetzt, Aktion = "Formule convertie",
                Begruendung = $"{VertragsRegeln.Bezeichnung(vorher)} ({betragVorher:N0} F) → {VertragsRegeln.Bezeichnung(intervall)} ({periode.PreisSnapshot:N0} F)"
            });
            await db.SaveChangesAsync(ct);
            return WechselArt.SofortUmgewandelt;
        }

        var laufend = await db.NutzungsAbos.Where(a => a.SalonId == salonId && a.ZeitraumVonUtc <= jetzt && a.ZeitraumBisUtc > jetzt)
            .OrderByDescending(a => a.ZeitraumVonUtc).Select(a => (AboIntervall?)a.Intervall).FirstOrDefaultAsync(ct);
        var ende = await db.NutzungsAbos.Where(a => a.SalonId == salonId && a.ZeitraumBisUtc > jetzt)
            .Select(a => (DateTime?)a.ZeitraumBisUtc).MaxAsync(ct);
        salon.NaechstesVertragsIntervall = intervall;
        salon.VertragsWechselAbUtc = ende ?? (salon.ProbeBisUtc is DateTime probe ? VertragsRegeln.BeginnNachProbe(probe) : jetzt);
        await db.SaveChangesAsync(ct);
        return laufend == intervall ? WechselArt.Unveraendert : WechselArt.AbNaechsterPeriode;
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
