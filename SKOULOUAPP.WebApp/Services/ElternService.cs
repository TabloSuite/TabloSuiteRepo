using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.Core.Models.Admin;
using SkoulouApp.Core.Models.Import;
using SkoulouApp.Core.Services;
using SkoulouApp.WebApp.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace SkoulouApp.WebApp.Services;

public sealed record ElternVerknuepfung(Guid ElternID, string Name, string Identifiant, string? Beziehung, bool Gesperrt);
public sealed record SchuelerMitEltern(Guid SchuelerID, string Name, string Klasse, string? ElternTelefon, string? ElternEmail, List<ElternVerknuepfung> Eltern);
public sealed record ElternKonto(Guid ElternID, string Name, string Identifiant, int Kinder);
public sealed record ElternEingabe(string Vorname, string Nachname, string? Identifiant, string? Email, string? Telefon, string? Beziehung);

public sealed record KindBulletin(Guid AusgabeID, string Titel, DateTime Datum, bool Ersetzt);
public sealed record KindAnwesenheit(DateOnly Datum, AnwesenheitsStatus Status, bool Entschuldigt, int Minuten);
public sealed record KindZahlung(Guid ZahlungID, DateTime Datum, decimal Betrag);
public sealed record KindGebuehren(string Klasse, decimal Schulgeld, decimal Bezahlt, decimal Rest, List<KindZahlung> Zahlungen);
public sealed record KindUebersicht(Guid SchuelerID, string Name, string Klasse, string Schule, string? Beziehung,
    List<KindBulletin> Bulletins, int Abwesenheiten, int Entschuldigt, int Verspaetungen, List<KindAnwesenheit> LetzteEintraege,
    List<KindGebuehren> Gebuehren);

/// <summary>
/// Eltern-Zugang (seit 11.11.2026): Die Direktion legt Elternkonten an bzw. verknüpft sie mit Schülern
/// ihrer Schule; Eltern sehen nur verknüpfte Kinder. Alle Prüfungen erfolgen hier, nicht nur in der Oberfläche.
/// </summary>
public sealed class ElternService(IDbContextFactory<ApplicationDbContext> factory, DashboardService dashboard, UserManager<AppUser> userManager)
{
    public const int MaxKinder = 12;

    // ---------------- Direktion ----------------

    private static async Task<Guid> DirektionsSchuleAsync(ApplicationDbContext db, Guid benutzerID, CancellationToken token)
    {
        var rollen = await (from benutzerRolle in db.UserRoles join rolle in db.Roles on benutzerRolle.RoleId equals rolle.Id
                            where benutzerRolle.UserId == benutzerID select rolle.Name).ToListAsync(token);
        if (!rollen.Contains(Rollen.SchulLeiter) && !rollen.Contains(Rollen.Koordinator))
            throw new UnauthorizedAccessException("Seule la direction gère les accès parents.");
        var schule = await db.UserSchools.AsNoTracking()
            .Where(l => l.AppUserId == benutzerID && l.CanManageStudents && !l.IstPlattformZuordnung && !l.AppUser.IsBlocked)
            .Select(l => (Guid?)l.SchoolId).FirstOrDefaultAsync(token);
        return schule ?? throw new UnauthorizedAccessException("Aucun établissement n'est associé à votre compte.");
    }

    private async Task<bool> IstElternkontoDerSchuleAsync(ApplicationDbContext db, Guid elternID, Guid schulID, CancellationToken token)
    {
        if (!await db.UserSchools.AnyAsync(l => l.AppUserId == elternID && l.SchoolId == schulID && !l.IstPlattformZuordnung, token))
            return false;
        var konto = await userManager.FindByIdAsync(elternID.ToString());
        return konto is not null && await userManager.IsInRoleAsync(konto, Rollen.Eltern);
    }

    private static Task<bool> IstSchuelerDerSchuleAsync(ApplicationDbContext db, Guid schuelerID, Guid schulID, CancellationToken token)
        => db.StudentProfiles.AnyAsync(p => p.AppUserId == schuelerID && p.SchoolId == schulID && !p.IsArchived, token);

