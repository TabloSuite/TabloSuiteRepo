using HalloApp.WebApp.Components;
using HalloApp.WebApp.Data;
using HalloApp.Core.DataModels;
using HalloApp.WebApp.Services;
using HalloApp.Core.Interfaces;
using HalloApp.Core.Interfaces.IRepositories;
using HalloApp.Core.Options;
using HalloApp.Core.Repositories;
using HalloApp.Core.Services;
using HalloApp.Core.Services.Email;
using HalloApp.Core.Services.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using System.Security.Claims;
using HalloApp.Core.DataModels.Identity;
using Microsoft.EntityFrameworkCore.Infrastructure;
using HalloApp.WebApp.Services;
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
builder.Services.AddTransient<IHalloAppDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
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
        var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return;
        }

        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByIdAsync(userId);
        var sessionValue = context.Principal?.FindFirstValue("halloapp_session");
        if (user is null
            || user.IsBlocked
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
    options.AddPolicy("RequireSchulLeiter", policy => policy.RequireRole("SchulLeiter", "Koordinator", "ProduktOwner"));
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
if (builder.Environment.IsDevelopment())
{
    builder.Services.PostConfigure<AzureBlobStorageOptions>(options => options.Enabled = false);
}
builder.Services.Configure<PostmarkOptions>(builder.Configuration.GetSection(PostmarkOptions.SectionName));

builder.Services.AddHttpClient("Postmark", client =>
{
    client.BaseAddress = new Uri("https://api.postmarkapp.com/");
    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
});
builder.Services.AddScoped<IEmailService, PostmarkEmailService>();
builder.Services.AddScoped<BroadcastEmailService>();

builder.Services.AddTransient<ICourseRepository, CourseRepository>();
builder.Services.AddTransient<ISchoolRepository, SchoolRepository>();
builder.Services.AddTransient<IUnitOfWork, UnitOfWork>();
builder.Services.AddTransient<IMediaStorageService, LocalMediaStorageService>();
builder.Services.AddTransient<IBlobMediaLibraryService, AzureBlobMediaLibraryService>();
builder.Services.AddTransient<HomePageService>();
builder.Services.AddTransient<DashboardService>();
builder.Services.AddTransient<LearningService>();
builder.Services.AddTransient<LessonExerciseA4PdfService>();
builder.Services.AddTransient<TeacherLessonService>();
builder.Services.AddTransient<ExamService>();
builder.Services.AddTransient<FormelFavoritService>();
builder.Services.AddTransient<AboService>();
builder.Services.AddTransient<NiveauGruppenService>();
builder.Services.AddTransient<AbrechnungService>();
builder.Services.AddTransient<ProvisionsService>();
builder.Services.AddTransient<FinanzService>();
builder.Services.AddTransient<SchoolUserImportService>();
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

    await HalloApp.WebApp.Data.Seed.ApplicationDbInitializer.SeedAsync(
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

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

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

app.MapGet("/teacher-exams/{examId:guid}/pdf-file", async (
    Guid examId,
    bool? solutions,
    ClaimsPrincipal user,
    ExamService examService,
    LessonExerciseA4PdfService pdfService,
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

    var bytes = await pdfService.GenerateHtmlPdfAsync(ExamA4HtmlDocument.BuildHtml(exam, includeSolutions), cancellationToken);
    var safeTitle = string.IsNullOrWhiteSpace(exam.Title)
        ? "examen"
        : string.Concat(exam.Title.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')).Trim();
    if (string.IsNullOrWhiteSpace(safeTitle))
    {
        safeTitle = "examen";
    }

    var fileName = $"{safeTitle}{(includeSolutions ? "-corrige" : "-sans-solutions")}.pdf";
    return Results.File(bytes, "application/pdf", fileName);
}).RequireAuthorization("RequireTeacher");

app.MapGet("/teacher/lesson-exercises/{lessonId:guid}/pdf-file", async (
    Guid lessonId,
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

    return Results.File(bytes, "application/pdf", $"{safeTitle}-sans-solutions.pdf");
}).RequireAuthorization("RequireTeacher");

app.MapGet("/teacher-lessons/{lessonId:guid}/pdf-file", async (
    Guid lessonId,
    bool? solutions,
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
    return Results.File(bytes, "application/pdf", fileName);
}).RequireAuthorization("RequireTeacher");

app.MapGet("/student/lessons/support/{lessonId:guid}/pdf-file", async (
    Guid lessonId,
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
    return Results.File(bytes, "application/pdf", fileName);
}).RequireAuthorization("RequireStudent");

app.MapGet("/student/exams/{examId:guid}/pdf-file", async (
    Guid examId,
    ClaimsPrincipal user,
    ExamService examService,
    LessonExerciseA4PdfService pdfService,
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

    var bytes = await pdfService.GenerateHtmlPdfAsync(ExamA4HtmlDocument.BuildHtml(exam, includeSolutions: false), cancellationToken);
    return Results.File(bytes, "application/pdf", "epreuve-sans-solutions.pdf");
}).RequireAuthorization("RequireStudent");

app.MapGet("/student/lessons/{lessonId:guid}/pdf-file", async (
    Guid lessonId,
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
    return Results.File(bytes, "application/pdf", "lecon-et-exercices-sans-solutions.pdf");
}).RequireAuthorization("RequireStudent");

app.MapGet("/media-library/videos/{assetId:guid}", async (
    Guid assetId,
    Guid? lessonId,
    ClaimsPrincipal user,
    IBlobMediaLibraryService mediaLibrary,
    HalloApp.Core.Interfaces.IHalloAppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var userIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(userIdValue, out var userId))
    {
        return Results.Unauthorized();
    }

    var roles = user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();
    var hasActiveSubscription = roles.Any(role => role is HalloApp.Core.DataModels.Identity.Rollen.SuperAdmin or HalloApp.Core.DataModels.Identity.Rollen.ProduktOwner or HalloApp.Core.DataModels.Identity.Rollen.SchulLeiter or HalloApp.Core.DataModels.Identity.Rollen.Koordinator or HalloApp.Core.DataModels.Identity.Rollen.Lehrer)
        || await dbContext.LevelEnrollments.AnyAsync(enrollment => enrollment.StudentId == userId
            && enrollment.IsUnlocked,
            cancellationToken);
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
    var email = form["email"].ToString();
    var password = form["password"].ToString();
    var rememberMe = string.Equals(form["rememberMe"], "true", StringComparison.OrdinalIgnoreCase);
    var returnUrl = form["returnUrl"].ToString();

    var result = await authService.PasswordSignInAsync(email, password, rememberMe);
    if (!result.Succeeded)
    {
        return Results.Redirect("/auth/login?error=invalid-credentials");
    }

    var signedInUser = await userManager.FindByEmailAsync(email);
    if (signedInUser is not null)
    {
        var roles = await userManager.GetRolesAsync(signedInUser);
        if (roles.Contains(Rollen.Schueler, StringComparer.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(returnUrl) || returnUrl == "/"))
        {
            return Results.LocalRedirect("/student");
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

