using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.WebApp.Data;

namespace SkoulouApp.WebApp.Services;

public sealed class BewertungsService(IDbContextFactory<ApplicationDbContext> factory)
{
    public Task<bool> PeriodeSchliessenAsync(Guid user, Guid periodeID, bool schliessen, CancellationToken token = default)
        => UnterrichtTransaktion.AusfuehrenAsync(factory, async db =>
        {
            var periode = await db.Set<BewertungsPeriode>().Include(x => x.Schuljahr).SingleOrDefaultAsync(x => x.Id == periodeID, token)
                ?? throw new InvalidOperationException("Cette période est introuvable.");
            await PruefeVerwalterAsync(db, user, periode.SchulID, token);
            if (periode.Schuljahr.IstGeschlossen) throw new InvalidOperationException("Cette année scolaire est clôturée.");
            periode.IstGeschlossen = schliessen; periode.GeaendertAmUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(token);
            return true;
        }, token);
    public async Task<Guid> PeriodeAnlegenAsync(Guid benutzerID, Guid schulID, Guid jahrID, string titel, BewertungsArt art,
        int reihenfolge, decimal jahresGewicht, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await PruefeVerwalterAsync(db, benutzerID, schulID, token);
        await PruefeJahrAsync(db, schulID, jahrID, token);
        if (!Enum.IsDefined(art) || string.IsNullOrWhiteSpace(titel) || titel.Trim().Length > 120 || reihenfolge < 1
            || (art == BewertungsArt.Trimester && reihenfolge > 3) || jahresGewicht < 0 || jahresGewicht > 100
            || (art != BewertungsArt.Test && jahresGewicht <= 0) || decimal.Round(jahresGewicht, 2) != jahresGewicht)
            throw new InvalidOperationException("Vérifiez le titre, le type, l'ordre et le poids de la période.");
        if (await db.Set<BewertungsPeriode>().AnyAsync(x => x.SchuljahrID == jahrID && (x.Titel == titel.Trim()
            || (art == BewertungsArt.Trimester && x.Art == art && x.Reihenfolge == reihenfolge)), token))
            throw new InvalidOperationException("Cette période existe déjà pour l'année scolaire.");
        var periode = new BewertungsPeriode { Id = Guid.NewGuid(), SchulID = schulID, SchuljahrID = jahrID,
            Titel = titel.Trim(), Art = art, Reihenfolge = reihenfolge, JahresGewicht = art == BewertungsArt.Test ? 0 : jahresGewicht };
        db.Add(periode);
        await db.SaveChangesAsync(token);
        return periode.Id;
    }

    public async Task<Guid> BestandteilAnlegenAsync(Guid benutzerID, Guid periodeID, string bezeichnung, decimal gewicht,
        decimal maximalPunkte, CancellationToken token = default)
    {
        return await UnterrichtTransaktion.AusfuehrenAsync(factory, async db =>
        {
        var periode = await OffenePeriodeAsync(db, periodeID, token);
        await PruefeVerwalterAsync(db, benutzerID, periode.SchulID, token);
        if (string.IsNullOrWhiteSpace(bezeichnung) || bezeichnung.Trim().Length > 80 || gewicht <= 0 || gewicht > 100
            || maximalPunkte <= 0 || maximalPunkte > 10000 || decimal.Round(gewicht, 2) != gewicht || decimal.Round(maximalPunkte, 2) != maximalPunkte)
            throw new InvalidOperationException("Saisissez un intitulé, un poids et un barème valides.");
        if (await db.Set<BewertungsBestandteil>().AnyAsync(x => x.PeriodeID == periodeID && x.Bezeichnung == bezeichnung.Trim(), token))
            throw new InvalidOperationException("Cette composante existe déjà.");
        if (await db.Set<FachNote>().AnyAsync(x => x.Bestandteil.PeriodeID == periodeID, token))
            throw new InvalidOperationException("La structure de la période ne peut plus changer après la saisie des notes.");
        var element = new BewertungsBestandteil { Id = Guid.NewGuid(), PeriodeID = periodeID, Bezeichnung = bezeichnung.Trim(),
            Gewicht = gewicht, MaximalPunkte = maximalPunkte };
        db.Add(element);
        await db.SaveChangesAsync(token);
        return element.Id;
        }, token);
    }