    public async Task<List<SchuelerMitEltern>> UebersichtAsync(Guid direktorID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var schulID = await DirektionsSchuleAsync(db, direktorID, token);
        var schueler = await db.StudentProfiles.AsNoTracking().Where(p => p.SchoolId == schulID && !p.IsArchived && !p.AppUser.IsBlocked)
            .OrderBy(p => p.AppUser.Nachname).ThenBy(p => p.AppUser.Vorname)
            .Select(p => new { p.AppUserId, p.AppUser.FullName, Klasse = p.CurrentKlasse != null ? p.CurrentKlasse.Code : "—", p.ElternTelefonNummer, p.ElternEmail })
            .ToListAsync(token);
        var links = await db.Set<ElternKind>().AsNoTracking().Where(x => x.SchulID == schulID)
            .Select(x => new { x.SchuelerID, x.ElternID, x.Eltern.FullName, x.Eltern.UserName, x.Beziehung, x.Eltern.IsBlocked }).ToListAsync(token);
        return schueler.Select(s => new SchuelerMitEltern(s.AppUserId, s.FullName, s.Klasse, s.ElternTelefonNummer, s.ElternEmail,
            links.Where(l => l.SchuelerID == s.AppUserId).Select(l => new ElternVerknuepfung(l.ElternID, l.FullName, l.UserName ?? "", l.Beziehung, l.IsBlocked)).ToList())).ToList();
    }

    public async Task<List<ElternKonto>> ElternkontenAsync(Guid direktorID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var schulID = await DirektionsSchuleAsync(db, direktorID, token);
        var elternRolle = await db.Roles.Where(r => r.Name == Rollen.Eltern).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(token);
        if (elternRolle is null) return [];
        return await db.UserSchools.AsNoTracking()
            .Where(l => l.SchoolId == schulID && !l.IstPlattformZuordnung && db.UserRoles.Any(r => r.UserId == l.AppUserId && r.RoleId == elternRolle))
            .OrderBy(l => l.AppUser.FullName)
            .Select(l => new ElternKonto(l.AppUserId, l.AppUser.FullName, l.AppUser.UserName ?? "",
                db.Set<ElternKind>().Count(k => k.ElternID == l.AppUserId)))
            .ToListAsync(token);
    }

    /// <summary>Legt ein Elternkonto in der Schule der Direktion an und verknüpft es mit dem Schüler. Liefert die Startzugangsdaten (einmalig).</summary>
    public async Task<(bool Ok, string Meldung, SchoolUserImportZugang? Zugang)> AnlegenAsync(Guid direktorID, Guid schuelerID, ElternEingabe eingabe, CancellationToken token = default)
    {
        Guid schulID;
        await using (var db = await factory.CreateDbContextAsync(token))
        {
            schulID = await DirektionsSchuleAsync(db, direktorID, token);
            if (!await IstSchuelerDerSchuleAsync(db, schuelerID, schulID, token))
                return (false, "Cet élève n'appartient pas à votre établissement.", null);
        }
        if (string.IsNullOrWhiteSpace(eingabe.Vorname) || string.IsNullOrWhiteSpace(eingabe.Nachname))
            return (false, "Indiquez le prénom et le nom du parent.", null);
        if (eingabe.Beziehung?.Trim().Length > 40)
            return (false, "Le lien de parenté est limité à 40 caractères.", null);

        var modell = new UserFormModel
        {
            Vorname = eingabe.Vorname.Trim(),
            Nachname = eingabe.Nachname.Trim(),
            Identifiant = eingabe.Identifiant?.Trim() ?? string.Empty,
            Email = eingabe.Email?.Trim() ?? string.Empty,
            TelefonNummer = string.IsNullOrWhiteSpace(eingabe.Telefon) ? null : eingabe.Telefon.Trim(),
            RoleName = Rollen.Eltern,
            SchoolIds = [schulID]
        };
        var ergebnis = await dashboard.SaveUserAsync(modell, token);
        if (!ergebnis.Succeeded) return (false, ergebnis.Message, null);

        var eltern = await userManager.FindByNameAsync(modell.Identifiant);
        if (eltern is null) return (false, "Le compte parent créé est introuvable.", null);
        await VerknuepfenAsync(direktorID, eltern.Id, schuelerID, eingabe.Beziehung, token);
        return (true, "Le compte parent a été créé et lié à l'élève.", new SchoolUserImportZugang(modell.Identifiant, modell.Password!));
    }

