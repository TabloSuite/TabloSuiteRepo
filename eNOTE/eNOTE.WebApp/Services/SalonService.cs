using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Infrastruktur;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services.Storage;
using ENote.WebApp.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>Salon-Einstellungen des Besitzers: Slogan und Logo (Branding für den Bon).</summary>
public class SalonService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IMediaStorageService mediaStorage,
    UserManager<AppUser> userManager, TimeProvider? zeit = null)
{
    private readonly TimeProvider uhr = zeit ?? TimeProvider.System;

    public async Task<Salon?> GetSalonAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Salons
            .Include(s => s.LogoAsset)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == salonId, cancellationToken);
    }

    public async Task<bool> SetzeSloganAsync(Guid salonId, string? slogan, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var salon = await db.Salons.FirstOrDefaultAsync(s => s.Id == salonId, cancellationToken);
        if (salon is null)
        {
            return false;
        }

        salon.Slogan = string.IsNullOrWhiteSpace(slogan) ? null : slogan.Trim();
        salon.UpdatedAtUtc = uhr.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Wählbare Länder (siehe <see cref="LaenderAuswahl"/>).</summary>
    public async Task<List<string>> GetLaenderAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await LaenderAuswahl.GetAsync(db, cancellationToken);
    }

    /// <summary>Besitzer ändert das Land seines Ladens (Ortszeit, Mobile-Money-Anbieter); <see cref="ArgumentException"/> bei ungültiger Wahl.</summary>
    public async Task<bool> SetzeLandAsync(Guid benutzerId, Guid salonId, string? land, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await LadenZugriff.IstBesitzerAsync(db, benutzerId, salonId, cancellationToken))
            throw new UnauthorizedAccessException("Seul le propriétaire peut changer le pays de l'établissement.");
        return await LaenderAuswahl.SetzeAsync(db, salonId, land, uhr.GetUtcNow().UtcDateTime, cancellationToken);
    }

    /// <summary>Aktualisiert die Kontaktdaten (Slogan, Telefon, Adresse, QR-Link) für den Bon.</summary>
    public async Task<bool> SetzeKontaktdatenAsync(
        Guid salonId, string? slogan, string? telefon, string? adresse, string? bonQrLink, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var salon = await db.Salons.FirstOrDefaultAsync(s => s.Id == salonId, cancellationToken);
        if (salon is null)
        {
            return false;
        }

        salon.Slogan = string.IsNullOrWhiteSpace(slogan) ? null : slogan.Trim();
        salon.TelefonNummer = string.IsNullOrWhiteSpace(telefon) ? null : telefon.Trim();
        salon.Adresse = string.IsNullOrWhiteSpace(adresse) ? null : adresse.Trim();
        salon.BonQrLink = string.IsNullOrWhiteSpace(bonQrLink) ? null : bonQrLink.Trim();
        salon.UpdatedAtUtc = uhr.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Lädt ein Logo hoch und verknüpft es mit dem Salon.</summary>
    public async Task<(bool Erfolg, string? Fehler, string? LogoUrl)> SetzeLogoAsync(
        Guid salonId, string dateiName, string contentType, Stream inhalt, Guid hochgeladenVonId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await LadenZugriff.IstBesitzerAsync(db, hochgeladenVonId, salonId, cancellationToken))
            return (false, "Accès non autorisé.", null);

        var ergebnis = await mediaStorage.SaveAsync(new MediaSaveRequest
        {
            DateiName = dateiName,
            ContentType = contentType,
            Inhalt = inhalt,
            SalonId = salonId
        }, cancellationToken);

        if (!ergebnis.Erfolg)
        {
            return (false, ergebnis.Fehler, null);
        }

        var salon = await db.Salons.FirstOrDefaultAsync(s => s.Id == salonId, cancellationToken);
        if (salon is null)
        {
            return (false, "Salon introuvable.", null);
        }

        var asset = new BlobMediaAsset
        {
            SalonId = salonId,
            DateiName = dateiName,
            BlobName = ergebnis.BlobName!,
            ContentType = contentType,
            GroesseBytes = ergebnis.GroesseBytes,
            HochgeladenVonId = hochgeladenVonId
        };

        db.BlobMediaAssets.Add(asset);
        await db.SaveChangesAsync(cancellationToken);

        salon.LogoAssetId = asset.Id;
        salon.UpdatedAtUtc = uhr.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);

        return (true, null, mediaStorage.GetPublicUrl(ergebnis.BlobName!));
    }

    /// <summary>Abrufrbare Logo-URL eines Salons (falls vorhanden).</summary>
    public string? GetLogoUrl(Salon salon)
        => salon.LogoAsset is null ? null : mediaStorage.GetPublicUrl(salon.LogoAsset.BlobName);

    /// <summary>Lädt die Logo-Bilddaten (für den Bon-PDF-Export).</summary>
    public async Task<byte[]?> GetLogoBytesAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var blobName = await db.Salons
            .AsNoTracking()
            .Where(s => s.Id == salonId && s.LogoAsset != null)
            .Select(s => s.LogoAsset!.BlobName)
            .FirstOrDefaultAsync(cancellationToken);

        if (blobName is null)
        {
            return null;
        }

        return await mediaStorage.LadeBytesAsync(blobName, cancellationToken);
    }

    /// <summary>Prüft, ob der Nutzer für den Salon eine Besitzer-PIN gesetzt hat.</summary>
    public async Task<bool> HatBesitzerPinAsync(Guid salonId, Guid benutzerId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.UserSalons
            .AnyAsync(us => us.SalonId == salonId && us.AppUserId == benutzerId && us.BesitzerPinHash != null, cancellationToken);
    }

    /// <summary>true, wenn der Besitzer einen weiteren Laden anlegen darf: mindestens ein
    /// freigegebener Laden, dessen Probezeit abgelaufen ist (oder der keine Probezeit hatte).</summary>
    public async Task<bool> DarfWeiterenLadenAnlegenAsync(Guid besitzerId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var jetzt = uhr.GetUtcNow().UtcDateTime;
        return await db.UserSalons
            .AnyAsync(us => us.AppUserId == besitzerId
                && us.Salon.IstFreigegeben
                && (us.Salon.ProbeBisUtc == null || us.Salon.ProbeBisUtc <= jetzt), cancellationToken);
    }

    /// <summary>
    /// Besitzer legt einen weiteren Laden an (erfordert abgelaufene Probezeit eines
    /// freigegebenen Ladens). Nach der Regel „ein Konto pro Laden" wird dabei ein
    /// <b>neues Besitzer-Konto</b> (eigenes Pseudonym + Passwort) angelegt; die E-Mail
    /// darf dieselbe wie beim ersten Laden sein (gleiche Person). Der neue Laden ist
    /// sofort aktiv, muss aber vom Admin freigegeben werden und hat keine erneute Probezeit.
    /// </summary>
    public async Task<(bool Erfolg, string? Fehler, Salon? Salon, AppUser? Konto)> FuegeSalonHinzuAsync(
        Guid bestehenderBesitzerId,
        string name, string? standort, string? land,
        string vorname, string nachname, string pseudonym, string email, string initialPasswort,
        CancellationToken cancellationToken = default)
    {
        var bereinigtName = (name ?? string.Empty).Trim();
        if (bereinigtName.Length < 2)
        {
            return (false, "Veuillez saisir un nom d'établissement (au moins 2 caractères).", null, null);
        }

        if (!await DarfWeiterenLadenAnlegenAsync(bestehenderBesitzerId, cancellationToken))
        {
            return (false, "Vous pourrez ajouter un autre établissement une fois la période d'essai de votre établissement validé terminée.", null, null);
        }

        var bereinigtPseudonym = (pseudonym ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(bereinigtPseudonym))
        {
            return (false, "Veuillez saisir un pseudonyme pour le nouveau compte.", null, null);
        }

        // Login = Pseudonym (eindeutig); E-Mail ist nur Kontaktadresse und darf mehrfach sein.
        if (await userManager.FindByNameAsync(bereinigtPseudonym) is not null)
        {
            return (false, "Ce pseudonyme est déjà utilisé.", null, null);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Salons.AnyAsync(s => s.Name == bereinigtName, cancellationToken))
        {
            return (false, "Un établissement portant ce nom existe déjà.", null, null);
        }

        var konto = new AppUser
        {
            UserName = bereinigtPseudonym,
            Email = (email ?? string.Empty).Trim(),
            Vorname = vorname.Trim(),
            Nachname = nachname.Trim(),
            FullName = $"{vorname.Trim()} {nachname.Trim()}",
            EmailConfirmed = true
        };

        var erstellen = await userManager.CreateAsync(konto, initialPasswort);
        if (!erstellen.Succeeded)
        {
            var fehler = string.Join(" ", erstellen.Errors.Select(e => e.Description));
            return (false, string.IsNullOrWhiteSpace(fehler) ? "La création a échoué." : fehler, null, null);
        }

        await userManager.AddToRoleAsync(konto, AppRollen.Besitzer);

        var salon = new Salon
        {
            Name = bereinigtName,
            Standort = string.IsNullOrWhiteSpace(standort) ? null : standort.Trim(),
            Land = string.IsNullOrWhiteSpace(land) ? null : land.Trim(),
            IstAktiv = true,
            IstFreigegeben = false, // muss vom Admin freigegeben werden
            ProbeBisUtc = null      // keine erneute Probezeit für weitere Läden
        };

        db.Salons.Add(salon);
        await db.SaveChangesAsync(cancellationToken);

        db.UserSalons.Add(new UserSalon
        {
            AppUserId = konto.Id,
            SalonId = salon.Id,
            KannFinanzenSehen = true,
            KannLeistungenVerwalten = true,
            KannRabatteVerwalten = true
        });
        await db.SaveChangesAsync(cancellationToken);

        return (true, null, salon, konto);
    }
}
