using System.Security.Claims;
using System.Text.RegularExpressions;
using Anthropic.Models.Messages;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using ENote.WebApp.Services.Formation;
using ENote.WebApp.Services.KiHilfe;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// KI-Hilfe « Poser une question » (19.11.2026): Dienst mit Fake-Client (nie echte Aufrufe), Kontingente, Datenschutz
/// (Frage nie im Log), Wissensbasis je Rolle ohne fremde Rollen, Anfrage-Parameter und Unsichtbarkeit ohne Schlüssel.
/// </summary>
public class KiHilfeTests
{
    private static readonly Guid KontoA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid KontoB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly KiRolle Besitzer = new(AppRollen.Besitzer);
    private static readonly KiRolle Employe = new(AppRollen.Kassierer);

    // ---------------- Dienst ----------------

    [Fact]
    public async Task Leere_und_zu_lange_Fragen_gehen_nicht_an_den_Dienst()
    {
        var w = new Welt();
        Assert.Equal(KiHilfeDienst.MeldungLeer, (await w.Dienst.FrageAsync(KontoA, Besitzer, " \n ")).Meldung);
        var lang = await w.Dienst.FrageAsync(KontoA, Besitzer, new string('a', KiHilfeOptionen.MaxFrageZeichen + 1));
        Assert.Equal(KiHilfeDienst.MeldungZuLang, lang.Meldung);
        Assert.Empty(w.Client.Anfragen);
        Assert.Equal(KiHilfeOptionen.FragenJeKontoUndTag, w.Dienst.Verbleibend(KontoA));
        Assert.True((await w.Dienst.FrageAsync(KontoA, Besitzer, new string('a', KiHilfeOptionen.MaxFrageZeichen))).Erfolg);
    }

    [Fact]
    public async Task Tagesgrenze_je_Konto_und_der_App()
    {
        var w = new Welt();
        for (var i = 0; i < KiHilfeOptionen.FragenJeKontoUndTag; i++)
            Assert.True((await w.Dienst.FrageAsync(KontoA, Besitzer, $"Question {i}")).Erfolg);
        var zuViel = await w.Dienst.FrageAsync(KontoA, Besitzer, "Encore ?");
        Assert.Equal(KiHilfeDienst.MeldungKontoGrenze, zuViel.Meldung);
        Assert.Equal(0, zuViel.Verbleibend);
        Assert.True((await w.Dienst.FrageAsync(KontoB, Besitzer, "Et moi ?")).Erfolg);
        w.Zeit.Jetzt += TimeSpan.FromDays(1);
        Assert.Equal(KiHilfeOptionen.FragenJeKontoUndTag, w.Dienst.Verbleibend(KontoA));

        var app = new Welt(o => o.FragenJeTag = 1);
        Assert.True((await app.Dienst.FrageAsync(KontoA, Besitzer, "Une")).Erfolg);
        Assert.Equal(KiHilfeDienst.MeldungAppGrenze, (await app.Dienst.FrageAsync(KontoB, Besitzer, "Deux")).Meldung);
        Assert.Single(app.Client.Anfragen);
    }

    [Fact]
    public async Task Fehler_des_Dienstes_freundliche_Meldung_und_Frage_zaehlt_nicht()
    {
        foreach (var fehler in new Exception[] { new KiDienstFehler(KiFehlerArt.Ueberlastet, "429"), new KiDienstFehler(KiFehlerArt.Konfiguration, "401"), new HttpRequestException("x") })
        {
            var w = new Welt { Fehler = fehler };
            var e = await w.Dienst.FrageAsync(KontoA, Besitzer, "Où sont les finances ?");
            Assert.False(e.Erfolg);
            Assert.Equal(KiHilfeDienst.MeldungIndisponible, e.Meldung);
            Assert.Equal(KiHilfeOptionen.FragenJeKontoUndTag, w.Dienst.Verbleibend(KontoA));
        }
    }

