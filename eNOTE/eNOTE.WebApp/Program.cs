using System.Security.Claims;
using ENote.Core.DataModels.Identity;
using ENote.WebApp.Components;
using ENote.WebApp.Data;
using ENote.WebApp.Data.Seed;
using ENote.WebApp.Services;
using ENote.WebApp.Services.Storage;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Einheitliche Anzeige auf jedem Server (Azure läuft auf Englisch): « 15.000 F », « −7,5 % », « 11/11/2026 ».
var enoteKultur = ENote.Core.Services.EnoteKultur.Erstelle();
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = enoteKultur;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = enoteKultur;
System.Globalization.CultureInfo.CurrentCulture = enoteKultur;
System.Globalization.CultureInfo.CurrentUICulture = enoteKultur;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((_, services, loggerConfiguration) => loggerConfiguration
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.Sink(new DatabaseExceptionLogSink(
        services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(),
        services.GetRequiredService<IHttpContextAccessor>())));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSignalR();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();

var configuredConnectionStringName = builder.Configuration.GetValue<string>("Database:ConnectionStringName") ?? "DefaultConnection";
var connectionString = builder.Configuration.GetConnectionString(configuredConnectionStringName);

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException($"No database connection string is configured for '{configuredConnectionStringName}'. Set ConnectionStrings:{configuredConnectionStringName} or choose another Database:ConnectionStringName.");
}

var administration = DatenbankAdministration.Pruefe(args, builder.Configuration, builder.Environment, connectionString);

builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 6,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null);
        sqlOptions.CommandTimeout(60);
    }));

builder.Services.AddTransient<ApplicationDbContext>(provider =>
    provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());
builder.Services.AddHttpContextAccessor();

builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        // Gleiche E-Mail für mehrere Konten erlaubt (z. B. Besitzer mit zweitem Laden = zweites Konto).
        // Anmeldung und « Mot de passe oublié » laufen über das Pseudonym, nie über die E-Mail.
        options.User.RequireUniqueEmail = false;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 6;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddRoles<Role>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/auth/login";
    options.LogoutPath = "/auth/logout";
    options.AccessDeniedPath = "/auth/login";

    options.Events.OnValidatePrincipal = async context =>
    {
        var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return;
        }

        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByIdAsync(userIdValue);
        var sessionValue = context.Principal?.FindFirstValue("enote_session");

        // Konto gesperrt/gelöscht oder Sitzung durch eine neuere Anmeldung ersetzt.
        if (user is null
            || user.IsBlocked
            || user.ActiveSessionId is null
            || sessionValue != user.ActiveSessionId.ToString())
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    };
});

