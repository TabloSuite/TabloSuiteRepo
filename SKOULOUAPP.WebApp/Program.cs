using SkoulouApp.WebApp.Components;
using SkoulouApp.WebApp.Data;
using SkoulouApp.Core.DataModels;
using SkoulouApp.WebApp.Services;
using SkoulouApp.Core.Interfaces;
using SkoulouApp.Core.Interfaces.IRepositories;
using SkoulouApp.Core.Options;
using SkoulouApp.Core.Repositories;
using SkoulouApp.Core.Services;
using SkoulouApp.Core.Services.Email;
using SkoulouApp.Core.Services.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using System.Security.Claims;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Finanzen;
using Microsoft.EntityFrameworkCore.Infrastructure;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var applyMigrationsOnStartup = builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup");
QuestPDF.Settings.License = LicenseType.Community;

builder.Host.UseSerilog((_, services, loggerConfiguration) => loggerConfiguration
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.Sink(new DatabaseExceptionLogSink(
        services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(),
        services.GetRequiredService<IHttpContextAccessor>())));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Bilder werden im Rich-Text-Editor als data:-URL (Base64) übertragen. Ein
// zulässiges 5-MB-Bild ist nach Base64-Kodierung größer als das SignalR-
// Standardlimit; ohne dieses Limit wird der Blazor-Circuit beim Speichern
// bzw. Synchronisieren des Editors getrennt.
builder.Services.Configure<HubOptions>(options =>
    options.MaximumReceiveMessageSize = 8 * 1024 * 1024);

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider, SchulAuthenticationStateProvider>();
builder.Services.AddAuthorizationCore();

// Datenbankverbindung:
// - Database:ConnectionStringName="DefaultConnection" nutzt den lokalen SQL Server
//   für die normale Entwicklung.
// - Database:ConnectionStringName="AzureDefaultConnection" verbindet mit der
//   Azure SQL Database. Der Wert steht in User Secrets bzw. in Azure als
//   App-Einstellung (ConnectionStrings__AzureDefaultConnection), damit keine
//   Zugangsdaten im Repository liegen.
var configuredConnectionStringName = builder.Configuration.GetValue<string>("Database:ConnectionStringName") ?? "DefaultConnection";
var connectionString = builder.Configuration.GetConnectionString(configuredConnectionStringName);

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException($"No database connection string is configured for '{configuredConnectionStringName}'. Set ConnectionStrings:{configuredConnectionStringName} or choose another Database:ConnectionStringName.");
}

builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        // Azure SQL trennt Verbindungen regelmäßig (Failover, Throttling, Wartung).
        // Ohne Wiederholungslogik führen diese transienten Fehler zu Abbrüchen.
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 6,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null);

        // Azure SQL antwortet unter Last langsamer als eine lokale Instanz.
        sqlOptions.CommandTimeout(60);
    }));

builder.Services.AddTransient<ApplicationDbContext>(provider =>
    provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());
builder.Services.AddTransient<ISkoulouAppDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
builder.Services.AddHttpContextAccessor();

builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        // Anmeldung per Identifiant (UserName); eine Kontakt-E-Mail darf zu mehreren Konten gehören.
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
        var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return;
        }

        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByIdAsync(userId);
        var sessionValue = context.Principal?.FindFirstValue("skoulouapp_session");
        await using var zugangsDb = await context.HttpContext.RequestServices.GetRequiredService<IDbContextFactory<ApplicationDbContext>>()
            .CreateDbContextAsync(context.HttpContext.RequestAborted);
        if (user is null
            || user.IsBlocked
            || !await new SchulZugangService(zugangsDb).DarfAnmeldenAsync(user.Id, context.HttpContext.RequestAborted)
            || !Guid.TryParse(sessionValue, out var sessionId)
            || user.ActiveSessionId != sessionId)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireSuperAdmin", policy => policy.RequireRole("SuperAdmin"));
    options.AddPolicy("RequireSchulLeiter", policy => policy.RequireRole("SchulLeiter", "Koordinator"));
    options.AddPolicy("RequireTeacher", policy => policy.RequireRole("Lehrer", "SchulLeiter", "Koordinator"));
    options.AddPolicy("RequireStudent", policy => policy.RequireRole("Schüler"));
});