    [Fact]
    public async Task Ablehnung_abgeschnitten_und_leer()
    {
        var w = new Welt { Stopp = KiStopp.Abgelehnt };
        var abgelehnt = await w.Dienst.FrageAsync(KontoA, Besitzer, "Écris mon business plan");
        Assert.Equal(KiHilfeDienst.MeldungAbgelehnt, abgelehnt.Meldung);
        Assert.Contains("eNOTE", abgelehnt.Meldung);
        Assert.DoesNotContain("SKOULOU", abgelehnt.Meldung);

        w.Stopp = KiStopp.Abgeschnitten;
        w.Antwort = "1. Ouvrez la caisse.\n2. Touchez";
        var ab = await w.Dienst.FrageAsync(KontoA, Besitzer, "Caisse ?");
        Assert.True(ab.Erfolg);
        Assert.EndsWith(" …", ab.Antwort);
        Assert.Equal(KiBlockArt.Schritte, Assert.Single(ab.Bloecke).Art);

        w.Stopp = KiStopp.Fertig;
        w.Antwort = "  ";
        Assert.Equal(KiHilfeDienst.MeldungOhneText, (await w.Dienst.FrageAsync(KontoA, Besitzer, "?")).Meldung);
    }

    [Fact]
    public async Task Passende_Videos_der_Rolle_werden_Knoepfe_fremde_nicht()
    {
        var eigenes = KiWissensbasis.Videos(Employe).First(v => v.Fuer == "Employé");
        var fremd = VideoKatalog.Videos.First(v => !v.Rollen.Contains(AppRollen.Kassierer));
        var w = new Welt { Antwort = $"Ouvrez la caisse.\nVidéo : « {eigenes.Titel} »\nAussi : « {fremd.Titel} »." };
        var e = await w.Dienst.FrageAsync(KontoA, Employe, "Encaisser ?");
        Assert.Equal([eigenes], e.Videos);
        Assert.DoesNotContain("Vidéo :", e.Antwort);
        Assert.Contains(fremd.Titel, e.Antwort);

        // Ohne Anführungszeichen ist ein Titel gewöhnlicher Text: « Vos clients » steht in vielen Sätzen.
        var kurz = KiWissensbasis.Videos(Besitzer).Single(v => v.Titel == "Vos clients");
        w.Antwort = "Ouvrez Vente › Clients pour voir vos clients.";
        Assert.Empty((await w.Dienst.FrageAsync(KontoA, Besitzer, "Clients ?")).Videos);
        w.Antwort = "Ouvrez Vente › Clients.\nVidéo : \"Vos clients\"";
        Assert.Equal([kurz], (await w.Dienst.FrageAsync(KontoA, Besitzer, "Clients ?")).Videos);
    }

    [Fact]
    public async Task An_den_Dienst_gehen_nur_Hilfetext_Rolle_und_Frage_nie_ins_Log()
    {
        var w = new Welt();
        await w.Dienst.FrageAsync(KontoA, Besitzer, "Le client <Ama Mensah> doit 5 000 F, où le noter ?");
        var a = Assert.Single(w.Client.Anfragen);
        Assert.Same(KiWissensbasis.SystemText(Besitzer), a.SystemText);
        Assert.Contains("Rôle de l'utilisateur : Propriétaire", a.NutzerText);
        Assert.Contains("‹Ama Mensah›", a.NutzerText);
        Assert.DoesNotContain(KontoA.ToString("N"), a.SystemText + a.NutzerText);
        Assert.NotEmpty(w.Log.Eintraege);
        Assert.All(w.Log.Eintraege, l => { Assert.DoesNotContain("Mensah", l); Assert.DoesNotContain("5 000", l); Assert.DoesNotContain(w.Antwort, l); });
    }

    [Fact]
    public async Task Ohne_Schluessel_unsichtbar_und_kein_Aufruf()
    {
        var w = new Welt(o => o.AnthropicApiKey = null);
        Assert.False(w.Dienst.IstVerfuegbar);
        Assert.Equal(KiHilfeDienst.MeldungIndisponible, (await w.Dienst.FrageAsync(KontoA, Besitzer, "Question")).Meldung);
        Assert.Empty(w.Client.Anfragen);

        var echt = new AnthropicKiClient(Options.Create(new KiHilfeOptionen()));
        Assert.Equal(KiFehlerArt.Konfiguration,
            (await Assert.ThrowsAsync<KiDienstFehler>(() => echt.FrageAsync(new KiAnfrage("s", "q"), CancellationToken.None))).Art);
    }

    // ---------------- Rolle ----------------

    private static ClaimsPrincipal Nutzer(string rolle) => new(new ClaimsIdentity([new Claim(ClaimTypes.Role, rolle)], "Test"));

