using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class BewertungsServiceTests
{
    [Fact]
    public async Task Notenhistorie_ErfasstNurEchteAenderungenUndPrueftFachrechte()
    {
        var d = await VorbereitenAsync();
        var service = new BewertungsService(d.Factory);
        await service.NoteSpeichernAsync(d.Lehrer, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 10, "Initial");
        await service.NoteSpeichernAsync(d.Lehrer, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 10, " Initial ");
        await service.NoteSpeichernAsync(d.Koordinator, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 15, "Correction");
        var historie = await service.HistorieAsync(d.Direktor, d.Bestandteil, d.FachBewertung, d.JahresZuordnung);
        Assert.Equal(2, historie.Count);
        Assert.Contains(historie, x => x.AltePunkte == null && x.NeuePunkte == 10 && x.BearbeitetVonID == d.Lehrer);
        Assert.Contains(historie, x => x.AltePunkte == 10 && x.NeuePunkte == 15 && x.AlteBemerkung == "Initial" && x.NeueBemerkung == "Correction");
        await using var db = d.Factory.CreateDbContext();
        db.Remove(await db.Set<UnterrichtsZuordnung>().SingleAsync()); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.HistorieAsync(d.Lehrer, d.Bestandteil, d.FachBewertung, d.JahresZuordnung));
    }

    [Fact]
    public async Task Layoutoptionen_WerdenGeprueftUndImSnapshotBewahrt()
    {
        var d = await VorbereitenAsync();
        var service = new BulletinService(d.Factory, new(d.Factory));
        await using var db = d.Factory.CreateDbContext();
        var klasse = (await db.Set<SchuelerJahresZuordnung>().SingleAsync()).KlasseID;
        var periode = (await db.Set<BewertungsPeriode>().SingleAsync()).Id;
        var vorlage = new BulletinVorlage { SchulID = d.Schule, KlasseID = klasse, Art = BewertungsArt.Trimester, Name = "Compact", SchriftGroesse = 7 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VorlageSpeichernAsync(d.Direktor, vorlage));
        vorlage.SchriftGroesse = 9; vorlage.SeitenRand = 30; vorlage.ZeigeLehrkraefte = false; vorlage.ZeigeAnwesenheit = false;
        await service.VorlageSpeichernAsync(d.Direktor, vorlage);
        var snapshot = await service.VorschauAsync(d.Direktor, d.Schule, periode, d.JahresZuordnung, null);
        Assert.Equal(9, snapshot.SchriftGroesse); Assert.Equal(30, snapshot.SeitenRand); Assert.False(snapshot.ZeigeLehrkraefte);
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        Assert.NotEmpty(new BulletinPdfService().Erzeugen(snapshot));
        var json = System.Text.Json.JsonSerializer.SerializeToNode(snapshot)!.AsObject();
        json.Remove("SchriftGroesse"); json.Remove("SeitenRand"); json.Remove("ZeigeLehrkraefte"); json.Remove("ZeigeAnwesenheit");
        var alt = System.Text.Json.JsonSerializer.Deserialize<BulletinSnapshot>(json.ToJsonString())!;
        Assert.Equal(10, alt.SchriftGroesse); Assert.Equal(25, alt.SeitenRand); Assert.True(alt.ZeigeLehrkraefte); Assert.True(alt.ZeigeAnwesenheit);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sammelausgabe_NurVeroeffentlichteSnapshotsDerKlasseUndPeriode(bool koordinator)
    {
        var d = await VorbereitenAsync();
        var service = new BulletinService(d.Factory, new(d.Factory));
        Guid periode, klasse, schueler;
        await using (var db = d.Factory.CreateDbContext())
        {
            periode = (await db.Set<BewertungsPeriode>().SingleAsync()).Id;
            var zuordnung = await db.Set<SchuelerJahresZuordnung>().SingleAsync();
            klasse = zuordnung.KlasseID; schueler = zuordnung.SchuelerID;
        }
        var user = koordinator ? d.Koordinator : d.Direktor;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SammelausgabeAsync(user, d.Schule, periode, klasse));
        await new BewertungsService(d.Factory).NoteSpeichernAsync(d.Direktor, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 15, null);
        await service.VorlageSpeichernAsync(d.Direktor, new() { SchulID = d.Schule, KlasseID = klasse, Art = BewertungsArt.Trimester, Name = "Modèle" });
        var id = await service.VeroeffentlichenAsync(d.Direktor, d.Schule, periode, d.JahresZuordnung, "Version initiale", "Édition");
        var snapshot = (await service.LesenAsync(d.Direktor, id))!;
        Guid andereKlasse = Guid.NewGuid(), anderePeriode = Guid.NewGuid();
        await using (var db = d.Factory.CreateDbContext())
        {
            db.Klassen.Add(new Klasse { Id = andereKlasse, SchoolId = d.Schule, Code = "5e" });
            db.Add(new BewertungsPeriode { Id = anderePeriode, SchulID = d.Schule, SchuljahrID = d.Jahr, Titel = "Test", Art = BewertungsArt.Test, Reihenfolge = 2 });
            var andererSchueler = Guid.NewGuid(); var andereZuordnung = Guid.NewGuid();
            db.Users.Add(new AppUser { Id = andererSchueler });
            db.Add(new SchuelerJahresZuordnung { Id = andereZuordnung, SchulID = d.Schule, SchuljahrID = d.Jahr, KlasseID = andereKlasse, SchuelerID = andererSchueler });
            db.Add(new BulletinAusgabe { Id = Guid.NewGuid(), SchulID = d.Schule, PeriodeID = periode, JahresZuordnungID = andereZuordnung,
                IstVeroeffentlicht = true, SnapshotJson = System.Text.Json.JsonSerializer.Serialize(snapshot with { Schueler = "Autre classe" }), ErstelltVonID = d.Direktor });
            db.Add(new BulletinAusgabe { Id = Guid.NewGuid(), SchulID = d.Schule, PeriodeID = anderePeriode, JahresZuordnungID = d.JahresZuordnung,
                IstVeroeffentlicht = true, SnapshotJson = System.Text.Json.JsonSerializer.Serialize(snapshot with { Titel = "Autre période" }), ErstelltVonID = d.Direktor });
            await db.SaveChangesAsync();
        }
        Assert.Equal(15m, Assert.Single(await service.SammelausgabeAsync(user, d.Schule, periode, klasse)).Durchschnitt);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SammelausgabeAsync(d.Lehrer, d.Schule, periode, klasse));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SammelausgabeAsync(schueler, d.Schule, periode, klasse));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SammelausgabeAsync(user, Guid.NewGuid(), periode, klasse));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SammelausgabeAsync(user, d.Schule, Guid.NewGuid(), klasse));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SammelausgabeAsync(user, d.Schule, periode, Guid.NewGuid()));
        await service.ZurueckziehenAsync(d.Direktor, id, "Correction");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SammelausgabeAsync(user, d.Schule, periode, klasse));
        await new BewertungsService(d.Factory).NoteSpeichernAsync(d.Direktor, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 18, null);
        await service.VeroeffentlichenAsync(d.Direktor, d.Schule, periode, d.JahresZuordnung, "Corrigé", "Correction");
        Assert.Equal(18m, Assert.Single(await service.SammelausgabeAsync(user, d.Schule, periode, klasse)).Durchschnitt);
        Assert.Equal(15m, (await service.LesenAsync(d.Direktor, id))!.Durchschnitt);
        await using (var db = d.Factory.CreateDbContext())
        {
            (await db.Users.SingleAsync(x => x.Id == user)).IsBlocked = true;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SammelausgabeAsync(user, d.Schule, periode, klasse));
    }

    [Fact]
    public async Task SammelPdf_JederSchuelerBeginntAufEigenerSeite()
    {
        var d = await VorbereitenAsync();
        var service = new BulletinService(d.Factory, new(d.Factory));
        await new BewertungsService(d.Factory).NoteSpeichernAsync(d.Direktor, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 15, null);
        await using var db = d.Factory.CreateDbContext();
        var periode = (await db.Set<BewertungsPeriode>().SingleAsync()).Id;
        var klasse = (await db.Set<SchuelerJahresZuordnung>().SingleAsync()).KlasseID;
        await service.VorlageSpeichernAsync(d.Direktor, new() { SchulID = d.Schule, KlasseID = klasse, Art = BewertungsArt.Trimester, Name = "Modèle" });
        var snapshot = await service.VorschauAsync(d.Direktor, d.Schule, periode, d.JahresZuordnung, "Bon travail");
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var pdf = new BulletinPdfService();
        var daten = pdf.SammelausgabeErzeugen([snapshot, snapshot with { Schueler = "Deuxième élève" }]);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(daten, 0, 5));
        var text = System.Text.Encoding.Latin1.GetString(daten);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page\b").Count);
        Assert.Throws<InvalidOperationException>(() => pdf.SammelausgabeErzeugen([]));
        Assert.Throws<InvalidOperationException>(() => pdf.SammelausgabeErzeugen(Enumerable.Repeat(snapshot, 201).ToArray()));
    }

    [Fact]
    public async Task Jahresbulletin_BenoetigtDreiVollstaendigeTrimesterUndIgnoriertTests()
    {
        var d = await VorbereitenAsync();
        var noten = new BewertungsService(d.Factory);
        var service = new BulletinService(d.Factory, noten);
        Guid klasse;
        await using (var db = d.Factory.CreateDbContext())
        {
            var erste = await db.Set<BewertungsPeriode>().SingleAsync();
            erste.JahresGewicht = 1;
            klasse = (await db.Set<SchuelerJahresZuordnung>().SingleAsync()).KlasseID;
            await db.SaveChangesAsync();
        }
        await service.VorlageSpeichernAsync(d.Direktor, new() { SchulID = d.Schule, KlasseID = klasse, Art = BewertungsArt.Trimester, Name = "Annuel" });
        await noten.NoteSpeichernAsync(d.Direktor, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 10, null);
        var dritte = await noten.PeriodeAnlegenAsync(d.Direktor, d.Schule, d.Jahr, "3e trimestre", BewertungsArt.Trimester, 3, 1);
        var dritteKomponente = await noten.BestandteilAnlegenAsync(d.Direktor, dritte, "Composition", 1, 20);
        await noten.NoteSpeichernAsync(d.Direktor, dritteKomponente, d.FachBewertung, d.JahresZuordnung, 18, null);
        Assert.Null((await service.VorschauAsync(d.Direktor, d.Schule, dritte, d.JahresZuordnung, null)).JahresNote);
        var zweite = await noten.PeriodeAnlegenAsync(d.Direktor, d.Schule, d.Jahr, "2e trimestre", BewertungsArt.Trimester, 2, 1);
        var zweiteKomponente = await noten.BestandteilAnlegenAsync(d.Direktor, zweite, "Composition", 1, 20);
        Assert.Null((await service.VorschauAsync(d.Direktor, d.Schule, dritte, d.JahresZuordnung, null)).JahresNote);
        await noten.NoteSpeichernAsync(d.Direktor, zweiteKomponente, d.FachBewertung, d.JahresZuordnung, 14, null);
        var test = await noten.PeriodeAnlegenAsync(d.Direktor, d.Schule, d.Jahr, "Examen blanc", BewertungsArt.Test, 4, 100);
        var testKomponente = await noten.BestandteilAnlegenAsync(d.Direktor, test, "Épreuve", 1, 20);
        await noten.NoteSpeichernAsync(d.Direktor, testKomponente, d.FachBewertung, d.JahresZuordnung, 0, null);
        Assert.Equal(14m, (await service.VorschauAsync(d.Direktor, d.Schule, dritte, d.JahresZuordnung, null)).JahresNote);
        await service.VorlageSpeichernAsync(d.Direktor, new() { SchulID = d.Schule, KlasseID = klasse, Art = BewertungsArt.Test, Name = "Test" });
        Assert.Null((await service.VorschauAsync(d.Direktor, d.Schule, test, d.JahresZuordnung, null)).JahresNote);
    }

    [Fact]
    public async Task Periodensperre_PrueftRollenWiederoeffnungUndGeschlossenesJahr()
    {
        var d = await VorbereitenAsync();
        var service = new BewertungsService(d.Factory);
        Guid periode;
        await using (var db = d.Factory.CreateDbContext())
            periode = (await db.Set<BewertungsPeriode>().SingleAsync()).Id;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PeriodeSchliessenAsync(d.Lehrer, periode, true));
        await service.PeriodeSchliessenAsync(d.Koordinator, periode, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.NoteSpeichernAsync(d.Direktor, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 12, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AnwesenheitsService(d.Factory).SpeichernAsync(d.Direktor,
            periode, d.JahresZuordnung, new(2026, 10, 5), AnwesenheitsStatus.Anwesend, false, 0, null));
        await service.PeriodeSchliessenAsync(d.Direktor, periode, false);
        await service.NoteSpeichernAsync(d.Lehrer, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 12, null);
        await using (var db = d.Factory.CreateDbContext())
        {
            (await db.Set<Schuljahr>().SingleAsync()).IstGeschlossen = true;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PeriodeSchliessenAsync(d.Direktor, periode, false));
    }

    [Fact]
    public async Task Bewertungskonfiguration_VerweigertMehrAlsZweiDezimalstellen()
    {
        var d = await VorbereitenAsync();
        var service = new BewertungsService(d.Factory);
        await using var db = d.Factory.CreateDbContext();
        var periode = (await db.Set<BewertungsPeriode>().SingleAsync()).Id;
        var fach = await db.Set<FachBewertung>().SingleAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PeriodeAnlegenAsync(d.Direktor, d.Schule, d.Jahr, "Test", BewertungsArt.Allgemein, 2, 1.001m));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BestandteilAnlegenAsync(d.Direktor, periode, "Test", 1.001m, 20));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.FachFestlegenAsync(d.Direktor, d.Schule, d.Jahr, fach.KlasseID, fach.FachID, 1.001m));
    }

    [Fact]
    public async Task Anwesenheit_PrueftRollenDatumUndSnapshotSperre()
    {
        var d = await VorbereitenAsync();
        var service = new AnwesenheitsService(d.Factory);
        Guid periode, klasse;
        await using (var db = d.Factory.CreateDbContext())
        {
            periode = (await db.Set<BewertungsPeriode>().SingleAsync()).Id;
            klasse = (await db.Set<SchuelerJahresZuordnung>().SingleAsync()).KlasseID;
        }
        var datum = new DateOnly(2026, 10, 5);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SpeichernAsync(d.Lehrer, periode, d.JahresZuordnung, datum, AnwesenheitsStatus.Abwesend, false, 0, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SpeichernAsync(d.Direktor, periode, d.JahresZuordnung, new(2025, 1, 1), AnwesenheitsStatus.Abwesend, false, 0, null));
        await service.SpeichernAsync(d.Koordinator, periode, d.JahresZuordnung, datum, AnwesenheitsStatus.Abwesend, true, 30, null);
        var eintrag = Assert.Single(await service.LadenAsync(d.Direktor, d.Schule, periode));
        Assert.True(eintrag.IstEntschuldigt);
        Assert.Equal(0, eintrag.VerspaetungsMinuten);
        await service.SpeichernAsync(d.Direktor, periode, d.JahresZuordnung, datum, AnwesenheitsStatus.Verspaetet, false, 15, null);
        Assert.Single(await service.LadenAsync(d.Koordinator, d.Schule, periode));
        var noten = new BewertungsService(d.Factory);
        await noten.NoteSpeichernAsync(d.Direktor, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 15, null);
        var bulletin = new BulletinService(d.Factory, noten);
        await bulletin.VorlageSpeichernAsync(d.Direktor, new() { SchulID = d.Schule, KlasseID = klasse, Art = BewertungsArt.Trimester, Name = "Présences" });
        var id = await bulletin.VeroeffentlichenAsync(d.Direktor, d.Schule, periode, d.JahresZuordnung, null, "Édition");
        var snapshot = await bulletin.LesenAsync(d.Direktor, id);
        Assert.Equal(1, snapshot!.ErfassteTage);
        Assert.Equal(1, snapshot.Verspaetungen);
        Assert.Equal(15, snapshot.VerspaetungsMinuten);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SpeichernAsync(d.Koordinator, periode, d.JahresZuordnung, datum, AnwesenheitsStatus.Anwesend, false, 0, null));
    }

    [Fact]
    public async Task Bulletin_BlockDesignerVerweigertUngueltigeReihenfolgeUndFremdeBilder()
    {
        var d = await VorbereitenAsync();
        var service = new BulletinService(d.Factory, new(d.Factory));
        await using var db = d.Factory.CreateDbContext();
        var klasse = (await db.Set<SchuelerJahresZuordnung>().SingleAsync()).KlasseID;
        var vorlage = new BulletinVorlage { SchulID = d.Schule, KlasseID = klasse, Name = "Test", BlockReihenfolge = "Noten,Noten" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VorlageSpeichernAsync(d.Direktor, vorlage));
        vorlage.BlockReihenfolge = "Noten,Ergebnisse,Schueler,Bemerkung,Unterschriften";
        vorlage.LogoAssetID = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VorlageSpeichernAsync(d.Direktor, vorlage));
        vorlage.LogoAssetID = null;
        await service.VorlageSpeichernAsync(d.Direktor, vorlage);
        Assert.Equal(vorlage.BlockReihenfolge, Assert.Single(await service.VorlagenAsync(d.Direktor, d.Schule)).BlockReihenfolge);
    }

    [Fact]
    public async Task Bulletin_SnapshotBleibtUnveraendertUndVersionierungSchuetztSchueler()
    {
        var d = await VorbereitenAsync();
        var noten = new BewertungsService(d.Factory);
        var service = new BulletinService(d.Factory, noten);
        await noten.NoteSpeichernAsync(d.Direktor, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 15, null);
        Guid periode, klasse, schueler;
        await using (var db = d.Factory.CreateDbContext())
        {
            periode = (await db.Set<BewertungsBestandteil>().SingleAsync()).PeriodeID;
            var zuordnung = await db.Set<SchuelerJahresZuordnung>().SingleAsync();
            klasse = zuordnung.KlasseID; schueler = zuordnung.SchuelerID;
        }
        await service.VorlageSpeichernAsync(d.Direktor, new BulletinVorlage { SchulID = d.Schule, KlasseID = klasse,
            Art = BewertungsArt.Trimester, Name = "Modèle collège" });
        var id = await service.VeroeffentlichenAsync(d.Koordinator, d.Schule, periode, d.JahresZuordnung, "Bon travail", "Première édition");
        var snapshot = await service.LesenAsync(schueler, id);
        Assert.NotNull(snapshot);
        Assert.Equal(15m, snapshot.Durchschnitt);
        Assert.Single(await service.ListeAsync(schueler, d.Schule));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LesenAsync(d.Lehrer, id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => noten.NoteSpeichernAsync(d.Lehrer, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 18, null));
        await service.ZurueckziehenAsync(d.Direktor, id, "Correction de saisie");
        await using (var db = d.Factory.CreateDbContext())
        {
            var historique = await db.Set<BulletinAusgabe>().SingleAsync(x => x.Id == id);
            Assert.Equal("Première édition", historique.AenderungsGrund);
            Assert.Equal("Correction de saisie", historique.RuecknahmeGrund);
            Assert.Equal(d.Direktor, historique.ZurueckgezogenVonID);
            Assert.NotNull(historique.ZurueckgezogenAmUtc);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ZurueckziehenAsync(d.Koordinator, id, "Autre motif"));
        Assert.Empty(await service.ListeAsync(schueler, d.Schule));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LesenAsync(schueler, id));
        await noten.NoteSpeichernAsync(d.Lehrer, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 18, null);
        var ancien = await service.LesenAsync(d.Direktor, id);
        Assert.Equal(15m, ancien!.Durchschnitt);
        var neu = await service.VeroeffentlichenAsync(d.Direktor, d.Schule, periode, d.JahresZuordnung, "Corrigé", "Correction");
        Assert.Equal(18m, (await service.LesenAsync(schueler, neu))!.Durchschnitt);
        Assert.Equal(2, (await service.ListeAsync(d.Direktor, d.Schule)).Max(x => x.Version));
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var pdf = new BulletinPdfService().Erzeugen(ancien);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
    }

    [Fact]
    public async Task Bulletin_VerweigertUnvollstaendigeNotenUndFremdeVorlagen()
    {
        var d = await VorbereitenAsync();
        var service = new BulletinService(d.Factory, new BewertungsService(d.Factory));
        Guid periode, klasse;
        await using (var db = d.Factory.CreateDbContext())
        {
            periode = (await db.Set<BewertungsBestandteil>().SingleAsync()).PeriodeID;
            klasse = (await db.Set<SchuelerJahresZuordnung>().SingleAsync()).KlasseID;
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.VorlageSpeichernAsync(d.Lehrer,
            new BulletinVorlage { SchulID = d.Schule, KlasseID = klasse, Name = "Test" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VorlageSpeichernAsync(d.Direktor,
            new BulletinVorlage { SchulID = d.Schule, KlasseID = Guid.NewGuid(), Name = "Test" }));
        await service.VorlageSpeichernAsync(d.Direktor, new BulletinVorlage { SchulID = d.Schule, KlasseID = klasse,
            Art = BewertungsArt.Trimester, Name = "Test" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VeroeffentlichenAsync(d.Direktor, d.Schule,
            periode, d.JahresZuordnung, null, "Première édition"));
    }

    [Theory]
    [InlineData("SchulLeiter")]
    [InlineData("Koordinator")]
    [InlineData("Lehrer")]

    public async Task BerechtigteRollen_KoennenNotenErfassen(string rolle)
    {
        var d = await VorbereitenAsync();
        var benutzer = rolle == "SchulLeiter" ? d.Direktor : rolle == "Koordinator" ? d.Koordinator : d.Lehrer;
        var service = new BewertungsService(d.Factory);
        await service.NoteSpeichernAsync(benutzer, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 15, "Bien");
        await service.NoteSpeichernAsync(benutzer, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 16, "Très bien");
        var note = Assert.Single((await service.LadenAsync(benutzer, d.Schule, d.Jahr)).Noten);
        Assert.Equal(16m, note.Punkte);
        Assert.Equal(benutzer, note.BearbeitetVonID);
    }

    [Fact]
    public async Task FremderLehrer_UndFehlendeZuordnung_WerdenAbgewiesen()
    {
        var d = await VorbereitenAsync();
        var service = new BewertungsService(d.Factory);
        await using (var db = d.Factory.CreateDbContext())
        {
            db.Remove(await db.Set<UnterrichtsZuordnung>().SingleAsync());
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.NoteSpeichernAsync(d.Lehrer, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 12, null));
        Assert.Empty((await service.LadenAsync(d.Lehrer, d.Schule, d.Jahr)).Faecher);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LadenAsync(d.Direktor, Guid.NewGuid(), d.Jahr));
    }

    [Fact]
    public async Task BarèmeUndGeschlossenePeriode_SperrenUngueltigeNoten()
    {
        var d = await VorbereitenAsync();
        var service = new BewertungsService(d.Factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.NoteSpeichernAsync(d.Direktor, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 21, null));
        await using (var db = d.Factory.CreateDbContext())
        {
            (await db.Set<BewertungsPeriode>().SingleAsync()).IstGeschlossen = true;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.NoteSpeichernAsync(d.Koordinator, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 12, null));
    }

    [Fact]
    public async Task TestPeriode_ErhaeltImmerJahresGewichtNull()
    {
        var d = await VorbereitenAsync();
        var service = new BewertungsService(d.Factory);
        await service.PeriodeAnlegenAsync(d.Direktor, d.Schule, d.Jahr, "Examen blanc", BewertungsArt.Test, 4, 99);
        var test = (await service.LadenAsync(d.Direktor, d.Schule, d.Jahr)).Perioden.Single(x => x.Art == BewertungsArt.Test);
        Assert.Equal(0m, test.JahresGewicht);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PeriodeAnlegenAsync(d.Direktor, d.Schule, d.Jahr, "Trimester 4", BewertungsArt.Trimester, 4, 1));
    }

    private static async Task<Daten> VorbereitenAsync()
    {
        var f = new TestApplicationDbContextFactory();
        await using var db = f.CreateDbContext();
        var schule = Guid.NewGuid(); var direktor = Guid.NewGuid(); var koordinator = Guid.NewGuid(); var lehrer = Guid.NewGuid();
        var schueler = Guid.NewGuid(); var jahr = Guid.NewGuid(); var klasse = Guid.NewGuid(); var fach = Guid.NewGuid();
        var periode = Guid.NewGuid(); var bestandteil = Guid.NewGuid(); var bewertung = Guid.NewGuid(); var zuordnung = Guid.NewGuid();
        db.Schools.Add(new School { Id = schule, Name = "École" });
        foreach (var (id, name) in new[] { (direktor, Rollen.SchulLeiter), (koordinator, Rollen.Koordinator), (lehrer, Rollen.Lehrer), (schueler, Rollen.Schueler) })
        {
            db.Users.Add(new AppUser { Id = id });
            var rolle = new Role { Id = Guid.NewGuid(), Name = name };
            db.Roles.Add(rolle); db.UserRoles.Add(new AppUserRole { UserId = id, RoleId = rolle.Id });
            db.UserSchools.Add(new UserSchool { AppUserId = id, SchoolId = schule });
        }
        db.Klassen.Add(new Klasse { Id = klasse, SchoolId = schule, Code = "6e" });
        db.Faecher.Add(new Fach { Id = fach, SchoolId = schule, Name = "Mathématiques" });
        db.Add(new Schuljahr { Id = jahr, SchulID = schule, Bezeichnung = "2026–2027", Beginn = new(2026, 9, 1), Ende = new(2027, 7, 31) });
        db.Add(new UnterrichtsZuordnung { Id = Guid.NewGuid(), SchulID = schule, SchuljahrID = jahr, KlasseID = klasse, FachID = fach, LehrerID = lehrer, Wochenstunden = 2 });
        db.Add(new SchuelerJahresZuordnung { Id = zuordnung, SchulID = schule, SchuljahrID = jahr, KlasseID = klasse, SchuelerID = schueler });
        db.Add(new BewertungsPeriode { Id = periode, SchulID = schule, SchuljahrID = jahr, Titel = "1er trimestre", Art = BewertungsArt.Trimester, Reihenfolge = 1 });
        db.Add(new BewertungsBestandteil { Id = bestandteil, PeriodeID = periode, Bezeichnung = "Composition", Gewicht = 1, MaximalPunkte = 20 });
        db.Add(new FachBewertung { Id = bewertung, SchuljahrID = jahr, KlasseID = klasse, FachID = fach, Koeffizient = 2 });
        await db.SaveChangesAsync();
        return new(f, schule, direktor, koordinator, lehrer, jahr, bestandteil, bewertung, zuordnung);
    }
    private sealed record Daten(TestApplicationDbContextFactory Factory, Guid Schule, Guid Direktor, Guid Koordinator, Guid Lehrer,
        Guid Jahr, Guid Bestandteil, Guid FachBewertung, Guid JahresZuordnung);
}
