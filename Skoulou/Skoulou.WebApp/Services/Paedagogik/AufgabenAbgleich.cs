using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Paedagogik;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Paedagogik;

/// <summary>
/// Prüft und speichert Aufgabenblöcke und Fragen eines Inhalts. Regeln: Fragen mit Antworten sind
/// unveränderlich – eine inhaltliche Änderung archiviert sie und legt eine neue Revision an (alte Versuche
/// bleiben gültig); entfernte Fragen/Blöcke mit Antworten werden archiviert statt gelöscht.
/// Nummern werden in zwei Schritten vergeben (gefilterte Unique-Indizes ohne Zwischenkonflikt).
/// </summary>
public sealed class AufgabenAbgleich(InhaltBereiniger bereiniger)
{
    public const int MaxBloecke = 30;
    public const int MaxFragen = 120;
    private const int Verschiebung = 10_000;

    /// <summary>Bereinigter, bewertungsrelevanter Inhalt einer Frage (Vergleich über den Fingerabdruck).</summary>
    private sealed record FrageInhalt(FragenTyp Typ, string TextHtml, decimal Punkte, string? AudioText, string Sprache,
        IReadOnlyList<string> Varianten, IReadOnlyList<IReadOnlyList<string>> Luecken, IReadOnlyList<string> Wortbank,
        bool AkzenteBeachten, IReadOnlyList<(string Text, bool Richtig)> Optionen)
    {
        public string Fingerabdruck => JsonSerializer.Serialize(new
        {
            T = (int)Typ, TextHtml, P = Punkte.ToString("0.##", CultureInfo.InvariantCulture), AudioText, Sprache, Varianten,
            Luecken, Wortbank, AkzenteBeachten, O = Optionen.Select(o => new[] { o.Text, o.Richtig ? "1" : "0" })
        });

        public void Anwenden(Frage f)
        {
            f.Typ = Typ;
            f.TextHtml = TextHtml;
            f.Punkte = Punkte;
            f.AudioText = AudioText;
            f.Sprache = Sprache;
            f.AkzeptierteAntworten = Varianten.ToList();
            f.LueckenJson = FragenAbbildung.LueckenJson(Luecken);
            f.Wortbank = Wortbank.ToList();
            f.AkzenteBeachten = AkzenteBeachten;
            f.Optionen = Optionen.Select((o, i) => new FrageOption { FrageId = f.Id, Nr = i + 1, Text = o.Text, Richtig = o.Richtig }).ToList();
        }

        public static FrageInhalt Aus(Frage f)
            => new(f.Typ, f.TextHtml, f.Punkte, f.AudioText, f.Sprache, f.AkzeptierteAntworten, FragenAbbildung.Luecken(f.LueckenJson),
                f.Wortbank, f.AkzenteBeachten, f.Optionen.OrderBy(o => o.Nr).Select(o => (o.Text, o.Richtig)).ToList());
    }

    /// <summary>Prüft alle Blöcke (wirft <see cref="FachFehler"/> mit Ort der Frage).</summary>
    public void Pruefe(IReadOnlyList<BlockFormular> bloecke)
    {
        if (bloecke.Count > MaxBloecke) throw new FachFehler($"Un contenu peut contenir au plus {MaxBloecke} exercices.");
        if (bloecke.Sum(b => b.Fragen.Count) > MaxFragen) throw new FachFehler($"Un contenu peut contenir au plus {MaxFragen} questions.");
        for (var b = 0; b < bloecke.Count; b++)
        {
            var block = bloecke[b];
            if ((block.Titel?.Trim().Length ?? 0) > 200) throw new FachFehler($"Exercice {b + 1} : le titre est trop long (200 caractères au plus).");
            if ((block.Consigne?.Trim().Length ?? 0) > 2000) throw new FachFehler($"Exercice {b + 1} : la consigne est trop longue (2000 caractères au plus).");
            for (var q = 0; q < block.Fragen.Count; q++) _ = Bereinigt(block.Fragen[q], $"Exercice {b + 1}, question {q + 1}");
        }
    }