    [Fact]
    public void Rolle_aus_Anmeldung_und_Rechten_des_Mitarbeiters()
    {
        var recht = new UserSalon { KannFinanzenSehen = true, KannRabatteVerwalten = true };
        Assert.Equal(new KiRolle(AppRollen.Kassierer, Remises: true, Finances: true), KiRolle.Aus(Nutzer(AppRollen.Kassierer), recht));
        Assert.Equal(new KiRolle(AppRollen.Besitzer), KiRolle.Aus(Nutzer(AppRollen.Besitzer), recht)); // Rechte nur für Mitarbeitende
        Assert.Equal(AppRollen.ProduktOwner, KiRolle.Aus(Nutzer(AppRollen.ProduktOwner), null).Rolle);
        Assert.Equal(AppRollen.SuperAdmin, KiRolle.Aus(Nutzer(AppRollen.SuperAdmin), null).Rolle);
        Assert.Equal("Employé (connexion personnelle)", KiRolle.Aus(Nutzer(AppRollen.Kassierer), null).Bezeichnung);
    }

    // ---------------- Wissensbasis ----------------

    private static IEnumerable<KiRolle> AlleRollen()
    {
        yield return new KiRolle(AppRollen.SuperAdmin);
        yield return new KiRolle(AppRollen.ProduktOwner);
        yield return Besitzer;
        yield return Employe;
        yield return new KiRolle(AppRollen.Kassierer, Prestations: true, Remises: true, Finances: true);
    }

    [Fact]
    public void Wissensbasis_je_Rolle_enthaelt_eigenes_Menue_und_ist_stabil()
    {
        foreach (var r in AlleRollen())
        {
            var text = KiWissensbasis.SystemText(r);
            Assert.StartsWith("Tu es l'assistant d'aide intégré à eNOTE", text);
            Assert.DoesNotContain("SKOULOU", text);
            Assert.Contains("rôle de l'utilisateur : " + r.Bezeichnung, text);
            foreach (var g in KiWissensbasis.Menue(r))
                foreach (var p in g.Punkte)
                    Assert.Contains("- " + (g.Titel is null ? p.Label : g.Titel + " › " + p.Label) + " : ", text);
            Assert.Same(text, KiWissensbasis.SystemText(r with { }));
            Assert.True(text.Length < 100_000, $"{r.Schluessel}: {text.Length} Zeichen");
        }
    }

    [Fact]
    public void Wissensbasis_enthaelt_keine_Menues_Videos_oder_Lektionen_anderer_Rollen()
    {
        static List<string> Ziele(KiRolle r) => [.. KiWissensbasis.Menue(r).SelectMany(g => g.Punkte).Select(p => p.Href)];

        // Mitarbeiterkonto: nie Kasse, « Clients présents », Équipe, Compte oder Plattform; nur die Bereiche seiner Rechte.
        Assert.Equal(["/", "/aide/videos", "/contact"], Ziele(Employe));
        Assert.Equal(["/", "/services", "/remises", "/finances", "/ausgaben", "/aide/videos", "/contact"],
            Ziele(new KiRolle(AppRollen.Kassierer, true, true, true)));
        Assert.Equal(["/", "/finances", "/ausgaben", "/aide/videos", "/contact"], Ziele(new KiRolle(AppRollen.Kassierer, Finances: true)));
        Assert.DoesNotContain(KiWissensbasis.Lektionen(Employe), l => l.Id is "chiffres" or "catalogue" or "equipe" or "abonnement");
        Assert.Contains(KiWissensbasis.Lektionen(Employe), l => l.Id == KiWissensbasis.LektionCaisse);

        // Propriétaire: kein Plattform-Menü, keine Lektionen der Responsables oder Administratoren.
        var besitzer = KiWissensbasis.SystemText(Besitzer);
        Assert.DoesNotContain(Ziele(Besitzer), h => h.StartsWith("/admin") || h is "/formation" or "/mes-etablissements");
        Assert.DoesNotContain("Journaux", besitzer);
        Assert.DoesNotContain("Leçon « Vos commissions »", besitzer);
        Assert.DoesNotContain("Leçon « Votre rôle de responsable »", besitzer);
        foreach (var l in FormationKatalog.AdminLektionen) Assert.DoesNotContain("Leçon « " + l.Titel + " »", besitzer);

        // Responsable: keine Admin-Seiten und keine Admin-Lektionen.
        var po = KiWissensbasis.SystemText(new KiRolle(AppRollen.ProduktOwner));
        Assert.DoesNotContain(Ziele(new KiRolle(AppRollen.ProduktOwner)), h => h is "/admin/journaux" or "/admin/emails" or "/admin/annonces");
        foreach (var l in FormationKatalog.AdminLektionen) Assert.DoesNotContain("Leçon « " + l.Titel + " »", po);
        Assert.Contains("Leçon « Vos commissions »", po);

        // Videos anderer Rollen: weder in der Liste noch als Verweis einer Lektion.
        foreach (var r in AlleRollen())
        {
            var text = KiWissensbasis.SystemText(r);
            foreach (var v in VideoKatalog.Videos.Where(v => !v.Rollen.Contains(r.Rolle)))
            {
                Assert.DoesNotContain("- « " + v.Titel + " » (", text);
                Assert.DoesNotContain("Vidéo « " + v.Titel + " »", text);
            }
        }
    }

