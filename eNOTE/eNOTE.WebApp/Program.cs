using System.Security.Claims;
using ENote.Core.DataModels.Identity;
using ENote.WebApp.Components;
using ENote.WebApp.Data;
using ENote.WebApp.Data.Seed;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
var applyMigrationsOnStartup = builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup");

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
builder.Services.AddAuthorizationCore();

var configuredConnectionStringName = builder.Configuration.GetValue<string>("Database:ConnectionStringName") ?? "DefaultConnection";
var connectionString = builder.Configuration.GetConnectionString(configuredConnectionStringName);

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException($"No database connection string is configured for '{configuredConnectionStringName}'. Set ConnectionStrings:{configuredConnectionStringName} or choose another Database:ConnectionStringName.");
}

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

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<KatalogService>();
builder.Services.AddScoped<KassenService>();
builder.Services.AddScoped<TeamService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<ProduktOwnerService>();
builder.Services.AddScoped<SalonService>();
builder.Services.AddScoped<FinanzService>();
builder.Services.AddScoped<AusgabenService>();
builder.Services.AddScoped<AboService>();
builder.Services.AddScoped<ExceptionLogService>();
builder.Services.AddScoped<UmsatzBenachrichtigungService>();
builder.Services.AddScoped<IUmsatzBenachrichtigungService>(sp => sp.GetRequiredService<UmsatzBenachrichtigungService>());
builder.Services.AddScoped<KassenAnzeigeState>();
builder.Services.AddScoped<GeraetFavoritService>();
builder.Services.AddScoped<ToastService>();

// Medienspeicher: Azure Blob (Container „enote-blobuploads“) außerhalb Development,
// lokal unter wwwroot/media-library in Development.
builder.Services.Configure<ENote.Core.Options.AzureBlobStorageOptions>(
    builder.Configuration.GetSection(ENote.Core.Options.AzureBlobStorageOptions.SectionName));
builder.Services.AddScoped<ENote.Core.Services.Storage.IMediaStorageService>(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ENote.Core.Options.AzureBlobStorageOptions>>().Value;
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    // Development: immer lokal unter wwwroot/media-library.
    // Außerhalb Development: Azure Blob (Container „enote-blobuploads“), wenn aktiviert.
    var useBlob = !env.IsDevelopment() && opts.Enabled && !string.IsNullOrWhiteSpace(opts.BlobConnectionString);
    return useBlob
        ? ActivatorUtilities.CreateInstance<ENote.WebApp.Services.Storage.AzureBlobMediaStorageService>(sp)
        : ActivatorUtilities.CreateInstance<ENote.WebApp.Services.Storage.LocalMediaStorageService>(sp);
});

var app = builder.Build();

if (applyMigrationsOnStartup)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    await ApplicationDbInitializer.SeedAsync(dbContext, roleManager, userManager, app.Configuration, applyMigrations: true);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<UmsatzHub>("/hubs/umsatz");

// Geschützte Auslieferung lokaler Mediendateien (Development).
app.MapGet("/media/{blobName}", async (
    string blobName,
    ENote.Core.Services.Storage.IMediaStorageService mediaStorage,
    CancellationToken cancellationToken) =>
{
    var bytes = await mediaStorage.LadeBytesAsync(blobName, cancellationToken);
    if (bytes is null)
    {
        return Results.NotFound();
    }

    var contentType = Path.GetExtension(blobName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };

    return Results.File(bytes, contentType);
}).RequireAuthorization();

app.MapPost("/auth/login/submit", async (
    HttpContext httpContext,
    IAntiforgery antiforgery,
    AuthService authService) =>
{
    await antiforgery.ValidateRequestAsync(httpContext);
    var form = await httpContext.Request.ReadFormAsync();
    var email = form["email"].ToString();
    var passwort = form["password"].ToString();
    var rememberMe = string.Equals(form["rememberMe"], "true", StringComparison.OrdinalIgnoreCase);
    var returnUrl = form["returnUrl"].ToString();

    var (succeeded, _) = await authService.LoginAsync(email, passwort, rememberMe, httpContext.RequestAborted);
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
    CancellationToken cancellationToken) =>
{
    await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
    var verkauf = await db.Verkaeufe
        .Include(v => v.Positionen)
        .Include(v => v.KassenSitzung).ThenInclude(k => k.Kassierer)
        .FirstOrDefaultAsync(v => v.Id == verkaufId, cancellationToken);
    if (verkauf is null)
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
    return InlinePdfResponse.Create(httpContext.Response, pdf, $"recu-{verkauf.Id.ToString()[..8]}.pdf");
}).RequireAuthorization();

// Finanzbericht als PDF (Zeitraum gefiltert). Nur Besitzer/delegierte Finanzberechtigte.
app.MapGet("/finances/rapport", async (
    Guid salonId,
    DateTime von,
    DateTime bis,
    HttpContext httpContext,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    FinanzService finanzService,
    CancellationToken cancellationToken) =>
{
    await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
    var salon = await db.Salons.FirstOrDefaultAsync(s => s.Id == salonId, cancellationToken);
    if (salon is null)
    {
        return Results.NotFound();
    }

    var bisEnde = bis.Date.AddDays(1).AddTicks(-1);
    var tage = await finanzService.GetTagesUmsaetzeImZeitraumAsync(salonId, von.Date, bisEnde, cancellationToken);
    var pdf = FinanzBerichtPdfService.ErzeugeBericht(salon, von, bis, tage);
    return InlinePdfResponse.Create(httpContext.Response, pdf, $"rapport-{salon.Name}-{von:yyyyMMdd}-{bis:yyyyMMdd}.pdf");
}).RequireAuthorization();

app.Run();
