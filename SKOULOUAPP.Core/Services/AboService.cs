using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace SkoulouApp.Core.Services;

/// <summary>
/// Verwaltet die Abos (SKOULOUBASIS/SKOULOUPLUS) der Schüler pro Niveau
/// sowie den Bezahlstatus. Nur Schüler haben Abos; SchulLeiter und Lehrer
/// bezahlen nicht.
///
/// Berechtigungsregeln (durch den Aufrufer/UI vorgelagert geprüft, hier zusätzlich abgesichert):
/// - Abo zuweisen / Niveau freischalten: SchulLeiter der Schule (oder SuperAdmin).
/// - Bezahlt = true setzen: SchulLeiter der Schule (oder SuperAdmin). NICHT rückgängig machbar durch SchulLeiter.
/// - Bezahlt = false (rückgängig): nur SuperAdmin.
/// </summary>
public class AboService(ISkoulouAppDbContext dbContext, KlassenZuordnungsService? niveauGruppenService = null, FinanzService? finanzService = null)
{
    // Optionaler Parameter, damit bestehende Aufrufe (u. a. Tests) unveraendert bleiben.
    private readonly KlassenZuordnungsService _niveauGruppen = niveauGruppenService ?? new KlassenZuordnungsService(dbContext);
    private readonly FinanzService _finanzService = finanzService ?? new FinanzService(dbContext);
    private async Task<decimal> GetGlobalSubscriptionPriceAsync(CancellationToken cancellationToken)
        => await dbContext.CommissionSettings
            .AsNoTracking()
            .OrderBy(x => x.ErstelltAmUtc)
            .Select(x => (decimal?)x.GlobalSubscriptionPrice)
            .FirstOrDefaultAsync(cancellationToken)
        ?? SkoulouAppKonstanten.StandardPreisAbo;
    private async Task<Guid> ResolveErfasserUserIdAsync(
        Guid erfasstVonUserId,
        Guid schoolId,
        Guid fallbackUserId,
        CancellationToken cancellationToken)
    {
        if (erfasstVonUserId != Guid.Empty)
        {
            return erfasstVonUserId;
        }

        if (schoolId != Guid.Empty)
        {
            var schulErfasserUserId = await dbContext.UserSchools
                .AsNoTracking()
                .Where(link => link.SchoolId == schoolId && link.CanManageStudents)
                .Select(link => link.AppUserId)
                .FirstOrDefaultAsync(cancellationToken);

            if (schulErfasserUserId != Guid.Empty)
            {
                return schulErfasserUserId;
            }
        }

        return fallbackUserId;
    }

    /// <summary>
    /// Legt einmalig ein Abo für ein Niveau fest oder führt ausschließlich ein
    /// Upgrade von SKOULOUBASIS auf SKOULOUPLUS durch. Eine Stornierung sowie eine
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