    public async Task<Guid> FachFestlegenAsync(Guid benutzerID, Guid schulID, Guid jahrID, Guid klasseID, Guid fachID,
        decimal koeffizient, CancellationToken token = default)
    {
        return await UnterrichtTransaktion.AusfuehrenAsync(factory, async db =>
        {
        await PruefeVerwalterAsync(db, benutzerID, schulID, token);
        await PruefeJahrAsync(db, schulID, jahrID, token);
        if (koeffizient <= 0 || koeffizient > 100 || decimal.Round(koeffizient, 2) != koeffizient
            || !await db.Klassen.AnyAsync(x => x.Id == klasseID && x.SchoolId == schulID, token)
            || !await db.Faecher.AnyAsync(x => x.Id == fachID && x.SchoolId == schulID, token))
            throw new InvalidOperationException("Choisissez une classe, une matière et un coefficient valides pour cette école.");
        var fach = await db.Set<FachBewertung>().SingleOrDefaultAsync(x => x.SchuljahrID == jahrID && x.KlasseID == klasseID && x.FachID == fachID, token);
        if (fach is null)
        {
            fach = new FachBewertung { Id = Guid.NewGuid(), SchuljahrID = jahrID, KlasseID = klasseID, FachID = fachID };
            db.Add(fach);
        }
        else if (await db.Set<FachNote>().AnyAsync(x => x.FachBewertungID == fach.Id, token))
            throw new InvalidOperationException("Le coefficient est verrouillé dès la première note.");
        fach.Koeffizient = koeffizient;
        if (await db.Set<BulletinAusgabe>().AnyAsync(x => x.Periode.SchuljahrID == jahrID
            && x.JahresZuordnung.KlasseID == klasseID && x.IstVeroeffentlicht, token))
            throw new InvalidOperationException("Les matières de cette classe sont verrouillées tant qu'un bulletin de l'année est publié.");
        await db.SaveChangesAsync(token);
        return fach.Id;
        }, token);
    }

    public async Task NoteSpeichernAsync(Guid benutzerID, Guid bestandteilID, Guid fachBewertungID, Guid jahresZuordnungID,
        decimal punkte, string? bemerkung, CancellationToken token = default)
    {
        await UnterrichtTransaktion.AusfuehrenAsync(factory, async db =>
        {
        var element = await db.Set<BewertungsBestandteil>().Include(x => x.Periode).SingleOrDefaultAsync(x => x.Id == bestandteilID, token)
            ?? throw new InvalidOperationException("Cette composante est introuvable.");
        await OffenePeriodeAsync(db, element.PeriodeID, token);
        var fach = await db.Set<FachBewertung>().SingleOrDefaultAsync(x => x.Id == fachBewertungID, token);
        var schueler = await db.Set<SchuelerJahresZuordnung>().SingleOrDefaultAsync(x => x.Id == jahresZuordnungID, token);
        if (fach is null || schueler is null || fach.SchuljahrID != element.Periode.SchuljahrID
            || schueler.SchuljahrID != fach.SchuljahrID || schueler.KlasseID != fach.KlasseID || schueler.SchulID != element.Periode.SchulID)
            throw new InvalidOperationException("La note doit concerner la même école, année et classe.");
        await PruefeNotenRechtAsync(db, benutzerID, element.Periode.SchulID, fach, token);
        if (await db.Set<BulletinAusgabe>().AnyAsync(x => x.PeriodeID == element.PeriodeID
            && x.JahresZuordnungID == jahresZuordnungID && x.IstVeroeffentlicht, token))
            throw new InvalidOperationException("Retirez le bulletin publié avant de corriger les notes de l'élève.");
        if (punkte < 0 || punkte > element.MaximalPunkte || decimal.Round(punkte, 2) != punkte || bemerkung?.Length > 300)
            throw new InvalidOperationException("La note doit respecter le barème et comporter au maximum deux décimales.");
        var note = await db.Set<FachNote>().SingleOrDefaultAsync(x => x.BestandteilID == bestandteilID
            && x.FachBewertungID == fachBewertungID && x.JahresZuordnungID == jahresZuordnungID, token);
        var altePunkte = note?.Punkte;
        var alteBemerkung = note?.Bemerkung;
        var neueBemerkung = string.IsNullOrWhiteSpace(bemerkung) ? null : bemerkung.Trim();
        if (note is not null && note.Punkte == punkte && note.Bemerkung == neueBemerkung) return true;
        if (note is null)
        {
            note = new FachNote { Id = Guid.NewGuid(), BestandteilID = bestandteilID, FachBewertungID = fachBewertungID, JahresZuordnungID = jahresZuordnungID };
            db.Add(note);
        }
        note.Punkte = punkte;
        note.Bemerkung = neueBemerkung;
        note.BearbeitetVonID = benutzerID;
        note.GeaendertAmUtc = DateTime.UtcNow;
        db.Add(new NotenAenderung { Id = Guid.NewGuid(), FachNoteID = note.Id, FachNote = note,
            BearbeitetVonID = benutzerID, BearbeiterName = await db.Users.Where(x => x.Id == benutzerID).Select(x => x.FullName).SingleAsync(token),
            AltePunkte = altePunkte, NeuePunkte = punkte, AlteBemerkung = alteBemerkung, NeueBemerkung = neueBemerkung });
        await db.SaveChangesAsync(token);
        return true;
        }, token);
    }

