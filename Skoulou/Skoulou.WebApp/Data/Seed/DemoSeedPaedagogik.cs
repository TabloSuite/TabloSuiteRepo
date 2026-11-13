using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Paedagogik;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Paedagogik;

namespace Skoulou.WebApp.Data.Seed;

/// <summary>
/// Demo-Daten des Bereichs E4 Pädagogik (nach der Grund-Demo-Schule; nur erfundene Daten): je Demo-Lehrkraft
/// eine Leçon mit Seiten und mehreren Fragetypen (inkl. Formel), ein Examen, Zuweisungen an Demo-Klassen und
/// einige Versuche – darunter eine Textantwort, die auf die Korrektur wartet.
/// </summary>
public static class DemoSeedPaedagogik
{
    /// <param name="dienste">Dienste der App (DbContextFactory, UserManager, Bereichsdienste …).</param>
    /// <param name="schuleId">Demo-Schule « Collège Démo Les Palmiers ».</param>
    /// <param name="neueKonten">Neu angelegte Demo-Konten (Passwörter landen nur in demo-konten.json).</param>
    public static async Task ErgaenzenAsync(IServiceProvider dienste, Guid schuleId, List<DemoKonto> neueKonten, CancellationToken ct)
    {
        var dbFactory = dienste.GetRequiredService<IDbContextFactory<SkoulouDbContext>>();
        var jetzt = dienste.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Lecons.AnyAsync(l => l.SchuleId == schuleId, ct)) return;

        var lehrer = await db.Users.Where(k => k.SchuleId == schuleId && (k.Vorname == "Yao" || k.Vorname == "Afi")).ToDictionaryAsync(k => k.Vorname, k => k.Id, ct);
        var faecher = await db.Faecher.Where(f => f.SchuleId == schuleId).ToDictionaryAsync(f => f.Name, f => f.Id, ct);
        var sechste = await db.Stufen.Where(s => s.SchuleId == schuleId && s.Name == "6e").Select(s => s.Id).FirstAsync(ct);
        var klassen = await db.Klassen.Where(k => k.SchuleId == schuleId).ToDictionaryAsync(k => k.Name, k => k.Id, ct);
        var schueler = await db.Users.Where(k => k.SchuleId == schuleId && k.Rolle == Rollen.Schueler).ToDictionaryAsync(k => k.Nachname, k => k.Id, ct);
        if (!lehrer.ContainsKey("Yao") || !lehrer.ContainsKey("Afi")) return;
        var yao = lehrer["Yao"];
        var afi = lehrer["Afi"];

        var b = new Bauer(db, schuleId);

