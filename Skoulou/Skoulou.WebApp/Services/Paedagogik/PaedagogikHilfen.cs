using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Paedagogik;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Paedagogik;

/// <summary>Rechte-Bausteine des Bereichs (immer nach <see cref="Zugriff.Pruefe"/>).</summary>
internal static class InhaltsRechte
{
    public const string MeldungFremderInhalt = "Ce contenu appartient à un autre enseignant.";
    public const string MeldungNichtGefunden = "Ce contenu est introuvable.";

    /// <summary>Lehrkraft oder Direktion, die Inhalte lesen darf (Direktion: alle der Schule, Lehrkraft: eigene).</summary>
    public static async Task<Akteur> LeserAsync(Zugriff zugriff, Guid akteurId, CancellationToken ct)
    {
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        Zugriff.Pruefe(akteur, Faehigkeit.InhalteErstellen);
        return akteur;
    }

    /// <summary>Wer Inhalte schreiben darf: Lehrkraft (eigene) oder Direktion (für Lehrkräfte).</summary>
    public static async Task<Akteur> SchreiberAsync(Zugriff zugriff, Guid akteurId, CancellationToken ct)
    {
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        Zugriff.Pruefe(akteur, akteur.IstDirektion ? Faehigkeit.InhalteFuerLehrerErfassen : Faehigkeit.InhalteErstellen, schreiben: true);
        return akteur;
    }

    /// <summary>Darf der Akteur diesen Inhalt sehen bzw. bearbeiten? (Direktion: Schule; Lehrkraft: Autor.)</summary>
    public static void PruefeInhalt(Akteur akteur, Guid schuleId, Guid autorId)
    {
        if (akteur.SchuleId != schuleId) throw new KeinZugriffFehler(Zugriff.MeldungAndereSchule);
        if (akteur.IstDirektion || autorId == akteur.Id) return;
        throw new KeinZugriffFehler(MeldungFremderInhalt);
    }

    /// <summary>Aktive Lehrkraft (Lehrer, Direktor, Koordinator) derselben Schule.</summary>
    public static async Task PruefeLehrkraftAsync(SkoulouDbContext db, Guid schuleId, Guid lehrkraftId, CancellationToken ct)
    {
        var ok = await db.Users.AnyAsync(k => k.Id == lehrkraftId && k.SchuleId == schuleId && k.Status == KontoStatus.Actif
                                             && Rollen.Lehrkraefte.Contains(k.Rolle), ct);
        if (!ok) throw new FachFehler("Cet enseignant n'existe pas ou n'est pas actif dans votre établissement.");
    }

    /// <summary>Schüler mit Fähigkeit « Lernen » laden.</summary>
    public static async Task<Akteur> SchuelerAsync(Zugriff zugriff, Guid schuelerId, CancellationToken ct)
    {
        var akteur = await zugriff.LadeAkteurAsync(schuelerId, ct);
        Zugriff.Pruefe(akteur, Faehigkeit.Lernen);
        return akteur;
    }
}

/// <summary>Umwandlungen zwischen Entitäten, Engine-Definitionen und Ansichten.</summary>
internal static class FragenAbbildung
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<IReadOnlyList<string>> Luecken(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var liste = JsonSerializer.Deserialize<List<List<string>>>(json, Json) ?? [];
            return liste.Select(l => (IReadOnlyList<string>)l).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string? LueckenJson(IReadOnlyList<IReadOnlyList<string>> luecken)
        => luecken.Count == 0 ? null : JsonSerializer.Serialize(luecken, Json);

    public static FrageDefinition Definition(Frage f)
        => new(f.Typ, f.Punkte, f.Optionen.OrderBy(o => o.Nr).Select(o => new OptionDefinition(o.Id, o.Richtig)).ToList(),
            f.AkzeptierteAntworten, Luecken(f.LueckenJson), f.AkzenteBeachten);

    public static AnsichtFrage Ansicht(Frage f, int nr)
        => new(f.Id, nr, f.Typ, f.TextHtml, f.Punkte, f.AudioText, f.Sprache,
            f.Optionen.OrderBy(o => o.Nr).Select(o => new AnsichtOption(o.Id, o.Text, o.Richtig)).ToList(),
            f.AkzeptierteAntworten, Luecken(f.LueckenJson), f.Wortbank, f.Revision);

    /// <summary>Schülersicht: Optionen ohne « richtig », Wortbank stabil gemischt, kein Audiotext.</summary>
    public static SchuelerFrage Schueler(Frage f, int nr)
        => new(f.Id, nr, f.Typ, f.TextHtml, f.Punkte, FragenTypen.IstAudio(f.Typ) && !string.IsNullOrWhiteSpace(f.AudioText),
            f.Optionen.OrderBy(o => o.Nr).Select(o => new SchuelerOption(o.Id, o.Text)).ToList(),
            Luecken(f.LueckenJson).Count, Gemischt(f.Wortbank, f.Id),
            FragenTypen.HatAutomatischeBewertung(f.Typ, f.AkzeptierteAntworten));

    public static Loesung Loesung(Frage f)
        => new(f.Optionen.Where(o => o.Richtig).Select(o => o.Id).ToList(), f.AkzeptierteAntworten.Where(a => !string.IsNullOrWhiteSpace(a)).ToList(),
            Luecken(f.LueckenJson));

    /// <summary>Gleiche Reihenfolge bei jedem Laden (Seed aus der Frage-Id), aber nicht die Lösungsreihenfolge.</summary>
    public static IReadOnlyList<string> Gemischt(IReadOnlyList<string> woerter, Guid seed)
    {
        if (woerter.Count < 2) return woerter;
        var zufall = new Random(BitConverter.ToInt32(seed.ToByteArray(), 0));
        var liste = woerter.ToList();
        for (var i = liste.Count - 1; i > 0; i--)
        {
            var j = zufall.Next(i + 1);
            (liste[i], liste[j]) = (liste[j], liste[i]);
        }
        if (liste.SequenceEqual(woerter)) (liste[0], liste[^1]) = (liste[^1], liste[0]);
        return liste;
    }

    /// <summary>Lädt Fragen (mit Optionen) nach Ids, auch archivierte Revisionen.</summary>
    public static async Task<Dictionary<Guid, Frage>> LadeAsync(SkoulouDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var liste = ids.Distinct().ToList();
        return await db.Fragen.AsNoTracking().Include(f => f.Optionen).Where(f => liste.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
    }

    /// <summary>Aktuelle (nicht archivierte) Fragen eines Inhalts in Lesereihenfolge.</summary>
    public static async Task<List<(AufgabenBlock Block, List<Frage> Fragen)>> AktuelleAsync(SkoulouDbContext db, Guid? leconId, Guid? pruefungId,
        CancellationToken ct)
    {
        var bloecke = await db.AufgabenBloecke.AsNoTracking()
            .Where(b => !b.Archiviert && (leconId != null ? b.LeconId == leconId : b.PruefungId == pruefungId))
            .OrderBy(b => b.Nr).ToListAsync(ct);
        var blockIds = bloecke.Select(b => b.Id).ToList();
        var fragen = await db.Fragen.AsNoTracking().Include(f => f.Optionen)
            .Where(f => blockIds.Contains(f.BlockId) && !f.Archiviert).OrderBy(f => f.Nr).ToListAsync(ct);
        return bloecke.Select(b => (b, fragen.Where(f => f.BlockId == b.Id).ToList())).ToList();
    }
}
