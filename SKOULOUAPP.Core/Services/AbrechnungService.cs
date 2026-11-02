using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.Interfaces;
using SkoulouApp.Core.Models.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace SkoulouApp.Core.Services;

/// <summary>
/// Verwaltet die periodenbasierte Abrechnung zwischen Schule und Betreiber.
///
/// Ablauf:
/// - Der SchulLeiter markiert Schüler-Niveaus als bezahlt (siehe <see cref="AboService"/>).
/// - Solange eine Zahlung keiner abgeschlossenen Periode zugeordnet ist, zählt sie
///   zum "offenen Umsatz".
/// - Der SuperAdmin schließt eine Periode ab, sobald das Geld überwiesen wurde.
///   Dabei werden alle offenen, bezahlten Positionen der aktuellen Periode zugeordnet.
/// - Der nächste offene Zeitraum beginnt automatisch am Ende der zuletzt
///   abgeschlossenen Periode.
/// </summary>
public class AbrechnungService(ISkoulouAppDbContext dbContext)
{
    /// <summary>Schulbezogene Übersicht ohne Zahlungsfiktion und ohne provisionabhängige Teilnehmerfilter.</summary>
    public async Task<ZyklusVerpflichtungsUebersicht> GetZyklusVerpflichtungenAsync(
        Guid schulID, Guid benutzerID, CancellationToken token = default)
    {
        if (!await new FinanzService(dbContext).CanAccessBillingAsync(benutzerID, schulID, token))
            throw new UnauthorizedAccessException("Vous n'êtes pas autorisé à consulter la facturation de cette école.");

        var laufend = await dbContext.KursNiveauDurchlaeufe.AsNoTracking()
            .Where(x => x.SchulID == schulID && x.EndDatum == null && x.PlattformPreisProSchueler != null)
            .OrderBy(x => x.Klasse.Code).ThenBy(x => x.StartDatum)
            .Select(x => new LaufendeZyklusVerpflichtung(x.Id, x.Klasse.Code, x.StartDatum,
                x.PlattformPreisProSchueler!.Value,
                dbContext.LevelEnrollments.Count(e => e.DurchlaufId == x.Id && e.StorniertAmUtc == null),
                dbContext.LevelEnrollments.Where(e => e.DurchlaufId == x.Id && e.StorniertAmUtc == null)
                    .Sum(e => (decimal?)e.PriceAtEnrollment) ?? 0m))
            .ToListAsync(token);
        var rechnungen = await dbContext.Abrechnungsperioden.AsNoTracking()
            .Where(x => x.SchulID == schulID && x.Status != AbrechnungStatus.Bestaetigt)
            .Select(x => new { x.Umsatz, x.Status }).ToListAsync(token);
        return new ZyklusVerpflichtungsUebersicht
        {
            LaufendeZyklen = laufend,
            OffeneRechnungenAnzahl = rechnungen.Count,
            OffeneRechnungenBetrag = rechnungen.Sum(x => x.Umsatz),
            ZahlungGemeldetBetrag = rechnungen.Where(x => x.Status == AbrechnungStatus.Akzeptiert).Sum(x => x.Umsatz)
        };
    }

    private static decimal BerechnePlattformforderung(DataModels.Courses.LevelEnrollment position)
        => Math.Max(0m, position.PriceAtEnrollment
            - position.SchoolLeaderCommissionAtEnrollment
            - position.TeacherCommissionAtEnrollment);

