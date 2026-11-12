using ENote.Core.DataModels.Kasse;
using ENote.Core.Services.Kasse;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Tickets « en attente »: ein noch nicht kassiertes Ticket zwischenspeichern und später fortsetzen –
/// von jeder Kassiererin des Ladens, auch nach Schichtwechsel oder Kassenabschluss.
/// Nichts wird gebucht, kein Lagerabgang; Preise gelten beim Fortsetzen wie im Katalog.
/// Je Kunde höchstens ein geparktes Ticket: erneutes Parken für denselben Kunden ergänzt es (Praxis: mehrere Tage).
/// Zugriff: Besitzer und Mitarbeitende des Ladens. Änderungen melden sich über <see cref="AnwesenheitsSignal"/>.
/// </summary>
public class TicketParkService(IDbContextFactory<ApplicationDbContext> factory, TimeProvider zeit, AnwesenheitsSignal signal)
{
    public sealed record PositionInput(Guid LeistungId, int Menge, decimal? FreierBetrag = null);

    public sealed record Eintrag(Guid Id, Guid? KundeId, string? KundeName, string? Notiz, int Artikel, decimal Summe, string Zeilen,
        DateTime ErstelltUtc, string ErstelltVon, DateTime GeaendertUtc, string GeaendertVon);

    /// <summary>Inhalt zum Fortsetzen; <paramref name="Ausgelassen"/> = Positionen, deren Leistung inzwischen inaktiv ist.</summary>
    public sealed record Uebernahme(Guid? KundeId, Guid? RabattId, bool RabattBedingungBestaetigt, string? Notiz,
        IReadOnlyList<PositionInput> Positionen, int Ausgelassen);

    public sealed record ParkErgebnis(Guid TicketId, bool Ergaenzt);

    private DateTime Jetzt => zeit.GetUtcNow().UtcDateTime;

    private static async Task<string> PruefeZugangAsync(ApplicationDbContext db, Guid user, Guid salonId, CancellationToken ct)
    {
        if (!await LadenZugriff.HatZugangAsync(db, user, salonId, ct)) throw new UnauthorizedAccessException("Accès refusé.");
        return await db.Users.AsNoTracking().Where(u => u.Id == user)
            .Select(u => u.FullName != "" ? u.FullName : (u.UserName ?? "")).FirstOrDefaultAsync(ct) ?? "";
    }

    /// <summary>Ticket in die Warteliste legen (oder das wartende Ticket desselben Kunden ergänzen).</summary>
    public async Task<ParkErgebnis> ParkeAsync(Guid user, Guid salonId, IReadOnlyList<PositionInput> positionen, Guid? kundeId,
        string? notiz, Guid? rabattId = null, bool rabattBedingungBestaetigt = false, CancellationToken ct = default)
    {
        var n = TicketParkRegeln.PruefeNotiz(notiz);
        if (positionen.Count == 0) throw new ArgumentException("Le ticket est vide.");
        if (positionen.Count > TicketParkRegeln.PositionenMax) throw new ArgumentException($"{TicketParkRegeln.PositionenMax} lignes au maximum.");
        if (positionen.Any(p => p.Menge < 1 || p.Menge > TicketParkRegeln.MengeMax)) throw new ArgumentException("Quantité invalide.");

        await using var db = await factory.CreateDbContextAsync(ct);
        var name = await PruefeZugangAsync(db, user, salonId, ct);

        var ids = positionen.Select(p => p.LeistungId).Distinct().ToList();
        var katalog = await db.Leistungen.AsNoTracking().Where(l => l.SalonId == salonId && ids.Contains(l.Id))
            .Select(l => new { l.Id, l.IstStandardArtikel }).ToDictionaryAsync(l => l.Id, ct);
        if (katalog.Count != ids.Count) throw new ArgumentException("Prestation introuvable.");
        if (positionen.Any(p => p.FreierBetrag is { } b && (b <= 0 || !katalog[p.LeistungId].IstStandardArtikel)))
            throw new ArgumentException("Montant invalide.");

        if (kundeId is Guid kid && !await db.Kunden.AnyAsync(k => k.Id == kid && k.SalonId == salonId, ct))
            throw new ArgumentException("Client introuvable.");

        var jetzt = Jetzt;
        var ticket = kundeId is Guid k2
            ? await db.GeparkteTickets.Include(t => t.Positionen).FirstOrDefaultAsync(t => t.SalonId == salonId && t.KundeId == k2, ct)
            : null;
        var ergaenzt = ticket is not null;
        if (ticket is null)
        {
            ticket = new GeparktesTicket
            {
                SalonId = salonId, KundeId = kundeId, ErstelltUtc = jetzt, ErstelltVonId = user, ErstelltVonName = name,
            };
            db.GeparkteTickets.Add(ticket);
        }

        var reihe = ticket.Positionen.Count == 0 ? 0 : ticket.Positionen.Max(p => p.Reihenfolge) + 1;
        foreach (var p in positionen)
        {
            var gleich = p.FreierBetrag is null
                ? ticket.Positionen.FirstOrDefault(x => x.LeistungId == p.LeistungId && x.FreierBetrag is null)
                : null;
            if (gleich is not null)
            {
                gleich.Menge = Math.Min(TicketParkRegeln.MengeMax, gleich.Menge + p.Menge);
            }
            else
            {
                var neu = new GeparktesTicketPosition { TicketId = ticket.Id, LeistungId = p.LeistungId, Menge = p.Menge, FreierBetrag = p.FreierBetrag, Reihenfolge = reihe++ };
                ticket.Positionen.Add(neu);
                db.GeparkteTicketPositionen.Add(neu);   // ausdrücklich: Schlüssel ist schon gesetzt
            }
        }
        if (ticket.Positionen.Count > TicketParkRegeln.PositionenMax) throw new ArgumentException($"{TicketParkRegeln.PositionenMax} lignes au maximum.");

        if (n is not null) ticket.Notiz = n;
        if (rabattId is not null) { ticket.RabattId = rabattId; ticket.RabattBedingungBestaetigt = rabattBedingungBestaetigt; }
        ticket.GeaendertUtc = jetzt;
        ticket.GeaendertVonName = name;
        await db.SaveChangesAsync(ct);
        signal.Melde(salonId);
        return new ParkErgebnis(ticket.Id, ergaenzt);
    }