// Blazor-Server-Komponenten können innerhalb eines Circuits parallel initialisieren.
// Jeder datenbanknutzende Dienst erhält deshalb als Transient über die Factory einen
// eigenen ApplicationDbContext statt eines im Circuit geteilten Contexts.
builder.Services.AddTransient<ISchuelerProfilService, SchuelerProfilService>();
builder.Services.AddTransient<ILektionsFortschrittService, LektionsFortschrittService>();
builder.Services.AddTransient<AuthService>();
builder.Services.AddScoped<DatabaseActionGuard>();

builder.Services.Configure<MediaStorageOptions>(builder.Configuration.GetSection(MediaStorageOptions.SectionName));
builder.Services.Configure<AzureBlobStorageOptions>(builder.Configuration.GetSection(AzureBlobStorageOptions.SectionName));
// App Service lehnt den Präfix « AzureBlobStorage » ab – dort gelten « Storage__… » (z. B. Storage__ServiceUri, Storage__ContainerName).
builder.Services.Configure<AzureBlobStorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.PostConfigure<AzureBlobStorageOptions>(options =>
{
    if (string.IsNullOrWhiteSpace(options.BlobConnectionString))
    {
        options.BlobConnectionString = builder.Configuration.GetConnectionString("BlobConnection")
            ?? builder.Configuration["BlobConnection"]
            ?? string.Empty;
    }
});
if (builder.Environment.IsDevelopment())
{
    builder.Services.PostConfigure<AzureBlobStorageOptions>(options => options.Enabled = false);
}
else
{
    builder.Services.PostConfigure<AzureBlobStorageOptions>(options => options.Enabled = true);
}
builder.Services.Configure<PostmarkOptions>(builder.Configuration.GetSection(PostmarkOptions.SectionName));

builder.Services.AddHttpClient("Postmark", client =>
{
    client.BaseAddress = new Uri("https://api.postmarkapp.com/");
    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
});
builder.Services.AddScoped<PostmarkEmailService>();
builder.Services.AddScoped<IEmailService>(services => new SkoulouEmailService(services.GetRequiredService<PostmarkEmailService>()));
builder.Services.AddScoped<BroadcastEmailService>();
builder.Services.AddTransient<MailboxService>();
builder.Services.AddTransient<KontoBenachrichtigungService>();
builder.Services.AddTransient<AbrechnungBenachrichtigungService>();

builder.Services.AddTransient<ICourseRepository, CourseRepository>();
builder.Services.AddTransient<ISchoolRepository, SchoolRepository>();
builder.Services.AddTransient<IUnitOfWork, UnitOfWork>();
if (builder.Environment.IsDevelopment())
    builder.Services.AddTransient<IMediaStorageService, LocalMediaStorageService>();
else
    builder.Services.AddTransient<IMediaStorageService, ProductionMediaStorageService>();
