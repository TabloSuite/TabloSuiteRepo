using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Paedagogik;
using Skoulou.WebApp.Services.Paedagogik;

namespace Skoulou.Tests.Paedagogik;

/// <summary>Schülerseite: Leçons mit Versuchsregeln und Lösungsanzeige, Examen mit Zeit/Karenz/Auto-Abgabe, Korrektur, Score.</summary>
public class LernUndPruefungTests
{
    private static AntwortEingabe Wahl(SchuelerFrage f, int index) => new() { Optionen = [f.Optionen[index].Id] };
    private static AntwortEingabe Satz(string text) => new() { Text = text };

    [Fact]
    public async Task Schueler_sieht_nur_Zuweisungen_der_eigenen_Klasse_Eltern_keine()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var (_, z6A) = await u.ZugewiesenAsync(u.Formular(InhaltsArt.Lecon, "Pour la 6e A", PaedagogikUmgebung.Text("?", 1, "a")));
        var (_, z6B) = await u.ZugewiesenAsync(u.Formular(InhaltsArt.Lecon, "Pour la 6e B", PaedagogikUmgebung.Text("?", 1, "a")), u.Klasse6B);

        Assert.Equal(["Pour la 6e A"], (await u.Lernen.LeconsAsync(u.SchuelerA.Id, null, null)).Eintraege.Select(l => l.Titel));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Lernen.OeffnenAsync(u.SchuelerA.Id, z6B));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Lernen.OeffnenAsync(u.Eltern.Id, z6A));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Lernen.OeffnenAsync(u.Lehrer.Id, z6A));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Pruefungen.ListeAsync(u.Eltern.Id));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Uebersicht.ErgebnisseAsync(u.Eltern.Id));
        var fragen = await u.FragenAsync(u.SchuelerB.Id, z6B);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Lernen.AntwortenAsync(u.SchuelerA.Id, z6B, fragen[0].Id, Satz("a")));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Lernen.AudioTextAsync(u.SchuelerA.Id, z6B, fragen[0].Id));

        // Noch nicht veröffentlichte Zuweisung (Ab in der Zukunft) ist unsichtbar.
        var id = await u.Inhalte.SpeichernAsync(u.Lehrer.Id, u.Formular(InhaltsArt.Lecon, "Plus tard"));
        var jetzt = u.Zeit.GetUtcNow().UtcDateTime;
        var spaeter = await u.Zuweisungen.ZuweisenAsync(u.Lehrer.Id, new ZuweisungFormular { InhaltId = id, Art = InhaltsArt.Lecon, KlasseId = u.Klasse6A, AbUtc = jetzt.AddDays(2) });
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Lernen.OeffnenAsync(u.SchuelerA.Id, spaeter));
    }

    [Fact]
    public async Task Schuelersicht_verraet_weder_Loesungen_noch_Audiotext_vor_der_Antwort()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var audio = new FrageFormular { Typ = FragenTyp.AudioTexte, TextHtml = "<p>Dictée</p>", Punkte = 1, AudioText = "Le chat dort", Varianten = ["Le chat dort"] };
        var (_, z) = await u.ZugewiesenAsync(u.Formular(InhaltsArt.Lecon, "Dictée", audio, PaedagogikUmgebung.Auswahl("?", 1, "oui", "non")));
        var lecon = await u.Lernen.OeffnenAsync(u.SchuelerA.Id, z);
        var fragen = lecon.Bloecke.SelectMany(b => b.Fragen).ToList();
        Assert.True(fragen[0].HatAudio);
        Assert.Empty(lecon.Antworten);
        // Der Audiotext kommt nur auf Abruf (Vorlesen), nie im Inhalt der Seite.
        Assert.Equal(("Le chat dort", "fr-FR"), await u.Lernen.AudioTextAsync(u.SchuelerA.Id, z, fragen[0].Id));
        var r = await u.Lernen.AntwortenAsync(u.SchuelerA.Id, z, fragen[0].Id, Satz("le chat dort."));
        Assert.Equal(BewertungsStatus.Richtig, r.Stand.Status);
        Assert.Equal(["Le chat dort"], r.Stand.Loesung!.Antworten);
    }

    [Fact]
    public async Task Leçon_sofortige_Rueckmeldung_jede_Frage_einmal_je_Versuch()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var (_, z) = await u.ZugewiesenAsync(u.Formular(InhaltsArt.Lecon, "Fractions",
            PaedagogikUmgebung.Auswahl("Moitié ?", 2, "1/2", "1/3"), PaedagogikUmgebung.Luecken("[[1]] et [[2]]", 2, ["a"], ["b"]),
            PaedagogikUmgebung.Text("Expliquez", 3)));
        var f = await u.FragenAsync(u.SchuelerA.Id, z);

        var r1 = await u.Lernen.AntwortenAsync(u.SchuelerA.Id, z, f[0].Id, Wahl(f[0], 1));
        Assert.Equal(BewertungsStatus.Falsch, r1.Stand.Status);
        Assert.Equal([f[0].Optionen[0].Id], r1.Stand.Loesung!.RichtigeOptionen);
        Assert.False(r1.VersuchAbgeschlossen);
        await Assert.ThrowsAsync<FachFehler>(() => u.Lernen.AntwortenAsync(u.SchuelerA.Id, z, f[0].Id, Wahl(f[0], 0)));
        await Assert.ThrowsAsync<FachFehler>(() => u.Lernen.AntwortenAsync(u.SchuelerA.Id, z, f[1].Id, new AntwortEingabe()));

        var r2 = await u.Lernen.AntwortenAsync(u.SchuelerA.Id, z, f[1].Id, new AntwortEingabe { Luecken = ["A", "x"] });
        Assert.Equal((BewertungsStatus.Teilweise, 1m), (r2.Stand.Status, r2.Stand.Punkte));
        var r3 = await u.Lernen.AntwortenAsync(u.SchuelerA.Id, z, f[2].Id, Satz("Un partage en parts égales."));
        Assert.Equal(BewertungsStatus.Manuell, r3.Stand.Status);
        Assert.True(r3.VersuchAbgeschlossen);
        Assert.Equal(VersuchsStatus.ACorriger, r3.Versuch.Status);
        Assert.Equal(1m, r3.Versuch.Punkte);
        Assert.Equal(7m, r3.Versuch.MaxPunkte);

        var stand = await u.Lernen.OeffnenAsync(u.SchuelerA.Id, z);
        Assert.Equal(3, stand.Antworten.Count);
        Assert.True(stand.Abgeschlossen);
    }

    [Fact]
    public async Task Versuchsregel_MaxN_und_Wertung_Meilleur_Dernier()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var (_, z) = await u.ZugewiesenAsync(u.Formular(InhaltsArt.Lecon, "QCM", PaedagogikUmgebung.Auswahl("?", 4, "oui", "non")), maxVersuche: 2);
        async Task<decimal> SpieleAsync(int option)
        {
            var f = await u.FragenAsync(u.SchuelerA.Id, z);
            var r = await u.Lernen.AntwortenAsync(u.SchuelerA.Id, z, f[0].Id, Wahl(f[0], option));
            return r.Versuch.Punkte;
        }
        Assert.Equal(4m, await SpieleAsync(0));
        await u.Lernen.NeuerVersuchAsync(u.SchuelerA.Id, z);
        Assert.Equal(0m, await SpieleAsync(1));
        await Assert.ThrowsAsync<FachFehler>(() => u.Lernen.NeuerVersuchAsync(u.SchuelerA.Id, z));
        var lecon = await u.Lernen.OeffnenAsync(u.SchuelerA.Id, z);
        Assert.Equal(0, lecon.VerbleibendeVersuche);
        Assert.Equal(100m, lecon.GewerteterProzent);

        // Dernier: der letzte Versuch zählt.
        var (_, z2) = await u.ZugewiesenAsync(u.Formular(InhaltsArt.Lecon, "QCM 2", PaedagogikUmgebung.Auswahl("?", 4, "oui", "non")),
            wertung: VersuchsWertung.Dernier);
        var g = await u.FragenAsync(u.SchuelerA.Id, z2);
        await u.Lernen.AntwortenAsync(u.SchuelerA.Id, z2, g[0].Id, Wahl(g[0], 0));
        await u.Lernen.NeuerVersuchAsync(u.SchuelerA.Id, z2);
        g = await u.FragenAsync(u.SchuelerA.Id, z2);
        await u.Lernen.AntwortenAsync(u.SchuelerA.Id, z2, g[0].Id, Wahl(g[0], 1));
        Assert.Equal(0m, (await u.Lernen.OeffnenAsync(u.SchuelerA.Id, z2)).GewerteterProzent);
        await using var db = await u.Env.Db.CreateDbContextAsync();
        var lernstand = await db.LernStaende.SingleAsync(s => s.ZuweisungId == z2);
        Assert.Equal(2, lernstand.AnzahlVersuche);
        Assert.Equal(4m, lernstand.MaxPunkte);
    }

    [Fact]
    public async Task Loesungen_Jamais_und_ApresFrist_bleiben_verborgen()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var (_, jamais) = await u.ZugewiesenAsync(u.Formular(InhaltsArt.Lecon, "Jamais", PaedagogikUmgebung.Auswahl("?", 1, "oui", "non")),
            loesungen: LoesungsSichtbarkeit.Jamais);
        var f = await u.FragenAsync(u.SchuelerA.Id, jamais);
        Assert.Null((await u.Lernen.AntwortenAsync(u.SchuelerA.Id, jamais, f[0].Id, Wahl(f[0], 1))).Stand.Loesung);

        var frist = u.Zeit.GetUtcNow().UtcDateTime.AddHours(1);
        var (_, apres) = await u.ZugewiesenAsync(u.Formular(InhaltsArt.Lecon, "Après", PaedagogikUmgebung.Auswahl("?", 1, "oui", "non")),
            loesungen: LoesungsSichtbarkeit.ApresFrist, frist: frist);
        var g = await u.FragenAsync(u.SchuelerA.Id, apres);
        Assert.Null((await u.Lernen.AntwortenAsync(u.SchuelerA.Id, apres, g[0].Id, Wahl(g[0], 1))).Stand.Loesung);
        Assert.All((await u.Lernen.OeffnenAsync(u.SchuelerA.Id, apres)).Antworten, a => Assert.Null(a.Loesung));
        u.Zeit.Vor(TimeSpan.FromHours(2));
        var nachFrist = await u.Lernen.OeffnenAsync(u.SchuelerA.Id, apres);
        Assert.NotNull(nachFrist.Antworten.Single().Loesung);
        Assert.NotNull(nachFrist.Sperrgrund);
        await Assert.ThrowsAsync<FachFehler>(() => u.Lernen.NeuerVersuchAsync(u.SchuelerA.Id, apres));
    }

    [Fact]
    public async Task Examen_ein_Versuch_Deadline_Karenz_und_Abgabe()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var (_, z) = await u.ZugewiesenAsync(u.Formular(InhaltsArt.Pruefung, "Devoir",
            PaedagogikUmgebung.Auswahl("2+2 ?", 2, "4", "5"), PaedagogikUmgebung.Text("Simplifiez 6/8", 2, "3/4")),
            frist: u.Zeit.GetUtcNow().UtcDateTime.AddHours(4));
        var vorher = await u.Pruefungen.OeffnenAsync(u.SchuelerA.Id, z);
        Assert.Equal(PruefungsZustand.Ouvert, vorher.Karte.Zustand);
        Assert.Empty(vorher.Bloecke);

        var start = u.Zeit.GetUtcNow().UtcDateTime;
        var v = await u.Pruefungen.StartenAsync(u.SchuelerA.Id, z);
        Assert.Equal(start.AddMinutes(30), v.DeadlineUtc);
        Assert.Equal(v.Id, (await u.Pruefungen.StartenAsync(u.SchuelerA.Id, z)).Id);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Pruefungen.StartenAsync(u.SchuelerB.Id, z));

        var sitzung = await u.Pruefungen.OeffnenAsync(u.SchuelerA.Id, z);
        Assert.Equal(PruefungsZustand.EnCours, sitzung.Karte.Zustand);
        Assert.Equal(1800, sitzung.RestSekunden);
        var fragen = sitzung.Bloecke.SelectMany(b => b.Fragen).ToList();
        await u.Pruefungen.AntwortSpeichernAsync(u.SchuelerA.Id, v.Id, fragen[0].Id, Wahl(fragen[0], 1));
        await u.Pruefungen.AntwortSpeichernAsync(u.SchuelerA.Id, v.Id, fragen[0].Id, Wahl(fragen[0], 0));
        // Während des Examens keine Bewertung sichtbar.
        Assert.All((await u.Pruefungen.OeffnenAsync(u.SchuelerA.Id, z)).Antworten, a => Assert.Null(a.Punkte));
        // Fremder Versuch ist tabu.
        await Assert.ThrowsAsync<FachFehler>(() => u.Pruefungen.AntwortSpeichernAsync(u.SchuelerB.Id, v.Id, fragen[0].Id, Wahl(fragen[0], 0)));

        // 30 s Karenz nach der Deadline, danach nicht mehr.
        u.Zeit.Vor(TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(20));
        await u.Pruefungen.AntwortSpeichernAsync(u.SchuelerA.Id, v.Id, fragen[1].Id, Satz("3/4"));
        u.Zeit.Vor(TimeSpan.FromSeconds(15));
        await Assert.ThrowsAsync<FachFehler>(() => u.Pruefungen.AntwortSpeichernAsync(u.SchuelerA.Id, v.Id, fragen[1].Id, Satz("6/8")));

        var abgabe = await u.Pruefungen.AbgebenAsync(u.SchuelerA.Id, v.Id);
        Assert.Equal(VersuchsStatus.Soumis, abgabe.Status);
        Assert.Equal(4m, abgabe.Punkte);
        Assert.Equal(abgabe.AbgabeUtc, (await u.Pruefungen.AbgebenAsync(u.SchuelerA.Id, v.Id)).AbgabeUtc);
        await Assert.ThrowsAsync<FachFehler>(() => u.Pruefungen.StartenAsync(u.SchuelerA.Id, z));

        // Note sofort, Korrektur erst nach der Frist.
        var danach = await u.Pruefungen.OeffnenAsync(u.SchuelerA.Id, z);
        Assert.Equal(100m, danach.Karte.Prozent);
        Assert.True(danach.Karte.Bestanden);
        Assert.False(danach.LoesungenSichtbar);
        Assert.Empty(danach.Bloecke);
        u.Zeit.Vor(TimeSpan.FromHours(4));
        var korrigiert = await u.Pruefungen.OeffnenAsync(u.SchuelerA.Id, z);
        Assert.True(korrigiert.LoesungenSichtbar);
        Assert.All(korrigiert.Antworten, a => Assert.NotNull(a.Loesung));
    }

    [Fact]
    public async Task Hintergrunddienst_gibt_abgelaufene_Examen_ab_idempotent()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var (_, z) = await u.ZugewiesenAsync(u.Formular(InhaltsArt.Pruefung, "Devoir", PaedagogikUmgebung.Auswahl("?", 2, "oui", "non")),
            frist: u.Zeit.GetUtcNow().UtcDateTime.AddMinutes(10));
        var v = await u.Pruefungen.StartenAsync(u.SchuelerA.Id, z);
        Assert.Equal(u.Zeit.GetUtcNow().UtcDateTime.AddMinutes(10), v.DeadlineUtc);
        var f = (await u.Pruefungen.OeffnenAsync(u.SchuelerA.Id, z)).Bloecke.Single().Fragen.Single();
        await u.Pruefungen.AntwortSpeichernAsync(u.SchuelerA.Id, v.Id, f.Id, Wahl(f, 0));

        u.Zeit.Vor(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(10));
        Assert.Equal(0, await u.Pruefungen.AbgelaufeneAbgebenAsync());
        u.Zeit.Vor(TimeSpan.FromSeconds(25));
        Assert.Equal(1, await u.Pruefungen.AbgelaufeneAbgebenAsync());
        Assert.Equal(0, await u.Pruefungen.AbgelaufeneAbgebenAsync());

        await using var db = await u.Env.Db.CreateDbContextAsync();
        var versuch = await db.Versuche.SingleAsync();
        Assert.Equal(VersuchsStatus.Soumis, versuch.Status);
        Assert.Equal(2m, versuch.Punkte);
        Assert.Equal(versuch.DeadlineUtc, versuch.AbgabeUtc);
        Assert.True((await db.LernStaende.SingleAsync()).Abgeschlossen);
        // Späte Abgabe des Schülers ändert nichts mehr.
        Assert.Equal(VersuchsStatus.Soumis, (await u.Pruefungen.AbgebenAsync(u.SchuelerA.Id, v.Id)).Status);
    }

    [Fact]
    public async Task Rechteverlust_sperrt_laufendes_Examen()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var (_, z) = await u.ZugewiesenAsync(u.Formular(InhaltsArt.Pruefung, "Devoir", PaedagogikUmgebung.Auswahl("?", 2, "oui", "non")));
        var v = await u.Pruefungen.StartenAsync(u.SchuelerA.Id, z);
        var f = (await u.Pruefungen.OeffnenAsync(u.SchuelerA.Id, z)).Bloecke.Single().Fragen.Single();
        await Assert.ThrowsAsync<FachFehler>(() => u.Zuweisungen.ArchivierenAsync(u.Direktor.Id, z));

        // Klassenwechsel: der laufende Versuch nimmt keine Antworten mehr an.
        await using (var db = await u.Env.Db.CreateDbContextAsync())
        {
            var e = await db.Einschreibungen.SingleAsync(x => x.SchuelerId == u.SchuelerA.Id);
            e.Status = EinschreibungsStatus.Changee;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Pruefungen.AntwortSpeichernAsync(u.SchuelerA.Id, v.Id, f.Id, Wahl(f, 0)));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Pruefungen.StartenAsync(u.SchuelerA.Id, z));
    }

    [Fact]
    public async Task Korrektur_eine_Warteschlange_Lehrkraft_eigene_Direktion_fuer_Lehrkraft_mit_Protokoll()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var (_, z) = await u.ZugewiesenAsync(u.Formular(InhaltsArt.Lecon, "Rédaction", PaedagogikUmgebung.Text("Expliquez", 4)));
        var f = await u.FragenAsync(u.SchuelerA.Id, z);
        await u.Lernen.AntwortenAsync(u.SchuelerA.Id, z, f[0].Id, Satz("Ma réponse"));

        // Ohne Frist sofort in der Liste.
        var (offen, _) = await u.Korrektur.SucheAsync(u.Lehrer.Id, new KorrekturFilter());
        var zeile = Assert.Single(offen);
        Assert.Equal(4m, zeile.MaxPunkte);
        Assert.Equal(1, await u.Korrektur.OffeneAnzahlAsync(u.Lehrer.Id));
        Assert.Empty((await u.Korrektur.SucheAsync(u.Lehrer2.Id, new KorrekturFilter())).Eintraege);
        Assert.Single((await u.Korrektur.SucheAsync(u.Direktor.Id, new KorrekturFilter())).Eintraege);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Korrektur.SucheAsync(u.SchuelerA.Id, new KorrekturFilter()));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Korrektur.KorrigierenAsync(u.Lehrer2.Id, zeile.AntwortId, 2, null));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Korrektur.DetailAsync(u.FremdDirektor.Id, zeile.AntwortId));
        await Assert.ThrowsAsync<FachFehler>(() => u.Korrektur.KorrigierenAsync(u.Lehrer.Id, zeile.AntwortId, 5, null));

        await u.Korrektur.KorrigierenAsync(u.Lehrer.Id, zeile.AntwortId, 3, "Bien");
        Assert.Empty((await u.Korrektur.SucheAsync(u.Lehrer.Id, new KorrekturFilter())).Eintraege);
        // Nachkorrektur durch die Direktion (für die Lehrkraft), mit Verlauf.
        u.Zeit.Vor(TimeSpan.FromMinutes(5));
        await u.Korrektur.KorrigierenAsync(u.Direktor.Id, zeile.AntwortId, 4, "Très bien");
        var detail = await u.Korrektur.DetailAsync(u.Lehrer.Id, zeile.AntwortId);
        Assert.Equal(4m, detail.Punkte);
        Assert.Equal(2, detail.Verlauf.Count);
        Assert.Equal(3m, detail.Verlauf[0].AltePunkte);
        Assert.Single((await u.Korrektur.SucheAsync(u.Lehrer.Id, new KorrekturFilter(NurOffene: false))).Eintraege);

        var lecon = await u.Lernen.OeffnenAsync(u.SchuelerA.Id, z);
        Assert.Equal("Très bien", lecon.Antworten.Single().Feedback);
        Assert.Equal(100m, lecon.GewerteterProzent);
        var ergebnisse = await u.Korrektur.ErgebnisseAsync(u.Lehrer.Id, z);
        var a = ergebnisse.Zeilen.Single(x => x.SchuelerId == u.SchuelerA.Id);
        Assert.Equal(SchuelerFortschritt.Termine, a.Fortschritt);
        Assert.Equal(100m, a.Prozent);
        Assert.Equal(VersuchsStatus.Corrige, (await u.Korrektur.VersuchAsync(u.Lehrer.Id, a.GewerteterVersuchId!.Value)).Status);
    }

    [Fact]
    public async Task Examen_Textantwort_wird_nach_Abgabe_korrigiert()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var (_, z) = await u.ZugewiesenAsync(u.Formular(InhaltsArt.Pruefung, "Devoir", PaedagogikUmgebung.Text("Justifiez", 5)));
        var v = await u.Pruefungen.StartenAsync(u.SchuelerA.Id, z);
        var f = (await u.Pruefungen.OeffnenAsync(u.SchuelerA.Id, z)).Bloecke.Single().Fragen.Single();
        await u.Pruefungen.AntwortSpeichernAsync(u.SchuelerA.Id, v.Id, f.Id, Satz("Parce que…"));
        // Während des Examens nichts zu korrigieren.
        Assert.Empty((await u.Korrektur.SucheAsync(u.Lehrer.Id, new KorrekturFilter())).Eintraege);
        Assert.Equal(VersuchsStatus.ACorriger, (await u.Pruefungen.AbgebenAsync(u.SchuelerA.Id, v.Id)).Status);
        var karte = (await u.Pruefungen.ListeAsync(u.SchuelerA.Id)).Single();
        Assert.True(karte.KorrekturOffen);
        Assert.Null(karte.Bestanden);

        var zeile = Assert.Single((await u.Korrektur.SucheAsync(u.Lehrer.Id, new KorrekturFilter())).Eintraege);
        await u.Korrektur.KorrigierenAsync(u.Lehrer.Id, zeile.AntwortId, 2, null);
        var nachher = (await u.Pruefungen.ListeAsync(u.SchuelerA.Id)).Single();
        Assert.Equal(40m, nachher.Prozent);
        Assert.False(nachher.Bestanden);
    }

    [Fact]
    public async Task Ergebnisse_eine_Formel_ueberfaellig_zaehlt_null()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var (_, gemacht) = await u.ZugewiesenAsync(u.Formular(InhaltsArt.Lecon, "Faite", PaedagogikUmgebung.Auswahl("?", 2, "oui", "non")));
        await u.ZugewiesenAsync(u.Formular(InhaltsArt.Lecon, "Oubliée", PaedagogikUmgebung.Auswahl("?", 6, "oui", "non")),
            frist: u.Zeit.GetUtcNow().UtcDateTime.AddHours(1));
        var f = await u.FragenAsync(u.SchuelerA.Id, gemacht);
        await u.Lernen.AntwortenAsync(u.SchuelerA.Id, gemacht, f[0].Id, Wahl(f[0], 0));

        var vorFrist = await u.Uebersicht.ErgebnisseAsync(u.SchuelerA.Id);
        Assert.Equal(100m, vorFrist.Gesamt);
        var start = await u.Uebersicht.EleveStartAsync(u.SchuelerA.Id);
        Assert.Equal("Oubliée", start.Offen.Single().Titel);

        u.Zeit.Vor(TimeSpan.FromHours(2));
        var danach = await u.Uebersicht.ErgebnisseAsync(u.SchuelerA.Id);
        Assert.Equal(25m, danach.Gesamt);
        Assert.True(danach.Zeilen.Single(x => x.Titel == "Oubliée").Ueberfaellig);
        Assert.Equal(25m, danach.Faecher.Single().Prozent);
        Assert.Empty((await u.Uebersicht.EleveStartAsync(u.SchuelerA.Id)).Offen);

        var lehrer = await u.Uebersicht.LehrerStartAsync(u.Lehrer.Id, null);
        Assert.Equal(["6e A", "6e B"], lehrer.Klassen.Select(k => k.Klasse));
        Assert.Equal(1, lehrer.Klassen[0].Schueler);
        Assert.Equal(2, lehrer.Lecons);
    }

    [Fact]
    public async Task Stimme_und_Druck_fuer_Schueler()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var (id, z) = await u.ZugewiesenAsync(u.Formular(InhaltsArt.Lecon, "Fractions", PaedagogikUmgebung.Text("?", 1, "a")));
        Assert.False((await u.Lernen.OeffnenAsync(u.SchuelerA.Id, z)).StimmeAus);
        await u.Lernen.StimmeSetzenAsync(u.SchuelerA.Id, aus: true);
        Assert.True((await u.Lernen.OeffnenAsync(u.SchuelerA.Id, z)).StimmeAus);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Lernen.StimmeSetzenAsync(u.Eltern.Id, true));

        var druck = await u.Lesen.DruckAsync(u.SchuelerA.Id, InhaltsArt.Lecon, id, mitLoesungen: true, z);
        Assert.False(druck.MitLoesungen);
        Assert.Equal("6e A", druck.Klasse);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Lesen.DruckAsync(u.SchuelerB.Id, InhaltsArt.Lecon, id, false, z));
        Assert.True((await u.Lesen.DruckAsync(u.Lehrer.Id, InhaltsArt.Lecon, id, true, z)).MitLoesungen);
    }
}
