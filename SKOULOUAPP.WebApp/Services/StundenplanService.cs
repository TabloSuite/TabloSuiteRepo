using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.WebApp.Data;

namespace SkoulouApp.WebApp.Services;

public sealed class StundenplanService(IDbContextFactory<ApplicationDbContext> factory)
{
    public Task<Guid> KopierenAsync(Guid user, Guid planID, string titel, DateOnly ab, DateOnly bis, CancellationToken token = default)
        => UnterrichtTransaktion.AusfuehrenAsync(factory, async db =>
        {
            var quelle = await db.Set<Stundenplan>().Include(x => x.Schuljahr).SingleOrDefaultAsync(x => x.Id == planID, token)
                ?? throw new InvalidOperationException("Ce planning est introuvable.");
            await VerwalterPruefenAsync(db, user, quelle.SchulID, token);
            if (quelle.Schuljahr.IstGeschlossen || string.IsNullOrWhiteSpace(titel) || titel.Trim().Length > 120
                || ab < quelle.Schuljahr.Beginn || bis > quelle.Schuljahr.Ende || ab > bis)
                throw new InvalidOperationException("La copie doit rester dans une année scolaire ouverte avec un intitulé valide.");
            var kopie = new Stundenplan { Id = Guid.NewGuid(), SchulID = quelle.SchulID, SchuljahrID = quelle.SchuljahrID,
                KlasseID = quelle.KlasseID, Bezeichnung = titel.Trim(), GueltigAb = ab, GueltigBis = bis };
            db.Add(kopie);
            var eintraege = await db.Set<StundenplanEintrag>().AsNoTracking().Where(x => x.StundenplanID == planID).ToListAsync(token);
            foreach (var e in eintraege) db.Add(new StundenplanEintrag { Id = Guid.NewGuid(), StundenplanID = kopie.Id,
                UnterrichtsZuordnungID = e.UnterrichtsZuordnungID, Wochentag = e.Wochentag, Beginn = e.Beginn, Ende = e.Ende, Raum = e.Raum });
            await db.SaveChangesAsync(token);
            return kopie.Id;
        }, token);

