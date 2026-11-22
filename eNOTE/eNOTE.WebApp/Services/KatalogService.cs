using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Infrastruktur;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services.Katalog;
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
        if (PruefeKategorieName(name) is string namensFehler)
        {
            return (false, namensFehler, null);
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
        leistung.UpdatedAtUtc = ENote.Core.Services.AppUhr.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Legt eine Hauptkategorie an (gleichnamige wird zurückgegeben); zu langer Name → <see cref="ArgumentException"/>.</summary>
    public async Task<LeistungKategorie> SpeichereKategorieAsync(Guid salonId, string name, CancellationToken cancellationToken = default)
    {
        if (PruefeKategorieName(name) is string fehler) throw new ArgumentException(fehler);
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
    public Task<(bool Erfolg, string? Fehler)> BenenneKategorieUmAsync(Guid kategorieId, Guid salonId, string neuerName, CancellationToken cancellationToken = default)
        => AktualisiereKategorieAsync(kategorieId, salonId, neuerName, farbe: null, farbeSetzen: false, cancellationToken);

    /// <summary>
    /// Ändert Name und Farbe einer Kategorie. <paramref name="farbe"/> null/leer = automatisch
    /// (Unterkategorien übernehmen dann die Farbe der Hauptkategorie).
    /// </summary>
    public Task<(bool Erfolg, string? Fehler)> AktualisiereKategorieAsync(Guid kategorieId, Guid salonId, string neuerName, string? farbe, CancellationToken cancellationToken = default)
        => AktualisiereKategorieAsync(kategorieId, salonId, neuerName, farbe, farbeSetzen: true, cancellationToken);

    private async Task<(bool Erfolg, string? Fehler)> AktualisiereKategorieAsync(Guid kategorieId, Guid salonId, string neuerName, string? farbe, bool farbeSetzen, CancellationToken cancellationToken)
    {
        if (PruefeKategorieName(neuerName) is string namensFehler)
        {
            return (false, namensFehler);
        }

        if (farbeSetzen && !KategorieFarben.IstGueltig(farbe))
        {
            return (false, "Couleur invalide.");
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
        if (farbeSetzen)
        {
            kategorie.Farbe = KategorieFarben.Normalisiere(farbe);
        }

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

    /// <summary>
    /// Längen und Höchstwerte (Fehlersuche 21.11.2026): ein 17-stelliger Preis sprengte decimal(18,2), ein Name über
    /// 200 Zeichen ließ « Déclarer une perte » und « Retrait » (Kopie des Namens in nvarchar(200)) mit Datenbankfehler
    /// scheitern, eine Kategorie über 450 Zeichen den eindeutigen Index. Höchstbetrag wie <c>BetragEingabe</c>.
    /// </summary>
    public const int LeistungNameMax = 200, KategorieNameMax = 100;
    public const decimal BetragMax = 999_999_999m;

    /// <summary>Null = in Ordnung, sonst die französische Meldung (Formular und Server).</summary>
    public static string? PruefeLeistung(Leistung leistung)
    {
        if (string.IsNullOrWhiteSpace(leistung.Name)) return "Veuillez saisir un nom.";
        if (leistung.Name.Trim().Length > LeistungNameMax) return $"Nom : {LeistungNameMax} caractères au maximum.";
        if (leistung.Preis < 0 || leistung.Kaufpreis < 0) return "Le prix ne peut pas être négatif.";
        if (leistung.Preis > BetragMax || leistung.Kaufpreis > BetragMax) return "Prix trop élevé : 999.999.999 F au maximum.";
        if (Math.Abs(leistung.Stueckzahl) > BetragMax || leistung.MindestbestandWarnung > BetragMax || leistung.BasisAbzugMenge > BetragMax)
            return "Quantité trop élevée : 999.999.999 au maximum.";
        return PreisEingabe.Pruefe(leistung.Preis, leistung.Kaufpreis);
    }

    /// <summary>Name einer Kategorie/Unterkategorie: Pflicht, höchstens <see cref="KategorieNameMax"/> Zeichen.</summary>
    public static string? PruefeKategorieName(string? name)
        => string.IsNullOrWhiteSpace(name) ? "Veuillez saisir un nom."
            : name.Trim().Length > KategorieNameMax ? $"Nom de catégorie : {KategorieNameMax} caractères au maximum." : null;

    public async Task<Leistung> SpeichereLeistungAsync(Leistung leistung, CancellationToken cancellationToken = default,
        decimal? stueckzahlBeimOeffnen = null)
    {
        if (PruefeLeistung(leistung) is string eingabeFehler)
        {
            throw new InvalidOperationException(eingabeFehler);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Barcode: eingegebenen Code prüfen (Format, eindeutig pro Laden); leer → automatisch erzeugen
        // (auch beim Bearbeiten, damit jeder Artikel scanbar bleibt).
        var pruefung = Core.Services.Katalog.BarcodeRegeln.Pruefe(leistung.StrichCode);
        if (pruefung.Fehler)
        {
            throw new InvalidOperationException(pruefung.Hinweis);
        }

        if (pruefung.Code is string code)
        {
            var belegt = await db.Leistungen.AsNoTracking()
                .Where(l => l.SalonId == leistung.SalonId && l.StrichCode == code && l.Id != leistung.Id)
                .Select(l => l.Name).FirstOrDefaultAsync(cancellationToken);
            if (belegt is not null)
            {
                throw new InvalidOperationException($"Ce code-barres est déjà utilisé par « {belegt} ».");
            }

            leistung.StrichCode = code;
        }
        else
        {
            leistung.StrichCode = await GeneriereEindeutigenBarcodeAsync(db, leistung.SalonId, cancellationToken);
        }

        // Anlegen nur mit Id == Guid.Empty; jede andere (nicht vorhandene) Id ist ein Fehler.
        // So wird versehentliches Anlegen über den Bearbeiten-Pfad verhindert (Ladenwechsel-Schutz).
        if (leistung.Id == Guid.Empty)
        {
            leistung.Id = Guid.NewGuid();
            leistung.CreatedAtUtc = ENote.Core.Services.AppUhr.UtcNow;
            db.Leistungen.Add(leistung);
        }
        else
        {
            var vorhanden = await db.Leistungen.FirstOrDefaultAsync(l => l.Id == leistung.Id, cancellationToken)
                ?? throw new InvalidOperationException("Leistung nicht gefunden.");
            var hatteStueckzahl = vorhanden.HatStueckzahl;
            vorhanden.Name = leistung.Name;
            vorhanden.Beschreibung = leistung.Beschreibung;
            vorhanden.Typ = leistung.Typ;
            vorhanden.Preis = leistung.Preis;
            vorhanden.Kaufpreis = leistung.Typ == LeistungsTyp.Prestation ? null : leistung.Kaufpreis;
            vorhanden.StrichCode = leistung.StrichCode;
            vorhanden.KategorieId = leistung.KategorieId;
            vorhanden.HatStueckzahl = leistung.HatStueckzahl;
            // Das Formular zeigt den Bestand beim Öffnen. Inzwischen gebuchte Verkäufe/Abgänge nicht überschreiben:
            // nur die Änderung aus dem Formular übernehmen (sonst « kommt » verkaufte Ware beim Speichern eines Preises zurück).
            vorhanden.Stueckzahl = !leistung.HatStueckzahl ? 0
                : hatteStueckzahl && stueckzahlBeimOeffnen is decimal beimOeffnen
                    ? vorhanden.Stueckzahl + (leistung.Stueckzahl - beimOeffnen)
                    : leistung.Stueckzahl;
            vorhanden.MindestbestandWarnung = leistung.HatStueckzahl ? leistung.MindestbestandWarnung : null;
            vorhanden.BasisLeistungId = leistung.BasisLeistungId;
            vorhanden.BasisAbzugMenge = leistung.BasisAbzugMenge;
            vorhanden.IstAktiv = leistung.IstAktiv;
            vorhanden.BildAssetId = leistung.BildAssetId;
            vorhanden.UpdatedAtUtc = ENote.Core.Services.AppUhr.UtcNow;
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

    /// <summary>
    /// Ergebnis der Barcode-Prüfung für das Eingabefeld: Format-Hinweis plus Dublette im Laden.
    /// </summary>
    public async Task<Core.Services.Katalog.BarcodeRegeln.Pruefung> PruefeBarcodeAsync(
        Guid salonId, string? eingabe, Guid leistungId, CancellationToken cancellationToken = default)
    {
        var pruefung = Core.Services.Katalog.BarcodeRegeln.Pruefe(eingabe);
        if (pruefung.Fehler || pruefung.Code is not string code)
        {
            return pruefung;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var belegt = await db.Leistungen.AsNoTracking()
            .Where(l => l.SalonId == salonId && l.StrichCode == code && l.Id != leistungId)
            .Select(l => l.Name).FirstOrDefaultAsync(cancellationToken);
        return belegt is null
            ? pruefung
            : pruefung with { Hinweis = $"Déjà utilisé par « {belegt} » : choisissez un autre code.", Fehler = true, Warnung = false };
    }

    /// <summary>Erzeugt einen eindeutigen internen Code (gültige EAN-13, Präfix „2“ = laden-intern).</summary>
    private static async Task<string> GeneriereEindeutigenBarcodeAsync(ApplicationDbContext db, Guid salonId, CancellationToken cancellationToken)
    {
        var zufall = new Random();
        for (var versuch = 0; versuch < 100; versuch++)
        {
            var barcode = Core.Services.Katalog.BarcodeRegeln.ErzeugeIntern(zufall);
            if (!await db.Leistungen.AnyAsync(l => l.SalonId == salonId && l.StrichCode == barcode, cancellationToken))
            {
                return barcode;
            }
        }

        throw new InvalidOperationException("Impossible de générer un code-barres unique. Réessayez.");
    }

    /// <summary>
    /// Löscht eine Leistung. Zeigen schon Verkäufe, Lagerabgänge (Perte/Retrait), eine Remise oder ein verknüpfter Artikel
    /// (Stock de base) auf sie, wird sie nur deaktiviert – wie bei den Remises: Belege und Zahlen bleiben richtig, an der
    /// Kasse erscheint sie nicht mehr. (16.11.2026: vorher scheiterte das Löschen dann an der Datenbank, unbehandelt.)
    /// </summary>
    /// <returns><c>true</c> = gelöscht, <c>false</c> = nur deaktiviert.</returns>
    public async Task<bool> LoescheLeistungAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var leistung = await db.Leistungen.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (leistung is not null)
        {
            // Für einen anwesenden Kunden erfasst und noch nicht bezahlt → erst kassieren oder zurücknehmen.
            var erfasst = await db.AufenthaltsPositionen.Where(a => a.LeistungId == id).ToListAsync(cancellationToken);
            if (erfasst.Any(a => a.VerkaufId == null && a.ZurueckgenommenUtc == null))
            {
                throw new ArgumentException("Cette prestation est enregistrée pour un client présent et pas encore payée : encaissez-la ou retirez-la d'abord.");
            }

            var benutzt = await db.VerkaufsPositionen.AnyAsync(p => p.LeistungId == id, cancellationToken)
                          || await db.LagerAbgaenge.AnyAsync(a => a.LeistungId == id, cancellationToken)
                          || await db.Rabatte.AnyAsync(r => r.LeistungId == id, cancellationToken)
                          || await db.Leistungen.AnyAsync(l => l.BasisLeistungId == id, cancellationToken);
            if (benutzt)
            {
                leistung.IstAktiv = false;
                await db.SaveChangesAsync(cancellationToken);
                return false;
            }

            foreach (var a in erfasst) a.LeistungId = null;

            // Tickets « en attente » verlieren diese Zeile; ein dadurch leeres Ticket verschwindet ganz.
            var zeilen = await db.GeparkteTicketPositionen.Where(p => p.LeistungId == id).ToListAsync(cancellationToken);
            if (zeilen.Count > 0)
            {
                var ticketIds = zeilen.Select(z => z.TicketId).Distinct().ToList();
                db.GeparkteTicketPositionen.RemoveRange(zeilen);
                var leer = await db.GeparkteTickets.Where(t => ticketIds.Contains(t.Id) && !t.Positionen.Any(p => p.LeistungId != id)).ToListAsync(cancellationToken);
                db.GeparkteTickets.RemoveRange(leer);
            }
            db.Leistungen.Remove(leistung);
            await db.SaveChangesAsync(cancellationToken);
        }
        return true;
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
        leistung.UpdatedAtUtc = ENote.Core.Services.AppUhr.UtcNow;
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

    public const int RabattNameMax = 120, RabattBedingungMax = 200;

    /// <summary>Regeln einer Remise (Formular und Server): Name, Wert &gt; 0, höchstens 100 %, Längen der Texte.</summary>
    public static string? PruefeRabatt(Rabatt rabatt)
    {
        if (string.IsNullOrWhiteSpace(rabatt.Name)) return "Veuillez saisir un nom.";
        if (rabatt.Name.Trim().Length > RabattNameMax) return $"Nom : {RabattNameMax} caractères au maximum.";
        if (rabatt.Bedingung is { } b && b.Trim().Length > RabattBedingungMax) return $"Condition : {RabattBedingungMax} caractères au maximum.";
        if (rabatt.Wert < 0) return "La valeur ne peut pas être négative.";
        if (rabatt.Wert == 0) return "Saisissez une valeur supérieure à 0.";
        if (rabatt.Art == RabattArt.Prozent && rabatt.Wert > 100) return "Le pourcentage ne peut pas dépasser 100.";
        if (rabatt.Wert > BetragMax) return "Montant trop élevé : 999.999.999 F au maximum.";
        return null;
    }

    /// <summary>Schalter auf der Karte: nur aktiv/inaktiv (auch für ältere Remises, die die heutigen Regeln nicht erfüllen).</summary>
    public async Task<bool> SetzeRabattAktivAsync(Guid id, Guid salonId, bool aktiv, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rabatt = await db.Rabatte.FirstOrDefaultAsync(r => r.Id == id && r.SalonId == salonId, cancellationToken);
        if (rabatt is null) return false;
        rabatt.IstAktiv = aktiv;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Speichert eine Remise; ungültige Eingaben (auch zu lange Texte) → <see cref="ArgumentException"/>.</summary>
    public async Task<Rabatt> SpeichereRabattAsync(Rabatt rabatt, CancellationToken cancellationToken = default)
    {
        if (PruefeRabatt(rabatt) is string fehler) throw new ArgumentException(fehler);
        rabatt.Name = rabatt.Name.Trim();
        rabatt.Bedingung = string.IsNullOrWhiteSpace(rabatt.Bedingung) ? null : rabatt.Bedingung.Trim();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (rabatt.Id == Guid.Empty)
        {
            rabatt.Id = Guid.NewGuid();
            rabatt.CreatedAtUtc = ENote.Core.Services.AppUhr.UtcNow;
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

    /// <summary>
    /// Löscht eine Remise. Wurde sie schon auf Bons verwendet, wird sie nur deaktiviert
    /// (Bons verweisen darauf). Liefert <c>true</c> bei echter Löschung.
    /// </summary>
    public async Task<bool> LoescheRabattAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rabatt = await db.Rabatte.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rabatt is null)
        {
            return true;
        }

        if (await db.VerkaufsPositionen.AnyAsync(p => p.RabattId == id, cancellationToken))
        {
            rabatt.IstAktiv = false;
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }

        db.Rabatte.Remove(rabatt);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
