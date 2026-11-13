using Microsoft.Extensions.Logging.Abstractions;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Medien;
using Skoulou.WebApp.Services.Paedagogik;

namespace Skoulou.Tests.Paedagogik;

/// <summary>
/// Schule mit laufendem Jahr, Stufe 6e, Fächern Maths/Français, Klassen 6e A/6e B, Lehraufträgen und Konten
/// aller Rollen (plus Direktion einer fremden Schule) – dazu alle Dienste des Bereichs mit Test-Uhr.
/// Lehrer: Maths in 6e A und 6e B; Lehrer2: Français in 6e A. SchuelerA in 6e A, SchuelerB in 6e B.
/// </summary>
internal sealed class PaedagogikUmgebung : IAsyncDisposable
{
    public TestUmgebung Env { get; } = new();
    public TestZeit Zeit => Env.Zeit;

    public Guid Schule, Jahr, Stufe, Maths, Francais, Klasse6A, Klasse6B;
    public Konto Lehrer = default!, Lehrer2 = default!, Direktor = default!, Koordinator = default!, SchuelerA = default!, SchuelerB = default!,
        Eltern = default!, FremdDirektor = default!;

    public InhaltService Inhalte = default!;
    public InhaltLeseService Lesen = default!;
    public ZuweisungService Zuweisungen = default!;
    public LernService Lernen = default!;
    public PruefungsService Pruefungen = default!;
    public KorrekturService Korrektur = default!;
    public UebersichtService Uebersicht = default!;
    public FormelService Formeln = default!;
    public MedienService Medien = default!;
    public string MedienOrdner { get; } = Path.Combine(Path.GetTempPath(), "skoulou-tests-medien", Guid.NewGuid().ToString("N"));

    public static async Task<PaedagogikUmgebung> ErstelleAsync()
    {
        var u = new PaedagogikUmgebung();
        await u.AufbauenAsync();
        return u;
    }

    private async Task AufbauenAsync()
    {
        await Env.SeedAsync();
        var schule = await Env.SchuleAsync("Collège Pédagogie");
        var fremd = await Env.SchuleAsync("Collège Ailleurs");
        Schule = schule.Id;
        Lehrer = await Env.KontoAsync("prof.maths", Rollen.Lehrer, Schule);
        Lehrer2 = await Env.KontoAsync("prof.francais", Rollen.Lehrer, Schule);
        Direktor = await Env.KontoAsync("dir.ecole", Rollen.Direktor, Schule);
        Koordinator = await Env.KontoAsync("coord.ecole", Rollen.Koordinator, Schule);
        SchuelerA = await Env.KontoAsync("eleve.a", Rollen.Schueler, Schule);
        SchuelerB = await Env.KontoAsync("eleve.b", Rollen.Schueler, Schule);
        Eltern = await Env.KontoAsync("parent.a", Rollen.Eltern, Schule);
        FremdDirektor = await Env.KontoAsync("dir.ailleurs", Rollen.Direktor, fremd.Id);

        await using (var db = await Env.Db.CreateDbContextAsync())
        {
            var jahr = new Schuljahr { SchuleId = Schule, Bezeichnung = "2026-2027", Beginn = new(2026, 9, 1), Ende = new(2027, 7, 31), Status = SchuljahrStatus.Laufend };
            var stufe = new Stufe { SchuleId = Schule, Name = "6e", Reihenfolge = 1 };
            var maths = new Fach { SchuleId = Schule, Name = "Mathématiques", Reihenfolge = 1 };
            var francais = new Fach { SchuleId = Schule, Name = "Français", Reihenfolge = 2 };
            var a = new Klasse { SchuleId = Schule, SchuljahrId = jahr.Id, StufeId = stufe.Id, Name = "6e A" };
            var b = new Klasse { SchuleId = Schule, SchuljahrId = jahr.Id, StufeId = stufe.Id, Name = "6e B" };
            db.AddRange(jahr, stufe, maths, francais, a, b);
            void Auftrag(Klasse k, Fach f, Konto lehrer)
            {
                var kf = new Klassenfach { SchuleId = Schule, KlasseId = k.Id, FachId = f.Id, Koeffizient = 1 };
                db.Klassenfaecher.Add(kf);
                db.Lehrauftraege.Add(new Lehrauftrag { SchuleId = Schule, KlassenfachId = kf.Id, LehrerId = lehrer.Id, Wochenstunden = 2 });
            }
            Auftrag(a, maths, Lehrer);
            Auftrag(b, maths, Lehrer);
            Auftrag(a, francais, Lehrer2);
            foreach (var (s, k) in new[] { (SchuelerA, a), (SchuelerB, b) })
            {
                db.SchuelerProfile.Add(new SchuelerProfil { KontoId = s.Id, SchuleId = Schule, Matrikel = s.UserName });
                db.Einschreibungen.Add(new Einschreibung { SchuleId = Schule, SchuelerId = s.Id, SchuljahrId = jahr.Id, KlasseId = k.Id, Von = jahr.Beginn, Status = EinschreibungsStatus.Active });
            }
            db.ElternKinder.Add(new ElternKind { SchuleId = Schule, ElternId = Eltern.Id, SchuelerId = SchuelerA.Id, Beziehung = "Mère" });
            await db.SaveChangesAsync();
            (Jahr, Stufe, Maths, Francais, Klasse6A, Klasse6B) = (jahr.Id, stufe.Id, maths.Id, francais.Id, a.Id, b.Id);
        }

        var zugriff = Env.Hole<Zugriff>();
        var audit = Env.Hole<AuditService>();
        var bereiniger = new InhaltBereiniger();
        Inhalte = new InhaltService(Env.Db, zugriff, new AufgabenAbgleich(bereiniger), bereiniger, audit);
        Lesen = new InhaltLeseService(Env.Db, zugriff, Zeit);
        Zuweisungen = new ZuweisungService(Env.Db, zugriff, Zeit);
        Lernen = new LernService(Env.Db, zugriff, Zeit);
        Pruefungen = new PruefungsService(Env.Db, zugriff, Zeit, NullLogger<PruefungsService>.Instance);
        Korrektur = new KorrekturService(Env.Db, zugriff, Zeit);
        Uebersicht = new UebersichtService(Env.Db, zugriff, Zeit);
        Formeln = new FormelService(Env.Db, zugriff);
        Directory.CreateDirectory(MedienOrdner);
        Medien = new MedienService(Env.Db, zugriff, new LokalerMedienSpeicher(MedienOrdner, Path.Combine(MedienOrdner, "..", "wwwroot-test")), Zeit,
            NullLogger<MedienService>.Instance);
    }

