using HalloApp.Core.DataModels.Finanzen;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Schools;
using HalloApp.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HalloApp.Core.Services;

/// <summary>
/// Zentrale Logik der Finanzverwaltung (Einnahmen/Ausgaben) pro Schule inklusive
/// der Klassenzyklen und Ratenzahlungen.
///
/// Berechtigungen (vom Aufrufer/UI vorgelagert geprüft):
/// - SchulLeiter: nur die eigene Schule.
/// - SuperAdmin: alle Schulen (mit Schulfilter).
/// - Lehrer: kein Zugriff.
///
/// Löschen von Buchungen: der Ersteller darf löschen. Automatisch erzeugte
/// HalloApp-Abo-Buchungen dürfen nur vom SuperAdmin storniert werden.
/// </summary>
public class FinanzService(IHalloAppDbContext dbContext)
{
    private async Task<bool> DarfSchuleVerwaltenAsync(
        Guid userId,
        bool istSuperAdmin,
        Guid schulId,
        CancellationToken cancellationToken)
        => userId == Guid.Empty || istSuperAdmin || await dbContext.UserSchools
            .AsNoTracking()
            .AnyAsync(link => link.AppUserId == userId
                && link.SchoolId == schulId
                && link.CanManageStudents, cancellationToken);

    private async Task<bool> DarfSchulleiterFinanzenVerwaltenAsync(
        Guid userId,
        bool istSuperAdmin,
        Guid schulId,
        CancellationToken cancellationToken)
        => userId == Guid.Empty || istSuperAdmin || await (
            from userSchool in dbContext.UserSchools.AsNoTracking()
            join userRole in dbContext.Set<AppUserRole>().AsNoTracking() on userSchool.AppUserId equals userRole.UserId
            join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userSchool.AppUserId == userId
                && userSchool.SchoolId == schulId
                && userSchool.CanManageStudents
                && (role.Name == Rollen.SchulLeiter || role.Name == Rollen.Koordinator)
            select userSchool.AppUserId)
            .AnyAsync(cancellationToken);

    private static DateTime ToInclusiveUtcDayEnd(DateTime date)
    {
        var localDayEnd = date.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(date.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local)
            : date.Date.AddDays(1).AddTicks(-1);
        return localDayEnd.ToUniversalTime();
    }

    private async Task<DateTime> ErmittleAbrechnungsZeitraumBeginnAsync(School schule, CancellationToken cancellationToken)
    {
        var letzte = await dbContext.Abrechnungsperioden
            .Where(x => x.SchulID == schule.Id)
            .OrderByDescending(x => x.BisUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return letzte?.BisUtc ?? schule.ErstelltAmUtc;
    }

    private async Task<List<DataModels.Courses.LevelEnrollment>> LadeOffeneHalloAppAboPositionenAsync(
        Guid schulId,
        DateTime vonUtc,
        DateTime bisUtc,
        CancellationToken cancellationToken)
    {
        var studentIds = await dbContext.StudentProfiles
            .Where(x => x.SchoolId == schulId)
            .Select(x => x.AppUserId)
            .ToListAsync(cancellationToken);

        return await dbContext.LevelEnrollments
            .Where(x => studentIds.Contains(x.StudentId)
                && x.IsPaid
                && x.PriceAtEnrollment > 0m
                && (x.SchoolLeaderCommissionAtEnrollment > 0m || x.TeacherCommissionAtEnrollment > 0m)
                && x.AbgerechnetInPeriodeId == null
                && x.PaidAtUtc != null
                && x.PaidAtUtc >= vonUtc
                && x.PaidAtUtc <= bisUtc)
            .ToListAsync(cancellationToken);
    }

    // -------------------- Kategorien --------------------

    /// <summary>
    /// Liefert alle für eine Schule gültigen Kategorien: globale (SchulID == null)
    /// plus die schul-eigenen. Optional nach Richtung gefiltert.
    /// </summary>
    public async Task<List<FinanzKategorie>> GetKategorienAsync(
        Guid schulId,
        FinanzRichtung? richtung = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.FinanzKategorien
            .Where(x => x.IstAktiv && (x.SchulID == null || x.SchulID == schulId));

        if (richtung is not null)
        {
            query = query.Where(x => x.Richtung == richtung);
        }

        var kategorien = await query
            .OrderBy(x => x.Richtung)
            .ThenBy(x => x.Bezeichnung)
            .ToListAsync(cancellationToken);

        var aktualisiert = false;
        foreach (var kategorie in kategorien.Where(kategorie => kategorie.IstSystemTyp))
        {
            var bezeichnung = kategorie.SystemCode switch
            {
                FinanzSystemCode.HalloAppAboZahlung => "HALLOAPP-Abo · Paiement élève",
                FinanzSystemCode.KursNiveauZahlung => "Paiement de classe",
                FinanzSystemCode.HalloAppAboAnteil => "HALLOAPP-Abo · Part plateforme",
                FinanzSystemCode.HalloAppAboLehrerAnteil => "HALLOAPP-Abo · Fonds enseignants",
                FinanzSystemCode.HalloAppAboSchulleiterAnteil => "HALLOAPP-Abo · Part direction",
                FinanzSystemCode.HalloAppAboProductOwnerAnteil => "Abonnement HALLOAPP · Part du responsable produit",
                _ => kategorie.Bezeichnung
            };

            if (kategorie.Bezeichnung != bezeichnung)
            {
                kategorie.Bezeichnung = bezeichnung;
                aktualisiert = true;
            }
        }

        if (aktualisiert)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            kategorien = kategorien
                .OrderBy(kategorie => kategorie.Richtung)
                .ThenBy(kategorie => kategorie.Bezeichnung)
                .ToList();
        }

        return kategorien;
    }

    /// <summary>
    /// Legt bei "kein Treffer" eine neue Kategorie an. Die Richtung
    /// (Einnahme/Ausgabe) muss zwingend gewählt werden. SuperAdmin-Kategorien
    /// sind global (schulId == null), SchulLeiter-Kategorien schul-lokal.
    /// </summary>
    public async Task<(bool Succeeded, string ErrorMessage, FinanzKategorie? Kategorie)> KategorieAnlegenAsync(
        Guid? schulId,
        string bezeichnung,
        FinanzRichtung richtung,
        Guid erstelltVonUserId = default,
        bool istSuperAdmin = false,
        bool autorisierungErzwingen = false,
        CancellationToken cancellationToken = default)
    {
        if (autorisierungErzwingen && schulId is Guid schoolId
            && !await DarfSchuleVerwaltenAsync(erstelltVonUserId, istSuperAdmin, schoolId, cancellationToken))
        {
            return (false, "Vous n'êtes pas autorisé à gérer cette école.", null);
        }

        if (autorisierungErzwingen && schulId is null && !istSuperAdmin)
        {
            return (false, "Seul un Administrateur peut créer une catégorie globale.", null);
        }

        bezeichnung = bezeichnung?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(bezeichnung))
        {
            return (false, "Veuillez indiquer un libellé pour la catégorie.", null);
        }

        var existiert = await dbContext.FinanzKategorien.AnyAsync(
            x => x.Richtung == richtung
                 && x.Bezeichnung == bezeichnung
                 && (x.SchulID == schulId || x.SchulID == null),
            cancellationToken);

        if (existiert)
        {
            return (false, "Une catégorie portant ce nom existe déjà.", null);
        }

