using ENote.Core.DataModels.Kasse;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Kundenkartei eines Ladens. Kasse (Kassierer + Besitzer): suchen und schnell anlegen, am Ticket wählen.
/// Besitzer: Liste mit Besuchen/Umsatz, Notiz, Bearbeiten, Löschen (Verkäufe bleiben, nur ohne Kunde).
/// Telefonnummer nur mit Zustimmung des Kunden; der Kassierer sieht sie nur gekürzt.
/// </summary>
public class KundenService(IDbContextFactory<ApplicationDbContext> factory, TimeProvider zeit)
{
    public const int NameMax = 100, NotizMax = 1000;

    public sealed record Kurz(Guid Id, string Name, string? TelefonGekuerzt, int Besuche, DateTime? LetzterBesuchUtc);

    public sealed record Zeile(Guid Id, string Name, string? Telefon, int Besuche, decimal Umsatz, DateTime? LetzterBesuchUtc, DateTime ErstelltUtc, bool HatNotiz);

    public sealed record Kennzahlen(int Kunden, int NeuDiesenMonat, int Wiederkehrend, int AnteilVerkaeufeMitKundeProzent);

    public sealed record Besuch(Guid VerkaufId, DateTime ZeitUtc, decimal Betrag, string Inhalt, bool IstRueckerstattung);

    public sealed record Detail(Guid Id, string Name, string? Telefon, string? Notiz, DateTime ErstelltUtc,
        int Besuche, decimal Umsatz, decimal Durchschnitt, DateTime? ErsterBesuchUtc, DateTime? LetzterBesuchUtc,
        IReadOnlyList<(string Leistung, int Menge)> Lieblinge, IReadOnlyList<Besuch> LetzteBesuche);

    // ------------------------------------------------------------------ Hilfen

    /// <summary>Nur Ziffern; „+228 90 12 34 56“ → „22890123456“.</summary>
    public static string? Ziffern(string? telefon)
    {
        if (string.IsNullOrWhiteSpace(telefon)) return null;
        var z = new string(telefon.Where(char.IsDigit).ToArray());
        return z.Length == 0 ? null : z;
    }

    /// <summary>Für die Kasse: nur die letzten vier Ziffern („•• 34 56“).</summary>
    public static string? Gekuerzt(string? ziffern)
        => ziffern is { Length: >= 4 } z ? $"•• {z[^4..^2]} {z[^2..]}" : null;

    private static (string Name, string? Telefon, string? Ziffern) Pruefe(string name, string? telefon, bool einwilligung)
    {
        var n = (name ?? "").Trim();
        if (n.Length < 2) throw new ArgumentException("Saisissez le nom du client (au moins 2 lettres).");
        if (n.Length > NameMax) throw new ArgumentException($"Nom : {NameMax} caractères au maximum.");
        var t = string.IsNullOrWhiteSpace(telefon) ? null : telefon.Trim();
        var z = Ziffern(t);
        if (t is not null)
        {
            if (z is null || z.Length < 6 || z.Length > 15 || t.Any(c => !(char.IsDigit(c) || c is ' ' or '+' or '-' or '.' or '(' or ')')))
                throw new ArgumentException("Numéro de téléphone invalide.");
            if (!einwilligung)
                throw new ArgumentException("Le numéro n'est enregistré qu'avec l'accord du client : cochez la case.");
        }
        return (n, t, z);
    }

    private static async Task PruefeZugangAsync(ApplicationDbContext db, Guid user, Guid salonId, bool besitzer, CancellationToken ct)
    {
        var ok = besitzer ? await LadenZugriff.IstBesitzerAsync(db, user, salonId, ct) : await LadenZugriff.HatZugangAsync(db, user, salonId, ct);
        if (!ok) throw new UnauthorizedAccessException("Accès refusé.");
    }

    private static IQueryable<Verkauf> VerkaeufeVon(ApplicationDbContext db, Guid salonId)
        => db.Verkaeufe.AsNoTracking().Where(v => v.SalonId == salonId && v.KundeId != null);

