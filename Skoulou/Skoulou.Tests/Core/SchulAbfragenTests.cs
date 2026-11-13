using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.Tests.Core;

/// <summary>Gemeinsame Abfragen: laufendes Jahr, aktuelle Klasse, Lehrauftrag-Rechte, Kinder, Zeitraum.</summary>
public class SchulAbfragenTests
{
    private static readonly Guid Schule = Guid.NewGuid();

    [Fact]
    public async Task Laufendes_Schuljahr_hat_Vorrang_sonst_das_neueste()
    {
        var factory = TestDbContextFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        var alt = new Schuljahr { SchuleId = Schule, Bezeichnung = "2025-2026", Beginn = new(2025, 9, 1), Ende = new(2026, 7, 31), Status = SchuljahrStatus.Abgeschlossen };
        var neu = new Schuljahr { SchuleId = Schule, Bezeichnung = "2026-2027", Beginn = new(2026, 9, 1), Ende = new(2027, 7, 31), Status = SchuljahrStatus.Vorbereitung };
        db.Schuljahre.AddRange(alt, neu);
        await db.SaveChangesAsync();

        Assert.Equal(neu.Id, (await SchulAbfragen.LaufendesSchuljahrAsync(db, Schule))!.Id);

        alt.Status = SchuljahrStatus.Laufend;
        await db.SaveChangesAsync();
        Assert.Equal(alt.Id, (await SchulAbfragen.LaufendesSchuljahrAsync(db, Schule))!.Id);
    }

    [Fact]
    public async Task Lehrauftrag_bestimmt_Klassen_Klassenfaecher_und_Schueler()
    {
        var factory = TestDbContextFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        var jahr = new Schuljahr { SchuleId = Schule, Bezeichnung = "2026-2027", Beginn = new(2026, 9, 1), Ende = new(2027, 7, 31), Status = SchuljahrStatus.Laufend };
        var a = new Klasse { SchuleId = Schule, SchuljahrId = jahr.Id, Name = "6e A" };
        var b = new Klasse { SchuleId = Schule, SchuljahrId = jahr.Id, Name = "6e B" };
        var fach = new Fach { SchuleId = Schule, Name = "Mathématiques" };
        var kfA = new Klassenfach { SchuleId = Schule, KlasseId = a.Id, FachId = fach.Id };
        var kfB = new Klassenfach { SchuleId = Schule, KlasseId = b.Id, FachId = fach.Id };
        var lehrer = Guid.NewGuid();
        var schuelerA = Guid.NewGuid();
        var schuelerB = Guid.NewGuid();
        db.AddRange(jahr, a, b, fach, kfA, kfB,
            new Lehrauftrag { SchuleId = Schule, KlassenfachId = kfA.Id, LehrerId = lehrer, Wochenstunden = 4 },
            new Einschreibung { SchuleId = Schule, SchuljahrId = jahr.Id, KlasseId = a.Id, SchuelerId = schuelerA, Von = new(2026, 9, 1) },
            new Einschreibung { SchuleId = Schule, SchuljahrId = jahr.Id, KlasseId = b.Id, SchuelerId = schuelerB, Von = new(2026, 9, 1) });
        await db.SaveChangesAsync();

        Assert.Equal([a.Id], SchulAbfragen.KlassenDesLehrers(db, lehrer, jahr.Id).ToList());
        Assert.True(await SchulAbfragen.UnterrichtetKlasseAsync(db, lehrer, a.Id));
        Assert.False(await SchulAbfragen.UnterrichtetKlasseAsync(db, lehrer, b.Id));
        Assert.True(await SchulAbfragen.UnterrichtetKlassenfachAsync(db, lehrer, kfA.Id));
        Assert.False(await SchulAbfragen.UnterrichtetKlassenfachAsync(db, lehrer, kfB.Id));
        Assert.True(await SchulAbfragen.UnterrichtetSchuelerAsync(db, lehrer, schuelerA));
        Assert.False(await SchulAbfragen.UnterrichtetSchuelerAsync(db, lehrer, schuelerB));
    }

    [Fact]
    public async Task Klassenwechsel_aktuelle_Klasse_und_Zeitraum()
    {
        var factory = TestDbContextFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        var jahr = Guid.NewGuid();
        var schueler = Guid.NewGuid();
        var alt = new Einschreibung { SchuleId = Schule, SchuljahrId = jahr, KlasseId = Guid.NewGuid(), SchuelerId = schueler, Von = new(2026, 9, 1), Bis = new(2026, 11, 30), Status = EinschreibungsStatus.Changee };
        var neu = new Einschreibung { SchuleId = Schule, SchuljahrId = jahr, KlasseId = Guid.NewGuid(), SchuelerId = schueler, Von = new(2026, 12, 1) };
        var storniert = new Einschreibung { SchuleId = Schule, SchuljahrId = jahr, KlasseId = Guid.NewGuid(), SchuelerId = Guid.NewGuid(), Von = new(2026, 9, 1), StorniertAmUtc = DateTime.UtcNow };
        db.AddRange(alt, neu, storniert);
        await db.SaveChangesAsync();

        Assert.Equal(neu.Id, (await SchulAbfragen.AktuelleEinschreibungAsync(db, schueler))!.Id);

        // 1er trimestre (Sept–Dez): beide Einschreibungen desselben Schülers schneiden ihn, die stornierte nicht.
        var t1 = SchulAbfragen.EinschreibungenImZeitraum(db, jahr, new(2026, 9, 1), new(2026, 12, 20)).ToList();
        Assert.Equal(2, t1.Count);
        Assert.Single(t1.Select(e => e.SchuelerId).Distinct());

        // 2e trimestre: nur noch die neue Klasse.
        var t2 = SchulAbfragen.EinschreibungenImZeitraum(db, jahr, new(2027, 1, 4), new(2027, 3, 31)).ToList();
        Assert.Equal(neu.Id, Assert.Single(t2).Id);
    }

    [Fact]
    public async Task Kinder_der_Eltern()
    {
        var factory = TestDbContextFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        var eltern = Guid.NewGuid();
        var kind = Guid.NewGuid();
        db.ElternKinder.Add(new ElternKind { SchuleId = Schule, ElternId = eltern, SchuelerId = kind, Beziehung = "Mère" });
        await db.SaveChangesAsync();

        Assert.Equal([kind], SchulAbfragen.KinderDerEltern(db, eltern).ToList());
        Assert.True(await SchulAbfragen.IstKindAsync(db, eltern, kind));
        Assert.False(await SchulAbfragen.IstKindAsync(db, eltern, Guid.NewGuid()));
    }
}
