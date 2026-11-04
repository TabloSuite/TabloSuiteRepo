using ENote.Core.DataModels.Infrastruktur;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services.Storage;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>Salon-Einstellungen des Besitzers: Slogan und Logo (Branding für den Bon).</summary>
public class SalonService(IDbContextFactory<ApplicationDbContext> dbContextFactory, IMediaStorageService mediaStorage)
{
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
        salon.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
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
        salon.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Lädt ein Logo hoch und verknüpft es mit dem Salon.</summary>
    public async Task<(bool Erfolg, string? Fehler, string? LogoUrl)> SetzeLogoAsync(
        Guid salonId, string dateiName, string contentType, Stream inhalt, Guid hochgeladenVonId,
        CancellationToken cancellationToken = default)
    {
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

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        salon.UpdatedAtUtc = DateTime.UtcNow;
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
        var jetzt = DateTime.UtcNow;
        return await db.UserSalons
            .AnyAsync(us => us.AppUserId == besitzerId
                && us.Salon.IstFreigegeben
                && (us.Salon.ProbeBisUtc == null || us.Salon.ProbeBisUtc <= jetzt), cancellationToken);
    }

    /// <summary>Besitzer legt einen weiteren Laden an (erfordert abgelaufene Probezeit eines
    /// freigegebenen Ladens). Der neue Laden ist sofort aktiv, muss aber vom Admin freigegeben
    /// werden und hat keine erneute Probezeit.</summary>
    public async Task<(bool Erfolg, string? Fehler, Salon? Salon)> FuegeSalonHinzuAsync(
        Guid besitzerId, string name, string? standort, string? land, CancellationToken cancellationToken = default)
    {
        var bereinigtName = (name ?? string.Empty).Trim();
        if (bereinigtName.Length < 2)
        {
            return (false, "Veuillez saisir un nom d'établissement (au moins 2 caractères).", null);
        }

        if (!await DarfWeiterenLadenAnlegenAsync(besitzerId, cancellationToken))
        {
            return (false, "Vous pourrez ajouter un autre établissement une fois la période d'essai de votre établissement validé terminée.", null);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
            AppUserId = besitzerId,
            SalonId = salon.Id,
            KannFinanzenSehen = true,
            KannLeistungenVerwalten = true,
            KannRabatteVerwalten = true
        });
        await db.SaveChangesAsync(cancellationToken);

        return (true, null, salon);
    }
}
