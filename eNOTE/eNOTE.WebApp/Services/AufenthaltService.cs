using ENote.Core.DataModels.Kasse;
using ENote.Core.Services;
using ENote.Core.Services.Kasse;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// « Compte de présence »: Leistungen eines anwesenden Kunden schon während der Anwesenheit fest erfassen
/// (« Enregistrer sans encaisser ») und erst beim Gehen alles zusammen kassieren – z. B. Patient bleibt mehrere Tage.
/// Erfassen: Name/Preis als Kopie, Tag in Ortszeit, Lagerabgang sofort. Bezahlen: <see cref="KassenService.BucheVerkaufAsync(Guid, IReadOnlyList{KassenService.VerkaufsPositionInput}, IReadOnlyList{KassenService.ZahlungInput}, CancellationToken, Guid?)"/>
/// mit allen offenen Zeilen. Zurücknehmen: mit Grund; eigene Erfassung bis <see cref="AufenthaltRegeln.SofortKorrektur"/> frei,
/// sonst Freigabe des Besitzers (PIN bzw. nur Besitzer). Zugriff: Besitzer und Mitarbeitende des Ladens.
/// </summary>
public class AufenthaltService(IDbContextFactory<ApplicationDbContext> factory, TimeProvider zeit, AnwesenheitsSignal signal, KassenService kassen)
{
    public sealed record PositionInput(Guid LeistungId, int Menge, decimal? FreierBetrag = null);

    public sealed record Zeile(Guid Id, Guid? LeistungId, string Name, decimal EinzelPreis, int Menge, DateOnly ErbrachtAm,
        DateTime ErfasstUtc, string ErfasstVon, bool Offen, DateTime? ZurueckgenommenUtc, string? ZurueckgenommenVon, string? Grund,
        bool DarfSofortZuruecknehmen, bool HatLager)
    {
        public decimal Summe => EinzelPreis * Menge;
    }

    /// <summary>Alle Zeilen des offenen Besuchs (offene und zurückgenommene), älteste zuerst.</summary>
    /// <param name="IstBesitzer">Darf ohne PIN zurücknehmen, wenn keine Besitzer-PIN eingerichtet ist.</param>
    /// <param name="BrauchtPin">Laden hat eine Besitzer-PIN: Rücknahmen (außer eigene Sofort-Korrektur) brauchen sie.</param>
    public sealed record Konto(Guid KundeId, string KundeName, Guid? BesuchId, DateTime? AnkunftUtc, IReadOnlyList<Zeile> Zeilen, DateOnly Heute,
        bool IstBesitzer, bool BrauchtPin)
    {
        public IEnumerable<Zeile> Offene => Zeilen.Where(z => z.Offen);
        public decimal Summe => Offene.Sum(z => z.Summe);
        public int Anzahl => Offene.Sum(z => z.Menge);
    }

    public sealed record Stand(int Anzahl, decimal Summe);

    public sealed record ErfassErgebnis(int Zeilen, Stand Gesamt, IReadOnlyList<string> Warnungen);

    private DateTime Jetzt => zeit.GetUtcNow().UtcDateTime;

    private sealed record Person(bool Besitzer, string Name);

    private static async Task<Person> PruefeZugangAsync(ApplicationDbContext db, Guid user, Guid salonId, CancellationToken ct)
    {
        if (!await LadenZugriff.HatZugangAsync(db, user, salonId, ct)) throw new UnauthorizedAccessException("Accès refusé.");
        var name = await db.Users.AsNoTracking().Where(u => u.Id == user)
            .Select(u => u.FullName != "" ? u.FullName : (u.UserName ?? "")).FirstOrDefaultAsync(ct) ?? "";
        return new Person(await LadenZugriff.IstBesitzerAsync(db, user, salonId, ct), name);
    }

    private static async Task<TimeZoneInfo> ZoneAsync(ApplicationDbContext db, Guid salonId, CancellationToken ct)
        => Zeitzonen.Fuer(await db.Salons.AsNoTracking().Where(s => s.Id == salonId).Select(s => s.Land).FirstOrDefaultAsync(ct));

    /// <summary>Offene Zeilen eines Kunden im Laden (für Kasse, Besuchsende, Kundenlöschung).</summary>
    internal static IQueryable<AufenthaltsPosition> Offene(ApplicationDbContext db, Guid salonId, Guid kundeId)
        => db.AufenthaltsPositionen.Where(a => a.SalonId == salonId && a.KundeId == kundeId && a.VerkaufId == null && a.ZurueckgenommenUtc == null);

