using ENote.Core.DataModels.Identity;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services.Mails;

/// <summary>
/// Manuelle Nachrichten der Administration an ausgewählte Nutzer (einzeln, alle Besitzer, Besitzer
/// oder Teams bestimmter Läden, ProduktOwner). Jeder Empfänger bekommt eine eigene Mail – keine
/// sichtbaren Mitempfänger. Nur SuperAdmin; gesperrte Konten und Konten ohne E-Mail werden übersprungen.
/// </summary>
public class NachrichtenService(IDbContextFactory<ApplicationDbContext> factory, EmailService email)
{
    public const int MaxEmpfaenger = 200;

    public enum Gruppe { Einzeln, AlleBesitzer, BesitzerVonLaeden, TeamVonLaeden, AlleProduktOwner }

    public sealed record Empfaenger(Guid UserId, string Name, string Vorname, string Email, string Rolle, string? Laden);

    public sealed record LadenOption(Guid Id, string Name, bool Aktiv);

    private sealed record Kandidat(Guid Id, string FullName, string Vorname, string? UserName, string Email);

    private static string RolleText(string? rolle) => rolle switch
    {
        AppRollen.SuperAdmin => "Administrateur",
        AppRollen.ProduktOwner => "Responsable",
        AppRollen.Besitzer => "Propriétaire",
        AppRollen.Kassierer => "Caissier",
        _ => "Utilisateur"
    };

