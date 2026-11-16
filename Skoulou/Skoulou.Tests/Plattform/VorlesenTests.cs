using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Paedagogik;
using Skoulou.Core.Rechte;
using Skoulou.Tests.Paedagogik;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Medien;
using Skoulou.WebApp.Services.Infrastruktur.Vorlesen;
using Skoulou.WebApp.Services.Paedagogik;

namespace Skoulou.Tests.Plattform;

/// <summary>Vorlesen der Audio-Aufgaben: Regeln, Niveau → Tempo, Azure-Dienst mit Zwischenspeicher und Rückfall, Auslieferung.</summary>
public class VorlesenTests
{
    private static readonly XNamespace Ssml = "http://www.w3.org/2001/10/synthesis";
    private static readonly string StimmeFr = VorleseRegeln.Stimme("fr-FR");

    // ---------- Stimmen und Sprachen ----------

    [Fact]
    public void Jede_Sprache_der_Liste_hat_eine_passende_Neural_Stimme()
    {
        foreach (var (code, _) in FragenTypen.Sprachen)
        {
            var stimme = VorleseRegeln.Stimme(code);
            Assert.StartsWith(code + "-", stimme);
            Assert.EndsWith("Neural", stimme);
            Assert.True(VorleseRegeln.IstStimmenName(stimme));
        }
        Assert.Equal(FragenTypen.Sprachen.Count, FragenTypen.Sprachen.Select(s => s.Code[..2]).Distinct().Count());
    }

    [Theory]
    [InlineData("fr-FR", "fr-FR-VivienneMultilingualNeural")]
    [InlineData("en-US", "en-US-AvaNeural")]
    [InlineData("de-DE", "de-DE-KatjaNeural")]
    [InlineData("es-ES", "es-ES-ElviraNeural")]
    [InlineData("it-IT", "it-IT-ElsaNeural")]
    [InlineData("pt-PT", "pt-PT-RaquelNeural")]
    [InlineData("ar-SA", "ar-SA-ZariyahNeural")]
    [InlineData("zh-CN", "zh-CN-XiaoxiaoNeural")]
    public void Stimme_je_Sprache(string sprache, string stimme) => Assert.Equal(stimme, VorleseRegeln.Stimme(sprache));

    [Theory]
    [InlineData(null, "fr-FR")]
    [InlineData("", "fr-FR")]
    [InlineData("en-US", "en-US")]
    [InlineData("EN-us", "en-US")]
    [InlineData("en-GB", "en-US")]
    [InlineData("zh-TW", "zh-CN")]
    [InlineData("fr-FR/A2", "fr-FR")]
    [InlineData("ru-RU", "fr-FR")]
    [InlineData("x", "fr-FR")]
    public void Sprache_faellt_auf_eine_unterstuetzte_zurueck(string? eingabe, string erwartet)
        => Assert.Equal(erwartet, VorleseRegeln.Sprache(eingabe));

