using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Zeile der Administratorenliste.</summary>
public sealed record AdminZeile(Guid Id, string Vorname, string Nachname, string Identifiant, string? Email, KontoStatus Status,
    bool IstHauptAdmin, DateTime? LetzteAnmeldungUtc)
{
    public string Name => $"{Vorname} {Nachname}".Trim();
}

/// <summary>Eingabe « Ajouter / Modifier un administrateur » (Identifiant nur beim Anlegen).</summary>
public sealed record AdminEingabe(string? Vorname, string? Nachname, string? Email, string? Identifiant = null);

/// <summary>
/// Administratoren verwalten wie eNOTE (<c>AdminKontenService</c>): nur der Hauptadministrator (<see cref="Konto.IstHauptAdmin"/>,
/// sonst das älteste aktive Admin-Konto) legt weitere Administratoren an (Startpasswort einmal anzeigen), ändert, sperrt
/// (sofort abgemeldet) und löscht sie (Archiv – das Journal behält ihre Spuren). Er selbst ist hier nicht änderbar.
/// Neue Administratoren haben alle Rechte des SuperAdmin außer dieser Verwaltung. Alles im Audit.
/// </summary>
public sealed class AdministratorenService(
    IDbContextFactory<SkoulouDbContext> dbFactory,
    Zugriff zugriff,
    AuditService audit,
    PlattformKontoFabrik konten,
    SitzungsPruefung sitzungen)
{
    public const string MeldungNurHauptAdmin = "Seul l'administrateur principal peut gérer les administrateurs.";
    public const string MeldungZiel = "Seuls les autres comptes administrateurs peuvent être modifiés ici.";

    private static IQueryable<Konto> Admins(SkoulouDbContext db) => db.Users.Where(k => k.Rolle == Rollen.SuperAdmin);

    /// <summary>Genau ein Hauptadministrator: fehlt die Kennzeichnung, erhält sie das älteste aktive Admin-Konto.</summary>
    public static async Task SichereHauptAdminAsync(SkoulouDbContext db, CancellationToken ct = default)
    {
        if (await Admins(db).AnyAsync(k => k.IstHauptAdmin, ct)) return;
        var aeltester = await Admins(db).Where(k => k.Status == KontoStatus.Actif)
            .OrderBy(k => k.ErstelltAmUtc).ThenBy(k => k.Id).FirstOrDefaultAsync(ct);
        if (aeltester is null) return;
        aeltester.IstHauptAdmin = true;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Ist das Konto der (aktive) Hauptadministrator? (Navigation und Seite.)</summary>
    public async Task<bool> IstHauptAdminAsync(Guid kontoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await SichereHauptAdminAsync(db, ct);
        return await Admins(db).AnyAsync(k => k.Id == kontoId && k.IstHauptAdmin && k.Status == KontoStatus.Actif, ct);
    }

    private async Task<Akteur> HauptAdminAsync(Guid akteurId, CancellationToken ct)
    {
        var akteur = await zugriff.AdminAsync(akteurId, ct);
        if (!await IstHauptAdminAsync(akteur.Id, ct)) throw new KeinZugriffFehler(MeldungNurHauptAdmin);
        return akteur;
    }

    /// <summary>Anderes Admin-Konto (nie der Hauptadministrator, nie man selbst, nie archiviert).</summary>
    private static async Task<Konto> ZielAsync(SkoulouDbContext db, Akteur akteur, Guid zielId, CancellationToken ct)
    {
        var konto = await Admins(db).FirstOrDefaultAsync(k => k.Id == zielId, ct) ?? throw new FachFehler(MeldungZiel);
        if (konto.IstHauptAdmin || konto.Id == akteur.Id || konto.Status == KontoStatus.Archive) throw new FachFehler(MeldungZiel);
        return konto;
    }

    public async Task<IReadOnlyList<AdminZeile>> ListeAsync(Guid akteurId, CancellationToken ct = default)
    {
        await HauptAdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Admins(db).AsNoTracking().Where(k => k.Status != KontoStatus.Archive)
            .OrderByDescending(k => k.IstHauptAdmin).ThenBy(k => k.Vorname).ThenBy(k => k.Nachname)
            .Select(k => new AdminZeile(k.Id, k.Vorname, k.Nachname, k.UserName!, k.Email, k.Status, k.IstHauptAdmin, k.LetzteAnmeldungUtc))
            .ToListAsync(ct);
    }

    /// <summary>Neuer Administrator (E-Mail Pflicht): Startpasswort einmal anzeigen – die Person ändert es bei der ersten Connexion.</summary>
    public async Task<Zugang> AnlegenAsync(Guid akteurId, AdminEingabe e, CancellationToken ct = default)
    {
        var akteur = await HauptAdminAsync(akteurId, ct);
        var vorname = Eingabe.Pflicht(e.Vorname, 100, "le prénom", "prenom");
        var nachname = Eingabe.Pflicht(e.Nachname, 100, "le nom", "nom");
        var email = Eingabe.Email(e.Email) ?? throw new FachFehler("Une adresse e-mail est nécessaire pour un administrateur.", "email");
        return await dbFactory.TransaktionAsync(async db =>
        {
            var identifiant = await konten.Basis.IdentifiantAsync(db, e.Identifiant, vorname, nachname, ct: ct);
            var passwort = StartPasswort.Erzeuge();
            var konto = await konten.AnlegenAsync(db, new KontoDaten(vorname, nachname, identifiant, email, null), identifiant,
                Rollen.SuperAdmin, null, KontoStatus.Actif, passwort, mussPasswortAendern: true, erstelltVonId: akteur.Id, selbstRegistriert: false, ct);
            audit.Erfasse(db, akteur.Id, null, AuditAktionen.KontoAngelegt, nameof(Konto), konto.Id, $"Administrateur {konto.VollerName} ({identifiant}) ajouté.");
            await db.SaveChangesAsync(ct);
            return new Zugang(konto.Id, konto.VollerName, Rollen.Bezeichnung(Rollen.SuperAdmin), identifiant, passwort);
        }, ct, DbArbeit.KontoVersuche);
    }

    /// <summary>Prénom, nom und E-Mail eines anderen Administrators ändern.</summary>
    public async Task BearbeitenAsync(Guid akteurId, Guid zielId, AdminEingabe e, CancellationToken ct = default)
    {
        var akteur = await HauptAdminAsync(akteurId, ct);
        var vorname = Eingabe.Pflicht(e.Vorname, 100, "le prénom", "prenom");
        var nachname = Eingabe.Pflicht(e.Nachname, 100, "le nom", "nom");
        var email = Eingabe.Email(e.Email) ?? throw new FachFehler("Une adresse e-mail est nécessaire pour un administrateur.", "email");
        await dbFactory.TransaktionAsync(async db =>
        {
            var konto = await ZielAsync(db, akteur, zielId, ct);
            (konto.Vorname, konto.Nachname) = (vorname, nachname);
            konten.Basis.SetzeEmail(konto, email);
            audit.Erfasse(db, akteur.Id, null, OrgAudit.KontoGeaendert, nameof(Konto), konto.Id, $"Administrateur {konto.VollerName} modifié.");
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    /// <summary>Sperren (sofort abgemeldet) bzw. entsperren.</summary>
    public async Task SperrenAsync(Guid akteurId, Guid zielId, bool gesperrt, CancellationToken ct = default)
    {
        var akteur = await HauptAdminAsync(akteurId, ct);
        await dbFactory.TransaktionAsync(async db =>
        {
            var konto = await ZielAsync(db, akteur, zielId, ct);
            if ((konto.Status == KontoStatus.Bloque) == gesperrt) return;
            konto.Status = gesperrt ? KontoStatus.Bloque : KontoStatus.Actif;
            if (gesperrt) konto.AktiveSitzungId = Guid.NewGuid();
            else (konto.AccessFailedCount, konto.LockoutEnd) = (0, null);
            audit.Erfasse(db, akteur.Id, null, gesperrt ? AuditAktionen.KontoGesperrt : PlattformAudit.KontoEntsperrt, nameof(Konto), konto.Id,
                $"Administrateur {konto.VollerName} {(gesperrt ? "bloqué et déconnecté" : "débloqué")}.");
            await db.SaveChangesAsync(ct);
        }, ct);
        sitzungen.Vergessen(zielId);
    }

    /// <summary>« Supprimer »: das Konto wird archiviert (keine Anmeldung mehr, Spuren im Journal bleiben).</summary>
    public async Task LoeschenAsync(Guid akteurId, Guid zielId, CancellationToken ct = default)
    {
        var akteur = await HauptAdminAsync(akteurId, ct);
        await dbFactory.TransaktionAsync(async db =>
        {
            var konto = await ZielAsync(db, akteur, zielId, ct);
            konto.Status = KontoStatus.Archive;
            konto.AktiveSitzungId = Guid.NewGuid();
            audit.Erfasse(db, akteur.Id, null, AuditAktionen.KontoArchiviert, nameof(Konto), konto.Id, $"Administrateur {konto.VollerName} supprimé (compte archivé).");
            await db.SaveChangesAsync(ct);
        }, ct);
        sitzungen.Vergessen(zielId);
    }
}
