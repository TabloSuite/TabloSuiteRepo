using System.Collections.Concurrent;
using ENote.Core.DataModels.Identity;
using ENote.Core.Services.Storage;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using ENote.WebApp.Services.Mails;
using ENote.WebApp.Services.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ENote.Tests.Simulation;

/// <summary>Nur mit ENOTE_SIM=1: Daten-Simulation auf einer echten SQL-Server-Datenbank (localhost).</summary>
public sealed class EnoteSimFactAttribute : FactAttribute
{
    public EnoteSimFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ENOTE_SIM") != "1")
            Skip = "Simulation nur mit ENOTE_SIM=1.";
    }
}

/// <summary>
/// Datenbank der Simulation: zufällig <c>ENOTE_SIM_*</c> (danach gelöscht) oder – mit <c>ENOTE_SIM_BEHALTEN=ENOTEDB_SIM</c> –
/// unter diesem Namen neu angelegt und behalten (für die Klick-Simulation). Nie die Entwicklungs-DB « ENOTEDB ».
/// </summary>
internal sealed class SimDatenbank
{
    public SimDatenbank(string? name = null)
    {
        var behalten = name ?? Environment.GetEnvironmentVariable("ENOTE_SIM_BEHALTEN");
        if (!string.IsNullOrWhiteSpace(behalten))
        {
            if (!behalten.StartsWith("ENOTEDB_SIM", StringComparison.Ordinal) && !behalten.StartsWith("ENOTE_SIM_", StringComparison.Ordinal))
                throw new InvalidOperationException("ENOTE_SIM_BEHALTEN muss mit « ENOTEDB_SIM » oder « ENOTE_SIM_ » beginnen.");
            Name = behalten;
            Behalten = true;
        }
        else Name = "ENOTE_SIM_" + Guid.NewGuid().ToString("N")[..12];
    }

    public string Name { get; }
    public bool Behalten { get; }

    public string Verbindung =>
        $"Server=localhost;Database={Name};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Encrypt=True";

    private static string Master => "Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True";

    public async Task AnlegenAsync()
    {
        await LoeschenAsync();
        var optionen = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(Verbindung, s => s.CommandTimeout(300)).Options;
        await using var db = new ApplicationDbContext(optionen);
        await db.Database.MigrateAsync();
    }

    public async Task LoeschenAsync()
    {
        SqlConnection.ClearAllPools();
        await using var verbindung = new SqlConnection(Master);
        await verbindung.OpenAsync();
        await using var befehl = verbindung.CreateCommand();
        befehl.CommandText = $"IF DB_ID('{Name}') IS NOT NULL BEGIN ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]; END";
        await befehl.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Globale Uhr der Simulation. Je Laden läuft eine eigene « Ortszeit » des Tages (AsyncLocal), damit die Läden
/// parallel arbeiten können; ohne lokale Zeit gilt die globale Zeit (Plattform, Abrechnung, nächtliche Dienste).
/// </summary>
internal sealed class SimUhr : TimeProvider
{
    private readonly AsyncLocal<DateTime?> lokal = new();
    public DateTime Global { get; set; } = new(2025, 3, 1, 6, 0, 0, DateTimeKind.Utc);

    public DateTime Jetzt => lokal.Value ?? Global;

    /// <summary>Nicht async aufrufen (sonst geht der Wert beim Rücksprung verloren).</summary>
    public void SetzeLokal(DateTime? utc) => lokal.Value = utc is null ? null : DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc);

    public void Weiter(TimeSpan dauer) => lokal.Value = Jetzt + dauer;

    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Jetzt, DateTimeKind.Utc), TimeSpan.Zero);
}