    public async Task<List<WochenstundenZeile>> WochenstundenAsync(Guid user, Guid planID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var plan = await db.Set<Stundenplan>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == planID, token)
            ?? throw new InvalidOperationException("Ce planning est introuvable.");
        await VerwalterPruefenAsync(db, user, plan.SchulID, token);
        var zuordnungen = await db.Set<UnterrichtsZuordnung>().AsNoTracking().Where(x => x.SchulID == plan.SchulID
            && x.SchuljahrID == plan.SchuljahrID && x.KlasseID == plan.KlasseID)
            .Select(x => new { x.Id, x.Fach.Name, Lehrer = x.Lehrer.FullName, x.Wochenstunden }).ToListAsync(token);
        var zeiten = await db.Set<StundenplanEintrag>().AsNoTracking().Where(x => x.StundenplanID == planID).ToListAsync(token);
        return zuordnungen.Select(x => new WochenstundenZeile(x.Name, x.Lehrer, x.Wochenstunden,
            zeiten.Where(e => e.UnterrichtsZuordnungID == x.Id).Sum(e => (decimal)(e.Ende - e.Beginn).TotalMinutes) / 60m)).ToList();
    }
    public async Task<List<Stundenplan>> PlaeneAsync(Guid benutzerID, Guid schulID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await VerwalterPruefenAsync(db, benutzerID, schulID, token);
        return await db.Set<Stundenplan>().AsNoTracking().Where(x => x.SchulID == schulID)
            .OrderByDescending(x => x.GueltigAb).ToListAsync(token);
    }

    public async Task<Guid> AnlegenAsync(Guid benutzerID, Guid schulID, Guid schuljahrID, Guid klasseID,
        string bezeichnung, DateOnly ab, DateOnly bis, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await VerwalterPruefenAsync(db, benutzerID, schulID, token);
        var jahr = await db.Set<Schuljahr>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == schuljahrID && x.SchulID == schulID && !x.IstGeschlossen, token);
        if (jahr is null || ab < jahr.Beginn || bis > jahr.Ende || bis < ab || string.IsNullOrWhiteSpace(bezeichnung) || bezeichnung.Trim().Length > 120)
            throw new InvalidOperationException("Saisissez un intitulé et une période compris dans une année scolaire ouverte.");
        if (!await db.Klassen.AnyAsync(x => x.Id == klasseID && x.SchoolId == schulID && !x.IstSystemNiveau, token))
            throw new InvalidOperationException("Choisissez une classe de cette école.");
        var plan = new Stundenplan { Id = Guid.NewGuid(), SchulID = schulID, SchuljahrID = schuljahrID, KlasseID = klasseID,
            Bezeichnung = bezeichnung.Trim(), GueltigAb = ab, GueltigBis = bis };
        db.Add(plan);
        await db.SaveChangesAsync(token);
        return plan.Id;
    }

    public async Task EintragSpeichernAsync(Guid benutzerID, Guid planID, Guid unterrichtID, DayOfWeek tag,
        TimeOnly beginn, TimeOnly ende, string? raum, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
            var plan = await BearbeitbarenPlanAsync(db, benutzerID, planID, token);
            raum = string.IsNullOrWhiteSpace(raum) ? null : raum.Trim().ToUpperInvariant();
            if (!Enum.IsDefined(tag) || beginn >= ende || raum?.Length > 80)
                throw new InvalidOperationException("Vérifiez le jour, les horaires et la salle (80 caractères maximum).");
            var unterricht = await db.Set<UnterrichtsZuordnung>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == unterrichtID
                && x.SchulID == plan.SchulID && x.SchuljahrID == plan.SchuljahrID && x.KlasseID == plan.KlasseID, token)
                ?? throw new InvalidOperationException("L'enseignement doit appartenir à la classe et à l'année du planning.");
            var eintrag = new StundenplanEintrag { Id = Guid.NewGuid(), StundenplanID = planID, UnterrichtsZuordnungID = unterrichtID,
                Wochentag = tag, Beginn = beginn, Ende = ende, Raum = raum };
            await KonfliktPruefenAsync(db, plan, eintrag, unterricht.LehrerID, token);
            db.Add(eintrag);
            plan.GeaendertAmUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
        });
    }

    public async Task EntfernenAsync(Guid benutzerID, Guid eintragID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var eintrag = await db.Set<StundenplanEintrag>().SingleOrDefaultAsync(x => x.Id == eintragID, token)
            ?? throw new InvalidOperationException("Ce créneau est introuvable.");
        var plan = await BearbeitbarenPlanAsync(db, benutzerID, eintrag.StundenplanID, token);
        db.Remove(eintrag);
        plan.GeaendertAmUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(token);
    }

    public async Task VeroeffentlichenAsync(Guid benutzerID, Guid planID, bool veroeffentlichen, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
            var plan = await db.Set<Stundenplan>().Include(x => x.Schuljahr).SingleOrDefaultAsync(x => x.Id == planID, token)
                ?? throw new InvalidOperationException("Ce planning est introuvable.");
            await VerwalterPruefenAsync(db, benutzerID, plan.SchulID, token);
            if (plan.Schuljahr.IstGeschlossen) throw new InvalidOperationException("Cette année scolaire est clôturée.");
            if (veroeffentlichen)
            {
                if (await db.Set<Stundenplan>().AnyAsync(x => x.Id != planID && x.KlasseID == plan.KlasseID && x.IstVeroeffentlicht
                    && x.GueltigAb <= plan.GueltigBis && plan.GueltigAb <= x.GueltigBis, token))
                    throw new InvalidOperationException("Un autre planning publié couvre déjà cette période pour la classe.");
                var eintraege = await db.Set<StundenplanEintrag>().Include(x => x.UnterrichtsZuordnung).Where(x => x.StundenplanID == planID).ToListAsync(token);
                if (eintraege.Count == 0) throw new InvalidOperationException("Ajoutez au moins un cours avant de publier le planning.");
                foreach (var eintrag in eintraege)
                    await KonfliktPruefenAsync(db, plan, eintrag, eintrag.UnterrichtsZuordnung.LehrerID, token);
            }
            plan.IstVeroeffentlicht = veroeffentlichen;
            await db.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
        });
    }

    public async Task<List<StundenplanZeile>> LesenAsync(Guid benutzerID, Guid schulID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        if (!await new SkoulouApp.Core.Services.SchulZugangService(db).IstSchuleFreigegebenAsync(schulID, token))
            throw new UnauthorizedAccessException(SkoulouApp.Core.Services.SchulZugangService.GesperrtHinweis);
        if (!await db.Users.AnyAsync(x => x.Id == benutzerID && !x.IsBlocked, token)
            || !await db.UserSchools.AnyAsync(x => x.AppUserId == benutzerID && x.SchoolId == schulID && x.School.IsActive, token))
            throw new UnauthorizedAccessException("Ce planning n'est pas accessible.");
        var rollen = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
            where ur.UserId == benutzerID select r.Name).ToListAsync(token);
        var verwalter = rollen.Contains(Rollen.SchulLeiter) || rollen.Contains(Rollen.Koordinator);
        var lehrer = rollen.Contains(Rollen.Lehrer);
        var schueler = rollen.Contains(Rollen.Schueler);
        return await db.Set<StundenplanEintrag>().AsNoTracking().Where(x => x.Stundenplan.SchulID == schulID
            && (verwalter || (x.Stundenplan.IstVeroeffentlicht &&
                ((lehrer && x.UnterrichtsZuordnung.LehrerID == benutzerID)
                || (schueler && db.Set<SchuelerJahresZuordnung>().Any(s => s.SchuelerID == benutzerID
                    && s.SchulID == schulID && s.SchuljahrID == x.Stundenplan.SchuljahrID && s.KlasseID == x.Stundenplan.KlasseID))))))
            .OrderBy(x => x.Wochentag).ThenBy(x => x.Beginn)
            .Select(x => new StundenplanZeile(x.Id, x.StundenplanID, x.Stundenplan.Bezeichnung, x.Stundenplan.Klasse.Code,
                x.UnterrichtsZuordnung.Fach.Name, x.UnterrichtsZuordnung.Lehrer.FullName, x.Wochentag, x.Beginn, x.Ende, x.Raum,
                x.Stundenplan.GueltigAb, x.Stundenplan.GueltigBis, x.Stundenplan.IstVeroeffentlicht)).ToListAsync(token);
    }

    private static async Task<Stundenplan> BearbeitbarenPlanAsync(ApplicationDbContext db, Guid benutzerID, Guid planID, CancellationToken token)
    {
        var plan = await db.Set<Stundenplan>().Include(x => x.Schuljahr).SingleOrDefaultAsync(x => x.Id == planID, token)
            ?? throw new InvalidOperationException("Ce planning est introuvable.");
        await VerwalterPruefenAsync(db, benutzerID, plan.SchulID, token);
        if (plan.IstVeroeffentlicht || plan.Schuljahr.IstGeschlossen)
            throw new InvalidOperationException("Retirez la publication avant de modifier un planning d'une année ouverte.");
        return plan;
    }

    private static async Task KonfliktPruefenAsync(ApplicationDbContext db, Stundenplan plan, StundenplanEintrag eintrag, Guid lehrerID, CancellationToken token)
    {
        // Andere Entwürfe blockieren nicht. Gleiche Lehrkraft wird auch schulübergreifend geprüft, ohne fremde Daten offenzulegen.
        var konflikt = await db.Set<StundenplanEintrag>().AnyAsync(x => x.Id != eintrag.Id && x.Wochentag == eintrag.Wochentag
            && x.Beginn < eintrag.Ende && eintrag.Beginn < x.Ende
            && (x.StundenplanID == plan.Id || x.Stundenplan.IstVeroeffentlicht)
            && x.Stundenplan.GueltigAb <= plan.GueltigBis && plan.GueltigAb <= x.Stundenplan.GueltigBis
            && (x.UnterrichtsZuordnung.LehrerID == lehrerID || x.Stundenplan.KlasseID == plan.KlasseID
                || (eintrag.Raum != null && x.Raum == eintrag.Raum && x.Stundenplan.SchulID == plan.SchulID)), token);
        if (konflikt) throw new InvalidOperationException("Ce créneau chevauche un cours pour l'enseignant, la classe ou la salle.");
    }

    private static async Task VerwalterPruefenAsync(ApplicationDbContext db, Guid benutzerID, Guid schulID, CancellationToken token)
    {
        if (!await new SkoulouApp.Core.Services.SchulZugangService(db).IstSchuleFreigegebenAsync(schulID, token))
            throw new UnauthorizedAccessException(SkoulouApp.Core.Services.SchulZugangService.GesperrtHinweis);
        if (!await db.UserSchools.AnyAsync(x => x.AppUserId == benutzerID && x.SchoolId == schulID && x.School.IsActive && !x.AppUser.IsBlocked, token)
            || !await db.UserRoles.AnyAsync(x => x.UserId == benutzerID && db.Roles.Any(r => r.Id == x.RoleId
                && (r.Name == Rollen.SchulLeiter || r.Name == Rollen.Koordinator)), token))
            throw new UnauthorizedAccessException("Vous n'êtes pas autorisé à gérer le planning de cette école.");
    }
}

public sealed record StundenplanZeile(Guid Id, Guid PlanID, string Plan, string Klasse, string Fach, string Lehrer,
    DayOfWeek Wochentag, TimeOnly Beginn, TimeOnly Ende, string? Raum, DateOnly GueltigAb, DateOnly GueltigBis, bool IstVeroeffentlicht);
public sealed record WochenstundenZeile(string Fach, string Lehrer, decimal Soll, decimal Ist);
