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

    public record VerkaufsPositionInput(Guid LeistungId, int Menge, Guid? RabattId, bool BedingungBestaetigt);

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

            var zeilenBetrag = leistung.Preis * pos.Menge;
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
                EinzelPreis = leistung.Preis,
                Menge = pos.Menge,
                RabattId = rabatt?.Id,
                RabattBetrag = rabattBetrag,
                ZeilenSumme = zeilenBetrag - rabattBetrag
            });

            // Bestand mindern (auch unter 0 erlaubt, mit Hinweis).
            if (leistung.HatStueckzahl)
            {
                leistung.Stueckzahl -= pos.Menge;
                if (leistung.Stueckzahl < 0)
                {
                    warnungen.Add($"« {leistung.Name} » : stock épuisé (vendu au-delà du stock).");
                }
                else if (leistung.MindestbestandWarnung is int min && leistung.Stueckzahl <= min)
                {
                    warnungen.Add($"« {leistung.Name} » : stock bientôt épuisé ({leistung.Stueckzahl} restant).");
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
