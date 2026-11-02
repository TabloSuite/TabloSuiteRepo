using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Interfaces;

namespace SkoulouApp.Core.Services;

public sealed record ZyklusTeilnehmerZeile(Guid Id, string Name, decimal Preis, DateTime? StorniertAmUtc, string? StornoGrund);

/// <summary>
/// Gemeinsamer Teilnehmerpfad für Zyklusstart, Annahme, Import und Klassenzuweisung.
/// Erfasst Verpflichtungen, niemals einen behaupteten Zahlungseingang.
/// Der Aufrufer prüft Verwaltungsrechte und speichert innerhalb seiner Transaktion.
/// </summary>
public sealed class ZyklusTeilnahmeService(ISkoulouAppDbContext db)
{
    public async Task AktiveKlasseErfassenAsync(Guid schulID, Guid klasseID, CancellationToken token = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        var eigeneTransaktion = db.Database.CurrentTransaction is null;
        var versuch = 0;
        await strategy.ExecuteAsync(async () =>
        {
        if (eigeneTransaktion) ZyklusTransaktionsSchutz.WiederholungVorbereiten(db, versuch++);
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token) : null;
        await ZyklusTransaktionsSchutz.SperrenAsync(db, token);
        var zyklus = await db.KursNiveauDurchlaeufe.SingleOrDefaultAsync(x => x.SchulID == schulID && x.KlasseId == klasseID && x.EndDatum == null, token);
        if (zyklus?.PlattformPreisProSchueler is not null)
        {
            await TeilnehmerErgaenzenAsync(zyklus, token);
            await db.SaveChangesAsync(token);
        }
        if (transaction is not null) await transaction.CommitAsync(token);
        });
    }

    public async Task<List<ZyklusTeilnehmerZeile>> ListeAsync(Guid userID, Guid zyklusID, CancellationToken token = default)
    {
        var zyklus = await db.KursNiveauDurchlaeufe.AsNoTracking().SingleOrDefaultAsync(x => x.Id == zyklusID, token)
            ?? throw new InvalidOperationException("Cycle introuvable.");
        var admin = await IstAdminAsync(userID, token);
        var direktion = await db.UserRoles.AnyAsync(x => x.UserId == userID && db.Roles.Any(r => r.Id == x.RoleId
            && (r.Name == Rollen.SchulLeiter || r.Name == Rollen.Koordinator)), token);
        if (!admin && (!direktion || !await new SchulZugangService(db).IstSchuleFreigegebenAsync(zyklus.SchulID, token)
            || !await db.UserSchools.AnyAsync(x => x.AppUserId == userID && x.SchoolId == zyklus.SchulID && x.CanManageStudents
            && !x.AppUser.IsBlocked && x.School.IsActive, token))) throw new UnauthorizedAccessException();
        return await db.LevelEnrollments.AsNoTracking().Where(x => x.DurchlaufId == zyklusID)
            .OrderBy(x => x.Student.FullName).ThenBy(x => x.Id)
            .Select(x => new ZyklusTeilnehmerZeile(x.Id, x.Student.FullName, x.PriceAtEnrollment, x.StorniertAmUtc, x.StornoGrund)).ToListAsync(token);
    }

    private Task<bool> IstAdminAsync(Guid userID, CancellationToken token)
        => db.Users.AnyAsync(u => u.Id == userID && !u.IsBlocked && db.UserRoles.Any(x => x.UserId == userID
            && db.Roles.Any(r => r.Id == x.RoleId && r.Name == Rollen.SuperAdmin)), token);

    public async Task StornierenAsync(Guid userID, Guid teilnahmeID, string grund, CancellationToken token = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        var eigeneTransaktion = db.Database.CurrentTransaction is null;
        var versuch = 0;
        await strategy.ExecuteAsync(async () =>
        {
        if (eigeneTransaktion) ZyklusTransaktionsSchutz.WiederholungVorbereiten(db, versuch++);
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token) : null;
        await ZyklusTransaktionsSchutz.SperrenAsync(db, token);
        if (!await IstAdminAsync(userID, token)) throw new UnauthorizedAccessException("Seul l'administrateur peut annuler une inscription facturable.");
        if (string.IsNullOrWhiteSpace(grund) || grund.Trim().Length > 300)
            throw new InvalidOperationException("Indiquez le motif de l'affectation erronée (300 caractères maximum).");
        var e = await db.LevelEnrollments.Include(x => x.Durchlauf).SingleOrDefaultAsync(x => x.Id == teilnahmeID, token)
            ?? throw new InvalidOperationException("Inscription introuvable.");
        if (e.Durchlauf?.PlattformPreisProSchueler is null || e.Durchlauf.EndDatum != null || e.AbgerechnetInPeriodeId != null)
            throw new InvalidOperationException("Cette inscription est historique ou déjà clôturée/facturée. Une rectification de facturation est nécessaire.");
        if (e.StorniertAmUtc != null) throw new InvalidOperationException("Cette inscription est déjà annulée.");
        e.StorniertAmUtc = DateTime.UtcNow; e.StorniertVonID = userID; e.StornoGrund = grund.Trim();
        // Keine Klassen-/Zugangsänderung: nur die dokumentierte finanzielle Ausnahme.
        await db.SaveChangesAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        });
    }

    public async Task<decimal> PreisErmittelnAsync(Guid schulID, CancellationToken token = default)
    {
        var schule = await db.Schools.AsNoTracking().SingleOrDefaultAsync(x => x.Id == schulID, token)
            ?? throw new InvalidOperationException("L'école est introuvable.");
        var standard = await db.CommissionSettings.AsNoTracking().OrderBy(x => x.ErstelltAmUtc)
            .Select(x => (decimal?)x.GlobalSubscriptionPrice).FirstOrDefaultAsync(token)
            ?? SkoulouAppKonstanten.StandardPreisAbo;
        var preis = schule.EffektiverPreis(standard);
        if (preis <= 0 || decimal.Round(preis, 2) != preis)
            throw new InvalidOperationException("Le tarif SKOULOUAPP fixé par l'administrateur doit être positif et comporter au maximum deux décimales.");
        return preis;
    }

    public async Task<int> TeilnehmerErgaenzenAsync(KursNiveauDurchlauf zyklus, CancellationToken token = default)
    {
        if (zyklus.EndDatum is not null)
            throw new InvalidOperationException("Ce cycle est déjà clôturé.");
        if (zyklus.PlattformPreisProSchueler is not { } preis || preis <= 0 || decimal.Round(preis, 2) != preis)
            throw new InvalidOperationException("Le tarif de ce cycle n'a pas été fixé.");
        if (!await db.Klassen.AnyAsync(x => x.Id == zyklus.KlasseId && x.SchoolId == zyklus.SchulID && !x.IstSystemNiveau, token))
            throw new InvalidOperationException("La classe n'appartient pas à cette école.");

        // Nicht angenommene Selbstregistrierungen sind noch keine Schulteilnehmer.
        // Persönlich gesperrte, bereits angenommene Schüler bleiben dagegen abrechnungsrelevant.
        var schueler = await db.StudentProfiles.AsNoTracking()
            .Where(x => x.SchoolId == zyklus.SchulID && x.CurrentKlasseId == zyklus.KlasseId && !x.IsArchived
                && (!x.AppUser.IsSelfRegistered || !x.AppUser.IsBlocked || x.AufnahmeBestaetigtAmUtc != null))
            .Select(x => x.AppUserId).Distinct().ToListAsync(token);
        var vorhanden = await db.LevelEnrollments.Where(x => x.DurchlaufId == zyklus.Id).Select(x => x.StudentId).ToListAsync(token);
        vorhanden.AddRange(db.LevelEnrollments.Local.Where(x => x.DurchlaufId == zyklus.Id).Select(x => x.StudentId));
        var fehlend = schueler.Except(vorhanden).ToList();
        var alt = await db.LevelEnrollments.Where(x => fehlend.Contains(x.StudentId) && x.KlasseId == zyklus.KlasseId && x.IstAktuell).ToListAsync(token);
        foreach (var e in alt) e.IstAktuell = false;
        // Bestehende gefilterte Unique-Indizes müssen vor dem Einfügen freigegeben werden.
        // Der Aufrufer muss hierfür eine gemeinsame Transaktion verwenden.
        if (alt.Count > 0) await db.SaveChangesAsync(token);
        var jetzt = DateTime.UtcNow;
        foreach (var studentID in fehlend)
        {
            db.LevelEnrollments.Add(new LevelEnrollment
            {
                StudentId = studentID, KlasseId = zyklus.KlasseId, DurchlaufId = zyklus.Id,
                AboTyp = AboTyp.SkoulouApp, IstAktuell = true, IsUnlocked = true,
                IsPaid = false, PaidAtUtc = null, UnlockedAtUtc = jetzt,
                PriceAtEnrollment = preis, SchoolLeaderCommissionAtEnrollment = 0,
                TeacherCommissionAtEnrollment = 0, ProductOwnerCommissionAtEnrollment = 0
            });
        }
        return fehlend.Count;
    }
}