    // ------------------------------------------------------------------ Kasse

    /// <summary>Suche nach Name oder Telefonnummer (max. 8 Treffer, zuletzt gesehene zuerst).</summary>
    public async Task<List<Kurz>> SucheAsync(Guid user, Guid salonId, string text, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeZugangAsync(db, user, salonId, false, ct);
        var s = (text ?? "").Trim();
        if (s.Length == 0) return [];
        var ziffern = Ziffern(s);
        var klein = s.ToLower();
        var treffer = await db.Kunden.AsNoTracking().Where(k => k.SalonId == salonId
                && (k.Name.ToLower().Contains(klein) || (ziffern != null && ziffern.Length >= 3 && k.TelefonZiffern != null && k.TelefonZiffern.Contains(ziffern))))
            .Select(k => new { k.Id, k.Name, k.TelefonZiffern })
            .Take(40).ToListAsync(ct);
        var ids = treffer.Select(t => t.Id).ToList();
        var stat = await VerkaeufeVon(db, salonId).Where(v => ids.Contains(v.KundeId!.Value) && v.StornoVonVerkaufId == null)
            .GroupBy(v => v.KundeId!.Value).Select(g => new { Id = g.Key, Anzahl = g.Count(), Letzter = g.Max(v => v.ZeitUtc) })
            .ToDictionaryAsync(x => x.Id, ct);
        return treffer
            .Select(t => new Kurz(t.Id, t.Name, Gekuerzt(t.TelefonZiffern), stat.GetValueOrDefault(t.Id)?.Anzahl ?? 0, stat.GetValueOrDefault(t.Id)?.Letzter))
            .OrderByDescending(k => k.Name.StartsWith(s, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(k => k.LetzterBesuchUtc ?? DateTime.MinValue).ThenBy(k => k.Name)
            .Take(8).ToList();
    }

    /// <summary>Einzelner Kunde für die Kasse (z. B. aus « Clients présents » → Encaisser).</summary>
    public async Task<Kurz?> GetKurzAsync(Guid user, Guid salonId, Guid kundeId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeZugangAsync(db, user, salonId, false, ct);
        var k = await db.Kunden.AsNoTracking().Where(x => x.Id == kundeId && x.SalonId == salonId)
            .Select(x => new { x.Id, x.Name, x.TelefonZiffern }).FirstOrDefaultAsync(ct);
        if (k is null) return null;
        var anzahl = await VerkaeufeVon(db, salonId).CountAsync(v => v.KundeId == k.Id && v.StornoVonVerkaufId == null, ct);
        var letzter = await VerkaeufeVon(db, salonId).Where(v => v.KundeId == k.Id && v.StornoVonVerkaufId == null).MaxAsync(v => (DateTime?)v.ZeitUtc, ct);
        return new Kurz(k.Id, k.Name, Gekuerzt(k.TelefonZiffern), anzahl, letzter);
    }

    /// <summary>Schnell anlegen (Kasse oder Kundenseite). Gleiche Nummer im Laden → Hinweis auf den vorhandenen Kunden.</summary>
    public async Task<Kurz> LegeAnAsync(Guid user, Guid salonId, string name, string? telefon, bool einwilligung, CancellationToken ct = default)
    {
        var (n, t, z) = Pruefe(name, telefon, einwilligung);
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeZugangAsync(db, user, salonId, false, ct);
        if (z is not null && await db.Kunden.AsNoTracking().FirstOrDefaultAsync(k => k.SalonId == salonId && k.TelefonZiffern == z, ct) is { } vorhanden)
            throw new ArgumentException($"Ce numéro est déjà enregistré pour « {vorhanden.Name} ».");
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        var kunde = new Kunde
        {
            SalonId = salonId, Name = n, Telefon = t, TelefonZiffern = z, TelefonEinwilligungUtc = t is null ? null : jetzt,
            ErstelltUtc = jetzt, ErstelltVonId = user
        };
        db.Kunden.Add(kunde);
        await db.SaveChangesAsync(ct);
        return new Kurz(kunde.Id, kunde.Name, Gekuerzt(z), 0, null);
    }

    // ------------------------------------------------------------------ Besitzer

    public async Task<Kennzahlen> GetKennzahlenAsync(Guid user, Guid salonId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeZugangAsync(db, user, salonId, true, ct);
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        var monatsBeginn = new DateTime(jetzt.Year, jetzt.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var kunden = await db.Kunden.CountAsync(k => k.SalonId == salonId, ct);
        var neu = await db.Kunden.CountAsync(k => k.SalonId == salonId && k.ErstelltUtc >= monatsBeginn, ct);
        var wiederkehrend = await VerkaeufeVon(db, salonId).Where(v => v.StornoVonVerkaufId == null)
            .GroupBy(v => v.KundeId).CountAsync(g => g.Count() >= 2, ct);
        var seit = jetzt.AddDays(-30);
        var alle = await db.Verkaeufe.CountAsync(v => v.SalonId == salonId && v.StornoVonVerkaufId == null && v.ZeitUtc >= seit, ct);
        var mit = await db.Verkaeufe.CountAsync(v => v.SalonId == salonId && v.StornoVonVerkaufId == null && v.ZeitUtc >= seit && v.KundeId != null, ct);
        return new Kennzahlen(kunden, neu, wiederkehrend, alle == 0 ? 0 : (int)Math.Round(100.0 * mit / alle));
    }

    /// <summary>Kundenliste mit Besuchen und Umsatz (Rückerstattungen abgezogen), zuletzt gesehene zuerst.</summary>
    public async Task<List<Zeile>> GetListeAsync(Guid user, Guid salonId, string? suche, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await PruefeZugangAsync(db, user, salonId, true, ct);
        var q = db.Kunden.AsNoTracking().Where(k => k.SalonId == salonId);
        if (!string.IsNullOrWhiteSpace(suche))
        {
            var s = suche.Trim().ToLower();
            var z = Ziffern(suche);
            q = q.Where(k => k.Name.ToLower().Contains(s) || (z != null && z.Length >= 3 && k.TelefonZiffern != null && k.TelefonZiffern.Contains(z)));
        }
        var kunden = await q.Select(k => new { k.Id, k.Name, k.Telefon, k.ErstelltUtc, HatNotiz = k.Notiz != null && k.Notiz != "" }).ToListAsync(ct);
        var ids = kunden.Select(k => k.Id).ToList();
        var stat = await VerkaeufeVon(db, salonId).Where(v => ids.Contains(v.KundeId!.Value))
            .GroupBy(v => v.KundeId!.Value)
            .Select(g => new
            {
                Id = g.Key,
                Besuche = g.Count(v => v.StornoVonVerkaufId == null),
                Umsatz = g.Sum(v => v.GesamtSumme),
                Letzter = g.Where(v => v.StornoVonVerkaufId == null).Max(v => (DateTime?)v.ZeitUtc)
            }).ToDictionaryAsync(x => x.Id, ct);
        return kunden.Select(k =>
            {
                var st = stat.GetValueOrDefault(k.Id);
                return new Zeile(k.Id, k.Name, k.Telefon, st?.Besuche ?? 0, st?.Umsatz ?? 0, st?.Letzter, k.ErstelltUtc, k.HatNotiz);
            })
            .OrderByDescending(z => z.LetzterBesuchUtc ?? z.ErstelltUtc).ThenBy(z => z.Name)
            .Take(300).ToList();
    }

    public async Task<Detail> GetDetailAsync(Guid user, Guid kundeId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var k = await db.Kunden.AsNoTracking().FirstOrDefaultAsync(x => x.Id == kundeId, ct) ?? throw new ArgumentException("Client introuvable.");
        await PruefeZugangAsync(db, user, k.SalonId, true, ct);
        var verkaeufe = await db.Verkaeufe.AsNoTracking().Include(v => v.Positionen)
            .Where(v => v.SalonId == k.SalonId && v.KundeId == k.Id).OrderByDescending(v => v.ZeitUtc).ToListAsync(ct);
        var echte = verkaeufe.Where(v => v.StornoVonVerkaufId == null).ToList();
        var umsatz = verkaeufe.Sum(v => v.GesamtSumme);
        var lieblinge = verkaeufe.SelectMany(v => v.Positionen)
            .GroupBy(p => p.LeistungName).Select(g => (Leistung: g.Key, Menge: g.Sum(p => p.Menge)))
            .Where(x => x.Menge > 0).OrderByDescending(x => x.Menge).Take(3).ToList();
        var letzte = verkaeufe.Take(10).Select(v => new Besuch(v.Id, v.ZeitUtc, v.GesamtSumme,
            string.Join(", ", v.Positionen.Select(p => (Math.Abs(p.Menge) == 1 ? "" : $"{Math.Abs(p.Menge)}× ") + p.LeistungName)),
            v.StornoVonVerkaufId != null)).ToList();
        return new Detail(k.Id, k.Name, k.Telefon, k.Notiz, k.ErstelltUtc, echte.Count, umsatz,
            echte.Count == 0 ? 0 : Math.Round(umsatz / echte.Count, 0), echte.LastOrDefault()?.ZeitUtc, echte.FirstOrDefault()?.ZeitUtc,
            lieblinge, letzte);
    }

    public async Task AktualisiereAsync(Guid user, Guid kundeId, string name, string? telefon, bool einwilligung, string? notiz, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var k = await db.Kunden.FirstOrDefaultAsync(x => x.Id == kundeId, ct) ?? throw new ArgumentException("Client introuvable.");
        await PruefeZugangAsync(db, user, k.SalonId, true, ct);
        // Bereits gespeicherte Nummer: Zustimmung liegt vor, solange sie unverändert bleibt
        var unveraendert = Ziffern(telefon) is { } zNeu && zNeu == k.TelefonZiffern;
        var (n, t, z) = Pruefe(name, telefon, einwilligung || unveraendert);
        if (z is not null && z != k.TelefonZiffern
            && await db.Kunden.AsNoTracking().FirstOrDefaultAsync(x => x.SalonId == k.SalonId && x.TelefonZiffern == z && x.Id != k.Id, ct) is { } andere)
            throw new ArgumentException($"Ce numéro est déjà enregistré pour « {andere.Name} ».");
        var no = string.IsNullOrWhiteSpace(notiz) ? null : notiz.Trim();
        if (no is { Length: > NotizMax }) throw new ArgumentException($"Note : {NotizMax} caractères au maximum.");
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        if (z != k.TelefonZiffern) k.TelefonEinwilligungUtc = z is null ? null : jetzt;
        k.Name = n; k.Telefon = t; k.TelefonZiffern = z; k.Notiz = no; k.GeaendertUtc = jetzt;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Löscht den Kunden (z. B. auf seinen Wunsch) samt Notizen und Passagen. Seine Verkäufe bleiben – nur ohne Kundenbezug.</summary>
    public async Task LoescheAsync(Guid user, Guid kundeId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var k = await db.Kunden.FirstOrDefaultAsync(x => x.Id == kundeId, ct) ?? throw new ArgumentException("Client introuvable.");
        await PruefeZugangAsync(db, user, k.SalonId, true, ct);
        foreach (var v in await db.Verkaeufe.Where(v => v.KundeId == k.Id).ToListAsync(ct)) v.KundeId = null;
        db.KundenNotizen.RemoveRange(await db.KundenNotizen.Where(n => n.KundeId == k.Id).ToListAsync(ct));
        db.KundenBesuche.RemoveRange(await db.KundenBesuche.Where(b => b.KundeId == k.Id).ToListAsync(ct));
        db.Kunden.Remove(k);
        await db.SaveChangesAsync(ct);
    }
}
