using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Interfaces;
using SkoulouApp.Core.Models.Billing;
using Microsoft.EntityFrameworkCore;

namespace SkoulouApp.Core.Services;

/// <summary>
/// Berechnet die Provisionsverteilung aus bezahlten SKOULOUAPP-Abo-Freischaltungen.
///
/// Aus jedem bezahlten Abo entstehen schulweit aggregierte Anteile für Schule,
/// Lehrerpool, ProductOwner und Admin/Betreiber. Bei alten Daten ohne Snapshot
/// greifen die bestätigten Standardprozente von 4 % (Schule) und 8 % (Lehrerpool).
///
/// Sichtbarkeit (durch die UI vorgelagert geprüft):
/// - Lehrer sehen den gesamten Lehrerpool ihrer zugeordneten Schule(n).
/// - Schulleiter sehen Schulanteil und Lehrerpool ihrer Schule(n).
/// - ProductOwner sehen ihren ProductOwner-Anteil der zugewiesenen Schule(n).
/// - SuperAdmin sieht alle Anteile.
/// </summary>
public class ProvisionsService(ISkoulouAppDbContext dbContext)
{
    /// <summary>Lädt die zentrale Konfiguration oder die bestätigten Standardwerte.</summary>
    public async Task<CommissionSettingsModel> GetCommissionSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await dbContext.CommissionSettings
            .AsNoTracking()
            .OrderBy(x => x.ErstelltAmUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return new CommissionSettingsModel
        {
            ReferencePrice = settings?.GlobalSubscriptionPrice ?? SkoulouAppKonstanten.KommissionsBasisPreis,
            GlobalSubscriptionPrice = settings?.GlobalSubscriptionPrice ?? SkoulouAppKonstanten.StandardPreisAbo,
            SchoolLeaderPercent = 0m,
            TeacherPercent = 0m,
            ProductOwnerPercent = settings?.ProductOwnerPercent ?? SkoulouAppKonstanten.StandardProvisionProductOwnerProzent
        };
    }

    /// <summary>Speichert die zentrale Kommissionskonfiguration.</summary>
    public async Task SaveCommissionSettingsAsync(CommissionSettingsModel model, Guid benutzerID, CancellationToken cancellationToken = default)
    {
        if (!await dbContext.Users.AnyAsync(u => u.Id == benutzerID && !u.IsBlocked
            && dbContext.UserRoles.Any(ur => ur.UserId == u.Id && dbContext.Roles.Any(r => r.Id == ur.RoleId && r.Name == Rollen.SuperAdmin)), cancellationToken))
            throw new UnauthorizedAccessException("Seul l'administrateur peut modifier le tarif général.");
        if (model.SchoolLeaderPercent < 0m
            || model.GlobalSubscriptionPrice <= 0m
            || decimal.Round(model.GlobalSubscriptionPrice, 2) != model.GlobalSubscriptionPrice
            || model.TeacherPercent < 0m
            || model.ProductOwnerPercent < 0m
            || model.SchoolLeaderPercent + model.TeacherPercent + model.ProductOwnerPercent > 100m)
        {
            throw new InvalidOperationException("La somme des commissions ne peut pas dépasser 100 %.");
        }

        var settings = await dbContext.CommissionSettings.OrderBy(x => x.ErstelltAmUtc).FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new CommissionSettings();
            dbContext.CommissionSettings.Add(settings);
        }

        settings.GlobalSubscriptionPrice = model.GlobalSubscriptionPrice;
        // Historische Konfiguration bleibt erhalten; neue Zyklen verwenden keine
        // Schul-/Lehreranteile. Das Preisformular darf alte Belege nicht umdeuten.
        settings.ProductOwnerPercent = model.ProductOwnerPercent;
        settings.GeaendertAmUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Liefert die Provisions-Übersicht für eine konkrete (abgerechnete) Periode.
    /// </summary>
    public async Task<ProvisionsUebersicht> GetPeriodeUebersichtAsync(
        Guid periodeId, CancellationToken cancellationToken = default)
    {
        var periode = await dbContext.Abrechnungsperioden
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == periodeId, cancellationToken)
            ?? throw new InvalidOperationException("La facturation est introuvable.");