    /// <summary>Alle wartenden Tickets des Ladens, zuletzt geändertes zuerst; Summe zu heutigen Katalogpreisen (ohne Remise).</summary>
    public async Task<List<Eintrag>> GetAsync(Guid user, Guid salonId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeZugangAsync(db, user, salonId, ct);
        var tickets = await db.GeparkteTickets.AsNoTracking()
            .Where(t => t.SalonId == salonId)
            .OrderByDescending(t => t.GeaendertUtc)
            .Select(t => new
            {
                t.Id, t.KundeId, KundeName = t.Kunde != null ? t.Kunde.Name : null, t.Notiz, t.ErstelltUtc, t.ErstelltVonName, t.GeaendertUtc, t.GeaendertVonName,
                Positionen = t.Positionen.OrderBy(p => p.Reihenfolge).Select(p => new { p.Menge, p.FreierBetrag, p.Leistung.Preis, p.Leistung.Name }).ToList(),
            })
            .Take(200).ToListAsync(ct);
        return tickets.Select(t => new Eintrag(t.Id, t.KundeId, t.KundeName, t.Notiz,
            t.Positionen.Sum(p => p.Menge),
            t.Positionen.Sum(p => (p.FreierBetrag ?? p.Preis) * p.Menge),
            string.Join(", ", t.Positionen.Select(p => p.Menge > 1 ? $"{p.Menge}× {p.Name}" : p.Name)),
            t.ErstelltUtc, t.ErstelltVonName, t.GeaendertUtc, t.GeaendertVonName)).ToList();
    }

    public async Task<int> ZaehleAsync(Guid user, Guid salonId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await LadenZugriff.HatZugangAsync(db, user, salonId, ct)) return 0;
        return await db.GeparkteTickets.CountAsync(t => t.SalonId == salonId, ct);
    }

    /// <summary>Wartendes Ticket fortsetzen: Inhalt zurückgeben und aus der Warteliste nehmen (nur eine Person kann es übernehmen).</summary>
    public async Task<Uebernahme> UebernehmeAsync(Guid user, Guid ticketId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ticket = await db.GeparkteTickets.Include(t => t.Positionen).ThenInclude(p => p.Leistung)
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct)
            ?? throw new ArgumentException("Ce ticket n'est plus en attente (déjà repris ou supprimé).");
        await PruefeZugangAsync(db, user, ticket.SalonId, ct);

        var aktiv = ticket.Positionen.Where(p => p.Leistung.IstAktiv).OrderBy(p => p.Reihenfolge)
            .Select(p => new PositionInput(p.LeistungId, p.Menge, p.FreierBetrag)).ToList();
        var ergebnis = new Uebernahme(ticket.KundeId, ticket.RabattId, ticket.RabattBedingungBestaetigt, ticket.Notiz, aktiv,
            ticket.Positionen.Count - aktiv.Count);

        db.GeparkteTickets.Remove(ticket);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ArgumentException("Ce ticket vient d'être repris à une autre caisse."); }
        signal.Melde(ticket.SalonId);
        return ergebnis;
    }

    /// <summary>Wartendes Ticket eines Kunden fortsetzen (z. B. aus « Clients présents » → Encaisser); null, wenn keins wartet.</summary>
    public async Task<Uebernahme?> UebernehmeFuerKundeAsync(Guid user, Guid salonId, Guid kundeId, CancellationToken ct = default)
    {
        Guid? id;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            await PruefeZugangAsync(db, user, salonId, ct);
            id = await db.GeparkteTickets.Where(t => t.SalonId == salonId && t.KundeId == kundeId).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        }
        return id is Guid t ? await UebernehmeAsync(user, t, ct) : null;
    }

    public async Task LoescheAsync(Guid user, Guid ticketId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ticket = await db.GeparkteTickets.Include(t => t.Positionen).FirstOrDefaultAsync(t => t.Id == ticketId, ct);
        if (ticket is null) return;
        await PruefeZugangAsync(db, user, ticket.SalonId, ct);
        db.GeparkteTickets.Remove(ticket);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { /* schon weg */ }
        signal.Melde(ticket.SalonId);
    }
}