builder.Services.AddTransient<IBlobMediaLibraryService, AzureBlobMediaLibraryService>();
builder.Services.AddTransient<IFinanzBelegStorage, AzureFinanzBelegStorage>();
builder.Services.AddTransient<HomePageService>();
builder.Services.AddTransient<AnkuendigungService>();
builder.Services.AddTransient<DashboardService>();
builder.Services.AddTransient<IBenutzerArbeitsvorgang, BenutzerArbeitsvorgang>();
builder.Services.AddTransient<LearningService>();
builder.Services.AddTransient<LessonExerciseA4PdfService>();
builder.Services.AddTransient<TeacherLessonService>();
builder.Services.AddTransient<ExamService>();
builder.Services.AddTransient<FormelFavoritService>();
builder.Services.AddTransient<AboService>();
builder.Services.AddTransient<KlassenZuordnungsService>();
builder.Services.AddTransient<AbrechnungService>();
builder.Services.AddTransient<ProvisionsService>();
builder.Services.AddTransient<FinanzService>();
builder.Services.AddTransient<FinanzBerichtPdfService>();
builder.Services.AddTransient<SchoolUserImportService>();
builder.Services.AddTransient<SkoulouApp.WebApp.Services.UnterrichtsVerwaltungService>();
builder.Services.AddTransient<SkoulouApp.WebApp.Services.StundenplanService>();
builder.Services.AddTransient<SkoulouApp.WebApp.Services.BewertungsService>();
builder.Services.AddTransient<SkoulouApp.WebApp.Services.BulletinService>();
builder.Services.AddTransient<SkoulouApp.WebApp.Services.SchulLogoService>();
builder.Services.AddTransient<SkoulouApp.WebApp.Services.AnwesenheitsService>();
builder.Services.AddScoped<SkoulouApp.WebApp.Services.DirektionsCockpitService>();
builder.Services.AddScoped<SkoulouApp.WebApp.Services.QuittungService>();
builder.Services.AddScoped<SkoulouApp.WebApp.Services.SchuelerkartenService>();
builder.Services.AddScoped<SkoulouApp.WebApp.Services.ElternService>();
builder.Services.AddScoped<SkoulouApp.WebApp.Services.KontoZugangService>();
builder.Services.AddTransient<SkoulouApp.WebApp.Services.BulletinPdfService>();
builder.Services.AddTransient<SkoulouApp.WebApp.Services.BulletinPruefungService>();
builder.Services.AddRateLimiter(options => options.AddPolicy("bulletin-verification", context =>
    System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
builder.Services.AddTransient<SkoulouApp.WebApp.Services.StundenplanPdfService>();
builder.Services.AddScoped<ITextToSpeechProvider, BrowserSpeechProvider>();
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    var startupLogger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("StartupMigrationGuard");

    if (app.Environment.IsDevelopment())
    {
        var hasPendingModelChangesMethod = typeof(RelationalDatabaseFacadeExtensions)
            .GetMethod("HasPendingModelChanges", [typeof(DatabaseFacade)]);
        if (hasPendingModelChangesMethod is not null)
        {
            var hasPendingModelChanges = (bool)(hasPendingModelChangesMethod.Invoke(null, [dbContext.Database]) ?? false);
            if (hasPendingModelChanges)
            {
                startupLogger.LogWarning("Des changements de modèle ont été détectés sans migration. Exécute d'abord 'dotnet ef migrations add <Nom>' avant la mise à jour de la base.");
            }
        }
    }

    await SkoulouApp.WebApp.Data.Seed.ApplicationDbInitializer.SeedAsync(
        dbContext,
        roleManager,
        userManager,
        applyMigrations: applyMigrationsOnStartup);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseSerilogRequestLogging();
// Innerhalb der Request-Protokollierung: Clientabbrüche als 499 statt Exception/500.
app.UseMiddleware<RequestAbbruchMiddleware>();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.UseAntiforgery();

app.MapStaticAssets();

// Videos: eigener Endpunkt statt wwwroot, damit Teilanfragen (Range) korrekt beantwortet werden –
// der Browser lädt Videos stückweise. Werbevideo öffentlich, Lernvideos nur für die passenden Rollen.
var videoOrdner = Path.Combine(app.Environment.ContentRootPath, "Medien", "videos");
app.MapGet("/medien/videos/{datei}", (string datei, HttpContext http) =>
{
    if (!System.Text.RegularExpressions.Regex.IsMatch(datei, @"^[a-z0-9-]+\.mp4$"))
        return Results.NotFound();
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

app.MapPost("/api/postmark/inbound", async (HttpRequest request, IConfiguration config, MailboxService mailbox, CancellationToken cancellationToken) =>
{
    var secret = config["Postmark:InboundWebhookSecret"];
    var authorization = request.Headers.Authorization.ToString();
    var expected = "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("skoulou:" + secret));
    if (string.IsNullOrWhiteSpace(secret) || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(authorization)))
        return Results.Unauthorized();

    using var document = await System.Text.Json.JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
    var root = document.RootElement;
    static string Value(System.Text.Json.JsonElement value, string key) => value.TryGetProperty(key, out var item) && item.ValueKind == System.Text.Json.JsonValueKind.String ? item.GetString() ?? string.Empty : string.Empty;
    var from = Value(root, "From");
    var to = Value(root, "To");
    var id = Value(root, "MessageID");
    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(from) || !to.Contains(SkoulouMarke.KontaktEmail, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest();
    await mailbox.EmpfangenAsync(from, to, Value(root, "Subject"), Value(root, "HtmlBody") is { Length: > 0 } html ? html : System.Net.WebUtility.HtmlEncode(Value(root, "TextBody")), id, cancellationToken: cancellationToken);
    return Results.Ok();
}).DisableAntiforgery();

app.MapGet("/media-library/{assetId:guid}", async (
    Guid assetId,
    ClaimsPrincipal user,
    IBlobMediaLibraryService mediaLibrary,
    CancellationToken cancellationToken) =>
{
    var userIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(userIdValue, out var userId))
    {
        return Results.Unauthorized();
    }

    var roles = user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();
    var image = await mediaLibrary.OpenImageAsync(assetId, userId, roles, cancellationToken);
    return image is null
        ? Results.NotFound()
        : Results.File(image.Content, image.ContentType, enableRangeProcessing: true);
}).RequireAuthorization();

app.MapGet("/verification-bulletin/{token}", async (string token, HttpContext http,
    SkoulouApp.WebApp.Services.BulletinPruefungService service, CancellationToken cancellationToken) =>
{
    http.Response.Headers.CacheControl = "no-store";
    http.Response.Headers["Referrer-Policy"] = "no-referrer";
    http.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
    var resultat = await service.PruefenAsync(token, cancellationToken);
    static string E(string? value) => System.Net.WebUtility.HtmlEncode(value ?? "—");
    var contenu = resultat is null ? "<h1>Document non trouvé</h1><p>Vérifiez le code ou contactez l'école. Aucun bulletin ne peut être téléchargé depuis cette page.</p>"
        : $"<h1>Vérification d'un bulletin</h1><p>École : {E(resultat.Schule)}</p><p>N° {E(resultat.DokumentNummer)}</p><p>Date : {resultat.DatumUtc:dd/MM/yyyy} UTC</p><p>Statut : {E(resultat.Status)}</p><p>Remplace : {E(resultat.ErsetztNummer)}</p><p>Remplacé par : {E(resultat.NachfolgerNummer)}</p><p>Cette page confirme uniquement l'existence et le statut du document. Elle ne confirme pas l'identité ni les notes figurant sur un papier présenté. Contactez l'école pour une vérification complète.</p>";
    return Results.Content($"<!doctype html><html lang='fr'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'><title>Vérification de bulletin</title></head><body>{contenu}</body></html>", "text/html; charset=utf-8", statusCode: resultat is null ? 404 : 200);
}).AllowAnonymous().RequireRateLimiting("bulletin-verification");

app.MapGet("/bulletins/{ausgabeID:guid}/pdf", async (Guid ausgabeID, ClaimsPrincipal user,
    SkoulouApp.WebApp.Services.BulletinService service, SkoulouApp.WebApp.Services.BulletinPdfService pdf, CancellationToken token) =>
{
    if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userID)) return Results.Unauthorized();
    try
    {
        var bulletin = await service.LesenAsync(userID, ausgabeID, token);
        return bulletin is null ? Results.NotFound() : Results.File(pdf.Erzeugen(bulletin), "application/pdf", "bulletin-scolaire.pdf");
    }
    catch (UnauthorizedAccessException) { return Results.Forbid(); }
}).RequireAuthorization(policy => policy.RequireRole(Rollen.SchulLeiter, Rollen.Koordinator, Rollen.Schueler, Rollen.Eltern));

app.MapGet("/bulletins/sammelausgabe/{schulID:guid}/{periodeID:guid}/{klasseID:guid}/pdf", async (
    Guid schulID, Guid periodeID, Guid klasseID, ClaimsPrincipal user, HttpContext http,
    SkoulouApp.WebApp.Services.BulletinService service, SkoulouApp.WebApp.Services.BulletinPdfService pdf, CancellationToken token) =>
{
    http.Response.Headers.CacheControl = "no-store, private";
    if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userID)) return Results.Unauthorized();
    try
    {
        var bulletins = await service.SammelausgabeAsync(userID, schulID, periodeID, klasseID, token);
        token.ThrowIfCancellationRequested();
        var datei = pdf.SammelausgabeErzeugen(bulletins);
        return Results.File(datei, "application/pdf", $"bulletins-{periodeID:N}-{klasseID:N}.pdf");
    }
    catch (UnauthorizedAccessException) { return Results.Forbid(); }
    catch (InvalidOperationException ex) { return Results.Text(ex.Message, "text/plain; charset=utf-8", statusCode: StatusCodes.Status400BadRequest); }
}).RequireAuthorization(policy => policy.RequireRole(Rollen.SchulLeiter, Rollen.Koordinator));

