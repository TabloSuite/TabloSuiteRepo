using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.Core.Services;
using SkoulouApp.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace SkoulouApp.WebApp.Services;

/// <summary>Kennzahlen für das Direktions-Cockpit (eine Schule pro Konto). Finanzwerte nur mit Finanz-Dashboard-Recht.</summary>
public sealed record DirektionsCockpit(
    Guid SchulID,
    string SchulName,
    string? Stadt,
    string? Schuljahr,
    int SchuelerAktiv,
    int AnmeldungenOffen,
    int SchuelerGesperrt,
    int Klassen,
    int Lehrkraefte,
    int AbwesendHeute,
    int VerspaetetHeute,
    bool AnwesenheitHeuteErfasst,
    int BulletinsVeroeffentlicht,
    bool FinanzenSichtbar,
    decimal SchulgebuehrenEingenommen,
    decimal SchulgebuehrenOffen,
    decimal Kontostand,
    int LaufendeZyklen);

public sealed class DirektionsCockpitService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    /// <summary>Liefert das Cockpit der Schule des Kontos oder <c>null</c>, wenn das Konto keine Direktionsschule hat.</summary>
    public async Task<DirektionsCockpit?> LadeAsync(Guid benutzerID, DateOnly heute, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var rollen = await (from benutzerRolle in db.UserRoles.AsNoTracking()
                            join rolle in db.Roles.AsNoTracking() on benutzerRolle.RoleId equals rolle.Id
                            where benutzerRolle.UserId == benutzerID
                            select rolle.Name).ToListAsync(cancellationToken);
        if (!rollen.Contains(Rollen.SchulLeiter) && !rollen.Contains(Rollen.Koordinator))
            return null;

        // Ein Konto = eine Schule: die (einzige) verwaltete Schule des Kontos.
        var schule = await db.UserSchools.AsNoTracking()
            .Where(link => link.AppUserId == benutzerID && link.CanManageStudents && !link.IstPlattformZuordnung && !link.AppUser.IsBlocked)
            .Select(link => new { link.School.Id, link.School.Name, link.School.City })
            .FirstOrDefaultAsync(cancellationToken);
        if (schule is null)
            return null;

        var lehrerRolleID = await db.Roles.AsNoTracking().Where(rolle => rolle.Name == Rollen.Lehrer).Select(rolle => (Guid?)rolle.Id).FirstOrDefaultAsync(cancellationToken);
        var profile = db.StudentProfiles.AsNoTracking().Where(profil => profil.SchoolId == schule.Id && !profil.IsArchived);
        var schuelerAktiv = await profile.CountAsync(profil => !profil.AppUser.IsBlocked, cancellationToken);
        var anmeldungenOffen = await profile.CountAsync(profil => profil.AppUser.IsSelfRegistered && profil.AppUser.IsBlocked, cancellationToken);
        var gesperrt = await profile.CountAsync(profil => profil.AppUser.IsBlocked && !profil.AppUser.IsSelfRegistered, cancellationToken);
        var klassen = await db.Set<Klasse>().AsNoTracking().CountAsync(klasse => klasse.SchoolId == schule.Id && !klasse.IstSystemNiveau, cancellationToken);
        var lehrkraefte = lehrerRolleID is Guid rolleID
            ? await db.UserSchools.AsNoTracking().CountAsync(link => link.SchoolId == schule.Id && link.CanManageStudents && !link.IstPlattformZuordnung
                && db.UserRoles.Any(benutzerRolle => benutzerRolle.UserId == link.AppUserId && benutzerRolle.RoleId == rolleID), cancellationToken)
            : 0;

        var schuljahr = await db.Set<Schuljahr>().AsNoTracking()
            .Where(jahr => jahr.SchulID == schule.Id && !jahr.IstGeschlossen && jahr.Beginn <= heute && jahr.Ende >= heute)
            .OrderByDescending(jahr => jahr.Beginn)
            .Select(jahr => new { jahr.Id, jahr.Bezeichnung })
            .FirstOrDefaultAsync(cancellationToken);

        var anwesenheitHeute = db.Set<AnwesenheitsEintrag>().AsNoTracking()
            .Where(eintrag => eintrag.Datum == heute && eintrag.JahresZuordnung.SchulID == schule.Id);
        var abwesend = await anwesenheitHeute.CountAsync(eintrag => eintrag.Status == AnwesenheitsStatus.Abwesend, cancellationToken);
        var verspaetet = await anwesenheitHeute.CountAsync(eintrag => eintrag.Status == AnwesenheitsStatus.Verspaetet, cancellationToken);
        var erfasst = abwesend + verspaetet > 0 || await anwesenheitHeute.AnyAsync(cancellationToken);

        var bulletins = schuljahr is null ? 0 : await db.Set<BulletinAusgabe>().AsNoTracking()
            .CountAsync(ausgabe => ausgabe.SchulID == schule.Id && ausgabe.IstVeroeffentlicht && ausgabe.ErsetztDurchID == null
                && ausgabe.Periode.SchuljahrID == schuljahr.Id, cancellationToken);

        var finanzService = new FinanzService(db);
        var zugriff = await finanzService.GetFinanceAccessAsync(benutzerID, schule.Id, cancellationToken: cancellationToken);
        var finanzenSichtbar = zugriff.Menu && zugriff.Dashboard;
        decimal eingenommen = 0m, offen = 0m, kontostand = 0m;
        var laufendeZyklen = await db.KursNiveauDurchlaeufe.AsNoTracking().CountAsync(durchlauf => durchlauf.SchulID == schule.Id && durchlauf.EndDatum == null, cancellationToken);
        if (finanzenSichtbar)
        {
            var uebersicht = await finanzService.GetDirektorKontostandUebersichtAsync(schule.Id, cancellationToken: cancellationToken);
            eingenommen = uebersicht.SchulgebuehrenEingenommen;
            offen = uebersicht.OffeneSchulgebuehrenAktiveKlassen;
            kontostand = uebersicht.IstKontostand;
        }

        return new DirektionsCockpit(schule.Id, schule.Name, schule.City, schuljahr?.Bezeichnung, schuelerAktiv, anmeldungenOffen, gesperrt,
            klassen, lehrkraefte, abwesend, verspaetet, erfasst, bulletins, finanzenSichtbar, eingenommen, offen, kontostand, laufendeZyklen);
    }
}