    [Fact]
    public void Eigene_Stimme_aus_der_Konfiguration_nur_wenn_gueltig()
    {
        var eigene = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = "en-GB-SoniaNeural",
            ["de"] = "<speak>böse</speak>",
            ["es"] = ""
        };
        Assert.Equal("en-GB-SoniaNeural", VorleseRegeln.Stimme("en-US", eigene));
        Assert.Equal("de-DE-KatjaNeural", VorleseRegeln.Stimme("de-DE", eigene));
        Assert.Equal("es-ES-ElviraNeural", VorleseRegeln.Stimme("es-ES", eigene));
        Assert.Equal(StimmeFr, VorleseRegeln.Stimme("fr-FR", eigene));
    }

    // ---------- Niveau → Tempo ----------

    [Theory]
    [InlineData("A1", false, -35)]
    [InlineData("A2", false, -25)]
    [InlineData("B1", false, -15)]
    [InlineData("B2", false, -5)]
    [InlineData("C1", false, 0)]
    [InlineData("C2", false, 5)]
    [InlineData("A1", true, -48)]
    [InlineData("A2", true, -40)]
    [InlineData("B1", true, -32)]
    [InlineData("B2", true, -24)]
    [InlineData("C1", true, -20)]
    [InlineData("C2", true, -16)]
    [InlineData(null, false, -15)]
    [InlineData("zz", true, -32)]
    public void Niveau_bestimmt_das_Tempo_und_Plus_lentement_verlangsamt(string? niveau, bool langsam, int prozent)
    {
        Assert.Equal(prozent, SprechNiveaus.Prozent(niveau, langsam));
        Assert.Equal(1 + prozent / 100.0, SprechNiveaus.Faktor(niveau, langsam), 6);
        Assert.InRange(SprechNiveaus.Prozent(niveau, langsam), SprechNiveaus.MinProzent, 100);
    }

    [Fact]
    public void Niveaus_werden_langsamer_je_tiefer_und_der_Standard_ist_B1()
    {
        var tempi = SprechNiveaus.Alle.Select(n => n.Prozent).ToList();
        Assert.Equal(tempi.Order(), tempi);
        Assert.Equal(["A1", "A2", "B1", "B2", "C1", "C2"], SprechNiveaus.Alle.Select(n => n.Code));
        Assert.Equal("B1", SprechNiveaus.Standard);
        Assert.Equal("A2", SprechNiveaus.Normiert(" a2 "));
        Assert.Equal("B1", SprechNiveaus.Normiert(""));
    }

    [Theory]
    [InlineData(-35, "-35%")]
    [InlineData(5, "+5%")]
    [InlineData(0, "0%")]
    public void Rate_als_SSML_Wert(int prozent, string erwartet) => Assert.Equal(erwartet, VorleseRegeln.Rate(prozent));

    [Fact]
    public void Speicherform_im_Feld_Sprache_alte_Aufgaben_gelten_als_Standard()
    {
        Assert.Equal("fr-FR", SprechNiveaus.Verbinden("fr-FR", "B1"));
        Assert.Equal("fr-FR", SprechNiveaus.Verbinden("fr-FR", null));
        Assert.Equal("en-US/A2", SprechNiveaus.Verbinden("en-US", "a2"));
        Assert.Equal(("en-US", "A2"), SprechNiveaus.Trennen("en-US/A2"));
        Assert.Equal(("fr-FR", "B1"), SprechNiveaus.Trennen("fr-FR"));
        Assert.Equal(("fr-FR", "B1"), SprechNiveaus.Trennen(null));
        Assert.Equal(("de-DE", "B1"), SprechNiveaus.Trennen("de-DE/ZZ"));
        // Jede Kombination passt in die Spalte (nvarchar(10)) und lässt sich verlustfrei zurücklesen.
        foreach (var (code, _) in FragenTypen.Sprachen)
            foreach (var (niveau, _, _) in SprechNiveaus.Alle)
            {
                var gespeichert = SprechNiveaus.Verbinden(code, niveau);
                Assert.True(gespeichert.Length <= 10, gespeichert);
                Assert.Equal((code, niveau), SprechNiveaus.Trennen(gespeichert));
            }
    }

    // ---------- SSML, Text, Schlüssel ----------

    [Fact]
    public void Ssml_ist_gueltiges_XML_maskiert_den_Text_und_setzt_Stimme_Sprache_Tempo()
    {
        var text = "Tom & Jerry <script> \"oui\" l'été";
        var doc = XDocument.Parse(VorleseRegeln.Ssml(text, "fr-FR", StimmeFr, -35));
        Assert.Equal(Ssml + "speak", doc.Root!.Name);
        Assert.Equal("fr-FR", (string?)doc.Root.Attribute(XNamespace.Xml + "lang"));
        var stimme = doc.Root.Element(Ssml + "voice")!;
        Assert.Equal(StimmeFr, (string?)stimme.Attribute("name"));
        // Mehrsprachige Stimme: Sprache ausdrücklich (sonst rät sie bei kurzen Wörtern).
        var lang = stimme.Element(Ssml + "lang")!;
        Assert.Equal("fr-FR", (string?)lang.Attribute(XNamespace.Xml + "lang"));
        var prosody = lang.Element(Ssml + "prosody")!;
        Assert.Equal("-35%", (string?)prosody.Attribute("rate"));
        Assert.Equal(text, prosody.Value);
    }

    [Fact]
    public void Ssml_natuerliches_Tempo_ohne_prosody_und_einsprachige_Stimme_ohne_lang()
    {
        var doc = XDocument.Parse(VorleseRegeln.Ssml("Hello", "en-US", "en-US-AvaNeural", 0));
        Assert.Empty(doc.Descendants(Ssml + "prosody"));
        Assert.Empty(doc.Descendants(Ssml + "lang"));
        Assert.Equal("Hello", doc.Root!.Element(Ssml + "voice")!.Value);
        Assert.Equal("+5%", (string?)XDocument.Parse(VorleseRegeln.Ssml("Hi", "en-US", "en-US-AvaNeural", 5))
            .Descendants(Ssml + "prosody").Single().Attribute("rate"));
    }

    [Fact]
    public void Normalisieren_vereinheitlicht_Leerraum_und_entfernt_ungueltige_Zeichen()
    {
        Assert.Equal("Le chat dort.", VorleseRegeln.Normalisieren("  Le  chat\r\n\tdort.\u0001 "));
        Assert.Equal("été", VorleseRegeln.Normalisieren("été"));
        Assert.Equal("Bonjour 😀 !", VorleseRegeln.Normalisieren("Bonjour 😀 !"));
        Assert.Equal("ab", VorleseRegeln.Normalisieren("a\uD800b"));
        Assert.Equal(string.Empty, VorleseRegeln.Normalisieren(null));
        Assert.Equal(string.Empty, VorleseRegeln.Normalisieren(" \n "));
        Assert.Equal("汉语", VorleseRegeln.Normalisieren("汉语"));
    }

    [Fact]
    public void Schluessel_ist_stabil_und_unterscheidet_Text_Sprache_Stimme_Niveau_und_Tempo()
    {
        var a = VorleseRegeln.Schluessel("Le chat", "fr-FR", StimmeFr, "B1", false);
        Assert.True(VorleseRegeln.IstSchluessel(a));
        Assert.Equal(a, VorleseRegeln.Schluessel("Le chat", "fr-FR", StimmeFr, "b1", false));
        Assert.Equal(a, VorleseRegeln.Schluessel("Le chat", "fr-FR", StimmeFr, null!, false));
        string[] andere =
        [
            VorleseRegeln.Schluessel("Le chien", "fr-FR", StimmeFr, "B1", false),
            VorleseRegeln.Schluessel("Le chat", "en-US", StimmeFr, "B1", false),
            VorleseRegeln.Schluessel("Le chat", "fr-FR", "fr-FR-DeniseNeural", "B1", false),
            VorleseRegeln.Schluessel("Le chat", "fr-FR", StimmeFr, "A1", false),
            VorleseRegeln.Schluessel("Le chat", "fr-FR", StimmeFr, "C2", false),
            VorleseRegeln.Schluessel("Le chat", "fr-FR", StimmeFr, "B1", true)
        ];
        Assert.DoesNotContain(a, andere);
        Assert.Equal(andere.Length, andere.Distinct().Count());
    }

    [Fact]
    public void Schluessel_Pfad_und_Adresse_lassen_keine_Manipulation_zu()
    {
        var s = VorleseRegeln.Schluessel("x", "fr-FR", StimmeFr, "B1", false);
        Assert.False(VorleseRegeln.IstSchluessel(s.ToUpperInvariant()));
        Assert.False(VorleseRegeln.IstSchluessel("../" + s[3..]));
        Assert.False(VorleseRegeln.IstSchluessel(s[..63]));
        Assert.False(VorleseRegeln.IstSchluessel(null));

        var schule = Guid.NewGuid();
        var pfad = VorleseRegeln.Pfad(schule, s);
        Assert.Equal($"schulen/{schule:N}/vorlesen/{s}", pfad);
        Assert.True(MedienPfad.IstGueltig(pfad));
        Assert.Equal($"plattform/vorlesen/{s}", VorleseRegeln.Pfad(null, s));
        Assert.Equal("/vorlesen/" + s, VorleseRegeln.Adresse(s));
    }

    [Theory]
    [InlineData("k", "westeurope", true)]
    [InlineData("k", " WestEurope ", true)]
    [InlineData("k", "evil.com/x", false)]
    [InlineData("k", "", false)]
    [InlineData("", "westeurope", false)]
    [InlineData(null, null, false)]
    public void Bereit_nur_mit_Key_und_gueltiger_Region(string? key, string? region, bool bereit)
        => Assert.Equal(bereit, new SprachausgabeOptionen { Key = key, Region = region }.IstBereit);

    // ---------- Dienst: Zwischenspeicher, Rückfall, Grenze ----------

    private static readonly Guid Konto = Guid.NewGuid();
    private static readonly Guid Schule = Guid.NewGuid();

    private static SprachausgabeOptionen Bereit() => new() { Key = "test-key", Region = "westeurope" };

    private static VorleseDienst Dienst(AzureImTest http, SpeicherImTest speicher, TestZeit? zeit = null, SprachausgabeOptionen? o = null)
        => new(http, new TestOptionen<SprachausgabeOptionen>(o ?? Bereit()), speicher, new MemoryCache(new MemoryCacheOptions()),
            zeit ?? new TestZeit(), NullLogger<VorleseDienst>.Instance);

    [Fact]
    public async Task Ohne_Konfiguration_liest_der_Browser_ohne_Anfrage_an_Azure()
    {
        var http = new AzureImTest();
        foreach (var o in new[] { new SprachausgabeOptionen(), new SprachausgabeOptionen { Key = "k" }, new SprachausgabeOptionen { Key = "k", Region = "a.b" } })
            Assert.Null(await Dienst(http, new SpeicherImTest(), o: o).AdresseAsync(Konto, Schule, "Bonjour", "fr-FR", "B1", false));
        Assert.Empty(http.Anfragen);
    }

    [Fact]
    public async Task Erzeugt_einmal_legt_im_Speicher_ab_und_liefert_danach_aus_dem_Zwischenspeicher()
    {
        var http = new AzureImTest();
        var speicher = new SpeicherImTest();
        var dienst = Dienst(http, speicher);

        var adresse = await dienst.AdresseAsync(Konto, Schule, " Le chat  dort. ", "fr-FR", "A2", false);
        var schluessel = VorleseRegeln.Schluessel("Le chat dort.", "fr-FR", StimmeFr, "A2", false);
        Assert.Equal(VorleseRegeln.Adresse(schluessel), adresse);

        var anfrage = Assert.Single(http.Anfragen);
        Assert.Equal("https://westeurope.tts.speech.microsoft.com/cognitiveservices/v1", anfrage.Url);
        Assert.Equal("test-key", anfrage.Key);
        Assert.Equal(VorleseRegeln.Format, anfrage.Format);
        Assert.Equal("application/ssml+xml", anfrage.Typ);
        Assert.Contains("rate=\"-25%\"", anfrage.Ssml);
        Assert.Contains(">Le chat dort.<", anfrage.Ssml);

        var datei = speicher.Dateien[VorleseRegeln.Pfad(Schule, schluessel)];
        Assert.Equal(AzureImTest.Mp3, datei.Daten);
        Assert.Equal("audio/mpeg", datei.Typ);

        // Noch einmal: aus dem Zwischenspeicher, auch nach einem Neustart (frischer Dienst, gleicher Speicher).
        Assert.Equal(adresse, await dienst.AdresseAsync(Konto, Schule, "Le chat dort.", "fr-FR", "A2", false));
        Assert.Equal(adresse, await Dienst(http, speicher).AdresseAsync(Guid.NewGuid(), Schule, "Le chat dort.", "fr-FR", "a2", false));
        Assert.Single(http.Anfragen);

        // Andere Schule, anderes Niveau, « Plus lentement »: je eine eigene Fassung.
        Assert.Equal(adresse, await dienst.AdresseAsync(Konto, Guid.NewGuid(), "Le chat dort.", "fr-FR", "A2", false));
        Assert.NotEqual(adresse, await dienst.AdresseAsync(Konto, Schule, "Le chat dort.", "fr-FR", "C1", false));
        Assert.NotEqual(adresse, await dienst.AdresseAsync(Konto, Schule, "Le chat dort.", "fr-FR", "A2", true));
        Assert.Equal(4, http.Anfragen.Count);
        Assert.DoesNotContain("prosody", http.Anfragen[2].Ssml);
        Assert.Contains("rate=\"-40%\"", http.Anfragen[3].Ssml);
    }

    [Fact]
    public async Task Gleichzeitige_Klicks_loesen_nur_eine_Anfrage_aus()
    {
        var http = new AzureImTest { Sperre = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var dienst = Dienst(http, new SpeicherImTest());
        var erster = dienst.AdresseAsync(Konto, Schule, "Diktat", "de-DE", "B1", false);
        var zweiter = dienst.AdresseAsync(Guid.NewGuid(), Schule, "Diktat", "de-DE", "B1", false);
        http.Sperre.SetResult();
        var adressen = await Task.WhenAll(erster, zweiter);
        Assert.NotNull(adressen[0]);
        Assert.Equal(adressen[0], adressen[1]);
        Assert.Single(http.Anfragen);
    }

    [Fact]
    public async Task Fehler_von_Azure_Browser_Stimme_und_kurze_Pause_ohne_weitere_Anfragen()
    {
        var zeit = new TestZeit();
        var http = new AzureImTest { Status = HttpStatusCode.Unauthorized };
        var speicher = new SpeicherImTest();
        var dienst = Dienst(http, speicher, zeit);

        Assert.Null(await dienst.AdresseAsync(Konto, Schule, "Bonjour", "fr-FR", "B1", false));
        Assert.Empty(speicher.Dateien);
        Assert.Null(await dienst.AdresseAsync(Konto, Schule, "Bonsoir", "fr-FR", "B1", false));
        Assert.Single(http.Anfragen);

        zeit.Vor(VorleseDienst.PauseNachFehler + TimeSpan.FromSeconds(1));
        http.Status = HttpStatusCode.OK;
        Assert.NotNull(await dienst.AdresseAsync(Konto, Schule, "Bonsoir", "fr-FR", "B1", false));
        Assert.Equal(2, http.Anfragen.Count);
    }

    [Fact]
    public async Task Netzfehler_und_Zeitueberschreitung_Browser_Stimme()
    {
        foreach (var fehler in new Exception[] { new HttpRequestException("hors ligne"), new TaskCanceledException("délai") })
        {
            var http = new AzureImTest { Ausnahme = fehler };
            Assert.Null(await Dienst(http, new SpeicherImTest()).AdresseAsync(Konto, Schule, "Hola", "es-ES", "B1", false));
            Assert.Single(http.Anfragen);
        }
    }

    [Fact]
    public async Task Speicherfehler_Browser_Stimme()
    {
        var http = new AzureImTest();
        Assert.Null(await Dienst(http, new SpeicherImTest { LesenFehler = true }).AdresseAsync(Konto, Schule, "Ciao", "it-IT", "B1", false));
        Assert.Empty(http.Anfragen);

        Assert.Null(await Dienst(http, new SpeicherImTest { SchreibenFehler = true }).AdresseAsync(Konto, Schule, "Ciao", "it-IT", "B1", false));
        Assert.Single(http.Anfragen);
    }

    [Fact]
    public async Task Leerer_oder_zu_langer_Text_Browser_Stimme_ohne_Anfrage()
    {
        var http = new AzureImTest();
        var dienst = Dienst(http, new SpeicherImTest());
        Assert.Null(await dienst.AdresseAsync(Konto, Schule, "   ", "fr-FR", "B1", false));
        Assert.Null(await dienst.AdresseAsync(Konto, Schule, null, "fr-FR", "B1", false));
        Assert.Null(await dienst.AdresseAsync(Konto, Schule, new string('a', VorleseRegeln.MaxZeichen + 1), "fr-FR", "B1", false));
        Assert.Empty(http.Anfragen);
        Assert.NotNull(await dienst.AdresseAsync(Konto, Schule, new string('a', VorleseRegeln.MaxZeichen), "fr-FR", "B1", false));
    }

    [Fact]
    public async Task Grenze_je_Konto_Treffer_im_Zwischenspeicher_zaehlen_nicht()
    {
        var http = new AzureImTest();
        var dienst = Dienst(http, new SpeicherImTest());
        var grenze = RateLimits.VorleseSynthesenJeKontoJeZehnMinuten;
        for (var i = 0; i < grenze; i++)
            Assert.NotNull(await dienst.AdresseAsync(Konto, Schule, $"Phrase {i}", "fr-FR", "B1", false));
        Assert.Null(await dienst.AdresseAsync(Konto, Schule, "Une de trop", "fr-FR", "B1", false));
        Assert.Equal(grenze, http.Anfragen.Count);

        Assert.NotNull(await dienst.AdresseAsync(Konto, Schule, "Phrase 0", "fr-FR", "B1", false));
        Assert.NotNull(await dienst.AdresseAsync(Guid.NewGuid(), Schule, "Une de trop", "fr-FR", "B1", false));
        Assert.Equal(grenze + 1, http.Anfragen.Count);
    }

    // ---------- Auslieferung ----------

    [Fact]
    public async Task Auslieferung_nur_angemeldet_aktiv_und_aus_der_eigenen_Schule()
    {
        await using var env = new TestUmgebung();
        await env.SeedAsync();
        var schuleA = await env.SchuleAsync("Collège A");
        var schuleB = await env.SchuleAsync("Collège B");
        var gesperrteSchule = await env.SchuleAsync("Collège C", SchulStatus.Suspendue);
        var eleveA = await env.KontoAsync("eleve.a", Rollen.Schueler, schuleA.Id);
        var eleveB = await env.KontoAsync("eleve.b", Rollen.Schueler, schuleB.Id);
        var blockiert = await env.KontoAsync("eleve.c", Rollen.Schueler, schuleA.Id, KontoStatus.Bloque);
        var eleveC = await env.KontoAsync("eleve.d", Rollen.Schueler, gesperrteSchule.Id);
        var zugriff = env.Hole<Zugriff>();
        var speicher = new SpeicherImTest();
        var schluessel = VorleseRegeln.Schluessel("Le chat dort.", "fr-FR", StimmeFr, "B1", false);
        foreach (var s in new[] { schuleA.Id, gesperrteSchule.Id })
            await speicher.SpeichernAsync(VorleseRegeln.Pfad(s, schluessel), new MemoryStream(AzureImTest.Mp3), VorleseRegeln.ContentType);

        async Task<(IResult Ergebnis, HttpContext Http)> Abruf(Guid? konto, string s)
        {
            var http = new DefaultHttpContext();
            if (konto is { } k) http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, k.ToString())], "Test"));
            return (await VorleseEndpunkt.AusliefernAsync(s, http, zugriff, speicher, default), http);
        }

        Assert.IsType<UnauthorizedHttpResult>((await Abruf(null, schluessel)).Ergebnis);
        var (ergebnis, kontext) = await Abruf(eleveA.Id, schluessel);
        var datei = Assert.IsType<FileStreamHttpResult>(ergebnis);
        Assert.Equal("audio/mpeg", datei.ContentType);
        Assert.True(datei.EnableRangeProcessing);
        Assert.Equal($"\"{schluessel}\"", datei.EntityTag?.Tag.ToString());
        Assert.StartsWith("private", kontext.Response.Headers.CacheControl.ToString());

        Assert.IsType<NotFound>((await Abruf(eleveB.Id, schluessel)).Ergebnis);
        Assert.IsType<NotFound>((await Abruf(blockiert.Id, schluessel)).Ergebnis);
        Assert.IsType<NotFound>((await Abruf(eleveC.Id, schluessel)).Ergebnis);
        Assert.IsType<NotFound>((await Abruf(Guid.NewGuid(), schluessel)).Ergebnis);
        Assert.IsType<NotFound>((await Abruf(eleveA.Id, schluessel.ToUpperInvariant())).Ergebnis);
        Assert.IsType<NotFound>((await Abruf(eleveA.Id, VorleseRegeln.Schluessel("autre", "fr-FR", StimmeFr, "B1", false))).Ergebnis);
    }

    // ---------- Speicherung im Editor (ohne Schemaänderung) ----------

    [Fact]
    public async Task Sprache_und_Niveau_im_Feld_Sprache_gespeichert_und_beim_Schueler_getrennt()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var englisch = new FrageFormular
        {
            Typ = FragenTyp.AudioTexte, TextHtml = "<p>Dictation</p>", Punkte = 1, AudioText = "The cat sleeps",
            Sprache = "en-US", Niveau = "A2", Varianten = ["The cat sleeps"]
        };
        var standard = new FrageFormular
        {
            Typ = FragenTyp.AudioChoixUnique, TextHtml = "<p>Écoutez</p>", Punkte = 1, AudioText = "marché", Sprache = "fr-FR",
            Optionen = [new() { Text = "marché", Richtig = true }, new() { Text = "mardi" }]
        };
        var (inhalt, z) = await u.ZugewiesenAsync(u.Formular(InhaltsArt.Lecon, "Écoute", englisch, standard));

        await using (var db = await u.Env.Db.CreateDbContextAsync())
            Assert.Equal(["en-US/A2", "fr-FR"], await db.Fragen.OrderBy(f => f.Nr).Select(f => f.Sprache).ToListAsync());

        var fragen = (await u.Inhalte.LadeAsync(u.Lehrer.Id, InhaltsArt.Lecon, inhalt)).Bloecke[0].Fragen;
        Assert.Equal(("en-US", "A2"), (fragen[0].Sprache, fragen[0].Niveau));
        Assert.Equal(("fr-FR", "B1"), (fragen[1].Sprache, fragen[1].Niveau));

        var schueler = await u.FragenAsync(u.SchuelerA.Id, z);
        Assert.Equal(new VorleseText("The cat sleeps", "en-US", "A2", u.Schule), await u.Lernen.AudioTextAsync(u.SchuelerA.Id, z, schueler[0].Id));
        Assert.Equal(new VorleseText("marché", "fr-FR", "B1", u.Schule), await u.Lernen.AudioTextAsync(u.SchuelerA.Id, z, schueler[1].Id));

        // « Écouter » im Editor: nur wer Inhalte schreibt; Ablage in der eigenen Schule.
        Assert.Equal(u.Schule, await u.Inhalte.VorleseSchuleAsync(u.Lehrer.Id));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Inhalte.VorleseSchuleAsync(u.SchuelerA.Id));
    }
}

