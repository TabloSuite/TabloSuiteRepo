using System.Text.RegularExpressions;
using ENote.Core.Services;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// 22.11.2026 (Befund B-3): Die Vertragssynchronisierung lief mit <c>Serializable</c> und gab am Monatsanfang Deadlocks 1205.
/// Jetzt Vertragssperre je Laden (<c>VertragsSperre</c>, READ COMMITTED + <c>sp_getapplock</c>); der frühere
/// <c>IsolationsRuecksetzer</c> ist entfernt. Damit das so bleibt: keine strengen Isolationsstufen im Code der App (SQL Server
/// behielte die Stufe an der gepoolten Verbindung, spätere Abfragen liefen dann ebenfalls streng). Gleichzeitigkeit je Laden
/// über Anwendungssperren. Die Gleichzeitigkeit selbst prüft <c>VertragsSperreSqlTests</c> (ENOTE_SIM=1).
/// </summary>
public class KeineStrengeIsolationTests
{
    private static string WebApp()
    {
        var ordner = new DirectoryInfo(AppContext.BaseDirectory);
        while (ordner is not null && !Directory.Exists(Path.Combine(ordner.FullName, "eNOTE", "eNOTE.WebApp"))) ordner = ordner.Parent;
        Assert.NotNull(ordner);
        return Path.Combine(ordner!.FullName, "eNOTE", "eNOTE.WebApp");
    }

    [Fact]
    public void App_Nutzt_Keine_Strenge_Isolationsstufe()
    {
        var streng = new Regex(@"IsolationLevel\.(Serializable|RepeatableRead|Snapshot)|ISOLATION\s+LEVEL\s+(SERIALIZABLE|REPEATABLE|SNAPSHOT)|TransactionScope",
            RegexOptions.IgnoreCase);
        var funde = Directory.EnumerateFiles(WebApp(), "*.cs", SearchOption.AllDirectories)
            .Where(d => !d.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !d.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(datei => File.ReadAllLines(datei).Select((zeile, i) => (datei, zeile, i)))
            .Where(x => streng.IsMatch(x.zeile) && !x.zeile.TrimStart().StartsWith("//") && !x.zeile.TrimStart().StartsWith("///"))
            .Select(x => $"{Path.GetFileName(x.datei)}:{x.i + 1}: {x.zeile.Trim()}")
            .ToList();
        Assert.True(funde.Count == 0, string.Join(Environment.NewLine, funde));
    }

    [Fact]
    public async Task Synchronisierung_Laeuft_Auch_Ohne_Relationale_Datenbank_Und_Bleibt_Idempotent()
    {
        // Testprovider der Unit-Tests ist InMemory: die Sperre entfällt dort, das Ergebnis muss gleich bleiben.
        var factory = TestDbContextFactory.Create();
        var start = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var salon = new Core.DataModels.Salons.Salon { Name = "A", IstFreigegeben = true, IstAktiv = true, ProbeBisUtc = start.AddTicks(-1) };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            db.MonatsPreisStaende.Add(new Core.DataModels.Abo.MonatsPreisStand { MonatsPreis = 4000, GueltigAbUtc = start.AddYears(-1) });
            await db.SaveChangesAsync();
        }
        var dienst = new VertragsService(factory, new FesteUhr(start.AddHours(8)));
        await dienst.SynchronisiereAsync(salon.Id);
        await dienst.SynchronisiereAsync(salon.Id);
        await using var kontrolle = await factory.CreateDbContextAsync();
        var periode = Assert.Single(await kontrolle.NutzungsAbos.ToListAsync());
        Assert.Equal(start, periode.ZeitraumVonUtc);
        Assert.Equal(VertragsRegeln.Preis(4000, Core.DataModels.Abo.AboIntervall.Monatlich), periode.PreisSnapshot);
    }

    private sealed class FesteUhr(DateTime jetzt) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(jetzt, DateTimeKind.Utc));
    }
}
