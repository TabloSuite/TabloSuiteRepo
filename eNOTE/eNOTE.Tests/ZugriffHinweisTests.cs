using System.Security.Claims;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Components.Pages;
using ENote.WebApp.Services.Storage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Fehlersuche 21.11.2026: (1) Der Responsable sah im Bon eines zugewiesenen Ladens den Kundennamen (Regel: PO nie
/// Kundendaten). (2) Ein angemeldeter Nutzer ohne Recht landete auf dem Anmeldeformular (AccessDeniedPath = /auth/login).
/// </summary>
public class ZugriffHinweisTests
{
    private static ClaimsPrincipal Person(Guid id) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Test"));

    [Fact]
    public async Task Kundenname_Im_Bon_Nur_Fuer_Mitglieder_Des_Ladens()
    {
        var f = TestDbContextFactory.Create();
        var laden = new Salon { Name = "Bon " + Guid.NewGuid().ToString("N")[..6], IstAktiv = true, IstFreigegeben = true };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Besitzer" };
        var ama = new AppUser { Id = Guid.NewGuid(), FullName = "Ama" };
        var po = new AppUser { Id = Guid.NewGuid(), FullName = "Responsable" };
        var gesperrt = new AppUser { Id = Guid.NewGuid(), FullName = "Gesperrt", IsBlocked = true };
        await using (var db = await f.CreateDbContextAsync())
        {
            db.Users.AddRange(besitzer, ama, po, gesperrt);
            db.Salons.Add(laden);
            db.UserSalons.AddRange(new UserSalon { AppUserId = besitzer.Id, SalonId = laden.Id }, new UserSalon { AppUserId = ama.Id, SalonId = laden.Id },
                new UserSalon { AppUserId = gesperrt.Id, SalonId = laden.Id });
            db.ProduktOwnerSalons.Add(new ProduktOwnerSalon { ProduktOwnerId = po.Id, SalonId = laden.Id });
            await db.SaveChangesAsync();
        }
        var zugriff = new MedienZugriff(f);
        Assert.True(await zugriff.DarfKundenSehenAsync(Person(besitzer.Id), laden.Id));
        Assert.True(await zugriff.DarfKundenSehenAsync(Person(ama.Id), laden.Id));
        Assert.False(await zugriff.DarfKundenSehenAsync(Person(po.Id), laden.Id));
        Assert.False(await zugriff.DarfKundenSehenAsync(Person(gesperrt.Id), laden.Id));
        Assert.False(await zugriff.DarfKundenSehenAsync(new ClaimsPrincipal(new ClaimsIdentity()), laden.Id));
    }

    [Fact]
    public async Task Zugriff_Verweigert_Zeigt_Hinweis_Statt_Anmeldeformular()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            System.Net.WebUtility.HtmlDecode((await renderer.RenderComponentAsync<AccesRefuse>(ParameterView.Empty)).ToHtmlString()));
        Assert.Contains("Accès refusé", html);
        Assert.Contains("Vous restez connecté", html);
        Assert.DoesNotContain("Connectez-vous", html);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "eNOTE.WebApp"))) dir = dir.Parent;
        var program = File.ReadAllText(Path.Combine(dir!.FullName, "eNOTE.WebApp", "Program.cs"));
        Assert.Contains("options.AccessDeniedPath = \"/acces-refuse\"", program);
    }
}
