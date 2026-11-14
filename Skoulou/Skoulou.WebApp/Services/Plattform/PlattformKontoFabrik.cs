using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>
/// Legt Konten des Bereichs Plattform im Kontext des Aufrufers an (Selbstregistrierung, Responsables) –
/// auch ohne Schule und mit selbst gewähltem Passwort. Identifiant-Regeln und E-Mail-Normalisierung
/// kommen aus der gemeinsamen <see cref="KontoFabrik"/>.
/// </summary>
public sealed class PlattformKontoFabrik(KontoFabrik fabrik, IPasswordHasher<Konto> hasher, ILookupNormalizer normalisierer, TimeProvider zeit)
{
    public KontoFabrik Basis => fabrik;

    /// <summary>Fügt Konto und Identity-Rolle hinzu (gespeichert wird vom Aufrufer).</summary>
    public async Task<Konto> AnlegenAsync(SkoulouDbContext db, KontoDaten daten, string identifiant, string rolle, Guid? schuleId,
        KontoStatus status, string passwort, bool mussPasswortAendern, Guid? erstelltVonId, bool selbstRegistriert, CancellationToken ct)
    {
        var rolleId = await db.Roles.Where(r => r.Name == rolle).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct)
                      ?? throw new InvalidOperationException($"Rôle « {rolle} » introuvable (seed de démarrage manquant).");
        var konto = new Konto
        {
            Id = NeueId.Erzeuge(),
            UserName = identifiant,
            NormalizedUserName = normalisierer.NormalizeName(identifiant),
            Vorname = daten.Vorname,
            Nachname = daten.Nachname,
            Telefon = daten.Telefon,
            Geschlecht = daten.Geschlecht,
            Geburtsdatum = daten.Geburtsdatum,
            Rolle = rolle,
            Status = status,
            SchuleId = schuleId,
            MussPasswortAendern = mussPasswortAendern,
            ErstelltVonId = erstelltVonId,
            SelbstRegistriert = selbstRegistriert,
            SecurityStamp = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true,
            ErstelltAmUtc = zeit.GetUtcNow().UtcDateTime
        };
        fabrik.SetzeEmail(konto, daten.Email);
        konto.PasswordHash = hasher.HashPassword(konto, passwort);
        db.Users.Add(konto);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = konto.Id, RoleId = rolleId });
        return konto;
    }
}
