using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Finanzen;
using HalloApp.Core.DataModels.Schools;
using HalloApp.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HalloApp.Core.Services;

/// <summary>
/// Verwaltet die Abos (HALLOBASIS/HALLOPLUS) der Schüler pro Niveau
/// sowie den Bezahlstatus. Nur Schüler haben Abos; SchulLeiter und Lehrer
/// bezahlen nicht.
///
/// Berechtigungsregeln (durch den Aufrufer/UI vorgelagert geprüft, hier zusätzlich abgesichert):
/// - Abo zuweisen / Niveau freischalten: SchulLeiter der Schule (oder SuperAdmin).
/// - Bezahlt = true setzen: SchulLeiter der Schule (oder SuperAdmin). NICHT rückgängig machbar durch SchulLeiter.
/// - Bezahlt = false (rückgängig): nur SuperAdmin.
/// </summary>
public class AboService(IHalloAppDbContext dbContext, NiveauGruppenService? niveauGruppenService = null, FinanzService? finanzService = null)
{
    // Optionaler Parameter, damit bestehende Aufrufe (u. a. Tests) unveraendert bleiben.
    private readonly NiveauGruppenService _niveauGruppen = niveauGruppenService ?? new NiveauGruppenService(dbContext);
    private readonly FinanzService _finanzService = finanzService ?? new FinanzService(dbContext);
    private async Task<decimal> GetGlobalSubscriptionPriceAsync(CancellationToken cancellationToken)
        => await dbContext.CommissionSettings
            .AsNoTracking()
            .OrderBy(x => x.ErstelltAmUtc)
            .Select(x => (decimal?)x.GlobalSubscriptionPrice)
            .FirstOrDefaultAsync(cancellationToken)
        ?? HalloAppKonstanten.StandardPreisAbo;