        var schule = await dbContext.Schools
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == periode.SchulID, cancellationToken);

        var positionen = await dbContext.LevelEnrollments
            .Where(x => x.AbgerechnetInPeriodeId == periodeId)
            .ToListAsync(cancellationToken);

        var anteile = await BerechneAnteileAsync(positionen, cancellationToken);
        var uebersicht = Aggregiere(anteile);
        SetzeAboTypZaehler(uebersicht, positionen);
        uebersicht.SchulID = periode.SchulID;
        uebersicht.SchulName = schule?.Name ?? string.Empty;
        uebersicht.PeriodeID = periode.Id;
        uebersicht.VonUtc = periode.VonUtc;
        uebersicht.BisUtc = periode.BisUtc;
        return uebersicht;
    }

    /// <summary>
    /// Liefert die noch offene Provisions-Übersicht einer Schule
    /// (bezahlte, aber noch nicht abgerechnete Freischaltungen).
    /// </summary>
    public async Task<ProvisionsUebersicht> GetOffeneUebersichtAsync(
        Guid schulId, CancellationToken cancellationToken = default)
    {
        var schule = await dbContext.Schools
            .FirstOrDefaultAsync(x => x.Id == schulId, cancellationToken)
            ?? throw new InvalidOperationException("L'école est introuvable.");

        var studentIds = await dbContext.StudentProfiles
            .Where(x => x.SchoolId == schulId)
            .Select(x => x.AppUserId)
            .ToListAsync(cancellationToken);

        var positionen = await dbContext.LevelEnrollments
            .Where(x => studentIds.Contains(x.StudentId)
                && x.IsPaid
                && x.PriceAtEnrollment > 0m
                && (x.SchoolLeaderCommissionAtEnrollment > 0m || x.TeacherCommissionAtEnrollment > 0m)
                && x.AbgerechnetInPeriodeId == null)
            .ToListAsync(cancellationToken);

        var anteile = await BerechneAnteileAsync(positionen, cancellationToken);

        var uebersicht = Aggregiere(anteile);
        SetzeAboTypZaehler(uebersicht, positionen);
        uebersicht.SchulID = schulId;
        uebersicht.SchulName = schule.Name;
        return uebersicht;
    }

    /// <summary>
    /// Liefert für einen Lehrer die schulweiten Lehrerpool-Übersichten je
    /// abgeschlossener Abrechnungsperiode seiner zugeordneten Schule(n).
    /// </summary>
    public async Task<List<ProvisionsUebersicht>> GetLehrerUebersichtenAsync(
        Guid lehrerId, CancellationToken cancellationToken = default)
    {
        var assignedSchoolIds = await dbContext.UserSchools
            .AsNoTracking()
            .Where(link => link.AppUserId == lehrerId)
            .Select(link => link.SchoolId)
            .ToListAsync(cancellationToken);
        if (assignedSchoolIds.Count == 0)
        {
            return [];
        }

        var perioden = await dbContext.Abrechnungsperioden
            .AsNoTracking()
            .Where(x => x.IstAbgeschlossen && assignedSchoolIds.Contains(x.SchulID))
            .OrderByDescending(x => x.BisUtc)
            .ToListAsync(cancellationToken);

        var schulNamen = await dbContext.Schools
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var ergebnis = new List<ProvisionsUebersicht>();
        foreach (var periode in perioden)
        {
            var positionen = await dbContext.LevelEnrollments
                .AsNoTracking()
                .Where(enrollment => enrollment.AbgerechnetInPeriodeId == periode.Id)
                .ToListAsync(cancellationToken);
            if (positionen.Count == 0)
            {
                continue;
            }

            var uebersicht = Aggregiere(await BerechneAnteileAsync(positionen, cancellationToken));
            SetzeAboTypZaehler(uebersicht, positionen);
            uebersicht.SchulID = periode.SchulID;
            uebersicht.SchulName = schulNamen.TryGetValue(periode.SchulID, out var name) ? name : string.Empty;
            uebersicht.PeriodeID = periode.Id;
            uebersicht.VonUtc = periode.VonUtc;
            uebersicht.BisUtc = periode.BisUtc;
            uebersicht.SichtbarerBetrag = uebersicht.ProvisionLehrer;
            ergebnis.Add(uebersicht);
        }

        return ergebnis;
    }

    /// <summary>
    /// Liefert die aggregierten ProductOwner-Anteile für die dem ProductOwner
    /// zugewiesenen Schulen.
    /// </summary>
    public async Task<List<ProvisionsUebersicht>> GetProduktOwnerUebersichtenAsync(
        Guid produktOwnerId, CancellationToken cancellationToken = default)
    {
        var assignedSchoolIds = await dbContext.UserSchools
            .AsNoTracking()
            .Where(link => link.AppUserId == produktOwnerId)
            .Select(link => link.SchoolId)
            .ToListAsync(cancellationToken);
        if (assignedSchoolIds.Count == 0)
        {
            return [];
        }

        var perioden = await dbContext.Abrechnungsperioden
            .AsNoTracking()
            .Where(periode => periode.IstAbgeschlossen
                && assignedSchoolIds.Contains(periode.SchulID))
            .OrderByDescending(periode => periode.BisUtc)
            .ToListAsync(cancellationToken);
        var schoolIds = perioden.Select(periode => periode.SchulID).Distinct().ToList();
        var schoolNames = await dbContext.Schools
            .AsNoTracking()
            .Where(school => schoolIds.Contains(school.Id))
            .ToDictionaryAsync(school => school.Id, school => school.Name, cancellationToken);

        var ergebnis = new List<ProvisionsUebersicht>();
        foreach (var periode in perioden)
        {
            var positionen = await dbContext.LevelEnrollments
                .AsNoTracking()
                .Where(enrollment => enrollment.AbgerechnetInPeriodeId == periode.Id)
                .ToListAsync(cancellationToken);
            if (positionen.Count == 0)
            {
                continue;
            }

            var uebersicht = Aggregiere(await BerechneAnteileAsync(positionen, cancellationToken));
            if (uebersicht.ProvisionProductOwner <= 0m)
            {
                continue;
            }

            SetzeAboTypZaehler(uebersicht, positionen);
            uebersicht.SchulID = periode.SchulID;
            uebersicht.SchulName = schoolNames.GetValueOrDefault(periode.SchulID, string.Empty);
            uebersicht.PeriodeID = periode.Id;
            uebersicht.VonUtc = periode.VonUtc;
            uebersicht.BisUtc = periode.BisUtc;
            uebersicht.SichtbarerBetrag = uebersicht.ProvisionProductOwner;
            ergebnis.Add(uebersicht);
        }

        return ergebnis;
    }

    public async Task<(bool Succeeded, string Message)> ProduktOwnerErhaltBestaetigenAsync(
        Guid produktOwnerId,
        Guid periodeId,
        CancellationToken cancellationToken = default)
    {
        _ = produktOwnerId;
        _ = periodeId;
        _ = cancellationToken;
        return (false, "La confirmation de réception n'est plus nécessaire. La commission est gérée par école.");
    }

    public async Task<(bool Succeeded, string Message)> LehrerErhaltBestaetigenAsync(
        Guid lehrerId,
        Guid periodeId,
        CancellationToken cancellationToken = default)
    {
        _ = lehrerId;
        _ = periodeId;
        _ = cancellationToken;
        return (false, "La confirmation de réception n'est plus nécessaire. La part enseignants est gérée par l'école.");
    }

    /// <summary>
    /// Zerlegt jede bezahlte Freischaltung in ihre schulweit aggregierten
    /// Provisionsanteile (Schule, Lehrerpool, ProductOwner, Admin).
    /// </summary>
    private async Task<List<ProvisionsAnteil>> BerechneAnteileAsync(
        List<LevelEnrollment> positionen, CancellationToken cancellationToken)
    {
        var anteile = new List<ProvisionsAnteil>();
        if (positionen.Count == 0)
        {
            return anteile;
        }

        var levelIds = positionen.Select(x => x.KlasseId).Distinct().ToList();
        var levelCodes = await dbContext.Klassen
            .Where(x => levelIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);
        var zyklusIds = positionen.Where(x => x.DurchlaufId.HasValue).Select(x => x.DurchlaufId!.Value).Distinct().ToList();
        var automatischeZyklen = await dbContext.KursNiveauDurchlaeufe.AsNoTracking()
            .Where(x => zyklusIds.Contains(x.Id) && x.PlattformPreisProSchueler != null)
            .Select(x => x.Id).ToHashSetAsync(cancellationToken);

        foreach (var position in positionen)
        {
            var niveauCode = levelCodes.TryGetValue(position.KlasseId, out var code) ? code : string.Empty;
            if (position.DurchlaufId is Guid zyklusID && automatischeZyklen.Contains(zyklusID))
            {
                anteile.Add(new ProvisionsAnteil
                {
                    FreischaltungID = position.Id, SchuelerID = position.StudentId, NiveauCode = niveauCode,
                    AboTyp = position.AboTyp, Empfaenger = ProvisionsEmpfaenger.Admin, Betrag = position.PriceAtEnrollment
                });
                continue;
            }
            var schoolShare = position.SchoolLeaderCommissionAtEnrollment > 0m
                ? position.SchoolLeaderCommissionAtEnrollment
                : SkoulouAppKonstanten.BerechneProvision(position.PriceAtEnrollment, SkoulouAppKonstanten.StandardProvisionSchulleiterProzent);
            var teacherShare = position.TeacherCommissionAtEnrollment > 0m
                ? position.TeacherCommissionAtEnrollment
                : SkoulouAppKonstanten.BerechneProvision(position.PriceAtEnrollment, SkoulouAppKonstanten.StandardProvisionLehrerProzent);
            var productOwnerShare = position.ProductOwnerCommissionAtEnrollment > 0m
                ? position.ProductOwnerCommissionAtEnrollment
                : SkoulouAppKonstanten.BerechneProvision(position.PriceAtEnrollment, SkoulouAppKonstanten.StandardProvisionProductOwnerProzent);
            var adminShare = position.PriceAtEnrollment - schoolShare - teacherShare - productOwnerShare;

            anteile.Add(new ProvisionsAnteil
            {
                FreischaltungID = position.Id,
                SchuelerID = position.StudentId,
                NiveauCode = niveauCode,
                AboTyp = position.AboTyp,
                Empfaenger = ProvisionsEmpfaenger.Schulleiter,
                Betrag = schoolShare
            });

            anteile.Add(new ProvisionsAnteil
            {
                FreischaltungID = position.Id,
                SchuelerID = position.StudentId,
                NiveauCode = niveauCode,
                AboTyp = position.AboTyp,
                Empfaenger = ProvisionsEmpfaenger.Lehrer,
                Betrag = teacherShare
            });

            anteile.Add(new ProvisionsAnteil
            {
                FreischaltungID = position.Id,
                SchuelerID = position.StudentId,
                NiveauCode = niveauCode,
                AboTyp = position.AboTyp,
                Empfaenger = ProvisionsEmpfaenger.ProductOwner,
                Betrag = productOwnerShare
            });

            anteile.Add(new ProvisionsAnteil
            {
                FreischaltungID = position.Id,
                SchuelerID = position.StudentId,
                NiveauCode = niveauCode,
                AboTyp = position.AboTyp,
                Empfaenger = ProvisionsEmpfaenger.Admin,
                Betrag = adminShare
            });
        }

        return anteile;
    }

    /// <summary>
    /// Fasst eine Liste von Einzelanteilen zu einer Übersicht zusammen.
    /// </summary>
    private static ProvisionsUebersicht Aggregiere(List<ProvisionsAnteil> anteile) => new()
    {
        ProvisionSchulleiter = anteile.Where(x => x.Empfaenger == ProvisionsEmpfaenger.Schulleiter).Sum(x => x.Betrag),
        ProvisionLehrer = anteile.Where(x => x.Empfaenger == ProvisionsEmpfaenger.Lehrer).Sum(x => x.Betrag),
        ProvisionAdmin = anteile.Where(x => x.Empfaenger == ProvisionsEmpfaenger.Admin).Sum(x => x.Betrag),
        ProvisionProductOwner = anteile.Where(x => x.Empfaenger == ProvisionsEmpfaenger.ProductOwner).Sum(x => x.Betrag),
        AnzahlPositionen = anteile.Select(x => x.FreischaltungID).Distinct().Count(),
        Anteile = anteile
    };

    private static void SetzeAboTypZaehler(ProvisionsUebersicht uebersicht, List<LevelEnrollment> positionen)
    {
        uebersicht.AnzahlPositionen = positionen.Count(position => position.AboTyp == AboTyp.SkoulouApp);
    }
}
