using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Provisionen der Responsables: entstehen automatisch je bestätigter Abo-Zahlung (<see cref="ErfasseAsync"/>),
/// der Administrateur markiert mehrere auf einmal als ausgezahlt, der Responsable bestätigt mehrere auf einmal den Empfang.
/// </summary>
public class PoProvisionService(IDbContextFactory<ApplicationDbContext> factory, TimeProvider uhr)
{
    public sealed record Eintrag(Guid Id, Guid PoId, string Po, string Laden, DateTime VonUtc, DateTime BisUtc,
        DateTime BestaetigtUtc, decimal AboBetrag, decimal Prozent, decimal Fest, decimal Betrag,
        DateTime? VerseeUtc, string? Referenz, DateTime? ErhaltenUtc, string? Hinweis, PoProvisionStatus Status);

    public sealed record Summen(decimal AVerser, int AnzahlAVerser, decimal Versee, int AnzahlVersee, decimal Recue, int AnzahlRecue);

    public sealed record Uebersicht(Summen Summen, List<Eintrag> Eintraege, bool Mehr);

    public sealed record AltAuszahlung(Guid Id, string Po, int Jahr, int Monat, decimal Betrag, string? Aufschluesselung, bool Bestaetigt);

    private DateTime Jetzt => uhr.GetUtcNow().UtcDateTime;