/// <summary>Medienspeicher im Arbeitsspeicher; Lese- bzw. Schreibfehler einschaltbar.</summary>
internal sealed class SpeicherImTest : IMedienSpeicher
{
    public ConcurrentDictionary<string, (byte[] Daten, string Typ)> Dateien { get; } = new();
    public bool LesenFehler { get; set; }
    public bool SchreibenFehler { get; set; }

    public async Task<long> SpeichernAsync(string blobName, Stream inhalt, string contentType, CancellationToken ct = default)
    {
        if (SchreibenFehler) throw new IOException("Écriture impossible.");
        Assert.True(MedienPfad.IstGueltig(blobName));
        using var puffer = new MemoryStream();
        await inhalt.CopyToAsync(puffer, ct);
        Dateien[blobName] = (puffer.ToArray(), contentType);
        return puffer.Length;
    }

    public Task<Stream?> OeffnenAsync(string blobName, CancellationToken ct = default)
    {
        if (LesenFehler) throw new IOException("Lecture impossible.");
        return Task.FromResult<Stream?>(Dateien.TryGetValue(blobName, out var d) ? new MemoryStream(d.Daten, writable: false) : null);
    }

    public Task<bool> LoeschenAsync(string blobName, CancellationToken ct = default) => Task.FromResult(Dateien.TryRemove(blobName, out _));
}