    /// <summary>
    /// Legt einmalig ein Abo für ein Niveau fest oder führt ausschließlich ein
    /// Upgrade von HALLOBASIS auf HALLOPLUS durch. Eine Stornierung sowie eine
    /// Rückstufung sind fachlich ausgeschlossen.
    /// </summary>
    public async Task<(bool Succeeded, string ErrorMessage)> AboZuweisenAsync(
        Guid studentId,
        Guid klasseId,
        AboTyp aboTyp,
        Guid erfasstVonUserId = default,
        CancellationToken cancellationToken = default)
    {
        // Schule des Schülers ermitteln (für die Preisbildung).
        var profil = await dbContext.StudentProfiles
            .Include(x => x.School)
            .FirstOrDefaultAsync(x => x.AppUserId == studentId, cancellationToken);

        if (profil is null)
        {
            return (false, "Aucun profil n'a été trouvé pour cet élève.");
        }

        var level = await dbContext.Klassen
            .FirstOrDefaultAsync(x => x.Id == klasseId, cancellationToken);
        if (level is null)
        {
            return (false, "Le niveau sélectionné est introuvable.");
        }

        var hasActiveClassCycle = await dbContext.KursNiveauDurchlaeufe
            .AsNoTracking()
            .AnyAsync(durchlauf => durchlauf.SchulID == profil.SchoolId
                && durchlauf.KlasseId == klasseId
                && durchlauf.EndDatum == null, cancellationToken);
        if (!hasActiveClassCycle)
        {
            return (false, "Démarre d'abord le cycle de cette classe avant d'attribuer ou modifier un abonnement HALLOAPP.");
        }

        // Ein Schüler darf ausschließlich Niveaus seiner eigenen Niveaugruppe belegen
        // (z. B. nur A1.1 und A1.2, wenn seine Gruppe "A1" ist).
        if (!await _niveauGruppen.IstKlasseErlaubtAsync(profil, klasseId, cancellationToken))
        {
            return (false, NiveauGruppenService.NichtErlaubtMeldung);
        }

        var globalSubscriptionPrice = await GetGlobalSubscriptionPriceAsync(cancellationToken);
        var preis = profil.School.EffektiverPreis(globalSubscriptionPrice);
        var commissionSettings = await dbContext.CommissionSettings
            .AsNoTracking()
            .OrderBy(x => x.ErstelltAmUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var schoolLeaderPercent = profil.School.SchoolLeaderCommissionPercentOverride
            ?? commissionSettings?.SchoolLeaderPercent
            ?? HalloAppKonstanten.StandardProvisionSchulleiterProzent;
        var teacherPercent = profil.School.TeacherCommissionPercentOverride
            ?? commissionSettings?.TeacherPercent
            ?? HalloAppKonstanten.StandardProvisionLehrerProzent;
        var productOwnerPercent = profil.School.ProductOwnerCommissionPercentOverride
            ?? commissionSettings?.ProductOwnerPercent
            ?? HalloAppKonstanten.StandardProvisionProductOwnerProzent;

        var schoolLeaderCommission = HalloAppKonstanten.BerechneProvision(preis, schoolLeaderPercent);
        var teacherCommission = HalloAppKonstanten.BerechneProvision(preis, teacherPercent);
        var productOwnerCommission = HalloAppKonstanten.BerechneProvision(preis, productOwnerPercent);

        if (schoolLeaderCommission + teacherCommission + productOwnerCommission > preis)
        {
            return (false, "Les commissions configurées dépassent le prix de l'abonnement.");
        }

        var enrollment = await dbContext.LevelEnrollments
            .FirstOrDefaultAsync(x => x.StudentId == studentId && x.KlasseId == klasseId, cancellationToken);

        var nowUtc = DateTime.UtcNow;
        var warVorherBezahlt = enrollment?.IsPaid ?? false;

        if (enrollment is null)
        {
            enrollment = new LevelEnrollment
            {
                StudentId = studentId,
                KlasseId = klasseId,
                AboTyp = AboTyp.HalloApp,
                IsUnlocked = true,
                IsPaid = true,
                PriceAtEnrollment = preis,
                SchoolLeaderCommissionAtEnrollment = schoolLeaderCommission,
                TeacherCommissionAtEnrollment = teacherCommission,
                ProductOwnerCommissionAtEnrollment = productOwnerCommission,
                UnlockedAtUtc = nowUtc,
                PaidAtUtc = nowUtc
            };
            dbContext.LevelEnrollments.Add(enrollment);
        }
        else
        {
            var kannDeaktiviertesAboNeuAktivieren = !enrollment.IsUnlocked
                && !enrollment.IsPaid
                && enrollment.AbgerechnetInPeriodeId is null;

            var kannUnbezahltesAboDerAktuellenKlasseNeuFestgelegtWerden = profil.CurrentKlasseId == klasseId
                && !enrollment.IsPaid
                && enrollment.AbgerechnetInPeriodeId is null;

            var istNochKeinFinalesHalloAppAbo = enrollment.SchoolLeaderCommissionAtEnrollment <= 0m
                && enrollment.TeacherCommissionAtEnrollment <= 0m;
            if (istNochKeinFinalesHalloAppAbo
                || kannDeaktiviertesAboNeuAktivieren
                || kannUnbezahltesAboDerAktuellenKlasseNeuFestgelegtWerden)
            {
                enrollment.AboTyp = AboTyp.HalloApp;
                enrollment.IsUnlocked = true;
                enrollment.IsPaid = true;
                enrollment.PriceAtEnrollment = preis;
                enrollment.SchoolLeaderCommissionAtEnrollment = schoolLeaderCommission;
                enrollment.TeacherCommissionAtEnrollment = teacherCommission;
                enrollment.ProductOwnerCommissionAtEnrollment = productOwnerCommission;
                enrollment.UnlockedAtUtc ??= nowUtc;
                enrollment.PaidAtUtc ??= nowUtc;
                enrollment.GeaendertAmUtc = nowUtc;
            }
            else
            {
                return (false, "L'abonnement HALLOAPP de cette classe est déjà attribué et ne peut plus être modifié.");
            }
        }

        // Das aktuelle Niveau des Schülers folgt dem Fortschritt: Wird ein höheres
        // Niveau freigeschaltet, wird es zum neuen aktuellen Niveau. Eine Rückstufung
        // (Freischalten eines niedrigeren Niveaus) lässt das aktuelle Niveau unverändert.
        if (string.IsNullOrWhiteSpace(profil.CurrentLevelCode)
            || string.CompareOrdinal(level.Code, profil.CurrentLevelCode) > 0)
        {
            profil.CurrentLevelCode = level.Code;
            profil.CurrentKlasseId = level.Id;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (!warVorherBezahlt)
        {
            var (financeOk, financeError) = await _finanzService.HalloAppAboBuchungenErstellenAsync(enrollment.Id, erfasstVonUserId, cancellationToken);
            if (!financeOk)
            {
                return (false, financeError);
            }
        }

        return (true, "L'abonnement a été enregistré comme payé et la classe a été activée.");
    }

    /// <summary>
    /// Markiert ein Niveau als bezahlt. Diese Aktion ist für den SchulLeiter
    /// endgültig (kein Rückgängig). Deshalb sollte die UI vorher eine
    /// Bestätigung (MessageBox) einholen.
    /// </summary>
    public async Task<(bool Succeeded, string ErrorMessage)> BezahltSetzenAsync(
        Guid studentId,
        Guid klasseId,
        Guid erfasstVonUserId = default,
        CancellationToken cancellationToken = default)
    {
        var enrollment = await dbContext.LevelEnrollments
            .FirstOrDefaultAsync(x => x.StudentId == studentId && x.KlasseId == klasseId, cancellationToken);

        if (enrollment is null)
        {
            return (false, "Aucune activation n'a été trouvée pour ce niveau.");
        }

        if (enrollment.IsPaid)
        {
            return (false, "Ce niveau est déjà marqué comme payé.");
        }

        enrollment.IsPaid = true;
        enrollment.PaidAtUtc = DateTime.UtcNow;
        enrollment.GeaendertAmUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        var (financeOk, financeError) = await _finanzService.HalloAppAboBuchungenErstellenAsync(enrollment.Id, erfasstVonUserId, cancellationToken);
        if (!financeOk)
        {
            return (false, financeError);
        }

        return (true, "Le niveau a été marqué comme payé.");
    }

    /// <summary>
    /// Macht eine Zahlung rückgängig. Nur der SuperAdmin darf dies (nach Bestätigung)
    /// und nur solange die Zahlung noch nicht in einer abgeschlossenen
    /// Abrechnungsperiode enthalten ist.
    /// </summary>
    public async Task<(bool Succeeded, string ErrorMessage)> BezahltZuruecknehmenAsync(
        Guid studentId,
        Guid klasseId,
        bool istSuperAdmin = false,
        CancellationToken cancellationToken = default)
    {
        if (!istSuperAdmin)
        {
            return (false, "Seul l'Administrateur peut annuler un paiement HALLOAPP.");
        }

        var enrollment = await dbContext.LevelEnrollments
            .FirstOrDefaultAsync(x => x.StudentId == studentId && x.KlasseId == klasseId, cancellationToken);

        if (enrollment is null)
        {
            return (false, "Aucune activation n'a été trouvée pour ce niveau.");
        }

        if (!enrollment.IsPaid)
        {
            return (false, "Ce niveau n'est pas marqué comme payé.");
        }

        if (enrollment.AbgerechnetInPeriodeId is not null)
        {
            return (false, "Le paiement a déjà été facturé et ne peut plus être annulé.");
        }

        var schoolId = await dbContext.StudentProfiles
            .Where(profile => profile.AppUserId == studentId)
            .Select(profile => profile.SchoolId)
            .FirstOrDefaultAsync(cancellationToken);
        if (schoolId == Guid.Empty)
        {
            return (false, "Aucun profil n'a été trouvé pour cet élève.");
        }

        var hasActiveClassCycle = await dbContext.KursNiveauDurchlaeufe
            .AsNoTracking()
            .AnyAsync(durchlauf => durchlauf.SchulID == schoolId
                && durchlauf.KlasseId == klasseId
                && durchlauf.EndDatum == null, cancellationToken);
        if (!hasActiveClassCycle)
        {
            return (false, "Le paiement HALLOAPP ne peut être annulé que tant que le cycle de cette classe est encore ouvert.");
        }

        enrollment.IsPaid = false;
        enrollment.PaidAtUtc = null;
        enrollment.GeaendertAmUtc = DateTime.UtcNow;

        var buchenZuEntfernen = await dbContext.FinanzBuchungen
            .Include(buchung => buchung.Kategorie)
            .Where(buchung => buchung.HerkunftReferenzID == enrollment.Id
                && (buchung.Kategorie.SystemCode == FinanzSystemCode.HalloAppAboZahlung
                    || buchung.Kategorie.SystemCode == FinanzSystemCode.HalloAppAboAnteil
                    || buchung.Kategorie.SystemCode == FinanzSystemCode.HalloAppAboLehrerAnteil
                    || buchung.Kategorie.SystemCode == FinanzSystemCode.HalloAppAboProductOwnerAnteil))
            .ToListAsync(cancellationToken);

        if (buchenZuEntfernen.Count > 0)
        {
            dbContext.FinanzBuchungen.RemoveRange(buchenZuEntfernen);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, "Le paiement a été annulé.");
    }

    /// <summary>
    /// Ein festgelegtes Abo kann nur vom SuperAdmin storniert werden
    /// und nur solange es noch nicht in einer Abrechnungsperiode enthalten ist.
    /// Bei bereits bezahlten Abos werden die zugehörigen HALLOAPP-Finanzbuchungen
    /// automatisch zurückgenommen.
    /// </summary>
    public async Task<(bool Succeeded, string ErrorMessage)> AboEntfernenAsync(
        Guid studentId,
        Guid klasseId,
        bool istSuperAdmin = false,
        CancellationToken cancellationToken = default)
    {
        if (!istSuperAdmin)
        {
            return (false, "L'abonnement attribué est définitif pour la direction. Seul l'Administrateur peut l'annuler après clarification.");
        }

        var enrollment = await dbContext.LevelEnrollments
            .FirstOrDefaultAsync(x => x.StudentId == studentId && x.KlasseId == klasseId, cancellationToken);
        if (enrollment is null)
        {
            return (false, "Aucune attribution d'abonnement n'a été trouvée pour cette classe.");
        }

        if (enrollment.AbgerechnetInPeriodeId is not null)
        {
            return (false, "Cet abonnement a déjà été facturé et ne peut plus être annulé.");
        }

        if (enrollment.IsPaid)
        {
            var buchungen = await dbContext.FinanzBuchungen
                .Where(buchung => buchung.HerkunftReferenzID == enrollment.Id
                    && (buchung.Kategorie.SystemCode == FinanzSystemCode.HalloAppAboZahlung
                        || buchung.Kategorie.SystemCode == FinanzSystemCode.HalloAppAboAnteil
                        || buchung.Kategorie.SystemCode == FinanzSystemCode.HalloAppAboLehrerAnteil
                        || buchung.Kategorie.SystemCode == FinanzSystemCode.HalloAppAboProductOwnerAnteil))
                .ToListAsync(cancellationToken);

            if (buchungen.Count > 0)
            {
                dbContext.FinanzBuchungen.RemoveRange(buchungen);
            }
        }

        enrollment.IsUnlocked = false;
        enrollment.IsPaid = false;
        enrollment.PaidAtUtc = null;
        enrollment.GeaendertAmUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, "L'abonnement a été annulé par l'Administrateur.");
    }
}
