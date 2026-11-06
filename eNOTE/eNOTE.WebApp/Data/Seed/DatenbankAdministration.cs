using Microsoft.Data.SqlClient;

namespace ENote.WebApp.Data.Seed;

/// <summary>Expliziter Einmalprozess: kein Webserver, keine automatische DDL im Runtime-Start.</summary>
public sealed record DatenbankAdministration(bool Migrieren, bool Seed)
{
    public static DatenbankAdministration? Pruefe(string[] args, IConfiguration configuration, IHostEnvironment environment, string connectionString)
    {
        var modus = Argument(args, "--database-admin=");
        var seedOnly = configuration.GetValue<bool>("Database:SeedOnly");
        var alterMigrationsSchalter = configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup");
        if (modus is null && seedOnly && environment.IsDevelopment())
            modus = alterMigrationsSchalter ? "migrate-and-seed" : "seed";
        if (modus is null)
        {
            if (seedOnly || alterMigrationsSchalter)
                throw new InvalidOperationException("Automatische Startup-Migrationen sind deaktiviert. Expliziten --database-admin-Modus mit --database-target verwenden.");
            return null;
        }
        var plan = modus switch
        {
            "migrate" => new DatenbankAdministration(true, false),
            "seed" => new DatenbankAdministration(false, true),
            "migrate-and-seed" => new DatenbankAdministration(true, true),
            _ => throw new InvalidOperationException("Ungültiger --database-admin-Modus (migrate, seed, migrate-and-seed).")
        };
        var sql = new SqlConnectionStringBuilder(connectionString);
        var ziel = $"{sql.DataSource}/{sql.InitialCatalog}";
        if (string.IsNullOrWhiteSpace(sql.DataSource) || string.IsNullOrWhiteSpace(sql.InitialCatalog)
            || !string.Equals(Argument(args, "--database-target="), ziel, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("--database-target muss exakt dem konfigurierten Server/Datenbank-Ziel entsprechen. Keine Datenbankaktion ausgeführt.");
        return plan;
    }

    private static string? Argument(string[] args, string prefix)
    {
        var treffer = args.Where(a => a.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        if (treffer.Length > 1) throw new InvalidOperationException("Administrationsargument mehrfach angegeben.");
        return treffer.Length == 0 ? null : treffer[0][prefix.Length..];
    }
}