        var kategorie = new FinanzKategorie
        {
            SchulID = schulId,
            Bezeichnung = bezeichnung,
            Richtung = richtung,
            SystemCode = FinanzSystemCode.Keiner,
            IstSystemTyp = false,
            IstAktiv = true
        };

        dbContext.FinanzKategorien.Add(kategorie);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, string.Empty, kategorie);
    }

    /// <summary>
    /// Löscht eine frei angelegte Kategorie, sofern sie nicht vom System verwendet
    /// wird und noch keine Finanzbuchung darauf verweist.
    /// </summary>
    public async Task<(bool Succeeded, string ErrorMessage)> KategorieLoeschenAsync(
        Guid kategorieId,
        Guid ausgefuehrtVonUserId,
        bool istSuperAdmin,
        CancellationToken cancellationToken = default)
    {
        var kategorie = await dbContext.FinanzKategorien
            .FirstOrDefaultAsync(x => x.Id == kategorieId, cancellationToken);
        if (kategorie is null)
        {
            return (false, "La catégorie est introuvable.");
        }

        if (kategorie.IstSystemTyp || kategorie.SystemCode != FinanzSystemCode.Keiner)
        {
            return (false, "Les catégories système ne peuvent pas être supprimées.");
        }

        if (kategorie.SchulID is null && !istSuperAdmin)
        {
            return (false, "Seul un Administrateur peut supprimer une catégorie globale.");
        }

        if (kategorie.SchulID is Guid schulId
            && !await DarfSchuleVerwaltenAsync(ausgefuehrtVonUserId, istSuperAdmin, schulId, cancellationToken))
        {
            return (false, "Vous n'êtes pas autorisé à gérer cette école.");
        }

        if (await dbContext.FinanzBuchungen.AnyAsync(x => x.KategorieID == kategorieId, cancellationToken))
        {
            return (false, "Cette catégorie est déjà utilisée par des écritures et ne peut pas être supprimée.");
        }

        dbContext.FinanzKategorien.Remove(kategorie);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, string.Empty);
    }

    // -------------------- Buchungen --------------------

    /// <summary>
    /// Erfasst eine manuelle Finanzbuchung. Audit-Felder (Erstellzeit, Erfasser)
    /// werden im Hintergrund gesetzt; das Wirkungsdatum darf rückwirkend sein.
    /// </summary>
    public async Task<(bool Succeeded, string ErrorMessage, FinanzBuchung? Buchung)> BuchungErfassenAsync(
        Guid schulId,
        Guid kategorieId,
        decimal betrag,
        DateTime wirkungsdatum,
        Guid erfasstVonUserId,
        string? beschreibungHtml = null,
        bool istSuperAdmin = false,
        bool autorisierungErzwingen = false,
        CancellationToken cancellationToken = default)
    {
        if (autorisierungErzwingen && !await DarfSchuleVerwaltenAsync(erfasstVonUserId, istSuperAdmin, schulId, cancellationToken))
        {
            return (false, "Vous n'êtes pas autorisé à gérer cette école.", null);
        }

        if (betrag <= 0)
        {
            return (false, "Le montant doit être supérieur à 0.", null);
        }

        var kategorie = await dbContext.FinanzKategorien
            .FirstOrDefaultAsync(x => x.Id == kategorieId, cancellationToken);
        if (kategorie is null)
        {
            return (false, "La catégorie sélectionnée est introuvable.", null);
        }

        if (kategorie.SchulID is not null && kategorie.SchulID != schulId)
        {
            return (false, "La catégorie appartient à une autre école.", null);
        }

        if (kategorie.SystemCode != FinanzSystemCode.Keiner)
        {
            return (false, "Les catégories automatiques HALLOAPP ne peuvent pas être saisies manuellement.", null);
        }

        var buchung = new FinanzBuchung
        {
            SchulID = schulId,
            KategorieID = kategorieId,
            Richtung = kategorie.Richtung,
            Betrag = betrag,
            Wirkungsdatum = wirkungsdatum,
            BeschreibungHtml = beschreibungHtml,
            ErfasstVonUserID = erfasstVonUserId
        };

        dbContext.FinanzBuchungen.Add(buchung);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, string.Empty, buchung);
    }

    /// <summary>
    /// Löscht eine manuelle Buchung. Automatisch erzeugte Systembuchungen dürfen
    /// nur vom SuperAdmin korrigiert werden.
    /// </summary>
    public async Task<(bool Succeeded, string ErrorMessage)> BuchungLoeschenAsync(
        Guid buchungId,
        Guid aktuellerUserId,
        bool istSuperAdmin,
        CancellationToken cancellationToken = default)
    {
        var buchung = await dbContext.FinanzBuchungen
            .Include(x => x.Kategorie)
            .FirstOrDefaultAsync(x => x.Id == buchungId, cancellationToken);
        if (buchung is null)
        {
            return (false, "L'écriture est introuvable.");
        }

        var istSystemBuchung = buchung.Kategorie.SystemCode != FinanzSystemCode.Keiner;

        if (istSystemBuchung && !istSuperAdmin)
        {
            return (false, "Les écritures automatiques doivent être corrigées depuis leur paiement d'origine.");
        }

        if (!istSuperAdmin && buchung.ErfasstVonUserID != aktuellerUserId)
        {
            return (false, "Seul l'auteur (ou un Administrateur) peut supprimer cette écriture.");
        }

        dbContext.FinanzBuchungen.Remove(buchung);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, string.Empty);
    }

    /// <summary>
    /// Bucht die Schulfinanz-Effekte eines bezahlten HALLOAPP-Abos:
    /// voller Abo-Preis als Einnahme, Plattform-/SuperAdmin-, Lehrerpool- und ProductOwner-Anteil als Ausgaben.
    /// Der Schulleiter-/Schulanteil bleibt als Netto-Rest in der Schule sichtbar.
    /// </summary>
    public async Task<(bool Succeeded, string ErrorMessage)> HalloAppAboBuchungenErstellenAsync(
        Guid enrollmentId,
        Guid erfasstVonUserId,
        CancellationToken cancellationToken = default)
    {
        var enrollment = await dbContext.LevelEnrollments
            .Include(item => item.Klasse)
            .FirstOrDefaultAsync(item => item.Id == enrollmentId, cancellationToken);
        if (enrollment is null)
        {
            return (false, "L'activation de l'abonnement est introuvable.");
        }

        if (!enrollment.IsPaid || enrollment.PaidAtUtc is null)
        {
            return (false, "L'abonnement HALLOAPP n'a pas encore été payé.");
        }

        if (enrollment.PriceAtEnrollment <= 0m)
        {
            return (false, "Le prix de l'abonnement est invalide.");
        }

        var schoolId = await dbContext.StudentProfiles
            .Where(profile => profile.AppUserId == enrollment.StudentId)
            .Select(profile => (Guid?)profile.SchoolId)
            .FirstOrDefaultAsync(cancellationToken);
        if (schoolId is null)
        {
            return (false, "L'école de l'élève est introuvable.");
        }

        var alreadyBooked = await dbContext.FinanzBuchungen
            .AnyAsync(buchung => buchung.HerkunftReferenzID == enrollment.Id
                && (buchung.Kategorie.SystemCode == FinanzSystemCode.HalloAppAboZahlung
                    || buchung.Kategorie.SystemCode == FinanzSystemCode.HalloAppAboAnteil
                    || buchung.Kategorie.SystemCode == FinanzSystemCode.HalloAppAboLehrerAnteil
                    || buchung.Kategorie.SystemCode == FinanzSystemCode.HalloAppAboProductOwnerAnteil),
                cancellationToken);
        if (alreadyBooked)
        {
            return (true, string.Empty);
        }

        var schulleiterAnteil = enrollment.SchoolLeaderCommissionAtEnrollment;
        var lehrerAnteil = enrollment.TeacherCommissionAtEnrollment;
        var plattformAnteil = enrollment.PriceAtEnrollment - schulleiterAnteil - lehrerAnteil;
        if (plattformAnteil < 0m)
        {
            return (false, "La répartition de l'abonnement dépasse son prix.");
        }

        var zahlungKategorie = await GetOderErstelleSystemKategorieAsync(
            FinanzSystemCode.HalloAppAboZahlung,
            "HALLOAPP-Abo · Paiement élève",
            FinanzRichtung.Einnahme,
            cancellationToken);
        var plattformKategorie = await GetOderErstelleSystemKategorieAsync(
            FinanzSystemCode.HalloAppAboAnteil,
            "HALLOAPP-Abo · Part plateforme",
            FinanzRichtung.Ausgabe,
            cancellationToken);
        var lehrerKategorie = await GetOderErstelleSystemKategorieAsync(
            FinanzSystemCode.HalloAppAboLehrerAnteil,
            "HALLOAPP-Abo · Fonds enseignants",
            FinanzRichtung.Ausgabe,
            cancellationToken);
        var wirkungsdatum = enrollment.PaidAtUtc.Value.Date;
        var beschreibung = $"Abonnement HALLOAPP {enrollment.AboTyp} · {enrollment.Klasse?.Code ?? string.Empty} · Prix total : {enrollment.PriceAtEnrollment:N0} FCFA.";
        dbContext.FinanzBuchungen.Add(new FinanzBuchung
        {
            SchulID = schoolId.Value,
            KategorieID = zahlungKategorie.Id,
            Richtung = FinanzRichtung.Einnahme,
            Betrag = enrollment.PriceAtEnrollment,
            Wirkungsdatum = wirkungsdatum,
            BeschreibungHtml = beschreibung,
            ErfasstVonUserID = erfasstVonUserId,
            HerkunftReferenzID = enrollment.Id
        });

        if (plattformAnteil > 0m)
        {
            dbContext.FinanzBuchungen.Add(new FinanzBuchung
            {
                SchulID = schoolId.Value,
                KategorieID = plattformKategorie.Id,
                Richtung = FinanzRichtung.Ausgabe,
                Betrag = plattformAnteil,
                Wirkungsdatum = wirkungsdatum,
                BeschreibungHtml = beschreibung,
                ErfasstVonUserID = erfasstVonUserId,
                HerkunftReferenzID = enrollment.Id
            });
        }

        if (lehrerAnteil > 0m)
        {
            dbContext.FinanzBuchungen.Add(new FinanzBuchung
            {
                SchulID = schoolId.Value,
                KategorieID = lehrerKategorie.Id,
                Richtung = FinanzRichtung.Ausgabe,
                Betrag = lehrerAnteil,
                Wirkungsdatum = wirkungsdatum,
                BeschreibungHtml = beschreibung,
                ErfasstVonUserID = erfasstVonUserId,
                HerkunftReferenzID = enrollment.Id
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, string.Empty);
    }

    // -------------------- Klassenzyklen --------------------

    /// <summary>
    /// Ermittelt den aktiven (noch nicht beendeten) Durchlauf für Schule + Klasse,
    /// oder null, wenn kein Durchlauf offen ist.
    /// </summary>
    public Task<KursNiveauDurchlauf?> GetAktivenDurchlaufAsync(
        Guid schulId,
        Guid klasseId,
        CancellationToken cancellationToken = default)
        => dbContext.KursNiveauDurchlaeufe
            .Include(x => x.RatenZahlungen)
            .FirstOrDefaultAsync(
                x => x.SchulID == schulId && x.KlasseId == klasseId && x.EndDatum == null,
                cancellationToken);

    /// <summary>
    /// Startet einen neuen Klassenzyklus. Pro Schule + Klasse darf nur ein
    /// aktiver Durchlauf existieren. Der Preis ist vorbelegt und überschreibbar.
    /// </summary>
    public async Task<(bool Succeeded, string ErrorMessage, KursNiveauDurchlauf? Durchlauf)> DurchlaufStartenAsync(
        Guid schulId,
        Guid klasseId,
        decimal niveauPreis,
        DateTime startDatum,
        Guid erstelltVonUserId,
        bool istSuperAdmin = false,
        bool autorisierungErzwingen = false,
        CancellationToken cancellationToken = default)
    {
        if (autorisierungErzwingen && !await DarfSchuleVerwaltenAsync(erstelltVonUserId, istSuperAdmin, schulId, cancellationToken))
        {
            return (false, "Vous n'êtes pas autorisé à gérer cette école.", null);
        }

        if (niveauPreis <= 0)
        {
            return (false, "Le prix de la classe doit être supérieur à 0.", null);
        }

        var istVerwaltbareSchulklasse = await dbContext.Klassen.AnyAsync(
            x => x.Id == klasseId && x.SchoolId == schulId && !x.IstSystemNiveau,
            cancellationToken);
        if (!istVerwaltbareSchulklasse)
        {
            return (false, "Seules les classes de l'école peuvent avoir un cycle.", null);
        }

        var aktiverVorhanden = await dbContext.KursNiveauDurchlaeufe.AnyAsync(
            x => x.SchulID == schulId && x.KlasseId == klasseId && x.EndDatum == null,
            cancellationToken);
        if (aktiverVorhanden)
        {
            return (false, "Un cycle est déjà actif pour cette classe. Veuillez d'abord le clôturer.", null);
        }

        var durchlauf = new KursNiveauDurchlauf
        {
            SchulID = schulId,
            KlasseId = klasseId,
            NiveauPreis = niveauPreis,
            StartDatum = startDatum,
            EndDatum = null,
            ErstelltVonUserID = erstelltVonUserId
        };

        dbContext.KursNiveauDurchlaeufe.Add(durchlauf);

        var klasse = await dbContext.Klassen
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == klasseId && x.SchoolId == schulId, cancellationToken);

        var autoBezahltEnrollments = new List<LevelEnrollment>();
        if (klasse?.AutoAssignHalloAppAboOnCycleStart == true)
        {
            var studentIds = await dbContext.StudentProfiles
                .Where(profile => profile.SchoolId == schulId && profile.CurrentKlasseId == klasseId)
                .Select(profile => profile.AppUserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (studentIds.Count > 0)
            {
                var globalSubscriptionPrice = await dbContext.CommissionSettings
                    .AsNoTracking()
                    .OrderBy(x => x.ErstelltAmUtc)
                    .Select(x => (decimal?)x.GlobalSubscriptionPrice)
                    .FirstOrDefaultAsync(cancellationToken)
                    ?? HalloAppKonstanten.StandardPreisAbo;
                var schule = await dbContext.Schools
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == schulId, cancellationToken);
                var effektiverAboPreis = schule?.EffektiverPreis(globalSubscriptionPrice) ?? globalSubscriptionPrice;

                var commissionSettings = await dbContext.CommissionSettings
                    .AsNoTracking()
                    .OrderBy(x => x.ErstelltAmUtc)
                    .FirstOrDefaultAsync(cancellationToken);
                var schoolLeaderPercent = schule?.SchoolLeaderCommissionPercentOverride
                    ?? commissionSettings?.SchoolLeaderPercent
                    ?? HalloAppKonstanten.StandardProvisionSchulleiterProzent;
                var teacherPercent = schule?.TeacherCommissionPercentOverride
                    ?? commissionSettings?.TeacherPercent
                    ?? HalloAppKonstanten.StandardProvisionLehrerProzent;
                var productOwnerPercent = schule?.ProductOwnerCommissionPercentOverride
                    ?? commissionSettings?.ProductOwnerPercent
                    ?? HalloAppKonstanten.StandardProvisionProductOwnerProzent;
                var schoolLeaderCommission = HalloAppKonstanten.BerechneProvision(
                    effektiverAboPreis,
                    schoolLeaderPercent);
                var teacherCommission = HalloAppKonstanten.BerechneProvision(
                    effektiverAboPreis,
                    teacherPercent);
                var productOwnerCommission = HalloAppKonstanten.BerechneProvision(
                    effektiverAboPreis,
                    productOwnerPercent);

                if (schoolLeaderCommission + teacherCommission + productOwnerCommission > effektiverAboPreis)
                {
                    return (false, "Les commissions configurées dépassent le prix de l'abonnement.", null);
                }

                var existingEnrollments = await dbContext.LevelEnrollments
                    .Where(enrollment => enrollment.KlasseId == klasseId && studentIds.Contains(enrollment.StudentId))
                    .ToListAsync(cancellationToken);
                var existingEnrollmentsByStudent = existingEnrollments.ToDictionary(enrollment => enrollment.StudentId);

                var nowUtc = DateTime.UtcNow;
                foreach (var studentId in studentIds)
                {
                    if (!existingEnrollmentsByStudent.TryGetValue(studentId, out var enrollment))
                    {
                        enrollment = new LevelEnrollment
                        {
                            StudentId = studentId,
                            KlasseId = klasseId,
                            AboTyp = AboTyp.HalloApp,
                            IsUnlocked = true,
                            IsPaid = true,
                            PriceAtEnrollment = effektiverAboPreis,
                            SchoolLeaderCommissionAtEnrollment = schoolLeaderCommission,
                            TeacherCommissionAtEnrollment = teacherCommission,
                            ProductOwnerCommissionAtEnrollment = productOwnerCommission,
                            UnlockedAtUtc = nowUtc,
                            PaidAtUtc = nowUtc,
                            GeaendertAmUtc = nowUtc,
                            ErstelltAmUtc = nowUtc
                        };
                        dbContext.LevelEnrollments.Add(enrollment);
                        autoBezahltEnrollments.Add(enrollment);
                        continue;
                    }

                    if (!enrollment.IsPaid)
                    {
                        enrollment.IsUnlocked = true;
                        enrollment.IsPaid = true;
                        enrollment.AboTyp = AboTyp.HalloApp;
                        enrollment.PriceAtEnrollment = effektiverAboPreis;
                        enrollment.SchoolLeaderCommissionAtEnrollment = schoolLeaderCommission;
                        enrollment.TeacherCommissionAtEnrollment = teacherCommission;
                        enrollment.ProductOwnerCommissionAtEnrollment = productOwnerCommission;
                        enrollment.UnlockedAtUtc ??= nowUtc;
                        enrollment.PaidAtUtc ??= nowUtc;
                        enrollment.GeaendertAmUtc = nowUtc;
                        autoBezahltEnrollments.Add(enrollment);
                    }
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (autoBezahltEnrollments.Count > 0)
        {
            foreach (var enrollment in autoBezahltEnrollments)
            {
                var (ok, error) = await HalloAppAboBuchungenErstellenAsync(enrollment.Id, erstelltVonUserId, cancellationToken);
                if (!ok)
                {
                    return (false, error, null);
                }
            }
        }

        return (true, string.Empty, durchlauf);
    }

    /// <summary>
    /// Beendet einen aktiven Durchlauf. Ist noch Restschuld offen, wird das Beenden
    /// dennoch erlaubt (die UI bestätigt zuvor). Die offene Summe wird automatisch
    /// als Verlust der Schule in der Finanzverwaltung gebucht.
    /// </summary>
    public async Task<(bool Succeeded, string ErrorMessage, decimal OffeneSchuld)> DurchlaufBeendenAsync(
        Guid durchlaufId,
        DateTime endDatum,
        Guid beendetVonUserId = default,
        bool istSuperAdmin = false,
        bool autorisierungErzwingen = false,
        CancellationToken cancellationToken = default)
    {
        var (succeeded, errorMessage, offeneSchuld, _) = await DurchlaufBeendenCoreAsync(
            durchlaufId,
            endDatum,
            beendetVonUserId,
            istSuperAdmin,
            autorisierungErzwingen,
            automatischeHalloAppAbrechnungErstellen: false,
            cancellationToken);
        return (succeeded, errorMessage, offeneSchuld);
    }

    public async Task<(bool Succeeded, string ErrorMessage, decimal OffeneSchuld, string? AutomatischeAbrechnungMessage)> DurchlaufBeendenMitAutomatischerHalloAppAbrechnungAsync(
        Guid durchlaufId,
        DateTime endDatum,
        Guid beendetVonUserId,
        bool istSuperAdmin = false,
        bool autorisierungErzwingen = false,
        CancellationToken cancellationToken = default)
    {
        var (succeeded, errorMessage, offeneSchuld, automatischeAbrechnungMessage) = await DurchlaufBeendenCoreAsync(
            durchlaufId,
            endDatum,
            beendetVonUserId,
            istSuperAdmin,
            autorisierungErzwingen,
            automatischeHalloAppAbrechnungErstellen: true,
            cancellationToken);

        return (succeeded, errorMessage, offeneSchuld, automatischeAbrechnungMessage);
    }

    private async Task<(bool Succeeded, string ErrorMessage, decimal OffeneSchuld, string? AutomatischeAbrechnungMessage)> DurchlaufBeendenCoreAsync(
        Guid durchlaufId,
        DateTime endDatum,
        Guid beendetVonUserId,
        bool istSuperAdmin,
        bool autorisierungErzwingen,
        bool automatischeHalloAppAbrechnungErstellen,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;

            var durchlauf = await dbContext.KursNiveauDurchlaeufe
                .Include(x => x.RatenZahlungen)
                .FirstOrDefaultAsync(x => x.Id == durchlaufId, cancellationToken);
            if (durchlauf is null)
            {
                return (false, "Le cycle est introuvable.", 0m, null);
            }

            if (autorisierungErzwingen && !await DarfSchuleVerwaltenAsync(beendetVonUserId, istSuperAdmin, durchlauf.SchulID, cancellationToken))
            {
                return (false, "Vous n'êtes pas autorisé à gérer cette école.", 0m, null);
            }

            if (automatischeHalloAppAbrechnungErstellen
                && autorisierungErzwingen
                && !await DarfSchulleiterFinanzenVerwaltenAsync(beendetVonUserId, istSuperAdmin, durchlauf.SchulID, cancellationToken))
            {
                return (false, "Seul le directeur de l'école peut déclencher cette facturation HALLOAPP automatique lors de la clôture.", 0m, null);
            }

            if (durchlauf.EndDatum is not null)
            {
                return (false, "Le cycle est déjà clôturé.", 0m, null);
            }

            if (endDatum < durchlauf.StartDatum)
            {
                return (false, "La date de fin ne peut pas être antérieure à la date de début.", 0m, null);
            }

            var offeneSchuld = await BerechneOffeneSchuldAsync(durchlauf, cancellationToken);
            string? automatischeAbrechnungMessage = null;

            if (automatischeHalloAppAbrechnungErstellen)
            {
                var schule = await dbContext.Schools.FirstOrDefaultAsync(x => x.Id == durchlauf.SchulID, cancellationToken);
                if (schule is null)
                {
                    return (false, "L'école est introuvable.", 0m, null);
                }

                var vonUtc = await ErmittleAbrechnungsZeitraumBeginnAsync(schule, cancellationToken);
                var bisUtc = ToInclusiveUtcDayEnd(endDatum);
                var positionen = await LadeOffeneHalloAppAboPositionenAsync(durchlauf.SchulID, vonUtc, bisUtc, cancellationToken);
                if (positionen.Count > 0)
                {
                    var jetztUtc = DateTime.UtcNow;
                    var periode = new Abrechnungsperiode
                    {
                        SchulID = durchlauf.SchulID,
                        VonUtc = vonUtc,
                        BisUtc = bisUtc,
                        AnzahlHalloBasis = positionen.Count,
                        AnzahlHalloPlus = 0,
                        Umsatz = positionen.Sum(position => Math.Max(0m,
                            position.PriceAtEnrollment
                            - position.SchoolLeaderCommissionAtEnrollment
                            - position.TeacherCommissionAtEnrollment)),
                        Status = AbrechnungStatus.Erstellt,
                        IstAbgeschlossen = false,
                        AutomatischDurchKlassenabschlussErstellt = true,
                        AusgeloestDurchDurchlaufId = durchlauf.Id,
                        AutomatischAusgeloestVonUserId = beendetVonUserId,
                        AutomatischErstelltAmUtc = jetztUtc,
                        ErstelltAmUtc = jetztUtc,
                        GeaendertAmUtc = jetztUtc
                    };
                    dbContext.Abrechnungsperioden.Add(periode);
                    await dbContext.SaveChangesAsync(cancellationToken);

                    foreach (var position in positionen)
                    {
                        position.AbgerechnetInPeriodeId = periode.Id;
                        position.GeaendertAmUtc = jetztUtc;
                    }

                    automatischeAbrechnungMessage = $"Facturation HALLOAPP automatique créée pour {positionen.Count} abonnement(s), montant {periode.Umsatz:N0} FCFA. Elle attend la suite du workflow normal.";
                }
            }

            durchlauf.EndDatum = endDatum;

            var lessonsInKlasse = await dbContext.Lessons
                .Where(lesson => lesson.KlasseId == durchlauf.KlasseId)
                .ToListAsync(cancellationToken);
            foreach (var lesson in lessonsInKlasse)
            {
                lesson.IsPublished = false;
                lesson.GeaendertAmUtc = DateTime.UtcNow;
            }

            var examsInKlasse = await dbContext.Exams
                .Where(exam => exam.KlasseId == durchlauf.KlasseId)
                .ToListAsync(cancellationToken);
            foreach (var exam in examsInKlasse)
            {
                exam.IsPublishedToStudents = false;
                exam.GeaendertAmUtc = DateTime.UtcNow;
            }

            if (offeneSchuld > 0m)
            {
                var verlustKategorie = await GetOderErstelleSystemKategorieAsync(
                    FinanzSystemCode.SchulgebuehrVerlust,
                    "Verlust Schulgebühr",
                    FinanzRichtung.Ausgabe,
                    cancellationToken);

                dbContext.FinanzBuchungen.Add(new FinanzBuchung
                {
                    SchulID = durchlauf.SchulID,
                    KategorieID = verlustKategorie.Id,
                    Richtung = FinanzRichtung.Ausgabe,
                    Betrag = offeneSchuld,
                    Wirkungsdatum = endDatum.Date,
                    BeschreibungHtml = $"Frais scolaires impayés lors de la clôture du cycle de classe {durchlauf.Klasse?.Code ?? string.Empty} : {offeneSchuld:N0} FCFA.",
                    ErfasstVonUserID = beendetVonUserId,
                    HerkunftReferenzID = durchlauf.Id
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return (true, string.Empty, offeneSchuld, automatischeAbrechnungMessage);
        });
    }

    // -------------------- Ratenzahlungen --------------------

    /// <summary>
    /// Bucht eine Zahlung (Cash oder Rate) eines eingeschriebenen Schülers und
    /// erzeugt automatisch eine zugehörige Einnahme-Buchung (KursNiveau-Zahlung).
    /// Nur erlaubt, solange der Durchlauf aktiv (nicht beendet) ist.
    /// </summary>
    public async Task<(bool Succeeded, string ErrorMessage, RatenZahlung? Zahlung)> ZahlungBuchenAsync(
        Guid durchlaufId,
        Guid studentId,
        decimal betrag,
        ZahlungsArt zahlungsArt,
        DateTime wirkungsdatum,
        Guid erfasstVonUserId,
        string? beschreibungHtml = null,
        bool istSuperAdmin = false,
        bool autorisierungErzwingen = false,
        CancellationToken cancellationToken = default)
    {
        if (betrag <= 0)
        {
            return (false, "Le montant doit être supérieur à 0.", null);
        }

        var durchlauf = await dbContext.KursNiveauDurchlaeufe
            .FirstOrDefaultAsync(x => x.Id == durchlaufId, cancellationToken);
        if (durchlauf is null)
        {
            return (false, "Le cycle est introuvable.", null);
        }

        if (autorisierungErzwingen && !await DarfSchuleVerwaltenAsync(erfasstVonUserId, istSuperAdmin, durchlauf.SchulID, cancellationToken))
        {
            return (false, "Vous n'êtes pas autorisé à gérer cette école.", null);
        }

        if (durchlauf.EndDatum is not null)
        {
            return (false, "Le cycle est clôturé. Aucun paiement n'est possible avant le démarrage d'un nouveau cycle pour la classe.", null);
        }

        var istEingeschrieben = await dbContext.StudentProfiles.AnyAsync(
            profile => profile.AppUserId == studentId
                       && profile.SchoolId == durchlauf.SchulID
                       && profile.CurrentKlasseId == durchlauf.KlasseId,
            cancellationToken);
        if (!istEingeschrieben)
        {
            return (false, "L'élève n'est pas inscrit dans cette classe.", null);
        }

        var bisherBezahlt = await dbContext.RatenZahlungen
            .Where(zahlung => zahlung.DurchlaufID == durchlaufId && zahlung.StudentId == studentId)
            .SumAsync(zahlung => (decimal?)zahlung.Betrag, cancellationToken)
            ?? 0m;
        var offenerBetrag = Math.Max(0m, durchlauf.NiveauPreis - bisherBezahlt);
        if (zahlungsArt == ZahlungsArt.Cash && betrag != offenerBetrag)
        {
            return (false, "Le paiement cash doit correspondre au montant restant à payer.", null);
        }

        // Kategorie für automatische Einnahme-Verbuchung ermitteln/anlegen.
        var kategorie = await GetOderErstelleSystemKategorieAsync(
            FinanzSystemCode.KursNiveauZahlung,
            "Paiement de classe",
            FinanzRichtung.Einnahme,
            cancellationToken);

        var zahlung = new RatenZahlung
        {
            DurchlaufID = durchlaufId,
            StudentId = studentId,
            Betrag = betrag,
            ZahlungsArt = zahlungsArt,
            Wirkungsdatum = wirkungsdatum,
            BeschreibungHtml = beschreibungHtml,
            ErfasstVonUserID = erfasstVonUserId
        };
        dbContext.RatenZahlungen.Add(zahlung);

        var buchung = new FinanzBuchung
        {
            SchulID = durchlauf.SchulID,
            KategorieID = kategorie.Id,
            Richtung = FinanzRichtung.Einnahme,
            Betrag = betrag,
            Wirkungsdatum = wirkungsdatum,
            BeschreibungHtml = beschreibungHtml,
            ErfasstVonUserID = erfasstVonUserId,
            HerkunftReferenzID = zahlung.Id
        };
        dbContext.FinanzBuchungen.Add(buchung);
        zahlung.FinanzBuchung = buchung;

        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, string.Empty, zahlung);
    }

    /// <summary>
    /// Berechnet die insgesamt offene Restschuld eines Durchlaufs:
    /// (Anzahl eingeschriebener Schüler * NiveauPreis) minus alle gebuchten Zahlungen.
    /// </summary>
    public async Task<decimal> BerechneOffeneSchuldAsync(
        KursNiveauDurchlauf durchlauf,
        CancellationToken cancellationToken = default)
    {
        var anzahlSchueler = (await GetDurchlaufTeilnehmerIdsAsync(durchlauf, cancellationToken)).Count;

        var gezahlt = await dbContext.RatenZahlungen
            .Where(x => x.DurchlaufID == durchlauf.Id)
            .SumAsync(x => (decimal?)x.Betrag, cancellationToken) ?? 0m;

        var soll = anzahlSchueler * durchlauf.NiveauPreis;
        var offen = soll - gezahlt;
        return offen > 0 ? offen : 0m;
    }

    // -------------------- Dashboard / Übersicht --------------------

    /// <summary>
    /// Liefert die gefilterte, sortierte Finanzübersicht inkl. Summen (Einnahmen,
    /// Ausgaben, Saldo). Für SuperAdmin optional über alle Schulen, für SchulLeiter
    /// auf die eigene Schule beschränkt (Filter durch den Aufrufer gesetzt).
    /// </summary>
    public async Task<FinanzUebersicht> GetUebersichtAsync(
        FinanzFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.FinanzBuchungen
            .Include(x => x.Kategorie)
            .Include(x => x.Schule)
            .Include(x => x.ErfasstVon)
            .AsQueryable();

        if (filter.SchulID is not null)
        {
            query = query.Where(x => x.SchulID == filter.SchulID);
        }

        if (filter.Von is not null)
        {
            query = query.Where(x => x.Wirkungsdatum >= filter.Von);
        }

        if (filter.Bis is not null)
        {
            query = query.Where(x => x.Wirkungsdatum <= filter.Bis);
        }

        if (filter.Richtung is not null)
        {
            query = query.Where(x => x.Richtung == filter.Richtung);
        }

        if (filter.KategorieID is not null)
        {
            query = query.Where(x => x.KategorieID == filter.KategorieID);
        }

        query = filter.Sortierung switch
        {
            FinanzSortierung.WirkungsdatumAufsteigend => query.OrderBy(x => x.Wirkungsdatum),
            FinanzSortierung.BetragAbsteigend => query.OrderByDescending(x => x.Betrag),
            FinanzSortierung.BetragAufsteigend => query.OrderBy(x => x.Betrag),
            FinanzSortierung.ErstelltAmAbsteigend => query.OrderByDescending(x => x.ErstelltAmUtc),
            _ => query.OrderByDescending(x => x.Wirkungsdatum)
        };

        var buchungen = await query.ToListAsync(cancellationToken);

        var uebersicht = new FinanzUebersicht
        {
            Zeilen = buchungen.Select(x => new FinanzUebersichtZeile
            {
                BuchungID = x.Id,
                Wirkungsdatum = x.Wirkungsdatum,
                ErstelltAmUtc = x.ErstelltAmUtc,
                SchulName = x.Schule.Name,
                Kategorie = x.Kategorie.Bezeichnung,
                KategorieSystemCode = x.Kategorie.SystemCode,
                Richtung = x.Richtung,
                Betrag = x.Betrag,
                BeschreibungHtml = x.BeschreibungHtml,
                ErfasstVon = x.ErfasstVon?.FullName ?? string.Empty
            }).ToList(),
            SummeEinnahmen = buchungen.Where(x => x.Richtung == FinanzRichtung.Einnahme).Sum(x => x.Betrag),
            SummeAusgaben = buchungen.Where(x => x.Richtung == FinanzRichtung.Ausgabe).Sum(x => x.Betrag)
        };

        return uebersicht;
    }

    // -------------------- UI-Lesemethoden --------------------

    /// <summary>Liefert aktive Schulen, die der Nutzer verwalten darf; SuperAdmins erhalten alle Schulen.</summary>
    public async Task<List<FinanzAuswahl>> GetSchulenAsync(
        Guid userId,
        bool istSuperAdmin,
        CancellationToken cancellationToken = default)
        => await dbContext.Schools
            .Where(x => x.IsActive && (istSuperAdmin || x.UserSchools.Any(link => link.AppUserId == userId && link.CanManageStudents)))
            .OrderBy(x => x.Name)
            .Select(x => new FinanzAuswahl { Id = x.Id, Name = x.Name })
            .ToListAsync(cancellationToken);

    /// <summary>Nur die schuleigenen Klassen einer Schule als Auswahlliste.</summary>
    public async Task<List<FinanzAuswahl>> GetNiveausAsync(Guid schulId, CancellationToken cancellationToken = default)
        => await dbContext.Klassen
            .Where(x => x.SchoolId == schulId && !x.IstSystemNiveau)
            .OrderBy(x => x.Code)
            .Select(x => new FinanzAuswahl { Id = x.Id, Name = x.Code + " – " + x.Title })
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Liefert einen Preisvorschlag für einen neuen Durchlauf: bevorzugt der
    /// Niveau-Preis, ersatzweise der effektive Abo-Preis der Schule.
    /// </summary>
    public async Task<decimal> GetPreisVorschlagAsync(
        Guid schulId,
        Guid klasseId,
        CancellationToken cancellationToken = default)
    {
        var niveauPreis = await dbContext.Klassen
            .Where(x => x.Id == klasseId)
            .Select(x => (decimal?)x.Price)
            .FirstOrDefaultAsync(cancellationToken);

        if (niveauPreis is > 0)
        {
            return niveauPreis.Value;
        }

        var schule = await dbContext.Schools
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == schulId, cancellationToken);
        var globalSubscriptionPrice = await dbContext.CommissionSettings
            .AsNoTracking()
            .OrderBy(x => x.ErstelltAmUtc)
            .Select(x => (decimal?)x.GlobalSubscriptionPrice)
            .FirstOrDefaultAsync(cancellationToken)
            ?? HalloAppKonstanten.StandardPreisAbo;
        return schule?.EffektiverPreis(globalSubscriptionPrice) ?? globalSubscriptionPrice;
    }

    /// <summary>Alle Durchläufe einer Schule (neueste zuerst) als Übersicht mit Zahlungsständen.</summary>
    public async Task<List<DurchlaufUebersicht>> GetDurchlaeufeAsync(
        Guid schulId,
        CancellationToken cancellationToken = default)
    {
        var durchlaeufe = await dbContext.KursNiveauDurchlaeufe
            .Include(x => x.Klasse)
            .Include(x => x.RatenZahlungen)
            .Where(x => x.SchulID == schulId)
            .OrderByDescending(x => x.StartDatum)
            .ToListAsync(cancellationToken);

        var result = new List<DurchlaufUebersicht>();
        foreach (var d in durchlaeufe)
        {
            result.Add(await BaueDurchlaufUebersichtAsync(d, cancellationToken));
        }

        return result;
    }

    /// <summary>Baut die Zahlungsstand-Übersicht für einen einzelnen Durchlauf.</summary>
    public async Task<DurchlaufUebersicht?> GetDurchlaufUebersichtAsync(
        Guid durchlaufId,
        CancellationToken cancellationToken = default)
    {
        var d = await dbContext.KursNiveauDurchlaeufe
            .Include(x => x.Klasse)
            .Include(x => x.RatenZahlungen)
            .FirstOrDefaultAsync(x => x.Id == durchlaufId, cancellationToken);
        return d is null ? null : await BaueDurchlaufUebersichtAsync(d, cancellationToken);
    }

    /// <summary>
    /// Liefert für einen Kursdurchlauf den vollständigen Finanzbericht: Schulgebühren
    /// aus Ratenzahlungen sowie HALLO-Abo- und Zahlungsstatus aller Teilnehmer.
    /// </summary>
    public async Task<KlassenFinanzbericht?> GetKlassenFinanzberichtAsync(
        Guid durchlaufId,
        CancellationToken cancellationToken = default)
    {
        var durchlauf = await dbContext.KursNiveauDurchlaeufe
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Include(x => x.RatenZahlungen)
            .FirstOrDefaultAsync(x => x.Id == durchlaufId, cancellationToken);
        if (durchlauf is null)
        {
            return null;
        }

        var teilnehmerIds = await GetDurchlaufTeilnehmerIdsAsync(durchlauf, cancellationToken);
        var schueler = await dbContext.Users
            .AsNoTracking()
            .Where(user => teilnehmerIds.Contains(user.Id))
            .Select(user => new { StudentId = user.Id, user.FullName })
            .ToListAsync(cancellationToken);
        var einschreibungen = await dbContext.LevelEnrollments
            .AsNoTracking()
            .Where(enrollment => teilnehmerIds.Contains(enrollment.StudentId) && enrollment.KlasseId == durchlauf.KlasseId)
            .ToDictionaryAsync(enrollment => enrollment.StudentId, cancellationToken);

        var zahlungenNachSchueler = durchlauf.RatenZahlungen
            .GroupBy(zahlung => zahlung.StudentId)
            .ToDictionary(gruppe => gruppe.Key, gruppe => gruppe.Sum(zahlung => zahlung.Betrag));

        return new KlassenFinanzbericht
        {
            DurchlaufId = durchlauf.Id,
            KlasseId = durchlauf.KlasseId,
            KlasseCode = durchlauf.Klasse.Code,
            KlasseTitel = durchlauf.Klasse.Title,
            StartDatum = durchlauf.StartDatum,
            EndDatum = durchlauf.EndDatum,
            SchulgebuehrProSchueler = durchlauf.NiveauPreis,
            Schueler = schueler
                .OrderBy(item => item.FullName)
                .Select(item => new KlassenFinanzberichtSchueler
                {
                    StudentId = item.StudentId,
                    Name = item.FullName,
                    SchulgebuehrSoll = durchlauf.NiveauPreis,
                    SchulgebuehrBezahlt = zahlungenNachSchueler.GetValueOrDefault(item.StudentId),
                    AboTyp = einschreibungen.TryGetValue(item.StudentId, out var enrollment) ? enrollment.AboTyp : AboTyp.HalloApp,
                    HalloAboPreis = enrollment?.PriceAtEnrollment ?? 0m,
                    HalloAboBezahlt = enrollment?.IsPaid ?? false
                })
                .ToList()
        };
    }

    public async Task<DirektorKontostandUebersicht> GetDirektorKontostandUebersichtAsync(
        Guid schulId,
        FinanzFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var finanzUebersicht = await GetUebersichtAsync(new FinanzFilter
        {
            SchulID = schulId
        }, cancellationToken);

        var schulgebuehrenEingenommen = finanzUebersicht.Zeilen
            .Where(zeile => zeile.KategorieSystemCode == FinanzSystemCode.KursNiveauZahlung
                && zeile.Richtung == FinanzRichtung.Einnahme)
            .Sum(zeile => zeile.Betrag);
        var halloAppAboEingenommen = finanzUebersicht.Zeilen
            .Where(zeile => zeile.KategorieSystemCode == FinanzSystemCode.HalloAppAboZahlung
                && zeile.Richtung == FinanzRichtung.Einnahme)
            .Sum(zeile => zeile.Betrag);
        var lehrerAboAnteil = finanzUebersicht.Zeilen
            .Where(zeile => zeile.KategorieSystemCode == FinanzSystemCode.HalloAppAboLehrerAnteil
                && zeile.Richtung == FinanzRichtung.Ausgabe)
            .Sum(zeile => zeile.Betrag);
        var halloAppAboAnteil = finanzUebersicht.Zeilen
            .Where(zeile => zeile.KategorieSystemCode == FinanzSystemCode.HalloAppAboAnteil
                && zeile.Richtung == FinanzRichtung.Ausgabe)
            .Sum(zeile => zeile.Betrag);
        var schulleiterAboAnteil = Math.Max(0m, halloAppAboEingenommen - lehrerAboAnteil - halloAppAboAnteil);
        var sonstigeEinnahmen = finanzUebersicht.Zeilen
            .Where(zeile => zeile.Richtung == FinanzRichtung.Einnahme
                && zeile.KategorieSystemCode is not FinanzSystemCode.KursNiveauZahlung and not FinanzSystemCode.HalloAppAboZahlung)
            .Sum(zeile => zeile.Betrag);
        var betrieblicheAusgaben = finanzUebersicht.Zeilen
            .Where(zeile => zeile.Richtung == FinanzRichtung.Ausgabe
                && zeile.KategorieSystemCode is not FinanzSystemCode.HalloAppAboAnteil
                    and not FinanzSystemCode.HalloAppAboSchulleiterAnteil
                    and not FinanzSystemCode.HalloAppAboLehrerAnteil
                    and not FinanzSystemCode.HalloAppAboProductOwnerAnteil)
            .Sum(zeile => zeile.Betrag);

        var kontostand = new DirektorKontostandUebersicht
        {
            IstKontostand = finanzUebersicht.Zeilen
                .Where(zeile => zeile.KategorieSystemCode != FinanzSystemCode.HalloAppAboProductOwnerAnteil)
                .Sum(zeile => zeile.Richtung == FinanzRichtung.Einnahme ? zeile.Betrag : -zeile.Betrag),
            SchulgebuehrenEingenommen = schulgebuehrenEingenommen,
            SonstigeEinnahmen = sonstigeEinnahmen,
            BetrieblicheAusgaben = betrieblicheAusgaben,
            HalloAppAboEingenommen = halloAppAboEingenommen,
            SchulleiterAboAnteil = schulleiterAboAnteil,
            LehrerAboAnteil = lehrerAboAnteil,
            HalloAppAboAnteil = halloAppAboAnteil
        };

        var aktiveDurchlaeufe = await dbContext.KursNiveauDurchlaeufe
            .AsNoTracking()
            .Where(durchlauf => durchlauf.SchulID == schulId && durchlauf.EndDatum == null)
            .Select(durchlauf => new { durchlauf.KlasseId, durchlauf.NiveauPreis })
            .ToListAsync(cancellationToken);

        if (aktiveDurchlaeufe.Count == 0)
        {
            return kontostand;
        }

        var klasseIds = aktiveDurchlaeufe.Select(d => d.KlasseId).Distinct().ToList();
        var profileSnapshot = await dbContext.StudentProfiles
            .AsNoTracking()
            .Where(profile => profile.SchoolId == schulId
                              && profile.CurrentKlasseId.HasValue
                              && klasseIds.Contains(profile.CurrentKlasseId.Value))
            .Select(profile => new { profile.AppUserId, KlasseId = profile.CurrentKlasseId!.Value })
            .ToListAsync(cancellationToken);

        var studentIds = profileSnapshot.Select(item => item.AppUserId).Distinct().ToList();

        var zahlungen = await dbContext.RatenZahlungen
            .AsNoTracking()
            .Where(zahlung => aktiveDurchlaeufe.Select(d => d.KlasseId).Contains(zahlung.Durchlauf.KlasseId)
                              && zahlung.Durchlauf.SchulID == schulId
                              && zahlung.Durchlauf.EndDatum == null)
            .GroupBy(zahlung => new { zahlung.StudentId, zahlung.Durchlauf.KlasseId })
            .Select(gruppe => new
            {
                gruppe.Key.StudentId,
                KlasseId = gruppe.Key.KlasseId,
                Summe = gruppe.Sum(item => item.Betrag)
            })
            .ToListAsync(cancellationToken);
        var zahlungsLookup = zahlungen.ToDictionary(item => (item.StudentId, item.KlasseId), item => item.Summe);

        var offeneSchulgebuehren = 0m;
        foreach (var eintrag in profileSnapshot)
        {
            var klassePreis = aktiveDurchlaeufe.First(d => d.KlasseId == eintrag.KlasseId).NiveauPreis;
            var bezahltSchulgebuehr = zahlungsLookup.GetValueOrDefault((eintrag.AppUserId, eintrag.KlasseId));
            offeneSchulgebuehren += Math.Max(0m, klassePreis - bezahltSchulgebuehr);
        }

        var offeneAboPositionen = await (
            from enrollment in dbContext.LevelEnrollments.AsNoTracking()
            join profile in dbContext.StudentProfiles.AsNoTracking() on enrollment.StudentId equals profile.AppUserId
            where profile.SchoolId == schulId
                  && klasseIds.Contains(enrollment.KlasseId)
                  && enrollment.IsUnlocked
                  && !enrollment.IsPaid
                  && enrollment.PriceAtEnrollment > 0m
                  && (enrollment.SchoolLeaderCommissionAtEnrollment > 0m || enrollment.TeacherCommissionAtEnrollment > 0m)
            select new { enrollment.Id, enrollment.PriceAtEnrollment })
            .ToListAsync(cancellationToken);

        var offeneAboGebuehren = offeneAboPositionen.Sum(x => x.PriceAtEnrollment);
        var offeneAboAnzahl = offeneAboPositionen.Count;

        kontostand.OffeneSchulgebuehrenAktiveKlassen = offeneSchulgebuehren;
        kontostand.OffeneAboGebuehrenAktiveKlassen = offeneAboGebuehren;
        kontostand.OffeneAboAnzahlAktiveKlassen = offeneAboAnzahl;
        return kontostand;
    }

    private async Task<DurchlaufUebersicht> BaueDurchlaufUebersichtAsync(
        KursNiveauDurchlauf durchlauf,
        CancellationToken cancellationToken)
    {
        var teilnehmerIds = await GetDurchlaufTeilnehmerIdsAsync(durchlauf, cancellationToken);
        var schueler = await dbContext.Users
            .Where(user => teilnehmerIds.Contains(user.Id))
            .Select(user => new { user.Id, user.FullName })
            .ToListAsync(cancellationToken);

        var zahlungenProSchueler = durchlauf.RatenZahlungen
            .GroupBy(x => x.StudentId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Betrag));

        var uebersicht = new DurchlaufUebersicht
        {
            DurchlaufID = durchlauf.Id,
            KlasseId = durchlauf.KlasseId,
            NiveauCode = durchlauf.Klasse.Code,
            NiveauPreis = durchlauf.NiveauPreis,
            StartDatum = durchlauf.StartDatum,
            EndDatum = durchlauf.EndDatum,
            Schueler = schueler.Select(s => new SchuelerZahlungsstand
            {
                StudentId = s.Id,
                Name = s.FullName,
                Soll = durchlauf.NiveauPreis,
                Bezahlt = zahlungenProSchueler.TryGetValue(s.Id, out var b) ? b : 0m
            }).OrderBy(x => x.Name).ToList()
        };

        return uebersicht;
    }

    // -------------------- Helfer --------------------

    private async Task<List<Guid>> GetDurchlaufTeilnehmerIdsAsync(
        KursNiveauDurchlauf durchlauf,
        CancellationToken cancellationToken)
    {
        if (durchlauf.EndDatum is null)
        {
            return await dbContext.StudentProfiles
                .AsNoTracking()
                .Where(profile => profile.SchoolId == durchlauf.SchulID && profile.CurrentKlasseId == durchlauf.KlasseId)
                .Select(profile => profile.AppUserId)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        var entschiedeneStudenten = await dbContext.KlassenAbschlussEntscheidungen
            .AsNoTracking()
            .Where(entscheidung => entscheidung.DurchlaufId == durchlauf.Id)
            .Select(entscheidung => entscheidung.StudentId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (entschiedeneStudenten.Count > 0)
        {
            return entschiedeneStudenten;
        }

        return await (
            from enrollment in dbContext.LevelEnrollments.AsNoTracking()
            join profile in dbContext.StudentProfiles.AsNoTracking() on enrollment.StudentId equals profile.AppUserId
            where enrollment.KlasseId == durchlauf.KlasseId && profile.SchoolId == durchlauf.SchulID
            select enrollment.StudentId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    private async Task<FinanzKategorie> GetOderErstelleSystemKategorieAsync(
        FinanzSystemCode systemCode,
        string bezeichnung,
        FinanzRichtung richtung,
        CancellationToken cancellationToken)
    {
        // System-Kategorien werden global (SchulID == null) gehalten.
        var kategorie = await dbContext.FinanzKategorien
            .FirstOrDefaultAsync(x => x.SystemCode == systemCode && x.SchulID == null, cancellationToken);

        if (kategorie is not null)
        {
            if (kategorie.Bezeichnung != bezeichnung || kategorie.Richtung != richtung)
            {
                kategorie.Bezeichnung = bezeichnung;
                kategorie.Richtung = richtung;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return kategorie;
        }

        kategorie = new FinanzKategorie
        {
            SchulID = null,
            Bezeichnung = bezeichnung,
            Richtung = richtung,
            SystemCode = systemCode,
            IstSystemTyp = true,
            IstAktiv = true
        };
        dbContext.FinanzKategorien.Add(kategorie);
        await dbContext.SaveChangesAsync(cancellationToken);
        return kategorie;
    }
}
