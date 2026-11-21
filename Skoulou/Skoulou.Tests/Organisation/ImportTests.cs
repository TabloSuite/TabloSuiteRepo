using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Services.Organisation.Import;

namespace Skoulou.Tests.Organisation;

/// <summary>XLSX-Import: Vorlage, Vorschau mit vollständiger Fehlerliste, Import nur gültiger Zeilen, Zugangsliste, Eltern über Matrikel.</summary>
public class ImportTests
{
    /// <summary>Füllt das erste Blatt einer Vorlage ab Zeile 2 (Werte je Spaltentitel).</summary>
    private static MemoryStream Fuelle(byte[] vorlage, params Dictionary<string, object?>[] zeilen)
    {
        using var mappe = new XLWorkbook(new MemoryStream(vorlage));
        var blatt = mappe.Worksheets.First();
        var spalten = blatt.Row(1).CellsUsed().ToDictionary(c => c.GetString(), c => c.Address.ColumnNumber);
        var nr = 2;
        foreach (var zeile in zeilen)
        {
            foreach (var (titel, wert) in zeile)
            {
                var zelle = blatt.Cell(nr, spalten[titel]);
                switch (wert)
                {
                    case DateTime d: zelle.Value = d; break;
                    case string s: zelle.Value = s; break;
                }
            }
            nr++;
        }
        var ziel = new MemoryStream();
        mappe.SaveAs(ziel);
        ziel.Position = 0;
        return ziel;
    }

    private static Dictionary<string, object?> Z(params (string Titel, object? Wert)[] werte) => werte.ToDictionary(w => w.Titel, w => w.Wert);

    [Fact]
    public async Task Vorlage_enthaelt_Kopfzeile_Erklaerung_und_Klassen()
    {
        var w = await OrgWelt.ErstelleAsync();
        using var mappe = new XLWorkbook(new MemoryStream(await w.D.Import.VorlageAsync(w.Direktor, ImportArt.Eleves, w.Jahr)));
        var kopf = mappe.Worksheets.First().Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Equal(new[] { "Prénom", "Nom", "Classe" }, kopf.Take(3).ToArray());
        Assert.Contains("Matricule", kopf);
        Assert.Contains("Téléphone parent", kopf);
        Assert.True(mappe.Worksheets.Contains("Mode d'emploi"));
        var klassen = mappe.Worksheet("Classes").Column(1).CellsUsed().Skip(1).Select(c => c.GetString()).ToList();
        Assert.Equal(new[] { "5e A", "6e A", "6e B" }, klassen.ToArray());
        // Ohne Daten unter der Kopfzeile: verständliche Meldung.
        var leereVorlage = await w.D.Import.VorlageAsync(w.Direktor, ImportArt.Eleves, w.Jahr);
        var leer = await Assert.ThrowsAsync<FachFehler>(() => w.D.Import.VorschauAsync(w.Direktor, ImportArt.Eleves, w.Jahr,
            new MemoryStream(leereVorlage), "vide.xlsx"));
        Assert.Contains("aucune ligne", leer.Message);
    }

    [Theory]
    [InlineData(false, "2026-11-13")]   // Schule seit August bei SKOULOU: Import im November = Neuzugänge ab heute
    [InlineData(true, "2026-09-01")]    // Schule erst am 10/11/2026 angemeldet (Startphase): « depuis la rentrée »
    public async Task Import_ohne_Datum_schreibt_nach_dem_Standard_ein(bool neueSchule, string erwartet)
    {
        var w = await OrgWelt.ErstelleAsync();
        if (neueSchule)
            await w.MitDbAsync(async db =>
            {
                (await db.Schulen.SingleAsync(s => s.Id == w.SchuleA)).ErstelltAmUtc = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
                return await db.SaveChangesAsync();
            });
        var vorher = await w.MitDbAsync(db => db.Einschreibungen.Select(e => e.Id).ToListAsync());
        var datei = Fuelle(await w.D.Import.VorlageAsync(w.Direktor, ImportArt.Eleves, w.Jahr),
            Z(("Prénom", "Ama"), ("Nom", "Mensah"), ("Classe", "6e A")),
            Z(("Prénom", "Kossi"), ("Nom", "Adjo"), ("Classe", "6e B")));
        var vorschau = await w.D.Import.VorschauAsync(w.Direktor, ImportArt.Eleves, w.Jahr, datei, "eleves.xlsx");
        Assert.Equal(DateOnly.Parse(erwartet), vorschau.VorgabeBeginn);
        Assert.Equal(2, (await w.D.Import.ImportierenAsync(w.Direktor, vorschau.Id)).Angelegt);
        var neu = await w.MitDbAsync(db => db.Einschreibungen.Where(e => !vorher.Contains(e.Id)).Select(e => e.Von).ToListAsync());
        Assert.Equal(2, neu.Count);
        Assert.All(neu, von => Assert.Equal(DateOnly.Parse(erwartet), von));
    }

