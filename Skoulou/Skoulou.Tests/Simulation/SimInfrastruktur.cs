using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skoulou.Core.Entitaeten;
using Skoulou.Tests.Organisation;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Finanzen;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Mails;
using Skoulou.WebApp.Services.Infrastruktur.Medien;
using Skoulou.WebApp.Services.Noten;
using Skoulou.WebApp.Services.Organisation;
using Skoulou.WebApp.Services.Paedagogik;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Simulation;

/// <summary>Nur mit SKOULOU_SIM=1: Simulation auf einer echten SQL-Server-Datenbank (Bauplan §9.4).</summary>
public sealed class SimFactAttribute : FactAttribute
{
    public SimFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SKOULOU_SIM") != "1")
            Skip = "Simulation nur mit SKOULOU_SIM=1.";
    }
}

/// <summary>
/// Datenbank der Simulation: zufällig <c>SKOULOU_SIM_*</c> (danach gelöscht) oder – mit
/// <c>SKOULOU_SIM_BEHALTEN=SkoulouDB_SIM</c> – unter diesem Namen neu angelegt und behalten (für den Browser-Rundgang).
/// </summary>
internal sealed class SimDatenbank
{
    public SimDatenbank()
    {
        var behalten = Environment.GetEnvironmentVariable("SKOULOU_SIM_BEHALTEN");
        if (!string.IsNullOrWhiteSpace(behalten))
        {
            // Schutz: nie die Entwicklungs-DB « SkoulouDB » oder eine fremde Datenbank überschreiben.
            if (!behalten.StartsWith("SkoulouDB_SIM", StringComparison.Ordinal) && !behalten.StartsWith("SKOULOU_SIM_", StringComparison.Ordinal))
                throw new InvalidOperationException("SKOULOU_SIM_BEHALTEN muss mit « SkoulouDB_SIM » oder « SKOULOU_SIM_ » beginnen.");
            Name = behalten;
            Behalten = true;
        }
        else Name = "SKOULOU_SIM_" + Guid.NewGuid().ToString("N")[..12];
    }

    public string Name { get; }
    public bool Behalten { get; }

    public string Verbindung =>
        $"Server=localhost;Database={Name};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Encrypt=True";

    private static string Master => "Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True";

    public async Task AnlegenAsync()
    {
        await LoeschenAsync();
        var optionen = new DbContextOptionsBuilder<SkoulouDbContext>().UseSqlServer(Verbindung).Options;
        await using var db = new SkoulouDbContext(optionen);
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
/// Dienste wie in der App (dieselben Registrierungen <c>AddPlattform … AddFinanzen</c>) auf der Simulations-DB,
/// mit steuerbarer Uhr, Outbox ohne Versand (Fake-Versender) und Medienspeicher im Arbeitsspeicher.
/// Einzige Abweichung: Passwort-Hash mit 1.000 statt 100.000 Iterationen (Tausende Konten in Minuten statt Stunden).
/// </summary>
internal sealed class SimDienste : IAsyncDisposable
{
    public SimDienste(string verbindung, TestZeit zeit, string seedEmail, string seedPasswort)
    {
        Zeit = zeit;
        var s = new ServiceCollection();
        s.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));
        s.AddMemoryCache();
        s.AddDataProtection();
        s.AddHttpContextAccessor();
        s.AddSingleton<TimeProvider>(zeit);
        s.AddOptions<AppOptionen>().Configure(o => o.BaseUrl = "https://skoulou.simulation");
        s.AddOptions<SeedOptionen>().Configure(o =>
        {
            o.SuperAdminEmail = seedEmail;
            o.SuperAdminPasswort = seedPasswort;
            o.SuperAdminIdentifiant = "admin.skoulou";
            o.SuperAdminName = "Administrateur SKOULOU";
        });
        s.AddOptions<PostmarkOptionen>();
        s.AddOptions<SpeicherOptionen>();
        s.Configure<PasswordHasherOptions>(o => o.IterationCount = 1_000);
        s.AddDbContextFactory<SkoulouDbContext>(o => o.UseSqlServer(verbindung, sql =>
        {
            sql.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
            sql.CommandTimeout(120);
        }));
        s.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<SkoulouDbContext>>().CreateDbContext());
        s.AddSingleton<Zugriff>();
        s.AddSingleton<AuditService>();
        s.AddSingleton<NummernkreisService>();
        s.AddSingleton<HtmlBereiniger>();
        s.AddSingleton<IMailVersender>(Versender);
        s.AddSingleton<MailSignal>();
        s.AddSingleton<MailAusgangService>();
        s.AddSingleton<IMedienSpeicher>(Speicher);
        s.AddPlattform().AddOrganisation().AddPaedagogik().AddNoten().AddFinanzen();
        Dienste = s.BuildServiceProvider();
        Db = Dienste.GetRequiredService<IDbContextFactory<SkoulouDbContext>>();
    }

    public ServiceProvider Dienste { get; }
    public IDbContextFactory<SkoulouDbContext> Db { get; }
    public TestZeit Zeit { get; }
    public FakeVersender Versender { get; } = new();
    public SpeicherAttrappe Speicher { get; } = new();

    /// <summary>Dienst in einem eigenen Scope (wie je Circuit/Anfrage).</summary>
    public T Hole<T>() where T : notnull => Dienste.CreateScope().ServiceProvider.GetRequiredService<T>();

    public async Task<T> MitDbAsync<T>(Func<SkoulouDbContext, Task<T>> abfrage)
    {
        await using var db = await Db.CreateDbContextAsync();
        db.Database.SetCommandTimeout(300);
        return await abfrage(db);
    }

    /// <summary>Uhr auf einen Kalendertag (Lomé = UTC) um 9 Uhr.</summary>
    public void Heute(DateOnly tag) => Zeit.Jetzt = new DateTimeOffset(tag.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);

    public DateOnly HeuteDatum => DateOnly.FromDateTime(Zeit.Jetzt.UtcDateTime);

    public async ValueTask DisposeAsync() => await Dienste.DisposeAsync();
}

