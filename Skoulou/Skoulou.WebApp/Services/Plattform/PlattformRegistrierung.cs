using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Data.Seed;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Bereich Plattform: Identity, Anmeldung, Sitzung, Konten, Kontakt, Posteingang.</summary>
public static class PlattformRegistrierung
{
    public static IServiceCollection AddPlattform(this IServiceCollection services)
    {
        services.AddIdentityCore<Konto>(o =>
            {
                // Eine E-Mail darf zu mehreren Konten gehören; angemeldet wird mit dem Identifiant.
                o.User.RequireUniqueEmail = false;
                o.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-@+";
                o.Password.RequiredLength = 6;
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
                o.SignIn.RequireConfirmedAccount = false;
            })
            .AddRoles<Rolle>()
            .AddEntityFrameworkStores<SkoulouDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<KontoClaimsFabrik>();

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(o =>
        {
            o.LoginPath = "/auth/connexion";
            o.LogoutPath = "/auth/deconnexion";
            o.AccessDeniedPath = "/acces-refuse";
            o.Cookie.Name = ".Skoulou.Connexion";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.ExpireTimeSpan = TimeSpan.FromDays(14);
            o.SlidingExpiration = true;
            o.Events.OnValidatePrincipal = SitzungsPruefung.PruefeCookieAsync;
            o.Events.OnRedirectToLogin = k =>
            {
                if (k.Request.Path.StartsWithSegments("/api") || k.Request.Path.StartsWithSegments("/medien")) k.Response.StatusCode = 401;
                else k.Response.Redirect(k.RedirectUri);
                return Task.CompletedTask;
            };
        });

        services.AddAuthorization(Richtlinien.Konfigurieren);
        services.AddCascadingAuthenticationState();
        services.AddSingleton<SitzungsPruefung>();
        services.AddScoped<AuthenticationStateProvider, SitzungsRevalidierung>();

        services.AddScoped<AnmeldeService>();
        services.AddScoped<PasswortService>();
        services.AddScoped<KontoService>();
        services.AddScoped<SchulKontext>();
        services.AddSingleton<KontaktService>();
        services.AddSingleton<StartseitenStatistik>();
        services.AddHttpClient(MailWeiterleitung.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<MailWeiterleitung>();
        services.AddSingleton<PostEingangService>();
        // E2: Registrierung, Admin, Responsables, Annonces, Messagerie, Journal, Medien
        services.AddSingleton<RegistrierungsBremse>();
        services.AddSingleton<PlattformMails>();
        services.AddScoped<PlattformKontoFabrik>();
        services.AddScoped<RegistrierungService>();
        services.AddScoped<SchulVerwaltungService>();
        services.AddScoped<KontenVerwaltungService>();
        services.AddScoped<AdministratorenService>();
        services.AddSingleton<AnkuendigungService>();
        services.AddScoped<MessagerieService>();
        services.AddScoped<ProtokollService>();
        services.AddScoped<MedienVerwaltungService>();
        services.AddScoped<PlattformUebersicht>();
        services.AddScoped<StartSeed>();
        services.AddScoped<DemoSeed>();
        return services;
    }

    public static WebApplication MapPlattformEndpunkte(this WebApplication app)
    {
        app.MapAuthEndpunkte();
        app.MapKommunikationsEndpunkte();
        app.MapInscriptionEndpunkt();
        // Öffentlicher Standardpreis für die Firmenseite tablosuite.com (nur die Zahl, zwischengespeichert).
        app.MapGet("/tarifs/public", async (StartseitenStatistik statistik, HttpContext http) =>
        {
            var preis = await statistik.JahrespreisAsync(http.RequestAborted);
            http.Response.Headers.CacheControl = "public, max-age=600";
            return preis is { } p ? Results.Json(new { jahrespreisProSchueler = p, waehrung = "FCFA" }) : Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting(Infrastruktur.RateLimits.Pruefung);
        return app;
    }
}