if (administration is null)
{
    builder.Services.AddScoped<AuthService>();
    builder.Services.AddScoped<KatalogService>();
    builder.Services.AddScoped<KassenService>();
    builder.Services.AddScoped<MobileMoneyService>();
    builder.Services.AddScoped<DashboardService>();
    builder.Services.AddScoped<BestandsService>();
    builder.Services.AddScoped<StornoService>();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddScoped<VertragsService>();
    builder.Services.AddScoped<KassenZugangService>();
    builder.Services.AddHostedService<VertragsSynchronisierungsDienst>();
    builder.Services.AddScoped<TeamService>();
    builder.Services.AddScoped<BesitzerPinHilfe>();
    builder.Services.AddScoped<KontaktService>();
    builder.Services.AddScoped<AdminService>();
    builder.Services.AddScoped<ProduktOwnerService>();
    builder.Services.AddScoped<SalonService>();
    builder.Services.AddScoped<FinanzService>();
    builder.Services.AddScoped<AusgabenService>();
    builder.Services.AddScoped<HinweisService>();
    builder.Services.AddScoped<AdminKontenService>();
    builder.Services.AddScoped<KontoProfilService>();
    builder.Services.AddScoped<KuendigungService>();
    builder.Services.AddScoped<KundenService>();
    builder.Services.AddScoped<KundenBesuchService>();
    builder.Services.AddScoped<TicketParkService>();
    builder.Services.AddSingleton<AnwesenheitsSignal>();
    builder.Services.AddScoped<AnkuendigungService>();
    builder.Services.AddScoped<PreisAnpassungService>();
    builder.Services.AddScoped<AboService>();
    builder.Services.AddScoped<ExceptionLogService>();
    builder.Services.AddScoped<UmsatzBenachrichtigungService>();
    builder.Services.AddScoped<IUmsatzBenachrichtigungService>(sp => sp.GetRequiredService<UmsatzBenachrichtigungService>());
    builder.Services.AddScoped<IDisplayBenachrichtigungService, DisplayBenachrichtigungService>();
    builder.Services.AddScoped<KassenAnzeigeState>();
    builder.Services.AddScoped<LadeZustand>();
    builder.Services.AddScoped<AktiverSalonState>();
    builder.Services.AddScoped<BesitzerPinState>();
    builder.Services.AddScoped<GeraetFavoritService>();
    builder.Services.AddScoped<ToastService>();
    builder.Services.AddScoped<MedienZugriff>();
    builder.Services.AddHttpClient("postmark", c => c.Timeout = TimeSpan.FromSeconds(20));
    builder.Services.AddScoped<ENote.WebApp.Services.Mails.EmailService>();
    builder.Services.AddScoped<ENote.WebApp.Services.Mails.MailBenachrichtigungen>();
    builder.Services.AddScoped<ENote.WebApp.Services.Mails.NachrichtenService>();
    builder.Services.AddHostedService<ENote.WebApp.Services.Mails.MailVersandDienst>();
    builder.Services.AddEnoteInfrastruktur(builder.Configuration, builder.Environment, connectionString);
}
else
    builder.Services.AddDataProtection(); // Einmalprozess benötigt weder Storage noch persistente Runtime-Schlüssel.

await using var app = builder.Build();

if (administration is not null)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    if (administration.Migrieren)
        await dbContext.Database.MigrateAsync();
    if (administration.Seed)
    {
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        await ApplicationDbInitializer.SeedAsync(dbContext, roleManager, userManager, app.Configuration);
        if (administration.DemoDaten)
            await DemoDatenSeeder.SeedAsync(dbContext, userManager);
    }
    Console.WriteLine("Datenbankadministration abgeschlossen; kein Webserver gestartet.");
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
// Jede Anfrage (auch die Blazor-Verbindung) in derselben Kultur, unabhängig von Browser und Server.
app.UseRequestLocalization(optionen =>
{
    optionen.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(enoteKultur);
    optionen.SupportedCultures = [enoteKultur];
    optionen.SupportedUICultures = [enoteKultur];
    optionen.RequestCultureProviders.Clear();
});
app.UseStaticFiles();
app.UseAntiforgery();

app.MapStaticAssets();

// Videos: eigener Endpunkt statt wwwroot, damit Teilanfragen (Range) korrekt beantwortet werden –
// der Browser lädt Videos stückweise; MapStaticAssets lieferte dabei die ganze Datei und das Video brach ab.
var videoOrdner = Path.Combine(app.Environment.ContentRootPath, "Medien", "videos");
app.MapGet("/medien/videos/{datei}", (string datei, HttpContext http) =>
{
    if (!System.Text.RegularExpressions.Regex.IsMatch(datei, @"^[a-z0-9-]+\.mp4$"))
        return Results.NotFound();
    // Werbevideo öffentlich, Erklärvideos nur für die passenden Rollen (VideoKatalog).
    if (!VideoKatalog.DarfAbrufen(datei, http.User))
        return http.User.Identity?.IsAuthenticated == true ? Results.StatusCode(StatusCodes.Status403Forbidden) : Results.Unauthorized();
    var info = new FileInfo(Path.Combine(videoOrdner, datei));
    if (!info.Exists)
        return Results.NotFound();
    var etag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}\"");
    http.Response.Headers.CacheControl = string.Equals(datei, VideoKatalog.Werbung, StringComparison.OrdinalIgnoreCase)
        ? "public, max-age=3600" : "private, max-age=3600";
    return Results.File(info.FullName, "video/mp4", lastModified: info.LastWriteTimeUtc, entityTag: etag, enableRangeProcessing: true);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<UmsatzHub>("/hubs/umsatz");

