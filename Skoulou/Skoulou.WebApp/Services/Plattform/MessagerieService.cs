using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Mails;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Zeile der Messagerie (Inhalt erst beim Öffnen).</summary>
public sealed record NachrichtZeile(Guid Id, NachrichtRichtung Richtung, string Von, string An, string Betreff, DateTime ZeitUtc,
    bool Gelesen, Guid? SchuleId, string? Schule);

/// <summary>Geöffnete Nachricht.</summary>
public sealed record NachrichtDetail(Guid Id, NachrichtRichtung Richtung, string Von, string An, string Betreff, string TextHtml,
    DateTime ZeitUtc, Guid? SchuleId, string? Schule, Guid? AntwortAufId, IReadOnlyList<NachrichtZeile> Antworten);

/// <summary>Empfänger einer neuen Nachricht (Direktion einer Schule).</summary>
public sealed record NachrichtEmpfaenger(string Email, string Name, string Rolle);

/// <summary>Ordner und Filter der Messagerie.</summary>
public static class Postfach
{
    public const string Eingang = "reception";
    public const string Ausgang = "envoyes";
    public const string Ungelesen = "non-lus";
    public const string OhneSchule = "sans-ecole";
}

/// <summary>
/// Messagerie (Fachkonzept 01 §3.15, verbessert): SuperAdmin sieht alles, ordnet zu und schreibt frei;
/// Responsable sieht nur Nachrichten seiner Schulen und darf darauf antworten bzw. der Direktion seiner
/// Schulen schreiben. Antworten gehen über die Outbox mit « [SKOULOU] » und « Re: » (nie doppelt).
/// </summary>
public sealed class MessagerieService(
    IDbContextFactory<SkoulouDbContext> dbFactory,
    Zugriff zugriff,
    AuditService audit,
    PlattformMails mails,
    IOptionsMonitor<PostmarkOptionen> postmark,
    TimeProvider zeit)
{
    public static string Antwortbetreff(string betreff)
    {
        var b = betreff.Trim();
        return b.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) || b.StartsWith("RE :", StringComparison.OrdinalIgnoreCase) ? b : "Re: " + b;
    }

    private static IQueryable<Nachricht> Sichtbar(SkoulouDbContext db, Akteur akteur)
        => akteur.IstAdmin ? db.Nachrichten : db.Nachrichten.Where(n => n.SchuleId != null && db.Schulen.Any(s => s.Id == n.SchuleId && s.ResponsableId == akteur.Id));

    public async Task<int> UngeleseneAsync(Guid akteurId, CancellationToken ct = default)
    {
        var akteur = await zugriff.BetreuerAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Sichtbar(db, akteur).CountAsync(n => n.Richtung == NachrichtRichtung.Eingang && n.GelesenAmUtc == null, ct);
    }

    public async Task<(IReadOnlyList<NachrichtZeile> Eintraege, bool Mehr)> SucheAsync(Guid akteurId, string ordner, string? suche, string? filter,
        int ueberspringen = 0, int anzahl = 50, CancellationToken ct = default)
    {
        var akteur = await zugriff.BetreuerAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var richtung = ordner == Postfach.Ausgang ? NachrichtRichtung.Ausgang : NachrichtRichtung.Eingang;
        var q = Sichtbar(db, akteur).AsNoTracking().Where(n => n.Richtung == richtung);
        if (filter == Postfach.Ungelesen) q = q.Where(n => n.GelesenAmUtc == null);
        if (filter == Postfach.OhneSchule) q = q.Where(n => n.SchuleId == null);
        if (PlattformListen.Suchtext(suche) is { } t)
            q = q.Where(n => n.Betreff.Contains(t) || n.Von.Contains(t) || n.An.Contains(t) || n.TextHtml.Contains(t));
        var abfrage = q.OrderByDescending(n => n.ErstelltAmUtc).Select(n => new NachrichtZeile(n.Id, n.Richtung, n.Von, n.An, n.Betreff,
            n.ErstelltAmUtc, n.GelesenAmUtc != null, n.SchuleId, db.Schulen.Where(s => s.Id == n.SchuleId).Select(s => s.Name).FirstOrDefault()));
        return await PlattformListen.SeiteAsync(abfrage, ueberspringen, anzahl, ct);
    }

    /// <summary>Öffnet eine Nachricht (eingehend: als gelesen markieren).</summary>
    public async Task<NachrichtDetail> OeffnenAsync(Guid akteurId, Guid id, CancellationToken ct = default)
    {
        var akteur = await zugriff.BetreuerAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var n = await Sichtbar(db, akteur).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new FachFehler("Message introuvable.");
        if (n.Richtung == NachrichtRichtung.Eingang && n.GelesenAmUtc is null)
        {
            n.GelesenAmUtc = zeit.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }
        var schule = n.SchuleId is null ? null : await db.Schulen.Where(s => s.Id == n.SchuleId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        var antworten = await db.Nachrichten.AsNoTracking().Where(a => a.AntwortAufId == n.Id).OrderBy(a => a.ErstelltAmUtc)
            .Select(a => new NachrichtZeile(a.Id, a.Richtung, a.Von, a.An, a.Betreff, a.ErstelltAmUtc, true, a.SchuleId, null)).ToListAsync(ct);
        return new NachrichtDetail(n.Id, n.Richtung, n.Von, n.An, n.Betreff, n.TextHtml, n.ErstelltAmUtc, n.SchuleId, schule, n.AntwortAufId, antworten);
    }

    /// <summary>Antwort auf eine sichtbare Nachricht (an den Absender bzw. bei Ausgang an denselben Empfänger).</summary>
    public async Task<Guid> AntwortenAsync(Guid akteurId, Guid nachrichtId, string? text, CancellationToken ct = default)
    {
        var akteur = await zugriff.BetreuerAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var n = await Sichtbar(db, akteur).AsNoTracking().FirstOrDefaultAsync(x => x.Id == nachrichtId, ct) ?? throw new FachFehler("Message introuvable.");
        var an = n.Richtung == NachrichtRichtung.Eingang ? n.Von : n.An;
        if (!System.Net.Mail.MailAddress.TryCreate(an, out var adresse)) throw new FachFehler("L'adresse du destinataire n'est pas valide.");
        return await SendenAsync(db, akteur, adresse.Address, Antwortbetreff(n.Betreff), text, n.SchuleId, n.Id, ct);
    }

    /// <summary>
    /// Neue Nachricht: SuperAdmin an jede Adresse (Schule optional); Responsable nur an Konten seiner Schulen
    /// (Schule Pflicht, Empfänger muss zu dieser Schule gehören).
    /// </summary>
    public async Task<Guid> NeuAsync(Guid akteurId, string? an, string? betreff, string? text, Guid? schuleId, CancellationToken ct = default)
    {
        var akteur = await zugriff.BetreuerAsync(akteurId, ct);
        var adresse = an?.Trim() ?? string.Empty;
        if (!MailAusgangService.IstAdresse(adresse) || adresse.Length > 256) throw new FachFehler("Indiquez une adresse e-mail valide.", "destinataire");
        var sujet = Organisation.Eingabe.Pflicht(betreff, 250, "l'objet", "objet");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (schuleId is { } s)
        {
            if (!await db.Schulen.AnyAsync(x => x.Id == s, ct)) throw new FachFehler("Établissement introuvable.", "ecole");
            if (!akteur.IstAdmin) await PlattformRechte.PruefeBetreuteSchuleAsync(db, akteur, s, ct);
        }
        if (!akteur.IstAdmin)
        {
            if (schuleId is null) throw new FachFehler("Choisissez une école pour envoyer ce message.", "ecole");
            var normal = adresse.ToUpperInvariant();
            if (!await db.Users.AnyAsync(k => k.SchuleId == schuleId && k.NormalizedEmail == normal && k.Status == KontoStatus.Actif, ct))
                throw new FachFehler("Le destinataire ne fait pas partie de cette école.", "destinataire");
        }
        return await SendenAsync(db, akteur, adresse, sujet, text, schuleId, null, ct);
    }

    private async Task<Guid> SendenAsync(SkoulouDbContext db, Akteur akteur, string an, string betreff, string? text, Guid? schuleId, Guid? antwortAuf, CancellationToken ct)
    {
        var inhalt = text?.Trim() ?? string.Empty;
        if (inhalt.Length < 2) throw new FachFehler("Écrivez votre message.", "message");
        if (inhalt.Length > 10_000) throw new FachFehler("Le message est trop long (10 000 caractères au maximum).", "message");
        var html = HtmlBereiniger.TextAlsHtml(inhalt);
        var nachricht = new Nachricht
        {
            Richtung = NachrichtRichtung.Ausgang,
            Von = postmark.CurrentValue.FromEmail,
            An = an,
            Betreff = MailAusgangService.MitAppKennung(betreff) is { Length: > 300 } lang ? lang[..300] : MailAusgangService.MitAppKennung(betreff),
            TextHtml = html,
            SchuleId = schuleId,
            AntwortAufId = antwortAuf,
            GesendetVonId = akteur.Id,
            GelesenAmUtc = zeit.GetUtcNow().UtcDateTime
        };
        db.Nachrichten.Add(nachricht);
        await db.SaveChangesAsync(ct);
        await mails.NachrichtAsync(an, betreff, html, ct);
        return nachricht.Id;
    }

    /// <summary>Eingehende Nachricht einer Schule zuordnen (nur SuperAdmin) – danach sieht sie der Responsable der Schule.</summary>
    public async Task ZuordnenAsync(Guid akteurId, Guid nachrichtId, Guid? schuleId, CancellationToken ct = default)
    {
        var akteur = await zugriff.AdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var n = await db.Nachrichten.FirstOrDefaultAsync(x => x.Id == nachrichtId, ct) ?? throw new FachFehler("Message introuvable.");
        string? name = null;
        if (schuleId is { } s) name = await db.Schulen.Where(x => x.Id == s).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? throw new FachFehler("Établissement introuvable.");
        n.SchuleId = schuleId;
        audit.Erfasse(db, akteur.Id, schuleId, PlattformAudit.NachrichtZugeordnet, nameof(Nachricht), n.Id,
            name is null ? $"Message « {n.Betreff} » sans établissement." : $"Message « {n.Betreff} » attribué à « {name} ».");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Schulen für Filter/Zuordnung (Admin alle, Responsable seine).</summary>
    public async Task<IReadOnlyList<SchulTreffer>> SchulenAsync(Guid akteurId, CancellationToken ct = default)
    {
        var akteur = await zugriff.BetreuerAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await PlattformRechte.SchulenVon(db, akteur).AsNoTracking().Where(s => s.Status != SchulStatus.Refusee)
            .OrderBy(s => s.Name).Select(s => new SchulTreffer(s.Id, s.Name, s.Ort)).ToListAsync(ct);
    }

    /// <summary>Empfänger je Schule: aktive Direktion mit E-Mail (Responsable nur seine Schulen).</summary>
    public async Task<IReadOnlyList<NachrichtEmpfaenger>> EmpfaengerAsync(Guid akteurId, Guid schuleId, CancellationToken ct = default)
    {
        var akteur = await zugriff.BetreuerAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await PlattformRechte.PruefeBetreuteSchuleAsync(db, akteur, schuleId, ct);
        return await db.Users.AsNoTracking()
            .Where(k => k.SchuleId == schuleId && k.Status == KontoStatus.Actif && k.Email != null && (k.Rolle == Rollen.Direktor || k.Rolle == Rollen.Koordinator))
            .OrderBy(k => k.Rolle).ThenBy(k => k.Nachname)
            .Select(k => new NachrichtEmpfaenger(k.Email!, k.Vorname + " " + k.Nachname, k.Rolle)).ToListAsync(ct);
    }
}