    public async Task VerknuepfenAsync(Guid direktorID, Guid elternID, Guid schuelerID, string? beziehung, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var schulID = await DirektionsSchuleAsync(db, direktorID, token);
        if (!await IstSchuelerDerSchuleAsync(db, schuelerID, schulID, token))
            throw new InvalidOperationException("Cet élève n'appartient pas à votre établissement.");
        if (!await IstElternkontoDerSchuleAsync(db, elternID, schulID, token))
            throw new InvalidOperationException("Ce compte parent n'appartient pas à votre établissement.");
        if (await db.Set<ElternKind>().AnyAsync(x => x.ElternID == elternID && x.SchuelerID == schuelerID, token))
            return;
        if (await db.Set<ElternKind>().CountAsync(x => x.ElternID == elternID, token) >= MaxKinder)
            throw new InvalidOperationException($"Un compte parent peut être lié à {MaxKinder} élèves au maximum.");
        db.Add(new ElternKind
        {
            Id = Guid.NewGuid(), SchulID = schulID, ElternID = elternID, SchuelerID = schuelerID,
            Beziehung = string.IsNullOrWhiteSpace(beziehung) ? null : beziehung.Trim(), ErstelltVonID = direktorID
        });
        await db.SaveChangesAsync(token);
    }

    public async Task TrennenAsync(Guid direktorID, Guid elternID, Guid schuelerID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var schulID = await DirektionsSchuleAsync(db, direktorID, token);
        var link = await db.Set<ElternKind>().SingleOrDefaultAsync(x => x.SchulID == schulID && x.ElternID == elternID && x.SchuelerID == schuelerID, token);
        if (link is null) return;
        db.Remove(link);
        await db.SaveChangesAsync(token);
    }

    /// <summary>Neues Startpasswort für ein Elternkonto (Eltern ohne E-Mail können « Mot de passe oublié » nicht nutzen).</summary>
    public async Task<SchoolUserImportZugang> NeuesPasswortAsync(Guid direktorID, Guid elternID, CancellationToken token = default)
    {
        await using (var db = await factory.CreateDbContextAsync(token))
        {
            var schulID = await DirektionsSchuleAsync(db, direktorID, token);
            if (!await IstElternkontoDerSchuleAsync(db, elternID, schulID, token))
                throw new InvalidOperationException("Ce compte parent n'appartient pas à votre établissement.");
        }
        var eltern = await userManager.FindByIdAsync(elternID.ToString()) ?? throw new InvalidOperationException("Compte parent introuvable.");
        var passwort = ErstzugangService.PasswortErzeugen();
        var jeton = await userManager.GeneratePasswordResetTokenAsync(eltern);
        var ergebnis = await userManager.ResetPasswordAsync(eltern, jeton, passwort);
        if (!ergebnis.Succeeded) throw new InvalidOperationException(string.Join(" ", ergebnis.Errors.Select(e => e.Description)));
        eltern.ActiveSessionId = Guid.NewGuid(); // laufende Sitzung beenden
        await userManager.UpdateAsync(eltern);
        return new SchoolUserImportZugang(eltern.UserName ?? "", passwort);
    }

    // ---------------- Eltern ----------------

    public static Task<bool> IstElternteilVonAsync(ApplicationDbContext db, Guid elternID, Guid schuelerID, CancellationToken token = default)
        => db.Set<ElternKind>().AnyAsync(x => x.ElternID == elternID && x.SchuelerID == schuelerID, token);

