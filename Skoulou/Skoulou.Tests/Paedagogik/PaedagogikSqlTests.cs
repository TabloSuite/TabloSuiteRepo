using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Paedagogik;
using Skoulou.Core.Rechte;
using Skoulou.Tests.Sql;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Paedagogik;

namespace Skoulou.Tests.Paedagogik;

/// <summary>Nur mit SKOULOU_SQL_TESTS=1: Unique (Versuch, Frage), paralleler Examensstart und parallele Abgabe auf SQL Server.</summary>
public class PaedagogikSqlTests(SqlDatenbank datenbank) : IClassFixture<SqlDatenbank>
{
    private sealed class Fabrik(SqlDatenbank d) : IDbContextFactory<SkoulouDbContext>
    {
        public SkoulouDbContext CreateDbContext() => d.Kontext(mitWiederholung: true);
    }

    private async Task<(Guid Zuweisung, Guid Frage, Guid Schueler)> ExamenAsync(TestZeit zeit)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = datenbank.Kontext();
        var schule = new Schule { Name = "Sql " + suffix, NameNormalisiert = "sql " + suffix, Ort = "Lomé", Status = SchulStatus.Active };
        var jahr = new Schuljahr { SchuleId = schule.Id, Bezeichnung = "2026-2027", Beginn = new(2026, 9, 1), Ende = new(2027, 6, 30), Status = SchuljahrStatus.Laufend };
        var stufe = new Stufe { SchuleId = schule.Id, Name = "6e" };
        var fach = new Fach { SchuleId = schule.Id, Name = "Maths" };
        var klasse = new Klasse { SchuleId = schule.Id, SchuljahrId = jahr.Id, StufeId = stufe.Id, Name = "6e A" };
        Konto Konto(string rolle, string name) => new()
        {
            UserName = name + suffix, NormalizedUserName = (name + suffix).ToUpperInvariant(), Rolle = rolle, SchuleId = schule.Id,
            Status = KontoStatus.Actif, SecurityStamp = Guid.NewGuid().ToString()
        };
        var lehrer = Konto(Rollen.Lehrer, "prof");
        var schueler = Konto(Rollen.Schueler, "eleve");
        db.AddRange(schule, jahr, stufe, fach, klasse, lehrer, schueler);
        db.Einschreibungen.Add(new Einschreibung { SchuleId = schule.Id, SchuelerId = schueler.Id, SchuljahrId = jahr.Id, KlasseId = klasse.Id, Von = jahr.Beginn, Status = EinschreibungsStatus.Active });
        var p = new Pruefung { SchuleId = schule.Id, AutorId = lehrer.Id, FachId = fach.Id, StufeId = stufe.Id, Titel = "Devoir", DauerMinuten = 20, Status = InhaltsStatus.Prete };
        var block = new AufgabenBlock { SchuleId = schule.Id, PruefungId = p.Id, Nr = 1 };
        var frage = new Frage { SchuleId = schule.Id, BlockId = block.Id, Nr = 1, Typ = FragenTyp.Texte, TextHtml = "<p>?</p>", Punkte = 2, AkzeptierteAntworten = ["oui"] };
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        var z = new Zuweisung
        {
            SchuleId = schule.Id, PruefungId = p.Id, KlasseId = klasse.Id, LehrerId = lehrer.Id, AbUtc = jetzt.AddMinutes(-5), FristUtc = jetzt.AddHours(1),
            MaxVersuche = 1, Wertung = VersuchsWertung.Premier, LoesungenSichtbar = LoesungsSichtbarkeit.ApresFrist
        };
        db.AddRange(p, block, frage, z);
        await db.SaveChangesAsync();
        return (z.Id, frage.Id, schueler.Id);
    }

    [SqlFact]
    public async Task Eine_Antwort_je_Versuch_und_Frage()
    {
        var zeit = new TestZeit();
        var (z, frage, schueler) = await ExamenAsync(zeit);
        await using var db = datenbank.Kontext();
        var v = new Versuch { SchuleId = (await db.Zuweisungen.FirstAsync(x => x.Id == z)).SchuleId, ZuweisungId = z, SchuelerId = schueler, Nr = 1, StartUtc = DateTime.UtcNow };
        db.Versuche.Add(v);
        db.Antworten.Add(new Antwort { VersuchId = v.Id, FrageId = frage });
        await db.SaveChangesAsync();
        db.Antworten.Add(new Antwort { VersuchId = v.Id, FrageId = frage });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.True(DbTransaktion.IstEindeutigkeitsFehler(ex));
    }

    [SqlFact]
    public async Task Paralleler_Start_ergibt_einen_Versuch_parallele_Abgabe_eine_Wertung()
    {
        var zeit = new TestZeit();
        var (z, frage, schueler) = await ExamenAsync(zeit);
        var fabrik = new Fabrik(datenbank);
        var zugriff = new Zugriff(fabrik);
        var pruefungen = new PruefungsService(fabrik, zugriff, zeit, NullLogger<PruefungsService>.Instance);

        var starts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => pruefungen.StartenAsync(schueler, z)));
        Assert.Single(starts.Select(s => s.Id).Distinct());
        var versuchId = starts[0].Id;
        await pruefungen.AntwortSpeichernAsync(schueler, versuchId, frage, new AntwortEingabe { Text = "oui" });

        // Schüler gibt genau dann ab, wenn der Hintergrunddienst die abgelaufene Arbeit einsammelt.
        zeit.Vor(TimeSpan.FromMinutes(21));
        var ergebnisse = await Task.WhenAll(
            pruefungen.AbgebenAsync(schueler, versuchId).ContinueWith(t => t.Result.Status),
            pruefungen.AbgelaufeneAbgebenAsync().ContinueWith(_ => VersuchsStatus.Soumis),
            pruefungen.AbgebenAsync(schueler, versuchId).ContinueWith(t => t.Result.Status));
        Assert.All(ergebnisse, s => Assert.Equal(VersuchsStatus.Soumis, s));

        await using var db = datenbank.Kontext();
        var v = await db.Versuche.SingleAsync(x => x.ZuweisungId == z);
        Assert.Equal(VersuchsStatus.Soumis, v.Status);
        Assert.Equal(2m, v.Punkte);
        Assert.Equal(1, await db.Antworten.CountAsync(a => a.VersuchId == v.Id));
        var stand = await db.LernStaende.SingleAsync(s => s.ZuweisungId == z);
        Assert.True(stand.Abgeschlossen);
        Assert.Equal(100m, stand.Prozent);
    }
}