        var activeClassCycleId = await dbContext.KursNiveauDurchlaeufe
            .AsNoTracking()
            .Where(durchlauf => durchlauf.SchulID == profil.SchoolId
                && durchlauf.KlasseId == klasseId
                && durchlauf.EndDatum == null)
            .Select(durchlauf => (Guid?)durchlauf.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (activeClassCycleId is null)
        {
            return (false, "Démarrez d'abord le cycle de cette classe avant d'attribuer ou modifier un abonnement SKOULOU.");
        }
        if (await dbContext.KursNiveauDurchlaeufe.AnyAsync(x => x.Id == activeClassCycleId && x.PlattformPreisProSchueler != null, cancellationToken))
            return (false, "Les participants de ce cycle sont enregistrés automatiquement. Aucune attribution manuelle n'est nécessaire.");

        // Zuordnungen sind auf reguläre Klassen der eigenen Schule beschränkt.
        if (!await _niveauGruppen.IstKlasseErlaubtAsync(profil, klasseId, cancellationToken))
        {
            return (false, KlassenZuordnungsService.NichtErlaubtMeldung);
        }

        var globalSubscriptionPrice = await GetGlobalSubscriptionPriceAsync(cancellationToken);
        var preis = profil.School.EffektiverPreis(globalSubscriptionPrice);
        var commissionSettings = await dbContext.CommissionSettings
            .AsNoTracking()
            .OrderBy(x => x.ErstelltAmUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var schoolLeaderPercent = profil.School.SchoolLeaderCommissionPercentOverride
            ?? commissionSettings?.SchoolLeaderPercent
            ?? SkoulouAppKonstanten.StandardProvisionSchulleiterProzent;
        var teacherPercent = profil.School.TeacherCommissionPercentOverride
            ?? commissionSettings?.TeacherPercent
            ?? SkoulouAppKonstanten.StandardProvisionLehrerProzent;
        var productOwnerPercent = profil.School.ProductOwnerCommissionPercentOverride
            ?? commissionSettings?.ProductOwnerPercent
            ?? SkoulouAppKonstanten.StandardProvisionProductOwnerProzent;

        var schoolLeaderCommission = SkoulouAppKonstanten.BerechneProvision(preis, schoolLeaderPercent);
        var teacherCommission = SkoulouAppKonstanten.BerechneProvision(preis, teacherPercent);
        var productOwnerCommission = SkoulouAppKonstanten.BerechneProvision(preis, productOwnerPercent);

        if (schoolLeaderCommission + teacherCommission + productOwnerCommission > preis)
        {
            return (false, "Les commissions configurées dépassent le prix de l'abonnement.");
        }

        var enrollment = await dbContext.LevelEnrollments
            .FirstOrDefaultAsync(x => x.StudentId == studentId && x.KlasseId == klasseId && x.IstAktuell, cancellationToken);

        if (enrollment is not null && enrollment.DurchlaufId != activeClassCycleId
            && enrollment.DurchlaufId is not null)
        {
            enrollment.IstAktuell = false;
            enrollment = null;
        }

        if (enrollment is not null && enrollment.DurchlaufId is null)
        {
            enrollment.DurchlaufId = activeClassCycleId;
        }

        var nowUtc = DateTime.UtcNow;
        var warVorherBezahlt = enrollment?.IsPaid ?? false;

        if (enrollment is null)
        {
            enrollment = new LevelEnrollment
            {
                StudentId = studentId,
                KlasseId = klasseId,
                DurchlaufId = activeClassCycleId,
                AboTyp = AboTyp.SkoulouApp,
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

            var istNochKeinFinalesSkoulouAppAbo = enrollment.SchoolLeaderCommissionAtEnrollment <= 0m
                && enrollment.TeacherCommissionAtEnrollment <= 0m;
            if (istNochKeinFinalesSkoulouAppAbo
                || kannDeaktiviertesAboNeuAktivieren
                || kannUnbezahltesAboDerAktuellenKlasseNeuFestgelegtWerden)
            {
                enrollment.AboTyp = AboTyp.SkoulouApp;
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
                return (false, "L'abonnement SKOULOU de cette classe est déjà attribué et ne peut plus être modifié.");
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
            var erfasserUserId = await ResolveErfasserUserIdAsync(
                erfasstVonUserId,
                profil.SchoolId,
                studentId,
                cancellationToken);
            var (financeOk, financeError) = await _finanzService.SkoulouAppAboBuchungenErstellenAsync(enrollment.Id, erfasserUserId, cancellationToken);
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
            .FirstOrDefaultAsync(x => x.StudentId == studentId && x.KlasseId == klasseId && x.IstAktuell, cancellationToken);

        if (enrollment?.DurchlaufId is { } zyklusID && await dbContext.KursNiveauDurchlaeufe.AnyAsync(x => x.Id == zyklusID && x.PlattformPreisProSchueler != null, cancellationToken))
            return (false, "Cette participation est facturée par cycle. Utilisez le suivi des paiements de l'école.");
        if (enrollment is null)
        {
            return (false, "Aucune activation n'a été trouvée pour ce niveau.");
        }

        if (enrollment.IsPaid)
        {
            return (false, "Ce niveau est déjà marqué comme payé.");
        }

        if (enrollment.PriceAtEnrollment <= 0m)
        {
            return await AboZuweisenAsync(studentId, klasseId, AboTyp.SkoulouApp, erfasstVonUserId, cancellationToken);
        }

        enrollment.IsPaid = true;
        enrollment.PaidAtUtc = DateTime.UtcNow;
        enrollment.GeaendertAmUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        var schoolId = await dbContext.StudentProfiles
            .Where(profile => profile.AppUserId == studentId)
            .Select(profile => profile.SchoolId)
            .FirstOrDefaultAsync(cancellationToken);
        var erfasserUserId = await ResolveErfasserUserIdAsync(
            erfasstVonUserId,
            schoolId,
            studentId,
            cancellationToken);
        var (financeOk, financeError) = await _finanzService.SkoulouAppAboBuchungenErstellenAsync(enrollment.Id, erfasserUserId, cancellationToken);
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
            return (false, "Seul l'Administrateur peut annuler un paiement SKOULOU.");
        }

        var enrollment = await dbContext.LevelEnrollments
            .FirstOrDefaultAsync(x => x.StudentId == studentId && x.KlasseId == klasseId && x.IstAktuell, cancellationToken);

        if (enrollment is null)
        {
            return (false, "Aucune activation n'a été trouvée pour ce niveau.");
        }

        if (!enrollment.IsPaid)
        {
            return (false, "Ce niveau n'est pas marqué comme payé.");
        }

        if (enrollment.DurchlaufId is { } zyklusID && await dbContext.KursNiveauDurchlaeufe.AnyAsync(x => x.Id == zyklusID && x.PlattformPreisProSchueler != null, cancellationToken))
            return (false, "Le règlement de ce cycle se gère dans la facturation de l'école, pas sur le compte de l'élève.");

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
            return (false, "Le paiement SKOULOU ne peut être annulé que tant que le cycle de cette classe est encore ouvert.");
        }

        enrollment.IsPaid = false;
        enrollment.PaidAtUtc = null;
        enrollment.GeaendertAmUtc = DateTime.UtcNow;

        var buchenZuEntfernen = await dbContext.FinanzBuchungen
            .Include(buchung => buchung.Kategorie)
            .Where(buchung => buchung.HerkunftReferenzID == enrollment.Id
                && (buchung.Kategorie.SystemCode == FinanzSystemCode.SkoulouAppAboZahlung
                    || buchung.Kategorie.SystemCode == FinanzSystemCode.SkoulouAppAboAnteil
                    || buchung.Kategorie.SystemCode == FinanzSystemCode.SkoulouAppAboLehrerAnteil
                    || buchung.Kategorie.SystemCode == FinanzSystemCode.SkoulouAppAboProductOwnerAnteil))
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
    /// Bei bereits bezahlten Abos werden die zugehörigen SKOULOUAPP-Finanzbuchungen
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
            .FirstOrDefaultAsync(x => x.StudentId == studentId && x.KlasseId == klasseId && x.IstAktuell, cancellationToken);
        if (enrollment is null)
        {
            return (false, "Aucune attribution d'abonnement n'a été trouvée pour cette classe.");
        }

        if (enrollment.AbgerechnetInPeriodeId is not null)
        {
            return (false, "Cet abonnement a déjà été facturé et ne peut plus être annulé.");
        }

        if (enrollment.DurchlaufId is { } zyklusID && await dbContext.KursNiveauDurchlaeufe.AnyAsync(x => x.Id == zyklusID && x.PlattformPreisProSchueler != null, cancellationToken))
            return (false, "Utilisez l'annulation motivée d'une affectation erronée dans les participants du cycle.");

        if (enrollment.IsPaid)
        {
            var buchungen = await dbContext.FinanzBuchungen
                .Where(buchung => buchung.HerkunftReferenzID == enrollment.Id
                    && (buchung.Kategorie.SystemCode == FinanzSystemCode.SkoulouAppAboZahlung
                        || buchung.Kategorie.SystemCode == FinanzSystemCode.SkoulouAppAboAnteil
                        || buchung.Kategorie.SystemCode == FinanzSystemCode.SkoulouAppAboLehrerAnteil
                        || buchung.Kategorie.SystemCode == FinanzSystemCode.SkoulouAppAboProductOwnerAnteil))
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

    /// <summary>
    /// Storniert ein noch nicht abgerechnetes Abo und trennt den Schüler im selben
    /// Admin-Vorgang von der zugehörigen Klasse. Die reine Abo-Stornierung bleibt
    /// davon unabhängig und entfernt keine Klassenzuordnung.
    /// </summary>
    public async Task<(bool Succeeded, string ErrorMessage)> AboEntfernenUndAusKlasseEntfernenAsync(
        Guid studentId,
        Guid klasseId,
        bool istSuperAdmin = false,
        CancellationToken cancellationToken = default)
    {
        if (!istSuperAdmin)
        {
            return (false, "Seul l'Administrateur peut annuler l'abonnement et retirer l'élève de la classe.");
        }

        var enrollment = await dbContext.LevelEnrollments
            .FirstOrDefaultAsync(item => item.StudentId == studentId
                && item.KlasseId == klasseId
                && item.IstAktuell, cancellationToken);
        if (enrollment is null)
        {
            return (false, "Aucune attribution active n'a été trouvée pour cette classe.");
        }

        if (enrollment.AbgerechnetInPeriodeId is not null)
        {
            return (false, "Cet abonnement a déjà été facturé et ne peut plus être annulé.");
        }

        var profile = await dbContext.StudentProfiles
            .FirstOrDefaultAsync(item => item.AppUserId == studentId
                && item.CurrentKlasseId == klasseId, cancellationToken);
        if (profile is null)
        {
            return (false, "L'élève n'est plus affecté à cette classe.");
        }

        if (enrollment.DurchlaufId is { } zyklusID && await dbContext.KursNiveauDurchlaeufe.AnyAsync(x => x.Id == zyklusID && x.PlattformPreisProSchueler != null, cancellationToken))
            return (false, "La sortie de classe ne supprime pas la participation facturable. Utilisez le suivi des participants du cycle.");

        if (enrollment.IsPaid)
        {
            var buchungen = await dbContext.FinanzBuchungen
                .Where(buchung => buchung.HerkunftReferenzID == enrollment.Id
                    && (buchung.Kategorie.SystemCode == FinanzSystemCode.SkoulouAppAboZahlung
                        || buchung.Kategorie.SystemCode == FinanzSystemCode.SkoulouAppAboAnteil
                        || buchung.Kategorie.SystemCode == FinanzSystemCode.SkoulouAppAboLehrerAnteil
                        || buchung.Kategorie.SystemCode == FinanzSystemCode.SkoulouAppAboProductOwnerAnteil))
                .ToListAsync(cancellationToken);
            if (buchungen.Count > 0)
            {
                dbContext.FinanzBuchungen.RemoveRange(buchungen);
            }
        }

        enrollment.IsUnlocked = false;
        enrollment.IsPaid = false;
        enrollment.PaidAtUtc = null;
        enrollment.IstAktuell = false;
        enrollment.GeaendertAmUtc = DateTime.UtcNow;
        profile.CurrentKlasseId = null;
        profile.CurrentLevelCode = string.Empty;
        profile.GeaendertAmUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, "L'abonnement a été annulé et l'élève a été retiré de la classe.");
    }
}
