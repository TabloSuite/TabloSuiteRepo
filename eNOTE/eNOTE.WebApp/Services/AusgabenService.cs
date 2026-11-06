using ENote.Core.DataModels.Finanzen;
using ENote.Core.DataModels.Infrastruktur;
using ENote.Core.Services.Storage;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>Ausgaben-Verwaltung des Besitzers: Kategorien festlegen, Ausgaben mit optionalem Beleg erfassen.</summary>
public class AusgabenService(IDbContextFactory<ApplicationDbContext> dbContextFactory, IMediaStorageService mediaStorage)
{
    // --- Kategorien ---

    public async Task<List<AusgabenKategorie>> GetKategorienAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.AusgabenKategorien
            .AsNoTracking()
            .Where(k => k.SalonId == salonId)
            .OrderBy(k => k.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<AusgabenKategorie> SpeichereKategorieAsync(Guid salonId, Guid? id, string name, bool istAktiv, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        AusgabenKategorie kategorie;
        if (id is null || id == Guid.Empty)
        {
            kategorie = new AusgabenKategorie { SalonId = salonId };
            db.AusgabenKategorien.Add(kategorie);
        }
        else
        {
            kategorie = await db.AusgabenKategorien.FirstOrDefaultAsync(k => k.Id == id && k.SalonId == salonId, cancellationToken)
                ?? throw new InvalidOperationException("Kategorie nicht gefunden.");
        }

        kategorie.Name = name.Trim();
        kategorie.IstAktiv = istAktiv;
        await db.SaveChangesAsync(cancellationToken);
        return kategorie;
    }

    // --- Ausgaben ---

    public async Task<List<Ausgabe>> GetAusgabenAsync(Guid salonId, DateTime? von, DateTime? bis, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Ausgaben
            .AsNoTracking()
            .Include(a => a.Kategorie)
            .Where(a => a.SalonId == salonId);

        if (von is not null) query = query.Where(a => a.DatumUtc >= von);
        if (bis is not null) query = query.Where(a => a.DatumUtc <= bis);

        return await query.OrderByDescending(a => a.DatumUtc).ToListAsync(cancellationToken);
    }

    public async Task<(bool Erfolg, string? Fehler, Ausgabe? Ausgabe)> ErfasseAusgabeAsync(
        Guid salonId,
        Guid kategorieId,
        decimal betrag,
        DateTime datumUtc,
        string? notiz,
        Guid erfasstVonId,
        (string DateiName, string ContentType, Stream Inhalt)? beleg,
        CancellationToken cancellationToken = default)
    {
        if (betrag <= 0)
        {
            return (false, "Le montant doit être positif.", null);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var kategorie = await db.AusgabenKategorien
            .FirstOrDefaultAsync(k => k.Id == kategorieId && k.SalonId == salonId && k.IstAktiv, cancellationToken);
        if (kategorie is null)
        {
            return (false, "Catégorie introuvable ou inactive.", null);
        }

        Guid? belegAssetId = null;
        if (beleg is not null)
        {
            if (!await LadenZugriff.HatZugangAsync(db, erfasstVonId, salonId, cancellationToken)
                || !await db.UserSalons.AnyAsync(us => us.AppUserId == erfasstVonId && us.SalonId == salonId
                    && us.KannFinanzenSehen, cancellationToken)
                    && !await LadenZugriff.IstBesitzerAsync(db, erfasstVonId, salonId, cancellationToken))
                return (false, "Accès non autorisé.", null);

            var ergebnis = await mediaStorage.SaveAsync(new MediaSaveRequest
            {
                DateiName = beleg.Value.DateiName,
                ContentType = beleg.Value.ContentType,
                Inhalt = beleg.Value.Inhalt,
                SalonId = salonId
            }, cancellationToken);

            if (!ergebnis.Erfolg)
            {
                return (false, ergebnis.Fehler, null);
            }

            var asset = new BlobMediaAsset
            {
                SalonId = salonId,
                DateiName = beleg.Value.DateiName,
                BlobName = ergebnis.BlobName!,
                ContentType = beleg.Value.ContentType,
                GroesseBytes = ergebnis.GroesseBytes,
                HochgeladenVonId = erfasstVonId
            };
            db.BlobMediaAssets.Add(asset);
            await db.SaveChangesAsync(cancellationToken);
            belegAssetId = asset.Id;
        }

        var ausgabe = new Ausgabe
        {
            SalonId = salonId,
            KategorieId = kategorieId,
            Betrag = Math.Round(betrag, 2),
            DatumUtc = datumUtc,
            Notiz = string.IsNullOrWhiteSpace(notiz) ? null : notiz.Trim(),
            BelegAssetId = belegAssetId,
            ErfasstVonId = erfasstVonId
        };

        db.Ausgaben.Add(ausgabe);
        await db.SaveChangesAsync(cancellationToken);
        return (true, null, ausgabe);
    }

    public async Task<bool> LoescheAusgabeAsync(Guid id, Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var ausgabe = await db.Ausgaben.FirstOrDefaultAsync(a => a.Id == id && a.SalonId == salonId, cancellationToken);
        if (ausgabe is null)
        {
            return false;
        }

        db.Ausgaben.Remove(ausgabe);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Abrufrbare URL eines Ausgaben-Belegs (falls vorhanden).</summary>
    public async Task<string?> GetBelegUrlAsync(Guid? belegAssetId, CancellationToken cancellationToken = default)
    {
        if (belegAssetId is null)
        {
            return null;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var blobName = await db.BlobMediaAssets
            .AsNoTracking()
            .Where(a => a.Id == belegAssetId)
            .Select(a => a.BlobName)
            .FirstOrDefaultAsync(cancellationToken);

        return blobName is null ? null : mediaStorage.GetPublicUrl(blobName);
    }
}
