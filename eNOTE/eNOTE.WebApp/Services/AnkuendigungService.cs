using System.Security.Claims;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Infrastruktur;
using ENote.WebApp.Data;
using ENote.WebApp.Services.Mails;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Ankündigungen der Plattform (App-Update, Wartung, Tarif …): der SuperAdmin veröffentlicht sie,
/// Nutzer der Zielgruppe sehen sie als Banner oben in der App, bis sie sie schließen.
/// Optional geht dieselbe Nachricht zusätzlich per E-Mail an die Besitzer.
/// </summary>
public class AnkuendigungService(IDbContextFactory<ApplicationDbContext> factory, TimeProvider zeit,
    NachrichtenService? nachrichten = null, MailBenachrichtigungen? mails = null)
{
    public sealed record Eintrag(Guid Id, AnkuendigungArt Art, AnkuendigungZiel Ziel, string Titel, string Text,
        string? LinkUrl, string? LinkText, DateTime ErstelltUtc, DateTime SichtbarAbUtc, DateTime? SichtbarBisUtc,
        bool IstAktiv, int Gelesen, int MailsGesendet, DateTime? WartungVonUtc = null, DateTime? WartungBisUtc = null);

    public sealed record Banner(Guid Id, AnkuendigungArt Art, string Titel, string Text, string? LinkUrl, string? LinkText,
        DateTime ErstelltUtc, DateTime? WartungVonUtc = null, DateTime? WartungBisUtc = null)
    {
        /// <summary>Die Aktualisierung läuft gerade (Banner deutlicher, Titel „en cours“).</summary>
        public bool WartungLaeuft(DateTime jetzt) => WartungVonUtc <= jetzt && jetzt < WartungBisUtc;
    }

    /// <summary>
    /// Entwurf einer Ankündigung. Bei <see cref="AnkuendigungArt.Maintenance"/> ist das Zeitfenster Pflicht;
    /// <paramref name="HinweisDanach"/> zeigt nach dem Fenster kurz „eNOTE a été mis à jour“.
    /// </summary>
    public sealed record Entwurf(AnkuendigungArt Art, AnkuendigungZiel Ziel, string Titel, string Text,
        string? LinkUrl, string? LinkText, DateTime? SichtbarBisUtc, bool PerMail,
        DateTime? WartungVonUtc = null, DateTime? WartungBisUtc = null, bool HinweisDanach = false);

    /// <summary>Fertige Texte für eine geplante Aktualisierung; Platzhalter füllt <see cref="FuelleWartung"/>.</summary>
    public sealed record TextVorlage(string Name, string Titel, string Text);

    public static readonly IReadOnlyList<(string Platzhalter, string Bedeutung)> WartungsPlatzhalter =
    [
        ("{jour}", "jour, ex. mardi 12 novembre"), ("{debut}", "heure de début"), ("{fin}", "heure de fin"),
        ("{duree}", "durée, ex. 30 minutes")
    ];

    public static readonly IReadOnlyList<TextVorlage> WartungsVorlagen =
    [
        new("Courte", "Mise à jour d'eNOTE le {jour}, de {debut} à {fin}",
            "eNOTE sera brièvement indisponible pendant cette période (heure de Lomé). Pensez à terminer vos ventes en cours avant {debut}. Vos données et vos caisses ouvertes restent conservées."),
        new("Avec conseil", "Maintenance prévue le {jour}",
            "Nous améliorons eNOTE le {jour} entre {debut} et {fin} (heure de Lomé, environ {duree}). Pendant ce temps, l'application peut être indisponible.\n\nConseil : terminez les ventes en cours avant {debut} et rechargez la page après {fin}. Aucune donnée ne sera perdue."),
        new("Nouveautés", "Nouvelle version d'eNOTE le {jour}",
            "Une nouvelle version d'eNOTE arrive le {jour} entre {debut} et {fin} (heure de Lomé). L'application sera indisponible quelques minutes.\n\nAprès la mise à jour, rechargez la page pour profiter des nouveautés."),
    ];

    public const string TitelDanach = "eNOTE a été mis à jour";
    public const string TextDanach = "La mise à jour est terminée, merci de votre patience. Si l'affichage semble incomplet, rechargez la page.";

    private static readonly System.Globalization.CultureInfo Fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Uhrzeit wie in Westafrika üblich: „22 h 00“.</summary>
    public static string Uhrzeit(DateTime utc) => utc.ToString("HH' h 'mm", Fr);

    public static string DauerText(TimeSpan dauer)
    {
        var minuten = (int)Math.Round(dauer.TotalMinutes);
        if (minuten < 60) return $"{minuten} minutes";
        var stunden = minuten / 60; var rest = minuten % 60;
        return rest == 0 ? (stunden == 1 ? "1 heure" : $"{stunden} heures") : $"{stunden} h {rest:00}";
    }

    /// <summary>Ersetzt {jour}, {debut}, {fin}, {duree} (UTC = heure de Lomé).</summary>
    public static string FuelleWartung(string vorlage, DateTime vonUtc, DateTime bisUtc)
        => vorlage
            .Replace("{jour}", vonUtc.ToString("dddd d MMMM", Fr))
            .Replace("{debut}", Uhrzeit(vonUtc)).Replace("{fin}", Uhrzeit(bisUtc))
            .Replace("{duree}", DauerText(bisUtc - vonUtc))
            .Trim();

    public const int TitelMax = 150, TextMax = 2000;

    public static string ZielText(AnkuendigungZiel ziel) => ziel switch
    {
        AnkuendigungZiel.Proprietaires => "Propriétaires",
        AnkuendigungZiel.ProprietairesEtCaissiers => "Propriétaires et employés",
        _ => "Tous les utilisateurs"
    };

    public static string ArtText(AnkuendigungArt art) => art switch
    {
        AnkuendigungArt.Nouveaute => "Nouveauté",
        AnkuendigungArt.Maintenance => "Mise à jour planifiée",
        AnkuendigungArt.Tarif => "Tarif",
        _ => "Information"
    };

    /// <summary>CSS-Zusatz (Farbe) je Art.</summary>
    public static string Klasse(AnkuendigungArt art) => art switch
    {
        AnkuendigungArt.Nouveaute => "neu",
        AnkuendigungArt.Maintenance => "wartung",
        AnkuendigungArt.Tarif => "tarif",
        _ => "info"
    };

    /// <summary>Symbol je Art.</summary>
    public static string Symbol(AnkuendigungArt art) => art switch
    {
        AnkuendigungArt.Nouveaute => "sparkles",
        AnkuendigungArt.Maintenance => "settings",
        AnkuendigungArt.Tarif => "tag",
        _ => "info"
    };

    /// <summary>Gilt die Zielgruppe für einen Nutzer mit diesen Rollen?</summary>
    public static bool Passt(AnkuendigungZiel ziel, ClaimsPrincipal user) => ziel switch
    {
        AnkuendigungZiel.Proprietaires => user.IsInRole(AppRollen.Besitzer),
        AnkuendigungZiel.ProprietairesEtCaissiers => user.IsInRole(AppRollen.Besitzer) || user.IsInRole(AppRollen.Kassierer),
        _ => true
    };

    private static IEnumerable<AnkuendigungZiel> Ziele(ClaimsPrincipal user)
        => Enum.GetValues<AnkuendigungZiel>().Where(z => Passt(z, user));

    /// <summary>Ungelesene, sichtbare Ankündigungen eines Nutzers (neueste zuerst).</summary>
    public async Task<List<Banner>> GetFuerNutzerAsync(Guid userId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var ziele = Ziele(user).ToList();
        if (ziele.Count == 0) return [];
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Ankuendigungen.AsNoTracking()
            .Where(a => a.IstAktiv && a.SichtbarAbUtc <= jetzt && (a.SichtbarBisUtc == null || a.SichtbarBisUtc > jetzt)
                && ziele.Contains(a.Ziel)
                && !db.AnkuendigungenGelesen.Any(g => g.AnkuendigungId == a.Id && g.UserId == userId))
            .OrderByDescending(a => a.SichtbarAbUtc)
            .Take(10)
            .Select(a => new Banner(a.Id, a.Art, a.Titel, a.Text, a.LinkUrl, a.LinkText, a.SichtbarAbUtc, a.WartungVonUtc, a.WartungBisUtc))
            .ToListAsync(ct);
    }

    /// <summary>Nutzer schließt das Banner – es erscheint für ihn nicht mehr.</summary>
    public async Task MarkiereGelesenAsync(Guid userId, Guid ankuendigungId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.AnkuendigungenGelesen.AnyAsync(g => g.AnkuendigungId == ankuendigungId && g.UserId == userId, ct)) return;
        if (!await db.Ankuendigungen.AnyAsync(a => a.Id == ankuendigungId, ct)) return;
        db.AnkuendigungenGelesen.Add(new AnkuendigungGelesen { AnkuendigungId = ankuendigungId, UserId = userId, GelesenUtc = zeit.GetUtcNow().UtcDateTime });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { /* doppelt geschlossen (zweiter Tab) – bereits gespeichert */ }
    }

    // ------------------------------------------------------------------ Administration

    public async Task<List<Eintrag>> GetAlleAsync(Guid bearbeiter, CancellationToken ct = default)
        => await GetSeiteAsync(bearbeiter, 0, 50, ct);

    /// <summary>Annonces seitenweise (neueste zuerst) – nie alle auf einmal.</summary>
    public async Task<List<Eintrag>> GetSeiteAsync(Guid bearbeiter, int ueberspringen, int anzahl, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await AdminKontenService.PruefeAsync(db, bearbeiter, false, ct);
        return await db.Ankuendigungen.AsNoTracking()
            .OrderByDescending(a => a.ErstelltUtc).ThenByDescending(a => a.SichtbarAbUtc)
            .Skip(Math.Max(0, ueberspringen)).Take(anzahl)
            .Select(a => new Eintrag(a.Id, a.Art, a.Ziel, a.Titel, a.Text, a.LinkUrl, a.LinkText, a.ErstelltUtc,
                a.SichtbarAbUtc, a.SichtbarBisUtc, a.IstAktiv,
                db.AnkuendigungenGelesen.Count(g => g.AnkuendigungId == a.Id), a.MailsGesendet, a.WartungVonUtc, a.WartungBisUtc))
            .ToListAsync(ct);
    }

    private static string? PruefeLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        if (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal)) return url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) return url;
        throw new ArgumentException("Le lien doit commencer par « / » (page de l'app) ou par https://");
    }

    /// <summary>Veröffentlicht eine Ankündigung (sofort sichtbar) und verschickt sie auf Wunsch per E-Mail an die Besitzer.</summary>
    public async Task<(Guid Id, int Mails)> VeroeffentlicheAsync(Guid bearbeiter, Entwurf e, CancellationToken ct = default)
    {
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        DateTime? von = null, bisWartung = null;
        if (e.Art == AnkuendigungArt.Maintenance)
        {
            if (e.WartungVonUtc is not DateTime v || e.WartungBisUtc is not DateTime b)
                throw new ArgumentException("Indiquez le jour, l'heure de début et l'heure de fin de la mise à jour.");
            von = DateTime.SpecifyKind(v, DateTimeKind.Utc);
            bisWartung = DateTime.SpecifyKind(b, DateTimeKind.Utc);
            if (bisWartung <= von) throw new ArgumentException("L'heure de fin doit être après l'heure de début.");
            if (bisWartung <= jetzt) throw new ArgumentException("Cette période est déjà passée.");
            if (bisWartung - von > TimeSpan.FromHours(24)) throw new ArgumentException("Une mise à jour dure au maximum 24 heures.");
        }
        var titel = von is DateTime v1 ? FuelleWartung(e.Titel ?? "", v1, bisWartung!.Value) : e.Titel?.Trim() ?? "";
        var text = von is DateTime v2 ? FuelleWartung(e.Text ?? "", v2, bisWartung!.Value) : e.Text?.Trim() ?? "";
        if (titel.Length == 0 || text.Length == 0) throw new ArgumentException("Saisissez un titre et un message.");
        if (titel.Length > TitelMax) throw new ArgumentException($"Titre : {TitelMax} caractères au maximum.");
        if (text.Length > TextMax) throw new ArgumentException($"Message : {TextMax} caractères au maximum.");
        var link = PruefeLink(e.LinkUrl);
        if (e.SichtbarBisUtc is DateTime bis && bis <= jetzt) throw new ArgumentException("La date de fin doit être dans le futur.");

        var ankuendigung = new Ankuendigung
        {
            Art = e.Art, Ziel = e.Ziel, Titel = titel, Text = text, LinkUrl = link,
            LinkText = link is null ? null : (string.IsNullOrWhiteSpace(e.LinkText) ? "En savoir plus" : e.LinkText.Trim()),
            ErstelltUtc = jetzt, SichtbarAbUtc = jetzt, ErstelltVonId = bearbeiter,
            // Eine geplante Aktualisierung verschwindet von selbst, sobald das Zeitfenster vorbei ist.
            SichtbarBisUtc = bisWartung ?? e.SichtbarBisUtc,
            WartungVonUtc = von, WartungBisUtc = bisWartung
        };
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            await AdminKontenService.PruefeAsync(db, bearbeiter, false, ct);
            db.Ankuendigungen.Add(ankuendigung);
            if (bisWartung is DateTime ende && e.HinweisDanach)
            {
                // Kurzer Hinweis nach dem Fenster (3 Tage sichtbar).
                db.Ankuendigungen.Add(new Ankuendigung
                {
                    Art = AnkuendigungArt.Nouveaute, Ziel = e.Ziel, Titel = TitelDanach, Text = TextDanach,
                    ErstelltUtc = jetzt, SichtbarAbUtc = ende, SichtbarBisUtc = ende.AddDays(3), ErstelltVonId = bearbeiter
                });
            }
            await db.SaveChangesAsync(ct);
        }

        var anzahl = 0;
        if (e.PerMail && nachrichten is not null)
        {
            var empfaenger = await nachrichten.ErmittleAsync(bearbeiter, NachrichtenService.Gruppe.AlleBesitzer, [], [], ct);
            var appUrl = mails?.AppUrl ?? BonQr.AppSeite;
            var knopfUrl = link is null ? appUrl : link.StartsWith('/') ? appUrl + link : link;
            if (empfaenger.Count > 0)
            {
                var (ok, _) = await nachrichten.SendeAsync(bearbeiter, empfaenger.Take(NachrichtenService.MaxEmpfaenger).ToList(),
                    titel, text, ankuendigung.LinkText ?? "Ouvrir eNOTE", knopfUrl, ct, kategorie: "annonce");
                anzahl = ok;
            }
            await using var db = await factory.CreateDbContextAsync(ct);
            var gespeichert = await db.Ankuendigungen.FirstAsync(a => a.Id == ankuendigung.Id, ct);
            gespeichert.MailsGesendet = anzahl;
            await db.SaveChangesAsync(ct);
        }
        return (ankuendigung.Id, anzahl);
    }

    /// <summary>Interne Ankündigung ohne Rechteprüfung (z. B. von der Preisanpassung erzeugt).</summary>
    internal static Ankuendigung Erzeuge(ApplicationDbContext db, AnkuendigungArt art, AnkuendigungZiel ziel, string titel, string text,
        string? linkUrl, string? linkText, DateTime jetzt, DateTime? bis, Guid? von)
    {
        var a = new Ankuendigung
        {
            Art = art, Ziel = ziel, Titel = titel, Text = text, LinkUrl = linkUrl, LinkText = linkText,
            ErstelltUtc = jetzt, SichtbarAbUtc = jetzt, SichtbarBisUtc = bis, ErstelltVonId = von
        };
        db.Ankuendigungen.Add(a);
        return a;
    }

    public async Task SetzeAktivAsync(Guid bearbeiter, Guid id, bool aktiv, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await AdminKontenService.PruefeAsync(db, bearbeiter, false, ct);
        var a = await db.Ankuendigungen.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new ArgumentException("Annonce introuvable.");
        a.IstAktiv = aktiv;
        await db.SaveChangesAsync(ct);
    }

    public async Task LoescheAsync(Guid bearbeiter, Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await AdminKontenService.PruefeAsync(db, bearbeiter, false, ct);
        db.AnkuendigungenGelesen.RemoveRange(await db.AnkuendigungenGelesen.Where(g => g.AnkuendigungId == id).ToListAsync(ct));
        db.Ankuendigungen.RemoveRange(await db.Ankuendigungen.Where(a => a.Id == id).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
    }
}
