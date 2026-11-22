using ENote.Core.DataModels.Identity;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

public static class LadenZugriff
{
    public static Task<bool> HatZugangAsync(ApplicationDbContext db, Guid benutzerId, Guid salonId, CancellationToken ct = default)
        => db.UserSalons.AnyAsync(us => us.AppUserId == benutzerId && us.SalonId == salonId
            && !us.AppUser.IsBlocked && us.Salon.IstAktiv, ct);

    /// <summary>Text bei abgewiesener Anmeldung eines Mitarbeiters ohne « Connexion personnelle ».</summary>
    public const string AnmeldungNurAmGeraetText =
        "Votre compte employé n'a pas la connexion personnelle. Encaissez sur l'appareil de l'établissement, connecté par le propriétaire, avec votre nom et votre code PIN.";

    /// <summary>Besitzer-PIN von einem Mitarbeiter-Konto aus: nie (sonst ließe sie sich z. B. von zu Hause durchprobieren).</summary>
    public const string BesitzerPinNurBeimBesitzerText =
        "Le code PIN du propriétaire se saisit uniquement sur un appareil où le propriétaire est connecté.";

    /// <summary>
    /// Reines Mitarbeiter-Konto: Rolle « Kassierer », aber weder Besitzer noch Responsable/Admin. Nur für solche Konten gelten
    /// « Connexion personnelle », die Sperre der Kasse und das Verbot der Besitzer-PIN.
    /// </summary>
    public static async Task<bool> IstMitarbeiterKontoAsync(ApplicationDbContext db, Guid benutzerId, CancellationToken ct = default)
    {
        var rollen = await db.UserRoles.Where(ur => ur.UserId == benutzerId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name).ToListAsync(ct);
        return IstMitarbeiterRolle(rollen.Contains(AppRollen.Kassierer), rollen.Contains(AppRollen.Besitzer),
            rollen.Contains(AppRollen.SuperAdmin) || rollen.Contains(AppRollen.ProduktOwner));
    }

    /// <summary>Gleiche Regel für Rollen aus den Anmelde-Claims (ohne Datenbank).</summary>
    public static bool IstMitarbeiterRolle(bool kassierer, bool besitzer, bool plattform) => kassierer && !besitzer && !plattform;

    /// <summary>Darf sich dieses Konto anmelden? Alle außer Mitarbeiter-Konten ohne « Connexion personnelle » in einem aktiven Laden.</summary>
    public static async Task<bool> DarfSichAnmeldenAsync(ApplicationDbContext db, Guid benutzerId, CancellationToken ct = default)
        => !await IstMitarbeiterKontoAsync(db, benutzerId, ct)
            || await db.UserSalons.AnyAsync(us => us.AppUserId == benutzerId && us.KannSichAnmelden && us.Salon.IstAktiv, ct);

    /// <summary>Besitzer oder Mitglied mit dem Recht « Dossiers clients » (Kundenliste, Kundenkarte, Notizen).</summary>
    public static async Task<bool> HatKundenRechtAsync(ApplicationDbContext db, Guid benutzerId, Guid salonId, CancellationToken ct = default)
        => await IstBesitzerAsync(db, benutzerId, salonId, ct)
            || await db.UserSalons.AnyAsync(us => us.AppUserId == benutzerId && us.SalonId == salonId && us.KannKundenAktenFuehren
                && !us.AppUser.IsBlocked && us.Salon.IstAktiv, ct);

    /// <summary>Delegierbare Rechte einer Verwaltungsseite.</summary>
    public enum Recht { Leistungen, Rabatte, Finanzen }

    public const string RechtEntzogenText =
        "Vous n'avez pas (ou plus) ce droit dans cet établissement. Rechargez la page.";

    /// <summary>
    /// Prüft bei JEDER schreibenden Aktion (nicht nur beim Seitenaufbau): Besitzer oder Mitglied mit dem Recht, Konto nicht
    /// gesperrt, Laden aktiv. Fehlersuche 21.11.2026: nach Entzug des Rechts, Entfernen aus dem Laden oder Deaktivieren des
    /// Ladens speicherte die noch offene Seite weiter (Prestations, Remises, Dépenses). Sonst <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    public static async Task PruefeRechtAsync(ApplicationDbContext db, Guid benutzerId, Guid salonId, Recht recht, CancellationToken ct = default)
    {
        if (benutzerId != Guid.Empty && await IstBesitzerAsync(db, benutzerId, salonId, ct)) return;
        var mitglied = db.UserSalons.Where(us => us.AppUserId == benutzerId && us.SalonId == salonId
            && !us.AppUser.IsBlocked && us.Salon.IstAktiv);
        var erlaubt = recht switch
        {
            Recht.Leistungen => await mitglied.AnyAsync(us => us.KannLeistungenVerwalten, ct),
            Recht.Rabatte => await mitglied.AnyAsync(us => us.KannRabatteVerwalten, ct),
            _ => await mitglied.AnyAsync(us => us.KannFinanzenSehen, ct)
        };
        if (!erlaubt) throw new UnauthorizedAccessException(RechtEntzogenText);
    }

    public static Task<bool> IstBesitzerAsync(ApplicationDbContext db, Guid benutzerId, Guid salonId, CancellationToken ct = default)
        => db.UserSalons.AnyAsync(us => us.AppUserId == benutzerId && us.SalonId == salonId
            && !us.AppUser.IsBlocked && us.Salon.IstAktiv && db.UserRoles.Any(ur => ur.UserId == benutzerId
                && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.Besitzer)), ct);
}
