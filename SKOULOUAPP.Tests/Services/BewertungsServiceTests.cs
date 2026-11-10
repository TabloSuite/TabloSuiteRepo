using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    public async Task StammdatenBerichtigung_ErsetztNurEigeneDokumenteUndBewahrtOriginale()
    {
        var d = await VorbereitenAsync(); var noten = new BewertungsService(d.Factory); var service = new BulletinService(d.Factory, noten);
        var zweiter = Guid.NewGuid(); var zweiteZuordnung = Guid.NewGuid(); Guid periode, klasse;
        await using (var db = d.Factory.CreateDbContext())
        {
            var ersterID = (await db.Set<SchuelerJahresZuordnung>().SingleAsync()).SchuelerID;
            (await db.Users.SingleAsync(x => x.Id == ersterID)).FullName = "Nom initial";
            periode = (await db.Set<BewertungsPeriode>().SingleAsync()).Id; klasse = (await db.Klassen.SingleAsync()).Id;
            db.Users.Add(new AppUser { Id = zweiter, FullName = "Autre élève" });
            db.Add(new SchuelerJahresZuordnung { Id = zweiteZuordnung, SchulID = d.Schule, SchuljahrID = d.Jahr, KlasseID = klasse, SchuelerID = zweiter });
            await db.SaveChangesAsync();
        }
        await noten.NoteSpeichernAsync(d.Direktor, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 10, null);
        await noten.NoteSpeichernAsync(d.Direktor, d.Bestandteil, d.FachBewertung, zweiteZuordnung, 15, null);
        await service.VorlageSpeichernAsync(d.Direktor, new() { SchulID = d.Schule, KlasseID = klasse, Art = BewertungsArt.Trimester, Name = "Officiel" });
        var p2 = await noten.PeriodeAnlegenAsync(d.Direktor, d.Schule, d.Jahr, "2e trimestre", BewertungsArt.Trimester, 2, 1);
        var c2 = await noten.BestandteilAnlegenAsync(d.Direktor, p2, "Composition", 1, 20);
        await noten.NoteSpeichernAsync(d.Direktor, c2, d.FachBewertung, d.JahresZuordnung, 12, null);
        await noten.NoteSpeichernAsync(d.Direktor, c2, d.FachBewertung, zweiteZuordnung, 14, null);
        foreach (var p in new[] { periode, p2 })
        {
            var v = await service.KlassePruefenAsync(d.Direktor, d.Schule, p, klasse);
            await service.KlasseEndgueltigVeroeffentlichenAsync(d.Direktor, d.Schule, p, klasse, v.Hash, true);
        }
        Dictionary<Guid, string> originale;
        await using (var db = d.Factory.CreateDbContext()) originale = await db.Set<BulletinAusgabe>().ToDictionaryAsync(x => x.Id, x => x.SnapshotJson);
        var id = await service.BerichtigungStartenAsync(d.Direktor, d.Schule, periode, klasse, "Erreur d'identité");
        var eingabe = await service.BerichtigungsStammdatenLadenAsync(d.Direktor, id, d.JahresZuordnung);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.BerichtigungsStammdatenAsync(d.Lehrer, id, eingabe));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BerichtigungsStammdatenAsync(d.Direktor, id, eingabe with { JahresZuordnungID = Guid.NewGuid() }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BerichtigungsStammdatenAsync(d.Direktor, id, eingabe with { Name = " " }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BerichtigungsStammdatenAsync(d.Direktor, id, eingabe with { Geburtsdatum = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1) }));
        await service.BerichtigungsStammdatenAsync(d.Koordinator, id, eingabe);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BerichtigungPruefenAsync(d.Direktor, id)); // Keine wirkliche Änderung.
        eingabe = eingabe with { Name = "Nom corrigé", Matrikelnummer = "EL-CORRIGE", Geburtsort = "Lomé", Geburtsdatum = new DateOnly(2012, 2, 29) };
        await service.BerichtigungsStammdatenAsync(d.Koordinator, id, eingabe);
        Assert.Equal(eingabe, await service.BerichtigungsStammdatenLadenAsync(d.Direktor, id, d.JahresZuordnung));
        await using (var db = d.Factory.CreateDbContext())
            Assert.All(await db.Set<BulletinAusgabe>().ToListAsync(), x => Assert.Equal(originale[x.Id], x.SnapshotJson));
        var pruefung = await service.BerichtigungPruefenAsync(d.Direktor, id);
        Assert.Equal(2, pruefung.Auswirkungen.Count);
        Assert.All(pruefung.Auswirkungen, x => { Assert.Equal("Nom corrigé", x.Vorschau.Schueler); Assert.Equal(x.Alt, x.Neu); Assert.Equal(x.AlterRang, x.NeuerRang); });
        await service.BerichtigungsStammdatenAsync(d.Direktor, id, eingabe with { Geburtsort = "Kara" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BerichtigungFreigebenAsync(d.Direktor, id, pruefung.Hash, true));
        pruefung = await service.BerichtigungPruefenAsync(d.Direktor, id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BerichtigungFreigebenAsync(d.Direktor, id, pruefung.Hash, false));
        Assert.Equal(2, await service.BerichtigungFreigebenAsync(d.Koordinator, id, pruefung.Hash, true));
        await using (var db = d.Factory.CreateDbContext())
        {
            Assert.Equal(6, await db.Set<BulletinAusgabe>().CountAsync());
            Assert.All(await db.Set<BulletinAusgabe>().Where(x => x.JahresZuordnungID == zweiteZuordnung).ToListAsync(), x =>
            { Assert.True(x.IstVeroeffentlicht); Assert.Null(x.ErsetztDurchID); Assert.Equal(originale[x.Id], x.SnapshotJson); });
            Assert.All(await db.Set<BulletinAusgabe>().Where(x => x.JahresZuordnungID == d.JahresZuordnung && !x.IstVeroeffentlicht).ToListAsync(), x => Assert.Equal(originale[x.Id], x.SnapshotJson));
            var userID = (await db.Set<SchuelerJahresZuordnung>().SingleAsync(x => x.Id == d.JahresZuordnung)).SchuelerID;
            Assert.NotEqual("Nom corrigé", (await db.Users.SingleAsync(x => x.Id == userID)).FullName);
            Assert.Equal(4, await db.Set<NotenAenderung>().CountAsync()); // Nur ursprüngliche Notenerfassung.
            foreach (var neu in await db.Set<BulletinAusgabe>().Where(x => x.JahresZuordnungID == d.JahresZuordnung && x.IstVeroeffentlicht).ToListAsync())
            {
                var s = await service.LesenAsync(d.Direktor, neu.Id);
                Assert.Equal("Nom corrigé", s!.Schueler); Assert.Equal("Kara", s.Geburtsort); Assert.Equal("29/02/2012", s.Geburtsdatum);
                Assert.Equal("EL-CORRIGE", s.Matrikelnummer); Assert.NotNull(s.ErsetztDokumentNummer);
            }
        }
    }

    [Fact]
    public async Task EndgueltigerAbschluss_BerichtigungUndQrBewahrenOriginal()
    {
        var d = await VorbereitenAsync(); var noten = new BewertungsService(d.Factory); var service = new BulletinService(d.Factory, noten);
        Guid periode, klasse, noteID, schueler;
        await noten.NoteSpeichernAsync(d.Direktor, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 10, "Initial");
        await using (var db = d.Factory.CreateDbContext())
        {
            periode = (await db.Set<BewertungsPeriode>().SingleAsync()).Id;
            var zuordnung = await db.Set<SchuelerJahresZuordnung>().SingleAsync(); klasse = zuordnung.KlasseID; schueler = zuordnung.SchuelerID;
            noteID = (await db.Set<FachNote>().SingleAsync()).Id;
        }
        await service.VorlageSpeichernAsync(d.Direktor, new() { SchulID = d.Schule, KlasseID = klasse, Art = BewertungsArt.Trimester, Name = "Officiel" });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.KlassePruefenAsync(d.Lehrer, d.Schule, periode, klasse));
        var pruefung = await service.KlassePruefenAsync(d.Direktor, d.Schule, periode, klasse);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.KlasseEndgueltigVeroeffentlichenAsync(d.Direktor, d.Schule, periode, klasse, pruefung.Hash, false));
        Assert.Equal(1, await service.KlasseEndgueltigVeroeffentlichenAsync(d.Direktor, d.Schule, periode, klasse, pruefung.Hash, true));
        var originalID = Assert.Single(await service.ListeAsync(schueler, d.Schule)).Id;
        var original = (await service.LesenAsync(d.Direktor, originalID))!;
        Assert.NotNull(original.PruefToken);
        var verifier = new BulletinPruefungService(d.Factory);
        Assert.Equal("Valide", (await verifier.PruefenAsync(original.PruefToken))!.Status);
        Assert.Null(await verifier.PruefenAsync("invalide"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ZurueckziehenAsync(d.Direktor, originalID, "Non"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => noten.NoteSpeichernAsync(d.Direktor, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 15, null));
        var correction = await service.BerichtigungStartenAsync(d.Koordinator, d.Schule, periode, klasse, "Erreur de transcription");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.BerichtigungsNoteAsync(d.Lehrer, correction, noteID, 15, null));
        await service.BerichtigungsNoteAsync(d.Koordinator, correction, noteID, 15, "Corrigé");
        await using (var db = d.Factory.CreateDbContext()) Assert.Equal(10, (await db.Set<FachNote>().SingleAsync()).Punkte);
        var wirkung = await service.BerichtigungPruefenAsync(d.Direktor, correction);
        Assert.Equal(15, Assert.Single(wirkung.Auswirkungen).Neu);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BerichtigungFreigebenAsync(d.Direktor, correction, wirkung.Hash, false));
        Assert.Equal(1, await service.BerichtigungFreigebenAsync(d.Direktor, correction, wirkung.Hash, true));
        Assert.Equal(10, (await service.LesenAsync(d.Direktor, originalID))!.Durchschnitt);
        var neuID = Assert.Single(await service.ListeAsync(schueler, d.Schule), x => x.IstVeroeffentlicht).Id;
        Assert.Contains(await service.ListeAsync(schueler, d.Schule), x => x.Id == originalID && x.ErsetztDurchID == neuID);
        var neu = (await service.LesenAsync(schueler, neuID))!;
        Assert.Equal(15, neu.Durchschnitt); Assert.Equal(original.DokumentNummer, neu.ErsetztDokumentNummer);
        Assert.Equal("Remplacé par un bulletin rectificatif", (await verifier.PruefenAsync(original.PruefToken))!.Status);
        Assert.Equal("Valide", (await verifier.PruefenAsync(neu.PruefToken!))!.Status);
        await service.UebergabeSpeichernAsync(d.Direktor, neuID, false, "Parent informé par téléphone");
        await service.UebergabeSpeichernAsync(d.Koordinator, neuID, true, "Remis au parent");
        Assert.Equal(2, (await service.UebergabenAsync(d.Direktor, neuID)).Count);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UebergabenAsync(schueler, neuID));
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = "https://skoulou.etsetech.com" }).Build();
        Assert.NotEmpty(new BulletinPdfService(config).Erzeugen(neu));
    }

    [Fact]
    public async Task Klassenpruefung_VerlangtVollstaendigkeitUndAktuellenStand()
    {
        var d = await VorbereitenAsync(); var noten = new BewertungsService(d.Factory); var service = new BulletinService(d.Factory, noten);
        await using var db = d.Factory.CreateDbContext();
        var periode = (await db.Set<BewertungsPeriode>().SingleAsync()).Id;
        var klasse = (await db.Set<SchuelerJahresZuordnung>().SingleAsync()).KlasseID;
        await service.VorlageSpeichernAsync(d.Direktor, new() { SchulID = d.Schule, KlasseID = klasse, Art = BewertungsArt.Trimester, Name = "Officiel" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.KlassePruefenAsync(d.Direktor, d.Schule, periode, klasse));
        await noten.NoteSpeichernAsync(d.Direktor, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 10, null);
        var alt = await service.KlassePruefenAsync(d.Direktor, d.Schule, periode, klasse);
        await noten.NoteSpeichernAsync(d.Direktor, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 11, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.KlasseEndgueltigVeroeffentlichenAsync(d.Direktor, d.Schule, periode, klasse, alt.Hash, true));
        Assert.Empty(await service.ListeAsync(d.Direktor, d.Schule));
    }

    [Fact]
    public async Task Berichtigung_PrueftRangAndererSchuelerUndSpaeterePerioden()
    {
        var d = await VorbereitenAsync(); var noten = new BewertungsService(d.Factory); var service = new BulletinService(d.Factory, noten);
        Guid periode, klasse, noteID; var zweiter = Guid.NewGuid(); var zuordnung = Guid.NewGuid();
        await noten.NoteSpeichernAsync(d.Direktor, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 10, null);
        await using (var db = d.Factory.CreateDbContext())
        {
            periode = (await db.Set<BewertungsPeriode>().SingleAsync()).Id; klasse = (await db.Klassen.SingleAsync()).Id;
            noteID = (await db.Set<FachNote>().SingleAsync()).Id;
            db.Users.Add(new AppUser { Id = zweiter, FullName = "Deuxième élève" });
            db.Add(new SchuelerJahresZuordnung { Id = zuordnung, SchulID = d.Schule, SchuljahrID = d.Jahr, KlasseID = klasse, SchuelerID = zweiter });
            await db.SaveChangesAsync();
        }
        await noten.NoteSpeichernAsync(d.Direktor, d.Bestandteil, d.FachBewertung, zuordnung, 15, null);
        await service.VorlageSpeichernAsync(d.Direktor, new() { SchulID = d.Schule, KlasseID = klasse, Art = BewertungsArt.Trimester, Name = "Officiel" });
        var p2 = await noten.PeriodeAnlegenAsync(d.Direktor, d.Schule, d.Jahr, "2e trimestre", BewertungsArt.Trimester, 2, 1);
        var c2 = await noten.BestandteilAnlegenAsync(d.Direktor, p2, "Composition", 1, 20);
        await noten.NoteSpeichernAsync(d.Direktor, c2, d.FachBewertung, d.JahresZuordnung, 12, null);
        await noten.NoteSpeichernAsync(d.Direktor, c2, d.FachBewertung, zuordnung, 14, null);
        foreach (var p in new[] { periode, p2 })
        {
            var v = await service.KlassePruefenAsync(d.Direktor, d.Schule, p, klasse);
            await service.KlasseEndgueltigVeroeffentlichenAsync(d.Direktor, d.Schule, p, klasse, v.Hash, true);
        }
        var id = await service.BerichtigungStartenAsync(d.Direktor, d.Schule, periode, klasse, "Erreur de note");
        await service.BerichtigungsNoteAsync(d.Direktor, id, noteID, 18, null);
        var wirkung = await service.BerichtigungPruefenAsync(d.Direktor, id);
        Assert.Contains(wirkung.Auswirkungen, x => x.AlterRang == 1 && x.NeuerRang == 2);
        Assert.Contains(wirkung.Auswirkungen, x => x.Periode == "2e trimestre" && x.Vorschau.Vorperioden.Any(p => p.Note == 18));
        await service.BerichtigungsNoteAsync(d.Direktor, id, noteID, 19, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BerichtigungFreigebenAsync(d.Direktor, id, wirkung.Hash, true));
        await service.BerichtigungAbbrechenAsync(d.Direktor, id);
        await using var kontrolle = d.Factory.CreateDbContext();
        Assert.Equal(10, (await kontrolle.Set<FachNote>().SingleAsync(x => x.Id == noteID)).Punkte);
    }

    [Fact]
    public async Task VorlagenMehrfachzuweisung_PrueftSchuleRechteUndExplizitesErsetzen()
    {
        var d = await VorbereitenAsync();
        var service = new BulletinService(d.Factory, new(d.Factory));
        Guid erste;
        var zweite = Guid.NewGuid(); var fremde = Guid.NewGuid(); var andereSchule = Guid.NewGuid();
        await using (var db = d.Factory.CreateDbContext())
        {
            erste = (await db.Klassen.SingleAsync()).Id;
            db.Schools.Add(new School { Id = andereSchule, Name = "Autre école" });
            db.Klassen.AddRange(new Klasse { Id = zweite, SchoolId = d.Schule, Code = "5e" },
                new Klasse { Id = fremde, SchoolId = andereSchule, Code = "4e" });
            await db.SaveChangesAsync();
        }
        var entwurf = new BulletinVorlage { SchulID = d.Schule, Art = BewertungsArt.Trimester, Name = "Commun",
            SchriftGroesse = 11, SeitenRand = 30, ZeigeLehrkraefte = false, FussText = "Merci" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.VorlagenZuweisenAsync(d.Lehrer, entwurf, [erste, zweite], false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VorlagenZuweisenAsync(d.Direktor, entwurf, [], false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VorlagenZuweisenAsync(d.Direktor, entwurf, [erste, fremde], true));
        Assert.Empty(await service.VorlagenAsync(d.Direktor, d.Schule));
        Assert.Equal(2, await service.VorlagenZuweisenAsync(d.Koordinator, entwurf, [erste, zweite, erste], false));
        var vorlagen = await service.VorlagenAsync(d.Direktor, d.Schule);
        Assert.Equal(2, vorlagen.Count);
        Assert.Equal(2, vorlagen.Select(x => x.Id).Distinct().Count());
        Assert.All(vorlagen, x => { Assert.Equal(11, x.SchriftGroesse); Assert.Equal(30, x.SeitenRand); Assert.False(x.ZeigeLehrkraefte); Assert.Equal("Merci", x.FussText); });
        entwurf.Name = "Remplacement";
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VorlagenZuweisenAsync(d.Direktor, entwurf, [erste, zweite], false));
        Assert.All(await service.VorlagenAsync(d.Direktor, d.Schule), x => Assert.Equal("Commun", x.Name));
        Assert.Equal(1, await service.VorlagenZuweisenAsync(d.Direktor, entwurf, [erste], true));
        vorlagen = await service.VorlagenAsync(d.Direktor, d.Schule);
        Assert.Equal("Remplacement", vorlagen.Single(x => x.KlasseID == erste).Name);
        Assert.Equal("Commun", vorlagen.Single(x => x.KlasseID == zweite).Name);
        entwurf.SchriftGroesse = 99;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VorlagenZuweisenAsync(d.Direktor, entwurf, [erste, zweite], true));
        Assert.All(await service.VorlagenAsync(d.Direktor, d.Schule), x => Assert.Equal(11, x.SchriftGroesse));
    }

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
    public async Task AllgemeinesBulletin_UebernimmtTeilnotenGruppeKlassenratUndLaesstSichDrucken()
    {
        var d = await VorbereitenAsync();
        var noten = new BewertungsService(d.Factory);
        var service = new BulletinService(d.Factory, noten);
        Guid klasse, periode;
        await using (var db = d.Factory.CreateDbContext())
        {
            klasse = (await db.Set<SchuelerJahresZuordnung>().SingleAsync()).KlasseID;
            periode = (await db.Set<BewertungsPeriode>().SingleAsync()).Id;
            (await db.Faecher.SingleAsync()).Gruppe = "Sciences";
            await db.SaveChangesAsync();
        }
        var classe = await noten.BestandteilAnlegenAsync(d.Direktor, periode, "Classe", 1, 20);
        await service.VorlageSpeichernAsync(d.Direktor, new() { SchulID = d.Schule, KlasseID = klasse, Art = BewertungsArt.Trimester, Name = "Standard" });
        await noten.NoteSpeichernAsync(d.Direktor, d.Bestandteil, d.FachBewertung, d.JahresZuordnung, 15, null);
        await noten.NoteSpeichernAsync(d.Direktor, classe, d.FachBewertung, d.JahresZuordnung, 17, null);

        // Ohne Eintrag: Vorschlag aus der Moyenne (16 → Félicitations).
        var rat = Assert.Single(await service.KlassenratAsync(d.Direktor, d.Schule, periode, klasse));
        Assert.Equal(16m, rat.Moyenne);
        Assert.Equal(KlassenratDistinktion.Felicitations, rat.Vorschlag);
        Assert.Null(rat.Distinktion);

        await service.KlassenratSpeichernAsync(d.Direktor, d.Schule, periode,
            [new KlassenratEingabe(d.JahresZuordnung, KlassenratDistinktion.Encouragements, KlassenratSanktion.AvertissementConduite, "Bon trimestre.", Jahresentscheidung.Offen)]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.KlassenratSpeichernAsync(d.Lehrer, d.Schule, periode,
            [new KlassenratEingabe(d.JahresZuordnung, null, KlassenratSanktion.Keine, null, Jahresentscheidung.Offen)]));

        var bulletin = await service.VorschauAsync(d.Direktor, d.Schule, periode, d.JahresZuordnung, null);
        Assert.NotNull(bulletin.Layout);
        var fach = Assert.Single(bulletin.Faecher);
        Assert.Equal("Sciences", fach.Gruppe);
        Assert.Equal(2, fach.Teilnoten!.Count);
        Assert.Equal(1, fach.Rang);
        Assert.Equal("Très bien", fach.Appreciation);
        Assert.Equal(["Composition", "Classe"], bulletin.TeilnotenSpalten!.OrderByDescending(x => x).ToList());
        Assert.Equal("Tableau d'honneur + Encouragements", bulletin.Distinktion);
        Assert.Equal(["Avertissement conduite"], bulletin.Sanktionen);
        Assert.Equal("Bon trimestre.", bulletin.RatsBemerkung);
        Assert.Equal(16m, bulletin.KlassenMittel);
        Assert.False(bulletin.IstJahresabschluss);

        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var pdf = new BulletinPdfService().Erzeugen(bulletin);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
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
