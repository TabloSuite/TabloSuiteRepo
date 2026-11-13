using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Fehler;
using Skoulou.Core.Organisation;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.Tests.Organisation;

/// <summary>Stundenpläne: Konflikte Klasse/Lehrkraft/Raum (angrenzend erlaubt), Veröffentlichung, Kopie, Sichten je Rolle.</summary>
public class StundenplanTests
{
    private static StundeEingabe Stunde(Guid auftrag, DayOfWeek tag, int vonH, int vonM, int bisH, int bisM, string? raum = null)
        => new(auftrag, tag, new TimeOnly(vonH, vonM), new TimeOnly(bisH, bisM), raum);

    [Fact]
    public void Konfliktpruefung_rein()
    {
        var plan = Guid.NewGuid();
        var klasse = Guid.NewGuid();
        var lehrer = Guid.NewGuid();
        Belegung B(Guid planId, Guid klasseId, Guid lehrerId, int von, int bis, string? raum, bool pub = true, DayOfWeek tag = DayOfWeek.Monday)
            => new(Guid.NewGuid(), planId, klasseId, "6e A", new(2026, 9, 1), new(2027, 6, 30), pub, tag, new(von, 0), new(bis, 0), lehrerId, "Prof", "Maths", raum);

        var neu = B(plan, klasse, lehrer, 8, 9, "Salle 1", pub: false);
        Assert.Null(StundenplanKonflikte.Pruefe(neu, [B(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 9, 10, "Salle 1")]));
        Assert.Null(StundenplanKonflikte.Pruefe(neu, [B(Guid.NewGuid(), Guid.NewGuid(), lehrer, 8, 9, null, tag: DayOfWeek.Tuesday)]));
        Assert.Null(StundenplanKonflikte.Pruefe(neu, [B(Guid.NewGuid(), Guid.NewGuid(), lehrer, 8, 9, null, pub: false)]));
        Assert.Contains("enseigne déjà", StundenplanKonflikte.Pruefe(neu, [B(Guid.NewGuid(), Guid.NewGuid(), lehrer, 7, 9, null)]));
        Assert.Contains("salle", StundenplanKonflikte.Pruefe(neu, [B(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 8, 10, " salle  1")]));
        Assert.Contains("la classe", StundenplanKonflikte.Pruefe(neu, [B(plan, klasse, Guid.NewGuid(), 8, 9, null, pub: false)]));
    }

