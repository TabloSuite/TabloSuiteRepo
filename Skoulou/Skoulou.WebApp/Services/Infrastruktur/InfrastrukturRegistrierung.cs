using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur.Mails;
using Skoulou.WebApp.Services.Infrastruktur.Medien;
using Skoulou.WebApp.Services.Infrastruktur.Vorlesen;

namespace Skoulou.WebApp.Services.Infrastruktur;

/// <summary>Querschnitt: Konfiguration, Datenbank, Protokoll, Mail, Speicher, Sicherheit, Pipeline.</summary>
public static class InfrastrukturRegistrierung
{
    /// <summary>Serilog (Konsole + gepuffertes Fehlerjournal); Stufen aus « Logging:LogLevel ».</summary>
    public static WebApplicationBuilder AddSkoulouProtokollierung(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<FehlerProtokollPuffer>();
        builder.Host.UseSerilog((kontext, dienste, cfg) =>
        {
            var stufen = kontext.Configuration.GetSection("Logging:LogLevel");
            cfg.MinimumLevel.Is(Stufe(stufen["Default"]) ?? LogEventLevel.Information);
            foreach (var eintrag in stufen.GetChildren().Where(e => e.Key != "Default"))
                if (Stufe(eintrag.Value) is { } s) cfg.MinimumLevel.Override(eintrag.Key, s);
            cfg.Enrich.FromLogContext()
               .WriteTo.Console()
               .WriteTo.Sink(new FehlerProtokollSink(dienste.GetRequiredService<FehlerProtokollPuffer>(),
                   dienste.GetRequiredService<IHttpContextAccessor>()));
        });
        return builder;
    }

    private static LogEventLevel? Stufe(string? wert) => wert switch
    {
        "Trace" => LogEventLevel.Verbose,
        "Debug" => LogEventLevel.Debug,
        "Information" => LogEventLevel.Information,
        "Warning" => LogEventLevel.Warning,
        "Error" => LogEventLevel.Error,
        "Critical" or "None" => LogEventLevel.Fatal,
        _ => null
    };