/// <summary>
/// Ersetzt beim Speichern Zeitstempel, die aus der echten Uhr stammen (Standardwerte der Modelle wie
/// <c>CreatedAtUtc = DateTime.UtcNow</c>, « UpdatedAtUtc »), durch die Simulationszeit. Werte, die ein Dienst aus der
/// Simulationsuhr berechnet hat, bleiben unverändert (sie liegen weit von der echten Uhr entfernt).
/// </summary>
internal sealed class SimZeitInterceptor(SimUhr uhr) : SaveChangesInterceptor
{
    public int Ersetzt;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Ersetze(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Ersetze(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Ersetze(DbContext? db)
    {
        if (db is null) return;
        var echt = DateTime.UtcNow;
        var sim = uhr.Jetzt;
        if ((echt - sim).Duration() < TimeSpan.FromMinutes(30)) return;
        foreach (var eintrag in db.ChangeTracker.Entries())
        {
            if (eintrag.State is not (EntityState.Added or EntityState.Modified)) continue;
            foreach (var p in eintrag.Properties)
            {
                if (p.Metadata.ClrType != typeof(DateTime) && p.Metadata.ClrType != typeof(DateTime?)) continue;
                if (eintrag.State == EntityState.Modified && !p.IsModified) continue;
                if (p.CurrentValue is DateTime wert && (wert - echt).Duration() < TimeSpan.FromMinutes(15))
                {
                    p.CurrentValue = DateTime.SpecifyKind(sim, DateTimeKind.Utc);
                    Interlocked.Increment(ref Ersetzt);
                }
            }
        }
    }
}

/// <summary>Medienspeicher im Arbeitsspeicher (nie Azure, nie die Entwicklungsordner).</summary>
internal sealed class SimMedienSpeicher : IMediaStorageService
{
    public ConcurrentDictionary<string, byte[]> Dateien { get; } = new();

    public async Task<MediaSaveResult> SaveAsync(MediaSaveRequest request, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await request.Inhalt.CopyToAsync(ms, cancellationToken);
        // Namen wie die echten Speicher (<salonId|global>/<guid>.<endung>) – eine behaltene SIM-DB läuft auch in der App.
        var name = MedienDateien.NeuerName(request.SalonId, request.DateiName);
        Dateien[name] = ms.ToArray();
        return MediaSaveResult.Ok(name, MedienDateien.Url(name), ms.Length);
    }

    public Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken = default) => Task.FromResult(Dateien.TryRemove(blobName, out _));
    public string GetPublicUrl(string blobName) => MedienDateien.Url(blobName);
    public Task<byte[]?> LadeBytesAsync(string blobName, CancellationToken cancellationToken = default)
        => Task.FromResult(Dateien.TryGetValue(blobName, out var b) ? b : null);
}

/// <summary>Sammelt Warnungen/Fehler aus den Logs der Dienste (Fehler = Befund).</summary>
internal sealed class SimLogSammler : ILoggerProvider
{
    public ConcurrentQueue<string> Fehler { get; } = new();
    public ConcurrentDictionary<string, int> Warnungen { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
    public void Dispose() { }

    private sealed class Logger(SimLogSammler s, string kategorie) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Warning) return;
            var text = $"{kategorie.Split('.').Last()}: {formatter(state, exception)}";
            if (logLevel >= LogLevel.Error) s.Fehler.Enqueue(text + (exception is null ? "" : $" ({exception.GetType().Name}: {exception.Message})"));
            else s.Warnungen.AddOrUpdate(text.Length > 140 ? text[..140] : text, 1, (_, n) => n + 1);
        }
    }
}

/// <summary>
/// Dienste wie in <c>Program.cs</c> auf der Simulations-DB: echte Anwendungsdienste, steuerbare Uhr, Mails nur in der
/// Warteschlange (kein Versandweg konfiguriert), Medien im Arbeitsspeicher, keine Hintergrunddienste (die Simulation ruft
/// deren Arbeit selbst täglich auf). Einzige Abweichung: Passwort-Hash mit 1.000 Iterationen (Hunderte Konten in Sekunden).
/// </summary>
internal sealed class SimDienste : IAsyncDisposable
{
    public const string AdminEmail = "admin@enote-sim.tg";
    public const string AdminPseudonym = "admin.enote.sim";