    /// <summary>Text, wenn ein Kunde noch offene Leistungen hat (Départ/Annulation/Löschen gesperrt), sonst null.</summary>
    internal static async Task<string?> OffenHinweisAsync(ApplicationDbContext db, Guid salonId, Guid kundeId, CancellationToken ct)
    {
        var offen = await Offene(db, salonId, kundeId).Select(a => new { a.Menge, a.EinzelPreis }).ToListAsync(ct);
        if (offen.Count == 0) return null;
        var summe = offen.Sum(o => o.EinzelPreis * o.Menge);
        return $"Ce client a encore {offen.Sum(o => o.Menge)} prestation(s) enregistrée(s) à payer ({summe:N0} F). Encaissez-les ou retirez-les d'abord.";
    }

    // ------------------------------------------------------------------ Erfassen

    /// <summary>
    /// Leistungen für einen anwesenden Kunden fest erfassen, ohne zu kassieren. Lagerartikel verlassen sofort den Bestand
    /// (auch unter 0, mit Hinweis). « Divers » mit freiem Betrag meldet sich wie an der Kasse beim Besitzer.
    /// </summary>
    public async Task<ErfassErgebnis> ErfasseAsync(Guid user, Guid salonId, Guid kundeId, IReadOnlyList<PositionInput> positionen, CancellationToken ct = default)
    {
        if (positionen.Count == 0) throw new ArgumentException("Aucune prestation à enregistrer.");
        if (positionen.Count > AufenthaltRegeln.ZeilenJeErfassungMax) throw new ArgumentException($"{AufenthaltRegeln.ZeilenJeErfassungMax} lignes au maximum.");
        if (positionen.Any(p => p.Menge < 1 || p.Menge > AufenthaltRegeln.MengeMax)) throw new ArgumentException("Quantité invalide.");

        await using var db = await factory.CreateDbContextAsync(ct);
        var person = await PruefeZugangAsync(db, user, salonId, ct);

        var besuch = await db.KundenBesuche.AsNoTracking()
            .Where(b => b.SalonId == salonId && b.KundeId == kundeId && b.EndeUtc == null)
            .OrderByDescending(b => b.AnkunftUtc).Select(b => new { b.Id }).FirstOrDefaultAsync(ct)
            ?? throw new ArgumentException("Ce client n'est pas enregistré comme présent. Enregistrez d'abord son arrivée.");
        var schonOffen = await Offene(db, salonId, kundeId).CountAsync(ct);
        if (schonOffen + positionen.Count > AufenthaltRegeln.OffeneZeilenMax)
            throw new ArgumentException($"{AufenthaltRegeln.OffeneZeilenMax} lignes à payer au maximum : encaissez d'abord une partie.");

        var ids = positionen.Select(p => p.LeistungId).Distinct().ToList();
        var leistungen = await db.Leistungen.Where(l => l.SalonId == salonId && ids.Contains(l.Id) && l.IstAktiv).ToListAsync(ct);
        if (leistungen.Count != ids.Count) throw new ArgumentException("Une prestation est introuvable ou n'est plus en vente.");
        var basisIds = leistungen.Where(l => l.BasisLeistungId is not null).Select(l => l.BasisLeistungId!.Value).Except(ids).Distinct().ToList();
        if (basisIds.Count > 0) leistungen.AddRange(await db.Leistungen.Where(l => l.SalonId == salonId && basisIds.Contains(l.Id)).ToListAsync(ct));

        var jetzt = Jetzt;
        var heute = DateOnly.FromDateTime(jetzt.ImLaden(await ZoneAsync(db, salonId, ct)));
        var warnungen = new List<string>();
        foreach (var p in positionen)
        {
            var l = leistungen.First(x => x.Id == p.LeistungId);
            decimal preis = l.Preis;
            if (l.IstStandardArtikel)
            {
                if (p.FreierBetrag is not > 0) throw new ArgumentException($"Veuillez saisir le montant pour « {l.Name} ».");
                preis = p.FreierBetrag.Value;
                db.BesitzerHinweise.Add(new BesitzerHinweis
                {
                    SalonId = salonId, AusgeloestVonId = user, AusgeloestVonName = person.Name,
                    Text = $"{p.Menge}× « {l.Name} » enregistré(s) pour un client présent ({preis * p.Menge:N0} F). Pensez à créer la prestation.",
                });
            }
            else if (p.FreierBetrag is not null) throw new ArgumentException("Montant invalide.");

            var zeile = new AufenthaltsPosition
            {
                SalonId = salonId, BesuchId = besuch.Id, KundeId = kundeId, LeistungId = l.Id, LeistungName = l.Name,
                EinzelPreis = preis, Menge = p.Menge, FreierBetrag = l.IstStandardArtikel,
                ErbrachtAm = heute, ErfasstUtc = jetzt, ErfasstVonId = user, ErfasstVonName = person.Name,
            };

            // Bestand wie beim Verkauf: Sammel-Artikel ziehen von ihrer Basis-Leistung ab.
            var ziel = l.BasisLeistungId is Guid b ? leistungen.FirstOrDefault(x => x.Id == b) ?? l : l;
            var abzug = l.BasisLeistungId is not null && l.BasisAbzugMenge is decimal m ? m * p.Menge : p.Menge;
            if (ziel.HatStueckzahl)
            {
                ziel.Stueckzahl -= abzug;
                zeile.LagerLeistungId = ziel.Id;
                zeile.LagerMenge = abzug;
                if (ziel.Stueckzahl < 0) warnungen.Add($"« {ziel.Name} » : stock épuisé (enregistré au-delà du stock).");
                else if (ziel.MindestbestandWarnung is decimal min && ziel.Stueckzahl <= min) warnungen.Add($"« {ziel.Name} » : stock bientôt épuisé ({ziel.Stueckzahl:0.##} restant).");
            }
            db.AufenthaltsPositionen.Add(zeile);
        }
        await db.SaveChangesAsync(ct);
        signal.Melde(salonId);

        var gesamt = await Offene(db, salonId, kundeId).Select(a => new { a.Menge, a.EinzelPreis }).ToListAsync(ct);
        return new ErfassErgebnis(positionen.Count, new Stand(gesamt.Sum(g => g.Menge), gesamt.Sum(g => g.EinzelPreis * g.Menge)), warnungen.Distinct().ToList());
    }

