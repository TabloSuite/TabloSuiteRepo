using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Data.Seed;

/// <summary>
/// Aufgaben beim Start: Migrationen nur mit <c>Database:ApplyMigrationsOnStartup</c>, Start-Seed,
/// und der Kommandozeilenmodus <c>--seed-demo</c> (nur Development; schreibt die Demo-Zugänge in eine
/// lokale Datei außerhalb des Repos und beendet sich danach).
/// </summary>
public static class StartAufgaben
{
    public const string DemoArgument = "--seed-demo";

    /// <summary>true = Kommandozeilenmodus erledigt, kein Webserver starten.</summary>
    public static async Task<bool> AusfuehrenAsync(WebApplication app, string[] args)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dienste = scope.ServiceProvider;
        var logger = dienste.GetRequiredService<ILoggerFactory>().CreateLogger("Start");

        if (dienste.GetRequiredService<IOptions<DatenbankOptionen>>().Value.ApplyMigrationsOnStartup)
        {
            await using var db = await dienste.GetRequiredService<IDbContextFactory<SkoulouDbContext>>().CreateDbContextAsync();
            await db.Database.MigrateAsync();
        }

        try
        {
            await dienste.GetRequiredService<StartSeed>().AusfuehrenAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Start-Seed fehlgeschlagen.");
        }

        if (!args.Contains(DemoArgument)) return false;
        if (!app.Environment.IsDevelopment())
        {
            logger.LogError("--seed-demo ist nur in der Entwicklungsumgebung erlaubt.");
            Environment.ExitCode = 1;
            return true;
        }

        var konten = await dienste.GetRequiredService<DemoSeed>().AusfuehrenAsync();
        if (konten.Count > 0)
        {
            // Eigene Datei je Arbeitskopie möglich (Demo:KontenDatei), sonst Temp/skoulou-neu/demo-konten.json.
            var datei = app.Configuration["Demo:KontenDatei"] is { Length: > 0 } eigene
                ? eigene
                : Path.Combine(Path.GetTempPath(), "skoulou-neu", "demo-konten.json");
            Directory.CreateDirectory(Path.GetDirectoryName(datei)!);
            await File.WriteAllTextAsync(datei, JsonSerializer.Serialize(konten, new JsonSerializerOptions { WriteIndented = true }));
            logger.LogInformation("{Anzahl} Demo-Konten angelegt; Zugänge in der lokalen Datei demo-konten.json (Temp-Ordner).", konten.Count);
        }
        return true;
    }
}
