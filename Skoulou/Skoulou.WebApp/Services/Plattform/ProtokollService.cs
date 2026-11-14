using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Zeile des Fehlerjournals (Details erst beim Aufklappen).</summary>
public sealed record FehlerZeile(Guid Id, DateTime ZeitUtc, string Nachricht, string? Typ, string? Pfad, string? Schule, bool Bearbeitet);

/// <summary>Zeile des Audits.</summary>
public sealed record AuditZeile(Guid Id, DateTime ZeitUtc, string Aktion, string Text, string? Akteur, string? Schule, string ZielTyp);

/// <summary>Filter eines Journals (Tage in UTC, jeweils inklusive).</summary>
public sealed record ProtokollFilter(string? Suche = null, DateOnly? Von = null, DateOnly? Bis = null, Guid? SchuleId = null, bool NurOffene = false);

/// <summary>
/// Fehlerjournal (Entscheidung 22: 90 Tage, Bereinigung durch <see cref="AufraeumDienst"/>) und Audit
/// (unbegrenzt) – nur für den SuperAdmin, serverseitig gefiltert und geblättert.
/// </summary>
public sealed class ProtokollService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, TimeProvider zeit)
{
    public async Task<(IReadOnlyList<FehlerZeile> Eintraege, bool Mehr)> FehlerAsync(Guid akteurId, ProtokollFilter f,
        int ueberspringen = 0, int anzahl = 50, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.FehlerProtokolle.AsNoTracking();
        if (f.NurOffene) q = q.Where(x => !x.Bearbeitet);
        if (f.SchuleId is { } s) q = q.Where(x => x.SchuleId == s);
        if (f.Von is { } von) { var v = von.ToDateTime(TimeOnly.MinValue); q = q.Where(x => x.ZeitUtc >= v); }
        if (f.Bis is { } bis) { var b = bis.AddDays(1).ToDateTime(TimeOnly.MinValue); q = q.Where(x => x.ZeitUtc < b); }
        if (PlattformListen.Suchtext(f.Suche) is { } t) q = q.Where(x => x.Nachricht.Contains(t) || x.Typ!.Contains(t) || x.Pfad!.Contains(t));
        var abfrage = q.OrderByDescending(x => x.ZeitUtc).Select(x => new FehlerZeile(x.Id, x.ZeitUtc, x.Nachricht, x.Typ, x.Pfad,
            db.Schulen.Where(sc => sc.Id == x.SchuleId).Select(sc => sc.Name).FirstOrDefault(), x.Bearbeitet));
        return await PlattformListen.SeiteAsync(abfrage, ueberspringen, anzahl, ct);
    }

    public async Task<string?> FehlerDetailsAsync(Guid akteurId, Guid id, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.FehlerProtokolle.Where(x => x.Id == id).Select(x => x.Details).FirstOrDefaultAsync(ct);
    }

    /// <summary>Als bearbeitet markieren (bzw. wieder öffnen).</summary>
    public async Task BearbeitetAsync(Guid akteurId, Guid id, bool bearbeitet, CancellationToken ct = default)
    {
        var akteur = await zugriff.AdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var f = await db.FehlerProtokolle.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new FachFehler("Entrée introuvable.");
        f.Bearbeitet = bearbeitet;
        f.BearbeitetAmUtc = bearbeitet ? zeit.GetUtcNow().UtcDateTime : null;
        f.BearbeitetVonId = bearbeitet ? akteur.Id : null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Löschen (nach Rückfrage in der Oberfläche).</summary>
    public async Task FehlerLoeschenAsync(Guid akteurId, Guid id, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var f = await db.FehlerProtokolle.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new FachFehler("Entrée introuvable.");
        db.FehlerProtokolle.Remove(f);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Audit aller Schulen; Filter <paramref name="bereich"/> = Präfix der Aktion (z. B. « schule », « konto »).</summary>
    public async Task<(IReadOnlyList<AuditZeile> Eintraege, bool Mehr)> AuditAsync(Guid akteurId, ProtokollFilter f, string? bereich,
        int ueberspringen = 0, int anzahl = 50, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.AuditEintraege.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(bereich)) { var p = bereich.Trim() + "."; q = q.Where(x => x.Aktion.StartsWith(p)); }
        if (f.SchuleId is { } s) q = q.Where(x => x.SchuleId == s);
        if (f.Von is { } von) { var v = von.ToDateTime(TimeOnly.MinValue); q = q.Where(x => x.ZeitUtc >= v); }
        if (f.Bis is { } bis) { var b = bis.AddDays(1).ToDateTime(TimeOnly.MinValue); q = q.Where(x => x.ZeitUtc < b); }
        if (PlattformListen.Suchtext(f.Suche) is { } t) q = q.Where(x => x.Text.Contains(t) || x.Aktion.Contains(t));
        var abfrage = q.OrderByDescending(x => x.ZeitUtc).Select(x => new AuditZeile(x.Id, x.ZeitUtc, x.Aktion, x.Text,
            db.Users.Where(k => k.Id == x.AkteurId).Select(k => k.Vorname + " " + k.Nachname).FirstOrDefault(),
            db.Schulen.Where(sc => sc.Id == x.SchuleId).Select(sc => sc.Name).FirstOrDefault(), x.ZielTyp));
        return await PlattformListen.SeiteAsync(abfrage, ueberspringen, anzahl, ct);
    }

    /// <summary>Bereiche (Präfixe) der vorhandenen Audit-Aktionen für den Filter.</summary>
    public static readonly IReadOnlyList<(string Wert, string Text)> AuditBereiche =
    [
        ("schule", "Établissements"), ("konto", "Comptes"), ("eleve", "Élèves"), ("einschreibung", "Inscriptions en classe"),
        ("eltern", "Parents"), ("note", "Notes"), ("bulletin", "Bulletins"), ("finanzen", "Finances"), ("rechnung", "Factures SKOULOU"),
        ("schuljahr", "Années scolaires"), ("periode", "Périodes"), ("inhalt", "Contenus"), ("nachricht", "Messagerie"), ("medium", "Médias")
    ];
}