/// <summary>Azure-Sprachausgabe ohne Netz: Status, Ausnahme und Sperre (gleichzeitige Klicks) steuerbar; Anfragen werden mitgeschrieben.</summary>
internal sealed class AzureImTest : HttpMessageHandler, IHttpClientFactory
{
    public sealed record Anfrage(string Url, string? Key, string? Format, string? Typ, string Ssml);

    public static readonly byte[] Mp3 = [0xFF, 0xF3, 0x44, 0xC4, 0x00, 0x01, 0x02];

    public List<Anfrage> Anfragen { get; } = [];
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public Exception? Ausnahme { get; set; }
    public TaskCompletionSource? Sperre { get; set; }

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var ssml = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        static string? Kopf(System.Net.Http.Headers.HttpHeaders h, string name) => h.TryGetValues(name, out var w) ? string.Join(",", w) : null;
        lock (Anfragen)
            Anfragen.Add(new Anfrage(request.RequestUri!.ToString(), Kopf(request.Headers, "Ocp-Apim-Subscription-Key"),
                Kopf(request.Headers, "X-Microsoft-OutputFormat"), request.Content?.Headers.ContentType?.MediaType, ssml));
        if (Sperre is { } sperre) await sperre.Task;
        if (Ausnahme is { } fehler) throw fehler;
        return new HttpResponseMessage(Status) { Content = new ByteArrayContent(Status == HttpStatusCode.OK ? Mp3 : []) };
    }
}