    [Fact]
    public async Task Vorschau_meldet_alle_Fehler_und_Import_legt_nur_gueltige_Zeilen_an()
    {
        var w = await OrgWelt.ErstelleAsync();
        var vorlage = await w.D.Import.VorlageAsync(w.Koordinator, ImportArt.Eleves, w.Jahr);
        var datei = Fuelle(vorlage,
            Z(("Prénom", "Ama"), ("Nom", "Mensah"), ("Classe", "6e a"), ("Sexe", "F"), ("Date de naissance", "14/03/2014"), ("E-mail", "ama@exemple.tg")),
            Z(("Prénom", "Kossi"), ("Nom", "Adjo"), ("Classe", "6e Z")),
            Z(("Prénom", "Essi"), ("Nom", "Amegah"), ("Classe", "6e A"), ("E-mail", "mauvais")),
            Z(("Prénom", "Edem"), ("Nom", "Kodjo"), ("Identifiant", "dup.id")),
            Z(("Prénom", "Yawa"), ("Nom", "Tchalla"), ("Identifiant", "DUP.ID")),
            Z(("Prénom", "Sena"), ("Nom", "Agbodjan"), ("Matricule", "IMP-1")),
            Z(("Prénom", "Kafui"), ("Nom", "Atsu"), ("Matricule", "imp-1")),
            Z(("Prénom", "Elom"), ("Nom", "Gbeassor"), ("Matricule", "CA260001")),
            Z(("Nom", "Sossou")),
            Z(),
            Z(("Prénom", "Abla"), ("Nom", "Dossou"), ("Sexe", "X"), ("Redoublant", "peut-être"), ("Date de naissance", "2014-13-45")),
            Z(("Prénom", "Mawuli"), ("Nom", "Ahadji"), ("Classe", "6e B"), ("Redoublant", "oui"), ("Date de naissance", new DateTime(2013, 5, 2))));

        var vorschau = await w.D.Import.VorschauAsync(w.Koordinator, ImportArt.Eleves, w.Jahr, datei, "eleves.xlsx");
        Assert.Equal(11, vorschau.Zeilen.Count);
        Assert.Equal(4, vorschau.Gueltig);
        Assert.Equal(7, vorschau.Ungueltig);
        Assert.Equal(9, vorschau.Fehler.Count);
        Assert.Equal(new[] { 3, 4, 6, 8, 9, 10, 12, 12, 12 }, vorschau.Fehler.Select(f => f.Zeile).ToArray());
        Assert.Contains(vorschau.Fehler, f => f.Zeile == 3 && f.Meldung.Contains("6e Z"));
        Assert.Contains(vorschau.Fehler, f => f.Zeile == 6 && f.Meldung.Contains("ligne 5"));
        Assert.Contains(vorschau.Fehler, f => f.Zeile == 8 && f.Meldung.Contains("ligne 7"));
        Assert.Contains(vorschau.Fehler, f => f.Zeile == 9 && f.Meldung.Contains("déjà attribué"));
        var ama = vorschau.Zeilen.Single(z => z.Nr == 2);
        Assert.Equal("ama.mensah", ama.Identifiant);
        Assert.True(ama.Vorgeschlagen);
        Assert.Equal("6e A", ama.Zusatz);

        // Fehlerliste als Excel-Datei.
        using (var fehlerMappe = new XLWorkbook(new MemoryStream(await w.D.Import.FehlerDateiAsync(w.Koordinator, vorschau.Id))))
            Assert.Equal(10, fehlerMappe.Worksheets.First().RowsUsed().Count());
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Import.FehlerDateiAsync(w.Direktor, vorschau.Id));

