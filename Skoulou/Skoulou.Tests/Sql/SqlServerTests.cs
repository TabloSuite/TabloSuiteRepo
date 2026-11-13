using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.Tests.Sql;

/// <summary>Nur mit SKOULOU_SQL_TESTS=1: echte SQL-Server-Tests (lokale Instanz, Windows-Auth).</summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SKOULOU_SQL_TESTS") != "1")
            Skip = "SQL-Server-Tests nur mit SKOULOU_SQL_TESTS=1.";
    }
}

/// <summary>Zufällige Datenbank SKOULOU_TEST_*, Migration angewendet, nach den Tests gelöscht.</summary>
public sealed class SqlDatenbank : IAsyncLifetime
{
    public string Name { get; } = "SKOULOU_TEST_" + Guid.NewGuid().ToString("N")[..12];
    public string Verbindung => $"Server=localhost;Database={Name};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True";

    public SkoulouDbContext Kontext(bool mitWiederholung = false)
        => new(new DbContextOptionsBuilder<SkoulouDbContext>()
            .UseSqlServer(Verbindung, o => { if (mitWiederholung) o.EnableRetryOnFailure(6); })
            .Options);

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("SKOULOU_SQL_TESTS") != "1") return;
        await using var db = Kontext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (Environment.GetEnvironmentVariable("SKOULOU_SQL_TESTS") != "1") return;
        SqlConnection.ClearAllPools();
        await using var verbindung = new SqlConnection($"Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True");
        await verbindung.OpenAsync();
        await using var befehl = verbindung.CreateCommand();
        befehl.CommandText = $"IF DB_ID('{Name}') IS NOT NULL BEGIN ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]; END";
        await befehl.ExecuteNonQueryAsync();
    }
}

public class SqlServerTests(SqlDatenbank datenbank) : IClassFixture<SqlDatenbank>
{
    private async Task<(Guid Schule, Guid Jahr, Guid Klasse, Guid Schueler)> GrunddatenAsync(string name)
    {
        await using var db = datenbank.Kontext();
        var schule = new Schule { Name = name, NameNormalisiert = name.ToLowerInvariant(), Ort = "Lomé", Status = SchulStatus.Active };
        db.Schulen.Add(schule);
        var jahr = new Schuljahr { SchuleId = schule.Id, Bezeichnung = "2026-2027", Beginn = new(2026, 9, 1), Ende = new(2027, 6, 30), Status = SchuljahrStatus.Laufend };
        db.Schuljahre.Add(jahr);
        var klasse = new Klasse { SchuleId = schule.Id, SchuljahrId = jahr.Id, Name = "6e A" };
        db.Klassen.Add(klasse);
        var schueler = new Konto { UserName = "eleve." + name, NormalizedUserName = ("ELEVE." + name).ToUpperInvariant(), Rolle = Rollen.Schueler, SchuleId = schule.Id, SecurityStamp = Guid.NewGuid().ToString() };
        db.Users.Add(schueler);
        await db.SaveChangesAsync();
        return (schule.Id, jahr.Id, klasse.Id, schueler.Id);
    }

    [SqlFact]
    public async Task Migration_legt_alle_Tabellen_an()
    {
        await using var db = datenbank.Kontext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.True(await db.Schulen.CountAsync() >= 0);
        Assert.True(await db.Zahlungsmeldungen.CountAsync() >= 0);
    }