    public async Task<List<NotenAenderung>> HistorieAsync(Guid user, Guid bestandteilID, Guid fachID, Guid zuordnungID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var fach = await db.Set<FachBewertung>().Include(x => x.Schuljahr).SingleOrDefaultAsync(x => x.Id == fachID, token)
            ?? throw new InvalidOperationException("Cette matière est introuvable.");
        await PruefeNotenRechtAsync(db, user, fach.Schuljahr.SchulID, fach, token);
        return await db.Set<NotenAenderung>().AsNoTracking().Where(x => x.FachNote.BestandteilID == bestandteilID
            && x.FachNote.FachBewertungID == fachID && x.FachNote.JahresZuordnungID == zuordnungID)
            .OrderByDescending(x => x.ErstelltAmUtc).ThenByDescending(x => x.Id).ToListAsync(token);
    }

    public async Task<BewertungsUebersicht> LadenAsync(Guid benutzerID, Guid schulID, Guid jahrID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        return await LadenAsync(db, benutzerID, schulID, jahrID, token);
    }

    internal static async Task<BewertungsUebersicht> LadenAsync(ApplicationDbContext db, Guid benutzerID,
        Guid schulID, Guid jahrID, CancellationToken token)
    {
        await PruefeMitgliedAsync(db, benutzerID, schulID, token);
        var verwalter = await IstVerwalterAsync(db, benutzerID, token);
        var lehrer = await db.UserRoles.AnyAsync(x => x.UserId == benutzerID && db.Roles.Any(r => r.Id == x.RoleId && r.Name == Rollen.Lehrer), token);
        if (!verwalter && !lehrer) throw new UnauthorizedAccessException("Vous ne pouvez pas accéder à la saisie des notes.");
        var perioden = await db.Set<BewertungsPeriode>().AsNoTracking().Where(x => x.SchulID == schulID && x.SchuljahrID == jahrID)
            .OrderBy(x => x.Reihenfolge).ToListAsync(token);
        var bestandteile = await db.Set<BewertungsBestandteil>().AsNoTracking().Where(x => x.Periode.SchulID == schulID && x.Periode.SchuljahrID == jahrID).ToListAsync(token);
        var faecher = await db.Set<FachBewertung>().AsNoTracking().Where(x => x.Schuljahr.SchulID == schulID && x.SchuljahrID == jahrID
            && (verwalter || db.Set<UnterrichtsZuordnung>().Any(u => u.SchulID == schulID && u.SchuljahrID == jahrID
                && u.KlasseID == x.KlasseID && u.FachID == x.FachID && u.LehrerID == benutzerID)))
            .Select(x => new BewertungsFachZeile(x.Id, x.KlasseID, x.Klasse.Code, x.Fach.Name, x.Koeffizient)).ToListAsync(token);
        var klassen = faecher.Select(x => x.KlasseID).Distinct().ToList();
        var schueler = await db.Set<SchuelerJahresZuordnung>().AsNoTracking().Where(x => x.SchulID == schulID
            && x.SchuljahrID == jahrID && klassen.Contains(x.KlasseID))
            .Select(x => new BewertungsSchuelerZeile(x.Id, x.KlasseID, x.Schueler.FullName)).ToListAsync(token);
        var fachIDs = faecher.Select(x => x.Id).ToList();
        var noten = await db.Set<FachNote>().AsNoTracking().Where(x => fachIDs.Contains(x.FachBewertungID)
            && x.Bestandteil.Periode.SchulID == schulID && x.Bestandteil.Periode.SchuljahrID == jahrID).ToListAsync(token);
        return new(perioden, bestandteile, faecher, schueler, noten);
    }