    /// <summary>Übersicht aller verknüpften Kinder: veröffentlichte Bulletins, Anwesenheit des laufenden Schuljahres, Schulgebühren.</summary>
    public async Task<List<KindUebersicht>> KinderAsync(Guid elternID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        if (await db.Users.AsNoTracking().AnyAsync(u => u.Id == elternID && u.IsBlocked, token)) return [];
        var kinder = await db.Set<ElternKind>().AsNoTracking().Where(x => x.ElternID == elternID)
            .Select(x => new { x.SchuelerID, x.SchulID, x.Beziehung, x.Schueler.FullName }).ToListAsync(token);
        var ergebnis = new List<KindUebersicht>();
        foreach (var kind in kinder.OrderBy(k => k.FullName))
        {
            var profil = await db.StudentProfiles.AsNoTracking().Where(p => p.AppUserId == kind.SchuelerID && p.SchoolId == kind.SchulID)
                .Select(p => new { Klasse = p.CurrentKlasse != null ? p.CurrentKlasse.Code : "—", p.CurrentKlasseId, Schule = p.School.Name }).FirstOrDefaultAsync(token);
            if (profil is null) continue;

            var bulletins = await db.Set<BulletinAusgabe>().AsNoTracking()
                .Where(a => a.SchulID == kind.SchulID && a.JahresZuordnung.SchuelerID == kind.SchuelerID && a.IstVeroeffentlicht)
                .OrderByDescending(a => a.ErstelltAmUtc)
                .Select(a => new KindBulletin(a.Id, a.Periode.Titel + " · " + a.Periode.Schuljahr.Bezeichnung, a.ErstelltAmUtc, a.ErsetztDurchID != null))
                .ToListAsync(token);

            var heute = DateOnly.FromDateTime(DateTime.Now);
            var anwesenheit = await db.Set<AnwesenheitsEintrag>().AsNoTracking()
                .Where(e => e.JahresZuordnung.SchuelerID == kind.SchuelerID && e.JahresZuordnung.SchulID == kind.SchulID
                    && e.JahresZuordnung.Schuljahr.Beginn <= heute && e.JahresZuordnung.Schuljahr.Ende >= heute
                    && e.Status != AnwesenheitsStatus.Anwesend)
                .OrderByDescending(e => e.Datum)
                .Select(e => new KindAnwesenheit(e.Datum, e.Status, e.IstEntschuldigt, e.VerspaetungsMinuten))
                .ToListAsync(token);

            var gebuehren = new List<KindGebuehren>();
            var durchlaeufe = await db.KursNiveauDurchlaeufe.AsNoTracking()
                .Where(d => d.SchulID == kind.SchulID && d.EndDatum == null && (d.KlasseId == profil.CurrentKlasseId
                    || db.Set<RatenZahlung>().Any(z => z.DurchlaufID == d.Id && z.StudentId == kind.SchuelerID)))
                .Select(d => new { d.Id, d.NiveauPreis, d.Klasse.Code }).ToListAsync(token);
            foreach (var durchlauf in durchlaeufe)
            {
                var zahlungen = await db.Set<RatenZahlung>().AsNoTracking()
                    .Where(z => z.DurchlaufID == durchlauf.Id && z.StudentId == kind.SchuelerID)
                    .OrderByDescending(z => z.Wirkungsdatum)
                    .Select(z => new KindZahlung(z.Id, z.Wirkungsdatum, z.Betrag)).ToListAsync(token);
                var bezahlt = zahlungen.Sum(z => z.Betrag);
                gebuehren.Add(new KindGebuehren(durchlauf.Code, durchlauf.NiveauPreis, bezahlt, Math.Max(0m, durchlauf.NiveauPreis - bezahlt), zahlungen));
            }

            ergebnis.Add(new KindUebersicht(kind.SchuelerID, kind.FullName, profil.Klasse, profil.Schule, kind.Beziehung, bulletins,
                anwesenheit.Count(a => a.Status == AnwesenheitsStatus.Abwesend),
                anwesenheit.Count(a => a.Status == AnwesenheitsStatus.Abwesend && a.Entschuldigt),
                anwesenheit.Count(a => a.Status == AnwesenheitsStatus.Verspaetet),
                anwesenheit.Take(10).ToList(), gebuehren));
        }
        return ergebnis;
    }
}