app.MapGet("/emplois-du-temps/{schulID:guid}/pdf", async (Guid schulID, ClaimsPrincipal user,
    SkoulouApp.WebApp.Services.StundenplanService service, SkoulouApp.WebApp.Services.StundenplanPdfService pdf,
    CancellationToken token) =>
{
    if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var benutzerID)) return Results.Unauthorized();
    try
    {
        var zeilen = await service.LesenAsync(benutzerID, schulID, token);
        return Results.File(pdf.Erzeugen(zeilen), "application/pdf", "emploi-du-temps.pdf");
    }
    catch (UnauthorizedAccessException) { return Results.Forbid(); }
}).RequireAuthorization(policy => policy.RequireRole(Rollen.SchulLeiter, Rollen.Koordinator, Rollen.Lehrer, Rollen.Schueler));

app.MapGet("/finanzen/rapport/pdf-file", async (
    Guid schoolId,
    DateTime? from,
    DateTime? to,
    FinanzRichtung? direction,
    FinanzSortierung? sort,
    bool? withSubscriptions,
    ClaimsPrincipal user,
    FinanzService finanzService,
    FinanzBerichtPdfService pdfService,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        return Results.Unauthorized();

    var bericht = await finanzService.GetFinanzBerichtAsync(userId, user.IsInRole(Rollen.SuperAdmin), new FinanzFilter
    {
        SchulID = schoolId,
        Von = from,
        Bis = to,
        Richtung = direction,
        Sortierung = sort ?? FinanzSortierung.WirkungsdatumAbsteigend
    }, cancellationToken, withSubscriptions == true);
    if (bericht is null) return Results.NotFound();

    var bytes = pdfService.Generate(bericht);
    return Results.File(bytes, "application/pdf", fileDownloadName: $"rapport-financier-{schoolId:N}-{DateTime.UtcNow:yyyyMMdd}.pdf");
}).RequireAuthorization(policy => policy.RequireRole(Rollen.SuperAdmin, Rollen.ProduktOwner, Rollen.SchulLeiter, Rollen.Koordinator));