    // ---------- Bausteine ----------

    public static FrageFormular Auswahl(string text, decimal punkte, string richtig, params string[] falsche)
        => new()
        {
            Typ = FragenTyp.ChoixUnique, TextHtml = $"<p>{text}</p>", Punkte = punkte,
            Optionen = [new() { Text = richtig, Richtig = true }, .. falsche.Select(f => new OptionFormular { Text = f })]
        };

    public static FrageFormular Text(string text, decimal punkte, params string[] varianten)
        => new() { Typ = FragenTyp.Texte, TextHtml = $"<p>{text}</p>", Punkte = punkte, Varianten = varianten.ToList() };

    public static FrageFormular Luecken(string text, decimal punkte, params string[][] luecken)
        => new() { Typ = FragenTyp.TexteATrous, TextHtml = text, Punkte = punkte, Luecken = luecken.Select(l => l.ToList()).ToList() };

    /// <summary>Formular einer Leçon/eines Examens (Maths 6e) mit den angegebenen Fragen in einem Block.</summary>
    public InhaltFormular Formular(InhaltsArt art, string titel, params FrageFormular[] fragen)
    {
        var f = new InhaltFormular
        {
            Art = art, Titel = titel, StufeId = Stufe, FachId = Maths, Status = InhaltsStatus.Prete, DauerMinuten = 30,
            Bloecke = [new BlockFormular { Titel = "Exercice 1", Fragen = fragen.ToList() }]
        };
        if (art == InhaltsArt.Lecon) f.Seiten.Add(new SeiteFormular { Html = "<p>Cours</p>" });
        return f;
    }

    /// <summary>Speichert und weist der Klasse zu (Lehrer = Autor).</summary>
    public async Task<(Guid InhaltId, Guid ZuweisungId)> ZugewiesenAsync(InhaltFormular f, Guid? klasse = null, Konto? autor = null,
        int? maxVersuche = null, VersuchsWertung wertung = VersuchsWertung.Meilleur, LoesungsSichtbarkeit loesungen = LoesungsSichtbarkeit.Immediat,
        DateTime? frist = null)
    {
        autor ??= Lehrer;
        var id = await Inhalte.SpeichernAsync(autor.Id, f);
        var jetzt = Zeit.GetUtcNow().UtcDateTime;
        var z = await Zuweisungen.ZuweisenAsync(autor.Id, new ZuweisungFormular
        {
            InhaltId = id, Art = f.Art, KlasseId = klasse ?? Klasse6A, AbUtc = jetzt.AddMinutes(-1),
            FristUtc = frist ?? (f.Art == InhaltsArt.Pruefung ? jetzt.AddDays(1) : null),
            MaxVersuche = maxVersuche, Wertung = wertung, Loesungen = loesungen
        });
        return (id, z);
    }

    /// <summary>Fragen in Lesereihenfolge (aus der Schülersicht der Zuweisung).</summary>
    public async Task<List<SchuelerFrage>> FragenAsync(Guid schuelerId, Guid zuweisungId)
        => (await Lernen.OeffnenAsync(schuelerId, zuweisungId)).Bloecke.SelectMany(b => b.Fragen).ToList();

    public async ValueTask DisposeAsync()
    {
        await Env.DisposeAsync();
        try { Directory.Delete(MedienOrdner, true); } catch (IOException) { }
    }
}
