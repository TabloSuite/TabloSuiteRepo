using System.Text;
using Microsoft.Extensions.Options;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.KiHilfe;

/// <summary>Ergebnis einer Frage: Antwort (Erfolg) oder freundliche Meldung; dazu passende Lernvideos und das Restkontingent.</summary>
public sealed record KiErgebnis(bool Erfolg, string? Antwort, string? Meldung, IReadOnlyList<VideoKatalog.LernVideo> Videos, int Verbleibend)
{
    public IReadOnlyList<KiBlock> Bloecke => KiAntwortFormat.Bloecke(Antwort);
}

/// <summary>
/// KI-Hilfe « Poser une question » (Nutzerwunsch 19.11.2026): beantwortet nur Fragen zur Bedienung aus der Wissensbasis
/// der Rolle. <b>Datenschutz:</b> an Anthropic gehen nur Hilfetexte, die Rolle und die Frage – keine Namen, keine Daten der
/// Schule; die Frage wird nirgends gespeichert und nie protokolliert (im Log nur Zähler und Token-Verbrauch).
/// Grenzen: <see cref="KiNutzungsZaehler"/>. Fehler des Dienstes → Meldung, die App läuft weiter.
/// </summary>
public sealed class KiHilfeDienst(IKiClient client, KiNutzungsZaehler zaehler, IOptions<KiHilfeOptionen> optionen, ILogger<KiHilfeDienst> log)
{
    public const string MeldungIndisponible = "Le service d'aide est momentanément indisponible. Réessayez dans quelques minutes, ou consultez les vidéos d'aide.";
    public const string MeldungKontoGrenze = "Vous avez posé 20 questions aujourd'hui : c'est la limite du jour. Réessayez demain, ou consultez les vidéos d'aide et le guide.";
    public const string MeldungAppGrenze = "Le nombre de questions prévu pour aujourd'hui est atteint. Réessayez demain, ou consultez les vidéos d'aide et le guide.";
    public const string MeldungAbgelehnt = "Je ne peux pas répondre à cette question. Je réponds seulement aux questions sur l'utilisation de SKOULOU : reformulez-la, ou consultez les vidéos d'aide et la page Contact.";
    public const string MeldungLeer = "Écrivez d'abord votre question.";
    public const string MeldungZuLang = "Votre question est trop longue : 500 caractères au plus.";
    public const string MeldungOhneText = "Je n'ai pas pu formuler de réponse courte. Posez une question plus précise, par exemple sur une seule fonction.";

    /// <summary>Nur mit Schlüssel sichtbar (Menüeintrag, Seite).</summary>
    public bool IstVerfuegbar => optionen.Value.IstBereit;

    public int Verbleibend(Guid kontoId) => zaehler.Verbleibend(Konto(kontoId));

    public async Task<KiErgebnis> FrageAsync(Guid kontoId, KiRolle rolle, string? frage, CancellationToken ct = default)
    {
        var konto = Konto(kontoId);
        if (!IstVerfuegbar) return Meldung(MeldungIndisponible, konto);
        var text = Normalisiere(frage);
        if (text.Length == 0) return Meldung(MeldungLeer, konto);
        if (text.Length > KiHilfeOptionen.MaxFrageZeichen) return Meldung(MeldungZuLang, konto);

        switch (zaehler.Reservieren(konto))
        {
            case KiKontingent.KontoGrenze:
                log.LogInformation("KI-Hilfe: Tagesgrenze eines Kontos erreicht (Rolle {Rolle}).", rolle.Rolle);
                return Meldung(MeldungKontoGrenze, konto);
            case KiKontingent.AppGrenze:
                log.LogWarning("KI-Hilfe: Tagesgrenze der App erreicht ({Grenze} Fragen).", optionen.Value.FragenJeTag);
                return Meldung(MeldungAppGrenze, konto);
        }

        KiRohAntwort roh;
        try
        {
            roh = await client.FrageAsync(new KiAnfrage(KiWissensbasis.SystemText(rolle), NutzerText(rolle, text)), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            zaehler.Freigeben(konto);
            throw;
        }
        catch (KiDienstFehler f)
        {
            zaehler.Freigeben(konto);
            // Nur Art und Typ – nie Frage, Antwort oder Schlüssel.
            if (f.Art == KiFehlerArt.Konfiguration)
                log.LogError("KI-Hilfe: Anfrage abgewiesen ({Art}: {Beschreibung}) – Ki:AnthropicApiKey und Ki:Modell prüfen.", f.Art, f.Message);
            else
                log.LogWarning("KI-Hilfe nicht erreichbar ({Art}: {Beschreibung}).", f.Art, f.Message);
            return Meldung(MeldungIndisponible, konto);
        }
        catch (Exception e)
        {
            zaehler.Freigeben(konto);
            log.LogError("KI-Hilfe: unerwarteter Fehler ({Typ}).", e.GetType().Name);
            return Meldung(MeldungIndisponible, konto);
        }

        log.LogInformation(
            "KI-Hilfe: Antwort (Rolle {Rolle}, {Stopp}); Tokens Eingabe {Eingabe}, Cache gelesen {CacheGelesen}, Cache geschrieben {CacheGeschrieben}, Ausgabe {Ausgabe}; heute {Heute}/{Grenze} Fragen.",
            rolle.Rolle, roh.Stopp, roh.EingabeTokens, roh.CacheGelesen, roh.CacheGeschrieben, roh.AusgabeTokens, zaehler.HeuteGesamt, optionen.Value.FragenJeTag);

        if (roh.Stopp == KiStopp.Abgelehnt) return Meldung(MeldungAbgelehnt, konto);
        if (string.IsNullOrWhiteSpace(roh.Text)) return Meldung(MeldungOhneText, konto);

        var videos = PassendeVideos(roh.Text, KiWissensbasis.Videos(rolle));
        var antwort = OhneVideoZeile(roh.Text, videos);
        if (roh.Stopp == KiStopp.Abgeschnitten) antwort += " …";
        return new KiErgebnis(true, antwort, null, videos, zaehler.Verbleibend(konto));
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

    private KiErgebnis Meldung(string meldung, string konto) => new(false, null, meldung, [], zaehler.Verbleibend(konto));

    private static string Konto(Guid kontoId) => kontoId.ToString("N");
}
