using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;
using Skoulou.WebApp.Services.Organisation.Druck;

namespace Skoulou.Tests.Organisation;

/// <summary>
/// PDFs des Bereichs (Zugangsliste, Schülerkarten, Stundenplan) und die öffentliche Kartenprüfung.
/// Mit SKOULOU_DRUCK_BILDER=1 werden zusätzlich PNG-Vorschauen nach %TEMP%\skoulou-neu\E3\druck geschrieben.
/// </summary>
public class DruckTests
{
    static DruckTests() => new ServiceCollection().AddOrganisationDruck();

    private static (KartenService Karten, DruckService Druck) Dienste(OrgWelt w)
    {
        var karten = new KartenService(w.D.Db, w.D.Zugriff);
        var druck = new DruckService(w.D.Db, w.D.Zugriff, w.D.Ablage, w.D.Speicher, karten, w.D.Stundenplan,
            new OptionsWrapper<AppOptionen>(new AppOptionen { BaseUrl = "https://skoulou.test" }), w.D.Zeit);
        return (karten, druck);
    }

    private static void IstPdf(byte[] pdf)
    {
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    private static void Vorschau(IDocument dokument, string name)
    {
        if (Environment.GetEnvironmentVariable("SKOULOU_DRUCK_BILDER") != "1") return;
        var ordner = Path.Combine(Path.GetTempPath(), "skoulou-neu", "E3", "druck");
        Directory.CreateDirectory(ordner);
        dokument.GenerateImages(i => Path.Combine(ordner, $"{name}-{i}.png"), new ImageGenerationSettings { RasterDpi = 110 });
    }

    [Fact]
    public async Task Zugangsliste_nur_fuer_den_Ersteller_des_Stapels()
    {
        var w = await OrgWelt.ErstelleAsync();
        var (_, druck) = Dienste(w);
        var zugaenge = new List<Zugang>
        {
            new(Guid.NewGuid(), "Ama Mensah", Rollen.Schueler, "ama.mensah", "k7Rm4pXa", "6e A · CA260004"),
            new(Guid.NewGuid(), "Kodzo Adjo", Rollen.Eltern, "kodzo.adjo", "Wq3nH8tz", "Père de Kossi Adjo"),
            new(Guid.NewGuid(), "Afi Lawson", Rollen.Lehrer, "afi.lawson", "Tz9cE4mb"),
        };
        var stapel = w.D.Ablage.Ablegen(w.Direktor, w.SchuleA, zugaenge);

        var pdf = await druck.ZugangslisteAsync(w.Direktor, stapel.Id);
        IstPdf(pdf.Inhalt);
        Assert.EndsWith(".pdf", pdf.Dateiname);
        Vorschau(ZugangslistePdf.Dokument(new SchulKopf(w.SchulNameA, null), zugaenge, "https://skoulou.test/auth/connexion", OrgWelt.Heute), "acces");

        // Ein anderes Konto derselben Schule bekommt den Stapel nicht; Lehrer haben gar keinen Zugriff.
        await Assert.ThrowsAsync<FachFehler>(() => druck.ZugangslisteAsync(w.Koordinator, stapel.Id));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => druck.ZugangslisteAsync(w.Lehrer, stapel.Id));
        await Assert.ThrowsAsync<FachFehler>(() => druck.ZugangslisteAsync(w.Direktor, Guid.NewGuid()));
    }