    public static IServiceCollection AddSkoulouInfrastruktur(this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        services.AddOptions<AppOptionen>().Bind(config.GetSection(AppOptionen.Abschnitt))
            .Validate(o => !env.IsProduction() || Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
                "App:BaseUrl doit être défini en production (liens des e-mails et QR des bulletins).")
            .ValidateOnStart();
        services.AddOptions<DatenbankOptionen>().Bind(config.GetSection(DatenbankOptionen.Abschnitt))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<SeedOptionen>().Bind(config.GetSection(SeedOptionen.Abschnitt));
        services.AddOptions<PostmarkOptionen>().Bind(config.GetSection(PostmarkOptionen.Abschnitt))
            .Validate(o => !o.Enabled || MailAusgangService.IstAdresse(o.FromEmail), "Postmark:FromEmail est invalide.")
            .ValidateOnStart();
        // « Storage » wird zuletzt gebunden und gewinnt gegenüber dem alten Abschnitt « AzureBlobStorage ».
        services.AddOptions<SpeicherOptionen>()
            .Bind(config.GetSection(SpeicherOptionen.AlterAbschnitt))
            .Bind(config.GetSection(SpeicherOptionen.Abschnitt))
            .ValidateOnStart();
        // Ohne Key/Region kein Fehler beim Start: dann liest der Browser vor (wie bisher).
        services.AddOptions<SprachausgabeOptionen>().Bind(config.GetSection(SprachausgabeOptionen.Abschnitt));

        AddDatenbank(services, config);

        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<Zugriff>();
        services.AddSingleton<AuditService>();
        services.AddSingleton<NummernkreisService>();
        services.AddSingleton<HtmlBereiniger>();
        services.AddScoped<ToastService>();
        services.AddScoped<LadeZustand>();
        services.AddHostedService<FehlerProtokollSchreiber>();
        services.AddHostedService<AufraeumDienst>();

        // Mail: Outbox + Postmark
        services.AddHttpClient(PostmarkVersender.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddSingleton<IMailVersender, PostmarkVersender>();
        services.AddSingleton<MailSignal>();
        services.AddSingleton<MailAusgangService>();
        services.AddHostedService<MailVersandDienst>();

        // Speicher: in Development immer lokal (nie Azure), sonst Blob.
        services.AddSingleton<IMedienSpeicher>(sp =>
        {
            var o = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SpeicherOptionen>>().Value;
            if (env.IsDevelopment())
            {
                var wurzel = string.IsNullOrWhiteSpace(o.LocalDataRootPath)
                    ? Path.Combine(env.ContentRootPath, "App_Data", "medien") : o.LocalDataRootPath;
                return new LokalerMedienSpeicher(wurzel, env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"));
            }
            return new BlobMedienSpeicher(o, config);
        });

        services.AddSkoulouRateLimits();
        // Vorlesen der Audio-Aufgaben (Azure-Sprachausgabe, MP3 im Speicher zwischengelagert)
        services.AddHttpClient(VorleseDienst.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(10);
            c.MaxResponseContentBufferSize = 8 * 1024 * 1024;
        });
        services.AddSingleton<VorleseDienst>();

        services.AddRazorComponents()
            .AddInteractiveServerComponents(VerbindungsOptionen)
            .AddHubOptions(o => o.MaximumReceiveMessageSize = 4 * 1024 * 1024);
        return services;
    }

    /// <summary>
    /// Einstellungen der Blazor-Verbindung. Beim Öffnen einer Seite entstehen mehr als zehn kleine Zeichenschritte
    /// (Rahmen, Navigation, Banner, Seite, Daten). Mit dem Standard (höchstens 10 unbestätigte Schritte) wartete der Server
    /// dann auf die Bestätigung des Browsers: bei langsamer Verbindung blieb die Seite einen ganzen Umlauf lang
    /// (0,8 s bei 400 ms Latenz) ohne ihre Daten – Felder verschwanden kurz, dabei Getipptes ging verloren.
    /// </summary>
    public static void VerbindungsOptionen(Microsoft.AspNetCore.Components.Server.CircuitOptions o)
        => o.MaxBufferedUnacknowledgedRenderBatches = 60;

    private static void AddDatenbank(IServiceCollection services, IConfiguration config)
    {
        var name = config["Database:ConnectionStringName"] is { Length: > 0 } n ? n : "DefaultConnection";
        var verbindung = config.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(verbindung))
            throw new InvalidOperationException($"Aucune chaîne de connexion « ConnectionStrings:{name} » n'est configurée.");

        services.AddDbContextFactory<SkoulouDbContext>(o => o.UseSqlServer(verbindung, sql =>
        {
            sql.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null);
            sql.CommandTimeout(60);
        }));
        // Für die Identity-Stores (je Anfrage ein eigener Kontext); Services nutzen die Factory.
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<SkoulouDbContext>>().CreateDbContext());
    }

    /// <summary>Middleware in fester Reihenfolge.</summary>
    public static WebApplication UseSkoulouPipeline(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }
        app.UseSicherheitsHeader();
        app.UseNichtGefundenSeite();
        app.UseHttpsRedirection();
        var kultur = SkoulouKultur.Kultur;
        app.UseRequestLocalization(o =>
        {
            o.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(kultur);
            o.SupportedCultures = [kultur];
            o.SupportedUICultures = [kultur];
            o.RequestCultureProviders.Clear();
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseAntiforgery();
        return app;
    }

    /// <summary>Adresse der Seite « Page introuvable ».</summary>
    public const string NichtGefundenPfad = "/introuvable";

    /// <summary>
    /// Unbekannte Adresse (alter Link, Tippfehler): die Seite « Page introuvable » statt einer leeren Antwort 404 –
    /// nur für Seitenaufrufe im Browser (GET, HTML), nicht für Dateien, Medien, Formular- und API-Endpunkte.
    /// </summary>
    public static IApplicationBuilder UseNichtGefundenSeite(this IApplicationBuilder app)
    {
        app.UseStatusCodePagesWithReExecute(NichtGefundenPfad);
        return app.Use((http, weiter) =>
        {
            var seitenaufruf = HttpMethods.IsGet(http.Request.Method)
                               && http.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
            if (!seitenaufruf && http.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>() is { } seiten)
                seiten.Enabled = false;
            return weiter(http);
        });
    }

    /// <summary>Statische Dateien, Lernvideos (Range), geschützte Medien und vorgelesene Audio-Aufgaben.</summary>
    public static WebApplication MapInfrastrukturEndpunkte(this WebApplication app)
    {
        app.MapStaticAssets();
        app.MapVideoEndpunkt(Path.Combine(app.Environment.ContentRootPath, "Medien", "videos"));
        app.MapMedienEndpunkte();
        app.MapVorleseEndpunkt();
        return app;
    }
}