    // ------------------------------------------------------------------ Lesen

    /// <summary>Konto des Kunden: Zeilen seines offenen Besuchs (ohne offenen Besuch: nur noch offene Zeilen, falls es welche gibt).</summary>
    public async Task<Konto> GetKontoAsync(Guid user, Guid salonId, Guid kundeId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var person = await PruefeZugangAsync(db, user, salonId, ct);
        var kunde = await db.Kunden.AsNoTracking().Where(k => k.Id == kundeId && k.SalonId == salonId).Select(k => k.Name).FirstOrDefaultAsync(ct)
            ?? throw new ArgumentException("Client introuvable.");
        var besuch = await db.KundenBesuche.AsNoTracking()
            .Where(b => b.SalonId == salonId && b.KundeId == kundeId && b.EndeUtc == null)
            .OrderByDescending(b => b.AnkunftUtc).Select(b => new { b.Id, b.AnkunftUtc }).FirstOrDefaultAsync(ct);
        var jetzt = Jetzt;
        Guid? besuchId = besuch?.Id;
        var zeilen = await db.AufenthaltsPositionen.AsNoTracking()
            .Where(a => a.SalonId == salonId && a.KundeId == kundeId && a.VerkaufId == null
                && ((besuchId != null && a.BesuchId == besuchId) || a.ZurueckgenommenUtc == null))
            .OrderBy(a => a.ErfasstUtc)
            .ToListAsync(ct);
        var heute = DateOnly.FromDateTime(jetzt.ImLaden(await ZoneAsync(db, salonId, ct)));
        return new Konto(kundeId, kunde, besuch?.Id, besuch?.AnkunftUtc, zeilen.Select(a => new Zeile(a.Id, a.LeistungId, a.LeistungName, a.EinzelPreis, a.Menge,
            a.ErbrachtAm, a.ErfasstUtc, a.ErfasstVonName, a.IstOffen, a.ZurueckgenommenUtc, a.ZurueckgenommenVonName, a.RuecknahmeGrund,
            a.IstOffen && a.ErfasstVonId == user && jetzt - a.ErfasstUtc <= AufenthaltRegeln.SofortKorrektur,
            a.LagerLeistungId is not null)).ToList(), heute, person.Besitzer, await kassen.HatBesitzerPinAsync(salonId, ct));
    }

