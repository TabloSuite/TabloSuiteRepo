using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;

namespace Skoulou.WebApp.Services.Organisation;

/// <summary>Zugangsdaten eines neuen oder zurückgesetzten Kontos (Passwort nur einmal anzeigen/drucken).</summary>
public sealed record Zugang(Guid KontoId, string Name, string Rolle, string Identifiant, string Passwort, string? Zusatz = null);

/// <summary>Grunddaten eines neuen Kontos.</summary>
public sealed record KontoDaten(string Vorname, string Nachname, string? Identifiant, string? Email, string? Telefon,
    Geschlecht Geschlecht = Geschlecht.Divers, DateOnly? Geburtsdatum = null);

/// <summary>
/// Legt Konten im Kontext des Aufrufers an (gleiche Transaktion wie Profil, Einschreibung, Audit) –
/// ohne UserManager im Circuit: Passwort-Hash, Normalisierung, Stempel und Identity-Rolle wie Identity selbst.
/// </summary>
public sealed class KontoFabrik(IPasswordHasher<Konto> hasher, ILookupNormalizer normalisierer, TimeProvider zeit)
{
    /// <summary>Ist das Identifiant schon vergeben (ohne Rücksicht auf Groß-/Kleinschreibung)?</summary>
    public Task<bool> IstVergebenAsync(SkoulouDbContext db, string identifiant, Guid? ausser = null, CancellationToken ct = default)
    {
        var normal = normalisierer.NormalizeName(identifiant);
        return db.Users.AnyAsync(k => k.NormalizedUserName == normal && (ausser == null || k.Id != ausser), ct);
    }

    /// <summary>Freier Vorschlag « vorname.nachname », « …2 » …; <paramref name="reserviert"/> = schon in dieser Sitzung vergeben.</summary>
    public Task<string> VorschlagAsync(SkoulouDbContext db, string? vorname, string? nachname,
        ISet<string>? reserviert = null, CancellationToken ct = default)
        => IdentifiantRegel.VorschlagAsync(vorname, nachname,
            async k => (reserviert?.Contains(k) ?? false) || await IstVergebenAsync(db, k, null, ct));

    /// <summary>Identifiant aus Eingabe (normalisiert, geprüft) oder automatisch vorgeschlagen.</summary>
    public async Task<string> IdentifiantAsync(SkoulouDbContext db, string? eingabe, string vorname, string nachname,
        Guid? ausser = null, ISet<string>? reserviert = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eingabe)) return await VorschlagAsync(db, vorname, nachname, reserviert, ct);
        var id = IdentifiantRegel.Normalisiere(eingabe);
        if (!IdentifiantRegel.IstGueltig(id)) throw new FachFehler(IdentifiantRegel.MeldungFormat, "identifiant");
        if ((reserviert?.Contains(id) ?? false) || await IstVergebenAsync(db, id, ausser, ct))
            throw new FachFehler(IdentifiantRegel.MeldungVergeben, "identifiant");
        return id;
    }

    /// <summary>
    /// Fügt Konto und Identity-Rolle dem Kontext hinzu (gespeichert wird vom Aufrufer). Das Konto muss
    /// beim ersten Login ein eigenes Passwort wählen.
    /// </summary>
    public async Task<(Konto Konto, string Passwort)> AnlegenAsync(SkoulouDbContext db, KontoDaten daten, string identifiant,
        string rolle, Guid schuleId, Guid erstelltVonId, KontoStatus status = KontoStatus.Actif,
        KoordinatorRechte rechte = KoordinatorRechte.Keine, CancellationToken ct = default)
    {
        var rolleId = await db.Roles.Where(r => r.Name == rolle).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct)
                      ?? throw new InvalidOperationException($"Rôle « {rolle} » introuvable (seed de démarrage manquant).");
        var passwort = StartPasswort.Erzeuge();
        var jetzt = zeit.GetUtcNow().UtcDateTime;
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
            KoordinatorRechte = rolle == Rollen.Koordinator ? rechte : KoordinatorRechte.Keine,
            MussPasswortAendern = true,
            ErstelltVonId = erstelltVonId,
            SecurityStamp = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true,
            ErstelltAmUtc = jetzt
        };
        SetzeEmail(konto, daten.Email);
        konto.PasswordHash = hasher.HashPassword(konto, passwort);
        db.Users.Add(konto);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = konto.Id, RoleId = rolleId });
        return (konto, passwort);
    }

    /// <summary>E-Mail setzen (optional, mehrfach erlaubt) inkl. Normalisierung.</summary>
    public void SetzeEmail(Konto konto, string? email)
    {
        konto.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        konto.NormalizedEmail = konto.Email is null ? null : normalisierer.NormalizeEmail(konto.Email);
        konto.EmailConfirmed = konto.Email is not null;
    }

    /// <summary>Neues Identifiant setzen (bereits geprüft).</summary>
    public void SetzeIdentifiant(Konto konto, string identifiant)
    {
        konto.UserName = identifiant;
        konto.NormalizedUserName = normalisierer.NormalizeName(identifiant);
    }

    /// <summary>Neues Startpasswort: Pflicht zum Ändern, alle Sitzungen enden, Sperre durch Fehlversuche aufgehoben.</summary>
    public string NeuesStartpasswort(Konto konto)
    {
        var passwort = StartPasswort.Erzeuge();
        konto.PasswordHash = hasher.HashPassword(konto, passwort);
        konto.SecurityStamp = Guid.NewGuid().ToString("N").ToUpperInvariant();
        konto.MussPasswortAendern = true;
        konto.AktiveSitzungId = Guid.NewGuid();
        konto.AccessFailedCount = 0;
        konto.LockoutEnd = null;
        return passwort;
    }
}