/// <summary>Bündel der Anwendungsdienste (jeweils aus einem eigenen Scope).</summary>
internal sealed class SimApi(SimDienste d)
{
    public RegistrierungService Registrierung => d.Hole<RegistrierungService>();
    public SchulVerwaltungService Schulen => d.Hole<SchulVerwaltungService>();
    public KontenVerwaltungService AdminKonten => d.Hole<KontenVerwaltungService>();
    public AdministratorenService Administratoren => d.Hole<AdministratorenService>();
    public ElternKinderService ElternKinder => d.Hole<ElternKinderService>();
    public ProtokollService Protokoll => d.Hole<ProtokollService>();
    public TarifService Tarife => d.Hole<TarifService>();
    public SchulService Schule => d.Hole<SchulService>();
    public SchuljahrService Jahre => d.Hole<SchuljahrService>();
    public KlassenService Klassen => d.Hole<KlassenService>();
    public FaecherService Faecher => d.Hole<FaecherService>();
    public UnterrichtService Unterricht => d.Hole<UnterrichtService>();
    public PersonalService Personal => d.Hole<PersonalService>();
    public SchuelerService Schueler => d.Hole<SchuelerService>();
    public EinschreibungService Einschreibung => d.Hole<EinschreibungService>();
    public ElternService Eltern => d.Hole<ElternService>();
    public KontoVerwaltungService Konten => d.Hole<KontoVerwaltungService>();
    public BewertungService Bewertungen => d.Hole<BewertungService>();
    public NotenService Noten => d.Hole<NotenService>();
    public KlassenratService Conseil => d.Hole<KlassenratService>();
    public BulletinService Bulletins => d.Hole<BulletinService>();
    public BulletinLeseService BulletinLesen => d.Hole<BulletinLeseService>();
    public JahreswechselService Wechsel => d.Hole<JahreswechselService>();
    public GebuehrenService Gebuehren => d.Hole<GebuehrenService>();
    public ZahlungService Zahlungen => d.Hole<ZahlungService>();
    public JournalService Journal => d.Hole<JournalService>();
    public PlattformAbrechnung Abrechnung => d.Hole<PlattformAbrechnung>();
    public RechnungService Rechnungen => d.Hole<RechnungService>();
    public AnteilService Anteile => d.Hole<AnteilService>();
    public FamilienFinanzService Familie => d.Hole<FamilienFinanzService>();
    public BerichtService Bericht => d.Hole<BerichtService>();
    public InhaltService Inhalte => d.Hole<InhaltService>();
    public ZuweisungService Zuweisungen => d.Hole<ZuweisungService>();
    public LernService Lernen => d.Hole<LernService>();
    public PruefungsService Pruefungen => d.Hole<PruefungsService>();
    public KorrekturService Korrektur => d.Hole<KorrekturService>();
    public AnwesenheitService Anwesenheit => d.Hole<AnwesenheitService>();
    public CockpitService Cockpit => d.Hole<CockpitService>();
    public UserManager<Konto> Benutzer => d.Hole<UserManager<Konto>>();
}
