using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services.Kasse;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ENote.WebApp.Services;

/// <summary>
/// Kassendomäne: Sitzung per PIN starten, Verkäufe (Bar/Karte/Mobile Money, kombinierbar) buchen,
/// Kassenabschluss. Läuft über IDbContextFactory, damit parallele
/// Tablet-/Besitzer-Zugriffe keinen geteilten DbContext verwenden.
/// </summary>
public class KassenService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IUmsatzBenachrichtigungService umsatzBenachrichtigung,
    TimeProvider? zeit = null,
    AnwesenheitsSignal? anwesenheit = null)
{
    private readonly TimeProvider uhr = zeit ?? TimeProvider.System;

    /// <summary>
    /// Kassensperre wegen unbezahltem Abo (ab dem 15. nach Fälligkeit, bis der Admin bestätigt).
    /// Abschluss einer laufenden Sitzung bleibt immer möglich.
    /// </summary>
    private async Task<string?> PruefeKassenSperreAsync(Guid salonId, bool synchronisieren, CancellationToken ct)
    {
        var zugang = await new KassenZugangService(dbContextFactory, uhr).PruefeAsync(salonId, ct, synchronisieren);
        return zugang.Gesperrt ? zugang.Hinweis ?? "Caisse suspendue : abonnement impayé." : null;
    }

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
        // Kassierer mit Kassen-PIN plus Besitzer mit Besitzer-PIN (damit der Besitzer
        // auch kassieren kann, wenn er alleiniger Nutzer des Ladens ist).
        return await db.UserSalons
            .AsNoTracking()
            .Where(us => us.SalonId == salonId && (us.KassenPinHash != null || us.BesitzerPinHash != null))
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

        // Alle Salon-Zuordnungen dieses Nutzers mit irgendeiner PIN (Kassen- oder Besitzer-PIN).
        var zuordnungen = await db.UserSalons
            .Include(us => us.AppUser)
            .Include(us => us.Salon)
            .Where(us => us.AppUserId == kassiererId && (us.KassenPinHash != null || us.BesitzerPinHash != null))
            .ToListAsync(cancellationToken);

        if (zuordnungen.Count == 0)
        {
            return (false, "Employé introuvable ou sans code PIN.", null, 0);
        }

        var jetzt = uhr.GetUtcNow().UtcDateTime;
        UserSalon? treffer = null;

        foreach (var zuordnung in zuordnungen)
        {
            if (zuordnung.PinGesperrtBisUtc is DateTime gesperrtBis && gesperrtBis > jetzt)
            {
                continue;
            }

            // PIN gegen Kassen-PIN oder (für den Besitzer) Besitzer-PIN prüfen.
            var pinOk =
                (zuordnung.KassenPinHash is not null && KassenPinService.PruefePin(pin, zuordnung.KassenPinHash))
                || (zuordnung.BesitzerPinHash is not null && KassenPinService.PruefePin(pin, zuordnung.BesitzerPinHash));

            if (pinOk)
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

        var jetzt = uhr.GetUtcNow().UtcDateTime;
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

    /// <summary>
    /// Bestätigung durch den Besitzer an der (gemeinsamen) Kasse: mit konfigurierter Besitzer-PIN
    /// zählt nur die PIN; ohne PIN muss der angemeldete Benutzer selbst Besitzer sein.
    /// Liefert eine Fehlermeldung oder <c>null</c>.
    /// </summary>
    public async Task<string?> PruefeBesitzerFreigabeAsync(Guid salonId, Guid userId, string? besitzerPin, CancellationToken cancellationToken = default)
    {
        if (await HatBesitzerPinAsync(salonId, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(besitzerPin)) return "Saisissez le code PIN du propriétaire.";
            var (ok, fehler) = await PruefeBesitzerPinAsync(salonId, besitzerPin, cancellationToken);
            return ok ? null : fehler;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await LadenZugriff.IstBesitzerAsync(db, userId, salonId, cancellationToken) ? null : "Réservé au propriétaire de l'établissement.";
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

        if (await PruefeKassenSperreAsync(salonId, synchronisieren: true, cancellationToken) is string sperre)
        {
            return new(false, sperre, null, 0);
        }

        var zuordnung = await db.UserSalons
            .Include(us => us.AppUser)
            .FirstOrDefaultAsync(us => us.SalonId == salonId && us.AppUserId == kassiererId, cancellationToken);

        if (zuordnung is null || (zuordnung.KassenPinHash is null && zuordnung.BesitzerPinHash is null))
        {
            return new(false, "Employé introuvable ou sans code PIN.", null, 0);
        }

        var jetzt = uhr.GetUtcNow().UtcDateTime;

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

    /// <summary>
    /// Eine Ticketzeile. Mit <paramref name="AufenthaltsPositionId"/>: schon erfasste Leistung eines anwesenden Kunden –
    /// Name, Preis, Menge und Tag kommen dann aus der Erfassung (LeistungId/Menge/FreierBetrag werden ignoriert), kein zweiter Lagerabzug.
    /// </summary>
    public record VerkaufsPositionInput(Guid LeistungId, int Menge, Guid? RabattId, bool BedingungBestaetigt, decimal? FreierBetrag = null,
        Guid? AufenthaltsPositionId = null);

    /// <summary>Fehlertext, wenn die erfassten Leistungen des Kunden nicht mehr zum Ticket passen (Kasse lädt sie dann neu).</summary>
    /// <summary>Sitzung nicht (mehr) offen – z. B. auf einem anderen Gerät geschlossen; die Kasse gleicht dann ihre Ansicht ab.</summary>
    public const string KeineOffeneKasseFehler = "Aucune caisse ouverte.";

    public const string AufenthaltGeaendertFehler = "Les prestations enregistrées de ce client ont changé entre-temps. Le ticket a été actualisé : vérifiez-le puis encaissez.";

    /// <summary>
    /// Ein Zahlungsanteil der Kasse. <c>Betrag</c> = <c>null</c> heißt „Restbetrag“
    /// (nur für den letzten Anteil). Bei Bar ist <c>Gegeben</c> der vom Kunden gegebene
    /// Betrag; Karte/Mobile Money gelten mit dem Aufruf als vom Kassierer bestätigt.
    /// </summary>
    public record ZahlungInput(ZahlungsArt Art, decimal? Betrag = null, decimal? Gegeben = null, Guid? AnbieterId = null, string? Referenz = null);

    /// <summary>Kompatibilität: Verkauf mit genau einer Zahlart (Bar oder Karte).</summary>
    public Task<(bool Erfolg, string? Fehler, Verkauf? Verkauf, List<string> Warnungen)> BucheVerkaufAsync(
        Guid kassenSitzungId,
        IReadOnlyList<VerkaufsPositionInput> positionen,
        ZahlungsArt zahlungsArt,
        decimal? gegeben,
        bool karteBestaetigt,
        CancellationToken cancellationToken = default)
    {
        if (zahlungsArt == ZahlungsArt.Bar && gegeben is null)
            return Task.FromResult<(bool, string?, Verkauf?, List<string>)>((false, "Veuillez saisir le montant reçu.", null, []));
        if (zahlungsArt != ZahlungsArt.Bar && !karteBestaetigt)
            return Task.FromResult<(bool, string?, Verkauf?, List<string>)>((false, "Veuillez confirmer que le paiement par carte a été effectué.", null, []));
        return BucheVerkaufAsync(kassenSitzungId, positionen, [new ZahlungInput(zahlungsArt, Gegeben: gegeben)], cancellationToken);
    }

    /// <summary>
    /// Bucht einen Verkauf mit 1..n Zahlungsanteilen (Bar, Karte, Mobile Money, kombinierbar).
    /// Bar: Rückgeld aus dem gegebenen Betrag. Mobile Money: Anbieter muss für den Laden
    /// zugelassen sein. Artikel-Bestand wird gemindert (auch unter 0 erlaubt, mit Hinweis).
    /// </summary>
    public async Task<(bool Erfolg, string? Fehler, Verkauf? Verkauf, List<string> Warnungen)> BucheVerkaufAsync(
        Guid kassenSitzungId,
        IReadOnlyList<VerkaufsPositionInput> positionen,
        IReadOnlyList<ZahlungInput> zahlungen,
        CancellationToken cancellationToken = default,
        Guid? kundeId = null)
    {
        if (positionen.Count == 0)
        {
            return (false, "Le ticket est vide.", null, []);
        }

        // Verkäufe und Abschluss derselben Sitzung laufen nacheinander (siehe SperreSitzungAsync).
        await using var vorbereitung = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await vorbereitung.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaktion = await SperreSitzungAsync(db, kassenSitzungId, cancellationToken);
            return await BucheVerkaufGesperrtAsync(db, transaktion, kassenSitzungId, positionen, zahlungen, kundeId, cancellationToken);
        });
    }

    /// <summary>
    /// Sperrt die Zeile der Kassensitzung bis zum Ende der Transaktion. Zwei Geräte desselben Ladens buchen dieselbe Kasse:
    /// ohne Sperre gingen bei gleichzeitigen Verkäufen Bestandsabzüge verloren, und ein Verkauf während des Abschlusses
    /// konnte im Soll fehlen (Klick-Simulation S2). Ohne relationale Datenbank (Unit-Tests) keine Transaktion.
    /// </summary>
    private static async Task<IDbContextTransaction?> SperreSitzungAsync(ApplicationDbContext db, Guid sitzungId, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return null;
        var transaktion = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DECLARE @x int; SELECT @x = 1 FROM [KassenSitzungen] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {sitzungId};", ct);
        return transaktion;
    }

    private async Task<(bool Erfolg, string? Fehler, Verkauf? Verkauf, List<string> Warnungen)> BucheVerkaufGesperrtAsync(
        ApplicationDbContext db,
        IDbContextTransaction? transaktion,
        Guid kassenSitzungId,
        IReadOnlyList<VerkaufsPositionInput> positionen,
        IReadOnlyList<ZahlungInput> zahlungen,
        Guid? kundeId,
        CancellationToken cancellationToken)
    {
        var warnungen = new List<string>();

        var sitzung = await db.KassenSitzungen
            .FirstOrDefaultAsync(k => k.Id == kassenSitzungId && k.Status == KassenSitzungStatus.Offen, cancellationToken);
        if (sitzung is null)
        {
            return (false, KeineOffeneKasseFehler, null, warnungen);
        }

        if (await PruefeKassenSperreAsync(sitzung.SalonId, synchronisieren: false, cancellationToken) is string sperre)
        {
            return (false, sperre, null, warnungen);
        }

        var leistungIds = positionen.Where(p => p.AufenthaltsPositionId is null).Select(p => p.LeistungId).Distinct().ToList();
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

        // Optionaler Kunde: muss zum selben Laden gehören.
        if (kundeId is Guid kid && !await db.Kunden.AnyAsync(k => k.Id == kid && k.SalonId == sitzung.SalonId, cancellationToken))
        {
            return (false, "Le client choisi est introuvable.", null, warnungen);
        }

        // Schon erfasste Leistungen eines anwesenden Kunden: alle offenen und nur diese gehören auf den Bon.
        var aufenthaltIds = positionen.Where(p => p.AufenthaltsPositionId is not null).Select(p => p.AufenthaltsPositionId!.Value).ToList();
        var aufenthalt = new Dictionary<Guid, AufenthaltsPosition>();
        if (kundeId is Guid kundeMitKonto)
        {
            var offen = await AufenthaltService.Offene(db, sitzung.SalonId, kundeMitKonto).ToListAsync(cancellationToken);
            if (aufenthaltIds.Distinct().Count() != aufenthaltIds.Count || offen.Count != aufenthaltIds.Count || offen.Any(o => !aufenthaltIds.Contains(o.Id)))
            {
                return (false, AufenthaltGeaendertFehler, null, warnungen);
            }
            aufenthalt = offen.ToDictionary(o => o.Id);
        }
        else if (aufenthaltIds.Count > 0)
        {
            return (false, AufenthaltGeaendertFehler, null, warnungen);
        }

        var verkauf = new Verkauf
        {
            KassenSitzungId = sitzung.Id,
            SalonId = sitzung.SalonId,
            ZeitUtc = uhr.GetUtcNow().UtcDateTime,
            KundeId = kundeId
        };

        var jetzt = uhr.GetUtcNow().UtcDateTime;
        decimal zwischensumme = 0m;
        decimal rabattSumme = 0m;

        // Remise einer Zeile prüfen und berechnen: (Fehler, Remise, Betrag).
        (string? Fehler, Rabatt? Rabatt, decimal Betrag) PruefeRabatt(VerkaufsPositionInput pos, Guid leistungId, string name, decimal zeilenBetrag)
        {
            if (pos.RabattId is not Guid rabattId)
            {
                return (null, null, 0m);
            }

            var rabatt = rabatte.FirstOrDefault(r => r.Id == rabattId);
            var gueltig = rabatt is not null && rabatt.IstAktiv
                && (rabatt.GueltigVonUtc is null || jetzt >= rabatt.GueltigVonUtc)
                && (rabatt.GueltigBisUtc is null || jetzt <= rabatt.GueltigBisUtc)
                && (rabatt.LeistungId is null || rabatt.LeistungId == leistungId);
            if (!gueltig)
            {
                return ($"La remise choisie pour « {name} » n'est pas valide.", null, 0m);
            }

            // Bedingung (z. B. client fidèle) muss vom Kassierer bestätigt worden sein.
            if (!string.IsNullOrWhiteSpace(rabatt!.Bedingung) && !pos.BedingungBestaetigt)
            {
                return ($"Veuillez confirmer la condition de la remise « {rabatt.Name} ».", null, 0m);
            }

            return (null, rabatt, zeilenBetrag - KassenRechner.WendeRabattAn(zeilenBetrag, rabatt.Art, rabatt.Wert));
        }

        foreach (var pos in positionen)
        {
            if (pos.AufenthaltsPositionId is Guid aufenthaltId)
            {
                // Schon erfasst: Kopie von Name/Preis/Tag, Lager wurde bei der Erfassung gemindert.
                var erfasst = aufenthalt[aufenthaltId];
                if (erfasst.LeistungId is not Guid erfassteLeistung)
                {
                    return (false, "Une prestation enregistrée est introuvable.", null, warnungen);
                }

                var (eFehler, eRabatt, eRabattBetrag) = PruefeRabatt(pos, erfassteLeistung, erfasst.LeistungName, erfasst.Summe);
                if (eFehler is not null)
                {
                    return (false, eFehler, null, warnungen);
                }

                zwischensumme += erfasst.Summe;
                rabattSumme += eRabattBetrag;
                verkauf.Positionen.Add(new VerkaufsPosition
                {
                    LeistungId = erfassteLeistung,
                    LeistungName = erfasst.LeistungName,
                    EinzelPreis = erfasst.EinzelPreis,
                    Menge = erfasst.Menge,
                    RabattId = eRabatt?.Id,
                    RabattBetrag = eRabattBetrag,
                    ZeilenSumme = erfasst.Summe - eRabattBetrag,
                    ErbrachtAm = erfasst.ErbrachtAm
                });
                erfasst.VerkaufId = verkauf.Id;
                continue;
            }

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
                    AusgeloestVonName = kassierer?.FullName ?? "Employé"
                });
            }

            var zeilenBetrag = einzelPreis * pos.Menge;
            var (rabattFehler, rabatt, rabattBetrag) = PruefeRabatt(pos, leistung.Id, leistung.Name, zeilenBetrag);
            if (rabattFehler is not null)
            {
                return (false, rabattFehler, null, warnungen);
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

        var zahlFehler = await ErfasseZahlungenAsync(db, sitzung.SalonId, verkauf, zahlungen, cancellationToken);
        if (zahlFehler is not null)
        {
            return (false, zahlFehler, null, warnungen);
        }

        db.Verkaeufe.Add(verkauf);
        // Kunde war als anwesend gemeldet → Besuch endet mit dem Encaissement.
        var beendeteBesuche = kundeId is Guid anwesenderKunde
            ? await KundenBesuchService.BeendeOffeneAsync(db, sitzung.SalonId, anwesenderKunde, verkauf.Id, verkauf.ZeitUtc, cancellationToken)
            : 0;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException) when (aufenthalt.Count > 0)
        {
            return (false, AufenthaltGeaendertFehler, null, warnungen);
        }
        if (transaktion is not null) await transaktion.CommitAsync(cancellationToken);
        if (beendeteBesuche > 0 || aufenthalt.Count > 0) anwesenheit?.Melde(sitzung.SalonId);

        // Live-Update an abonnierte Besitzer-Ansichten desselben Salons.
        try
        {
            var zone = ENote.Core.Services.Zeitzonen.Fuer(await db.Salons.Where(s => s.Id == sitzung.SalonId).Select(s => s.Land).FirstOrDefaultAsync(cancellationToken));
            var heute = ENote.Core.Services.Zeitzonen.NachUtc(ENote.Core.Services.Zeitzonen.Heute(zone, uhr.GetUtcNow().UtcDateTime), zone);
            var tagesUmsatz = await db.Verkaeufe
                .Where(v => v.SalonId == sitzung.SalonId && v.ZeitUtc >= heute)
                .SumAsync(v => v.GesamtSumme, cancellationToken);
            var anzahlHeute = await db.Verkaeufe
                .CountAsync(v => v.SalonId == sitzung.SalonId && v.ZeitUtc >= heute && v.StornoVonVerkaufId == null, cancellationToken);

            await umsatzBenachrichtigung.VerkaufGemeldetAsync(sitzung.SalonId, tagesUmsatz, anzahlHeute, cancellationToken);
        }
        catch
        {
            // Live-Benachrichtigung darf den Verkauf nie blockieren.
        }

        return (true, null, verkauf, warnungen);
    }

    /// <summary>
    /// Validiert die Zahlungsanteile, löst „Restbetrag“ auf, berechnet das Rückgeld und
    /// hängt die <see cref="VerkaufZahlung"/>-Einträge an. Liefert eine Fehlermeldung oder null.
    /// </summary>
    private static async Task<string?> ErfasseZahlungenAsync(
        ApplicationDbContext db, Guid salonId, Verkauf verkauf, IReadOnlyList<ZahlungInput> zahlungen, CancellationToken ct)
    {
        if (zahlungen.Count == 0)
            return "Veuillez choisir un mode de paiement.";
        if (zahlungen.Take(zahlungen.Count - 1).Any(z => z.Betrag is null))
            return "Seul le dernier paiement peut couvrir le reste.";

        var fest = zahlungen.Where(z => z.Betrag is not null).Sum(z => z.Betrag!.Value);
        var anteile = zahlungen
            .Select(z => (z.Art, Betrag: Math.Round(z.Betrag ?? verkauf.GesamtSumme - fest, 2)))
            .ToList();
        var fehler = KassenRechner.PruefeZahlungen(verkauf.GesamtSumme, anteile);
        if (fehler is not null)
            return fehler;

        var mobile = zahlungen.Where(z => z.Art == ZahlungsArt.MobileMoney).ToList();
        if (mobile.Any(z => z.AnbieterId is null))
            return "Veuillez choisir l'opérateur Mobile Money.";
        var erlaubt = mobile.Count == 0
            ? new Dictionary<Guid, MobileMoneyAnbieter>()
            : await MobileMoneyService.ErlaubteAnbieterAbfrage(db, salonId).ToDictionaryAsync(a => a.Id, ct);

        for (var i = 0; i < zahlungen.Count; i++)
        {
            var eingabe = zahlungen[i];
            var zahlung = new VerkaufZahlung { Art = eingabe.Art, Betrag = anteile[i].Betrag, Reihenfolge = i };

            switch (eingabe.Art)
            {
                case ZahlungsArt.Bar:
                    if (eingabe.Gegeben is null)
                        return "Veuillez saisir le montant reçu.";
                    try
                    {
                        verkauf.Rueckgeld = KassenRechner.BerechneRueckgeld(zahlung.Betrag, eingabe.Gegeben.Value);
                    }
                    catch (InvalidOperationException)
                    {
                        return "Le montant reçu est inférieur au montant en espèces.";
                    }
                    verkauf.Gegeben = Math.Round(eingabe.Gegeben.Value, 2);
                    break;
                case ZahlungsArt.Karte:
                    verkauf.KarteZahlungBestaetigt = true;
                    break;
                case ZahlungsArt.MobileMoney:
                    if (!erlaubt.TryGetValue(eingabe.AnbieterId!.Value, out var anbieter))
                        return "Cet opérateur Mobile Money n'est pas accepté par l'établissement.";
                    var referenz = eingabe.Referenz?.Trim();
                    zahlung.MobileMoneyAnbieterId = anbieter.Id;
                    zahlung.AnbieterName = anbieter.Name;
                    zahlung.Referenz = string.IsNullOrEmpty(referenz) ? null : referenz[..Math.Min(80, referenz.Length)];
                    break;
            }

            verkauf.Zahlungen.Add(zahlung);
        }

        verkauf.ZahlungsArt = KassenRechner.Zusammenfassen(verkauf.Zahlungen.Select(z => z.Art));
        return null;
    }

    /// <summary>Kennzahlen einer offenen Sitzung für den Kassenabschluss.</summary>
    public record SitzungsUebersicht(
        decimal Startbestand, decimal Bar, decimal Karte, decimal MobileMoney,
        IReadOnlyList<(string Anbieter, decimal Betrag)> MobileMoneyNachAnbieter, int AnzahlVerkaeufe,
        decimal EinlagenBar = 0, decimal EntnahmenBar = 0, decimal Erstattungen = 0, int AnzahlErstattungen = 0, decimal ErstattungenBar = 0)
    {
        /// <summary>Barverkäufe ohne Rückerstattungen (Bar = Brutto − Bar-Erstattungen).</summary>
        public decimal BarVerkaeufe => Bar + ErstattungenBar;

        /// <summary>Startbestand + Baranteile + Einlagen in die Kasse − Bar-Entnahmen des Besitzers.</summary>
        public decimal BarSoll => Startbestand + Bar + EinlagenBar - EntnahmenBar;
        public decimal Umsatz => Bar + Karte + MobileMoney;
    }

    /// <summary>
    /// Übersicht einer offenen Sitzung: Bar-Soll sowie Karten- und Mobile-Money-Einnahmen
    /// (pro Anbieter), damit der Abschluss mit Terminal- und MoMo-Saldo abgeglichen werden kann.
    /// </summary>
    public async Task<SitzungsUebersicht?> GetSitzungsUebersichtAsync(Guid kassenSitzungId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var sitzung = await db.KassenSitzungen
            .AsNoTracking()
            .Include(k => k.Verkaeufe).ThenInclude(v => v.Zahlungen)
            .FirstOrDefaultAsync(k => k.Id == kassenSitzungId && k.Status == KassenSitzungStatus.Offen, cancellationToken);
        if (sitzung is null)
        {
            return null;
        }

        var anteile = sitzung.Verkaeufe.SelectMany(KassenRechner.Anteile).ToList();
        decimal Summe(ZahlungsArt art) => anteile.Where(a => a.Art == art).Sum(a => a.Betrag);
        var nachAnbieter = anteile
            .Where(a => a.Art == ZahlungsArt.MobileMoney)
            .GroupBy(a => a.AnbieterName ?? "Mobile Money")
            .Select(g => (g.Key, g.Sum(a => a.Betrag)))
            .OrderByDescending(x => x.Item2)
            .ToList();

        var bewegungen = await KassenBewegungenAsync(db, sitzung.Id, cancellationToken);
        return new SitzungsUebersicht(sitzung.Startbestand, Summe(ZahlungsArt.Bar), Summe(ZahlungsArt.Karte),
            Summe(ZahlungsArt.MobileMoney), nachAnbieter, sitzung.Verkaeufe.Count(v => !v.IstStorno),
            bewegungen.Where(b => b.Art == Core.DataModels.Finanzen.GeldBewegungArt.Einlage).Sum(b => b.Betrag),
            bewegungen.Where(b => b.Art == Core.DataModels.Finanzen.GeldBewegungArt.EntnahmeBesitzer).Sum(b => b.Betrag),
            -sitzung.Verkaeufe.Where(v => v.IstStorno).Sum(v => v.GesamtSumme),
            sitzung.Verkaeufe.Count(v => v.IstStorno),
            -sitzung.Verkaeufe.Where(v => v.IstStorno).SelectMany(KassenRechner.Anteile).Where(a => a.Art == ZahlungsArt.Bar).Sum(a => a.Betrag));
    }

    /// <summary>Bargeldbewegungen (Einlagen/Entnahmen) in der Kassenschublade einer Sitzung.</summary>
    private static Task<List<Core.DataModels.Finanzen.GeldBewegung>> KassenBewegungenAsync(ApplicationDbContext db, Guid sitzungId, CancellationToken ct)
        => db.GeldBewegungen.AsNoTracking()
            .Where(g => g.KassenSitzungId == sitzungId && g.Ort == Core.DataModels.Finanzen.GeldOrt.Kasse)
            .ToListAsync(ct);

    /// <summary>Liefert den aktuell erwarteten Bargeldbestand (Soll) einer offenen Sitzung, ohne sie abzuschließen.</summary>
    public async Task<decimal?> GetBarSollAsync(Guid kassenSitzungId, CancellationToken cancellationToken = default)
    {
        var aufstellung = await GetBarSollAufstellungAsync(kassenSitzungId, cancellationToken);
        return aufstellung?.Soll;
    }

    /// <summary>Liefert Startbestand, Bar-Einnahmen und daraus resultierenden Soll-Bestand einer offenen Sitzung.</summary>
    public async Task<(decimal Startbestand, decimal BarEinnahmen, decimal Soll)?> GetBarSollAufstellungAsync(
        Guid kassenSitzungId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var sitzung = await db.KassenSitzungen
            .Include(k => k.Verkaeufe).ThenInclude(v => v.Zahlungen)
            .FirstOrDefaultAsync(k => k.Id == kassenSitzungId && k.Status == KassenSitzungStatus.Offen, cancellationToken);
        if (sitzung is null)
        {
            return null;
        }

        var barEinnahmen = sitzung.Verkaeufe
            .SelectMany(KassenRechner.Anteile)
            .Where(a => a.Art == ZahlungsArt.Bar)
            .Sum(a => a.Betrag);
        var soll = KassenRechner.BerechneBarSoll(sitzung.Startbestand, sitzung.Verkaeufe, await KassenBewegungenAsync(db, sitzung.Id, cancellationToken));
        return (sitzung.Startbestand, Math.Round(barEinnahmen, 2), soll);
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
        // Wartet auf laufende Verkäufe derselben Sitzung (Sperre), danach bucht keiner mehr in die geschlossene Kasse.
        await using var vorbereitung = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await vorbereitung.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaktion = await SperreSitzungAsync(db, kassenSitzungId, cancellationToken);

            var sitzung = await db.KassenSitzungen
                .Include(k => k.Verkaeufe).ThenInclude(v => v.Zahlungen)
                .FirstOrDefaultAsync(k => k.Id == kassenSitzungId && k.Status == KassenSitzungStatus.Offen, cancellationToken);
            if (sitzung is null)
            {
                return (false, KeineOffeneKasseFehler, (KassenSitzung?)null);
            }

            var soll = KassenRechner.BerechneBarSoll(sitzung.Startbestand, sitzung.Verkaeufe, await KassenBewegungenAsync(db, sitzung.Id, cancellationToken));

            sitzung.Status = KassenSitzungStatus.Abgeschlossen;
            sitzung.AbgeschlossenUtc = uhr.GetUtcNow().UtcDateTime;
            sitzung.BareinnahmenSoll = soll;
            sitzung.EndbestandGezaehlt = Math.Round(endbestandGezaehlt, 2);
            sitzung.Differenz = KassenRechner.BerechneDifferenz(soll, endbestandGezaehlt);
            sitzung.AbschlussNotiz = string.IsNullOrWhiteSpace(notiz) ? null : notiz.Trim();

            await db.SaveChangesAsync(cancellationToken);
            if (transaktion is not null) await transaktion.CommitAsync(cancellationToken);
            return (true, (string?)null, (KassenSitzung?)sitzung);
        });
    }
}