    private static decimal Runde(decimal wert) => Math.Round(wert, 0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Legt für eine gerade bestätigte Abo-Zahlung die Provision jedes Responsable an, der den Laden zum Zeitpunkt
    /// der Bestätigung betreut (nur hinzufügen, kein SaveChanges). Ausgenommen: Zahlungen, die der Responsable selbst
    /// kassiert hat (Provision steckt dort im Zahlungseingang), und Monate, die das alte Monatsverfahren schon erfasst hat.
    /// </summary>
    internal static async Task ErfasseAsync(ApplicationDbContext db, NutzungsAbo abo, DateTime erfasstUtc, CancellationToken ct)
    {
        if (abo.ZahlungStatus != AboZahlungStatus.Bestaetigt || abo.ZahlungBestaetigtUtc is not { } bestaetigt || abo.PreisSnapshot <= 0) return;
        if (await db.PoZahlungseingaenge.AnyAsync(e => e.NutzungsAboId == abo.Id, ct)) return;

        var zuordnungen = await db.ProduktOwnerSalons.AsNoTracking()
            .Where(p => p.SalonId == abo.SalonId && p.CreatedAtUtc <= bestaetigt && (p.GueltigBisUtc == null || p.GueltigBisUtc > bestaetigt))
            .ToListAsync(ct);
        if (zuordnungen.Count == 0) return;

        var laden = await db.Salons.Where(s => s.Id == abo.SalonId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "";
        var monat = new DateTime(bestaetigt.Year, bestaetigt.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monatEnde = monat.AddMonths(1);

        foreach (var z in zuordnungen)
        {
            var prozent = z.ProzentProvision ?? 0m;
            var fest = z.FestbetragProvision ?? 0m;
            if (prozent <= 0 && fest <= 0) continue;
            if (db.PoProvisionen.Local.Any(p => p.ProduktOwnerId == z.ProduktOwnerId && p.NutzungsAboId == abo.Id)
                || await db.PoProvisionen.AnyAsync(p => p.ProduktOwnerId == z.ProduktOwnerId && p.NutzungsAboId == abo.Id, ct)) continue;
            // Altes Monatsverfahren: hat die Monatsberechnung diese Zahlung schon enthalten, nicht doppelt anlegen.
            if (await db.ProduktOwnerAuszahlungen.AnyAsync(a => a.ProduktOwnerId == z.ProduktOwnerId && a.Jahr == monat.Year
                && a.Monat == monat.Month && a.CreatedAtUtc >= bestaetigt, ct)) continue;

            if (fest > 0 && (db.PoProvisionen.Local.Any(p => p.ProduktOwnerId == z.ProduktOwnerId && p.SalonId == abo.SalonId
                    && p.FestProvision > 0 && p.ZahlungBestaetigtUtc >= monat && p.ZahlungBestaetigtUtc < monatEnde)
                || await db.PoProvisionen.AnyAsync(p => p.ProduktOwnerId == z.ProduktOwnerId && p.SalonId == abo.SalonId
                    && p.FestProvision > 0 && p.ZahlungBestaetigtUtc >= monat && p.ZahlungBestaetigtUtc < monatEnde, ct)))
                fest = 0m;

            var betrag = Runde(abo.PreisSnapshot * prozent / 100m + fest);
            if (betrag <= 0) continue;
            db.PoProvisionen.Add(new PoProvision
            {
                ProduktOwnerId = z.ProduktOwnerId, NutzungsAboId = abo.Id, SalonId = abo.SalonId, Laden = laden,
                ZeitraumVonUtc = abo.ZeitraumVonUtc, ZeitraumBisUtc = abo.ZeitraumBisUtc, ZahlungBestaetigtUtc = bestaetigt,
                AboBetrag = abo.PreisSnapshot, ProzentProvision = prozent, FestProvision = fest, Betrag = betrag, ErstelltUtc = erfasstUtc
            });
        }
    }

    /// <summary>
    /// Ergänzt Provisionen für Zahlungen, die vor Einführung der automatischen Provision bestätigt wurden
    /// (idempotent; Monate des alten Verfahrens bleiben ausgenommen). Liefert die Zahl der neu angelegten Provisionen.
    /// </summary>
    public async Task<int> ErgaenzeFehlendeAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var abos = await db.NutzungsAbos
            .Where(a => a.ZahlungStatus == AboZahlungStatus.Bestaetigt && a.ZahlungBestaetigtUtc != null && a.PreisSnapshot > 0
                && !db.PoZahlungseingaenge.Any(e => e.NutzungsAboId == a.Id)
                && db.ProduktOwnerSalons.Any(p => p.SalonId == a.SalonId && p.CreatedAtUtc <= a.ZahlungBestaetigtUtc
                    && (p.GueltigBisUtc == null || p.GueltigBisUtc > a.ZahlungBestaetigtUtc)
                    && (p.ProzentProvision > 0 || p.FestbetragProvision > 0)
                    && !db.PoProvisionen.Any(x => x.ProduktOwnerId == p.ProduktOwnerId && x.NutzungsAboId == a.Id)))
            .OrderBy(a => a.ZahlungBestaetigtUtc)
            .ToListAsync(ct);
        if (abos.Count == 0) return 0;
        var jetzt = Jetzt;
        foreach (var abo in abos) await ErfasseAsync(db, abo, jetzt, ct);
        var neu = db.ChangeTracker.Entries<PoProvision>().Count(e => e.State == EntityState.Added);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return 0; } // gleichzeitig von einem anderen Gerät ergänzt – beim nächsten Laden ist alles da
        return neu;
    }

    private static async Task PruefeAdminAsync(ApplicationDbContext db, Guid admin, CancellationToken ct)
        => await AdminKontenService.PruefeAsync(db, admin, false, ct);

    private static async Task PruefePoAsync(ApplicationDbContext db, Guid po, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == po && !u.IsBlocked && db.UserRoles.Any(ur => ur.UserId == po
                && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.ProduktOwner)), ct))
            throw new UnauthorizedAccessException("Vous ne disposez pas des droits nécessaires.");
    }

    private static IQueryable<PoProvision> NachStatus(IQueryable<PoProvision> q, PoProvisionStatus? status) => status switch
    {
        PoProvisionStatus.AVerser => q.Where(p => p.VerseeUtc == null && p.ErhaltenUtc == null),
        PoProvisionStatus.Versee => q.Where(p => p.VerseeUtc != null && p.ErhaltenUtc == null),
        PoProvisionStatus.Recue => q.Where(p => p.ErhaltenUtc != null),
        _ => q
    };

    private static async Task<Uebersicht> LadeAsync(IQueryable<PoProvision> basis, PoProvisionStatus? status,
        int ueberspringen, int anzahl, CancellationToken ct)
    {
        // Summen über alle Einträge (unabhängig von Filter und Seite); wenige Zeilen je Responsable und Monat.
        var werte = await basis.Select(p => new { p.Betrag, Versee = p.VerseeUtc != null, Erhalten = p.ErhaltenUtc != null }).ToListAsync(ct);
        var summen = new Summen(
            werte.Where(w => !w.Versee && !w.Erhalten).Sum(w => w.Betrag), werte.Count(w => !w.Versee && !w.Erhalten),
            werte.Where(w => w.Versee && !w.Erhalten).Sum(w => w.Betrag), werte.Count(w => w.Versee && !w.Erhalten),
            werte.Where(w => w.Erhalten).Sum(w => w.Betrag), werte.Count(w => w.Erhalten));
        var liste = await NachStatus(basis, status)
            .OrderBy(p => p.ErhaltenUtc != null ? 2 : p.VerseeUtc != null ? 1 : 0).ThenByDescending(p => p.ZahlungBestaetigtUtc).ThenBy(p => p.Laden)
            .Skip(ueberspringen).Take(anzahl + 1)
            .Select(p => new Eintrag(p.Id, p.ProduktOwnerId, p.ProduktOwner.FullName, p.Laden, p.ZeitraumVonUtc, p.ZeitraumBisUtc,
                p.ZahlungBestaetigtUtc, p.AboBetrag, p.ProzentProvision, p.FestProvision, p.Betrag, p.VerseeUtc, p.VersementReferenz,
                p.ErhaltenUtc, p.NichtErhaltenHinweis,
                p.ErhaltenUtc != null ? PoProvisionStatus.Recue : p.VerseeUtc != null ? PoProvisionStatus.Versee : PoProvisionStatus.AVerser))
            .ToListAsync(ct);
        var mehr = liste.Count > anzahl;
        if (mehr) liste.RemoveAt(liste.Count - 1);
        return new Uebersicht(summen, liste, mehr);
    }

    /// <summary>Provisionen aller (oder eines) Responsable für den Administrateur.</summary>
    public async Task<Uebersicht> GetFuerAdminAsync(Guid admin, Guid? poId, PoProvisionStatus? status, int ueberspringen = 0,
        int anzahl = 100, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeAdminAsync(db, admin, ct);
        var basis = db.PoProvisionen.AsNoTracking().Where(p => poId == null || p.ProduktOwnerId == poId);
        return await LadeAsync(basis, status, ueberspringen, anzahl, ct);
    }

    /// <summary>Eigene Provisionen des Responsable.</summary>
    public async Task<Uebersicht> GetFuerPoAsync(Guid po, PoProvisionStatus? status, int ueberspringen = 0, int anzahl = 100,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefePoAsync(db, po, ct);
        return await LadeAsync(db.PoProvisionen.AsNoTracking().Where(p => p.ProduktOwnerId == po), status, ueberspringen, anzahl, ct);
    }

    private static string Text(string? text, int max, string fehler)
    {
        var wert = text?.Trim() ?? "";
        if (wert.Length == 0) throw new ArgumentException(fehler);
        if (wert.Length > max) throw new ArgumentException($"Le texte est trop long ({max} caractères au maximum).");
        return wert;
    }

    private async Task EreignisAsync(ApplicationDbContext db, Guid po, Guid user, Guid vorgang, string aktion, decimal betrag, string? notiz, CancellationToken ct)
    {
        var name = await db.Users.Where(u => u.Id == user).Select(u => u.FullName).SingleAsync(ct);
        db.PoAbrechnungsEreignisse.Add(new PoAbrechnungsEreignis { ProduktOwnerId = po, BenutzerId = user, Benutzer = name,
            VorgangId = vorgang, ZeitpunktUtc = Jetzt, Aktion = aktion, Betrag = betrag, Notiz = notiz ?? "" });
    }

    /// <summary>
    /// Administrateur: mehrere Provisionen (auch verschiedener Responsables) als ausgezahlt markieren.
    /// Schon ausgezahlte oder erhaltene werden übersprungen. Liefert Anzahl und Summe der markierten.
    /// </summary>
    public async Task<(int Anzahl, decimal Summe)> MarkiereVerseeAsync(Guid admin, IReadOnlyCollection<Guid> ids, DateTime datum,
        string? referenz, CancellationToken ct = default)
    {
        if (ids.Count == 0) throw new ArgumentException("Sélectionnez au moins une commission.");
        var tag = DateTime.SpecifyKind(datum.Date, DateTimeKind.Utc);
        if (tag > Jetzt.Date) throw new ArgumentException("La date du versement ne peut pas être dans le futur.");
        var text = Text(referenz, 200, "Indiquez une référence (n° de transaction Mobile Money, virement, « espèces »…).");
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeAdminAsync(db, admin, ct);
        var liste = await db.PoProvisionen.Where(p => ids.Contains(p.Id) && p.VerseeUtc == null && p.ErhaltenUtc == null).ToListAsync(ct);
        if (liste.Count == 0) throw new InvalidOperationException("Ces commissions ont déjà été versées. Actualisez la liste.");
        var lauf = Guid.NewGuid();
        var jetzt = Jetzt;
        foreach (var p in liste)
        {
            p.VerseeUtc = tag; p.VersementReferenz = text; p.VersementVonId = admin; p.VersementErfasstUtc = jetzt; p.NichtErhaltenHinweis = null;
        }
        foreach (var g in liste.GroupBy(p => p.ProduktOwnerId))
            await EreignisAsync(db, g.Key, admin, lauf, "Commissions versées", g.Sum(p => p.Betrag),
                $"{g.Count()} commission(s) · versement du {tag:dd/MM/yyyy} · référence : {text}", ct);
        await db.SaveChangesAsync(ct);
        return (liste.Count, liste.Sum(p => p.Betrag));
    }

    /// <summary>Administrateur: irrtümlich als ausgezahlt markierte Provision zurücksetzen (nur solange nicht erhalten).</summary>
    public async Task AnnuliereVersementAsync(Guid admin, Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeAdminAsync(db, admin, ct);
        var p = await db.PoProvisionen.SingleAsync(p => p.Id == id, ct);
        if (p.ErhaltenUtc is not null) throw new InvalidOperationException("Le responsable a déjà confirmé la réception.");
        if (p.VerseeUtc is null) return;
        await EreignisAsync(db, p.ProduktOwnerId, admin, p.Id, "Versement de commission annulé", p.Betrag,
            $"{p.Laden} · référence annulée : {p.VersementReferenz}", ct);
        p.VerseeUtc = null; p.VersementReferenz = null; p.VersementVonId = null; p.VersementErfasstUtc = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Responsable: Empfang mehrerer ausgezahlter Provisionen bestätigen. Liefert Anzahl und Summe.</summary>
    public async Task<(int Anzahl, decimal Summe)> BestaetigeErhaltAsync(Guid po, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) throw new ArgumentException("Sélectionnez au moins une commission.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefePoAsync(db, po, ct);
        var liste = await db.PoProvisionen.Where(p => ids.Contains(p.Id) && p.ProduktOwnerId == po && p.VerseeUtc != null && p.ErhaltenUtc == null)
            .ToListAsync(ct);
        if (liste.Count == 0) throw new InvalidOperationException("Aucune de ces commissions n'est en attente de confirmation. Actualisez la liste.");
        var jetzt = Jetzt;
        foreach (var p in liste) p.ErhaltenUtc = jetzt;
        await EreignisAsync(db, po, po, Guid.NewGuid(), "Réception des commissions confirmée", liste.Sum(p => p.Betrag),
            $"{liste.Count} commission(s) · références : {string.Join(", ", liste.Select(p => p.VersementReferenz).Distinct())}", ct);
        await db.SaveChangesAsync(ct);
        return (liste.Count, liste.Sum(p => p.Betrag));
    }

    /// <summary>Responsable: ausgezahlte Provision ist nicht angekommen – zurück auf « à verser », mit Grund für den Administrateur.</summary>
    public async Task MeldeNichtErhaltenAsync(Guid po, Guid id, string? grund, CancellationToken ct = default)
    {
        var text = Text(grund, 500, "Indiquez pourquoi le versement n'est pas arrivé.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefePoAsync(db, po, ct);
        var p = await db.PoProvisionen.SingleOrDefaultAsync(p => p.Id == id && p.ProduktOwnerId == po, ct)
            ?? throw new UnauthorizedAccessException("Vous ne disposez pas des droits nécessaires.");
        if (p.ErhaltenUtc is not null || p.VerseeUtc is null) throw new InvalidOperationException("Cette commission n'est pas en attente de confirmation.");
        await EreignisAsync(db, po, po, p.Id, "Commission signalée non reçue", p.Betrag,
            $"{p.Laden} · référence : {p.VersementReferenz} · motif : {text}", ct);
        p.VerseeUtc = null; p.VersementReferenz = null; p.VersementVonId = null; p.VersementErfasstUtc = null;
        p.NichtErhaltenHinweis = text;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Responsables mit Provisionen oder Rolle – für den Filter des Administrateur.</summary>
    public async Task<List<(Guid Id, string Name)>> GetResponsablesAsync(Guid admin, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeAdminAsync(db, admin, ct);
        var liste = await db.Users.AsNoTracking()
            .Where(u => db.PoProvisionen.Any(p => p.ProduktOwnerId == u.Id) || db.UserRoles.Any(ur => ur.UserId == u.Id
                && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.ProduktOwner)))
            .OrderBy(u => u.FullName).Select(u => new { u.Id, u.FullName }).ToListAsync(ct);
        return liste.Select(u => (u.Id, u.FullName)).ToList();
    }

    /// <summary>Ältere Monatsberechnungen (vor der automatischen Provision) – nur zur Ansicht und zum Abschließen.</summary>
    public async Task<List<AltAuszahlung>> GetAltAuszahlungenAsync(Guid admin, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeAdminAsync(db, admin, ct);
        return await db.ProduktOwnerAuszahlungen.AsNoTracking()
            .OrderBy(a => a.Status).ThenByDescending(a => a.Jahr).ThenByDescending(a => a.Monat)
            .Select(a => new AltAuszahlung(a.Id, a.ProduktOwner.FullName, a.Jahr, a.Monat, a.Betrag, a.Aufschluesselung, a.Status == AuszahlungStatus.Bestaetigt))
            .ToListAsync(ct);
    }
}
