using System.Reflection;
using System.Security.Claims;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Components.Pages;
using ENote.WebApp.Components.Shared;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ENote.WebApp.Services.Mails;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Xunit;

namespace ENote.Tests;

public class LeerzustandUiTests
{
    private sealed class Anmeldung(Guid id, string rolle) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, rolle)], "Test"))));
    }

    private sealed class Js : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class KomponentenAktivator(IServiceProvider services, Action<IComponent>? konfigurieren) : IComponentActivator
    {
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)ActivatorUtilities.CreateInstance(services, componentType);
            konfigurieren?.Invoke(component);
            return component;
        }
    }

    private sealed class SeitenRenderer(IServiceProvider services, ILoggerFactory logger)
        : Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure.StaticHtmlRenderer(services, logger)
    {
        protected override IComponent ResolveComponentForRenderMode(Type componentType, int? parentComponentId,
            IComponentActivator componentActivator, IComponentRenderMode renderMode)
            => componentActivator.CreateInstance(componentType);
    }

    private static void SetzeFeld(IComponent component, string name, object wert)
        => component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, wert);

    private static async Task<string> RenderAsync<T>(IDbContextFactory<ApplicationDbContext> factory, Guid user,
        Action<IComponent>? konfigurieren = null, string rolle = AppRollen.SuperAdmin) where T : IComponent
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<AuthenticationStateProvider>(new Anmeldung(user, rolle));
        services.AddSingleton<IJSRuntime, Js>();
        services.AddSingleton(factory);
        services.AddSingleton(TimeProvider.System);
        services.AddTransient(_ => factory.CreateDbContext());
        services.AddIdentityCore<AppUser>().AddRoles<Role>().AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<BesitzerPinState>();
        services.AddSingleton(new BesitzerPinHilfe(null!, factory));
        services.AddSingleton(new KatalogService(factory, null!, null!));
        services.AddSingleton(new KassenService(factory, new FakeUmsatzBenachrichtigungService()));
        services.AddSingleton(_ => new EmailService(new ConfigurationBuilder().Build(), factory, NullLogger<EmailService>.Instance));
        services.AddSingleton<LadeZustand>();
        services.AddScoped<AdminService>();
        services.AddScoped(_ => new AdminKontenService(factory, null!));
        services.AddSingleton<ProduktOwnerService>();
        services.AddSingleton<PoProvisionService>();
        services.AddSingleton(sp => new PreisAnpassungService(factory, TimeProvider.System));
        services.AddSingleton<IComponentActivator>(provider => new KomponentenAktivator(provider, konfigurieren));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new SeitenRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            RenderFragment inhalt = builder =>
            {
                builder.OpenComponent<T>(0);
                builder.CloseComponent();
            };
            var ausgabe = renderer.BeginRenderingComponent(typeof(CascadingAuthenticationState), ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(CascadingAuthenticationState.ChildContent)] = inhalt }));
            await ausgabe.QuiescenceTask;
            return System.Net.WebUtility.HtmlDecode(ausgabe.ToHtmlString());
        });
    }

    [Theory]
    [InlineData("etablissements", false, false, "Aucun établissement pour le moment.")]
    [InlineData("etablissements", true, true, "Aucun établissement ne correspond à votre recherche")]
    [InlineData("plateforme", false, false, "Aucun propriétaire plateforme pour le moment.")]
    [InlineData("plateforme", true, true, "Aucun propriétaire plateforme ne correspond à votre recherche.")]
    public async Task Administration_Zeigt_Leere_Listen_Und_Filtertreffer(string tab, bool mitDaten, bool mitSuche, string hinweis)
    {
        var factory = TestDbContextFactory.Create();
        if (mitDaten)
        {
            await using var db = await factory.CreateDbContextAsync();
            db.Salons.Add(new Salon { Name = "Boutique A" });
            var po = new AppUser { FullName = "Responsable A" };
            var rolle = new Role { Name = AppRollen.ProduktOwner, NormalizedName = AppRollen.ProduktOwner.ToUpperInvariant() };
            db.Users.Add(po);
            db.Roles.Add(rolle);
            db.UserRoles.Add(new AppUserRole { UserId = po.Id, RoleId = rolle.Id });
            await db.SaveChangesAsync();
        }
        var html = await RenderAsync<Admin>(factory, Guid.NewGuid(), component =>
        {
            if (component is not Admin) return;
            SetzeFeld(component, "aktiverTab", tab);
            if (mitSuche) SetzeFeld(component, tab == "plateforme" ? "poSuche" : "salonSuche", "Introuvable");
        });
        Assert.Contains(hinweis, html);
        Assert.Contains("class=\"enote-empty\" role=\"status\"", html);
        Assert.DoesNotContain("Chargement…", html);
    }

    [Fact]
    public async Task Administration_Gefuellte_Liste_Zeigt_Keinen_Leerhinweis()
    {
        var factory = TestDbContextFactory.Create();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(new Salon { Name = "Boutique A" });
            await db.SaveChangesAsync();
        }
        var html = await RenderAsync<Admin>(factory, Guid.NewGuid());
        Assert.Contains("Boutique A", html);
        Assert.DoesNotContain("Aucun établissement pour le moment.", html);
        Assert.DoesNotContain("Aucun établissement ne correspond", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Optionsliste_Zeigt_Hinweis_Nur_Wenn_Leer(bool mitOption)
    {
        var html = await RenderAsync<EnoteRadioGroup<string>>(TestDbContextFactory.Create(), Guid.NewGuid(), component =>
        {
            if (component is EnoteRadioGroup<string> gruppe && mitOption)
                gruppe.Options = [new EnoteRadioOption<string> { Value = "bar", Label = "Espèces" }];
        });
        if (mitOption)
        {
            Assert.Contains("Espèces", html);
            Assert.DoesNotContain("Aucune option disponible.", html);
        }
        else Assert.Contains("Aucune option disponible.", html);
    }
}