    public SimDienste(string verbindung, string adminPasswort)
    {
        Konfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mail:Provider"] = "",
            ["Mail:PostmarkServerToken"] = "",
            ["Mail:SmtpHost"] = "",
            ["Mail:AdminEmpfaenger"] = "admin@enote-sim.tg",
            ["App:PublicBaseUrl"] = "https://enote.simulation",
            ["Seed:SuperAdminEmail"] = AdminEmail,
            ["Seed:SuperAdminPasswort"] = adminPasswort,
            ["Seed:SuperAdminBenutzername"] = AdminPseudonym,
        }).Build();

        var s = new ServiceCollection();
        s.AddLogging(l => l.ClearProviders().SetMinimumLevel(LogLevel.Warning).AddProvider(Logs));
        s.AddSingleton<IConfiguration>(Konfiguration);
        s.AddSingleton(Uhr);
        s.AddSingleton<TimeProvider>(Uhr);
        s.AddSingleton(Interceptor);
        s.AddDataProtection();
        s.AddHttpContextAccessor();
        s.AddDbContextFactory<ApplicationDbContext>((sp, o) => o.UseSqlServer(verbindung, sql =>
        {
            sql.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
            sql.CommandTimeout(120);
        }).AddInterceptors(sp.GetRequiredService<SimZeitInterceptor>(), new IsolationsRuecksetzer()));
        s.AddTransient(sp => sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());
        s.Configure<PasswordHasherOptions>(o => o.IterationCount = 1_000);
        s.AddIdentityCore<AppUser>(o =>
            {
                o.User.RequireUniqueEmail = false;
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredLength = 6;
                o.SignIn.RequireConfirmedAccount = false;
            })
            .AddRoles<Role>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
        s.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();

        s.AddScoped<AuthService>();
        s.AddScoped<KatalogService>();
        s.AddScoped<KassenService>();
        s.AddScoped<MobileMoneyService>();
        s.AddScoped<DashboardService>();
        s.AddScoped<BestandsService>();
        s.AddScoped<StornoService>();
        s.AddScoped<VertragsService>();
        s.AddScoped<KassenZugangService>();
        s.AddScoped<TeamService>();
        s.AddScoped<BesitzerPinHilfe>();
        s.AddScoped<KontaktService>();
        s.AddScoped<PostEingangService>();
        s.AddScoped<AdminService>();
        s.AddScoped<ProduktOwnerService>();
        s.AddScoped<SalonService>();
        s.AddScoped<FinanzService>();
        s.AddScoped<AusgabenService>();
        s.AddScoped<HinweisService>();
        s.AddScoped<AdminKontenService>();
        s.AddScoped<KontoProfilService>();
        s.AddScoped<KuendigungService>();
        s.AddScoped<KundenService>();
        s.AddScoped<KundenBesuchService>();
        s.AddScoped<TicketParkService>();
        s.AddScoped<AufenthaltService>();
        s.AddSingleton<AnwesenheitsSignal>();
        s.AddScoped<AnkuendigungService>();
        s.AddScoped<PreisAnpassungService>();
        s.AddScoped<AboService>();
        s.AddScoped<PoProvisionService>();
        s.AddScoped<BesitzerFinanzService>();
        s.AddScoped<ExceptionLogService>();
        s.AddScoped<IUmsatzBenachrichtigungService, FakeUmsatzBenachrichtigungService>();
        s.AddScoped<EmailService>();
        s.AddScoped<MailBenachrichtigungen>();
        s.AddScoped<NachrichtenService>();
        s.AddSingleton<IMediaStorageService>(Speicher);
        Dienste = s.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = false });
        Db = Dienste.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
    }

    public IConfiguration Konfiguration { get; }
    public ServiceProvider Dienste { get; }
    public IDbContextFactory<ApplicationDbContext> Db { get; }
    public SimUhr Uhr { get; } = new();
    public SimZeitInterceptor Interceptor => interceptor ??= new SimZeitInterceptor(Uhr);
    private SimZeitInterceptor? interceptor;
    public SimMedienSpeicher Speicher { get; } = new();
    public SimLogSammler Logs { get; } = new();

    /// <summary>Dienst in einem eigenen Scope (wie je Circuit/Anfrage).</summary>
    public T Hole<T>() where T : notnull => Dienste.CreateScope().ServiceProvider.GetRequiredService<T>();

    public async Task<T> MitDbAsync<T>(Func<ApplicationDbContext, Task<T>> abfrage)
    {
        await using var db = await Db.CreateDbContextAsync();
        db.Database.SetCommandTimeout(300);
        return await abfrage(db);
    }

    public async ValueTask DisposeAsync() => await Dienste.DisposeAsync();
}

/// <summary>Deterministischer Zufall (fester Startwert je Laden und Tag).</summary>
internal sealed class Zufall(int startwert)
{
    private readonly Random r = new(startwert);

    public static int Kombiniere(params int[] werte)
    {
        unchecked
        {
            var h = 17;
            foreach (var w in werte) h = h * 1_000_003 + w;
            return h & 0x7fffffff;
        }
    }

    public int Zahl(int max) => r.Next(max);
    public int Zahl(int min, int max) => r.Next(min, max);
    public bool Prozent(double p) => r.NextDouble() * 100 < p;
    public double Kommazahl() => r.NextDouble();
    public T Wahl<T>(IReadOnlyList<T> liste) => liste[r.Next(liste.Count)];
}