        // ---------- Mathématiques (Yao): Leçon « Les fractions » ----------
        var maths = new Lecon
        {
            SchuleId = schuleId, AutorId = yao, FachId = faecher["Mathématiques"], StufeId = sechste, Status = InhaltsStatus.Prete,
            Titel = "Les fractions", Thema = "Nombres et calculs",
            Zusammenfassung = "Lire, écrire, simplifier et comparer des fractions simples."
        };
        maths.Seiten =
        [
            Seite(maths, 1, "<h2>Qu'est-ce qu'une fraction ?</h2><p>Une fraction s'écrit <span class=\"ql-formula\" data-value=\"\\frac{a}{b}\"></span> : "
                + "<strong>a</strong> est le <em>numérateur</em>, <strong>b</strong> est le <em>dénominateur</em> (b ≠ 0).</p>"
                + "<p>Exemple : si l'on partage une galette en 4 parts égales et que l'on en mange 3, on a mangé <span class=\"ql-formula\" data-value=\"\\frac{3}{4}\"></span> de la galette.</p>"
                + "<ol><li data-list=\"bullet\">Le dénominateur dit en combien de parts on partage.</li><li data-list=\"bullet\">Le numérateur dit combien de parts on prend.</li></ol>"),
            Seite(maths, 2, "<h2>Simplifier une fraction</h2><p>On divise le numérateur et le dénominateur par un même nombre :</p>"
                + "<p class=\"ql-align-center\"><span class=\"ql-formula\" data-value=\"\\frac{6}{8}=\\frac{6\\div 2}{8\\div 2}=\\frac{3}{4}\"></span></p>"
                + "<p>Une fraction est <strong>irréductible</strong> quand on ne peut plus la simplifier.</p>"),
            Seite(maths, 3, "<h2>Comparer des fractions</h2><p>Avec le même dénominateur, la plus grande fraction est celle qui a le plus grand numérateur :</p>"
                + "<p class=\"ql-align-center\"><span class=\"ql-formula\" data-value=\"\\frac{2}{7} \\lt \\frac{5}{7}\"></span></p>")
        ];
        db.Lecons.Add(maths);
        var m1 = b.Block(maths.Id, null, 1, "Reconnaître une fraction", "Lisez bien chaque question.");
        var mq1 = b.Auswahl(m1, 1, FragenTyp.ChoixUnique, "<p>Quelle fraction représente la moitié d'une galette ?</p>", 1, ("1/2", true), ("1/3", false), ("2/1", false));
        var mq2 = b.Auswahl(m1, 2, FragenTyp.ChoixMultiple, "<p>Quelles fractions sont égales à <span class=\"ql-formula\" data-value=\"\\frac{1}{2}\"></span> ?</p>", 2,
            ("2/4", true), ("3/6", true), ("2/3", false));
        var m2 = b.Block(maths.Id, null, 2, "Calculer", "Écrivez vos réponses sous la forme a/b.");
        var mq3 = b.Text(m2, 1, FragenTyp.Texte, "<p>Simplifiez la fraction 6/8.</p>", 2, ["3/4"]);
        var mq4 = b.Luecken(m2, 2, "Dans la fraction 3/5, le numérateur est [[1]] et le dénominateur est [[2]].", 2, [["3", "trois"], ["5", "cinq"]]);
        var mq5 = b.Text(m2, 3, FragenTyp.Texte, "<p>Expliquez avec vos mots ce qu'est une fraction. Donnez un exemple de la vie de tous les jours.</p>", 3, []);

        // ---------- Mathématiques (Yao): Examen ----------
        var examen = new Pruefung
        {
            SchuleId = schuleId, AutorId = yao, FachId = faecher["Mathématiques"], StufeId = sechste, Status = InhaltsStatus.Prete,
            Titel = "Devoir n° 1 – Fractions", DauerMinuten = 45, Bestehensgrenze = 50,
            Beschreibung = "Calculatrice interdite. Lisez chaque question avant de répondre."
        };
        db.Pruefungen.Add(examen);
        var p1 = b.Block(null, examen.Id, 1, "Questions de cours", null);
        var pq1 = b.Auswahl(p1, 1, FragenTyp.ChoixUnique, "<p>Dans <span class=\"ql-formula\" data-value=\"\\frac{5}{9}\"></span>, quel est le dénominateur ?</p>", 2,
            ("5", false), ("9", true), ("14", false));
        var pq2 = b.Luecken(p1, 2, "Une fraction qu'on ne peut plus simplifier est [[1]].", 2, [["irréductible"]]);
        var p2 = b.Block(null, examen.Id, 2, "Problème", "Rédigez une réponse complète.");
        var pq3 = b.Text(p2, 1, FragenTyp.Texte, "<p>Simplifiez 10/15.</p>", 3, ["2/3"]);
        var pq4 = b.Text(p2, 2, FragenTyp.Texte, "<p>Awa a mangé 2/8 d'une pizza et Kodjo 1/4. Qui a mangé le plus ? Justifiez.</p>", 3, []);