        var ergebnis = await w.D.Import.ImportierenAsync(w.Koordinator, vorschau.Id);
        Assert.Equal(4, ergebnis.Angelegt);
        Assert.Equal(7, ergebnis.Ausgelassen);
        Assert.Equal(4, ergebnis.Zugaenge.Zugaenge.Count);
        Assert.Same(ergebnis.Zugaenge, w.D.Ablage.Holen(ergebnis.Zugaenge.Id, w.Koordinator));
        Assert.All(ergebnis.Zugaenge.Zugaenge, z => Assert.Equal(Rollen.Schueler, z.Rolle));
        Assert.Contains(ergebnis.Zugaenge.Zugaenge, z => z.Identifiant == "dup.id");
        Assert.Contains(ergebnis.Zugaenge.Zugaenge, z => z.Zusatz == "6e A · CA260004");

        var konten = await w.MitDbAsync(db => db.Users.Where(k => k.ErstelltVonId == w.Koordinator).ToListAsync());
        Assert.Equal(4, konten.Count);
        Assert.All(konten, k => Assert.True(k.MussPasswortAendern));
        var mawuli = konten.Single(k => k.Vorname == "Mawuli");
        Assert.Equal(new DateOnly(2013, 5, 2), mawuli.Geburtsdatum);
        var einschreibung = await w.MitDbAsync(db => db.Einschreibungen.SingleAsync(e => e.SchuelerId == mawuli.Id));
        Assert.Equal(w.Klasse6B, einschreibung.KlasseId);
        Assert.True(einschreibung.IstWiederholer);
        var sena = konten.Single(k => k.Vorname == "Sena").Id;
        Assert.Equal("IMP-1", await w.MitDbAsync(db => db.SchuelerProfile.Where(p => p.KontoId == sena).Select(p => p.Matrikel).SingleAsync()));
        Assert.Equal(KontoStatus.Actif, konten.Single(k => k.Vorname == "Ama").Status);
        Assert.Equal(Geschlecht.Weiblich, konten.Single(k => k.Vorname == "Ama").Geschlecht);
        Assert.Equal(1, await w.MitDbAsync(db => db.MailAusgaenge.CountAsync()));
        Assert.Equal(1, await w.MitDbAsync(db => db.AuditEintraege.CountAsync(a => a.Aktion == Skoulou.WebApp.Services.Organisation.OrgAudit.ImportAusgefuehrt)));

