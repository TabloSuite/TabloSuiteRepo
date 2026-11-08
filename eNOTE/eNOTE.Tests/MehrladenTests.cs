using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Xunit;

namespace ENote.Tests;

public class MehrladenTests
{
    private sealed class Js : IJSRuntime
    {
        public Dictionary<string, string> Werte { get; } = [];
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args)
        {
            var key = (string)args![0]!;
            if (name.EndsWith("setItem")) Werte[key] = (string)args[1]!;
            return ValueTask.FromResult(name.EndsWith("getItem") && Werte.TryGetValue(key, out var wert) ? (T)(object)wert : default!);
        }
        public ValueTask<T> InvokeAsync<T>(string name, CancellationToken ct, object?[]? args) => InvokeAsync<T>(name, args);
    }

    [Fact]
    public async Task Mehrere_Laeden_Pro_Konto_Waehlen_Ersten_Freigegebenen_Laden()
    {
        var factory = TestDbContextFactory.Create();
        var owner = new AppUser { Id = Guid.NewGuid() };
        var a = new Salon { Name = "A", IstFreigegeben = true }; var b = new Salon { Name = "B" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UserSalons.AddRange(new UserSalon { AppUser = owner, Salon = a }, new UserSalon { AppUser = owner, Salon = b });
            await db.SaveChangesAsync();
        }
        var js = new Js(); var state = new AktiverSalonState(factory, js);
        await state.InitialisiereAsync(owner.Id);
        Assert.NotNull(state.Aktiv);
        Assert.Equal(2, state.Laeden.Count);
    }

    [Fact]
    public async Task Entfernte_Auswahl_Waehlt_Nicht_Heimlich_Einen_Anderen_Laden()
    {
        var factory = TestDbContextFactory.Create();
        var owner = new AppUser { Id = Guid.NewGuid() };
        var laeden = new[] { new Salon { Name = "A" } };
        await using (var db = await factory.CreateDbContextAsync())
        {
            foreach (var laden in laeden) db.UserSalons.Add(new UserSalon { AppUser = owner, Salon = laden });
            await db.SaveChangesAsync();
        }
        var state = new AktiverSalonState(factory, new Js()); await state.InitialisiereAsync(owner.Id); await state.WaehleAsync(laeden[0].Id);
        await using (var db = await factory.CreateDbContextAsync())
        { db.UserSalons.Remove(await db.UserSalons.SingleAsync(us => us.SalonId == laeden[0].Id)); await db.SaveChangesAsync(); }
        await state.AktualisiereLaedenAsync(owner.Id);
        Assert.Null(state.Aktiv);
    }

    [Fact]
    public async Task Katalog_Verhindert_Ladenwechsel_Und_Fremde_Rabattzuordnung()
    {
        var factory = TestDbContextFactory.Create();
        var owner = new AppUser { Id = Guid.NewGuid() };
        var role = new Role { Name = AppRollen.Besitzer, NormalizedName = AppRollen.Besitzer.ToUpperInvariant() };
        var a = new Salon { Name = "A", IstFreigegeben = true }; var b = new Salon { Name = "B", IstFreigegeben = true };
        var artikel = new Leistung { SalonId = a.Id, Name = "Original" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Roles.Add(role); db.UserRoles.Add(new AppUserRole { UserId = owner.Id, RoleId = role.Id });
            db.UserSalons.AddRange(new UserSalon { AppUser = owner, Salon = a }, new UserSalon { AppUser = owner, Salon = b });
            db.Leistungen.Add(artikel); await db.SaveChangesAsync();
        }
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging(); services.AddSingleton(factory); services.AddTransient(_ => factory.CreateDbContext());
        services.AddIdentityCore<AppUser>().AddRoles<Role>().AddEntityFrameworkStores<ENote.WebApp.Data.ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var katalog = new KatalogService(factory, scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>(), null!);
        // Leistung mit nicht vorhandener Id läuft in den Aktualisierungszweig und schlägt fehl.
        await Assert.ThrowsAsync<InvalidOperationException>(() => katalog.SpeichereLeistungAsync(new Leistung { Id = Guid.NewGuid(), SalonId = b.Id, Name = "Falsch" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => katalog.SpeichereRabattAsync(new Rabatt { Id = Guid.NewGuid(), SalonId = b.Id, LeistungId = artikel.Id, Name = "Falsch" }));
        await using var kontrolle = await factory.CreateDbContextAsync(); Assert.Equal("Original", (await kontrolle.Leistungen.SingleAsync()).Name);
    }

    [Fact]
    public async Task Bon_Medien_Zugriff_Nur_Mit_Eigener_Aktiver_Zuordnung()
    {
        var factory = TestDbContextFactory.Create(); var u = new AppUser { Id = Guid.NewGuid() }; var a = new Salon { Name = "A" };
        await using var db = await factory.CreateDbContextAsync();
        db.UserSalons.Add(new UserSalon { AppUser = u, Salon = a }); await db.SaveChangesAsync();
        Assert.True(await LadenZugriff.HatZugangAsync(db, u.Id, a.Id));
        Assert.False(await LadenZugriff.HatZugangAsync(db, Guid.NewGuid(), a.Id));
        Assert.False(await LadenZugriff.HatZugangAsync(db, u.Id, Guid.NewGuid()));
        u.IsBlocked = true; await db.SaveChangesAsync(); Assert.False(await LadenZugriff.HatZugangAsync(db, u.Id, a.Id));
    }
}