// Geschützte Auslieferung von Mediendateien.
app.MapEnoteMedien();

app.MapPost("/auth/login/submit", async (
    HttpContext httpContext,
    IAntiforgery antiforgery,
    AuthService authService) =>
{
    await antiforgery.ValidateRequestAsync(httpContext);
    var form = await httpContext.Request.ReadFormAsync();
    var pseudonym = form["pseudonym"].ToString();
    var passwort = form["password"].ToString();
    var rememberMe = string.Equals(form["rememberMe"], "true", StringComparison.OrdinalIgnoreCase);
    var returnUrl = form["returnUrl"].ToString();

    var (succeeded, _) = await authService.LoginAsync(pseudonym, passwort, rememberMe, httpContext.RequestAborted);
    if (!succeeded)
    {
        return Results.LocalRedirect("/auth/login?error=invalid-credentials");
    }

    var ziel = !string.IsNullOrWhiteSpace(returnUrl)
               && returnUrl.StartsWith("/", StringComparison.Ordinal)
               && !returnUrl.StartsWith("//", StringComparison.Ordinal)
        ? returnUrl
        : "/";
    return Results.LocalRedirect(ziel);
});

app.MapPost("/auth/logout", async (AuthService authService) =>
{
    await authService.LogoutAsync();
    // Nach der Abmeldung zurück auf die öffentliche Startseite
    return Results.LocalRedirect("/");
});

// Kassenbon als PDF (inline, für Druck/Anzeige nach dem Verkauf).
app.MapGet("/bon/{verkaufId:guid}", async (
    Guid verkaufId,
    HttpContext httpContext,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    SalonService salonService,
    MedienZugriff zugriff,
    CancellationToken cancellationToken) =>
{
    await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
    var verkauf = await db.Verkaeufe
        .Include(v => v.Positionen)
        .Include(v => v.Zahlungen)
        .Include(v => v.KassenSitzung).ThenInclude(k => k.Kassierer)
        .Include(v => v.Kunde)
        .FirstOrDefaultAsync(v => v.Id == verkaufId, cancellationToken);
    if (verkauf is null || !await zugriff.HatSalonZugangAsync(httpContext.User, verkauf.SalonId, false, cancellationToken))
    {
        return Results.NotFound();
    }

    var salon = await db.Salons.FirstAsync(s => s.Id == verkauf.SalonId, cancellationToken);
    var logoBytes = await salonService.GetLogoBytesAsync(salon.Id, cancellationToken);

    // QR-Ziel: eigener Link des Salons (z. B. WhatsApp/Bewertung), sonst die eNOTE-Seite.
    var qrZiel = BonQr.Ziel(salon, app.Configuration);

    var pdf = BonPdfService.ErzeugeBon(verkauf, salon, verkauf.KassenSitzung.Kassierer.FullName, logoBytes, qrZiel);
    httpContext.Response.Headers.CacheControl = "private, no-store";
    return InlinePdfResponse.Create(httpContext.Response, pdf, $"recu-{verkauf.Id.ToString()[..8]}.pdf");
}).RequireAuthorization();

// Artikel-Etiketten als PDF (A4-Bogen oder Einzel-Etiketten 62×29 mm). Lange Codes (Hersteller-QR) als QR-Code.
app.MapGet("/etiketten", async (
    Guid salonId,
    Guid leistungId,
    int? anzahl,
    string? format,
    HttpContext httpContext,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    MedienZugriff zugriff,
    CancellationToken cancellationToken) =>
{
    if (!await zugriff.HatSalonZugangAsync(httpContext.User, salonId, false, cancellationToken))
        return Results.NotFound();

    await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
    var leistung = await db.Leistungen.AsNoTracking()
        .FirstOrDefaultAsync(l => l.Id == leistungId && l.SalonId == salonId, cancellationToken);
    if (leistung is null || string.IsNullOrWhiteSpace(leistung.StrichCode))
        return Results.NotFound();

    var etikett = new EtikettPdfService.Etikett(leistung.StrichCode, leistung.Name,
        leistung.Preis.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " F");
    var stueck = Math.Clamp(anzahl ?? 1, 1, 48);
    var pdf = format == "einzel"
        ? EtikettPdfService.ErzeugeEinzelEtikett(etikett, stueck)
        : EtikettPdfService.ErzeugeA4Bogen(Enumerable.Repeat(etikett, stueck).ToList());
    httpContext.Response.Headers.CacheControl = "private, no-store";
    return InlinePdfResponse.Create(httpContext.Response, pdf, $"etiquettes-{leistung.Id.ToString()[..8]}.pdf");
}).RequireAuthorization();

