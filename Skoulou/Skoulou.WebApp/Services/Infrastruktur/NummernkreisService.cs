using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;

namespace Skoulou.WebApp.Services.Infrastruktur;

/// <summary>
/// Vergibt fortlaufende Nummern atomar. Auf SQL Server per MERGE mit UPDLOCK/HOLDLOCK in der
/// Transaktion des Aufrufers (Nummer und Beleg werden gemeinsam gespeichert oder gar nicht);
/// ohne laufende Transaktion mit Wiederholung bei Deadlocks. InMemory (Tests) zählt einfach hoch.
/// </summary>
public sealed class NummernkreisService(TimeProvider zeit)
{
    private const string MergeSql = """
        MERGE [Nummernkreise] WITH (UPDLOCK, HOLDLOCK) AS ziel
        USING (SELECT @schule AS SchuleId, @art AS Art, @jahr AS Jahr) AS quelle
        ON ((ziel.[SchuleId] = quelle.SchuleId) OR (ziel.[SchuleId] IS NULL AND quelle.SchuleId IS NULL))
           AND ziel.[Art] = quelle.Art AND ziel.[Jahr] = quelle.Jahr
        WHEN MATCHED THEN UPDATE SET [Letzte] = ziel.[Letzte] + 1, [GeaendertAmUtc] = @jetzt
        WHEN NOT MATCHED THEN INSERT ([Id], [SchuleId], [Art], [Jahr], [Letzte], [ErstelltAmUtc], [GeaendertAmUtc])
             VALUES (@id, quelle.SchuleId, quelle.Art, quelle.Jahr, 1, @jetzt, @jetzt)
        OUTPUT inserted.[Letzte];
        """;

    /// <summary>Nächster Zählerstand (1, 2, 3 …) des Kreises.</summary>
    public async Task<long> NaechsteAsync(SkoulouDbContext db, Guid? schuleId, NummernArt art, int jahr, CancellationToken ct = default)
    {
        if (NummernFormat.IstGlobal(art)) schuleId = null;
        if (!db.Database.IsSqlServer()) return await NaechsteEinfachAsync(db, schuleId, art, jahr, ct);

        if (db.Database.CurrentTransaction is not null)
            return await MergeAsync(db, schuleId, art, jahr, ct);

        var strategie = db.Database.CreateExecutionStrategy();
        return await strategie.ExecuteAsync(() => MergeAsync(db, schuleId, art, jahr, ct));
    }

    /// <summary>Nächste formatierte Nummer, z. B. « REC-2026-00042 ».</summary>
    public async Task<string> NaechsteNummerAsync(SkoulouDbContext db, Guid? schuleId, NummernArt art, int jahr,
        string? praefix = null, CancellationToken ct = default)
        => NummernFormat.Formatiere(art, jahr, await NaechsteAsync(db, schuleId, art, jahr, ct), praefix);

    private async Task<long> MergeAsync(SkoulouDbContext db, Guid? schuleId, NummernArt art, int jahr, CancellationToken ct)
    {
        var verbindung = db.Database.GetDbConnection();
        var selbstGeoeffnet = verbindung.State != ConnectionState.Open;
        if (selbstGeoeffnet) await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var befehl = verbindung.CreateCommand();
            befehl.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            befehl.CommandText = MergeSql;
            Parameter(befehl, "@schule", DbType.Guid, schuleId);
            Parameter(befehl, "@art", DbType.Int32, (int)art);
            Parameter(befehl, "@jahr", DbType.Int32, jahr);
            Parameter(befehl, "@id", DbType.Guid, Guid.NewGuid());
            Parameter(befehl, "@jetzt", DbType.DateTime2, zeit.GetUtcNow().UtcDateTime);
            var wert = await befehl.ExecuteScalarAsync(ct);
            return Convert.ToInt64(wert, System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            if (selbstGeoeffnet) await db.Database.CloseConnectionAsync();
        }
    }

    private static void Parameter(DbCommand befehl, string name, DbType typ, object? wert)
    {
        var p = befehl.CreateParameter();
        p.ParameterName = name;
        p.DbType = typ;
        p.Value = wert ?? DBNull.Value;
        befehl.Parameters.Add(p);
    }

    private static async Task<long> NaechsteEinfachAsync(SkoulouDbContext db, Guid? schuleId, NummernArt art, int jahr, CancellationToken ct)
    {
        var kreis = await db.Nummernkreise.FirstOrDefaultAsync(k => k.SchuleId == schuleId && k.Art == art && k.Jahr == jahr, ct);
        if (kreis is null)
        {
            kreis = new Nummernkreis { SchuleId = schuleId, Art = art, Jahr = jahr };
            db.Nummernkreise.Add(kreis);
        }
        kreis.Letzte++;
        await db.SaveChangesAsync(ct);
        return kreis.Letzte;
    }
}
