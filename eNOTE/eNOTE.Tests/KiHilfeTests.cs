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
/// KI-Hilfe « Poser une question » (19.11.2026): Dienst mit Fake-Client (nie echte Aufrufe), keine Fragen-Grenzen, zunächst
/// nur Administrateur/Responsable, Datenschutz (Frage nie im Log), Wissensbasis je Rolle ohne fremde Rollen, Anfrage-Parameter
/// und Unsichtbarkeit ohne Schlüssel.
/// </summary>
public class KiHilfeTests
{
    private static readonly KiRolle Besitzer = new(AppRollen.Besitzer);
    private static readonly KiRolle Employe = new(AppRollen.Kassierer);

    // ---------------- Dienst ----------------

    [Fact]
    public async Task Leere_und_zu_lange_Fragen_gehen_nicht_an_den_Dienst()
    {
        var w = new Welt();
        Assert.Equal(KiHilfeDienst.MeldungLeer, (await w.Dienst.FrageAsync(Besitzer, " \n ")).Meldung);
        var lang = await w.Dienst.FrageAsync(Besitzer, new string('a', KiHilfeOptionen.MaxFrageZeichen + 1));
        Assert.Equal(KiHilfeDienst.MeldungZuLang, lang.Meldung);
        Assert.Empty(w.Client.Anfragen);
        Assert.True((await w.Dienst.FrageAsync(Besitzer, new string('a', KiHilfeOptionen.MaxFrageZeichen))).Erfolg);
    }

    [Fact]
    public async Task Keine_Fragen_Grenzen()
    {
        // Nutzer 19.11.2026: « nicht limitieren » – auch viele Fragen hintereinander gehen alle an den Dienst.
        var w = new Welt();
        var admin = new KiRolle(AppRollen.SuperAdmin);
        for (var i = 0; i < 100; i++)
            Assert.True((await w.Dienst.FrageAsync(admin, $"Question {i}")).Erfolg);
        Assert.Equal(100, w.Client.Anfragen.Count);
    }

    [Fact]
    public async Task Zunaechst_nur_fuer_Administrateur_und_Responsable()
    {
        var w = new Welt(o => o.AlleRollen = false);
        Assert.True(w.Dienst.IstVerfuegbarFuer(AppRollen.SuperAdmin));
        Assert.True(w.Dienst.IstVerfuegbarFuer(AppRollen.ProduktOwner));
        foreach (var rolle in new[] { AppRollen.Besitzer, AppRollen.Kassierer, string.Empty })
        {
            Assert.False(w.Dienst.IstVerfuegbarFuer(rolle));
            // Auch ein direkter Aufruf (z. B. alte Seite offen) geht nicht an den Dienst.
            Assert.Equal(KiHilfeDienst.MeldungIndisponible, (await w.Dienst.FrageAsync(new KiRolle(rolle), "Question")).Meldung);
        }
        Assert.Empty(w.Client.Anfragen);
        Assert.True((await w.Dienst.FrageAsync(new KiRolle(AppRollen.ProduktOwner), "Question")).Erfolg);

        // Später für alle: Ki:AlleRollen = true. Ohne Schlüssel nie.
        Assert.True(new Welt(o => o.AlleRollen = true).Dienst.IstVerfuegbarFuer(AppRollen.Kassierer));
        Assert.False(new Welt(o => o.AnthropicApiKey = null).Dienst.IstVerfuegbarFuer(AppRollen.SuperAdmin));
    }

    [Fact]
    public async Task Fehler_des_Dienstes_freundliche_Meldung()
    {
        foreach (var fehler in new Exception[] { new KiDienstFehler(KiFehlerArt.Ueberlastet, "429"), new KiDienstFehler(KiFehlerArt.Konfiguration, "401"), new HttpRequestException("x") })
        {
            var w = new Welt { Fehler = fehler };
            var e = await w.Dienst.FrageAsync(Besitzer, "Où sont les finances ?");
            Assert.False(e.Erfolg);
            Assert.Equal(KiHilfeDienst.MeldungIndisponible, e.Meldung);
        }
    }

    [Fact]
    public async Task Ablehnung_abgeschnitten_und_leer()
    {
        var w = new Welt { Stopp = KiStopp.Abgelehnt };
        var abgelehnt = await w.Dienst.FrageAsync(Besitzer, "Écris mon business plan");
        Assert.Equal(KiHilfeDienst.MeldungAbgelehnt, abgelehnt.Meldung);
        Assert.Contains("eNOTE", abgelehnt.Meldung);
        Assert.DoesNotContain("SKOULOU", abgelehnt.Meldung);

        w.Stopp = KiStopp.Abgeschnitten;
        w.Antwort = "1. Ouvrez la caisse.\n2. Touchez";
        var ab = await w.Dienst.FrageAsync(Besitzer, "Caisse ?");
        Assert.True(ab.Erfolg);
        Assert.EndsWith(" …", ab.Antwort);
        Assert.Equal(KiBlockArt.Schritte, Assert.Single(ab.Bloecke).Art);

        w.Stopp = KiStopp.Fertig;
        w.Antwort = "  ";
        Assert.Equal(KiHilfeDienst.MeldungOhneText, (await w.Dienst.FrageAsync(Besitzer, "?")).Meldung);
    }