// Kassenbon als HTML-Druckansicht: öffnet sofort den Druck-Dialog (window.print()).
app.MapGet("/bon/{verkaufId:guid}/druck", async (
    Guid verkaufId,
    HttpContext httpContext,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    MedienZugriff zugriff,
    CancellationToken cancellationToken) =>
{
    await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
    var verkauf = await db.Verkaeufe
        .Include(v => v.Positionen)
        .Include(v => v.Zahlungen)
        .Include(v => v.KassenSitzung).ThenInclude(k => k.Kassierer)
        .Include(v => v.Kunde)
        .FirstOrDefaultAsync(v => v.Id == verkaufId, cancellationToken);
    if (verkauf is null || !await zugriff.HatSalonZugangAsync(httpContext.User, verkauf.SalonId, false, cancellationToken))
    {
        return Results.NotFound();
    }

    var salon = await db.Salons.FirstAsync(s => s.Id == verkauf.SalonId, cancellationToken);
    var qrZiel = BonQr.Ziel(salon, app.Configuration);
    var html = BonDruckHtml.Erzeuge(verkauf, salon, verkauf.KassenSitzung.Kassierer.FullName, qrZiel);
    httpContext.Response.Headers.CacheControl = "private, no-store";
    return Results.Content(html, "text/html; charset=utf-8");
}).RequireAuthorization();

// Finanzbericht als PDF (Zeitraum gefiltert, inkl. Ausgaben und Solde de caisse).
app.MapGet("/finances/rapport", async (
    Guid salonId,
    DateTime von,
    DateTime bis,
    HttpContext httpContext,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    FinanzService finanzService,
    MedienZugriff zugriff,
    CancellationToken cancellationToken) =>
{
    if (!await zugriff.HatSalonZugangAsync(httpContext.User, salonId, finanzen: true, cancellationToken))
    {
        return Results.Forbid();
    }

    if (!FinanzZeitraum.TryErstellen(von, bis, out var zeitraum, out var fehler))
    {
        return Results.BadRequest(fehler);
    }

    await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
    var salon = await db.Salons.FirstOrDefaultAsync(s => s.Id == salonId, cancellationToken);
    if (salon is null)
    {
        return Results.NotFound();
    }

    // Gewählte Tage sind Kalendertage des Ladens (Ortszeit aus dem Land).
    var zone = ENote.Core.Services.Zeitzonen.Fuer(salon.Land);
    var (vonUtc, bisEnde) = ENote.Core.Services.Zeitzonen.TageInUtc(zeitraum!.VonUtc, zeitraum.Bis, zone);
    var versatz = ENote.Core.Services.Zeitzonen.Versatz(zone, DateTime.UtcNow);
    var tage = await finanzService.GetTagesUmsaetzeImZeitraumAsync(salonId, vonUtc, bisEnde, cancellationToken, versatz);
    var uebersicht = await finanzService.GetFinanzUebersichtAsync(salonId, vonUtc, bisEnde, cancellationToken);
    var pdf = FinanzBerichtPdfService.ErzeugeBericht(salon, zeitraum.VonUtc, zeitraum.Bis, tage, uebersicht);
    httpContext.Response.Headers.CacheControl = "private, no-store";
    return InlinePdfResponse.Create(httpContext.Response, pdf, $"rapport-{salon.Name}-{zeitraum.VonUtc:yyyyMMdd}-{zeitraum.Bis:yyyyMMdd}.pdf");
}).RequireAuthorization();

await app.RunAsync();
