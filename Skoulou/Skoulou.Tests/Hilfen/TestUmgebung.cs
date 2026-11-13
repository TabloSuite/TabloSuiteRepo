using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Data.Seed;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Mails;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Hilfen;

/// <summary>
/// Dienste wie in der App, aber mit InMemory-DB, Test-Uhr und Fake-Mailversand
/// (Identity mit Lockout, Sitzungsprüfung, Seeds, Kontodienste, Zugriff).
/// </summary>
internal sealed class TestUmgebung : IAsyncDisposable
{
    public ServiceProvider Dienste { get; }
    public IDbContextFactory<SkoulouDbContext> Db { get; }
    public TestZeit Zeit { get; } = new();
    public FakeVersender Versender { get; } = new();
    public SeedOptionen Seed { get; } = new();

    public TestUmgebung()
    {
        Db = TestDbContextFactory.Create();
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddMemoryCache();
        s.AddDataProtection();
        s.AddSingleton(Db);
        s.AddScoped(_ => Db.CreateDbContext());
        s.AddSingleton<TimeProvider>(Zeit);
        s.AddSingleton<IOptions<SeedOptionen>>(new OptionsWrapper<SeedOptionen>(Seed));
        s.AddSingleton<IOptions<AppOptionen>>(new OptionsWrapper<AppOptionen>(new AppOptionen { BaseUrl = "https://skoulou.test" }));
        s.AddIdentityCore<Konto>(o =>
            {
                o.User.RequireUniqueEmail = false;
                o.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-@+";
                o.Password.RequiredLength = 6;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<Rolle>()
            .AddEntityFrameworkStores<SkoulouDbContext>()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<KontoClaimsFabrik>();
        s.AddSingleton<SitzungsPruefung>();
        s.AddSingleton<Zugriff>();
        s.AddSingleton<AuditService>();
        s.AddSingleton<NummernkreisService>();
        s.AddSingleton<IMailVersender>(Versender);
        s.AddSingleton<MailSignal>();
        s.AddSingleton<MailAusgangService>();
        s.AddScoped<AnmeldeService>();
        s.AddScoped<PasswortService>();
        s.AddScoped<KontoService>();
        s.AddScoped<StartSeed>();
        Dienste = s.BuildServiceProvider();
    }

    public T Hole<T>() where T : notnull => Dienste.CreateScope().ServiceProvider.GetRequiredService<T>();

    /// <summary>Rollen und Einstellungen wie beim Start.</summary>
    public async Task SeedAsync()
    {
        using var scope = Dienste.CreateScope();
        await scope.ServiceProvider.GetRequiredService<StartSeed>().AusfuehrenAsync();
    }

    /// <summary>Schule mit Status anlegen.</summary>
    public async Task<Schule> SchuleAsync(string name = "Collège Test", SchulStatus status = SchulStatus.Active, Guid? responsable = null)
    {
        await using var db = await Db.CreateDbContextAsync();
        var schule = new Schule { Name = name, NameNormalisiert = name.ToLowerInvariant(), Ort = "Lomé", Status = status, ResponsableId = responsable };
        db.Schulen.Add(schule);
        await db.SaveChangesAsync();
        return schule;
    }

    /// <summary>Konto mit Rolle anlegen (Passwort « motdepasse1 »).</summary>
    public async Task<Konto> KontoAsync(string identifiant, string rolle, Guid? schuleId = null, KontoStatus status = KontoStatus.Actif,
        KoordinatorRechte rechte = KoordinatorRechte.Keine, bool agbAkzeptiert = true, string? email = null)
    {
        using var scope = Dienste.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Konto>>();
        string? version = null;
        if (agbAkzeptiert)
        {
            await using var db = await Db.CreateDbContextAsync();
            version = await db.PlattformEinstellungen.Where(p => p.Id == PlattformEinstellung.EinzigeId).Select(p => p.AgbVersion).FirstOrDefaultAsync();
        }
        var konto = new Konto
        {
            UserName = identifiant, Vorname = "Test", Nachname = identifiant, SchuleId = schuleId, Status = status,
            KoordinatorRechte = rechte, AgbVersion = version, Email = email
        };
        var ergebnis = await KontoAnlage.AnlegenAsync(users, konto, rolle, Passwort);
        Assert.True(ergebnis.Succeeded, KontoAnlage.Fehlertext(ergebnis));
        return konto;
    }

    public const string Passwort = "motdepasse1";

    public async ValueTask DisposeAsync() => await Dienste.DisposeAsync();
}