    [Fact]
    public async Task Passende_Videos_der_Rolle_werden_Knoepfe_fremde_nicht()
    {
        var eigenes = KiWissensbasis.Videos(Employe).First(v => v.Fuer == "Employé");
        var fremd = VideoKatalog.Videos.First(v => !v.Rollen.Contains(AppRollen.Kassierer));
        var w = new Welt { Antwort = $"Ouvrez la caisse.\nVidéo : « {eigenes.Titel} »\nAussi : « {fremd.Titel} »." };
        var e = await w.Dienst.FrageAsync(Employe, "Encaisser ?");
        Assert.Equal([eigenes], e.Videos);
        Assert.DoesNotContain("Vidéo :", e.Antwort);
        Assert.Contains(fremd.Titel, e.Antwort);

        // Ohne Anführungszeichen ist ein Titel gewöhnlicher Text: « Vos clients » steht in vielen Sätzen.
        var kurz = KiWissensbasis.Videos(Besitzer).Single(v => v.Titel == "Vos clients");
        w.Antwort = "Ouvrez Vente › Clients pour voir vos clients.";
        Assert.Empty((await w.Dienst.FrageAsync(Besitzer, "Clients ?")).Videos);
        w.Antwort = "Ouvrez Vente › Clients.\nVidéo : \"Vos clients\"";
        Assert.Equal([kurz], (await w.Dienst.FrageAsync(Besitzer, "Clients ?")).Videos);
    }

    [Fact]
    public async Task An_den_Dienst_gehen_nur_Hilfetext_Rolle_und_Frage_nie_ins_Log()
    {
        var w = new Welt();
        await w.Dienst.FrageAsync(Besitzer, "Le client <Ama Mensah> doit 5 000 F, où le noter ?");
        var a = Assert.Single(w.Client.Anfragen);
        Assert.Same(KiWissensbasis.SystemText(Besitzer), a.SystemText);
        Assert.Contains("Rôle de l'utilisateur : Propriétaire", a.NutzerText);
        Assert.Contains("‹Ama Mensah›", a.NutzerText);
        Assert.NotEmpty(w.Log.Eintraege);
        Assert.All(w.Log.Eintraege, l => { Assert.DoesNotContain("Mensah", l); Assert.DoesNotContain("5 000", l); Assert.DoesNotContain(w.Antwort, l); });
    }

