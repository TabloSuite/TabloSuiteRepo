using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Infrastruktur;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services.Storage;
using ENote.WebApp.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Verwaltung von Leistungen/Artikeln und Rabatten eines Salons. Prüft die
/// salonbezogenen Rechte serverseitig (Besitzer oder delegierter Kassierer).
/// </summary>
public class KatalogService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    UserManager<AppUser> userManager,
    IMediaStorageService mediaStorage)
{
    /// <summary>
    /// Alle Salon-Zuordnungen eines Nutzers (ein Nutzer kann mehreren Läden
    /// zugeordnet sein, z. B. ein Kassierer an zwei Läden desselben Besitzers).
    /// </summary>
    public async Task<List<UserSalon>> GetUserSalonsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.UserSalons
            .Include(us => us.Salon)
            .AsNoTracking()
            .Where(us => us.AppUserId == userId)
            .OrderBy(us => us.Salon.Name)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Ermittelt die erste Salon-Zuordnung eines Nutzers (Kompatibilität für
    /// Kontexte ohne aktive Salon-Wahl). Bevorzugt freigegebene Salons.
    /// </summary>
    public async Task<UserSalon?> GetUserSalonAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var salone = await GetUserSalonsAsync(userId, cancellationToken);
        return salone.FirstOrDefault(s => s.Salon.IstFreigegeben) ?? salone.FirstOrDefault();
    }

    public async Task<bool> DarfLeistungenVerwaltenAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var zuordnung = await GetUserSalonAsync(userId, cancellationToken);
        if (zuordnung is null || !zuordnung.Salon.IstFreigegeben)
        {
            return false;
        }

        var istBesitzer = await IstBesitzerAsync(userId);
        return istBesitzer || zuordnung.KannLeistungenVerwalten;
    }

    public async Task<bool> DarfRabatteVerwaltenAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var zuordnung = await GetUserSalonAsync(userId, cancellationToken);
        if (zuordnung is null || !zuordnung.Salon.IstFreigegeben)
        {
            return false;
        }

        var istBesitzer = await IstBesitzerAsync(userId);
        return istBesitzer || zuordnung.KannRabatteVerwalten;
    }

    private async Task<bool> IstBesitzerAsync(Guid userId)
    {
        // Eigener Factory-Context statt UserManager: der UserManager teilt sich im Blazor-Circuit
        // einen DbContext, parallele Komponenten-Abfragen führten sonst zu Concurrency-Fehlern.
        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await db.UserRoles.AnyAsync(ur => ur.UserId == userId
            && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.Besitzer));
    }

    // --- Kategorien ---

    public async Task<List<LeistungKategorie>> GetKategorienAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.LeistungKategorien
            .AsNoTracking()
            .Where(k => k.SalonId == salonId && k.IstAktiv)
            .OrderBy(k => k.Name)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Kategorie-Baum: Hauptkategorien mit ihren Unterkategorien (1 Ebene).</summary>
    public async Task<List<KategorieBaum>> GetKategorieBaumAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var alle = await db.LeistungKategorien
            .AsNoTracking()
            .Where(k => k.SalonId == salonId && k.IstAktiv)
            .OrderBy(k => k.Name)
            .ToListAsync(cancellationToken);

        return alle
            .Where(k => k.ParentId is null)
            .Select(h => new KategorieBaum(h, alle.Where(u => u.ParentId == h.Id).OrderBy(u => u.Name).ToList()))
            .OrderBy(h => h.Kategorie.Name)
            .ToList();
    }

    public record KategorieBaum(LeistungKategorie Kategorie, List<LeistungKategorie> Unterkategorien);

    /// <summary>Legt eine Unterkategorie an (validiert: Parent existiert, ist Hauptkategorie, kein Duplikat).</summary>
    public async Task<(bool Erfolg, string? Fehler, LeistungKategorie? Kategorie)> SpeichereUnterkategorieAsync(
        Guid salonId, Guid parentId, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return (false, "Veuillez saisir un nom.", null);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var parent = await db.LeistungKategorien
            .FirstOrDefaultAsync(k => k.Id == parentId && k.SalonId == salonId, cancellationToken);
        if (parent is null)
        {
            return (false, "Catégorie parente introuvable.", null);
        }
        if (parent.ParentId is not null)
        {
            return (false, "Une sous-catégorie ne peut pas avoir de sous-catégorie.", null);
        }

        var bereinigt = name.Trim();
        var duplikat = await db.LeistungKategorien
            .AnyAsync(k => k.SalonId == salonId && k.Name == bereinigt, cancellationToken);
        if (duplikat)
        {
            return (false, "Une catégorie portant ce nom existe déjà.", null);
        }

        var kategorie = new LeistungKategorie { SalonId = salonId, Name = bereinigt, ParentId = parentId };
        db.LeistungKategorien.Add(kategorie);
        await db.SaveChangesAsync(cancellationToken);
        return (true, null, kategorie);
    }

    /// <summary>Ordnet eine Leistung einer Kategorie/Unterkategorie zu (null = ohne Kategorie).</summary>
    public async Task<bool> SetzeLeistungKategorieAsync(Guid leistungId, Guid salonId, Guid? kategorieId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var leistung = await db.Leistungen
            .FirstOrDefaultAsync(l => l.Id == leistungId && l.SalonId == salonId, cancellationToken);
        if (leistung is null)
        {
            return false;
        }

        if (kategorieId is not null)
        {
            var existiert = await db.LeistungKategorien
                .AnyAsync(k => k.Id == kategorieId && k.SalonId == salonId && k.IstAktiv, cancellationToken);
            if (!existiert)
            {
                return false;
            }
        }

        leistung.KategorieId = kategorieId;
        leistung.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<LeistungKategorie> SpeichereKategorieAsync(Guid salonId, string name, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var bereinigt = name.Trim();

        var vorhanden = await db.LeistungKategorien
            .FirstOrDefaultAsync(k => k.SalonId == salonId && k.Name == bereinigt, cancellationToken);
        if (vorhanden is not null)
        {
            return vorhanden;
        }

        var kategorie = new LeistungKategorie { SalonId = salonId, Name = bereinigt };
        db.LeistungKategorien.Add(kategorie);
        await db.SaveChangesAsync(cancellationToken);
        return kategorie;
    }

    /// <summary>Benennt eine Kategorie um (Leistungen behalten die Verknüpfung über die Id).</summary>
    public async Task<(bool Erfolg, string? Fehler)> BenenneKategorieUmAsync(Guid kategorieId, Guid salonId, string neuerName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(neuerName))
        {
            return (false, "Veuillez saisir un nom.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var kategorie = await db.LeistungKategorien
            .FirstOrDefaultAsync(k => k.Id == kategorieId && k.SalonId == salonId, cancellationToken);
        if (kategorie is null)
        {
            return (false, "Catégorie introuvable.");
        }

        var bereinigt = neuerName.Trim();
        var duplikat = await db.LeistungKategorien
            .AnyAsync(k => k.SalonId == salonId && k.Name == bereinigt && k.Id != kategorieId, cancellationToken);
        if (duplikat)
        {
            return (false, "Une catégorie portant ce nom existe déjà.");
        }

        kategorie.Name = bereinigt;
        await db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    /// <summary>
    /// Löscht eine Kategorie. Verknüpfte Leistungen verlieren dadurch ihre Kategorie
    /// (KategorieId wird null), bleiben aber bestehen.
    /// </summary>
    /// <summary>
    /// Löscht eine Kategorie. Verknüpfte Leistungen verlieren dadurch ihre Kategorie
    /// (KategorieId wird null), bleiben aber bestehen. Eine Hauptkategorie mit
    /// Unterkategorien kann nicht gelöscht werden (zuerst Unterkategorien löschen).
    /// </summary>
    public async Task<(bool Erfolg, string? Fehler)> LoescheKategorieAsync(Guid kategorieId, Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var kategorie = await db.LeistungKategorien
            .FirstOrDefaultAsync(k => k.Id == kategorieId && k.SalonId == salonId, cancellationToken);
        if (kategorie is null)
        {
            return (false, "Catégorie introuvable.");
        }

        // Hauptkategorie mit Unterkategorien nicht löschbar.
        var hatUnter = await db.LeistungKategorien.AnyAsync(k => k.ParentId == kategorieId, cancellationToken);
        if (hatUnter)
        {
            return (false, "Supprimez d'abord les sous-catégories de cette catégorie.");
        }

        // Leistungen mit dieser Kategorie zurücksetzen (Kategorie entfernen, Leistung bleibt).
        var betroffene = await db.Leistungen.Where(l => l.KategorieId == kategorieId).ToListAsync(cancellationToken);
        foreach (var l in betroffene)
        {
            l.KategorieId = null;
        }

        db.LeistungKategorien.Remove(kategorie);
        await db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    // --- Favoriten (app-weit pro Salon) ---

    public async Task<HashSet<Guid>> GetFavoritenIdsAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var ids = await db.LeistungFavoriten
            .AsNoTracking()
            .Where(f => f.SalonId == salonId)
            .Select(f => f.LeistungId)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    public async Task ToggleFavoritAsync(Guid salonId, Guid leistungId, Guid gesetztVonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var vorhanden = await db.LeistungFavoriten
            .FirstOrDefaultAsync(f => f.SalonId == salonId && f.LeistungId == leistungId, cancellationToken);

        if (vorhanden is not null)
        {
            db.LeistungFavoriten.Remove(vorhanden);
        }
        else
        {
            db.LeistungFavoriten.Add(new LeistungFavorit
            {
                SalonId = salonId,
                LeistungId = leistungId,
                GesetztVonId = gesetztVonId
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // --- Leistungen ---

    public async Task<List<Leistung>> GetLeistungenAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Leistungen
            .AsNoTracking()
            .Include(l => l.Kategorie)
            .Where(l => l.SalonId == salonId)
            .OrderBy(l => l.HatStueckzahl)
            .ThenBy(l => l.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Leistung?> GetLeistungAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Leistungen.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
    }

    public async Task<Leistung> SpeichereLeistungAsync(Leistung leistung, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Barcode automatisch generieren, wenn leer (eindeutig pro Salon).
        if (string.IsNullOrWhiteSpace(leistung.StrichCode))
        {
            leistung.StrichCode = await GeneriereEindeutigenBarcodeAsync(db, leistung.SalonId, cancellationToken);
        }

        // Anlegen nur mit Id == Guid.Empty; jede andere (nicht vorhandene) Id ist ein Fehler.
        // So wird versehentliches Anlegen über den Bearbeiten-Pfad verhindert (Ladenwechsel-Schutz).
        if (leistung.Id == Guid.Empty)
        {
            leistung.Id = Guid.NewGuid();
            leistung.CreatedAtUtc = DateTime.UtcNow;
            db.Leistungen.Add(leistung);
        }
        else
        {
            var vorhanden = await db.Leistungen.FirstOrDefaultAsync(l => l.Id == leistung.Id, cancellationToken)
                ?? throw new InvalidOperationException("Leistung nicht gefunden.");


            vorhanden.Name = leistung.Name;
            vorhanden.Beschreibung = leistung.Beschreibung;
            vorhanden.Typ = leistung.Typ;
            vorhanden.Preis = leistung.Preis;
            vorhanden.Kaufpreis = leistung.Typ == LeistungsTyp.Prestation ? null : leistung.Kaufpreis;
            vorhanden.StrichCode = string.IsNullOrWhiteSpace(leistung.StrichCode) ? null : leistung.StrichCode.Trim();
            vorhanden.KategorieId = leistung.KategorieId;
            vorhanden.HatStueckzahl = leistung.HatStueckzahl;
            vorhanden.Stueckzahl = leistung.HatStueckzahl ? leistung.Stueckzahl : 0;
            vorhanden.MindestbestandWarnung = leistung.HatStueckzahl ? leistung.MindestbestandWarnung : null;
            vorhanden.BasisLeistungId = leistung.BasisLeistungId;
            vorhanden.BasisAbzugMenge = leistung.BasisAbzugMenge;
            vorhanden.IstAktiv = leistung.IstAktiv;
            vorhanden.BildAssetId = leistung.BildAssetId;
            vorhanden.UpdatedAtUtc = DateTime.UtcNow;
            leistung = vorhanden;
        }

        await db.SaveChangesAsync(cancellationToken);
        return leistung;
    }

    /// <summary>Generiert einen neuen eindeutigen Barcode für einen Salon (öffentlich, für UI-Vorausfüllung).</summary>
    public async Task<string> GeneriereNeuenBarcodeAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await GeneriereEindeutigenBarcodeAsync(db, salonId, cancellationToken);
    }

    /// <summary>Generiert einen eindeutigen Barcode pro Salon (Format: 9 + Salon-Kurz-ID + Zufall).</summary>
    private static async Task<string> GeneriereEindeutigenBarcodeAsync(ApplicationDbContext db, Guid salonId, CancellationToken cancellationToken)
    {
        // Format: 9 (Präfix für interne Codes) + 4 Ziffern aus Salon-ID + 7 Zufallsziffern = 12 Stellen (EAN-12-ähnlich).
        var salonKurz = Math.Abs(salonId.GetHashCode()) % 10000;
        var random = new Random();
        string barcode;
        int versuche = 0;
        do
        {
            var zufall = random.Next(0, 10000000); // 7 Ziffern
            barcode = $"9{salonKurz:D4}{zufall:D7}";
            versuche++;
            if (versuche > 100) throw new InvalidOperationException("Konnte keinen eindeutigen Barcode generieren.");
        }
        while (await db.Leistungen.AnyAsync(l => l.SalonId == salonId && l.StrichCode == barcode, cancellationToken));

        return barcode;
    }

    public async Task LoescheLeistungAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var leistung = await db.Leistungen.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (leistung is not null)
        {
            db.Leistungen.Remove(leistung);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Speichert ein Bild für eine Leistung im Medienspeicher und verknüpft es
    /// als BlobMediaAsset mit der Leistung.
    /// </summary>
    public async Task<(bool Erfolg, string? Fehler, string? BildUrl)> SpeichereLeistungsBildAsync(
        Guid leistungId,
        string dateiName,
        string contentType,
        Stream inhalt,
        Guid hochgeladenVonId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var leistung = await db.Leistungen.FirstOrDefaultAsync(l => l.Id == leistungId, cancellationToken);
        if (leistung is null)
        {
            return (false, "Prestation introuvable.", null);
        }

        if (!await LadenZugriff.HatZugangAsync(db, hochgeladenVonId, leistung.SalonId, cancellationToken)
            || !await db.UserSalons.AnyAsync(us => us.AppUserId == hochgeladenVonId && us.SalonId == leistung.SalonId
                && us.KannLeistungenVerwalten, cancellationToken)
                && !await LadenZugriff.IstBesitzerAsync(db, hochgeladenVonId, leistung.SalonId, cancellationToken))
            return (false, "Accès non autorisé.", null);

        var ergebnis = await mediaStorage.SaveAsync(new MediaSaveRequest
        {
            DateiName = dateiName,
            ContentType = contentType,
            Inhalt = inhalt,
            SalonId = leistung.SalonId
        }, cancellationToken);

        if (!ergebnis.Erfolg)
        {
            return (false, ergebnis.Fehler, null);
        }

        var asset = new BlobMediaAsset
        {
            SalonId = leistung.SalonId,
            DateiName = dateiName,
            BlobName = ergebnis.BlobName!,
            ContentType = contentType,
            GroesseBytes = ergebnis.GroesseBytes,
            HochgeladenVonId = hochgeladenVonId
        };

        db.BlobMediaAssets.Add(asset);
        await db.SaveChangesAsync(cancellationToken);

        leistung.BildAssetId = asset.Id;
        leistung.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return (true, null, mediaStorage.GetPublicUrl(ergebnis.BlobName!));
    }

    /// <summary>Bild-URLs aller Leistungen eines Ladens (eine Abfrage, für die Kassen-Kacheln).</summary>
    public async Task<Dictionary<Guid, string>> GetLeistungsBildUrlsAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var bilder = await db.Leistungen
            .AsNoTracking()
            .Where(l => l.SalonId == salonId && l.BildAssetId != null)
            .Select(l => new { l.Id, l.BildAsset!.BlobName })
            .ToListAsync(cancellationToken);

        return bilder
            .Where(b => !string.IsNullOrWhiteSpace(b.BlobName))
            .ToDictionary(b => b.Id, b => mediaStorage.GetPublicUrl(b.BlobName));
    }

    /// <summary>Liefert die abrufbare URL eines Leistungs-Bildes (falls vorhanden).</summary>
    public async Task<string?> GetLeistungsBildUrlAsync(Guid? bildAssetId, CancellationToken cancellationToken = default)
    {
        if (bildAssetId is null)
        {
            return null;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var blobName = await db.BlobMediaAssets
            .AsNoTracking()
            .Where(a => a.Id == bildAssetId)
            .Select(a => a.BlobName)
            .FirstOrDefaultAsync(cancellationToken);

        return blobName is null ? null : mediaStorage.GetPublicUrl(blobName);
    }

    // --- Rabatte ---

    public async Task<List<Rabatt>> GetRabatteAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Rabatte
            .AsNoTracking()
            .Include(r => r.Leistung)
            .Where(r => r.SalonId == salonId)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Rabatt> SpeichereRabattAsync(Rabatt rabatt, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (rabatt.Id == Guid.Empty)
        {
            rabatt.Id = Guid.NewGuid();
            rabatt.CreatedAtUtc = DateTime.UtcNow;
            db.Rabatte.Add(rabatt);
        }
        else
        {
            var vorhanden = await db.Rabatte.FirstOrDefaultAsync(r => r.Id == rabatt.Id, cancellationToken)
                ?? throw new InvalidOperationException("Rabatt nicht gefunden.");

            vorhanden.Name = rabatt.Name;
            vorhanden.Bedingung = rabatt.Bedingung;
            vorhanden.Art = rabatt.Art;
            vorhanden.Wert = rabatt.Wert;
            vorhanden.LeistungId = rabatt.LeistungId;
            vorhanden.IstAktiv = rabatt.IstAktiv;
            vorhanden.GueltigVonUtc = rabatt.GueltigVonUtc;
            vorhanden.GueltigBisUtc = rabatt.GueltigBisUtc;
            rabatt = vorhanden;
        }

        await db.SaveChangesAsync(cancellationToken);
        return rabatt;
    }

    public async Task LoescheRabattAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rabatt = await db.Rabatte.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rabatt is not null)
        {
            db.Rabatte.Remove(rabatt);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
