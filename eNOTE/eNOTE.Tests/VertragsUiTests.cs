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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Abonnement_Zeigt_Zahlungsuebersicht_Nur_Einmal_Und_Alle_Rabatte(bool mitPo)
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
            if (mitPo)
            {
                var po = new AppUser { Id = Guid.NewGuid(), FullName = "Responsable A" };
                db.Users.Add(po);
                db.BesitzerBetreuungen.Add(new BesitzerBetreuung { BesitzerId = owner.Id, ProduktOwnerId = po.Id });
            }
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
        Assert.Equal(1, Regex.Matches(html, "Mes paiements · Payé / Non payé").Count);
        Assert.Equal(1, Regex.Matches(html, "Tous les paiements").Count);
        Assert.Equal(4, Regex.Matches(html, ">Choisir</button>").Count);
        Assert.Contains("remise 5 %", html);
        Assert.Contains("remise 7,5 %", html);
        Assert.Contains("remise 10 %", html);
        Assert.Contains("15 jours de grâce", html);
        Assert.DoesNotContain("le contrat Mensuel est actif", html);
        Assert.Contains("Destinataire du paiement", html);
        if (mitPo)
        {
            Assert.Contains("Responsable : Responsable A", html);
            Assert.DoesNotContain("Paiement direct à la plateforme", html);
        }
        else
        {
            Assert.Contains("Paiement direct à la plateforme", html);
            Assert.DoesNotContain("Responsable :", html);
        }
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
        Assert.Contains("alert-error", html);
        Assert.Contains("caisse existante", html);
    }
}