    /// <summary>Höchste Rolle je Benutzer (für Anzeige und Gruppen).</summary>
    private static async Task<Dictionary<Guid, string>> RollenAsync(ApplicationDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var paare = await db.UserRoles.Where(ur => ids.Contains(ur.UserId))
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(ct);
        string[] rang = [AppRollen.SuperAdmin, AppRollen.ProduktOwner, AppRollen.Besitzer, AppRollen.Kassierer];
        return paare.GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => rang.FirstOrDefault(r => g.Any(p => p.Name == r)) ?? "");
    }

    /// <summary>Erreichbare Konten (nicht gesperrt, kein interner Kassierer, mit E-Mail). Filter immer vor der Projektion.</summary>
    private static IQueryable<AppUser> Erreichbar(ApplicationDbContext db)
        => db.Users.AsNoTracking().Where(u => !u.IsBlocked && !u.IstInternerKassierer && u.Email != null && u.Email != "");

    private static Task<List<Kandidat>> AlsKandidatenAsync(IQueryable<AppUser> q, CancellationToken ct)
        => q.OrderBy(u => u.FullName).Select(u => new Kandidat(u.Id, u.FullName, u.Vorname, u.UserName, u.Email!)).ToListAsync(ct);

    /// <summary>Suche nach Name, Pseudonym, E-Mail oder Laden (max. 20 Treffer).</summary>
    public async Task<List<Empfaenger>> SucheAsync(Guid bearbeiter, string suche, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await AdminKontenService.PruefeAsync(db, bearbeiter, false, ct);
        var s = suche.Trim();
        if (s.Length < 2) return [];

        var nachLaden = db.UserSalons.Where(us => us.Salon.Name.Contains(s)).Select(us => us.AppUserId);
        var treffer = await AlsKandidatenAsync(Erreichbar(db)
            .Where(u => u.FullName.Contains(s) || u.Email!.Contains(s) || (u.UserName != null && u.UserName.Contains(s)) || nachLaden.Contains(u.Id))
            .OrderBy(u => u.FullName).Take(20), ct);
        return await MitDetailsAsync(db, treffer, ct);
    }

    public async Task<List<LadenOption>> GetLaedenAsync(Guid bearbeiter, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await AdminKontenService.PruefeAsync(db, bearbeiter, false, ct);
        return await db.Salons.AsNoTracking().OrderBy(s => s.Name).Select(s => new LadenOption(s.Id, s.Name, s.IstAktiv)).ToListAsync(ct);
    }

    /// <summary>Empfängerliste einer Auswahl (ohne Duplikate, gesperrte/adresslose Konten ausgenommen).</summary>
    public async Task<List<Empfaenger>> ErmittleAsync(Guid bearbeiter, Gruppe gruppe, IReadOnlyCollection<Guid> nutzerIds,
        IReadOnlyCollection<Guid> ladenIds, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await AdminKontenService.PruefeAsync(db, bearbeiter, false, ct);

        IQueryable<Guid> ids = gruppe switch
        {
            Gruppe.Einzeln => db.Users.Where(u => nutzerIds.Contains(u.Id)).Select(u => u.Id),
            Gruppe.AlleBesitzer => MitRolle(db, AppRollen.Besitzer),
            Gruppe.BesitzerVonLaeden => db.UserSalons.Where(us => ladenIds.Contains(us.SalonId)).Select(us => us.AppUserId)
                .Where(id => MitRolle(db, AppRollen.Besitzer).Contains(id)),
            Gruppe.TeamVonLaeden => db.UserSalons.Where(us => ladenIds.Contains(us.SalonId)).Select(us => us.AppUserId),
            Gruppe.AlleProduktOwner => MitRolle(db, AppRollen.ProduktOwner),
            _ => db.Users.Where(_ => false).Select(u => u.Id)
        };

        var kandidaten = await AlsKandidatenAsync(Erreichbar(db).Where(u => ids.Contains(u.Id)), ct);
        var liste = await MitDetailsAsync(db, kandidaten, ct);
        return liste.GroupBy(e => e.Email.Trim().ToLowerInvariant()).Select(g => g.First()).ToList();
    }

    private static IQueryable<Guid> MitRolle(ApplicationDbContext db, string rolle)
        => db.UserRoles.Where(ur => db.Roles.Any(r => r.Id == ur.RoleId && r.Name == rolle)).Select(ur => ur.UserId);

    private static async Task<List<Empfaenger>> MitDetailsAsync(ApplicationDbContext db, List<Kandidat> kandidaten, CancellationToken ct)
    {
        var ids = kandidaten.Select(k => k.Id).ToList();
        var rollen = await RollenAsync(db, ids, ct);
        var laeden = await db.UserSalons.AsNoTracking().Where(us => ids.Contains(us.AppUserId))
            .Select(us => new { us.AppUserId, us.Salon.Name }).ToListAsync(ct);
        return kandidaten.Select(k => new Empfaenger(k.Id, string.IsNullOrWhiteSpace(k.FullName) ? k.UserName ?? k.Email : k.FullName,
            k.Vorname, k.Email, RolleText(rollen.GetValueOrDefault(k.Id)),
            laeden.Where(l => l.AppUserId == k.Id).Select(l => l.Name).FirstOrDefault())).ToList();
    }

    /// <summary>Sendet die Nachricht einzeln an jeden Empfänger (persönliche Anrede).</summary>
    public async Task<(int Gesendet, int Fehlgeschlagen)> SendeAsync(Guid bearbeiter, IReadOnlyList<Empfaenger> empfaenger,
        string betreff, string text, string? knopfText, string? knopfUrl, CancellationToken ct = default)
    {
        await using (var db = await factory.CreateDbContextAsync(ct))
            await AdminKontenService.PruefeAsync(db, bearbeiter, false, ct);

        if (string.IsNullOrWhiteSpace(betreff) || string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Saisissez un objet et un message.");
        if (empfaenger.Count == 0) throw new ArgumentException("Choisissez au moins un destinataire.");
        if (empfaenger.Count > MaxEmpfaenger) throw new ArgumentException($"Au maximum {MaxEmpfaenger} destinataires par envoi.");
        if (!string.IsNullOrWhiteSpace(knopfUrl) && !(Uri.TryCreate(knopfUrl.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
            throw new ArgumentException("Le lien du bouton doit commencer par https://");

        int ok = 0, fehler = 0;
        foreach (var e in empfaenger)
        {
            var html = TabloSuiteMails.Nachricht(e.Vorname, betreff.Trim(), text, knopfText, knopfUrl?.Trim());
            if (await email.SendeAsync(e.Email, betreff.Trim(), html, "message", ct)) ok++; else fehler++;
        }
        return (ok, fehler);
    }
}