    [Fact]
    public async Task Konflikte_Lehrkraft_Raum_Klasse_angrenzend_erlaubt()
    {
        var w = await OrgWelt.ErstelleAsync();
        // Lehrer unterrichtet auch in 6e B (Mathématiques); Montag 08:00–09:00 ist er in 6e A (veröffentlicht).
        var auftrag6B = await w.D.Unterricht.LehrauftragSetzenAsync(w.Direktor, w.Kf6BMath, w.Lehrer, 2);
        var lehrer = await Assert.ThrowsAsync<FachFehler>(() => w.D.Stundenplan.StundeSpeichernAsync(w.Direktor, w.PlanEntwurf, null, Stunde(auftrag6B, DayOfWeek.Monday, 8, 30, 9, 30)));
        Assert.Contains("enseigne déjà", lehrer.Message);
        var angrenzend = await w.D.Stundenplan.StundeSpeichernAsync(w.Direktor, w.PlanEntwurf, null, Stunde(auftrag6B, DayOfWeek.Monday, 9, 0, 10, 0, "Salle 2"));

        var raum = await Assert.ThrowsAsync<FachFehler>(() => w.D.Stundenplan.StundeSpeichernAsync(w.Direktor, w.PlanEntwurf, null, Stunde(w.Auftrag6B, DayOfWeek.Monday, 8, 0, 9, 0, "SALLE 1")));
        Assert.Contains("salle", raum.Message);
        var klasse = await Assert.ThrowsAsync<FachFehler>(() => w.D.Stundenplan.StundeSpeichernAsync(w.Direktor, w.PlanEntwurf, null, Stunde(w.Auftrag6B, DayOfWeek.Monday, 9, 30, 10, 30)));
        Assert.Contains("la classe", klasse.Message);
        await w.D.Stundenplan.StundeSpeichernAsync(w.Direktor, w.PlanEntwurf, null, Stunde(w.Auftrag6B, DayOfWeek.Tuesday, 8, 0, 9, 0, "Salle 1"));
        // Eine Stunde verschieben (gleiche Id) kollidiert nicht mit sich selbst.
        await w.D.Stundenplan.StundeSpeichernAsync(w.Direktor, w.PlanEntwurf, angrenzend, Stunde(auftrag6B, DayOfWeek.Monday, 9, 0, 10, 30, "Salle 2"));

        // Ungültige Eingaben.
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Stundenplan.StundeSpeichernAsync(w.Direktor, w.PlanEntwurf, null, Stunde(w.Auftrag6B, DayOfWeek.Friday, 10, 0, 9, 0)));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Stundenplan.StundeSpeichernAsync(w.Direktor, w.PlanEntwurf, null, Stunde(w.Auftrag6B, DayOfWeek.Friday, 5, 0, 7, 0)));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Stundenplan.StundeSpeichernAsync(w.Direktor, w.PlanEntwurf, null, Stunde(w.Auftrag, DayOfWeek.Friday, 10, 0, 11, 0)));

        var detail = await w.D.Stundenplan.PlanAsync(w.Direktor, w.PlanEntwurf);
        Assert.Equal(2, detail.Stunden.Count);
        Assert.Equal(0, detail.Konflikte);
        Assert.Equal(1.5m, detail.Lehrauftraege.Single(l => l.Id == auftrag6B).Ist);
        Assert.Equal(2m, detail.Lehrauftraege.Single(l => l.Id == auftrag6B).Soll);
    }

    [Fact]
    public async Task Veroeffentlichen_nur_mit_Stunden_ohne_doppelten_Plan_und_dann_gesperrt()
    {
        var w = await OrgWelt.ErstelleAsync();
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Stundenplan.VeroeffentlichenAsync(w.Direktor, w.PlanEntwurf));
        var stunde = await w.D.Stundenplan.StundeSpeichernAsync(w.Direktor, w.PlanEntwurf, null, Stunde(w.Auftrag6B, DayOfWeek.Wednesday, 8, 0, 10, 0, "Salle 2"));
        await w.D.Stundenplan.VeroeffentlichenAsync(w.Direktor, w.PlanEntwurf);
        Assert.True((await w.D.Stundenplan.PlanAsync(w.Direktor, w.PlanEntwurf)).Veroeffentlicht);
        Assert.Contains("retirez la publication", (await Assert.ThrowsAsync<FachFehler>(() =>
            w.D.Stundenplan.StundeSpeichernAsync(w.Direktor, w.PlanEntwurf, stunde, Stunde(w.Auftrag6B, DayOfWeek.Wednesday, 8, 0, 9, 0)))).Message);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Stundenplan.StundeLoeschenAsync(w.Direktor, stunde));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Stundenplan.PlanLoeschenAsync(w.Direktor, w.PlanEntwurf));

        // Zweiter Plan derselben Klasse mit überlappender Gültigkeit kann nicht veröffentlicht werden.
        var zweiter = await w.D.Stundenplan.PlanAnlegenAsync(w.Direktor, w.Klasse6B, new(2027, 1, 4), null, w.PlanEntwurf);
        Assert.Single((await w.D.Stundenplan.PlanAsync(w.Direktor, zweiter)).Stunden);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Stundenplan.VeroeffentlichenAsync(w.Direktor, zweiter));
        await w.D.Stundenplan.PlanZeitraumAsync(w.Direktor, zweiter, new(2027, 1, 4), new(2027, 3, 26));
        await w.D.Stundenplan.ZurueckziehenAsync(w.Direktor, w.PlanEntwurf);
        await w.D.Stundenplan.PlanZeitraumAsync(w.Direktor, w.PlanEntwurf, new(2026, 9, 1), new(2027, 1, 3));
        await w.D.Stundenplan.VeroeffentlichenAsync(w.Direktor, w.PlanEntwurf);
        await w.D.Stundenplan.VeroeffentlichenAsync(w.Direktor, zweiter);
        Assert.Equal(2, (await w.D.Stundenplan.PlaeneAsync(w.Direktor, w.Klasse6B)).Count(p => p.Veroeffentlicht));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Stundenplan.PlanZeitraumAsync(w.Direktor, zweiter, new(2026, 8, 1), null));
    }

    [Fact]
    public async Task Kopieren_aus_anderer_Klasse_nimmt_den_Lehrauftrag_der_Zielklasse()
    {
        var w = await OrgWelt.ErstelleAsync();
        var (kopiert, ausgelassen) = await w.D.Stundenplan.KopierenAsync(w.Direktor, w.PlanEntwurf, w.PlanPub);
        Assert.Equal(1, kopiert);
        Assert.Equal(0, ausgelassen);
        var stunde = Assert.Single((await w.D.Stundenplan.PlanAsync(w.Direktor, w.PlanEntwurf)).Stunden);
        Assert.Equal(w.Auftrag6B, stunde.LehrauftragId);
        Assert.Equal("Salle 2", stunde.Raum);
        // Fach ohne Lehrauftrag in der Zielklasse wird ausgelassen.
        var plan5 = await w.D.Stundenplan.PlanAnlegenAsync(w.Direktor, w.Klasse5A, null, null, null);
        Assert.Equal((0, 1), await w.D.Stundenplan.KopierenAsync(w.Direktor, plan5, w.PlanPub));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Stundenplan.KopierenAsync(w.Direktor, w.PlanPub, w.PlanEntwurf));
    }

    [Fact]
    public async Task Sichten_zeigen_nur_veroeffentlichte_Plaene()
    {
        var w = await OrgWelt.ErstelleAsync();
        await w.D.Stundenplan.StundeSpeichernAsync(w.Direktor, w.PlanEntwurf, null, Stunde(w.Auftrag6B, DayOfWeek.Thursday, 8, 0, 9, 0));
        var lehrer = await w.D.Stundenplan.LehrerWocheAsync(w.Lehrer, OrgWelt.Heute);
        Assert.Equal(new DateOnly(2026, 11, 9), lehrer.Montag);
        Assert.Equal("6e A", Assert.Single(lehrer.Stunden).Klasse);
        var lehrerZiel = await w.D.Stundenplan.LehrerWocheAsync(w.LehrerZiel, OrgWelt.Heute);
        Assert.Empty(lehrerZiel.Stunden);
        Assert.NotNull(lehrerZiel.Hinweis);

        var schueler = await w.D.Stundenplan.SchuelerWocheAsync(w.Schueler, w.Schueler, OrgWelt.Heute);
        Assert.Equal(w.PlanPub, schueler.PlanId);
        Assert.Equal("Mathématiques", Assert.Single(schueler.Stunden).Fach);
        var eltern = await w.D.Stundenplan.SchuelerWocheAsync(w.Eltern, w.Schueler, OrgWelt.Heute);
        Assert.Single(eltern.Stunden);
        var ohne = await w.D.Stundenplan.SchuelerWocheAsync(w.Direktor, w.SchuelerOhne, OrgWelt.Heute);
        Assert.Empty(ohne.Stunden);
        Assert.NotNull(ohne.Hinweis);

        await w.D.Stundenplan.ZurueckziehenAsync(w.Direktor, w.PlanPub);
        Assert.Empty((await w.D.Stundenplan.SchuelerWocheAsync(w.Schueler, w.Schueler, OrgWelt.Heute)).Stunden);
        Assert.Empty((await w.D.Stundenplan.LehrerWocheAsync(w.Lehrer, OrgWelt.Heute)).Stunden);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Stundenplan.PlanPdfAsync(w.Schueler, w.PlanPub));
        var pdf = await w.D.Stundenplan.PlanPdfAsync(w.Direktor, w.PlanPub);
        Assert.Contains("6e A", pdf.Titel);
    }

    [Fact]
    public async Task Plan_anlegen_im_Jahr_und_nur_im_Entwurf_loeschen()
    {
        var w = await OrgWelt.ErstelleAsync();
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Stundenplan.PlanAnlegenAsync(w.Direktor, w.Klasse5A, new(2027, 7, 1), null, null));
        var plan = await w.D.Stundenplan.PlanAnlegenAsync(w.Direktor, w.Klasse5A, null, null, null);
        var zeile = Assert.Single(await w.D.Stundenplan.PlaeneAsync(w.Direktor, w.Klasse5A));
        Assert.Equal("Toute l'année", zeile.Label);
        await w.D.Stundenplan.PlanLoeschenAsync(w.Direktor, plan);
        Assert.False(await w.MitDbAsync(db => db.Stundenplaene.AnyAsync(p => p.Id == plan)));
    }
}