// Cartes d'élève einer Klasse (Direktion/Koordination der eigenen Schule).
app.MapGet("/direction/cartes/{klasseId:guid}", async (
    Guid klasseId, ClaimsPrincipal user, SkoulouApp.WebApp.Services.SchuelerkartenService karten, CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        return Results.Unauthorized();
    var ergebnis = await karten.KlasseAsync(userId, klasseId, cancellationToken);
    return ergebnis is null ? Results.NotFound() : Results.File(ergebnis.Value.Pdf, "application/pdf");
}).RequireAuthorization(policy => policy.RequireRole(Rollen.SchulLeiter, Rollen.Koordinator));

// Reçu de paiement (Schulgebühren): Direktion/Koordination mit Finanzrecht, Plattform lesend, Schüler für eigene Zahlungen.
app.MapGet("/finanzen/recu/{zahlungId:guid}", async (
    Guid zahlungId, ClaimsPrincipal user, SkoulouApp.WebApp.Services.QuittungService quittungen, CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        return Results.Unauthorized();
    var quittung = await quittungen.LadenAsync(zahlungId, userId, cancellationToken);
    return quittung is null ? Results.NotFound() : Results.File(quittungen.Pdf(quittung), "application/pdf");
}).RequireAuthorization();

app.MapGet("/finanzen/justificatif/{buchungId:guid}", async (
    Guid buchungId, ClaimsPrincipal user, FinanzService finanzService, CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        return Results.Unauthorized();
    var beleg = await finanzService.GetFinanzBelegAsync(buchungId, userId, cancellationToken);
    if (beleg is null) return Results.NotFound();
    var inhalt = await finanzService.OeffneFinanzBelegAsync(beleg, cancellationToken);
    return inhalt is null ? Results.NotFound() : Results.File(inhalt, beleg.Inhaltstyp, enableRangeProcessing: false);
}).RequireAuthorization(policy => policy.RequireRole(Rollen.SuperAdmin, Rollen.ProduktOwner, Rollen.SchulLeiter, Rollen.Koordinator));

