using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.Tests.Organisation;

/// <summary>Stammdaten der Schule (Logo, Einstellungen) und Cockpit-Zahlen der Direktion.</summary>
public class SchuleUndCockpitTests
{
    [Fact]
    public async Task Logo_nur_als_Bild_bis_fuenf_MB()
    {
        var w = await OrgWelt.ErstelleAsync();
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Schule.LogoHochladenAsync(w.Direktor, new MemoryStream("GIF89a-pas-autorise"u8.ToArray()), "logo.gif"));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Schule.LogoHochladenAsync(w.Direktor, new MemoryStream([]), "vide.png"));
        var gross = new byte[BildDatei.MaxBytes + 1];
        OrgWelt.Png.CopyTo(gross, 0);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Schule.LogoHochladenAsync(w.Direktor, new MemoryStream(gross), "gross.png"));
        Assert.Empty(w.D.Speicher.Dateien);

        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };
        var erstes = await w.D.Schule.LogoHochladenAsync(w.Direktor, new MemoryStream(OrgWelt.Png), "logo.png");
        var zweites = await w.D.Schule.LogoHochladenAsync(w.Direktor, new MemoryStream(jpeg), "../../logo.jpg");
        var daten = await w.D.Schule.LadeAsync(w.Direktor);
        Assert.Equal(zweites, daten.LogoMediumId);
        var medien = await w.MitDbAsync(db => db.Medien.ToListAsync());
        Assert.Equal(2, medien.Count);
        Assert.Equal("image/jpeg", medien.Single(m => m.Id == zweites).ContentType);
        Assert.Equal("logo.jpg", medien.Single(m => m.Id == zweites).Dateiname);
        Assert.StartsWith($"schulen/{w.SchuleA:N}/logo/", medien.Single(m => m.Id == erstes).BlobName);
        // Nur das aktuelle Logo ist « benutzt »; das alte bleibt gespeichert (Bulletins verweisen darauf).
        Assert.Equal(new[] { zweites }, await w.MitDbAsync(db => db.MedienNutzungen.Where(n => n.Typ == "Schule.Logo").Select(n => n.MediumId).ToListAsync()));
        await w.D.Schule.LogoEntfernenAsync(w.Direktor);
        Assert.Null((await w.D.Schule.LadeAsync(w.Direktor)).LogoMediumId);
    }

    [Fact]
    public async Task Stammdaten_Dokumente_und_Einstellungen()
    {
        var w = await OrgWelt.ErstelleAsync();
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Schule.StammdatenSpeichernAsync(w.Direktor, new SchulStammEingabe("COLLÈGE  beta " + w.Kennung, "Lomé", null)));
        await w.D.Schule.StammdatenSpeichernAsync(w.Direktor, new SchulStammEingabe("  Lycée   Alpha " + w.Kennung, "Kpalimé", null));
        var daten = await w.D.Schule.LadeAsync(w.Direktor);
        Assert.Equal("Lycée Alpha " + w.Kennung, daten.Name);
        Assert.Equal("Togo", daten.Land);
        Assert.Equal(1, await w.MitDbAsync(db => db.AuditEintraege.CountAsync(a => a.Aktion == OrgAudit.SchuleGeaendert)));

        await Assert.ThrowsAsync<FachFehler>(() => w.D.Schule.DokumenteSpeichernAsync(w.Direktor,
            new DokumentEingabe(null, null, null, null, "pas-une-adresse", null, null, null, null, null, null)));
        await w.D.Schule.DokumenteSpeichernAsync(w.Direktor, new DokumentEingabe(" Lycée Alpha ", "", null, "+228 22 00 00 00", "Contact@Exemple.tg",
            null, null, "RÉPUBLIQUE TOGOLAISE", null, null, "Public"));
        daten = await w.D.Schule.LadeAsync(w.Direktor);
        Assert.Equal("Lycée Alpha", daten.OffizielleBezeichnung);
        Assert.Null(daten.Slogan);
        Assert.Equal("contact@exemple.tg", daten.Email);

        await Assert.ThrowsAsync<FachFehler>(() => w.D.Schule.EinstellungenSpeichernAsync(w.Direktor, new SchulEinstellungEingabe("ÉCOLE", true, false)));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Schule.EinstellungenSpeichernAsync(w.Direktor, new SchulEinstellungEingabe("ABCDEFG", true, false)));
        await w.D.Schule.EinstellungenSpeichernAsync(w.Direktor, new SchulEinstellungEingabe(" lya ", false, true));
        daten = await w.D.Schule.LadeAsync(w.Direktor);
        Assert.Equal("LYA", daten.MatrikelPraefix);
        Assert.False(daten.AbsenzOhneEntschuldigungZaehltNull);
        Assert.True(daten.ElternSehenNotenLive);
        Assert.Equal("LYA260001", daten.MatrikelBeispiel(2026));
        Assert.Single(await w.D.Schule.KoordinatorenAsync(w.Direktor), k => k.KontoId == w.KoordZiel);
    }

    [Fact]
    public async Task Cockpit_zaehlt_das_laufende_Jahr()
    {
        var w = await OrgWelt.ErstelleAsync();
        await w.D.Anwesenheit.AppellSpeichernAsync(w.Lehrer, w.Klasse6A, OrgWelt.Heute,
            [new AppellEingabe(w.EinschreibungSchueler, AnwesenheitsStatus.Absent, null, false, null),
             new AppellEingabe(w.EinschreibungZiel, AnwesenheitsStatus.Retard, 5, false, null)]);
        var c = await w.D.Cockpit.LadeAsync(w.Koordinator);
        Assert.Equal("2026-2027", c.Jahr);
        Assert.Equal(SchuljahrStatus.Laufend, c.JahrStatus);
        Assert.Equal(3, c.SchuelerAktiv);
        Assert.Equal(1, c.AValider);
        Assert.Equal(1, c.OhneKlasse);
        Assert.Equal(3, c.Klassen);
        Assert.Equal(3, c.Lehrer);
        Assert.Equal(1, c.LehrerOhneAuftrag);
        Assert.Equal(1, c.AbsencesHeute);
        Assert.Equal(1, c.RetardsHeute);
        Assert.Equal(1, c.KlassenMitAppellHeute);
        Assert.Equal(2, c.ElevesEnClasseHeute);
        Assert.True(c.EinrichtungFertig);
    }

    [Fact]
    public async Task Cockpit_einer_neuen_Schule_zeigt_die_naechsten_Schritte()
    {
        var w = await OrgWelt.ErstelleAsync();
        var c = await w.D.Cockpit.LadeAsync(w.DirektorB);
        Assert.Null(c.Jahr);
        Assert.Equal(0, c.SchuelerAktiv);
        Assert.Equal(0, c.Klassen);
        Assert.False(c.EinrichtungFertig);
        Assert.All(c.Schritte, s => Assert.False(s.Erledigt));
        Assert.Equal("/direction/annee", c.Schritte[0].Href);
        Assert.Equal(6, c.Schritte.Count);
    }
}
