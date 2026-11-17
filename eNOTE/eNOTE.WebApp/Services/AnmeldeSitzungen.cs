using ENote.Core.DataModels.Identity;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// « Appareils connectés » (17.11.2026): Anmeldungen je Gerät. Besitzer-Konten dürfen auf mehreren Geräten gleichzeitig
/// angemeldet sein (zwei Kassen im Laden); alle anderen Konten behalten « eine Anmeldung » (neue Anmeldung beendet die alte).
/// Gültig ist eine Sitzung nur, solange ihr Stempel dem Konto-Stempel (ActiveSessionId) entspricht und sie nicht beendet wurde.
/// </summary>
public class AnmeldeSitzungen(IDbContextFactory<ApplicationDbContext> factory, TimeProvider uhr)
{
    /// <summary>Name des Cookie-Claims mit der Geräte-Sitzung (der Konto-Stempel steht weiter in « enote_session »).</summary>
    public const string GeraetClaim = "enote_geraet";

    /// <summary>Zuletzt-gesehen höchstens alle 5 Minuten schreiben (sonst bei jeder Anfrage).</summary>
    public static readonly TimeSpan AktivitaetsIntervall = TimeSpan.FromMinutes(5);

    /// <summary>Beendete/abgelaufene Einträge werden nach dieser Zeit beim nächsten Login aufgeräumt.</summary>
    public static readonly TimeSpan Aufbewahrung = TimeSpan.FromDays(60);

    public sealed record Geraet(Guid Id, string Beschreibung, DateTime ErstelltUtc, DateTime ZuletztUtc, bool IstDiesesGeraet);

    private DateTime Jetzt => uhr.GetUtcNow().UtcDateTime;