        // Die Vorschau ist verbraucht.
        Assert.Equal(ImportService.MeldungAbgelaufen, (await Assert.ThrowsAsync<FachFehler>(() => w.D.Import.ImportierenAsync(w.Koordinator, vorschau.Id))).Message);
    }

    [Fact]
    public async Task Eltern_werden_ueber_die_Matrikel_der_Kinder_verknuepft()
    {
        var w = await OrgWelt.ErstelleAsync();
        var vorlage = await w.D.Import.VorlageAsync(w.Direktor, ImportArt.Parents, null);
        var datei = Fuelle(vorlage,
            Z(("Prénom", "Kodzo"), ("Nom", "Adjo"), ("Matricule enfant", "CA260001, ca260002"), ("Lien de parenté", "père")),
            Z(("Prénom", "Mawuéna"), ("Nom", "Dossou"), ("Matricule enfant", "XX999")),
            Z(("Prénom", "Afi"), ("Nom", "Lawson"), ("Téléphone", "+228 90 00 00 01")));
        var vorschau = await w.D.Import.VorschauAsync(w.Direktor, ImportArt.Parents, null, datei, "parents.xlsx");
        Assert.Equal(2, vorschau.Gueltig);
        var fehler = Assert.Single(vorschau.Fehler);
        Assert.Equal(3, fehler.Zeile);
        Assert.Contains("XX999", fehler.Meldung);

        var ergebnis = await w.D.Import.ImportierenAsync(w.Direktor, vorschau.Id);
        Assert.Equal(2, ergebnis.Angelegt);
        var kodzo = ergebnis.Zugaenge.Zugaenge.Single(z => z.Name == "Kodzo Adjo");
        Assert.Equal(Rollen.Eltern, kodzo.Rolle);
        var links = await w.MitDbAsync(db => db.ElternKinder.Where(e => e.ElternId == kodzo.KontoId).ToListAsync());
        Assert.Equal(2, links.Count);
        Assert.All(links, l => Assert.Equal("Père", l.Beziehung));
        Assert.Contains(links, l => l.SchuelerId == w.SchuelerZiel);
    }

    [Fact]
    public async Task Lehrkraefte_ohne_Jahr_und_falsche_Dateien_verstaendlich_abgewiesen()
    {
        var w = await OrgWelt.ErstelleAsync();
        var vorlage = await w.D.Import.VorlageAsync(w.Direktor, ImportArt.Eleves, w.Jahr);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Import.VorschauAsync(w.Direktor, ImportArt.Eleves, null,
            Fuelle(vorlage, Z(("Prénom", "A"), ("Nom", "B"))), "x.xlsx"));
        Assert.Contains(".xlsx", (await Assert.ThrowsAsync<FachFehler>(() =>
            w.D.Import.VorschauAsync(w.Direktor, ImportArt.Enseignants, null, new MemoryStream("Prénom;Nom\nAma;Mensah"u8.ToArray()), "x.csv"))).Message);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Import.VorschauAsync(w.Direktor, ImportArt.Enseignants, null, new MemoryStream([0x50, 0x4B, 3, 4, 0, 0]), "kaputt.xlsx"));

        using var mappe = new XLWorkbook();
        var blatt = mappe.Worksheets.Add("Feuil1");
        blatt.Cell(1, 1).Value = "Nom";
        blatt.Cell(1, 2).Value = "Courriel";
        blatt.Cell(2, 1).Value = "Mensah";
        var ziel = new MemoryStream();
        mappe.SaveAs(ziel);
        ziel.Position = 0;
        Assert.Contains("Prénom", (await Assert.ThrowsAsync<FachFehler>(() => w.D.Import.VorschauAsync(w.Direktor, ImportArt.Enseignants, null, ziel, "equipe.xlsx"))).Message);

        // Kopfzeilen tolerant (ohne Akzente, andere Schreibweisen); Lehrkräfte werden als « Enseignant » angelegt.
        using var tolerant = new XLWorkbook();
        var b2 = tolerant.Worksheets.Add("Equipe");
        b2.Cell(1, 1).Value = "PRENOM *";
        b2.Cell(1, 2).Value = "nom";
        b2.Cell(1, 3).Value = "Courriel";
        b2.Cell(2, 1).Value = "Yao";
        b2.Cell(2, 2).Value = "Kpodar";
        b2.Cell(2, 3).Value = "yao@exemple.tg";
        var z2 = new MemoryStream();
        tolerant.SaveAs(z2);
        z2.Position = 0;
        var vorschau = await w.D.Import.VorschauAsync(w.Direktor, ImportArt.Enseignants, null, z2, "equipe.xlsx");
        Assert.Equal(1, vorschau.Gueltig);
        var ergebnis = await w.D.Import.ImportierenAsync(w.Direktor, vorschau.Id);
        var konto = await w.MitDbAsync(db => db.Users.SingleAsync(k => k.Id == ergebnis.Zugaenge.Zugaenge[0].KontoId));
        Assert.Equal(Rollen.Lehrer, konto.Rolle);
        Assert.Equal("yao@exemple.tg", konto.Email);
    }
}