        // ---------- Français (Afi): Leçon « Le présent de l'indicatif » ----------
        var francais = new Lecon
        {
            SchuleId = schuleId, AutorId = afi, FachId = faecher["Français"], StufeId = sechste, Status = InhaltsStatus.Prete,
            Titel = "Le présent de l'indicatif", Thema = "Conjugaison",
            Zusammenfassung = "Conjuguer les verbes du 1er groupe au présent et écrire une phrase correcte."
        };
        francais.Seiten =
        [
            Seite(francais, 1, "<h2>Les verbes du 1er groupe</h2><p>Au présent, les verbes en <strong>-er</strong> prennent les terminaisons :</p>"
                + "<p class=\"ql-align-center\"><strong>-e, -es, -e, -ons, -ez, -ent</strong></p><p>Exemple : je chant<strong>e</strong>, nous chant<strong>ons</strong>, ils chant<strong>ent</strong>.</p>"),
            Seite(francais, 2, "<h2>Écrire une phrase</h2><p>Une phrase commence par une majuscule et se termine par un point. Le verbe s'accorde avec son sujet.</p>")
        ];
        db.Lecons.Add(francais);
        var f1 = b.Block(francais.Id, null, 1, "Conjuguer", null);
        b.Luecken(f1, 1, "Nous [[1]] une chanson. (chanter)", 1, [["chantons"]]);
        b.Auswahl(f1, 2, FragenTyp.ChoixUnique, "<p>Choisissez la bonne forme : « Les élèves … bien. » (travailler)</p>", 1,
            ("travaille", false), ("travaillent", true), ("travaillez", false));
        var f2 = b.Block(francais.Id, null, 2, "Écouter et écrire", "Touchez « Écouter » autant de fois que nécessaire.");
        b.Text(f2, 1, FragenTyp.ConstructionPhrase, "<p>Remettez les mots dans l'ordre pour former une phrase.</p>", 2, ["Les enfants jouent dans la cour"],
            wortbank: ["Les", "enfants", "jouent", "dans", "la", "cour"]);
        b.Text(f2, 2, FragenTyp.AudioTexte, "<p>Dictée : écrivez la phrase que vous entendez.</p>", 2, ["Le chat dort sur le tapis"], audio: "Le chat dort sur le tapis.");
        b.Auswahl(f2, 3, FragenTyp.AudioChoixUnique, "<p>Écoutez : quel mot avez-vous entendu ?</p>", 1, ("marché", true), ("marcher", false), ("mardi", false),
            audio: "Je vais au marché.");
        await db.SaveChangesAsync(ct);

        // ---------- Zuweisungen ----------
        var zMaths6A = Zuweisung(schuleId, maths.Id, null, klassen["6e A"], yao, jetzt.AddDays(-3), jetzt.AddDays(7));
        var zMaths6B = Zuweisung(schuleId, maths.Id, null, klassen["6e B"], yao, jetzt.AddDays(-1), jetzt.AddDays(10));
        var zFr6A = Zuweisung(schuleId, francais.Id, null, klassen["6e A"], afi, jetzt.AddDays(-2), null);
        var zExamen = Zuweisung(schuleId, null, examen.Id, klassen["6e A"], yao, jetzt.AddHours(-2), jetzt.AddDays(3));
        zExamen.MaxVersuche = 1;
        zExamen.Wertung = VersuchsWertung.Premier;
        zExamen.LoesungenSichtbar = LoesungsSichtbarkeit.ApresFrist;
        db.Zuweisungen.AddRange(zMaths6A, zMaths6B, zFr6A, zExamen);
        await db.SaveChangesAsync(ct);