    private static async Task<BewertungsPeriode> OffenePeriodeAsync(ApplicationDbContext db, Guid id, CancellationToken token)
    {
        var periode = await db.Set<BewertungsPeriode>().Include(x => x.Schuljahr).SingleOrDefaultAsync(x => x.Id == id, token);
        if (periode is null || periode.IstGeschlossen || periode.Schuljahr.IstGeschlossen)
            throw new InvalidOperationException("Cette période est introuvable ou clôturée.");
        return periode;
    }
    private static async Task PruefeJahrAsync(ApplicationDbContext db, Guid schule, Guid jahr, CancellationToken token)
    {
        if (!await db.Set<Schuljahr>().AnyAsync(x => x.Id == jahr && x.SchulID == schule && !x.IstGeschlossen, token))
            throw new InvalidOperationException("Choisissez une année scolaire ouverte de cette école.");
    }
    private static Task<bool> IstVerwalterAsync(ApplicationDbContext db, Guid user, CancellationToken token)
        => db.UserRoles.AnyAsync(x => x.UserId == user && db.Roles.Any(r => r.Id == x.RoleId && (r.Name == Rollen.SchulLeiter || r.Name == Rollen.Koordinator)), token);
    private static async Task PruefeMitgliedAsync(ApplicationDbContext db, Guid user, Guid schule, CancellationToken token)
    {
        if (!await db.UserSchools.AnyAsync(x => x.AppUserId == user && x.SchoolId == schule && x.School.IsActive && !x.AppUser.IsBlocked, token))
            throw new UnauthorizedAccessException("Vous n'êtes pas autorisé pour cette école.");
    }
    private static async Task PruefeVerwalterAsync(ApplicationDbContext db, Guid user, Guid schule, CancellationToken token)
    {
        await PruefeMitgliedAsync(db, user, schule, token);
        if (!await IstVerwalterAsync(db, user, token)) throw new UnauthorizedAccessException("Cette action est réservée à la direction et aux coordinateurs.");
    }
    private static async Task PruefeNotenRechtAsync(ApplicationDbContext db, Guid user, Guid schule, FachBewertung fach, CancellationToken token)
    {
        await PruefeMitgliedAsync(db, user, schule, token);
        if (await IstVerwalterAsync(db, user, token)) return;
        if (!await db.UserRoles.AnyAsync(x => x.UserId == user && db.Roles.Any(r => r.Id == x.RoleId && r.Name == Rollen.Lehrer), token)
            || !await db.Set<UnterrichtsZuordnung>().AnyAsync(x => x.LehrerID == user && x.SchulID == schule
                && x.SchuljahrID == fach.SchuljahrID && x.KlasseID == fach.KlasseID && x.FachID == fach.FachID, token))
            throw new UnauthorizedAccessException("Vous pouvez saisir les notes uniquement pour vos matières et classes.");
    }
}

public sealed record BewertungsFachZeile(Guid Id, Guid KlasseID, string Klasse, string Fach, decimal Koeffizient);
public sealed record BewertungsSchuelerZeile(Guid Id, Guid KlasseID, string Name);
public sealed record BewertungsUebersicht(List<BewertungsPeriode> Perioden, List<BewertungsBestandteil> Bestandteile,
    List<BewertungsFachZeile> Faecher, List<BewertungsSchuelerZeile> Schueler, List<FachNote> Noten);
