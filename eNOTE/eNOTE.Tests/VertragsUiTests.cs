using System.Security.Claims;
using System.Text.RegularExpressions;
using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Components.Pages;
using ENote.WebApp.Components.Shared;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace ENote.Tests;

public class VertragsUiTests
{
    private sealed class SeitenRenderer(IServiceProvider services, ILoggerFactory logger)
        : Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure.StaticHtmlRenderer(services, logger)
    {
        protected override IComponent ResolveComponentForRenderMode(Type componentType, int? parentComponentId,
            IComponentActivator componentActivator, IComponentRenderMode renderMode)
            => componentActivator.CreateInstance(componentType);
    }

    private class Anmeldung(Guid id) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                new Claim(ClaimTypes.Role, AppRollen.Besitzer)], "Test"))));
    }

    private class Js : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }

    [Fact]
    public async Task Abonnement_Zeigt_Essai_Banner_Und_Vier_Formeln_Nach_Vertragsregeln()
    {
        var factory = TestDbContextFactory.Create();
        var owner = new AppUser { Id = Guid.NewGuid() };
        var salon = new Salon { Name = "Boutique A", IstFreigegeben = true, ProbeBisUtc = DateTime.UtcNow.AddDays(10) };
        await using (var db = await factory.CreateDbContextAsync())
        {
            var rolle = new Role { Name = AppRollen.Besitzer };
            db.Roles.Add(rolle);
            db.UserRoles.Add(new AppUserRole { UserId = owner.Id, RoleId = rolle.Id });
            db.UserSalons.Add(new UserSalon { AppUser = owner, Salon = salon });
            db.PlattformPreise.Add(new PlattformPreis { Intervall = AboIntervall.Monatlich, Preis = 5000 });
            await db.SaveChangesAsync();
        }
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IJSRuntime, Js>();
        services.AddSingleton<AuthenticationStateProvider>(new Anmeldung(owner.Id));
        services.AddSingleton(factory);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AboService>();
        services.AddSingleton<VertragsService>();
        services.AddSingleton<AktiverSalonState>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<BesitzerPinState>();
        services.AddSingleton<LadeZustand>();
        services.AddSingleton(new KatalogService(factory, null!, null!));
        services.AddSingleton(new KassenService(factory, new FakeUmsatzBenachrichtigungService()));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new SeitenRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var ausgabe = renderer.BeginRenderingComponent(typeof(Abonnement), ParameterView.Empty);
            await ausgabe.QuiescenceTask;
            return ausgabe.ToHtmlString();
        });
        html = System.Net.WebUtility.HtmlDecode(html);
        // Titel und kostenlose Probezeit
        Assert.Contains("Abonnement", html);
        Assert.Contains("Boutique A", html);
        Assert.Contains("Essai gratuit", html);
        // Vier Formeln (wie im Admin-Tarif): die künftige ist markiert, die drei anderen wählbar
        Assert.Equal(3, Regex.Matches(html, ">Choisir</button>").Count);
        Assert.Contains("Sélectionnée", html);
        Assert.Contains("Mensuel", html);
        Assert.Contains("Trimestriel", html);
        Assert.Contains("Semestriel", html);
        Assert.Contains("Annuel", html);
        // Abgeleitete Preise aus 5.000 F/Monat: 14.250 (−5 %), 27.750 (−7,5 %), 54.000 (−10 %)
        Assert.Matches(new Regex(@"14\D?250"), html);
        Assert.Matches(new Regex(@"27\D?750"), html);
        Assert.Matches(new Regex(@"54\D?000"), html);
        // Geseedeter Monatspreis wird angezeigt (Tausendertrennzeichen kulturabhängig)
        Assert.Matches(new Regex(@"5\D?000"), html);
    }

    [Fact]
    public async Task Kassenhinweis_Verwendet_Sitzungsladen_Statt_Aktiver_Auswahl()
    {
        var factory = TestDbContextFactory.Create();
        var owner = new AppUser();
        var a = new Salon { Name = "A", IstFreigegeben = true, ProbeBisUtc = DateTime.UtcNow.AddDays(30) };
        var b = new Salon { Name = "B", IstFreigegeben = true, IndividuellerMonatsPreis = 1000 };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UserSalons.AddRange(new UserSalon { AppUser = owner, Salon = a }, new UserSalon { AppUserId = owner.Id, Salon = b });
            db.NutzungsAbos.Add(new NutzungsAbo { Salon = b, ZeitraumVonUtc = DateTime.UtcNow.AddMonths(-1), ZeitraumBisUtc = DateTime.UtcNow.AddMonths(1), ZahlungStatus = AboZahlungStatus.Offen });
            await db.SaveChangesAsync();
        }
        var state = new AktiverSalonState(factory, new Js());
        await state.InitialisiereAsync(owner.Id);
        Assert.Equal(a.Id, state.Aktiv!.SalonId);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddSingleton<AuthenticationStateProvider>(new Anmeldung(owner.Id));
        services.AddSingleton(new KassenZugangService(factory, TimeProvider.System));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<KassenZugangsHinweis>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(KassenZugangsHinweis.SalonId)] = b.Id }))).ToHtmlString());
        Assert.Contains("enote-sperre gesperrt", html);
        Assert.Contains("clôturer la caisse", html);
    }
}
