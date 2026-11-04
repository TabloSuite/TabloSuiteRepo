using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services.Kasse;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Kassendomäne: Sitzung per PIN starten, Verkäufe (Bar/Karte) buchen,
/// Kassenabschluss. Läuft über IDbContextFactory, damit parallele
/// Tablet-/Besitzer-Zugriffe keinen geteilten DbContext verwenden.
/// </summary>
public class KassenService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IUmsatzBenachrichtigungService umsatzBenachrichtigung)
{
    public record StartErgebnis(bool Erfolg, string? Fehler, KassenSitzung? Sitzung, int VerbleibendeVersuche);

    /// <summary>Aktuell offene Kassensitzung eines Salons (höchstens eine).</summary>
    public async Task<KassenSitzung?> GetOffeneSitzungAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.KassenSitzungen
            .Include(k => k.Kassierer)
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.SalonId == salonId && k.Status == KassenSitzungStatus.Offen, cancellationToken);
    }

    /// <summary>Kassierer eines Salons für die PIN-Auswahl am Tablet.</summary>
    public async Task<List<(Guid UserId, string Name)>> GetKassiererAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.UserSalons
            .AsNoTracking()
            .Where(us => us.SalonId == salonId && us.KassenPinHash != null)
            .Select(us => new ValueTuple<Guid, string>(us.AppUserId, us.AppUser.FullName))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Löst über Name + PIN die eindeutige Salon-Zuordnung auf. Da die PIN an der
    /// UserSalon-Zuordnung hängt, öffnet sie automatisch den richtigen Laden –
    /// auch wenn ein Kassierer an mehreren Läden freigegeben ist.
    /// </summary>
    public async Task<(bool Erfolg, string? Fehler, UserSalon? Zuordnung, int VerbleibendeVersuche)> FindeZuordnungPerPinAsync(
        Guid kassiererId,
        string pin,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Alle Salon-Zuordnungen dieses Kassierers mit PIN (kann mehrere Läden sein).
        var zuordnungen = await db.UserSalons
            .Include(us => us.AppUser)
            .Include(us => us.Salon)
            .Where(us => us.AppUserId == kassiererId && us.KassenPinHash != null)
            .ToListAsync(cancellationToken);

        if (zuordnungen.Count == 0)
        {
            return (false, "Caissier introuvable ou sans code PIN.", null, 0);
        }

        var jetzt = DateTime.UtcNow;
        UserSalon? treffer = null;

        foreach (var zuordnung in zuordnungen)
        {
            if (zuordnung.PinGesperrtBisUtc is DateTime gesperrtBis && gesperrtBis > jetzt)
            {
                continue;
            }

            if (KassenPinService.PruefePin(pin, zuordnung.KassenPinHash!))
            {
                treffer = zuordnung;
                break;
            }
        }

        if (treffer is null)
        {
            // Fehlversuch auf alle nicht gesperrten Zuordnungen anrechnen.
            var verbleibend = KassenPinService.MaxFehlversuche;
            foreach (var zuordnung in zuordnungen)
            {
                if (zuordnung.PinGesperrtBisUtc is DateTime g && g > jetzt)
                {
                    continue;
                }

                zuordnung.PinFehlversuche++;
                if (zuordnung.PinFehlversuche >= KassenPinService.MaxFehlversuche)
                {
                    zuordnung.PinGesperrtBisUtc = jetzt.Add(KassenPinService.SperrDauer);
                    zuordnung.PinFehlversuche = 0;
                }

                verbleibend = Math.Min(verbleibend, Math.Max(0, KassenPinService.MaxFehlversuche - zuordnung.PinFehlversuche));
            }

            await db.SaveChangesAsync(cancellationToken);
            return (false, "Code PIN incorrect.", null, verbleibend);
        }

        // Erfolg: Zähler/Sperre der getroffenen Zuordnung zurücksetzen.
        treffer.PinFehlversuche = 0;
        treffer.PinGesperrtBisUtc = null;
        await db.SaveChangesAsync(cancellationToken);

        return (true, null, treffer, 0);
    }

    /// <summary>
    /// Prüft die Besitzer-PIN für einen Salon (zum Freischalten der Verwaltung am
    /// gemeinsamen Tablet). Sucht die Besitzer-Zuordnung mit gesetzter Besitzer-PIN.
    /// </summary>
    public async Task<(bool Erfolg, string? Fehler)> PruefeBesitzerPinAsync(
        Guid salonId, string pin, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var besitzerZuordnungen = await db.UserSalons
            .Include(us => us.AppUser)
            .Where(us => us.SalonId == salonId && us.BesitzerPinHash != null)
            .ToListAsync(cancellationToken);

        if (besitzerZuordnungen.Count == 0)
        {
            return (false, "Aucun code PIN propriétaire configuré.");
        }

        var jetzt = DateTime.UtcNow;
        foreach (var zuordnung in besitzerZuordnungen)
        {
            if (zuordnung.PinGesperrtBisUtc is DateTime gesperrtBis && gesperrtBis > jetzt)
            {
                continue;
            }

            if (KassenPinService.PruefePin(pin, zuordnung.BesitzerPinHash!))
            {
                zuordnung.PinFehlversuche = 0;
                zuordnung.PinGesperrtBisUtc = null;
                await db.SaveChangesAsync(cancellationToken);
                return (true, null);
            }
        }

        // Fehlversuch anrechnen (Sperrlogik geteilt mit Kassen-PIN).
        foreach (var zuordnung in besitzerZuordnungen)
        {
            if (zuordnung.PinGesperrtBisUtc is DateTime g && g > jetzt)
            {
                continue;
            }

            zuordnung.PinFehlversuche++;
            if (zuordnung.PinFehlversuche >= KassenPinService.MaxFehlversuche)
            {
                zuordnung.PinGesperrtBisUtc = jetzt.Add(KassenPinService.SperrDauer);
                zuordnung.PinFehlversuche = 0;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return (false, "Code PIN incorrect.");
    }

    /// <summary>Prüft, ob ein Salon eine konfigurierte Besitzer-PIN hat.</summary>
    public async Task<bool> HatBesitzerPinAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.UserSalons
            .AnyAsync(us => us.SalonId == salonId && us.BesitzerPinHash != null, cancellationToken);
    }

    /// <summary>
    /// Stellt sicher, dass ein Laden den Standard-Artikel „Divers“ hat (freier Betrag
    /// in der Kasse, für vergessene Leistungen). Wird beim Kassenstart geprüft.
    /// </summary>
    public async Task StelleStandardArtikelSicherAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var vorhanden = await db.Leistungen
            .AnyAsync(l => l.SalonId == salonId && l.IstStandardArtikel, cancellationToken);
        if (vorhanden)
        {
            return;
        }

        db.Leistungen.Add(new Core.DataModels.Katalog.Leistung
        {
            SalonId = salonId,
            Name = "Divers",
            Beschreibung = "Montant libre (prestation non créée)",
            Preis = 0m,
            IstStandardArtikel = true,
            IstAktiv = true
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Startet eine Kassensitzung: PIN prüfen (mit Sperrlogik) und den gezählten
    /// Wechselgeld-Startbestand bestätigen. Der Salon ergibt sich aus der Zuordnung.
    /// </summary>
    public async Task<StartErgebnis> StarteSitzungAsync(
        Guid salonId,
        Guid kassiererId,
        decimal startbestand,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var hatOffene = await db.KassenSitzungen
            .AnyAsync(k => k.SalonId == salonId && k.Status == KassenSitzungStatus.Offen, cancellationToken);
        if (hatOffene)
        {
            return new(false, "Une caisse est déjà ouverte pour ce salon.", null, 0);
        }

        var zuordnung = await db.UserSalons
            .Include(us => us.AppUser)
            .FirstOrDefaultAsync(us => us.SalonId == salonId && us.AppUserId == kassiererId, cancellationToken);

        if (zuordnung?.KassenPinHash is null)
        {
            return new(false, "Caissier introuvable ou sans code PIN.", null, 0);
        }

        var jetzt = DateTime.UtcNow;

        // Standard-Artikel „Divers“ sicherstellen (freier Betrag für vergessene Leistungen).
        await StelleStandardArtikelSicherAsync(salonId, cancellationToken);

        var sitzung = new KassenSitzung
        {
            SalonId = salonId,
            KassiererId = kassiererId,
            Startbestand = Math.Round(startbestand, 2),
            GestartetUtc = jetzt
        };

        db.KassenSitzungen.Add(sitzung);
        await db.SaveChangesAsync(cancellationToken);

        return new(true, null, sitzung, 0);
    }

    public record VerkaufsPositionInput(Guid LeistungId, int Menge, Guid? RabattId, bool BedingungBestaetigt, decimal? FreierBetrag = null);

    /// <summary>
    /// Bucht einen Verkauf. Bei Barzahlung wird das Rückgeld berechnet und geprüft;
    /// bei Kartenzahlung muss der Kassierer den externen Vorgang bestätigt haben.
    /// Artikel-Bestand wird gemindert (auch unter 0 erlaubt, mit Hinweis).
    /// </summary>
    public async Task<(bool Erfolg, string? Fehler, Verkauf? Verkauf, List<string> Warnungen)> BucheVerkaufAsync(
        Guid kassenSitzungId,
        IReadOnlyList<VerkaufsPositionInput> positionen,
        ZahlungsArt zahlungsArt,
        decimal? gegeben,
        bool karteBestaetigt,
        CancellationToken cancellationToken = default)
    {
        var warnungen = new List<string>();

        if (positionen.Count == 0)
        {
            return (false, "Le ticket est vide.", null, warnungen);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var sitzung = await db.KassenSitzungen
            .FirstOrDefaultAsync(k => k.Id == kassenSitzungId && k.Status == KassenSitzungStatus.Offen, cancellationToken);
        if (sitzung is null)
        {
            return (false, "Aucune caisse ouverte.", null, warnungen);
        }

        var leistungIds = positionen.Select(p => p.LeistungId).Distinct().ToList();
        var leistungen = await db.Leistungen
            .Where(l => leistungIds.Contains(l.Id) && l.SalonId == sitzung.SalonId && l.IstAktiv)
            .ToListAsync(cancellationToken);

        // Basis-Lager-Leistungen der Sammel-Artikel ebenfalls laden (zum Bestandsabzug).
        var basisIds = leistungen.Where(l => l.BasisLeistungId is not null).Select(l => l.BasisLeistungId!.Value).Distinct().ToList();
        if (basisIds.Count > 0)
        {
            var basisLeistungen = await db.Leistungen
                .Where(l => basisIds.Contains(l.Id) && l.SalonId == sitzung.SalonId)
                .ToListAsync(cancellationToken);
            leistungen.AddRange(basisLeistungen.Where(b => leistungen.All(l => l.Id != b.Id)));
        }

        var rabattIds = positionen.Where(p => p.RabattId is not null).Select(p => p.RabattId!.Value).Distinct().ToList();
        var rabatte = await db.Rabatte
            .Where(r => rabattIds.Contains(r.Id) && r.SalonId == sitzung.SalonId)
            .ToListAsync(cancellationToken);

        var verkauf = new Verkauf
        {
            KassenSitzungId = sitzung.Id,
            SalonId = sitzung.SalonId,
            ZahlungsArt = zahlungsArt,
            ZeitUtc = DateTime.UtcNow
        };

        var jetzt = DateTime.UtcNow;
        decimal zwischensumme = 0m;
        decimal rabattSumme = 0m;

        foreach (var pos in positionen)
        {
            var leistung = leistungen.FirstOrDefault(l => l.Id == pos.LeistungId);
            if (leistung is null)
            {
                return (false, "Une prestation du ticket est introuvable ou inactive.", null, warnungen);
            }

            if (pos.Menge <= 0)
            {
                return (false, "La quantité doit être positive.", null, warnungen);
            }

            // Standard-Artikel „Divers“: freier Betrag vom Kassierer.
            decimal einzelPreis = leistung.Preis;
            if (leistung.IstStandardArtikel)
            {
                if (pos.FreierBetrag is null or <= 0)
                {
                    return (false, $"Veuillez saisir le montant pour « {leistung.Name} ».", null, warnungen);
                }

                einzelPreis = pos.FreierBetrag.Value;

                // Besitzer benachrichtigen: Standard-Artikel wurde verwendet.
                var kassierer = await db.Users.FirstOrDefaultAsync(u => u.Id == sitzung.KassiererId, cancellationToken);
                db.BesitzerHinweise.Add(new BesitzerHinweis
                {
                    SalonId = sitzung.SalonId,
                    Text = $"{pos.Menge}× « {leistung.Name} » utilisé(s) en caisse ({einzelPreis * pos.Menge:N0} F). Pensez à créer la prestation.",
                    AusgeloestVonId = sitzung.KassiererId,
                    AusgeloestVonName = kassierer?.FullName ?? "Caissier"
                });
            }

            var zeilenBetrag = einzelPreis * pos.Menge;
            decimal rabattBetrag = 0m;
            Rabatt? rabatt = null;

            if (pos.RabattId is Guid rabattId)
            {
                rabatt = rabatte.FirstOrDefault(r => r.Id == rabattId);
                var gueltig = rabatt is not null && rabatt.IstAktiv
                    && (rabatt.GueltigVonUtc is null || jetzt >= rabatt.GueltigVonUtc)
                    && (rabatt.GueltigBisUtc is null || jetzt <= rabatt.GueltigBisUtc)
                    && (rabatt.LeistungId is null || rabatt.LeistungId == leistung.Id);

                if (!gueltig)
                {
                    return (false, $"La remise choisie pour « {leistung.Name} » n'est pas valide.", null, warnungen);
                }

                // Bedingung (z. B. client fidèle) muss vom Kassierer bestätigt worden sein.
                if (!string.IsNullOrWhiteSpace(rabatt!.Bedingung) && !pos.BedingungBestaetigt)
                {
                    return (false, $"Veuillez confirmer la condition de la remise « {rabatt.Name} ».", null, warnungen);
                }

                var nachRabatt = KassenRechner.WendeRabattAn(zeilenBetrag, rabatt.Art, rabatt.Wert);
                rabattBetrag = zeilenBetrag - nachRabatt;
            }

            zwischensumme += zeilenBetrag;
            rabattSumme += rabattBetrag;

            verkauf.Positionen.Add(new VerkaufsPosition
            {
                LeistungId = leistung.Id,
                LeistungName = leistung.Name,
                EinzelPreis = einzelPreis,
                Menge = pos.Menge,
                RabattId = rabatt?.Id,
                RabattBetrag = rabattBetrag,
                ZeilenSumme = zeilenBetrag - rabattBetrag
            });

            // Bestand mindern (auch unter 0 erlaubt, mit Hinweis).
            // Sammel-Artikel (z. B. „Stoff A (3 m)“) ziehen vom Bestand ihrer Basis-Leistung ab.
            var lagerZiel = leistung.BasisLeistungId is Guid basisId
                ? leistungen.FirstOrDefault(l => l.Id == basisId) ?? leistung
                : leistung;
            var abzug = leistung.BasisLeistungId is not null && leistung.BasisAbzugMenge is decimal menge
                ? menge * pos.Menge
                : pos.Menge;

            if (lagerZiel.HatStueckzahl)
            {
                lagerZiel.Stueckzahl -= abzug;
                if (lagerZiel.Stueckzahl < 0)
                {
                    warnungen.Add($"« {lagerZiel.Name} » : stock épuisé (vendu au-delà du stock).");
                }
                else if (lagerZiel.MindestbestandWarnung is decimal min && lagerZiel.Stueckzahl <= min)
                {
                    warnungen.Add($"« {lagerZiel.Name} » : stock bientôt épuisé ({lagerZiel.Stueckzahl} restant).");
                }
            }
        }

        verkauf.Zwischensumme = Math.Round(zwischensumme, 2);
        verkauf.RabattSumme = Math.Round(rabattSumme, 2);
        verkauf.GesamtSumme = Math.Round(zwischensumme - rabattSumme, 2);

        if (zahlungsArt == ZahlungsArt.Bar)
        {
            if (gegeben is null)
            {
                return (false, "Veuillez saisir le montant reçu.", null, warnungen);
            }

            try
            {
                verkauf.Rueckgeld = KassenRechner.BerechneRueckgeld(verkauf.GesamtSumme, gegeben.Value);
            }
            catch (InvalidOperationException)
            {
                return (false, "Le montant reçu est inférieur au total.", null, warnungen);
            }

            verkauf.Gegeben = gegeben.Value;
        }
        else
        {
            if (!karteBestaetigt)
            {
                return (false, "Veuillez confirmer que le paiement par carte a été effectué.", null, warnungen);
            }

            verkauf.KarteZahlungBestaetigt = true;
        }

        db.Verkaeufe.Add(verkauf);
        await db.SaveChangesAsync(cancellationToken);

        // Live-Update an abonnierte Besitzer-Ansichten desselben Salons.
        try
        {
            var heute = DateTime.UtcNow.Date;
            var tagesUmsatz = await db.Verkaeufe
                .Where(v => v.SalonId == sitzung.SalonId && v.ZeitUtc >= heute)
                .SumAsync(v => v.GesamtSumme, cancellationToken);
            var anzahlHeute = await db.Verkaeufe
                .CountAsync(v => v.SalonId == sitzung.SalonId && v.ZeitUtc >= heute, cancellationToken);

            await umsatzBenachrichtigung.VerkaufGemeldetAsync(sitzung.SalonId, tagesUmsatz, anzahlHeute, cancellationToken);
        }
        catch
        {
            // Live-Benachrichtigung darf den Verkauf nie blockieren.
        }

        return (true, null, verkauf, warnungen);
    }

    /// <summary>
    /// Schließt die offene Sitzung ab (Kassiererwechsel/Tagesende) und dokumentiert
    /// Soll/Ist und die Differenz.
    /// </summary>
    public async Task<(bool Erfolg, string? Fehler, KassenSitzung? Sitzung)> SchliesseSitzungAsync(
        Guid kassenSitzungId,
        decimal endbestandGezaehlt,
        string? notiz,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var sitzung = await db.KassenSitzungen
            .Include(k => k.Verkaeufe)
            .FirstOrDefaultAsync(k => k.Id == kassenSitzungId && k.Status == KassenSitzungStatus.Offen, cancellationToken);
        if (sitzung is null)
        {
            return (false, "Aucune caisse ouverte.", null);
        }

        var soll = KassenRechner.BerechneBarSoll(sitzung.Startbestand, sitzung.Verkaeufe);

        sitzung.Status = KassenSitzungStatus.Abgeschlossen;
        sitzung.AbgeschlossenUtc = DateTime.UtcNow;
        sitzung.BareinnahmenSoll = soll;
        sitzung.EndbestandGezaehlt = Math.Round(endbestandGezaehlt, 2);
        sitzung.Differenz = KassenRechner.BerechneDifferenz(soll, endbestandGezaehlt);
        sitzung.AbschlussNotiz = string.IsNullOrWhiteSpace(notiz) ? null : notiz.Trim();

        await db.SaveChangesAsync(cancellationToken);
        return (true, null, sitzung);
    }
}
