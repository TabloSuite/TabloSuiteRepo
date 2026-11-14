using Skoulou.Core.Entitaeten;
using Skoulou.Core.Paedagogik;
using Skoulou.WebApp.Services.Paedagogik;

namespace Skoulou.Tests.Simulation;

internal sealed partial class Simulation
{
    /// <summary>Leçons/Examens je Schule und Lehrkraft (Stufe + Fach, überleben Schuljahre).</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(Guid Lehrer, Guid Stufe, Guid Fach, InhaltsArt Art), Guid> inhalte = new();

    private static FrageFormular Wahl(string text, string richtig, params string[] falsch) => new()
    {
        Typ = FragenTyp.ChoixUnique, TextHtml = $"<p>{text}</p>", Punkte = 2,
        Optionen = [new() { Text = richtig, Richtig = true }, .. falsch.Select(f => new OptionFormular { Text = f })]
    };

    private static FrageFormular Text(string text, decimal punkte, params string[] varianten) => new()
    {
        Typ = FragenTyp.Texte, TextHtml = $"<p>{text}</p>", Punkte = punkte, Varianten = varianten.ToList()
    };

    private static FrageFormular Luecke(string text, params string[] loesung) => new()
    {
        Typ = FragenTyp.TexteATrous, TextHtml = text, Punkte = 2, Luecken = [loesung.ToList()]
    };

    /// <summary>Oktober: Leçon mit Übungen (beliebig viele Versuche); Februar: Examen mit offener Frage (manuelle Korrektur).</summary>
    private async Task InhalteAsync(SimSchule s, Zufall z, DateOnly heute)
    {
        var examen = heute.Month == 2;
        var art = examen ? InhaltsArt.Pruefung : InhaltsArt.Lecon;
        foreach (var k in s.Jahr.Klassen.Where(k => k.Session <= 1).Take(2))
        {
            var kf = k.Faecher.FirstOrDefault(f => f.Lehrer != s.Direktor);
            if (kf is null) continue;
            var lehrer = kf.Lehrer;
            var schluessel = (lehrer, k.Stufe.Id, kf.FachId, art);
            if (!inhalte.TryGetValue(schluessel, out var inhaltId))
            {
                var f = new InhaltFormular
                {
                    Art = art, Titel = examen ? $"Composition – {k.Stufe.Name}" : $"Les fractions – {k.Stufe.Name}", StufeId = k.Stufe.Id, FachId = kf.FachId,
                    Status = InhaltsStatus.Prete, DauerMinuten = 30, Bestehensgrenze = 50, Zusammenfassung = "Révision guidée avec exercices corrigés.",
                    Bloecke =
                    [
                        new BlockFormular
                        {
                            Titel = "Exercice 1", Consigne = "Répondez aux questions.",
                            Fragen = examen
                                ? [Wahl("Combien font 3 × 4 ?", "12", "7", "34"), Text("Expliquez en une phrase ce qu'est une fraction.", 4)]
                                : [Wahl("Quelle fraction est égale à 1/2 ?", "2/4", "2/3", "3/4"), Text("Écrivez « un demi » en chiffres.", 2, "1/2", "0,5"),
                                   Luecke("<p>La moitié de 10 est [[1]].</p>", "5", "cinq")]
                        }
                    ]
                };
                if (!examen) f.Seiten.Add(new SeiteFormular { Html = "<h2>Les fractions</h2><p>Une fraction représente une partie d'un tout.</p>" });
                inhaltId = await SollAsync("Inhalt speichern", () => api.Inhalte.SpeichernAsync(lehrer, f));
                if (inhaltId == Guid.Empty) continue;
                inhalte[schluessel] = inhaltId;
                Zaehle(examen ? "Examens" : "Leçons");
            }
            var jetzt = d.Zeit.GetUtcNow().UtcDateTime;
            var zuweisung = await SollAsync("Zuweisen", () => api.Zuweisungen.ZuweisenAsync(lehrer, new ZuweisungFormular
            {
                InhaltId = inhaltId, Art = art, KlasseId = k.Id, AbUtc = jetzt.AddMinutes(-5), FristUtc = examen ? jetzt.AddDays(2) : null,
                Wertung = VersuchsWertung.Meilleur, Loesungen = examen ? LoesungsSichtbarkeit.ApresFrist : LoesungsSichtbarkeit.Immediat
            }));
            if (zuweisung == Guid.Empty) continue;
            Zaehle("Zuweisungen");
            var teilnehmer = s.Aktive.Where(x => x.Klasse == k).Where(_ => z.Ja(examen ? 0.7 : 0.5)).ToList();
            foreach (var schueler in teilnehmer)
            {
                if (examen) await ExamenSchreibenAsync(z, schueler, zuweisung);
                else await LeconUebenAsync(z, schueler, zuweisung);
            }
            if (examen)
            {
                var offen = await SollAsync("Korrekturliste", () => api.Korrektur.SucheAsync(lehrer, new KorrekturFilter(true, k.Id), 0, 200));
                foreach (var a in offen.Eintraege ?? [])
                {
                    await SollAsync("Korrigieren", () => api.Korrektur.KorrigierenAsync(lehrer, a.AntwortId, Math.Round((decimal)z.Zahl(0, 5)), "Réponse correcte mais incomplète."));
                    Zaehle("Korrekturen");
                }
            }
        }
    }