    [Fact]
    public async Task Karten_einer_Klasse_vergeben_Kartencodes_und_bleiben_in_der_Schule()
    {
        var w = await OrgWelt.ErstelleAsync();
        var (karten, druck) = Dienste(w);

        var pdf = await druck.KartenAsync(w.Koordinator, w.Klasse6A);
        IstPdf(pdf.Inhalt);
        Assert.Equal("cartes-6e-a.pdf", pdf.Dateiname);

        var tokens = await w.MitDbAsync(db => db.SchuelerProfile.Where(p => p.KontoId == w.Schueler || p.KontoId == w.SchuelerZiel)
            .Select(p => p.KartenToken).ToListAsync());
        Assert.Equal(2, tokens.Count);
        Assert.All(tokens, t => Assert.True(KartenService.IstTokenFormat(t)));

        // Zweiter Druck behält die Codes (Karten bleiben gültig).
        var bogen = await karten.KartenAsync(w.Direktor, w.Klasse6A);
        Assert.Equal(tokens.Order(), bogen.Karten.Select(k => (string?)k.Token).Order());
        Vorschau(SchuelerKartenPdf.Dokument(bogen, null, new Dictionary<Guid, byte[]>(), t => "https://skoulou.test/verification/carte/" + t), "cartes");

        // Leere Klasse: trotzdem ein PDF mit Hinweis.
        IstPdf((await druck.KartenAsync(w.Direktor, w.Klasse5A)).Inhalt);

        await Assert.ThrowsAsync<KeinZugriffFehler>(() => druck.KartenAsync(w.DirektorB, w.Klasse6A));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => druck.KartenAsync(w.Lehrer, w.Klasse6A));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => druck.KartenAsync(w.Eltern, w.Klasse6A));
    }

    [Fact]
    public async Task Kartenpruefung_zeigt_nur_Schule_Jahr_und_Gueltigkeit()
    {
        var w = await OrgWelt.ErstelleAsync();
        var (karten, _) = Dienste(w);
        var bogen = await karten.KartenAsync(w.Direktor, w.Klasse6A);
        var token = bogen.Karten.First(k => k.SchuelerId == w.Schueler).Token;

        var ok = await karten.PruefeKarteAsync(token);
        Assert.True(ok.Gueltig);
        Assert.Equal(w.SchulNameA, ok.Schule);
        Assert.Equal("2026-2027", ok.Jahr);
        // Keine Personendaten im Ergebnis.
        Assert.Equal(["Gueltig", "Jahr", "Schule"], typeof(KartenPruefung).GetProperties().Select(p => p.Name).Order().ToArray());

        var unbekannt = await karten.PruefeKarteAsync(new string('a', 32));
        Assert.False(unbekannt.Gueltig);
        Assert.Null(unbekannt.Schule);
        Assert.False((await karten.PruefeKarteAsync("<script>")).Gueltig);
        Assert.False((await karten.PruefeKarteAsync(null)).Gueltig);

        // Archiviertes Konto: Karte ungültig, Schule bleibt erkennbar.
        await w.MitDbAsync(async db =>
        {
            var k = await db.Users.FirstAsync(x => x.Id == w.Schueler);
            k.Status = KontoStatus.Archive;
            return await db.SaveChangesAsync();
        });
        var archiviert = await karten.PruefeKarteAsync(token);
        Assert.False(archiviert.Gueltig);
        Assert.Equal(w.SchulNameA, archiviert.Schule);
    }

    [Fact]
    public async Task Stundenplan_als_Wochenraster_je_nach_Rolle()
    {
        var w = await OrgWelt.ErstelleAsync();
        var (_, druck) = Dienste(w);

        var pdf = await druck.PlanAsync(w.Direktor, w.PlanPub);
        IstPdf(pdf.Inhalt);
        Assert.Equal("emploi-du-temps-6e-a.pdf", pdf.Dateiname);
        IstPdf((await druck.PlanAsync(w.Direktor, w.PlanEntwurf)).Inhalt);      // Direktion: auch Entwürfe
        IstPdf((await druck.PlanAsync(w.Lehrer, w.PlanPub)).Inhalt);            // unterrichtet in diesem Plan
        IstPdf((await druck.PlanAsync(w.Schueler, w.PlanPub)).Inhalt);          // eigene Klasse
        IstPdf((await druck.PlanAsync(w.Eltern, w.PlanPub)).Inhalt);            // Klasse des Kindes
        IstPdf((await druck.LehrerWocheAsync(w.Lehrer, OrgWelt.Heute)).Inhalt);

        await Assert.ThrowsAsync<KeinZugriffFehler>(() => druck.PlanAsync(w.Lehrer2, w.PlanPub));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => druck.PlanAsync(w.SchuelerOhne, w.PlanPub));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => druck.PlanAsync(w.Schueler, w.PlanEntwurf));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => druck.PlanAsync(w.DirektorB, w.PlanPub));

        var daten = await w.D.Stundenplan.PlanPdfAsync(w.Direktor, w.PlanPub);
        Vorschau(StundenplanPdf.Dokument(new SchulKopf(daten.Schule, null), daten.Titel, daten.Untertitel, daten.Stunden, false, OrgWelt.Heute), "edt");
    }

    [Fact]
    public void Dateinamen_ohne_Sonderzeichen()
    {
        Assert.Equal("emploi-du-temps-6e-a", DruckService.DateiTeil("Emploi du temps · 6e A"));
        Assert.Equal("cm2-b", DruckService.DateiTeil("CM2 « B »"));
        Assert.Equal("document", DruckService.DateiTeil("···"));
    }
}
