using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Mails;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Angezeigte Annonce (Banner).</summary>
public sealed record AnnonceAnzeige(Guid Id, string Titel, string InhaltHtml, AnkuendigungsStufe Stufe, DateTime EndeUtc);

/// <summary>Zeile der Verwaltung.</summary>
public sealed record AnnonceZeile(Guid Id, string Titel, AnkuendigungsStufe Stufe, ZielRollen Zielrollen, Guid? SchuleId, string? Schule,
    DateTime StartUtc, DateTime EndeUtc);

/// <summary>Eingabe einer Annonce (Text ohne HTML; Zeilenumbrüche bleiben).</summary>
public sealed record AnnonceEingabe(string? Titel, string? Text, AnkuendigungsStufe Stufe, ZielRollen Zielrollen, Guid? SchuleId,
    DateTime StartUtc, DateTime EndeUtc);

/// <summary>
/// Annonces (Entscheidung 21): Zielrollen inkl. « Visiteurs » (öffentliche Seiten), optional eine Schule,
/// Start/Ende, Stufe Info/Important. Die aktiven Annonces liegen eine Minute im Cache; Speichern/Löschen
/// leert ihn sofort (kein Abfragen pro Circuit alle 30 s wie in der Alt-App).
/// </summary>
public sealed class AnkuendigungService(
    IDbContextFactory<SkoulouDbContext> dbFactory,
    Zugriff zugriff,
    IMemoryCache cache,
    TimeProvider zeit)
{
    private const string CacheSchluessel = "plattform:annonces";
    public const string FilterAktiv = "actives";
    public const string FilterGeplant = "planifiees";
    public const string FilterAbgelaufen = "expirees";

    /// <summary>Zielrolle eines Kontos (anonym = Visiteurs).</summary>
    public static ZielRollen ZielFuer(string? rolle) => rolle switch
    {
        Rollen.SuperAdmin => ZielRollen.SuperAdmin,
        Rollen.Responsable => ZielRollen.Responsable,
        Rollen.Direktor => ZielRollen.Direktor,
        Rollen.Koordinator => ZielRollen.Koordinator,
        Rollen.Lehrer => ZielRollen.Lehrer,
        Rollen.Schueler => ZielRollen.Schueler,
        Rollen.Eltern => ZielRollen.Eltern,
        _ => ZielRollen.Visiteurs
    };

    /// <summary>Bezeichnungen der Zielgruppen (für Formular und Liste).</summary>
    public static readonly IReadOnlyList<(ZielRollen Wert, string Text)> Zielgruppen =
    [
        (ZielRollen.Visiteurs, "Visiteurs (pages publiques)"), (ZielRollen.Direktor, "Directeurs"), (ZielRollen.Koordinator, "Coordinateurs"),
        (ZielRollen.Lehrer, "Enseignants"), (ZielRollen.Schueler, "Élèves"), (ZielRollen.Eltern, "Parents"),
        (ZielRollen.Responsable, "Responsables"), (ZielRollen.SuperAdmin, "Administrateurs")
    ];

    public static string ZielText(ZielRollen ziel)
        => (ziel & ZielRollen.Alle) == ZielRollen.Alle ? "Tout le monde"
            : string.Join(", ", Zielgruppen.Where(z => ziel.HasFlag(z.Wert)).Select(z => z.Text.Split(' ')[0]));

    /// <summary>Aktive Annonces für eine Rolle (null = Besucher) und Schule – aus dem Cache.</summary>
    public async Task<IReadOnlyList<AnnonceAnzeige>> AktiveAsync(string? rolle, Guid? schuleId, CancellationToken ct = default)
    {
        var alle = await cache.GetOrCreateAsync(CacheSchluessel, async eintrag =>
        {
            eintrag.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            var jetzt = zeit.GetUtcNow().UtcDateTime;
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            // Auch bald startende laden, damit der Cache-Takt den Start nicht verzögert.
            return await db.Ankuendigungen.AsNoTracking().Where(a => a.EndeUtc > jetzt && a.StartUtc <= jetzt.AddMinutes(1))
                .OrderByDescending(a => a.Stufe).ThenByDescending(a => a.StartUtc)
                .Select(a => new { a.Id, a.Titel, a.InhaltHtml, a.Stufe, a.Zielrollen, a.SchuleId, a.StartUtc, a.EndeUtc })
                .ToListAsync(ct);
        }) ?? [];
        var ziel = ZielFuer(rolle);
        var nun = zeit.GetUtcNow().UtcDateTime;
        return alle.Where(a => a.StartUtc <= nun && a.EndeUtc > nun && (a.Zielrollen & ziel) != 0
                               && (a.SchuleId is null || (schuleId is not null && a.SchuleId == schuleId)))
            .Select(a => new AnnonceAnzeige(a.Id, a.Titel, a.InhaltHtml, a.Stufe, a.EndeUtc)).ToList();
    }

    public async Task<(IReadOnlyList<AnnonceZeile> Eintraege, bool Mehr)> SucheAsync(Guid akteurId, string? suche, string? filter,
        int ueberspringen = 0, int anzahl = 50, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(akteurId, ct);
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Ankuendigungen.AsNoTracking();
        q = filter switch
        {
            FilterAktiv => q.Where(a => a.StartUtc <= jetzt && a.EndeUtc > jetzt),
            FilterGeplant => q.Where(a => a.StartUtc > jetzt),
            FilterAbgelaufen => q.Where(a => a.EndeUtc <= jetzt),
            _ => q
        };
        if (PlattformListen.Suchtext(suche) is { } t) q = q.Where(a => a.Titel.Contains(t));
        var abfrage = q.OrderByDescending(a => a.StartUtc).Select(a => new AnnonceZeile(a.Id, a.Titel, a.Stufe, a.Zielrollen, a.SchuleId,
            db.Schulen.Where(s => s.Id == a.SchuleId).Select(s => s.Name).FirstOrDefault(), a.StartUtc, a.EndeUtc));
        return await PlattformListen.SeiteAsync(abfrage, ueberspringen, anzahl, ct);
    }

    /// <summary>Annonce zum Bearbeiten (Text ohne HTML).</summary>
    public async Task<AnnonceEingabe> LadenAsync(Guid akteurId, Guid id, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var a = await db.Ankuendigungen.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new FachFehler("Annonce introuvable.");
        return new AnnonceEingabe(a.Titel, MailLayout.AlsText(a.InhaltHtml), a.Stufe, a.Zielrollen, a.SchuleId, a.StartUtc, a.EndeUtc);
    }

    /// <summary>Neue Annonce (id null) oder Änderung; leert den Cache.</summary>
    public async Task<Guid> SpeichernAsync(Guid akteurId, Guid? id, AnnonceEingabe e, CancellationToken ct = default)
    {
        var akteur = await zugriff.AdminAsync(akteurId, ct);
        var titel = Eingabe.Pflicht(e.Titel, 200, "le titre", "titre");
        var text = e.Text?.Trim() ?? string.Empty;
        if (text.Length < 5) throw new FachFehler("Le message doit contenir au moins 5 caractères.", "texte");
        if (text.Length > 2000) throw new FachFehler("Le message est trop long (2 000 caractères au maximum).", "texte");
        var ziel = e.Zielrollen & ZielRollen.Alle;
        if (ziel == ZielRollen.Keine) throw new FachFehler("Choisissez au moins un public.", "public");
        if (e.EndeUtc <= e.StartUtc) throw new FachFehler("La fin doit être après le début.", "fin");
        if (id is null && e.EndeUtc <= zeit.GetUtcNow().UtcDateTime) throw new FachFehler("La date de fin doit être dans le futur.", "fin");
        if (e.SchuleId is not null && (ziel & ZielRollen.Visiteurs) != 0)
            throw new FachFehler("Une annonce limitée à un établissement ne peut pas viser les visiteurs.", "public");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (e.SchuleId is { } s && !await db.Schulen.AnyAsync(x => x.Id == s, ct)) throw new FachFehler("Établissement introuvable.", "ecole");
        var a = id is { } vorhanden
            ? await db.Ankuendigungen.FirstOrDefaultAsync(x => x.Id == vorhanden, ct) ?? throw new FachFehler("Annonce introuvable.")
            : db.Ankuendigungen.Add(new Ankuendigung { ErstelltVonId = akteur.Id }).Entity;
        a.Titel = titel;
        a.InhaltHtml = HtmlBereiniger.TextAlsHtml(text);
        a.Stufe = e.Stufe;
        a.Zielrollen = ziel;
        a.SchuleId = e.SchuleId;
        a.StartUtc = e.StartUtc;
        a.EndeUtc = e.EndeUtc;
        await db.SaveChangesAsync(ct);
        cache.Remove(CacheSchluessel);
        return a.Id;
    }

    public async Task LoeschenAsync(Guid akteurId, Guid id, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var a = await db.Ankuendigungen.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new FachFehler("Annonce introuvable.");
        db.Ankuendigungen.Remove(a);
        await db.SaveChangesAsync(ct);
        cache.Remove(CacheSchluessel);
    }
}
