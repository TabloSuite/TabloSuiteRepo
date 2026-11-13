using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.WebApp.Data.Seed;

/// <summary>
/// Start-Seed (idempotent): Rollen, Plattform-Einstellungen und der Start-Admin aus <c>Seed:*</c>.
/// Der Admin wird nur angelegt, wenn er fehlt – nie geändert, nie sein Passwort zurückgesetzt.
/// Existiert das Identifiant ohne Admin-Rolle, wird abgebrochen (keine Rechteerweiterung).
/// </summary>
public sealed class StartSeed(
    RoleManager<Rolle> rollen,
    UserManager<Konto> users,
    IDbContextFactory<SkoulouDbContext> dbFactory,
    IOptions<SeedOptionen> seed,
    ILogger<StartSeed> logger)
{
    public const string AgbStartVersion = "2026-11-13";

    public async Task AusfuehrenAsync(CancellationToken ct = default)
    {
        await RollenAsync();
        await EinstellungenAsync(ct);
        await AdminAsync();
    }

    private async Task RollenAsync()
    {
        foreach (var name in Rollen.Alle)
        {
            if (await rollen.RoleExistsAsync(name)) continue;
            var ergebnis = await rollen.CreateAsync(new Rolle { Name = name, Beschreibung = Rollen.Beschreibung(name) });
            if (!ergebnis.Succeeded) throw new InvalidOperationException($"Rôle « {name} » non créé : {KontoAnlage.Fehlertext(ergebnis)}");
        }
    }

    private async Task EinstellungenAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.PlattformEinstellungen.AnyAsync(ct)) return;
        db.PlattformEinstellungen.Add(new PlattformEinstellung
        {
            Id = PlattformEinstellung.EinzigeId,
            JahrespreisProSchueler = 12_500,
            ZahlungszielTage = 30,
            ResponsableProzentStandard = 0,
            AgbVersion = AgbStartVersion
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task AdminAsync()
    {
        var o = seed.Value;
        if (string.IsNullOrWhiteSpace(o.SuperAdminEmail) || string.IsNullOrWhiteSpace(o.SuperAdminPasswort))
        {
            if (!await users.Users.AnyAsync(k => k.Rolle == Rollen.SuperAdmin))
                logger.LogWarning("Aucun compte SuperAdmin : définissez Seed__SuperAdminEmail et Seed__SuperAdminPasswort.");
            return;
        }

        var identifiant = string.IsNullOrWhiteSpace(o.SuperAdminIdentifiant) ? o.SuperAdminEmail.Trim() : o.SuperAdminIdentifiant.Trim();
        var vorhanden = await users.FindByNameAsync(identifiant);
        if (vorhanden is not null)
        {
            if (vorhanden.Rolle == Rollen.SuperAdmin || await users.IsInRoleAsync(vorhanden, Rollen.SuperAdmin)) return;
            throw new InvalidOperationException("L'identifiant du compte de démarrage existe déjà sans rôle administrateur.");
        }

        var name = (o.SuperAdminName ?? "Administrateur SKOULOU").Trim();
        var leer = name.IndexOf(' ');
        var konto = new Konto
        {
            UserName = identifiant,
            Email = o.SuperAdminEmail.Trim(),
            Vorname = leer > 0 ? name[..leer] : name,
            Nachname = leer > 0 ? name[(leer + 1)..] : string.Empty,
            Status = KontoStatus.Actif
        };
        var ergebnis = await KontoAnlage.AnlegenAsync(users, konto, Rollen.SuperAdmin, o.SuperAdminPasswort);
        if (!ergebnis.Succeeded)
            throw new InvalidOperationException("Le compte de démarrage n'a pas pu être créé : " + KontoAnlage.Fehlertext(ergebnis));
        logger.LogInformation("Compte SuperAdmin de démarrage créé.");
    }
}