app.MapGet("/teacher-exams/{examId:guid}/pdf-file", async (
    Guid examId,
    bool? solutions,
    HttpResponse response,
    ClaimsPrincipal user,
    ExamService examService,
    LessonExerciseA4PdfService pdfService,
    IBlobMediaLibraryService mediaLibrary,
    CancellationToken cancellationToken) =>
{
    var userIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(userIdValue, out var userId))
    {
        return Results.Unauthorized();
    }

    var includeSolutions = solutions ?? false;
    var canExportSharedDirectionContent = user.IsInRole(Rollen.SchulLeiter) || user.IsInRole(Rollen.Koordinator);
    var exam = await examService.GetExamForPdfAsync(
        userId,
        examId,
        canExportSharedDirectionContent,
        cancellationToken);
    if (exam is null)
    {
        return Results.NotFound();
    }

    var bytes = await pdfService.GenerateHtmlPdfAsync(ExamA4HtmlDocument.BuildHtml(exam, includeSolutions), mediaLibrary, userId, user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray(), cancellationToken);
    var safeTitle = string.IsNullOrWhiteSpace(exam.Title)
        ? "examen"
        : string.Concat(exam.Title.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')).Trim();
    if (string.IsNullOrWhiteSpace(safeTitle))
    {
        safeTitle = "examen";
    }

    var fileName = $"{safeTitle}{(includeSolutions ? "-corrige" : "-sans-solutions")}.pdf";
    return InlinePdfResponse.Create(response, bytes, fileName);
}).RequireAuthorization("RequireTeacher");

app.MapGet("/teacher/lesson-exercises/{lessonId:guid}/pdf-file", async (
    Guid lessonId,
    HttpResponse response,
    ClaimsPrincipal user,
    LearningService learningService,
    LessonExerciseA4PdfService pdfService,
    IBlobMediaLibraryService mediaLibrary,
    CancellationToken cancellationToken) =>
{
    var userIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(userIdValue, out var userId))
    {
        return Results.Unauthorized();
    }

    var lesson = await learningService.GetLessonDetailForTeacherOrDirectionReadOnlyAsync(lessonId, userId, cancellationToken);
    if (lesson is null)
    {
        return Results.NotFound();
    }

    var bytes = await pdfService.GeneratePdfAsync(lesson, mediaLibrary, userId, user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray(), cancellationToken);
    var safeTitle = string.IsNullOrWhiteSpace(lesson.Title)
        ? "support"
        : string.Concat(lesson.Title.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')).Trim();
    if (string.IsNullOrWhiteSpace(safeTitle))
    {
        safeTitle = "support";
    }

    return InlinePdfResponse.Create(response, bytes, $"{safeTitle}-sans-solutions.pdf");
}).RequireAuthorization("RequireTeacher");

app.MapGet("/teacher-lessons/{lessonId:guid}/pdf-file", async (
    Guid lessonId,
    bool? solutions,
    HttpResponse response,
    ClaimsPrincipal user,
    TeacherLessonService teacherLessonService,
    LessonExerciseA4PdfService pdfService,
    IBlobMediaLibraryService mediaLibrary,
    CancellationToken cancellationToken) =>
{
    var userIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(userIdValue, out var userId))
    {
        return Results.Unauthorized();
    }

    var includeSolutions = solutions ?? true;
    var canExportSharedDirectionContent = user.IsInRole(Rollen.SchulLeiter) || user.IsInRole(Rollen.Koordinator);

    var lesson = await teacherLessonService.GetLessonDetailForPdfExportAsync(
        lessonId,
        userId,
        canExportSharedDirectionContent,
        cancellationToken);
    if (lesson is null)
    {
        return Results.NotFound();
    }

    var bytes = await pdfService.GeneratePdfAsync(lesson, mediaLibrary, userId, user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray(), cancellationToken);
    var safeTitle = string.IsNullOrWhiteSpace(lesson.Title)
        ? "support"
        : string.Concat(lesson.Title.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')).Trim();
    if (string.IsNullOrWhiteSpace(safeTitle))
    {
        safeTitle = "support";
    }

    var fileName = $"{safeTitle}{(includeSolutions ? "-avec-solutions" : "-sans-solutions")}.pdf";
    return InlinePdfResponse.Create(response, bytes, fileName);
}).RequireAuthorization("RequireTeacher");