    /// <summary>Besitzer-Konten: mehrere Geräte gleichzeitig. Alle anderen: eine Anmeldung.</summary>
    public static async Task<bool> MehrereGeraeteErlaubtAsync(ApplicationDbContext db, Guid userId, CancellationToken ct = default)
        => await db.UserRoles.AnyAsync(ur => ur.UserId == userId && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.Besitzer), ct);

    /// <summary>
    /// Neue Anmeldung: Stempel festlegen (bei « eine Anmeldung » oder ohne Stempel neu → frühere Geräte sind abgemeldet)
    /// und die Geräte-Sitzung anlegen. Liefert (Konto-Stempel, Geräte-Sitzung).
    /// </summary>
    public async Task<(Guid Stempel, Guid SitzungId)> AnmeldenAsync(Guid userId, string? userAgent, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        var jetzt = Jetzt;
        if (user.ActiveSessionId is null || !await MehrereGeraeteErlaubtAsync(db, userId, ct))
        {
            user.ActiveSessionId = Guid.NewGuid();
            user.UpdatedAtUtc = jetzt;
        }

        // Aufräumen: ungültige (anderer Stempel), beendete und lange unbenutzte Einträge des Kontos.
        var grenze = jetzt - Aufbewahrung;
        var alt = await db.AnmeldeSitzungen
            .Where(s => s.UserId == userId && (s.Stempel != user.ActiveSessionId || s.BeendetUtc != null || s.ZuletztUtc < grenze))
            .ToListAsync(ct);
        db.AnmeldeSitzungen.RemoveRange(alt);

        var sitzung = new AnmeldeSitzung
        {
            UserId = userId, Stempel = user.ActiveSessionId.Value, Geraet = GeraetAus(userAgent),
            ErstelltUtc = jetzt, ZuletztUtc = jetzt
        };
        db.AnmeldeSitzungen.Add(sitzung);
        await db.SaveChangesAsync(ct);
        return (user.ActiveSessionId.Value, sitzung.Id);
    }

    /// <summary>
    /// Prüfung bei jeder Anfrage. Mit Geräte-Sitzung: muss existieren, offen sein und zum Konto-Stempel passen (Zuletzt-gesehen
    /// wird höchstens alle 5 Minuten aktualisiert). Ohne (Cookie von vor dem 17.11.2026): wird nachträglich angelegt –
    /// dann liefert die Methode die neue Sitzungs-ID, damit das Cookie erneuert werden kann.
    /// </summary>
    public async Task<(bool Gueltig, Guid? NeueSitzungId)> PruefeAsync(Guid userId, Guid kontoStempel, Guid? sitzungId, string? userAgent,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var jetzt = Jetzt;
        if (sitzungId is Guid id)
        {
            var s = await db.AnmeldeSitzungen.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
            if (s is null || s.BeendetUtc is not null || s.Stempel != kontoStempel) return (false, null);
            if (jetzt - s.ZuletztUtc >= AktivitaetsIntervall)
            {
                s.ZuletztUtc = jetzt;
                await db.SaveChangesAsync(ct);
            }
            return (true, null);
        }

        var neu = new AnmeldeSitzung { UserId = userId, Stempel = kontoStempel, Geraet = GeraetAus(userAgent), ErstelltUtc = jetzt, ZuletztUtc = jetzt };
        db.AnmeldeSitzungen.Add(neu);
        await db.SaveChangesAsync(ct);
        return (true, neu.Id);
    }

    /// <summary>Abmelden dieses Geräts (Knopf « Se déconnecter »).</summary>
    public async Task BeendeAsync(Guid userId, Guid? sitzungId, CancellationToken ct = default)
    {
        if (sitzungId is not Guid id) return;
        await using var db = await factory.CreateDbContextAsync(ct);
        var s = await db.AnmeldeSitzungen.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (s is null || s.BeendetUtc is not null) return;
        s.BeendetUtc = Jetzt;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Angemeldete Geräte des Kontos (nur gültige), dieses Gerät zuerst.</summary>
    public async Task<List<Geraet>> GetGeraeteAsync(Guid userId, Guid? diesesGeraet, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var stempel = await db.Users.Where(u => u.Id == userId).Select(u => u.ActiveSessionId).FirstOrDefaultAsync(ct);
        if (stempel is null) return [];
        var grenze = Jetzt - Aufbewahrung;
        var liste = await db.AnmeldeSitzungen.AsNoTracking()
            .Where(s => s.UserId == userId && s.Stempel == stempel && s.BeendetUtc == null && s.ZuletztUtc >= grenze)
            .OrderByDescending(s => s.ZuletztUtc)
            .Select(s => new Geraet(s.Id, s.Geraet, s.ErstelltUtc, s.ZuletztUtc, s.Id == diesesGeraet))
            .ToListAsync(ct);
        return liste.OrderByDescending(g => g.IstDiesesGeraet).ToList();
    }

    /// <summary>Ein anderes Gerät abmelden (« Déconnecter »). Das eigene Gerät meldet man über « Se déconnecter » ab.</summary>
    public async Task<bool> MeldeGeraetAbAsync(Guid userId, Guid geraetId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var s = await db.AnmeldeSitzungen.FirstOrDefaultAsync(x => x.Id == geraetId && x.UserId == userId && x.BeendetUtc == null, ct);
        if (s is null) return false;
        s.BeendetUtc = Jetzt;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Alle anderen Geräte abmelden; dieses bleibt angemeldet. Liefert die Anzahl.</summary>
    public async Task<int> MeldeAndereAbAsync(Guid userId, Guid? diesesGeraet, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var jetzt = Jetzt;
        var andere = await db.AnmeldeSitzungen.Where(s => s.UserId == userId && s.BeendetUtc == null && s.Id != diesesGeraet).ToListAsync(ct);
        foreach (var s in andere) s.BeendetUtc = jetzt;
        await db.SaveChangesAsync(ct);
        return andere.Count;
    }

    /// <summary>Kurze, verständliche Gerätebeschreibung aus dem User-Agent, z. B. « Chrome · Android ».</summary>
    public static string GeraetAus(string? userAgent)
    {
        var ua = userAgent ?? "";
        string browser = ua.Contains("Edg/", StringComparison.Ordinal) ? "Edge"
            : ua.Contains("OPR/", StringComparison.Ordinal) || ua.Contains("Opera", StringComparison.Ordinal) ? "Opera"
            : ua.Contains("SamsungBrowser", StringComparison.Ordinal) ? "Samsung Internet"
            : ua.Contains("Firefox/", StringComparison.Ordinal) ? "Firefox"
            : ua.Contains("Chrome/", StringComparison.Ordinal) || ua.Contains("CriOS/", StringComparison.Ordinal) ? "Chrome"
            : ua.Contains("Safari/", StringComparison.Ordinal) ? "Safari"
            : "Navigateur";
        string system = ua.Contains("Android", StringComparison.Ordinal) ? "Android"
            : ua.Contains("iPhone", StringComparison.Ordinal) ? "iPhone"
            : ua.Contains("iPad", StringComparison.Ordinal) ? "iPad"
            : ua.Contains("Windows", StringComparison.Ordinal) ? "Windows"
            : ua.Contains("Mac OS", StringComparison.Ordinal) ? "Mac"
            : ua.Contains("Linux", StringComparison.Ordinal) ? "Linux"
            : "appareil inconnu";
        return $"{browser} · {system}";
    }
}