    /// <summary>
    /// Ermittelt den Beginn des aktuell offenen Abrechnungszeitraums einer Schule:
    /// das Ende der zuletzt erstellten Periode oder – falls es keine gibt –
    /// den Erstellungszeitpunkt der Schule.
    /// </summary>
    private async Task<DateTime> ErmittleZeitraumBeginnAsync(School schule, CancellationToken cancellationToken)
    {
        var letzte = await dbContext.Abrechnungsperioden
            .Where(x => x.SchulID == schule.Id && !x.IstSammelabrechnungAusGeschlossenenKlassen)
            .OrderByDescending(x => x.BisUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return letzte?.BisUtc ?? schule.ErstelltAmUtc;
    }

    /// <summary>
    /// Liefert die offene Abrechnungsübersicht einer Schule (bezahlte, noch nicht
    /// abgerechnete SKOULOUAPP-Abos im aktuellen Zeitraum). Optional kann das Ende des
    /// betrachteten Zeitraums frei festgelegt werden (Beginn ist immer die letzte Facturation).
    /// </summary>
    public async Task<AbrechnungsUebersicht> GetOffeneUebersichtAsync(Guid schulId, DateTime? bisUtcFilter = null, CancellationToken cancellationToken = default)
    {
        var schule = await dbContext.Schools.FirstOrDefaultAsync(x => x.Id == schulId, cancellationToken)
            ?? throw new InvalidOperationException("L'école est introuvable.");

        var vonUtc = await ErmittleZeitraumBeginnAsync(schule, cancellationToken);
        var jetzt = DateTime.UtcNow;
        var bisUtc = bisUtcFilter is { } filter && filter > vonUtc ? filter : jetzt;

        var positionen = await OffenePositionenAsync(schulId, vonUtc, bisUtc, cancellationToken);

        return new AbrechnungsUebersicht
        {
            SchulID = schulId,
            SchulName = schule.Name,
            VonUtc = vonUtc,
            BisUtc = bisUtc,
            AnzahlAbonnements = positionen.Count,
            OffenerUmsatz = positionen.Sum(BerechnePlattformforderung)
        };
    }

    /// <summary>
    /// Liefert die noch nicht fakturierten Plattformforderungen aus abgeschlossenen
    /// Klassenzyklen. Jede Abo-Freischaltung wird höchstens einem Zyklus zugeordnet.
    /// </summary>
    public async Task<List<GeschlosseneKlasseOffeneAboForderung>> GetOffeneForderungenAusGeschlossenenKlassenAsync(
        CancellationToken cancellationToken = default)
    {
        var positionen = await (
            from enrollment in dbContext.LevelEnrollments.AsNoTracking()
            join profil in dbContext.StudentProfiles.AsNoTracking() on enrollment.StudentId equals profil.AppUserId
            join durchlauf in dbContext.KursNiveauDurchlaeufe.AsNoTracking()
                on new { SchulID = profil.SchoolId, enrollment.KlasseId } equals new { durchlauf.SchulID, durchlauf.KlasseId }
            join klasse in dbContext.Klassen.AsNoTracking() on enrollment.KlasseId equals klasse.Id
            join schule in dbContext.Schools.AsNoTracking() on profil.SchoolId equals schule.Id
            where enrollment.IsPaid
                && enrollment.PriceAtEnrollment > 0m
                && (enrollment.SchoolLeaderCommissionAtEnrollment > 0m || enrollment.TeacherCommissionAtEnrollment > 0m)
                && enrollment.AbgerechnetInPeriodeId == null
                && enrollment.PaidAtUtc != null
                && durchlauf.EndDatum != null
                && (enrollment.DurchlaufId == durchlauf.Id
                    || enrollment.DurchlaufId == null && enrollment.PaidAtUtc >= durchlauf.StartDatum && enrollment.PaidAtUtc <= durchlauf.EndDatum)
            select new
            {
                EnrollmentId = enrollment.Id,
                SchulID = schule.Id,
                SchulName = schule.Name,
                KlasseId = klasse.Id,
                KlasseCode = klasse.Code,
                KlasseTitel = klasse.Title,
                ZyklusEnde = durchlauf.EndDatum!.Value,
                Plattformforderung = Math.Max(0m, enrollment.PriceAtEnrollment
                    - enrollment.SchoolLeaderCommissionAtEnrollment
                    - enrollment.TeacherCommissionAtEnrollment)
            })
            .ToListAsync(cancellationToken);

        return positionen
            .GroupBy(position => position.EnrollmentId)
            .Select(gruppe => gruppe.OrderByDescending(position => position.ZyklusEnde).First())
            .GroupBy(position => new
            {
                position.SchulID,
                position.SchulName,
                position.KlasseId,
                position.KlasseCode,
                position.KlasseTitel,
                position.ZyklusEnde
            })
            .Select(gruppe => new GeschlosseneKlasseOffeneAboForderung
            {
                SchulID = gruppe.Key.SchulID,
                SchulName = gruppe.Key.SchulName,
                KlasseId = gruppe.Key.KlasseId,
                KlasseCode = gruppe.Key.KlasseCode,
                KlasseTitel = gruppe.Key.KlasseTitel,
                ZyklusEnde = gruppe.Key.ZyklusEnde,
                AnzahlAbonnements = gruppe.Count(),
                Plattformforderung = gruppe.Sum(position => position.Plattformforderung)
            })
            .OrderByDescending(forderung => forderung.ZyklusEnde)
            .ThenBy(forderung => forderung.SchulName)
            .ThenBy(forderung => forderung.KlasseCode)
            .ToList();
    }

    private async Task<(bool Succeeded, string ErrorMessage)> StatusTransaktionAsync(Guid periodeID,
        Func<Task<(bool Succeeded, string ErrorMessage)>> aktion, CancellationToken token)
    {
        if (dbContext.Database.CurrentTransaction is not null) return await aktion();
        var versuch = 0;
        return await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            ZyklusTransaktionsSchutz.WiederholungVorbereiten(dbContext, versuch++);
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token) : null;
            await ZyklusTransaktionsSchutz.SperrenAsync(dbContext, token, $"SKOULOUAPP:Facturation:{periodeID:N}");
            if (dbContext.Database.IsRelational())
            {
                var context = dbContext.Database.GetService<Microsoft.EntityFrameworkCore.Infrastructure.ICurrentDbContext>().Context;
                foreach (var entry in context.ChangeTracker.Entries<Abrechnungsperiode>().Where(x => x.Entity.Id == periodeID).ToList())
                    entry.State = EntityState.Detached;
            }
            var result = await aktion();
            if (result.Succeeded && transaction is not null) await transaction.CommitAsync(token);
            return result;
        });
    }

    /// <summary>
    /// Liefert noch nicht fakturierte SKOULOUAPP-Abos, deren Zahlung mindestens zehn
    /// Monate zurückliegt und deren Klassenzyklus ebenso lange aktiv ist.
    /// </summary>
    public async Task<List<AktiveKlasseUeberfaelligeAboForderung>> GetUeberfaelligeForderungenAusAktivenKlassenAsync(
        DateTime? jetztUtc = null,
        CancellationToken cancellationToken = default)
    {
        var referenzZeitpunktUtc = jetztUtc ?? DateTime.UtcNow;
        var stichtagUtc = referenzZeitpunktUtc.AddMonths(-10);
        var positionen = await (
            from enrollment in dbContext.LevelEnrollments.AsNoTracking()
            join profil in dbContext.StudentProfiles.AsNoTracking() on enrollment.StudentId equals profil.AppUserId
            join durchlauf in dbContext.KursNiveauDurchlaeufe.AsNoTracking()
                on new { SchulID = profil.SchoolId, enrollment.KlasseId } equals new { durchlauf.SchulID, durchlauf.KlasseId }
            join klasse in dbContext.Klassen.AsNoTracking() on enrollment.KlasseId equals klasse.Id
            join schule in dbContext.Schools.AsNoTracking() on profil.SchoolId equals schule.Id
            where enrollment.IsPaid
                && enrollment.PriceAtEnrollment > 0m
                && (enrollment.SchoolLeaderCommissionAtEnrollment > 0m || enrollment.TeacherCommissionAtEnrollment > 0m)
                && enrollment.AbgerechnetInPeriodeId == null
                && enrollment.PaidAtUtc != null
                && enrollment.PaidAtUtc <= stichtagUtc
                && durchlauf.EndDatum == null
                && durchlauf.StartDatum <= stichtagUtc
                && (enrollment.DurchlaufId == durchlauf.Id
                    || enrollment.DurchlaufId == null && enrollment.PaidAtUtc >= durchlauf.StartDatum)
            select new
            {
                EnrollmentId = enrollment.Id,
                SchulID = schule.Id,
                SchulName = schule.Name,
                KlasseId = klasse.Id,
                KlasseCode = klasse.Code,
                KlasseTitel = klasse.Title,
                PaidAtUtc = enrollment.PaidAtUtc!.Value,
                Plattformforderung = Math.Max(0m, enrollment.PriceAtEnrollment
                    - enrollment.SchoolLeaderCommissionAtEnrollment
                    - enrollment.TeacherCommissionAtEnrollment)
            })
            .ToListAsync(cancellationToken);

        return positionen
            .GroupBy(position => position.EnrollmentId)
            .Select(gruppe => gruppe.First())
            .GroupBy(position => new
            {
                position.SchulID,
                position.SchulName,
                position.KlasseId,
                position.KlasseCode,
                position.KlasseTitel
            })
            .Select(gruppe => new AktiveKlasseUeberfaelligeAboForderung
            {
                SchulID = gruppe.Key.SchulID,
                SchulName = gruppe.Key.SchulName,
                KlasseId = gruppe.Key.KlasseId,
                KlasseCode = gruppe.Key.KlasseCode,
                KlasseTitel = gruppe.Key.KlasseTitel,
                AeltesteZahlungAmUtc = gruppe.Min(position => position.PaidAtUtc),
                TageOffen = (int)(referenzZeitpunktUtc - gruppe.Min(position => position.PaidAtUtc)).TotalDays,
                AnzahlAbonnements = gruppe.Count(),
                Plattformforderung = gruppe.Sum(position => position.Plattformforderung)
            })
            .OrderByDescending(forderung => forderung.TageOffen)
            .ThenBy(forderung => forderung.SchulName)
            .ThenBy(forderung => forderung.KlasseCode)
            .ToList();
    }

    /// <summary>
    /// Erstellt je Schule eine Sammelabrechnung ausschließlich für noch offene Abos,
    /// deren Zahlung innerhalb eines abgeschlossenen Klassenzyklus erfolgt ist.
    /// </summary>
    public async Task<(bool Succeeded, string ErrorMessage)> SammelabrechnungenAusGeschlossenenKlassenErstellenAsync(
        CancellationToken cancellationToken = default)
    {
        var positionen = await GetOffenePositionenAusGeschlossenenKlassenAsync(cancellationToken);
        if (positionen.Count == 0)
        {
            return (false, "Il n'existe aucun abonnement SKOULOUAPP ouvert issu de cycles de classes clôturés.");
        }

        var anzahlAbrechnungen = 0;
        foreach (var gruppe in positionen.GroupBy(position => position.SchulID))
        {
            var abrechnungsPositionen = gruppe.Select(position => position.Enrollment).ToList();
            var periode = new Abrechnungsperiode
            {
                SchulID = gruppe.Key,
                VonUtc = abrechnungsPositionen.Min(position => position.PaidAtUtc!.Value),
                BisUtc = DateTime.UtcNow,
                AnzahlAbonnements = abrechnungsPositionen.Count,
                Umsatz = abrechnungsPositionen.Sum(BerechnePlattformforderung),
                Status = AbrechnungStatus.Erstellt,
                IstAbgeschlossen = false,
                IstSammelabrechnungAusGeschlossenenKlassen = true
            };
            dbContext.Abrechnungsperioden.Add(periode);

            foreach (var position in abrechnungsPositionen)
            {
                position.AbgerechnetInPeriodeId = periode.Id;
                position.GeaendertAmUtc = DateTime.UtcNow;
            }

            anzahlAbrechnungen++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, $"{anzahlAbrechnungen} facturation(s) groupée(s) pour des cycles de classes clôturés a/ont été créée(s).");
    }

    private async Task<List<(Guid SchulID, DataModels.Courses.LevelEnrollment Enrollment)>> GetOffenePositionenAusGeschlossenenKlassenAsync(
        CancellationToken cancellationToken)
    {
        var zuordnungen = await (
            from enrollment in dbContext.LevelEnrollments
            join profil in dbContext.StudentProfiles on enrollment.StudentId equals profil.AppUserId
            join durchlauf in dbContext.KursNiveauDurchlaeufe
                on new { SchulID = profil.SchoolId, enrollment.KlasseId } equals new { durchlauf.SchulID, durchlauf.KlasseId }
            where enrollment.IsPaid
                && enrollment.PriceAtEnrollment > 0m
                && (enrollment.SchoolLeaderCommissionAtEnrollment > 0m || enrollment.TeacherCommissionAtEnrollment > 0m)
                && enrollment.AbgerechnetInPeriodeId == null
                && enrollment.PaidAtUtc != null
                && durchlauf.EndDatum != null
                && (enrollment.DurchlaufId == durchlauf.Id
                    || enrollment.DurchlaufId == null && enrollment.PaidAtUtc >= durchlauf.StartDatum && enrollment.PaidAtUtc <= durchlauf.EndDatum)
            select new { profil.SchoolId, Enrollment = enrollment, ZyklusEnde = durchlauf.EndDatum.GetValueOrDefault() })
            .ToListAsync(cancellationToken);

        return zuordnungen
            .GroupBy(position => position.Enrollment.Id)
            .Select(gruppe => gruppe.OrderByDescending(position => position.ZyklusEnde).First())
            .Select(position => (position.SchoolId, position.Enrollment))
            .ToList();
    }

    /// <summary>
    /// Ermittelt die bezahlten, noch nicht abgerechneten SKOULOUAPP-Abo-Freischaltungen einer Schule
    /// im angegebenen Zeitraum (nach PaidAtUtc).
    /// </summary>
    private async Task<List<DataModels.Courses.LevelEnrollment>> OffenePositionenAsync(
        Guid schulId, DateTime vonUtc, DateTime bisUtc, CancellationToken cancellationToken)
    {
        // Schüler der Schule bestimmen.
        var studentIds = await dbContext.StudentProfiles
            .Where(x => x.SchoolId == schulId)
            .Select(x => x.AppUserId)
            .ToListAsync(cancellationToken);

        return await dbContext.LevelEnrollments
            .Where(x => studentIds.Contains(x.StudentId)
                && x.IsPaid
                && (x.DurchlaufId == null || x.Durchlauf!.PlattformPreisProSchueler == null)
                && x.PriceAtEnrollment > 0m
                && (x.SchoolLeaderCommissionAtEnrollment > 0m || x.TeacherCommissionAtEnrollment > 0m)
                && x.AbgerechnetInPeriodeId == null
                && x.PaidAtUtc != null
                && x.PaidAtUtc >= vonUtc
                && x.PaidAtUtc <= bisUtc)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Erstellt eine Abrechnung für den aktuell offenen Zeitraum einer Schule.
    /// Der SuperAdmin oder ein für die Schule zuständiger Product Owner löst dies aus.
    /// der neuen Periode zugeordnet und der Umsatz festgeschrieben. Die Abrechnung
    /// erhält den Status <see cref="AbrechnungStatus.Erstellt"/> und wartet auf die
    /// Reaktion des SchulLeiters.
    /// </summary>
    public async Task<(bool Succeeded, string ErrorMessage)> AbrechnungErstellenAsync(
        Guid schulId,
        DateTime? bisUtc = null,
        CancellationToken cancellationToken = default,
        Guid? erstellerId = null)
    {
        var result = await ManuelleAbrechnungErstellenAsync(schulId, bisUtc, cancellationToken, erstellerId);
        return (result.Succeeded, result.ErrorMessage);
    }

    public async Task<(bool Succeeded, string ErrorMessage, Guid? PeriodeId)> ManuelleAbrechnungErstellenAsync(
        Guid schulId,
        DateTime? bisUtc = null,
        CancellationToken cancellationToken = default,
        Guid? erstellerId = null)
    {
        if (erstellerId is Guid benutzerId
            && !await DarfAbrechnungErstellenAsync(benutzerId, schulId, cancellationToken))
        {
            return (false, "Vous n'êtes pas autorisé à créer une facturation pour cette école.", null);
        }

        return await AbrechnungErstellenCoreAsync(
            schulId,
            bisUtc,
            automatischDurchKlassenabschluss: false,
            erstellerId,
            cancellationToken);
    }

    private async Task<(bool Succeeded, string ErrorMessage, Guid? PeriodeId)> AbrechnungErstellenCoreAsync(
        Guid schulId,
        DateTime? bisUtc,
        bool automatischDurchKlassenabschluss,
        Guid? erstelltVonUserId,
        CancellationToken cancellationToken)
    {
        var schule = await dbContext.Schools.FirstOrDefaultAsync(x => x.Id == schulId, cancellationToken);
        if (schule is null)
        {
            return (false, "L'école est introuvable.", null);
        }

        var vonUtc = await ErmittleZeitraumBeginnAsync(schule, cancellationToken);
        var periodeBisUtc = bisUtc is { } requestedEnd && requestedEnd > vonUtc
            ? requestedEnd
            : DateTime.UtcNow;

        var positionen = await OffenePositionenAsync(schulId, vonUtc, periodeBisUtc, cancellationToken);
        if (positionen.Count == 0)
        {
            return (false, "Aucun paiement d'abonnement SKOULOUAPP ouvert n'est à facturer pour cette période.", null);
        }

        var periode = new Abrechnungsperiode
        {
            SchulID = schulId,
            VonUtc = vonUtc,
            BisUtc = periodeBisUtc,
            AnzahlAbonnements = positionen.Count,
            Umsatz = positionen.Sum(BerechnePlattformforderung),
            Status = AbrechnungStatus.Erstellt,
            IstAbgeschlossen = false,
            ErstelltVonUserId = erstelltVonUserId
        };
        dbContext.Abrechnungsperioden.Add(periode);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var position in positionen)
        {
            position.AbgerechnetInPeriodeId = periode.Id;
            position.GeaendertAmUtc = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var prefix = automatischDurchKlassenabschluss
            ? "La facturation SKOULOUAPP a été créée automatiquement lors de la clôture de la classe."
            : "La facturation a été créée.";
        return (true, $"{prefix} Montant : {periode.Umsatz:N0} FCFA.", periode.Id);
    }

    /// <summary>
    /// Setzt die Reaktion des SchulLeiters auf eine Abrechnung: akzeptiert (und überwiesen)
    /// oder abgelehnt (mit Begründung). Solange die Abrechnung nicht vom SuperAdmin
    /// bestätigt wurde, kann der Status geändert werden.
    /// </summary>
    public async Task<(bool Succeeded, string ErrorMessage)> SchulLeiterStatusSetzenAsync(
        Guid periodeId,
        bool akzeptiert,
        string? ablehnungsGrund = null,
        Guid? schulLeiterId = null,
        CancellationToken cancellationToken = default)
    {
        return await StatusTransaktionAsync(periodeId, () => SchulLeiterStatusSetzenCoreAsync(
            periodeId, akzeptiert, ablehnungsGrund, schulLeiterId, cancellationToken), cancellationToken);
    }

    private async Task<(bool Succeeded, string ErrorMessage)> SchulLeiterStatusSetzenCoreAsync(
        Guid periodeId, bool akzeptiert, string? ablehnungsGrund, Guid? schulLeiterId, CancellationToken cancellationToken)
    {
        var periode = await dbContext.Abrechnungsperioden.FirstOrDefaultAsync(x => x.Id == periodeId, cancellationToken);
        if (periode is null)
        {
            return (false, "La facturation est introuvable.");
        }

        if (periode.Status == AbrechnungStatus.Bestaetigt)
        {
            return (false, "Cette facturation a déjà été confirmée et ne peut plus être modifiée.");
        }

        if (schulLeiterId is Guid userId
            && !await DarfZahlungMeldenAsync(userId, periode.SchulID, cancellationToken))
        {
            return (false, "Vous n'êtes pas autorisé à modifier cette facturation.");
        }

        if (!akzeptiert && string.IsNullOrWhiteSpace(ablehnungsGrund))
        {
            return (false, "Veuillez indiquer le motif du refus.");
        }

        periode.Status = akzeptiert ? AbrechnungStatus.Akzeptiert : AbrechnungStatus.Abgelehnt;
        periode.AblehnungsGrund = akzeptiert ? null : ablehnungsGrund!.Trim();
        periode.SchulLeiterReaktionAmUtc = DateTime.UtcNow;
        periode.ZahlungGemeldetVonUserId = akzeptiert ? schulLeiterId : null;
        periode.GeaendertAmUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, akzeptiert
            ? "La facturation a été marquée comme acceptée et payée par virement."
            : "La facturation a été refusée.");
    }

    private async Task<bool> DarfAbrechnungErstellenAsync(Guid benutzerId, Guid schulId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Users.AsNoTracking().AnyAsync(x => x.Id == benutzerId && !x.IsBlocked, cancellationToken)) return false;
        var rollen = await (from userRole in dbContext.UserRoles.AsNoTracking()
                            join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                            where userRole.UserId == benutzerId
                            select role.Name)
            .ToListAsync(cancellationToken);

        if (rollen.Contains(Rollen.SuperAdmin))
        {
            return true;
        }

        return rollen.Contains(Rollen.ProduktOwner)
            && await dbContext.UserSchools.AnyAsync(link => link.AppUserId == benutzerId
                && link.SchoolId == schulId, cancellationToken);
    }

    private async Task<bool> DarfZahlungMeldenAsync(Guid benutzerId, Guid schulId, CancellationToken cancellationToken)
    {
        if (!await new FinanzService(dbContext).CanAccessBillingAsync(benutzerId, schulId, cancellationToken)) return false;
        var rollen = await (from userRole in dbContext.UserRoles.AsNoTracking()
                            join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                            where userRole.UserId == benutzerId
                            select role.Name)
            .ToListAsync(cancellationToken);

        return await dbContext.UserSchools.AnyAsync(link => link.AppUserId == benutzerId
            && link.SchoolId == schulId && link.CanManageStudents
            && (rollen.Contains(Rollen.SchulLeiter)
                || (rollen.Contains(Rollen.Koordinator) && link.CanAccessFinances && link.CanAccessBilling)),
            cancellationToken);
    }

    /// <summary>
    /// Bestätigt eine vom SchulLeiter akzeptierte Abrechnung endgültig. Nur der SuperAdmin
    /// löst dies aus. Die Abrechnung wird abgeschlossen und kann nicht mehr geändert werden.
    /// </summary>
    public async Task<(bool Succeeded, string ErrorMessage)> AdminBestaetigenAsync(
        Guid periodeId,
        Guid superAdminId,
        CancellationToken cancellationToken = default)
    {
        return await StatusTransaktionAsync(periodeId, () => AdminBestaetigenCoreAsync(
            periodeId, superAdminId, cancellationToken), cancellationToken);
    }

    private async Task<(bool Succeeded, string ErrorMessage)> AdminBestaetigenCoreAsync(
        Guid periodeId, Guid superAdminId, CancellationToken cancellationToken)
    {
        if (!await IstSuperAdminAsync(superAdminId, cancellationToken))
        {
            return (false, "Seul l'Administrateur peut confirmer la réception du virement.");
        }

        var periode = await dbContext.Abrechnungsperioden.FirstOrDefaultAsync(x => x.Id == periodeId, cancellationToken);
        if (periode is null)
        {
            return (false, "La facturation est introuvable.");
        }

        if (periode.Status != AbrechnungStatus.Akzeptiert)
        {
            return (false, "Seule une facturation acceptée par le directeur peut être confirmée.");
        }

        var jetzt = DateTime.UtcNow;
        periode.Status = AbrechnungStatus.Bestaetigt;
        periode.IstAbgeschlossen = true;
        periode.AbgeschlossenAmUtc = jetzt;
        periode.BestaetigtAmUtc = jetzt;
        periode.BestaetigtVonSuperAdminId = superAdminId;
        periode.GeaendertAmUtc = jetzt;

        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, $"La facturation a été confirmée et clôturée. Montant : {periode.Umsatz:N0} FCFA.");
    }

    private Task<bool> IstSuperAdminAsync(Guid benutzerId, CancellationToken cancellationToken)
        => (from userRole in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == benutzerId && role.Name == Rollen.SuperAdmin
                && dbContext.Users.Any(u => u.Id == benutzerId && !u.IsBlocked)
            select userRole)
            .AnyAsync(cancellationToken);

    /// <summary>Liefert alle von Schulen gemeldeten Zahlungen, die noch die finale Bankprüfung des SuperAdmins benötigen.</summary>
    public async Task<List<OffeneSuperAdminBestaetigung>> GetOffeneSuperAdminBestaetigungenAsync(
        CancellationToken cancellationToken = default)
    {
        return await (
            from periode in dbContext.Abrechnungsperioden.AsNoTracking()
            join schule in dbContext.Schools.AsNoTracking() on periode.SchulID equals schule.Id
            join ersteller in dbContext.Users.AsNoTracking() on periode.ErstelltVonUserId equals ersteller.Id into erstellerJoin
            from ersteller in erstellerJoin.DefaultIfEmpty()
            join melder in dbContext.Users.AsNoTracking() on periode.ZahlungGemeldetVonUserId equals melder.Id into melderJoin
            from melder in melderJoin.DefaultIfEmpty()
            where periode.Status == AbrechnungStatus.Akzeptiert && !periode.IstAbgeschlossen
            orderby periode.SchulLeiterReaktionAmUtc descending, periode.BisUtc descending
            select new OffeneSuperAdminBestaetigung
            {
                AbrechnungsperiodeId = periode.Id,
                SchulID = periode.SchulID,
                SchulName = schule.Name,
                Umsatz = periode.Umsatz,
                AnzahlAbonnements = periode.AnzahlAbonnements,
                ZahlungGemeldetAmUtc = periode.SchulLeiterReaktionAmUtc,
                ZahlungGemeldetVon = melder == null ? "Direction de l'école (historique)" : melder.FullName,
                RechnungErstelltVon = ersteller == null ? "Administrateur (historique)" : ersteller.FullName,
                WurdeVonProduktOwnerErstellt = dbContext.UserRoles
                    .Join(dbContext.Roles,
                        userRole => userRole.RoleId,
                        role => role.Id,
                        (userRole, role) => new { userRole, role })
                    .Any(zuordnung => zuordnung.userRole.UserId == periode.ErstelltVonUserId
                        && zuordnung.role.Name == Rollen.ProduktOwner)
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Liefert die Historie aller Abrechnungen einer Schule (neueste zuerst), inkl. Status.
    /// </summary>
    public Task<List<Abrechnungsperiode>> GetHistorieAsync(Guid schulId, CancellationToken cancellationToken = default)
        => dbContext.Abrechnungsperioden
            .AsNoTracking()
            .Where(x => x.SchulID == schulId)
            .OrderByDescending(x => x.BisUtc)
            .ToListAsync(cancellationToken);
}
