using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Paedagogik;
using Skoulou.WebApp.Services.Paedagogik;

namespace Skoulou.Tests.Paedagogik;

/// <summary>Inhalte: Rechte je Rolle, « Saisi par la direction », Revisionen, Zuweisung, Übertragen, Medien, Formeln.</summary>
public class InhaltServiceTests
{
    [Fact]
    public async Task Lehrkraft_legt_nur_fuer_eigene_Stufe_und_Fach_an()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var id = await u.Inhalte.SpeichernAsync(u.Lehrer.Id, u.Formular(InhaltsArt.Lecon, "Fractions", PaedagogikUmgebung.Auswahl("1/2 ?", 1, "oui", "non")));
        var geladen = await u.Inhalte.LadeAsync(u.Lehrer.Id, InhaltsArt.Lecon, id);
        Assert.Equal(u.Lehrer.Id, geladen.AutorId);
        Assert.Null(geladen.ErfasstVonName);

        // Français unterrichtet nur Lehrer2.
        var fremdesFach = u.Formular(InhaltsArt.Lecon, "Conjugaison");
        fremdesFach.FachId = u.Francais;
        var f = await Assert.ThrowsAsync<FachFehler>(() => u.Inhalte.SpeichernAsync(u.Lehrer.Id, fremdesFach));
        Assert.Equal("StufeFach", f.Feld);
    }

    [Fact]
    public async Task Lehrkraft_sieht_und_aendert_keine_fremden_Inhalte_Direktion_alle_der_Schule()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var id = await u.Inhalte.SpeichernAsync(u.Lehrer.Id, u.Formular(InhaltsArt.Lecon, "Fractions"));

        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Inhalte.LadeAsync(u.Lehrer2.Id, InhaltsArt.Lecon, id));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Lesen.AnsichtAsync(u.Lehrer2.Id, InhaltsArt.Lecon, id));
        var (liste, _) = await u.Inhalte.SucheAsync(u.Lehrer2.Id, new InhaltFilter(InhaltsArt.Lecon));
        Assert.Empty(liste);

        // Direktion und Koordination: transparent alle Inhalte der Schule.
        Assert.Equal("Fractions", (await u.Inhalte.LadeAsync(u.Direktor.Id, InhaltsArt.Lecon, id)).Titel);
        Assert.Single((await u.Inhalte.SucheAsync(u.Koordinator.Id, new InhaltFilter(InhaltsArt.Lecon))).Eintraege);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Inhalte.LadeAsync(u.FremdDirektor.Id, InhaltsArt.Lecon, id));

        // Schüler und Eltern: keine Inhaltsverwaltung.
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Inhalte.SucheAsync(u.SchuelerA.Id, new InhaltFilter(InhaltsArt.Lecon)));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Inhalte.LadeAsync(u.Eltern.Id, InhaltsArt.Lecon, id));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Lesen.DruckAsync(u.Eltern.Id, InhaltsArt.Lecon, id, true, null));
    }

    [Fact]
    public async Task Direktion_erfasst_fuer_Lehrkraft_Kennzeichnung_bleibt_nach_Bearbeitung()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var neu = await u.Inhalte.NeuAsync(u.Direktor.Id, InhaltsArt.Pruefung, u.Lehrer.Id);
        Assert.Equal(u.Lehrer.Id, neu.AutorId);
        Assert.NotNull(neu.ErfasstVonName);
        var f = u.Formular(InhaltsArt.Pruefung, "Devoir", PaedagogikUmgebung.Auswahl("2+2 ?", 1, "4", "5"));
        f.AutorId = u.Lehrer.Id;
        var id = await u.Inhalte.SpeichernAsync(u.Direktor.Id, f);

        var vomLehrer = await u.Inhalte.LadeAsync(u.Lehrer.Id, InhaltsArt.Pruefung, id);
        vomLehrer.Titel = "Devoir corrigé par le prof";
        await u.Inhalte.SpeichernAsync(u.Lehrer.Id, vomLehrer);
        await using var db = await u.Env.Db.CreateDbContextAsync();
        var p = await db.Pruefungen.SingleAsync();
        Assert.Equal(u.Lehrer.Id, p.AutorId);
        Assert.Equal(u.Direktor.Id, p.ErfasstVonId);
        Assert.True((await u.Inhalte.SucheAsync(u.Lehrer.Id, new InhaltFilter(InhaltsArt.Pruefung))).Eintraege.Single().SaisiParDirection);

        // Nur für aktive Lehrkräfte derselben Schule; Lehrkräfte erfassen nicht für Kollegen.
        await Assert.ThrowsAsync<FachFehler>(() => u.Inhalte.NeuAsync(u.Direktor.Id, InhaltsArt.Lecon, u.SchuelerA.Id));
        var fremd = await u.Inhalte.NeuAsync(u.Lehrer2.Id, InhaltsArt.Lecon, u.Lehrer.Id);
        Assert.Equal(u.Lehrer2.Id, fremd.AutorId);
    }

    [Fact]
    public async Task Fragen_mit_Antworten_sind_unveraenderlich_Bearbeiten_erzeugt_Revision()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var (id, z) = await u.ZugewiesenAsync(u.Formular(InhaltsArt.Lecon, "Fractions",
            PaedagogikUmgebung.Auswahl("Moitié ?", 1, "1/2", "1/3"), PaedagogikUmgebung.Text("Simplifiez 6/8", 2, "3/4")));
        var fragen = await u.FragenAsync(u.SchuelerA.Id, z);
        await u.Lernen.AntwortenAsync(u.SchuelerA.Id, z, fragen[0].Id, new AntwortEingabe { Optionen = [fragen[0].Optionen[0].Id] });

        var f = await u.Inhalte.LadeAsync(u.Lehrer.Id, InhaltsArt.Lecon, id);
        Assert.True(f.Bloecke[0].Fragen[0].HatAntworten);
        Assert.False(f.Bloecke[0].Fragen[1].HatAntworten);
        var alteId = f.Bloecke[0].Fragen[0].Id;
        var zweiteId = f.Bloecke[0].Fragen[1].Id;
        f.Bloecke[0].Fragen[0].TextHtml = "<p>Quelle fraction vaut la moitié ?</p>";
        f.Bloecke[0].Fragen[1].Punkte = 3;
        await u.Inhalte.SpeichernAsync(u.Lehrer.Id, f);

        await using var db = await u.Env.Db.CreateDbContextAsync();
        var alt = await db.Fragen.SingleAsync(x => x.Id == alteId);
        Assert.True(alt.Archiviert);
        Assert.Equal("<p>Moitié ?</p>", alt.TextHtml);
        var neu = await db.Fragen.SingleAsync(x => x.VorgaengerId == alteId);
        Assert.Equal(2, neu.Revision);
        Assert.False(neu.Archiviert);
        Assert.Equal(1, neu.Nr);
        // Ohne Antworten: Änderung an Ort und Stelle.
        Assert.Equal(3m, (await db.Fragen.SingleAsync(x => x.Id == zweiteId)).Punkte);

        // Der laufende Versuch behält die alte Revision, ein neuer Versuch bekommt die neue.
        var laufend = await db.Versuche.SingleAsync();
        Assert.Contains(alteId!.Value, (IEnumerable<Guid>)laufend.FragenIds);
        await u.Lernen.VersuchBeendenAsync(u.SchuelerA.Id, z);
        await u.Lernen.NeuerVersuchAsync(u.SchuelerA.Id, z);
        var neueFragen = await u.FragenAsync(u.SchuelerA.Id, z);
        Assert.Equal(neu.Id, neueFragen[0].Id);
        Assert.Contains(await db.Antworten.Select(a => a.FrageId).ToListAsync(), x => x == alteId);

        // Entfernen einer beantworteten Frage archiviert statt zu löschen.
        var g = await u.Inhalte.LadeAsync(u.Lehrer.Id, InhaltsArt.Lecon, id);
        g.Bloecke[0].Fragen.RemoveAt(0);
        await u.Inhalte.SpeichernAsync(u.Lehrer.Id, g);
        await using var db2 = await u.Env.Db.CreateDbContextAsync();
        Assert.True(await db2.Fragen.AnyAsync(x => x.Id == alteId));
    }

    [Fact]
    public async Task Speichern_prueft_Fragen_Lueckentext_und_Punkte()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        await Assert.ThrowsAsync<FachFehler>(() => u.Inhalte.SpeichernAsync(u.Lehrer.Id,
            u.Formular(InhaltsArt.Lecon, "x", PaedagogikUmgebung.Luecken("Le [[1]] et [[3]]", 1, ["a"], ["b"]))));
        await Assert.ThrowsAsync<FachFehler>(() => u.Inhalte.SpeichernAsync(u.Lehrer.Id,
            u.Formular(InhaltsArt.Lecon, "x", PaedagogikUmgebung.Luecken("Le [[1]]", 1, ["a"], ["b"]))));
        var ohneRichtige = PaedagogikUmgebung.Auswahl("?", 1, "a", "b");
        ohneRichtige.Optionen[0].Richtig = false;
        await Assert.ThrowsAsync<FachFehler>(() => u.Inhalte.SpeichernAsync(u.Lehrer.Id, u.Formular(InhaltsArt.Lecon, "x", ohneRichtige)));
        await Assert.ThrowsAsync<FachFehler>(() => u.Inhalte.SpeichernAsync(u.Lehrer.Id, u.Formular(InhaltsArt.Lecon, "x", PaedagogikUmgebung.Text("?", 0))));
        var leer = u.Formular(InhaltsArt.Lecon, "  ");
        Assert.Equal("Titel", (await Assert.ThrowsAsync<FachFehler>(() => u.Inhalte.SpeichernAsync(u.Lehrer.Id, leer))).Feld);

        // Lückentext wird als sicheres HTML gespeichert.
        var id = await u.Inhalte.SpeichernAsync(u.Lehrer.Id, u.Formular(InhaltsArt.Lecon, "ok", PaedagogikUmgebung.Luecken("a <b> [[1]]", 1, ["x"])));
        await using var db = await u.Env.Db.CreateDbContextAsync();
        Assert.Equal("a &lt;b&gt; [[1]]", (await db.Fragen.SingleAsync()).TextHtml);
    }

    [Fact]
    public async Task Zuweisung_nur_an_Klassen_mit_Lehrauftrag_und_neue_Runde_archiviert_alte()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var id = await u.Inhalte.SpeichernAsync(u.Lehrer.Id, u.Formular(InhaltsArt.Lecon, "Fractions", PaedagogikUmgebung.Text("?", 1, "a")));
        var klassen = await u.Zuweisungen.KlassenAsync(u.Lehrer.Id, InhaltsArt.Lecon, id, null);
        Assert.Equal(["6e A", "6e B"], klassen.Select(k => k.Name));

        var jetzt = u.Zeit.GetUtcNow().UtcDateTime;
        var formular = new ZuweisungFormular { InhaltId = id, Art = InhaltsArt.Lecon, KlasseId = u.Klasse6A, AbUtc = jetzt };
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Zuweisungen.ZuweisenAsync(u.Lehrer2.Id, formular));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Zuweisungen.ZuweisenAsync(u.SchuelerA.Id, formular));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Zuweisungen.ZuweisenAsync(u.FremdDirektor.Id, formular));
        var erste = await u.Zuweisungen.ZuweisenAsync(u.Lehrer.Id, formular);
        var zweite = await u.Zuweisungen.ZuweisenAsync(u.Direktor.Id, formular);
        var liste = await u.Zuweisungen.ListeAsync(u.Lehrer.Id, InhaltsArt.Lecon, id);
        Assert.Equal(ZuweisungsStatus.Active, liste.Single(z => z.Id == zweite).Status);
        Assert.Equal(ZuweisungsStatus.Archivee, liste.Single(z => z.Id == erste).Status);

        // Entwürfe und Fristen in der Vergangenheit werden abgelehnt.
        var entwurfFormular = u.Formular(InhaltsArt.Lecon, "Entwurf");
        entwurfFormular.Status = InhaltsStatus.Brouillon;
        var entwurf = await u.Inhalte.SpeichernAsync(u.Lehrer.Id, entwurfFormular);
        await Assert.ThrowsAsync<FachFehler>(() => u.Zuweisungen.ZuweisenAsync(u.Lehrer.Id,
            new ZuweisungFormular { InhaltId = entwurf, Art = InhaltsArt.Lecon, KlasseId = u.Klasse6A, AbUtc = jetzt }));
        await Assert.ThrowsAsync<FachFehler>(() => u.Zuweisungen.ZuweisenAsync(u.Lehrer.Id,
            new ZuweisungFormular { InhaltId = id, Art = InhaltsArt.Lecon, KlasseId = u.Klasse6A, AbUtc = jetzt, FristUtc = jetzt.AddMinutes(-5) }));

        // Lehrer2 unterrichtet Français nur in 6e A.
        var fr = await u.Inhalte.SpeichernAsync(u.Lehrer2.Id,
            new InhaltFormular { Art = InhaltsArt.Lecon, Titel = "Fr", StufeId = u.Stufe, FachId = u.Francais, Status = InhaltsStatus.Prete, Seiten = [new() { Html = "<p>x</p>" }] });
        Assert.Equal(["6e A"], (await u.Zuweisungen.KlassenAsync(u.Direktor.Id, InhaltsArt.Lecon, fr, null)).Select(k => k.Name));
        await Assert.ThrowsAsync<FachFehler>(() => u.Zuweisungen.ZuweisenAsync(u.Lehrer2.Id,
            new ZuweisungFormular { InhaltId = fr, Art = InhaltsArt.Lecon, KlasseId = u.Klasse6B, AbUtc = jetzt }));
    }

    [Fact]
    public async Task Examen_Zuweisung_erzwingt_einen_Versuch_und_Loesungen_nach_Frist()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var id = await u.Inhalte.SpeichernAsync(u.Lehrer.Id, u.Formular(InhaltsArt.Pruefung, "Devoir", PaedagogikUmgebung.Text("?", 1, "a")));
        var jetzt = u.Zeit.GetUtcNow().UtcDateTime;
        await Assert.ThrowsAsync<FachFehler>(() => u.Zuweisungen.ZuweisenAsync(u.Lehrer.Id,
            new ZuweisungFormular { InhaltId = id, Art = InhaltsArt.Pruefung, KlasseId = u.Klasse6A, AbUtc = jetzt }));
        var z = await u.Zuweisungen.ZuweisenAsync(u.Lehrer.Id, new ZuweisungFormular
        {
            InhaltId = id, Art = InhaltsArt.Pruefung, KlasseId = u.Klasse6A, AbUtc = jetzt, FristUtc = jetzt.AddHours(2),
            MaxVersuche = 5, Wertung = VersuchsWertung.Dernier, Loesungen = LoesungsSichtbarkeit.Immediat
        });
        await using var db = await u.Env.Db.CreateDbContextAsync();
        var gespeichert = await db.Zuweisungen.SingleAsync(x => x.Id == z);
        Assert.Equal(1, gespeichert.MaxVersuche);
        Assert.Equal(LoesungsSichtbarkeit.ApresFrist, gespeichert.LoesungenSichtbar);
    }

    [Fact]
    public async Task Uebertragen_nur_Direktion_mit_Audit_und_Zuweisungen()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var (id, z) = await u.ZugewiesenAsync(u.Formular(InhaltsArt.Lecon, "Fractions", PaedagogikUmgebung.Text("?", 1, "a")));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Inhalte.UebertragenAsync(u.Lehrer.Id, InhaltsArt.Lecon, id, u.Lehrer2.Id));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Inhalte.UebertragenAsync(u.FremdDirektor.Id, InhaltsArt.Lecon, id, u.Lehrer2.Id));
        await u.Inhalte.UebertragenAsync(u.Direktor.Id, InhaltsArt.Lecon, id, u.Lehrer2.Id);

        await using var db = await u.Env.Db.CreateDbContextAsync();
        Assert.Equal(u.Lehrer2.Id, (await db.Lecons.SingleAsync()).AutorId);
        Assert.Equal(u.Lehrer2.Id, (await db.Zuweisungen.SingleAsync(x => x.Id == z)).LehrerId);
        var audit = await db.AuditEintraege.SingleAsync(a => a.Aktion == InhaltService.AuditUebertragen);
        Assert.Equal(u.Direktor.Id, audit.AkteurId);
        Assert.Contains("Fractions", audit.Text);
        // Der frühere Autor sieht den Inhalt nicht mehr.
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Inhalte.LadeAsync(u.Lehrer.Id, InhaltsArt.Lecon, id));
    }

    [Fact]
    public async Task Duplizieren_Loeschen_und_Archivieren()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var (id, _) = await u.ZugewiesenAsync(u.Formular(InhaltsArt.Lecon, "Fractions", PaedagogikUmgebung.Text("?", 1, "a")));
        var kopie = await u.Inhalte.DuplizierenAsync(u.Lehrer.Id, InhaltsArt.Lecon, id);
        var k = await u.Inhalte.LadeAsync(u.Lehrer.Id, InhaltsArt.Lecon, kopie);
        Assert.Equal("Fractions (copie)", k.Titel);
        Assert.Single(k.Bloecke.Single().Fragen);
        Assert.NotEqual(id, kopie);

        await Assert.ThrowsAsync<FachFehler>(() => u.Inhalte.LoeschenAsync(u.Lehrer.Id, InhaltsArt.Lecon, id));
        await u.Inhalte.StatusSetzenAsync(u.Lehrer.Id, InhaltsArt.Lecon, id, InhaltsStatus.Archive);
        Assert.DoesNotContain((await u.Inhalte.SucheAsync(u.Lehrer.Id, new InhaltFilter(InhaltsArt.Lecon))).Eintraege, e => e.Id == id);
        await u.Inhalte.LoeschenAsync(u.Lehrer.Id, InhaltsArt.Lecon, kopie);
        await using var db = await u.Env.Db.CreateDbContextAsync();
        Assert.False(await db.Lecons.AnyAsync(l => l.Id == kopie));
        Assert.Equal(1, await db.Fragen.CountAsync());
    }

    [Fact]
    public async Task Medien_hochladen_pruefen_und_Nutzung_im_Inhalt()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3, 4];
        var bild = await u.Medien.HochladenAsync(u.Lehrer.Id, MedienArt.Bild, "../../fraction.png", new MemoryStream(png));
        Assert.Equal("fraction.png", bild.Dateiname);
        Assert.Equal(png.Length, bild.Bytes);
        await Assert.ThrowsAsync<FachFehler>(() => u.Medien.HochladenAsync(u.Lehrer.Id, MedienArt.Bild, "x.png", new MemoryStream("<svg onload=x>"u8.ToArray())));
        await Assert.ThrowsAsync<FachFehler>(() => u.Medien.HochladenAsync(u.Lehrer.Id, MedienArt.Video, "v.mp4", new MemoryStream(png)));
        await Assert.ThrowsAsync<FachFehler>(() => u.Medien.UploadStartenAsync(u.Lehrer.Id, MedienArt.Bild, "gros.png", 6L * 1024 * 1024));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Medien.UploadStartenAsync(u.SchuelerA.Id, MedienArt.Bild, "x.png", 10));
        var video = await u.Medien.HochladenAsync(u.Lehrer.Id, MedienArt.Video, "cours.mp4", RegelnTests.Mp4(1000, 61_000));
        Assert.Equal(61, video.DauerSek);

        var f = u.Formular(InhaltsArt.Lecon, "Avec image");
        f.Seiten[0].Html = $"<p>Regardez :</p><p><img src=\"/medien/{bild.Id}\"></p>";
        f.VideoMediumId = video.Id;
        var id = await u.Inhalte.SpeichernAsync(u.Lehrer.Id, f);
        await using var db = await u.Env.Db.CreateDbContextAsync();
        var nutzungen = await db.MedienNutzungen.Where(n => n.ZielId == id).Select(n => n.MediumId).ToListAsync();
        Assert.Equal(new[] { bild.Id, video.Id }.Order(), nutzungen.Order());

        // Bild einer fremden Schule wird abgelehnt.
        var fremd = new Medium { SchuleId = Guid.NewGuid(), BesitzerId = u.FremdDirektor.Id, Art = MedienArt.Bild, BlobName = "x", Dateiname = "x.png", ContentType = "image/png" };
        db.Medien.Add(fremd);
        await db.SaveChangesAsync();
        var g = u.Formular(InhaltsArt.Lecon, "Fremd");
        g.Seiten[0].Html = $"<img src=\"/medien/{fremd.Id}\">";
        await Assert.ThrowsAsync<FachFehler>(() => u.Inhalte.SpeichernAsync(u.Lehrer.Id, g));
    }

    [Fact]
    public async Task Medienfreigabe_fuer_Schueler_nur_ueber_zugewiesenen_Inhalt()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
        var bild = await u.Medien.HochladenAsync(u.Lehrer.Id, MedienArt.Bild, "a.png", new MemoryStream(png));
        var f = u.Formular(InhaltsArt.Lecon, "Avec image");
        f.Seiten[0].Html = $"<p><img src=\"/medien/{bild.Id}\"></p>";
        await u.ZugewiesenAsync(f, u.Klasse6A);

        var freigabe = new PaedagogikMedienFreigabe(u.Zeit, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        await using var db = await u.Env.Db.CreateDbContextAsync();
        var medium = await db.Medien.SingleAsync(m => m.Id == bild.Id);
        Assert.True(await freigabe.DarfSehenAsync(db, u.SchuelerA.Id, Skoulou.Core.Rechte.Rollen.Schueler, u.Schule, medium, default));
        Assert.False(await freigabe.DarfSehenAsync(db, u.SchuelerB.Id, Skoulou.Core.Rechte.Rollen.Schueler, u.Schule, medium, default));
        Assert.False(await freigabe.DarfSehenAsync(db, u.Eltern.Id, Skoulou.Core.Rechte.Rollen.Eltern, u.Schule, medium, default));
        // Grundregel: Lehrkräfte derselben Schule, fremde Direktion nicht.
        Assert.True(Skoulou.WebApp.Services.Infrastruktur.Medien.MedienEndpunkte.DarfSehen(Skoulou.Core.Rechte.Rollen.Lehrer, u.Schule, u.Lehrer2.Id, medium));
        Assert.False(Skoulou.WebApp.Services.Infrastruktur.Medien.MedienEndpunkte.DarfSehen(Skoulou.Core.Rechte.Rollen.Direktor, Guid.NewGuid(), u.FremdDirektor.Id, medium));
    }

    [Fact]
    public async Task Kassenbeleg_nicht_ueber_medien_und_nicht_in_Inhalten()
    {
        // Klicktest 21.11.2026: /medien/{beleg} lieferte Lehrkraft und Koordinator ohne Finanzrecht den Kassenbeleg.
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        var beleg = new Medium { SchuleId = u.Schule, BesitzerId = u.FremdDirektor.Id, Art = MedienArt.Bild, Zweck = MedienZweck.Beleg, BlobName = "b", Dateiname = "b.png", ContentType = "image/png" };
        Assert.False(Skoulou.WebApp.Services.Infrastruktur.Medien.MedienEndpunkte.DarfSehen(Skoulou.Core.Rechte.Rollen.Lehrer, u.Schule, u.Lehrer.Id, beleg));
        Assert.False(Skoulou.WebApp.Services.Infrastruktur.Medien.MedienEndpunkte.DarfSehen(Skoulou.Core.Rechte.Rollen.Koordinator, u.Schule, u.Lehrer2.Id, beleg));
        Assert.True(Skoulou.WebApp.Services.Infrastruktur.Medien.MedienEndpunkte.DarfSehen(Skoulou.Core.Rechte.Rollen.SuperAdmin, null, Guid.NewGuid(), beleg));
        Assert.True(Skoulou.WebApp.Services.Infrastruktur.Medien.MedienEndpunkte.DarfSehen(Skoulou.Core.Rechte.Rollen.Direktor, u.Schule, u.FremdDirektor.Id, beleg)); // eigener Upload

        await using (var db = await u.Env.Db.CreateDbContextAsync()) { db.Medien.Add(beleg); await db.SaveChangesAsync(); }
        var f = u.Formular(InhaltsArt.Lecon, "Avec justificatif");
        f.Seiten[0].Html = $"<p><img src=\"/medien/{beleg.Id}\"></p>";
        await Assert.ThrowsAsync<FachFehler>(() => u.Inhalte.SpeichernAsync(u.Lehrer.Id, f));
    }

    [Fact]
    public async Task Formeln_je_Konto_ohne_Duplikat_mit_Grenze()
    {
        await using var u = await PaedagogikUmgebung.ErstelleAsync();
        await u.Formeln.SpeichernAsync(u.Lehrer.Id, "\\frac{a}{b}", "Fraction");
        await u.Formeln.SpeichernAsync(u.Lehrer.Id, "\\frac{a}{b}", "Ma fraction");
        var liste = await u.Formeln.ListeAsync(u.Lehrer.Id);
        Assert.Equal("Ma fraction", liste.Single().Bezeichnung);
        Assert.Empty(await u.Formeln.ListeAsync(u.Lehrer2.Id));
        await Assert.ThrowsAsync<FachFehler>(() => u.Formeln.SpeichernAsync(u.Lehrer.Id, "a<b", null));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Formeln.ListeAsync(u.SchuelerA.Id));
        await Assert.ThrowsAsync<FachFehler>(() => u.Formeln.LoeschenAsync(u.Lehrer2.Id, liste[0].Id));

        await using (var db = await u.Env.Db.CreateDbContextAsync())
        {
            db.FormelFavoriten.AddRange(Enumerable.Range(0, FormelService.Limit - 1).Select(i => new FormelFavorit { KontoId = u.Lehrer.Id, LaTeX = $"x^{i}", Bezeichnung = "f" }));
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<FachFehler>(() => u.Formeln.SpeichernAsync(u.Lehrer.Id, "y", null));
        await u.Formeln.SpeichernAsync(u.Lehrer.Id, "x^1", "mise à jour");
    }
}