app.MapGet("/student/lessons/support/{lessonId:guid}/pdf-file", async (
    Guid lessonId,
    HttpResponse response,
    ClaimsPrincipal user,
    TeacherLessonService teacherLessonService,
    LessonExerciseA4PdfService pdfService,
    IBlobMediaLibraryService mediaLibrary,
    CancellationToken cancellationToken) =>
{
    var userIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(userIdValue, out var userId))
    {
        return Results.Unauthorized();
    }

    var lesson = await teacherLessonService.GetLessonDetailForStudentPdfExportAsync(lessonId, userId, cancellationToken);
    if (lesson is null)
    {
        return Results.NotFound();
    }

    var bytes = await pdfService.GeneratePdfAsync(lesson, mediaLibrary, userId, user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray(), cancellationToken);
    var safeTitle = string.IsNullOrWhiteSpace(lesson.Title)
        ? "support"
        : string.Concat(lesson.Title.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')).Trim();
    if (string.IsNullOrWhiteSpace(safeTitle))
    {
        safeTitle = "support";
    }

    var fileName = $"{safeTitle}-eleve.pdf";
    return InlinePdfResponse.Create(response, bytes, fileName);
}).RequireAuthorization("RequireStudent");

app.MapGet("/student/exams/{examId:guid}/pdf-file", async (
    Guid examId,
    HttpResponse response,
    ClaimsPrincipal user,
    ExamService examService,
    LessonExerciseA4PdfService pdfService,
    IBlobMediaLibraryService mediaLibrary,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
    {
        return Results.Unauthorized();
    }

    var exam = await examService.GetExamForStudentPdfAsync(userId, examId, cancellationToken);
    if (exam is null)
    {
        return Results.NotFound();
    }

    var bytes = await pdfService.GenerateHtmlPdfAsync(ExamA4HtmlDocument.BuildHtml(exam, includeSolutions: false), mediaLibrary, userId, user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray(), cancellationToken);
    return InlinePdfResponse.Create(response, bytes, "epreuve-sans-solutions.pdf");
}).RequireAuthorization("RequireStudent");

app.MapGet("/student/lessons/{lessonId:guid}/pdf-file", async (
    Guid lessonId,
    HttpResponse response,
    ClaimsPrincipal user,
    LearningService learningService,
    LessonExerciseA4PdfService pdfService,
    IBlobMediaLibraryService mediaLibrary,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
    {
        return Results.Unauthorized();
    }

    var lesson = await learningService.GetLessonDetailAsync(lessonId, userId, cancellationToken);
    if (lesson is null)
    {
        return Results.NotFound();
    }

    var bytes = await pdfService.GeneratePdfAsync(lesson, mediaLibrary, userId, user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray(), cancellationToken);
    return InlinePdfResponse.Create(response, bytes, "lecon-et-exercices-sans-solutions.pdf");
}).RequireAuthorization("RequireStudent");

app.MapGet("/media-library/videos/{assetId:guid}", async (
    Guid assetId,
    Guid? lessonId,
    ClaimsPrincipal user,
    IBlobMediaLibraryService mediaLibrary,
    SkoulouApp.Core.Interfaces.ISkoulouAppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var userIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(userIdValue, out var userId))
    {
        return Results.Unauthorized();
    }

    var roles = user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();
    var hasActiveSubscription = roles.Any(role => role is SkoulouApp.Core.DataModels.Identity.Rollen.SuperAdmin or SkoulouApp.Core.DataModels.Identity.Rollen.ProduktOwner or SkoulouApp.Core.DataModels.Identity.Rollen.SchulLeiter or SkoulouApp.Core.DataModels.Identity.Rollen.Koordinator or SkoulouApp.Core.DataModels.Identity.Rollen.Lehrer)
        || (await new SchuelerZugangService(dbContext).KlassenAsync(userId, mitHistorie: true, cancellationToken)).Count > 0;
    var video = await mediaLibrary.OpenVideoAsync(assetId, userId, roles, hasActiveSubscription, lessonId, cancellationToken);
    return video is null
        ? Results.NotFound()
        : Results.File(video.Content, video.ContentType, enableRangeProcessing: true);
}).RequireAuthorization();

