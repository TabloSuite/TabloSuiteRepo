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
        options.User.RequireUniqueEmail = true;
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
    builder.Services.AddScoped<AdminService>();
    builder.Services.AddScoped<ProduktOwnerService>();
    builder.Services.AddScoped<SalonService>();
    builder.Services.AddScoped<FinanzService>();
    builder.Services.AddScoped<AusgabenService>();
    builder.Services.AddScoped<HinweisService>();
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
app.UseStaticFiles();
app.UseAntiforgery();

app.MapStaticAssets();

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
    return Results.LocalRedirect("/auth/login?info=logged-out");
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
        .FirstOrDefaultAsync(v => v.Id == verkaufId, cancellationToken);
    if (verkauf is null || !await zugriff.HatSalonZugangAsync(httpContext.User, verkauf.SalonId, false, cancellationToken))
    {
        return Results.NotFound();
    }

    var salon = await db.Salons.FirstAsync(s => s.Id == verkauf.SalonId, cancellationToken);
    var logoBytes = await salonService.GetLogoBytesAsync(salon.Id, cancellationToken);

    // QR-Ziel: eigener Link des Salons (z. B. WhatsApp/Bewertung) oder die digitale Bon-Kopie.
    var qrZiel = !string.IsNullOrWhiteSpace(salon.BonQrLink)
        ? salon.BonQrLink
        : $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/bon/{verkauf.Id}";

    var pdf = BonPdfService.ErzeugeBon(verkauf, salon, verkauf.KassenSitzung.Kassierer.FullName, logoBytes, qrZiel);
    httpContext.Response.Headers.CacheControl = "private, no-store";
    return InlinePdfResponse.Create(httpContext.Response, pdf, $"recu-{verkauf.Id.ToString()[..8]}.pdf");
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
        .FirstOrDefaultAsync(v => v.Id == verkaufId, cancellationToken);
    if (verkauf is null || !await zugriff.HatSalonZugangAsync(httpContext.User, verkauf.SalonId, false, cancellationToken))
    {
        return Results.NotFound();
    }

    var salon = await db.Salons.FirstAsync(s => s.Id == verkauf.SalonId, cancellationToken);
    var qrZiel = !string.IsNullOrWhiteSpace(salon.BonQrLink)
        ? salon.BonQrLink
        : $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/bon/{verkauf.Id}";
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

    var bisEnde = zeitraum!.BisExklusivUtc.AddTicks(-1);
    var tage = await finanzService.GetTagesUmsaetzeImZeitraumAsync(salonId, zeitraum.VonUtc, bisEnde, cancellationToken);
    var uebersicht = await finanzService.GetFinanzUebersichtAsync(salonId, zeitraum.VonUtc, bisEnde, cancellationToken);
    var pdf = FinanzBerichtPdfService.ErzeugeBericht(salon, zeitraum.VonUtc, zeitraum.Bis, tage, uebersicht);
    httpContext.Response.Headers.CacheControl = "private, no-store";
    return InlinePdfResponse.Create(httpContext.Response, pdf, $"rapport-{salon.Name}-{zeitraum.VonUtc:yyyyMMdd}-{zeitraum.Bis:yyyyMMdd}.pdf");
}).RequireAuthorization();

await app.RunAsync();
