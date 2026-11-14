using ENote.Core.DataModels.Finanzen;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.Core.Services.Kasse;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Bewegungen außerhalb von Verkauf und Ausgabe: Abschreibung defekter Ware, Entnahmen des
/// Besitzers (Ware oder Bargeld, an der offenen Kasse) und Einlagen/Hilfen ins Finanzbuch.
/// Alle Rechte werden serverseitig geprüft.
/// </summary>
public class BestandsService(IDbContextFactory<ApplicationDbContext> dbContextFactory, KassenService kassenService,
    AnwesenheitsSignal? signal = null)
{
    public record Ergebnis(bool Erfolg, string? Fehler);

    /// <summary>Unter der Kassensperre des Ladens (siehe <see cref="KassenSperre"/>); belegte Sperre → Fehlertext.</summary>
    private async Task<Ergebnis> GesperrtAsync(Func<Task<Ergebnis>> aktion)
    {
        try { return await aktion(); }
        catch (InvalidOperationException ex) when (ex.Message == KassenSperre.BelegtFehler) { return new(false, ex.Message); }
    }

    public static string GrundText(AbschreibungsGrund grund) => grund switch
    {
        AbschreibungsGrund.Casse => "Cassé / abîmé",
        AbschreibungsGrund.Peremption => "Périmé",
        AbschreibungsGrund.Vol => "Vol / perte",
        _ => "Autre"
    };

    private static async Task<string> NameAsync(ApplicationDbContext db, Guid userId, CancellationToken ct)
        => await db.Users.Where(u => u.Id == userId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "—";

    /// <summary>Besitzer oder Mitglied mit Recht „Prestations verwalten“.</summary>
    private static async Task<bool> DarfLagerVerwaltenAsync(ApplicationDbContext db, Guid userId, Guid salonId, CancellationToken ct)
        => await LadenZugriff.IstBesitzerAsync(db, userId, salonId, ct)
            || await db.UserSalons.AnyAsync(us => us.AppUserId == userId && us.SalonId == salonId && us.KannLeistungenVerwalten
                && !us.AppUser.IsBlocked && us.Salon.IstAktiv, ct);

    /// <summary>Besitzer-Freigabe an der Kasse (PIN oder Besitzer selbst), siehe <see cref="KassenService.PruefeBesitzerFreigabeAsync"/>.</summary>
    private Task<string?> PruefeBesitzerFreigabeAsync(ApplicationDbContext db, Guid salonId, Guid userId, string? besitzerPin, CancellationToken ct)
        => kassenService.PruefeBesitzerFreigabeAsync(salonId, userId, besitzerPin, ct);

    // ---------------- Lager ----------------

    /// <summary>Defekte/abgelaufene/verlorene Ware abschreiben: Bestand sinkt, Wert zum Einkaufspreis.</summary>
    public async Task<Ergebnis> SchreibeAbAsync(Guid userId, Guid leistungId, decimal menge, AbschreibungsGrund grund, string? notiz, CancellationToken ct = default)
    {
        Guid salonId;
        await using (var vorab = await dbContextFactory.CreateDbContextAsync(ct))
        {
            var gefunden = await vorab.Leistungen.AsNoTracking().Where(l => l.Id == leistungId).Select(l => (Guid?)l.SalonId).FirstOrDefaultAsync(ct);
            if (gefunden is null) return new(false, "Article introuvable.");
            salonId = gefunden.Value;
        }

        // Bestand unter der Kassensperre lesen und schreiben: ein gleichzeitiger Verkauf am Tablet verliert keinen Abzug (W3).
        return await GesperrtAsync(() => KassenSperre.MitLadenAsync(dbContextFactory, salonId, async (db, transaktion) =>
        {
            var leistung = await db.Leistungen.FirstOrDefaultAsync(l => l.Id == leistungId, ct);
            if (leistung is null) return new Ergebnis(false, "Article introuvable.");
            if (!await DarfLagerVerwaltenAsync(db, userId, leistung.SalonId, ct)) return new(false, "Vous n'avez pas le droit de modifier le stock.");
            if (grund == AbschreibungsGrund.Autre && string.IsNullOrWhiteSpace(notiz)) return new(false, "Précisez la raison dans la note.");

            var fehler = BucheAbgang(db, leistung, menge, LagerAbgangArt.Abschreibung, grund, notiz, null, userId, await NameAsync(db, userId, ct));
            if (fehler is not null) return new(false, fehler);
            await db.SaveChangesAsync(ct);
            await KassenSperre.BestaetigeAsync(transaktion, ct);
            return new(true, null);
        }, ct));
    }

    /// <summary>Besitzer entnimmt Ware an der offenen Kasse (kein Umsatz, Wert zum Einkaufspreis).</summary>
    public async Task<Ergebnis> EntnehmeWareAsync(Guid userId, Guid kassenSitzungId, string? besitzerPin, Guid leistungId, decimal menge, string? notiz, CancellationToken ct = default)
    {
        // PIN vorher prüfen (eigene Verbindung, zählt Fehlversuche), dann Bestand unter der Kassensperre (W3).
        Guid salonId;
        await using (var vorab = await dbContextFactory.CreateDbContextAsync(ct))
        {
            var sitzung = await vorab.KassenSitzungen.AsNoTracking().FirstOrDefaultAsync(k => k.Id == kassenSitzungId && k.Status == KassenSitzungStatus.Offen, ct);
            if (sitzung is null) return new(false, "Aucune caisse ouverte.");
            if (!await vorab.Leistungen.AnyAsync(l => l.Id == leistungId && l.SalonId == sitzung.SalonId, ct)) return new(false, "Article introuvable.");
            if (await PruefeBesitzerFreigabeAsync(vorab, sitzung.SalonId, userId, besitzerPin, ct) is string pinFehler) return new(false, pinFehler);
            salonId = sitzung.SalonId;
        }

        var ergebnis = await GesperrtAsync(() => KassenSperre.MitSitzungAsync(dbContextFactory, kassenSitzungId, async (db, transaktion) =>
        {
            if (!await db.KassenSitzungen.AnyAsync(k => k.Id == kassenSitzungId && k.Status == KassenSitzungStatus.Offen, ct))
                return new Ergebnis(false, KassenService.KeineOffeneKasseFehler);
            var leistung = await db.Leistungen.FirstAsync(l => l.Id == leistungId, ct);
            var fehler = BucheAbgang(db, leistung, menge, LagerAbgangArt.Eigenentnahme, null, notiz, kassenSitzungId, userId, "Propriétaire");
            if (fehler is not null) return new(false, fehler);
            await db.SaveChangesAsync(ct);
            await KassenSperre.BestaetigeAsync(transaktion, ct);
            return new(true, null);
        }, ct));
        return ergebnis;
    }

    private static string? BucheAbgang(ApplicationDbContext db, Leistung leistung, decimal menge, LagerAbgangArt art,
        AbschreibungsGrund? grund, string? notiz, Guid? sitzungId, Guid userId, string userName)
    {
        if (!leistung.HatStueckzahl) return "Cet article n'est pas géré en stock.";
        if (menge <= 0) return "La quantité doit être positive.";
        if (menge > leistung.Stueckzahl) return $"Quantité supérieure au stock ({leistung.Stueckzahl:0.##}).";

        leistung.Stueckzahl -= menge;
        leistung.UpdatedAtUtc = DateTime.UtcNow;
        db.LagerAbgaenge.Add(new LagerAbgang
        {
            SalonId = leistung.SalonId, LeistungId = leistung.Id, LeistungName = leistung.Name, Menge = menge,
            Art = art, Grund = grund, Notiz = Kuerze(notiz),
            EinkaufspreisSnapshot = leistung.Kaufpreis is > 0 ? leistung.Kaufpreis : null, VerkaufspreisSnapshot = leistung.Preis,
            KassenSitzungId = sitzungId, ErfasstVonId = userId, ErfasstVonName = userName
        });
        return null;
    }

    // ---------------- Geld ----------------

    /// <summary>Besitzer entnimmt Bargeld an der offenen Kasse – höchstens den erwarteten Bestand.</summary>
    public async Task<Ergebnis> EntnehmeBargeldAsync(Guid userId, Guid kassenSitzungId, string? besitzerPin, decimal betrag, string? notiz, CancellationToken ct = default)
    {
        if (betrag <= 0) return new(false, "Le montant doit être positif.");
        Guid salonId;
        await using (var vorab = await dbContextFactory.CreateDbContextAsync(ct))
        {
            var sitzung = await vorab.KassenSitzungen.AsNoTracking().FirstOrDefaultAsync(k => k.Id == kassenSitzungId && k.Status == KassenSitzungStatus.Offen, ct);
            if (sitzung is null) return new(false, "Aucune caisse ouverte.");
            if (await PruefeBesitzerFreigabeAsync(vorab, sitzung.SalonId, userId, besitzerPin, ct) is string pinFehler) return new(false, pinFehler);
            salonId = sitzung.SalonId;
        }

        // Soll und Entnahme unter der Kassensperre: zwei Geräte entnehmen zusammen nie mehr als das Bargeld,
        // und eine Entnahme während des Abschlusses fehlt nicht im Soll (W3).
        var ergebnis = await GesperrtAsync(() => KassenSperre.MitSitzungAsync(dbContextFactory, kassenSitzungId, async (db, transaktion) =>
        {
            var soll = await KassenService.BarSollAsync(db, kassenSitzungId, ct);
            if (soll is null) return new Ergebnis(false, KassenService.KeineOffeneKasseFehler);
            if (betrag > soll) return new(false, $"Il n'y a que {soll:N0} F attendus en caisse.");

            db.GeldBewegungen.Add(new GeldBewegung
            {
                SalonId = salonId, Art = GeldBewegungArt.EntnahmeBesitzer, Ort = GeldOrt.Kasse, Betrag = Math.Round(betrag, 0),
                Notiz = Kuerze(notiz), KassenSitzungId = kassenSitzungId, ErfasstVonId = userId, ErfasstVonName = "Propriétaire"
            });
            await db.SaveChangesAsync(ct);
            await KassenSperre.BestaetigeAsync(transaktion, ct);
            return new(true, null);
        }, ct));
        if (ergebnis.Erfolg) signal?.MeldeKasse(salonId);
        return ergebnis;
    }

    /// <summary>
    /// Einlage/Hilfe ins Finanzbuch (nur Besitzer). „Kasse“ erhöht den erwarteten Bestand der
    /// offenen Sitzung; „außerhalb“ (Bank/Mobile Money) betrifft nur das Finanzbuch.
    /// </summary>
    public async Task<Ergebnis> ErfasseEinlageAsync(Guid userId, Guid salonId, decimal betrag, GeldOrt ort, string? herkunft, string? notiz, DateTime? datumUtc = null, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        if (!await LadenZugriff.IstBesitzerAsync(db, userId, salonId, ct)) return new(false, "Réservé au propriétaire de l'établissement.");
        if (betrag <= 0) return new(false, "Le montant doit être positif.");
        if (string.IsNullOrWhiteSpace(herkunft)) return new(false, "Indiquez la provenance (ex. aide familiale, microcrédit).");

        var name = await NameAsync(db, userId, ct);
        GeldBewegung Bewegung(Guid? sitzungId) => new()
        {
            SalonId = salonId, Art = GeldBewegungArt.Einlage, Ort = ort, Betrag = Math.Round(betrag, 0),
            Herkunft = Kuerze(herkunft, 120), Notiz = Kuerze(notiz), KassenSitzungId = sitzungId,
            ErfasstVonId = userId, ErfasstVonName = name,
            ZeitUtc = ort == GeldOrt.AusserhalbKasse && datumUtc is DateTime d ? d : DateTime.UtcNow
        };

        if (ort != GeldOrt.Kasse)
        {
            db.GeldBewegungen.Add(Bewegung(null));
            await db.SaveChangesAsync(ct);
            return new(true, null);
        }

        // In die Kasse: unter der Kassensperre, damit die Einlage nicht in einer gerade geschlossenen Kasse landet (W3).
        var ergebnis = await GesperrtAsync(() => KassenSperre.MitLadenAsync(dbContextFactory, salonId, async (gesperrt, transaktion) =>
        {
            var sitzungId = await gesperrt.KassenSitzungen.Where(k => k.SalonId == salonId && k.Status == KassenSitzungStatus.Offen)
                .Select(k => (Guid?)k.Id).FirstOrDefaultAsync(ct);
            if (sitzungId is null) return new Ergebnis(false, "Aucune caisse ouverte : choisissez « hors caisse » ou ouvrez d'abord la caisse.");
            gesperrt.GeldBewegungen.Add(Bewegung(sitzungId));
            await gesperrt.SaveChangesAsync(ct);
            await KassenSperre.BestaetigeAsync(transaktion, ct);
            return new(true, null);
        }, ct));
        if (ergebnis.Erfolg) signal?.MeldeKasse(salonId);
        return ergebnis;
    }

    // ---------------- Auswertung ----------------

    public record Bewegungen(IReadOnlyList<GeldBewegung> Geld, IReadOnlyList<LagerAbgang> Lager)
    {
        public decimal Einlagen => Geld.Where(g => g.Art == GeldBewegungArt.Einlage).Sum(g => g.Betrag);
        public decimal EntnahmenBar => Geld.Where(g => g.Art == GeldBewegungArt.EntnahmeBesitzer).Sum(g => g.Betrag);
        public decimal WarenEntnahmenWert => Lager.Where(l => l.Art == LagerAbgangArt.Eigenentnahme).Sum(l => l.Einkaufswert ?? 0);
        public decimal AbschreibungenWert => Lager.Where(l => l.Art == LagerAbgangArt.Abschreibung).Sum(l => l.Einkaufswert ?? 0);
    }

    public async Task<Bewegungen> GetBewegungenAsync(Guid salonId, DateTime vonUtc, DateTime bisUtc, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var geld = await db.GeldBewegungen.AsNoTracking().Where(g => g.SalonId == salonId && g.ZeitUtc >= vonUtc && g.ZeitUtc <= bisUtc)
            .OrderByDescending(g => g.ZeitUtc).ToListAsync(ct);
        var lager = await db.LagerAbgaenge.AsNoTracking().Where(l => l.SalonId == salonId && l.ZeitUtc >= vonUtc && l.ZeitUtc <= bisUtc)
            .OrderByDescending(l => l.ZeitUtc).ToListAsync(ct);
        return new(geld, lager);
    }

    private static string? Kuerze(string? text, int max = 500)
    {
        var t = text?.Trim();
        return string.IsNullOrEmpty(t) ? null : t[..Math.Min(max, t.Length)];
    }
}