app.MapPost("/auth/login/submit", async (
    HttpContext httpContext,
    IAntiforgery antiforgery,
    AuthService authService,
    UserManager<AppUser> userManager) =>
{
    await antiforgery.ValidateRequestAsync(httpContext);
    var form = await httpContext.Request.ReadFormAsync();
    var identifiant = form["identifiant"].ToString();
    var password = form["password"].ToString();
    var rememberMe = string.Equals(form["rememberMe"], "true", StringComparison.OrdinalIgnoreCase);
    var returnUrl = form["returnUrl"].ToString();

    var result = await authService.PasswordSignInAsync(identifiant, password, rememberMe);
    if (!result.Succeeded)
    {
        return Results.Redirect(result.ErrorMessage == AuthService.BlockedAccountMessage
            ? "/auth/login?error=account-blocked"
            : "/auth/login?error=invalid-credentials");
    }

    var signedInUser = await userManager.FindByNameAsync(identifiant.Trim());
    if (signedInUser is not null)
    {
        var roles = await userManager.GetRolesAsync(signedInUser);
        if (roles.Contains(Rollen.Schueler, StringComparer.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(returnUrl) || returnUrl == "/"))
        {
            return Results.LocalRedirect("/student");
        }

        if (roles.Contains(Rollen.Eltern) && (string.IsNullOrWhiteSpace(returnUrl) || returnUrl == "/"))
        {
            return Results.LocalRedirect("/parents");
        }
    }

    var destination = !string.IsNullOrWhiteSpace(returnUrl)
                      && returnUrl.StartsWith("/", StringComparison.Ordinal)
                      && !returnUrl.StartsWith("//", StringComparison.Ordinal)
        ? returnUrl
        : "/";
    return Results.LocalRedirect(destination);
});

app.MapPost("/auth/logout", async (SignInManager<AppUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/");
});

app.MapGet("/api/formules/favoris", async (
    ClaimsPrincipal user,
    FormelFavoritService favoritService,
    CancellationToken cancellationToken) =>
{
    var userIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(userIdValue, out var userId))
    {
        return Results.Unauthorized();
    }

    var favoriten = await favoritService.LadeAsync(userId, cancellationToken);
    return Results.Ok(favoriten);
}).RequireAuthorization();

app.MapPost("/api/formules/favoris", async (
    ClaimsPrincipal user,
    FormelFavoritService favoritService,
    FormelFavoritAnfrage request,
    CancellationToken cancellationToken) =>
{
    var userIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(userIdValue, out var userId))
    {
        return Results.Unauthorized();
    }

    var favorit = await favoritService.HinzufuegenAsync(userId, request.Latex, request.Bezeichnung, request.Vorschau, cancellationToken);
    return favorit is null ? Results.BadRequest() : Results.Ok(favorit);
}).RequireAuthorization();

app.MapDelete("/api/formules/favoris", async (
    HttpContext httpContext,
    ClaimsPrincipal user,
    FormelFavoritService favoritService,
    CancellationToken cancellationToken) =>
{
    var userIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(userIdValue, out var userId))
    {
        return Results.Unauthorized();
    }

    var latex = httpContext.Request.Query["latex"].ToString();
    if (string.IsNullOrWhiteSpace(latex))
    {
        return Results.BadRequest();
    }

    var entfernt = await favoritService.EntfernenAsync(userId, latex, cancellationToken);
    return entfernt ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization();

app.Run();

internal sealed record FormelFavoritAnfrage(string? Latex, string? Bezeichnung, string? Vorschau);