    [Fact]
    public void Lektionen_der_Wissensbasis_existieren()
    {
        var ids = FormationKatalog.Lektionen.Select(l => l.Id).ToHashSet();
        Assert.All(KiWissensbasis.LektionenBesitzer.Append(KiWissensbasis.LektionCaisse).Append(KiWissensbasis.LektionCatalogue)
            .Append(KiWissensbasis.LektionChiffres), id => Assert.Contains(id, ids));
        Assert.Equal(KiWissensbasis.LektionenBesitzer.Length, KiWissensbasis.Lektionen(Besitzer).Count);
    }

    /// <summary>Pflege: jede Seite der Seitenleiste (NavMenu.razor) steht in der Wissensbasis – sonst fehlt der KI ein Menüpunkt.</summary>
    [Fact]
    public void Jede_Seite_des_Menues_steht_in_der_Wissensbasis()
    {
        var nav = File.ReadAllText(Path.Combine(WebApp(), "Components", "Layout", "NavMenu.razor"));
        var menue = Regex.Matches(nav, "new\\(\"(/[^\"]*)\", \"([^\"]+)\"").Select(m => (Href: m.Groups[1].Value, Label: m.Groups[2].Value)).ToList();
        Assert.True(menue.Count >= 15, "Menü nicht gefunden");
        var bekannt = AlleRollen().SelectMany(KiWissensbasis.Menue).SelectMany(g => g.Punkte).ToList();
        foreach (var (href, label) in menue)
            Assert.True(bekannt.Any(p => p.Href == href && p.Label == label), $"NavMenu: {href} (« {label} ») fehlt in KiWissensbasis.Menue");
    }

    // ---------------- Sichtbarkeit und Seite ----------------

    private static string WebApp()
    {
        var ordner = new DirectoryInfo(AppContext.BaseDirectory);
        while (ordner is not null && !Directory.Exists(Path.Combine(ordner.FullName, "eNOTE", "eNOTE.WebApp"))) ordner = ordner.Parent;
        Assert.NotNull(ordner);
        return Path.Combine(ordner!.FullName, "eNOTE", "eNOTE.WebApp");
    }

    [Fact]
    public void Menue_und_Seite_nur_mit_Schluessel()
    {
        var nav = File.ReadAllText(Path.Combine(WebApp(), "Components", "Layout", "NavMenu.razor"));
        Assert.Contains("if (KiHilfe.IstVerfuegbar) aide.Add(new(ENote.WebApp.Services.KiHilfe.KiHilfeDienst.SeitenAdresse, \"Poser une question\"", nav);
        Assert.Equal("/aide/question", KiHilfeDienst.SeitenAdresse);

        var seite = File.ReadAllText(Path.Combine(WebApp(), "Components", "Pages", "AideQuestion.razor"));
        Assert.Contains("@page \"/aide/question\"", seite);
        Assert.Contains("@attribute [Authorize]", seite);
        Assert.Contains("Nav.NavigateTo(\"/introuvable\", replace: true);", seite);   // ohne Schlüssel: « Page introuvable »
        Assert.Contains("@if (verfuegbar)", seite);
        Assert.Contains("maxlength=\"@KiHilfeOptionen.MaxFrageZeichen\"", seite);
        Assert.Contains("<AsyncActionButton Text=\"Poser la question\"", seite);   // Ladekreis und Sperre wie überall
        Assert.Contains("Il vous reste {verbleibend} questions aujourd'hui.", seite);
        Assert.Contains("Votre question est envoyée à un service d'intelligence artificielle (Anthropic).", seite);
        Assert.Contains("<video class=\"vh-spieler\"", seite);   // gewohnter Video-Dialog

        var programm = File.ReadAllText(Path.Combine(WebApp(), "Program.cs"));
        Assert.Contains("GetSection(ENote.WebApp.Services.KiHilfe.KiHilfeOptionen.Abschnitt)", programm);
        foreach (var datei in Directory.GetFiles(WebApp(), "appsettings*.json"))
            Assert.DoesNotContain("sk-ant-", File.ReadAllText(datei));
    }

