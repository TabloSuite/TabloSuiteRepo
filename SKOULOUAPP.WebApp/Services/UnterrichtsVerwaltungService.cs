using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.WebApp.Data;

namespace SkoulouApp.WebApp.Services;

/// <summary>Ein eigener Context je Operation; keine vom Client behaupteten Rollen.</summary>
public sealed class UnterrichtsVerwaltungService(IDbContextFactory<ApplicationDbContext> factory)
{
    public async Task<SchulDokumentStammdaten> DokumentDatenLadenAsync(Guid benutzerID, Guid schulID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await PruefeSchuleAsync(db, benutzerID, schulID, token);
        return await db.Schools.AsNoTracking().Where(x => x.Id == schulID).Select(x => new SchulDokumentStammdaten
        {
            Slogan = x.Slogan, OffizielleBezeichnung = x.OffizielleBezeichnung, DokumentAdresse = x.DokumentAdresse,
            DokumentTelefon = x.DokumentTelefon, DokumentEmail = x.DokumentEmail, Zulassungsnummer = x.Zulassungsnummer,
            Ministerium = x.Ministerium, RepublikZeile = x.RepublikZeile, StaatsMotto = x.StaatsMotto
        }).SingleAsync(token);
    }

    public async Task DokumentDatenSpeichernAsync(Guid benutzerID, Guid schulID, SchulDokumentStammdaten daten, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await PruefeSchuleAsync(db, benutzerID, schulID, token);
        daten.DokumentEmail = LeerZuNull(daten.DokumentEmail);
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(daten,
            new System.ComponentModel.DataAnnotations.ValidationContext(daten), null, true))
            throw new InvalidOperationException("Vérifiez les coordonnées et la longueur des champs de l'école.");
        var schule = await db.Schools.SingleAsync(x => x.Id == schulID, token);
        schule.Slogan = LeerZuNull(daten.Slogan);
        schule.OffizielleBezeichnung = LeerZuNull(daten.OffizielleBezeichnung);
        schule.DokumentAdresse = LeerZuNull(daten.DokumentAdresse);
        schule.DokumentTelefon = LeerZuNull(daten.DokumentTelefon);
        schule.DokumentEmail = daten.DokumentEmail;
        schule.Zulassungsnummer = LeerZuNull(daten.Zulassungsnummer);
        schule.Ministerium = LeerZuNull(daten.Ministerium);
        schule.RepublikZeile = LeerZuNull(daten.RepublikZeile);
        schule.StaatsMotto = LeerZuNull(daten.StaatsMotto);
        await db.SaveChangesAsync(token);
    }

    public async Task<List<JahresSchuelerZeile>> JahresSchuelerAsync(Guid benutzerID, Guid schulID, Guid schuljahrID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await PruefeSchuleAsync(db, benutzerID, schulID, token);
        return await db.Set<SchuelerJahresZuordnung>().AsNoTracking()
            .Where(x => x.SchulID == schulID && x.SchuljahrID == schuljahrID).OrderBy(x => x.Schueler.FullName)
            .Select(x => new JahresSchuelerZeile(x.SchuelerID, x.Schueler.FullName, x.Klasse.Code, x.IstWiederholer)).ToListAsync(token);
    }

    public async Task SchuljahrSchliessenAsync(Guid benutzerID, Guid schulID, Guid schuljahrID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await PruefeSchuleAsync(db, benutzerID, schulID, token);
        var jahr = await db.Set<Schuljahr>().SingleOrDefaultAsync(x => x.Id == schuljahrID && x.SchulID == schulID, token)
            ?? throw new InvalidOperationException("Cette année scolaire est introuvable.");
        jahr.IstGeschlossen = true;
        await db.SaveChangesAsync(token);
    }

    public async Task<List<SchuelerStammdatenZeile>> SchuelerStammdatenAsync(Guid benutzerID, Guid schulID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await PruefeSchuleAsync(db, benutzerID, schulID, token);
        return await db.StudentProfiles.AsNoTracking().Where(x => x.SchoolId == schulID && !x.IsArchived)
            .OrderBy(x => x.AppUser.FullName).Select(x => new SchuelerStammdatenZeile(x.AppUserId, x.AppUser.FullName,
                x.CurrentKlasse != null ? x.CurrentKlasse.Code : "—", x.Matrikelnummer, x.Geburtsort)).ToListAsync(token);
    }

    public async Task SchuelerStammdatenSpeichernAsync(Guid benutzerID, Guid schulID, Guid schuelerID, string? matrikelnummer,
        string? geburtsort, CancellationToken token = default)
    {
        await UnterrichtTransaktion.AusfuehrenAsync(factory, async db =>
        {
        await PruefeSchuleAsync(db, benutzerID, schulID, token);
        matrikelnummer = LeerZuNull(matrikelnummer)?.ToUpperInvariant();
        geburtsort = LeerZuNull(geburtsort);
        if (matrikelnummer?.Length > 60 || geburtsort?.Length > 150)
            throw new InvalidOperationException("Le matricule est limité à 60 caractères et le lieu de naissance à 150 caractères.");
        var profil = await db.StudentProfiles.SingleOrDefaultAsync(x => x.AppUserId == schuelerID && x.SchoolId == schulID && !x.IsArchived, token)
            ?? throw new InvalidOperationException("Cet élève n'appartient pas à cette école.");
        if (matrikelnummer is not null && await db.StudentProfiles.AnyAsync(x => x.SchoolId == schulID
            && x.AppUserId != schuelerID && x.Matrikelnummer == matrikelnummer, token))
            throw new InvalidOperationException("Ce matricule est déjà utilisé dans cette école.");
        profil.Matrikelnummer = matrikelnummer ?? profil.Matrikelnummer ?? $"EL-{profil.AppUserId:N}";
        profil.Geburtsort = geburtsort;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            throw new InvalidOperationException("Ce matricule est déjà utilisé dans cette école.", ex);
        }
        return true;
        }, token);
    }

    public Task<int> MatrikelnummernErgaenzenAsync(Guid user, Guid schule, CancellationToken token = default)
        => UnterrichtTransaktion.AusfuehrenAsync(factory, async db =>
        {
            await PruefeSchuleAsync(db, user, schule, token);
            var profile = await db.StudentProfiles.Where(x => x.SchoolId == schule && !x.IsArchived && x.Matrikelnummer == null).ToListAsync(token);
            foreach (var profil in profile)
            {
                var nummer = $"EL-{profil.AppUserId:N}";
                if (await db.StudentProfiles.AnyAsync(x => x.SchoolId == schule && x.Matrikelnummer == nummer, token))
                    throw new InvalidOperationException("Un matricule automatique est déjà utilisé. Corrigez le doublon avant de continuer.");
                profil.Matrikelnummer = nummer;
            }
            await db.SaveChangesAsync(token);
            return profile.Count;
        }, token);

    private static string? LeerZuNull(string? wert) => string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();

    public async Task<List<AuswahlEintrag>> SchulenAsync(Guid benutzerID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        if (!await IstVerwalterAsync(db, benutzerID, token)) return [];
        return await db.UserSchools.AsNoTracking()
            .Where(x => x.AppUserId == benutzerID && x.School.IsActive)
            .OrderBy(x => x.School.Name).Select(x => new AuswahlEintrag(x.SchoolId, x.School.Name)).ToListAsync(token);
    }

    public async Task<UnterrichtsUebersicht> LadenAsync(Guid benutzerID, Guid schulID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await PruefeSchuleAsync(db, benutzerID, schulID, token);
        var jahre = await db.Set<Schuljahr>().AsNoTracking().Where(x => x.SchulID == schulID)
            .OrderByDescending(x => x.Beginn).ToListAsync(token);
        var klassen = await db.Klassen.AsNoTracking().Where(x => x.SchoolId == schulID && !x.IstSystemNiveau)
            .OrderBy(x => x.Code).Select(x => new AuswahlEintrag(x.Id, x.Code + " — " + x.Title)).ToListAsync(token);
        var faecher = await db.Faecher.AsNoTracking().Where(x => x.SchoolId == schulID)
            .OrderBy(x => x.Name).Select(x => new AuswahlEintrag(x.Id, x.Name)).ToListAsync(token);
        var lehrer = await db.UserSchools.AsNoTracking().Where(x => x.SchoolId == schulID && !x.AppUser.IsBlocked
            && db.UserRoles.Any(r => r.UserId == x.AppUserId && db.Roles.Any(role => role.Id == r.RoleId && role.Name == Rollen.Lehrer)))
            .OrderBy(x => x.AppUser.FullName).Select(x => new AuswahlEintrag(x.AppUserId, x.AppUser.FullName)).ToListAsync(token);
        var unterricht = await db.Set<UnterrichtsZuordnung>().AsNoTracking().Where(x => x.SchulID == schulID)
            .OrderBy(x => x.Klasse.Code).ThenBy(x => x.Fach.Name)
            .Select(x => new UnterrichtsZeile(x.Id, x.SchuljahrID, x.Klasse.Code, x.Fach.Name, x.Lehrer.FullName, x.Wochenstunden, x.KlasseID)).ToListAsync(token);
        return new(jahre, klassen, faecher, lehrer, unterricht);
    }

    public async Task SchuljahrSpeichernAsync(Guid benutzerID, Guid schulID, string bezeichnung, DateOnly beginn, DateOnly ende,
        CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await PruefeSchuleAsync(db, benutzerID, schulID, token);
        bezeichnung = bezeichnung.Trim();
        if (bezeichnung.Length is < 1 or > 80 || ende <= beginn)
            throw new InvalidOperationException("Saisissez un intitulé et une période valides pour l'année scolaire.");
        if (await db.Set<Schuljahr>().AnyAsync(x => x.SchulID == schulID && (x.Bezeichnung == bezeichnung
            || (x.Beginn <= ende && beginn <= x.Ende)), token))
            throw new InvalidOperationException("Une année scolaire portant ce nom ou couvrant cette période existe déjà.");
        db.Add(new Schuljahr { Id = Guid.NewGuid(), SchulID = schulID, Bezeichnung = bezeichnung, Beginn = beginn, Ende = ende });
        await db.SaveChangesAsync(token);
    }

    public async Task UnterrichtSpeichernAsync(Guid benutzerID, Guid schulID, Guid schuljahrID, Guid klasseID, Guid fachID,
        Guid lehrerID, decimal wochenstunden, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await PruefeSchuleAsync(db, benutzerID, schulID, token);
        await PruefeJahrAsync(db, schulID, schuljahrID, token);
        if (wochenstunden <= 0 || wochenstunden > 168 || decimal.Round(wochenstunden, 2) != wochenstunden)
            throw new InvalidOperationException("Le volume hebdomadaire doit être compris entre 0,01 et 168 heures (deux décimales maximum).");
        if (!await db.Klassen.AnyAsync(x => x.Id == klasseID && x.SchoolId == schulID && !x.IstSystemNiveau, token)
            || !await db.Faecher.AnyAsync(x => x.Id == fachID && x.SchoolId == schulID, token))
            throw new InvalidOperationException("La classe et la matière doivent appartenir à cette école.");
        if (!await db.UserSchools.AnyAsync(x => x.AppUserId == lehrerID && x.SchoolId == schulID && !x.AppUser.IsBlocked, token)
            || !await db.UserRoles.AnyAsync(x => x.UserId == lehrerID && db.Roles.Any(r => r.Id == x.RoleId && r.Name == Rollen.Lehrer), token)
            || !await db.LehrerNiveauZuordnungen.AnyAsync(x => x.LehrerID == lehrerID && x.SchulID == schulID && x.KlasseId == klasseID, token)
            || !await db.LehrerFachZuordnungen.AnyAsync(x => x.LehrerId == lehrerID && x.FachId == fachID, token))
            throw new InvalidOperationException("Choisissez un enseignant actif déjà affecté à cette classe et à cette matière.");
        var eintrag = await db.Set<UnterrichtsZuordnung>().SingleOrDefaultAsync(x => x.SchuljahrID == schuljahrID
            && x.KlasseID == klasseID && x.FachID == fachID && x.LehrerID == lehrerID && x.SchulID == schulID, token);
        if (eintrag is null)
        {
            eintrag = new UnterrichtsZuordnung { Id = Guid.NewGuid(), SchulID = schulID, SchuljahrID = schuljahrID,
                KlasseID = klasseID, FachID = fachID, LehrerID = lehrerID };
            db.Add(eintrag);
        }
        eintrag.Wochenstunden = wochenstunden;
        await db.SaveChangesAsync(token);
    }

    public async Task UnterrichtEntfernenAsync(Guid benutzerID, Guid schulID, Guid zuordnungID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await PruefeSchuleAsync(db, benutzerID, schulID, token);
        var eintrag = await db.Set<UnterrichtsZuordnung>().SingleOrDefaultAsync(x => x.Id == zuordnungID && x.SchulID == schulID, token)
            ?? throw new InvalidOperationException("Cette affectation est introuvable.");
        await PruefeJahrAsync(db, schulID, eintrag.SchuljahrID, token);
        db.Remove(eintrag);
        await db.SaveChangesAsync(token);
    }

    public async Task SchuelerZuordnenAsync(Guid benutzerID, Guid schulID, Guid schuljahrID, Guid klasseID, Guid schuelerID,
        bool istWiederholer, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await PruefeSchuleAsync(db, benutzerID, schulID, token);
        await PruefeJahrAsync(db, schulID, schuljahrID, token);
        if (!await db.StudentProfiles.AnyAsync(x => x.AppUserId == schuelerID && x.SchoolId == schulID && !x.IsArchived
                && x.CurrentKlasseId == klasseID, token)
            || !await db.Klassen.AnyAsync(x => x.Id == klasseID && x.SchoolId == schulID, token))
            throw new InvalidOperationException("L'élève doit appartenir à cette classe dans cette école.");
        var zuordnung = await db.Set<SchuelerJahresZuordnung>().SingleOrDefaultAsync(x => x.SchuljahrID == schuljahrID
            && x.SchuelerID == schuelerID && x.SchulID == schulID, token);
        if (zuordnung is not null && zuordnung.KlasseID != klasseID)
            throw new InvalidOperationException("L'élève possède déjà une classe pour cette année scolaire.");
        if (zuordnung is null)
        {
            zuordnung = new SchuelerJahresZuordnung { Id = Guid.NewGuid(), SchulID = schulID, SchuljahrID = schuljahrID,
                KlasseID = klasseID, SchuelerID = schuelerID };
            db.Add(zuordnung);
        }
        zuordnung.IstWiederholer = istWiederholer;
        await db.SaveChangesAsync(token);
    }

    private static Task<bool> IstVerwalterAsync(ApplicationDbContext db, Guid benutzerID, CancellationToken token)
        => db.Users.AnyAsync(u => u.Id == benutzerID && !u.IsBlocked && db.UserRoles.Any(ur => ur.UserId == u.Id
            && db.Roles.Any(r => r.Id == ur.RoleId && (r.Name == Rollen.SchulLeiter || r.Name == Rollen.Koordinator))), token);

    private static async Task PruefeSchuleAsync(ApplicationDbContext db, Guid benutzerID, Guid schulID, CancellationToken token)
    {
        if (!await IstVerwalterAsync(db, benutzerID, token)
            || !await db.UserSchools.AnyAsync(x => x.AppUserId == benutzerID && x.SchoolId == schulID && x.School.IsActive, token))
            throw new UnauthorizedAccessException("Vous n'êtes pas autorisé à gérer cette école.");
    }

    private static async Task PruefeJahrAsync(ApplicationDbContext db, Guid schulID, Guid schuljahrID, CancellationToken token)
    {
        if (!await db.Set<Schuljahr>().AnyAsync(x => x.Id == schuljahrID && x.SchulID == schulID && !x.IstGeschlossen, token))
            throw new InvalidOperationException("Choisissez une année scolaire ouverte de cette école.");
    }
}

public sealed record AuswahlEintrag(Guid Id, string Bezeichnung);
public sealed record JahresSchuelerZeile(Guid SchuelerID, string Name, string Klasse, bool IstWiederholer);
public sealed record UnterrichtsZeile(Guid Id, Guid SchuljahrID, string Klasse, string Fach, string Lehrer, decimal Wochenstunden, Guid KlasseID);
public sealed record UnterrichtsUebersicht(List<Schuljahr> Schuljahre, List<AuswahlEintrag> Klassen, List<AuswahlEintrag> Faecher,
    List<AuswahlEintrag> Lehrer, List<UnterrichtsZeile> Unterricht);