        // ---------- Versuche ----------
        var mathsFragen = new[] { mq1, mq2, mq3, mq4, mq5 };
        // Kossi: alles beantwortet, Erklärung wartet auf die Korrektur.
        await VersuchAsync(db, zMaths6A, schueler["Adjo"], mathsFragen, jetzt.AddDays(-2), new()
        {
            [mq1.Id] = Wahl(mq1, "1/2"), [mq2.Id] = Wahl(mq2, "2/4", "2/3"), [mq3.Id] = new AntwortEingabe { Text = "3/4" },
            [mq4.Id] = new AntwortEingabe { Luecken = ["3", "4"] },
            [mq5.Id] = new AntwortEingabe { Text = "Une fraction, c'est un partage en parts égales. Par exemple la moitié d'une orange." }
        }, ct);
        // Ama: zweiter Versuch, korrigiert von Yao.
        await VersuchAsync(db, zMaths6A, schueler["Dossou"], mathsFragen, jetzt.AddDays(-2), new()
        {
            [mq1.Id] = Wahl(mq1, "1/3"), [mq2.Id] = Wahl(mq2, "2/4"), [mq3.Id] = new AntwortEingabe { Text = "6/8" },
            [mq4.Id] = new AntwortEingabe { Luecken = ["3", "5"] }, [mq5.Id] = new AntwortEingabe { Text = "C'est un nombre." }
        }, ct, korrektor: (yao, 1m, "Bon début : précisez que les parts sont égales."));
        await VersuchAsync(db, zMaths6A, schueler["Dossou"], mathsFragen, jetzt.AddDays(-1), new()
        {
            [mq1.Id] = Wahl(mq1, "1/2"), [mq2.Id] = Wahl(mq2, "2/4", "3/6"), [mq3.Id] = new AntwortEingabe { Text = "3/4" },
            [mq4.Id] = new AntwortEingabe { Luecken = ["trois", "cinq"] },
            [mq5.Id] = new AntwortEingabe { Text = "Une fraction est une partie d'un tout partagé en parts égales, comme 1/4 d'une galette." }
        }, ct, korrektor: (yao, 3m, "Très bien expliqué !"));
        // Ama: Examen abgegeben (Erklärung wartet auf die Korrektur).
        await VersuchAsync(db, zExamen, schueler["Dossou"], [pq1, pq2, pq3, pq4], jetzt.AddMinutes(-90), new()
        {
            [pq1.Id] = Wahl(pq1, "9"), [pq2.Id] = new AntwortEingabe { Luecken = ["irreductible"] }, [pq3.Id] = new AntwortEingabe { Text = "2/3" },
            [pq4.Id] = new AntwortEingabe { Text = "Ils ont mangé pareil car 2/8 = 1/4." }
        }, ct, dauer: 45);
    }

    private static LeconSeite Seite(Lecon l, int nr, string html) => new() { LeconId = l.Id, Nr = nr, Html = html };

    private static Zuweisung Zuweisung(Guid schuleId, Guid? lecon, Guid? pruefung, Guid klasse, Guid lehrer, DateTime ab, DateTime? frist)
        => new()
        {
            SchuleId = schuleId, LeconId = lecon, PruefungId = pruefung, KlasseId = klasse, LehrerId = lehrer, AbUtc = ab, FristUtc = frist,
            Wertung = VersuchsWertung.Meilleur, LoesungenSichtbar = LoesungsSichtbarkeit.Immediat, Status = ZuweisungsStatus.Active
        };

    private static AntwortEingabe Wahl(Frage f, params string[] texte)
        => new() { Optionen = f.Optionen.Where(o => texte.Contains(o.Text)).Select(o => o.Id).ToList() };

    /// <summary>Abgeschlossener Versuch mit bewerteten Antworten (Engine) und optionaler manueller Korrektur offener Antworten.</summary>
    private static async Task VersuchAsync(SkoulouDbContext db, Zuweisung z, Guid schuelerId, Frage[] fragen, DateTime start,
        Dictionary<Guid, AntwortEingabe> eingaben, CancellationToken ct, (Guid Von, decimal Punkte, string Feedback)? korrektor = null, int dauer = 0)
    {
        var nr = await db.Versuche.CountAsync(v => v.ZuweisungId == z.Id && v.SchuelerId == schuelerId, ct) + 1;
        var abgabe = start.AddMinutes(dauer > 0 ? dauer - 5 : 12);
        var v = new Versuch
        {
            SchuleId = z.SchuleId, ZuweisungId = z.Id, SchuelerId = schuelerId, Nr = nr, StartUtc = start, AbgabeUtc = abgabe,
            DeadlineUtc = dauer > 0 ? PruefungsZeit.Deadline(start, dauer, z.FristUtc) : z.FristUtc, Status = VersuchsStatus.Soumis,
            FragenIds = fragen.Select(f => f.Id).ToList(), MaxPunkte = fragen.Sum(f => f.Punkte)
        };
        db.Versuche.Add(v);
        var antworten = new List<Antwort>();
        foreach (var f in fragen)
        {
            var eingabe = eingaben.GetValueOrDefault(f.Id) ?? AntwortEingabe.Leer;
            var e = BewertungsEngine.Bewerte(FragenAbbildung.Definition(f), eingabe);
            var a = new Antwort
            {
                VersuchId = v.Id, FrageId = f.Id, AntwortJson = eingabe.AlsJson(), Punkte = e.Punkte,
                Korrektur = e.BrauchtKorrektur ? KorrekturArt.Manuelle : KorrekturArt.Auto
            };
            if (e.BrauchtKorrektur && korrektor is { } k)
            {
                a.Punkte = k.Punkte;
                a.Feedback = k.Feedback;
                a.KorrigiertVonId = k.Von;
                a.KorrigiertAmUtc = abgabe.AddHours(5);
                db.Set<KorrekturEintrag>().Add(new KorrekturEintrag
                {
                    SchuleId = z.SchuleId, AntwortId = a.Id, VersuchId = v.Id, NeuePunkte = k.Punkte, Feedback = k.Feedback,
                    KorrigiertVonId = k.Von, ZeitUtc = abgabe.AddHours(5)
                });
            }
            antworten.Add(a);
        }
        db.Antworten.AddRange(antworten);
        VersuchsLogik.Aufsummieren(v, antworten);
        await db.SaveChangesAsync(ct);
        await VersuchsLogik.LernStandAsync(db, z, schuelerId, abgabe, ct);
    }

    /// <summary>Kleiner Baukasten für Blöcke und Fragen (wie der Editor sie speichert).</summary>
    private sealed class Bauer(SkoulouDbContext db, Guid schuleId)
    {
        public AufgabenBlock Block(Guid? leconId, Guid? pruefungId, int nr, string? titel, string? consigne)
        {
            var b = new AufgabenBlock { SchuleId = schuleId, LeconId = leconId, PruefungId = pruefungId, Nr = nr, Titel = titel, Consigne = consigne };
            db.AufgabenBloecke.Add(b);
            return b;
        }

        private Frage Neu(AufgabenBlock b, int nr, FragenTyp typ, string html, decimal punkte)
        {
            var f = new Frage { SchuleId = schuleId, BlockId = b.Id, Nr = nr, Typ = typ, TextHtml = html, Punkte = punkte };
            db.Fragen.Add(f);
            return f;
        }

        public Frage Auswahl(AufgabenBlock b, int nr, FragenTyp typ, string html, decimal punkte, params (string Text, bool Richtig)[] optionen)
            => Auswahl(b, nr, typ, html, punkte, optionen, null);

        public Frage Auswahl(AufgabenBlock b, int nr, FragenTyp typ, string html, decimal punkte, (string, bool) o1, (string, bool) o2, (string, bool) o3,
            string audio)
            => Auswahl(b, nr, typ, html, punkte, [o1, o2, o3], audio);

        private Frage Auswahl(AufgabenBlock b, int nr, FragenTyp typ, string html, decimal punkte, (string Text, bool Richtig)[] optionen, string? audio)
        {
            var f = Neu(b, nr, typ, html, punkte);
            f.AudioText = audio;
            f.Optionen = optionen.Select((o, i) => new FrageOption { FrageId = f.Id, Nr = i + 1, Text = o.Text, Richtig = o.Richtig }).ToList();
            return f;
        }

        public Frage Text(AufgabenBlock b, int nr, FragenTyp typ, string html, decimal punkte, List<string> antworten,
            List<string>? wortbank = null, string? audio = null)
        {
            var f = Neu(b, nr, typ, html, punkte);
            f.AkzeptierteAntworten = antworten;
            f.Wortbank = wortbank ?? [];
            f.AudioText = audio;
            return f;
        }

        public Frage Luecken(AufgabenBlock b, int nr, string text, decimal punkte, List<List<string>> luecken)
        {
            var f = Neu(b, nr, FragenTyp.TexteATrous, HtmlBereiniger.TextAlsHtml(text), punkte);
            f.LueckenJson = FragenAbbildung.LueckenJson(luecken.Select(l => (IReadOnlyList<string>)l).ToList());
            return f;
        }
    }
}