    /// <summary>Offene Summen je Kunde des Ladens (für « Clients présents »).</summary>
    public async Task<Dictionary<Guid, Stand>> GetStaendeAsync(Guid user, Guid salonId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeZugangAsync(db, user, salonId, ct);
        return await StaendeAsync(db, salonId, ct);
    }

    internal static async Task<Dictionary<Guid, Stand>> StaendeAsync(ApplicationDbContext db, Guid salonId, CancellationToken ct)
    {
        var offen = await db.AufenthaltsPositionen.AsNoTracking()
            .Where(a => a.SalonId == salonId && a.VerkaufId == null && a.ZurueckgenommenUtc == null)
            .GroupBy(a => a.KundeId)
            .Select(g => new { g.Key, Anzahl = g.Sum(a => a.Menge), Summe = g.Sum(a => a.EinzelPreis * a.Menge) })
            .ToListAsync(ct);
        return offen.ToDictionary(o => o.Key, o => new Stand(o.Anzahl, o.Summe));
    }

    // ------------------------------------------------------------------ Zurücknehmen

    /// <summary>
    /// Erfasste Zeile zurücknehmen (bleibt mit Grund sichtbar, zählt nicht mehr). Ware auf Wunsch zurück ins Lager
    /// (nicht bei « Client parti sans payer », wenn sie verbraucht ist). Eigene Erfassung bis 15 Minuten danach frei,
    /// sonst Freigabe des Besitzers; Rücknahmen durch Mitarbeitende melden sich beim Besitzer.
    /// </summary>
    public async Task NimmZurueckAsync(Guid user, Guid zeileId, string? grund, bool insLager, string? besitzerPin = null, CancellationToken ct = default)
    {
        var g = AufenthaltRegeln.PruefeGrund(grund);
        await using var db = await factory.CreateDbContextAsync(ct);
        var zeile = await db.AufenthaltsPositionen.FirstOrDefaultAsync(a => a.Id == zeileId, ct) ?? throw new ArgumentException("Ligne introuvable.");
        var person = await PruefeZugangAsync(db, user, zeile.SalonId, ct);
        if (zeile.VerkaufId is not null) throw new ArgumentException("Cette prestation est déjà encaissée : utilisez « Rembourser » dans l'historique.");
        if (zeile.ZurueckgenommenUtc is not null) throw new ArgumentException("Cette ligne est déjà retirée.");

        var jetzt = Jetzt;
        var sofort = zeile.ErfasstVonId == user && jetzt - zeile.ErfasstUtc <= AufenthaltRegeln.SofortKorrektur;
        if (!sofort && await kassen.PruefeBesitzerFreigabeAsync(zeile.SalonId, user, besitzerPin, ct) is string fehler)
            throw new UnauthorizedAccessException(fehler);

        zeile.ZurueckgenommenUtc = jetzt;
        zeile.ZurueckgenommenVonName = person.Name;
        zeile.RuecknahmeGrund = g;
        if (insLager && zeile.LagerLeistungId is Guid lagerId && zeile.LagerMenge is decimal menge
            && await db.Leistungen.FirstOrDefaultAsync(l => l.Id == lagerId, ct) is { HatStueckzahl: true } lager)
        {
            lager.Stueckzahl += menge;
            zeile.InsLagerZurueck = true;
        }
        if (!person.Besitzer)
        {
            var kunde = await db.Kunden.AsNoTracking().Where(k => k.Id == zeile.KundeId).Select(k => k.Name).FirstOrDefaultAsync(ct) ?? "client";
            db.BesitzerHinweise.Add(new BesitzerHinweis
            {
                SalonId = zeile.SalonId, AusgeloestVonId = user, AusgeloestVonName = person.Name,
                Text = $"{zeile.Menge}× « {zeile.LeistungName} » ({zeile.Summe:N0} F) retiré(s) des prestations à payer de « {kunde} » : {g}.",
            });
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ArgumentException("Cette ligne vient d'être encaissée ou retirée à une autre caisse."); }
        signal.Melde(zeile.SalonId);
    }
}