    private FrageInhalt Bereinigt(FrageFormular f, string ort)
    {
        static List<string> Liste(IEnumerable<string>? werte)
            => (werte ?? []).Select(v => v?.Trim() ?? string.Empty).Where(v => v.Length > 0).Distinct().ToList();

        if (f.Punkte is <= 0 or > 100) throw new FachFehler($"{ort} : les points doivent être compris entre 0,25 et 100.");
        var punkte = Math.Round(f.Punkte, 2, MidpointRounding.AwayFromZero);

        string text;
        IReadOnlyList<IReadOnlyList<string>> luecken = [];
        if (f.Typ == FragenTyp.TexteATrous)
        {
            var roh = (f.TextHtml ?? string.Empty).Trim();
            luecken = f.Luecken.Select(l => (IReadOnlyList<string>)Liste(l)).ToList();
            if (roh.Length == 0) throw new FachFehler($"{ort} : saisissez le texte à trous.");
            if (LueckenText.Pruefe(roh, luecken.Count) is { } fehler) throw new FachFehler($"{ort} : {fehler}");
            for (var i = 0; i < luecken.Count; i++)
                if (luecken[i].Count == 0) throw new FachFehler($"{ort} : le trou [[{i + 1}]] n'a pas de réponse.");
            text = HtmlBereiniger.TextAlsHtml(roh);
        }
        else
        {
            text = bereiniger.Bereinige(f.TextHtml);
            if (text.Length == 0) throw new FachFehler($"{ort} : saisissez l'énoncé de la question.");
        }

        string? audio = null;
        var sprache = FragenTypen.Sprachen.Any(s => s.Code == f.Sprache) ? f.Sprache : "fr-FR";
        if (FragenTypen.IstAudio(f.Typ))
        {
            // Niveau im selben Feld (« fr-FR/A2 »; Standard ohne Zusatz – ältere Fragen gelten als unverändert).
            sprache = SprechNiveaus.Verbinden(sprache, f.Niveau);
            audio = f.AudioText?.Trim();
            if (string.IsNullOrEmpty(audio)) throw new FachFehler($"{ort} : saisissez le texte que l'élève entendra.");
            if (audio.Length > 2000) throw new FachFehler($"{ort} : le texte audio est trop long (2000 caractères au plus).");
        }

        var optionen = new List<(string, bool)>();
        if (FragenTypen.IstAuswahl(f.Typ))
        {
            optionen = f.Optionen.Select(o => (Text: o.Text?.Trim() ?? string.Empty, o.Richtig)).Where(o => o.Text.Length > 0).ToList();
            if (optionen.Count < 2) throw new FachFehler($"{ort} : proposez au moins deux réponses.");
            if (optionen.Count > 12) throw new FachFehler($"{ort} : 12 réponses au plus.");
            if (optionen.Any(o => o.Item1.Length > 1000)) throw new FachFehler($"{ort} : une réponse est trop longue (1000 caractères au plus).");
            var richtige = optionen.Count(o => o.Item2);
            if (richtige == 0) throw new FachFehler($"{ort} : cochez au moins une bonne réponse.");
            if (!FragenTypen.IstMehrfach(f.Typ) && richtige > 1)
                throw new FachFehler($"{ort} : un choix unique n'a qu'une seule bonne réponse.");
        }

        var varianten = FragenTypen.IstText(f.Typ) ? Liste(f.Varianten) : [];
        var wortbank = new List<string>();
        if (f.Typ == FragenTyp.ConstructionPhrase)
        {
            wortbank = (f.Wortbank ?? []).Select(w => w?.Trim() ?? string.Empty).Where(w => w.Length > 0).ToList();
            if (wortbank.Count is > 0 and < 2) throw new FachFehler($"{ort} : proposez au moins deux mots.");
            if (wortbank.Count > 40) throw new FachFehler($"{ort} : 40 mots au plus.");
        }

        return new FrageInhalt(f.Typ, text, punkte, audio, sprache, varianten, luecken, wortbank,
            f.AkzenteBeachten && f.Typ != FragenTyp.ChoixUnique && !FragenTypen.IstAuswahl(f.Typ), optionen);
    }

