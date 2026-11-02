using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.WebApp.Data;

namespace SkoulouApp.WebApp.Services;

public sealed class AnwesenheitsService(IDbContextFactory<ApplicationDbContext> factory)
{
    public Task<bool> SpeichernAsync(Guid user, Guid periodeID, Guid zuordnungID, DateOnly datum,
        AnwesenheitsStatus status, bool entschuldigt, int minuten, string? bemerkung, CancellationToken token = default)
        => UnterrichtTransaktion.AusfuehrenAsync(factory, async db =>
        {
            var periode = await db.Set<BewertungsPeriode>().Include(x => x.Schuljahr).SingleOrDefaultAsync(x => x.Id == periodeID, token)
                ?? throw new InvalidOperationException("Cette période est introuvable.");
            await PruefenAsync(db, user, periode.SchulID, token);
            if (await db.Set<BulletinAbschluss>().AnyAsync(x => x.PeriodeID == periodeID
                && db.Set<SchuelerJahresZuordnung>().Any(s => s.Id == zuordnungID && s.KlasseID == x.KlasseID), token))
                throw new InvalidOperationException("Les présences de cette classe sont verrouillées après publication définitive.");
            if (periode.IstGeschlossen || periode.Schuljahr.IstGeschlossen || datum < periode.Schuljahr.Beginn || datum > periode.Schuljahr.Ende
                || !Enum.IsDefined(status) || minuten < 0 || minuten > 1440 || bemerkung?.Length > 300
                || !await db.Set<SchuelerJahresZuordnung>().AnyAsync(x => x.Id == zuordnungID && x.SchulID == periode.SchulID && x.SchuljahrID == periode.SchuljahrID, token))
                throw new InvalidOperationException("Vérifiez l'élève, la date, le statut et les minutes de retard pour une période ouverte.");
            if (await db.Set<BulletinAusgabe>().AnyAsync(x => x.PeriodeID == periodeID && x.JahresZuordnungID == zuordnungID && x.IstVeroeffentlicht, token))
                throw new InvalidOperationException("Retirez le bulletin publié avant de corriger les présences.");
            var eintrag = await db.Set<AnwesenheitsEintrag>().SingleOrDefaultAsync(x => x.PeriodeID == periodeID && x.JahresZuordnungID == zuordnungID && x.Datum == datum, token);
            if (eintrag is null) { eintrag = new() { Id = Guid.NewGuid(), PeriodeID = periodeID, JahresZuordnungID = zuordnungID, Datum = datum }; db.Add(eintrag); }
            eintrag.Status = status; eintrag.IstEntschuldigt = status != AnwesenheitsStatus.Anwesend && entschuldigt;
            eintrag.VerspaetungsMinuten = status == AnwesenheitsStatus.Verspaetet ? minuten : 0;
            eintrag.Bemerkung = bemerkung?.Trim(); eintrag.BearbeitetVonID = user; eintrag.GeaendertAmUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(token);
            return true;
        }, token);

    public async Task<List<AnwesenheitsEintrag>> LadenAsync(Guid user, Guid schule, Guid periodeID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await PruefenAsync(db, user, schule, token);
        return await db.Set<AnwesenheitsEintrag>().AsNoTracking().Where(x => x.PeriodeID == periodeID && x.Periode.SchulID == schule)
            .OrderByDescending(x => x.Datum).ToListAsync(token);
    }

    private static async Task PruefenAsync(ApplicationDbContext db, Guid user, Guid schule, CancellationToken token)
    {
        if (!await new SkoulouApp.Core.Services.SchulZugangService(db).IstSchuleFreigegebenAsync(schule, token))
            throw new UnauthorizedAccessException(SkoulouApp.Core.Services.SchulZugangService.GesperrtHinweis);
        if (!await db.UserSchools.AnyAsync(x => x.AppUserId == user && x.SchoolId == schule && x.School.IsActive && !x.AppUser.IsBlocked, token)
            || !await db.UserRoles.AnyAsync(x => x.UserId == user && db.Roles.Any(r => r.Id == x.RoleId && (r.Name == Rollen.SchulLeiter || r.Name == Rollen.Koordinator)), token))
            throw new UnauthorizedAccessException("La gestion des présences est réservée à la direction et aux coordinateurs de l'école.");
    }
}