    private static AntwortEingabe Antwort(Zufall z, SimSchueler s, SchuelerFrage f)
    {
        var richtig = z.Anteil() < s.Niveau / 20.0;
        return f.Typ switch
        {
            FragenTyp.ChoixUnique => new AntwortEingabe { Optionen = [(richtig ? f.Optionen.First(o => o.Text is "2/4" or "12") : f.Optionen.First(o => o.Text is not ("2/4" or "12"))).Id] },
            FragenTyp.TexteATrous => new AntwortEingabe { Luecken = [richtig ? "cinq" : "6"] },
            _ => new AntwortEingabe { Text = richtig ? "1/2" : "une partie" }
        };
    }

    private async Task LeconUebenAsync(Zufall z, SimSchueler s, Guid zuweisung)
    {
        await ErsteAnmeldungAsync(s.Id);
        var lecon = await SollAsync("Leçon öffnen", () => api.Lernen.OeffnenAsync(s.Id, zuweisung));
        if (lecon is null) return;
        var versuche = z.Ja(0.3) ? 2 : 1;
        for (var v = 0; v < versuche; v++)
        {
            if (v > 0 && await SollAsync("Neuer Versuch", () => api.Lernen.NeuerVersuchAsync(s.Id, zuweisung)) is null) return;
            foreach (var f in lecon.Bloecke.SelectMany(b => b.Fragen))
                await SollAsync("Antworten", () => api.Lernen.AntwortenAsync(s.Id, zuweisung, f.Id, Antwort(z, s, f)));
            Zaehle("Versuche Leçon");
        }
    }

    private async Task ExamenSchreibenAsync(Zufall z, SimSchueler s, Guid zuweisung)
    {
        await ErsteAnmeldungAsync(s.Id);
        var versuch = await SollAsync("Examen starten", () => api.Pruefungen.StartenAsync(s.Id, zuweisung));
        if (versuch is null) return;
        var sitzung = await SollAsync("Examen öffnen", () => api.Pruefungen.OeffnenAsync(s.Id, zuweisung));
        if (sitzung is null) return;
        foreach (var f in sitzung.Bloecke.SelectMany(b => b.Fragen))
            await SollAsync("Examen antworten", () => api.Pruefungen.AntwortSpeichernAsync(s.Id, versuch.Id, f.Id, Antwort(z, s, f)));
        await SollAsync("Examen abgeben", () => api.Pruefungen.AbgebenAsync(s.Id, versuch.Id));
        Zaehle("Versuche Examen");
    }
}
