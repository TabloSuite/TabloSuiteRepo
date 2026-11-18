using System.Data;
using ENote.WebApp.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests.Simulation;

/// <summary>
/// 18.11.2026: SQL Server behält die Isolationsstufe einer gepoolten Verbindung. Ohne <see cref="IsolationsRuecksetzer"/> kam die
/// Verbindung nach einer Serializable-Transaktion mit Stufe 4 zurück – spätere Abfragen (auch Verkäufe) liefen dann Serializable.
/// </summary>
public class IsolationsRuecksetzerSqlTests
{
    private static async Task<short> StufeNachSerializableAsync(string verbindung, bool mitRuecksetzer)
    {
        var b = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(verbindung);
        if (mitRuecksetzer) b.AddInterceptors(new IsolationsRuecksetzer());
        var optionen = b.Options;
        await using (var db = new ApplicationDbContext(optionen))
        {
            await using var t = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            _ = await db.Salons.CountAsync();
            await t.CommitAsync();
        }
        await using var db2 = new ApplicationDbContext(optionen);
        return await db2.Database.SqlQueryRaw<short>("SELECT transaction_isolation_level AS Value FROM sys.dm_exec_sessions WHERE session_id = @@SPID").SingleAsync();
    }

    [EnoteSimFact]
    public async Task Nach_Serializable_Kommt_Die_Verbindung_Mit_Read_Committed_Aus_Dem_Pool()
    {
        var datenbank = new SimDatenbank();
        await datenbank.AnlegenAsync();
        try
        {
            // eigener Pool je Fall (Application Name), damit sich die Fälle nicht gegenseitig beeinflussen
            var ohne = new SqlConnectionStringBuilder(datenbank.Verbindung) { ApplicationName = "iso-ohne", MaxPoolSize = 1 }.ConnectionString;
            var mit = new SqlConnectionStringBuilder(datenbank.Verbindung) { ApplicationName = "iso-mit", MaxPoolSize = 1 }.ConnectionString;
            Assert.Equal((short)4, await StufeNachSerializableAsync(ohne, mitRuecksetzer: false)); // Nachweis des Problems
            Assert.Equal((short)2, await StufeNachSerializableAsync(mit, mitRuecksetzer: true));   // READ COMMITTED
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await datenbank.LoeschenAsync();
        }
    }
}