    [Fact]
    public async Task Ohne_Schluessel_unsichtbar_und_kein_Aufruf()
    {
        var w = new Welt(o => o.AnthropicApiKey = null);
        Assert.False(w.Dienst.IstVerfuegbar);
        Assert.Equal(KiHilfeDienst.MeldungIndisponible, (await w.Dienst.FrageAsync(Besitzer, "Question")).Meldung);
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
        var recht = new UserSalon { KannFinanzenSehen = true, KannRabatteVerwalten = true, KannKundenAktenFuehren = true };
        Assert.Equal(new KiRolle(AppRollen.Kassierer, Remises: true, Finances: true, Clients: true), KiRolle.Aus(Nutzer(AppRollen.Kassierer), recht));
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
        yield return new KiRolle(AppRollen.Kassierer, Clients: true);
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
        // « Dossiers clients » (angestellter Arzt im Büro): nur « Clients » dazu, mit der Lektion zu den Clients.
        var arzt = new KiRolle(AppRollen.Kassierer, Clients: true);
        Assert.Equal(["/", "/clients", "/aide/videos", "/contact"], Ziele(arzt));
        Assert.Contains(KiWissensbasis.Lektionen(arzt), l => l.Id == KiWissensbasis.LektionClients);
        Assert.DoesNotContain(KiWissensbasis.Lektionen(Employe), l => l.Id == KiWissensbasis.LektionClients);
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

    /// <summary>
    /// Nutzer 20.11.2026: Administrateur und Responsable dürfen alles fragen – ihre Wissensbasis kennt die anderen Rollen;
    /// der Responsable aber nichts vom Administrateur (« die KI bei POs soll nichts von Admin-Menüs kennen »).
    /// </summary>
    [Fact]
    public void Plattform_kennt_Menue_und_Hinweise_der_anderen_Rollen_Responsable_nichts_vom_Admin()
    {
        static List<string> Zeilen(KiRolle r) => [.. KiWissensbasis.Menue(r).SelectMany(g => g.Punkte.Select(p => "- " + (g.Titel is null ? p.Label : g.Titel + " › " + p.Label) + " : " + p.Erklaerung))];
        Assert.Contains(KiWissensbasis.AlleRollenFuerPlattform, r => r.Rolle == AppRollen.Kassierer && r.Prestations && r.Remises && r.Finances && r.Clients);

        foreach (var eigene in new[] { new KiRolle(AppRollen.SuperAdmin), new KiRolle(AppRollen.ProduktOwner) })
        {
            var text = KiWissensbasis.SystemText(eigene);
            var bekannt = KiWissensbasis.PlattformReihenfolge(eigene).ToList();
            Assert.Equal(eigene.Rolle, bekannt[0].Rolle);
            Assert.Equal(eigene.Rolle == AppRollen.SuperAdmin ? 4 : 3, bekannt.Count);
            Assert.Contains(KiWissensbasis.Regel3Plattform, text);
            Assert.DoesNotContain(KiWissensbasis.Regel3Laden, text);
            foreach (var r in bekannt)
            {
                Assert.Contains("### Menu : " + r.Bezeichnung, text);
                foreach (var z in Zeilen(r)) Assert.Contains(z, text);
            }
            // eigene Rolle zuerst und markiert
            Assert.Contains("### Menu : " + eigene.Bezeichnung + " (votre rôle)", text);
            Assert.True(text.IndexOf("### Menu : " + eigene.Bezeichnung, StringComparison.Ordinal)
                < bekannt.Skip(1).Min(r => text.IndexOf("### Menu : " + r.Bezeichnung, StringComparison.Ordinal)));
            // Hinweise des Propriétaire und des Mitarbeiters mit Rollenname
            Assert.Contains("Propriétaire – Zone propriétaire :", text);
            Assert.Contains("Employé (connexion personnelle) – Votre compte personnel ne donne jamais accès à la caisse", text);
            Assert.True(text.Length < 100_000, $"{eigene.Schluessel}: {text.Length} Zeichen");
        }

        // Responsable: kein Menü des Administrateurs, keine Admin-Seiten (Journaux, E-mails, Annonces, Admin-Tabs)
        var po = KiWissensbasis.SystemText(new KiRolle(AppRollen.ProduktOwner));
        Assert.DoesNotContain("### Menu : Administrateur", po);
        var poZeilen = KiWissensbasis.PlattformReihenfolge(new KiRolle(AppRollen.ProduktOwner)).SelectMany(Zeilen).ToHashSet();
        var nurAdmin = Zeilen(new KiRolle(AppRollen.SuperAdmin)).Where(z => !poZeilen.Contains(z)).ToList();
        Assert.NotEmpty(nurAdmin);
        foreach (var z in nurAdmin) Assert.DoesNotContain(z, po);
        Assert.DoesNotContain("Journaux", po);
        Assert.DoesNotContain("Propriétaires plateforme", po);

        // Employé (unterste Rolle): Regel 3 unverändert, kein fremdes Menü und keine fremden Hinweise.
        // Der Propriétaire kennt seit 21.11.2026 auch seine Employés (« nach unten », eigener Test).
        var employe = KiWissensbasis.SystemText(Employe);
        Assert.Contains(KiWissensbasis.Regel3Laden, employe);
        Assert.DoesNotContain(KiWissensbasis.Regel3Plattform, employe);
        Assert.DoesNotContain(KiWissensbasis.Regel3Besitzer, employe);
        Assert.DoesNotContain("### Menu : ", employe);
        Assert.DoesNotContain("Équipe de la plateforme", employe);
        Assert.DoesNotContain("Zone propriétaire :", employe);
        Assert.Equal([Employe], KiWissensbasis.BekannteRollen(Employe));
    }

    /// <summary>
    /// Nutzer 21.11.2026, Regel « nach unten »: jede Rolle kennt sich und die Rollen darunter, nie die darüber – der Propriétaire
    /// kennt das Menü seiner Employés (Mitarbeiterkonto mit allen Rechten), aber nichts vom Administrateur oder Responsable.
    /// </summary>
    [Fact]
    public void Proprietaire_kennt_seine_Employes_aber_nichts_von_der_Plattform()
    {
        static List<string> Zeilen(KiRolle r) => [.. KiWissensbasis.Menue(r).SelectMany(g => g.Punkte.Select(p => "- " + (g.Titel is null ? p.Label : g.Titel + " › " + p.Label) + " : " + p.Erklaerung))];
        var text = KiWissensbasis.SystemText(Besitzer);

        // Eigene Rolle zuerst, dann das Mitarbeiterkonto mit allen Rechten (« selon les droits accordés »).
        Assert.Equal([Besitzer, KiWissensbasis.MitarbeiterAlleRechte], KiWissensbasis.BekannteRollen(Besitzer));
        Assert.Contains("### Menu : Propriétaire (votre rôle)", text);
        Assert.Contains("### Menu : Employé (connexion personnelle)", text);
        Assert.True(text.IndexOf("### Menu : Propriétaire (votre rôle)", StringComparison.Ordinal)
            < text.IndexOf("### Menu : Employé (connexion personnelle)", StringComparison.Ordinal));
        Assert.Contains("selon les droits accordés", text);
        Assert.Contains(KiWissensbasis.MitarbeiterHinweisBesitzer, text);
        foreach (var z in Zeilen(Besitzer).Concat(Zeilen(KiWissensbasis.MitarbeiterAlleRechte))) Assert.Contains(z, text);

        // Hinweise beider Rollen mit Rollenname; Regel 3 für den Propriétaire.
        Assert.Contains("Propriétaire (votre rôle) – Zone propriétaire :", text);
        Assert.Contains("Employé (connexion personnelle) – Votre compte personnel ne donne jamais accès à la caisse", text);
        Assert.Contains(KiWissensbasis.Regel3Besitzer, text);
        Assert.Contains("comment font ses employés", text);
        Assert.Contains("Ne décris jamais les rôles de la plateforme", text);
        Assert.DoesNotContain(KiWissensbasis.Regel3Laden, text);
        Assert.DoesNotContain(KiWissensbasis.Regel3Plattform, text);

        // Nichts von Administrateur oder Responsable: keine Menüs, Seiten, Lektionen oder Hinweise der Plattform.
        foreach (var verboten in new[]
        {
            "### Menu : Administrateur", "### Menu : Responsable", "Équipe de la plateforme", "Plateforme ›", "Administration",
            "Journaux", "Mes établissements", "Propriétaires plateforme", "Facturation", "Annonces", "Formation ›", "Mes commissions",
            "Vous accompagnez vos établissements"
        })
            Assert.DoesNotContain(verboten, text);
        foreach (var plattform in new[] { new KiRolle(AppRollen.SuperAdmin), new KiRolle(AppRollen.ProduktOwner) })
            foreach (var z in Zeilen(plattform).Where(z => !Zeilen(Besitzer).Contains(z) && !Zeilen(KiWissensbasis.MitarbeiterAlleRechte).Contains(z)))
                Assert.DoesNotContain(z, text);
        Assert.True(text.Length < 100_000, $"Propriétaire: {text.Length} Zeichen");
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
        Assert.Contains("if (KiHilfe.IstVerfuegbarFuer(kiRolle)) aide.Add(new(ENote.WebApp.Services.KiHilfe.KiHilfeDienst.SeitenAdresse, \"Poser une question\"", nav);
        Assert.Equal("/aide/question", KiHilfeDienst.SeitenAdresse);

        var seite = File.ReadAllText(Path.Combine(WebApp(), "Components", "Pages", "AideQuestion.razor"));
        Assert.Contains("@page \"/aide/question\"", seite);
        Assert.Contains("@attribute [Authorize]", seite);
        Assert.Contains("if (!verfuegbar) Nav.NavigateTo(\"/introuvable\", replace: true);", seite);   // ohne Schlüssel/Rolle: « Page introuvable »
        Assert.Contains("verfuegbar = await KiHilfe.IstVerfuegbarFuerAsync(rolle.Rolle);", seite);   // Schalter frisch gelesen
        Assert.Contains("@if (verfuegbar)", seite);
        Assert.Contains("maxlength=\"@KiHilfeOptionen.MaxFrageZeichen\"", seite);
        Assert.Contains("<AsyncActionButton Text=\"Poser la question\"", seite);   // Ladekreis und Sperre wie überall
        Assert.DoesNotContain("Il vous reste", seite);                             // keine Fragen-Grenzen
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

    private sealed class Welt
    {
        public FakeKiClient Client { get; } = new();
        public ListenLogger Log { get; } = new();
        public KiHilfeDienst Dienst { get; }

        public string Antwort { get => Client.Antwort; set => Client.Antwort = value; }
        public KiStopp Stopp { get => Client.Stopp; set => Client.Stopp = value; }
        public Exception? Fehler { get => Client.Fehler; set => Client.Fehler = value; }

        public Welt(Action<KiHilfeOptionen>? anpassen = null)
        {
            // Die Dienst-Tests nutzen Besitzer-/Mitarbeiter-Wissensbasen → alle Rollen frei; die Rollensperre prüft ein eigener Test.
            var o = new KiHilfeOptionen { AnthropicApiKey = "sk-test", AlleRollen = true };
            anpassen?.Invoke(o);
            Dienst = new KiHilfeDienst(Client, Options.Create(o), Log);
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