    [SqlFact]
    public async Task Eindeutige_Indizes_greifen()
    {
        var (schule, jahr, klasse, schueler) = await GrunddatenAsync("idx-" + Guid.NewGuid().ToString("N")[..6]);

        await using (var db = datenbank.Kontext())
        {
            db.Schuljahre.Add(new Schuljahr { SchuleId = schule, Bezeichnung = "2026-2027", Beginn = new(2027, 9, 1), Ende = new(2028, 6, 30) });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = datenbank.Kontext())
        {
            // Zweites laufendes Jahr verboten, zweites in Vorbereitung erlaubt.
            db.Schuljahre.Add(new Schuljahr { SchuleId = schule, Bezeichnung = "2027-2028", Beginn = new(2027, 9, 1), Ende = new(2028, 6, 30), Status = SchuljahrStatus.Laufend });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = datenbank.Kontext())
        {
            db.Schuljahre.Add(new Schuljahr { SchuleId = schule, Bezeichnung = "2027-2028", Beginn = new(2027, 9, 1), Ende = new(2028, 6, 30), Status = SchuljahrStatus.Vorbereitung });
            await db.SaveChangesAsync();
        }
        await using (var db = datenbank.Kontext())
        {
            db.Einschreibungen.Add(new Einschreibung { SchuleId = schule, SchuelerId = schueler, SchuljahrId = jahr, KlasseId = klasse, Von = new(2026, 9, 1) });
            await db.SaveChangesAsync();
            db.Einschreibungen.Add(new Einschreibung { SchuleId = schule, SchuelerId = schueler, SchuljahrId = jahr, KlasseId = klasse, Von = new(2026, 10, 1) });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = datenbank.Kontext())
        {
            db.Klassen.Add(new Klasse { SchuleId = schule, SchuljahrId = jahr, Name = "6e A" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [SqlFact]
    public async Task Gleiche_Email_fuer_mehrere_Konten_erlaubt_Identifiant_nicht()
    {
        await using var db = datenbank.Kontext();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        db.Users.Add(new Konto { UserName = "ama." + suffix, NormalizedUserName = "AMA." + suffix.ToUpperInvariant(), Email = "f@exemple.tg", NormalizedEmail = "F@EXEMPLE.TG", Rolle = Rollen.Eltern });
        db.Users.Add(new Konto { UserName = "ama2." + suffix, NormalizedUserName = "AMA2." + suffix.ToUpperInvariant(), Email = "f@exemple.tg", NormalizedEmail = "F@EXEMPLE.TG", Rolle = Rollen.Eltern });
        await db.SaveChangesAsync();
        db.Users.Add(new Konto { UserName = "AMA." + suffix, NormalizedUserName = "AMA." + suffix.ToUpperInvariant(), Rolle = Rollen.Eltern });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SqlFact]
    public async Task Nummernkreis_vergibt_parallel_keine_Dubletten()
    {
        var (schule, _, _, _) = await GrunddatenAsync("nr-" + Guid.NewGuid().ToString("N")[..6]);
        var dienst = new NummernkreisService(TimeProvider.System);
        var aufgaben = Enumerable.Range(0, 40).Select(async i =>
        {
            await using var db = datenbank.Kontext(mitWiederholung: true);
            return await dienst.NaechsteAsync(db, i % 2 == 0 ? schule : null, i % 2 == 0 ? NummernArt.Quittung : NummernArt.Plattformrechnung, 2026);
        });
        var nummern = await Task.WhenAll(aufgaben);
        var schulNummern = nummern.Where((_, i) => i % 2 == 0).OrderBy(n => n).ToList();
        var globale = nummern.Where((_, i) => i % 2 == 1).OrderBy(n => n).ToList();
        Assert.Equal(Enumerable.Range(1, 20).Select(n => (long)n), schulNummern);
        Assert.Equal(20, globale.Distinct().Count());

        // In einer Transaktion: Rollback gibt die Nummer nicht frei, aber es entstehen nie Dubletten.
        await using var db2 = datenbank.Kontext();
        await using (var tx = await db2.Database.BeginTransactionAsync())
        {
            Assert.Equal(21, await dienst.NaechsteAsync(db2, schule, NummernArt.Quittung, 2026));
            await tx.RollbackAsync();
        }
        Assert.Equal(21, await dienst.NaechsteAsync(db2, schule, NummernArt.Quittung, 2026));
        Assert.Equal(1, await db2.Nummernkreise.CountAsync(k => k.SchuleId == null && k.Art == NummernArt.Plattformrechnung && k.Jahr == 2026));
    }
}
