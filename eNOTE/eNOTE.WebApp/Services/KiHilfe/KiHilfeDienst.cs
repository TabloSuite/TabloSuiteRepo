using System.Text;
using Microsoft.Extensions.Options;
using ENote.Core.DataModels.Identity;
using ENote.WebApp.Services;

namespace ENote.WebApp.Services.KiHilfe;

/// <summary>Ergebnis einer Frage: Antwort (Erfolg) oder freundliche Meldung; dazu passende Lernvideos.</summary>
public sealed record KiErgebnis(bool Erfolg, string? Antwort, string? Meldung, IReadOnlyList<VideoKatalog.LernVideo> Videos)
{
    public IReadOnlyList<KiBlock> Bloecke => KiAntwortFormat.Bloecke(Antwort);
}

/// <summary>
/// KI-Hilfe « Poser une question » (Nutzerwunsch 19.11.2026): beantwortet nur Fragen zur Bedienung aus der Wissensbasis
/// der Rolle. <b>Datenschutz:</b> an Anthropic gehen nur Hilfetexte, die Rolle und die Frage – keine Namen, keine Daten des
/// Ladens (Kunden, Verkäufe, Konten); die Frage wird nirgends gespeichert und nie protokolliert (im Log nur Rolle und Token-Verbrauch).
/// Keine Fragen-Grenzen; zunächst nur für Administrateurs und Responsables (<see cref="IstVerfuegbarFuer"/>).
/// Fehler des Dienstes → Meldung, die App läuft weiter.
/// </summary>
public sealed class KiHilfeDienst(IKiClient client, IOptions<KiHilfeOptionen> optionen, ILogger<KiHilfeDienst> log,
    PlattformEinstellungen? einstellungen = null)
{
    public const string MeldungIndisponible = "Le service d'aide est momentanément indisponible. Réessayez dans quelques minutes, ou consultez les vidéos d'aide.";
    public const string MeldungAbgelehnt = "Je ne peux pas répondre à cette question. Je réponds seulement aux questions sur l'utilisation d'eNOTE : reformulez-la, ou consultez les vidéos d'aide et la page « Aide & contact ».";
    public const string MeldungLeer = "Écrivez d'abord votre question.";
    public const string MeldungZuLang = "Votre question est trop longue : 500 caractères au plus.";
    public const string MeldungOhneText = "Je n'ai pas pu formuler de réponse courte. Posez une question plus précise, par exemple sur une seule fonction.";

    /// <summary>Adresse der Seite « Poser une question » (Menü « Aide »).</summary>
    public const string SeitenAdresse = "/aide/question";

    /// <summary>Schlüssel konfiguriert (Voraussetzung für alles Weitere).</summary>
    public bool IstVerfuegbar => optionen.Value.IstBereit;

    /// <summary>Freigabe für alle Rollen schon über die App-Einstellung <c>Ki:AlleRollen</c> (Schalter dann wirkungslos).</summary>
    public bool AlleRollenPerKonfiguration => optionen.Value.AlleRollen;

    private static bool IstPlattformRolle(string rolle) => rolle is AppRollen.SuperAdmin or AppRollen.ProduktOwner;

    /// <summary>
    /// Menüeintrag und Seite für diese Rolle: mit Schlüssel zunächst nur Administrateur und Responsable eNOTE
    /// (Nutzer 19.11.2026); für alle mit <see cref="KiHilfeOptionen.AlleRollen"/> oder dem Schalter in Administration ›
    /// Réglages (<see cref="PlattformEinstellungen"/>, 21.11.2026). Ohne Warten (Menü): Schalterwert aus dem Zwischenspeicher.
    /// </summary>
    public bool IstVerfuegbarFuer(string rolle)
        => IstVerfuegbar && (optionen.Value.AlleRollen || IstPlattformRolle(rolle) || einstellungen?.KiFuerAlleRollen == true);

    /// <summary>Wie <see cref="IstVerfuegbarFuer"/>, mit frisch gelesenem Schalter (Seite, Frage).</summary>
    public async Task<bool> IstVerfuegbarFuerAsync(string rolle, CancellationToken ct = default)
        => IstVerfuegbar && (optionen.Value.AlleRollen || IstPlattformRolle(rolle)
            || (einstellungen is not null && await einstellungen.GetKiFuerAlleRollenAsync(ct)));

    public async Task<KiErgebnis> FrageAsync(KiRolle rolle, string? frage, CancellationToken ct = default)
    {
        if (!await IstVerfuegbarFuerAsync(rolle.Rolle, ct)) return Meldung(MeldungIndisponible);
        var text = Normalisiere(frage);
        if (text.Length == 0) return Meldung(MeldungLeer);
        if (text.Length > KiHilfeOptionen.MaxFrageZeichen) return Meldung(MeldungZuLang);

        KiRohAntwort roh;
        try
        {
            roh = await client.FrageAsync(new KiAnfrage(KiWissensbasis.SystemText(rolle), NutzerText(rolle, text)), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (KiDienstFehler f)
        {
            // Nur Art und Typ – nie Frage, Antwort oder Schlüssel. Konfigurationsfehler mit Ausnahme, damit sie unter
            // Administration › Journaux erscheinen (20.11.2026: ungültiger Schlüssel → 401 stand vorher nirgends).
            if (f.Art == KiFehlerArt.Konfiguration)
                log.LogError(f, "KI-Hilfe: Anfrage abgewiesen ({Art}: {Beschreibung}) – Ki:AnthropicApiKey und Ki:Modell prüfen.", f.Art, f.Message);
            else
                log.LogWarning("KI-Hilfe nicht erreichbar ({Art}: {Beschreibung}).", f.Art, f.Message);
            return Meldung(MeldungIndisponible);
        }
        catch (Exception e)
        {
            log.LogError("KI-Hilfe: unerwarteter Fehler ({Typ}).", e.GetType().Name);
            return Meldung(MeldungIndisponible);
        }

        log.LogInformation(
            "KI-Hilfe: Antwort (Rolle {Rolle}, {Stopp}); Tokens Eingabe {Eingabe}, Cache gelesen {CacheGelesen}, Cache geschrieben {CacheGeschrieben}, Ausgabe {Ausgabe}.",
            rolle.Rolle, roh.Stopp, roh.EingabeTokens, roh.CacheGelesen, roh.CacheGeschrieben, roh.AusgabeTokens);

        if (roh.Stopp == KiStopp.Abgelehnt) return Meldung(MeldungAbgelehnt);
        if (string.IsNullOrWhiteSpace(roh.Text)) return Meldung(MeldungOhneText);

        var videos = PassendeVideos(roh.Text, KiWissensbasis.Videos(rolle));
        var antwort = OhneVideoZeile(roh.Text, videos);
        if (roh.Stopp == KiStopp.Abgeschnitten) antwort += " …";
        return new KiErgebnis(true, antwort, null, videos);
    }

    /// <summary>Kurzer Rollenhinweis und die Frage (in einem Rahmen, damit sie sich nicht als Anweisung ausgibt).</summary>
    internal static string NutzerText(KiRolle rolle, string frage)
        => $"Rôle de l'utilisateur : {rolle.Bezeichnung}\n<question>\n{frage.Replace('<', '‹').Replace('>', '›')}\n</question>";

    /// <summary>Steuerzeichen weg, Leerzeichen zusammenfassen, Zeilenumbrüche behalten.</summary>
    internal static string Normalisiere(string? frage)
    {
        if (string.IsNullOrWhiteSpace(frage)) return string.Empty;
        var sb = new StringBuilder(frage.Length);
        foreach (var c in frage.Replace("\r\n", "\n"))
            if (c == '\n' || !char.IsControl(c)) sb.Append(c);
        var zeilen = sb.ToString().Split('\n').Select(z => string.Join(' ', z.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        return string.Join('\n', zeilen.Where(z => z.Length > 0)).Trim();
    }

    /// <summary>
    /// Lernvideos, deren genauer Titel in Anführungszeichen in der Antwort steht (« titre exact », höchstens drei, Reihenfolge
    /// des Katalogs). Ohne Anführungszeichen zählt ein Titel nicht – kurze Titel kämen sonst in gewöhnlichen Sätzen vor.
    /// </summary>
    internal static IReadOnlyList<VideoKatalog.LernVideo> PassendeVideos(string antwort, IReadOnlyList<VideoKatalog.LernVideo> erlaubt)
    {
        var a = Vergleich(antwort);
        return [.. erlaubt.Where(v => Zitiert(a, Vergleich(v.Titel))).Take(3)];
    }

    private static bool Zitiert(string text, string titel)
        => text.Contains("«" + titel + "»", StringComparison.Ordinal) || text.Contains("\"" + titel + "\"", StringComparison.Ordinal)
           || text.Contains("“" + titel + "”", StringComparison.Ordinal);

    /// <summary>Die Zeile « Vidéo : « … » » entfällt, wenn das Video als Knopf darunter erscheint.</summary>
    private static string OhneVideoZeile(string antwort, IReadOnlyList<VideoKatalog.LernVideo> videos)
    {
        if (videos.Count == 0) return antwort.Trim();
        var titel = videos.Select(v => Vergleich(v.Titel)).ToList();
        var zeilen = antwort.Replace("\r", string.Empty).Split('\n')
            .Where(z => !(z.TrimStart('*', ' ', '-').StartsWith("Vid", StringComparison.OrdinalIgnoreCase)
                          && titel.Any(t => Zitiert(Vergleich(z), t))));
        return string.Join('\n', zeilen).Trim();
    }

    /// <summary>Kleinbuchstaben, ein Apostroph, Leerraum zusammengefasst, Leerzeichen innerhalb von « » entfernt.</summary>
    private static string Vergleich(string s)
        => string.Join(' ', s.ToLowerInvariant().Replace('’', '\'').Replace('\u00a0', ' ').Replace('\u202f', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)).Replace("« ", "«").Replace(" »", "»");

    private static KiErgebnis Meldung(string meldung) => new(false, null, meldung, []);
}