    [Fact]
    public void Parameter_Haiku_kurze_Antwort_Cache_Marke_auf_dem_System_Text()
    {
        var o = new KiHilfeOptionen { AnthropicApiKey = "test" };
        var p = AnthropicKiClient.BaueParameter(new KiAnfrage("WISSEN", "FRAGE"), o);
        Assert.Equal("claude-haiku-5-5", p.Model.Raw());
        Assert.Equal(o.MaxTokens, p.MaxTokens);
        Assert.True(p.Thinking!.TryPickAdaptive(out _));
        Assert.Equal(Effort.Low, p.OutputConfig!.Effort!.Value());
        Assert.True(p.System!.TryPickTextBlockParams(out var bloecke));
        var system = Assert.Single(bloecke);
        Assert.Equal("WISSEN", system.Text);
        Assert.NotNull(system.CacheControl);
        Assert.True(Assert.Single(p.Messages).TryPickUser(out var nutzer));
        Assert.True(nutzer!.Content.TryPickString(out var inhalt));
        Assert.Equal("FRAGE", inhalt);
        Assert.Equal(256, AnthropicKiClient.BaueParameter(new KiAnfrage("s", "q"), new KiHilfeOptionen { MaxTokens = 1 }).MaxTokens);
    }

    // ---------------- Hilfen ----------------

    private sealed class Uhr : TimeProvider
    {
        public DateTimeOffset Jetzt { get; set; } = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Jetzt;
    }

    private sealed class Welt
    {
        public FakeKiClient Client { get; } = new();
        public Uhr Zeit { get; } = new();
        public ListenLogger Log { get; } = new();
        public KiHilfeDienst Dienst { get; }

        public string Antwort { get => Client.Antwort; set => Client.Antwort = value; }
        public KiStopp Stopp { get => Client.Stopp; set => Client.Stopp = value; }
        public Exception? Fehler { get => Client.Fehler; set => Client.Fehler = value; }

        public Welt(Action<KiHilfeOptionen>? anpassen = null)
        {
            var o = new KiHilfeOptionen { AnthropicApiKey = "sk-test" };
            anpassen?.Invoke(o);
            var optionen = Options.Create(o);
            Dienst = new KiHilfeDienst(Client, new KiNutzungsZaehler(optionen, Zeit), optionen, Log);
        }
    }

    private sealed class FakeKiClient : IKiClient
    {
        public List<KiAnfrage> Anfragen { get; } = [];
        public string Antwort { get; set; } = "Ouvrez Gestion › Finances.";
        public KiStopp Stopp { get; set; } = KiStopp.Fertig;
        public Exception? Fehler { get; set; }

        public Task<KiRohAntwort> FrageAsync(KiAnfrage anfrage, CancellationToken ct)
        {
            Anfragen.Add(anfrage);
            if (Fehler is not null) throw Fehler;
            return Task.FromResult(new KiRohAntwort(Antwort, Stopp, 30, 80, 6000, 0));
        }
    }

    private sealed class ListenLogger : ILogger<KiHilfeDienst>
    {
        public List<string> Eintraege { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var werte = state is IEnumerable<KeyValuePair<string, object?>> liste ? string.Join("|", liste.Select(kv => kv.Value)) : string.Empty;
            Eintraege.Add(formatter(state, exception) + "|" + werte + "|" + exception);
        }
    }
}