    /// <summary>Speichert die Blöcke (Aufrufer hält die Transaktion und speichert danach nichts mehr selbst).</summary>
    public async Task AbgleichenAsync(SkoulouDbContext db, Guid schuleId, Guid? leconId, Guid? pruefungId,
        IReadOnlyList<BlockFormular> formular, CancellationToken ct)
    {
        var bloecke = await db.AufgabenBloecke
            .Where(b => !b.Archiviert && (leconId != null ? b.LeconId == leconId : b.PruefungId == pruefungId)).ToListAsync(ct);
        var blockIds = bloecke.Select(b => b.Id).ToList();
        var fragen = await db.Fragen.Include(f => f.Optionen).Where(f => blockIds.Contains(f.BlockId) && !f.Archiviert).ToListAsync(ct);
        var fragenIds = fragen.Select(f => f.Id).ToList();
        var beantwortet = (await db.Antworten.Where(a => fragenIds.Contains(a.FrageId)).Select(a => a.FrageId).Distinct().ToListAsync(ct)).ToHashSet();

        // Plan: was passiert mit jeder bestehenden Frage?
        var fragenNachId = fragen.ToDictionary(f => f.Id);
        var neueInhalte = new Dictionary<string, FrageInhalt>();
        var aenderungen = new Dictionary<Guid, FrageInhalt>();
        for (var b = 0; b < formular.Count; b++)
        {
            for (var q = 0; q < formular[b].Fragen.Count; q++)
            {
                var ff = formular[b].Fragen[q];
                var inhalt = Bereinigt(ff, $"Exercice {b + 1}, question {q + 1}");
                neueInhalte[ff.Schluessel] = inhalt;
                if (ff.Id is { } id && fragenNachId.TryGetValue(id, out var alt) && FrageInhalt.Aus(alt).Fingerabdruck != inhalt.Fingerabdruck)
                    aenderungen[id] = inhalt;
            }
        }
        var behalteneFragen = formular.SelectMany(b => b.Fragen).Where(f => f.Id is { } id && fragenNachId.ContainsKey(id))
            .Select(f => f.Id!.Value).ToHashSet();
        var behalteneBloecke = formular.Where(b => b.Id is { } id && blockIds.Contains(id)).Select(b => b.Id!.Value).ToHashSet();

        // Schritt 1: Nummern aus dem Weg, Entferntes archivieren/löschen, Optionen geänderter Fragen ohne Antworten löschen.
        foreach (var b in bloecke) b.Nr += Verschiebung;
        foreach (var f in fragen)
        {
            if (!behalteneFragen.Contains(f.Id) || (aenderungen.ContainsKey(f.Id) && beantwortet.Contains(f.Id)))
            {
                if (beantwortet.Contains(f.Id)) f.Archiviert = true;
                else db.Fragen.Remove(f);
                continue;
            }
            f.Nr += Verschiebung;
            if (aenderungen.ContainsKey(f.Id))
            {
                db.FrageOptionen.RemoveRange(f.Optionen);
                f.Optionen = [];
            }
        }
        await db.SaveChangesAsync(ct);

        // Schritt 2: Blöcke und Fragen in ihre endgültige Ordnung.
        var bloeckeNachId = bloecke.ToDictionary(b => b.Id);
        for (var b = 0; b < formular.Count; b++)
        {
            var bf = formular[b];
            if (bf.Id is not { } bid || !bloeckeNachId.TryGetValue(bid, out var block))
            {
                block = new AufgabenBlock { SchuleId = schuleId, LeconId = leconId, PruefungId = pruefungId };
                db.AufgabenBloecke.Add(block);
                bf.Id = block.Id;
            }
            block.Nr = b + 1;
            block.Titel = Leer(bf.Titel);
            block.Consigne = Leer(bf.Consigne);

            for (var q = 0; q < bf.Fragen.Count; q++)
            {
                var ff = bf.Fragen[q];
                var inhalt = neueInhalte[ff.Schluessel];
                if (ff.Id is { } fid && fragenNachId.TryGetValue(fid, out var frage))
                {
                    if (aenderungen.ContainsKey(fid) && beantwortet.Contains(fid))
                    {
                        var neu = new Frage { SchuleId = schuleId, BlockId = block.Id, Nr = q + 1, Revision = frage.Revision + 1, VorgaengerId = frage.Id };
                        inhalt.Anwenden(neu);
                        db.Fragen.Add(neu);
                        ff.Id = neu.Id;
                        ff.Revision = neu.Revision;
                        ff.HatAntworten = false;
                        continue;
                    }
                    if (aenderungen.ContainsKey(fid))
                    {
                        inhalt.Anwenden(frage);
                        db.FrageOptionen.AddRange(frage.Optionen);
                    }
                    frage.BlockId = block.Id;
                    frage.Nr = q + 1;
                }
                else
                {
                    var neu = new Frage { SchuleId = schuleId, BlockId = block.Id, Nr = q + 1 };
                    inhalt.Anwenden(neu);
                    db.Fragen.Add(neu);
                    ff.Id = neu.Id;
                }
            }
        }
        await db.SaveChangesAsync(ct);

        // Schritt 3: entfernte Blöcke – mit (archivierten) Fragen archivieren, sonst löschen.
        foreach (var b in bloecke.Where(b => !behalteneBloecke.Contains(b.Id)))
        {
            if (await db.Fragen.AnyAsync(f => f.BlockId == b.Id, ct)) b.Archiviert = true;
            else db.AufgabenBloecke.Remove(b);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Für den Editor: Lückentext zurück in Klartext (Gegenstück zu <see cref="HtmlBereiniger.TextAlsHtml"/>).</summary>
    public static string LueckenTextZumBearbeiten(string html)
        => WebUtility.HtmlDecode(html.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase));

    private static string? Leer(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
