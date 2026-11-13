using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>
/// Eine aktive Sitzung je Konto: der Cookie-Claim muss der <c>AktiveSitzungId</c> entsprechen und das
/// Konto muss angemeldet bleiben dürfen. Stand je Konto 60 s im Speicher (kein DB-Zugriff je Anfrage).
/// </summary>
public sealed class SitzungsPruefung(IDbContextFactory<SkoulouDbContext> dbFactory, IMemoryCache cache)
{
    public static readonly TimeSpan CacheDauer = TimeSpan.FromSeconds(60);

    public sealed record Stand(Guid? AktiveSitzungId, bool Zugelassen);

    private static string Schluessel(Guid kontoId) => "sitzung:" + kontoId.ToString("N");

    public async Task<bool> IstGueltigAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var kontoId = user.KontoId();
        var sitzung = user.FindFirstValue(SkoulouClaims.Sitzung);
        if (kontoId is null || string.IsNullOrEmpty(sitzung)) return false;
        var stand = await StandAsync(kontoId.Value, ct);
        return stand.Zugelassen && string.Equals(stand.AktiveSitzungId?.ToString(), sitzung, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<Stand> StandAsync(Guid kontoId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(Schluessel(kontoId), out Stand? stand) && stand is not null) return stand;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var k = await db.Users.AsNoTracking().Where(x => x.Id == kontoId)
            .Select(x => new { x.AktiveSitzungId, x.Status, x.Rolle, SchulStatus = x.Schule == null ? (SchulStatus?)null : x.Schule.Status })
            .FirstOrDefaultAsync(ct);
        stand = k is null
            ? new Stand(null, false)
            : new Stand(k.AktiveSitzungId, AnmeldeRegeln.IstZugelassen(AnmeldeRegeln.Pruefe(k.Status, k.Rolle, k.SchulStatus)));
        cache.Set(Schluessel(kontoId), stand, CacheDauer);
        return stand;
    }

    /// <summary>Nach Anmeldung, Passwortwechsel oder Sperre sofort neu laden.</summary>
    public void Vergessen(Guid kontoId) => cache.Remove(Schluessel(kontoId));

    /// <summary>Cookie-Ereignis: ungültige Sitzung → abmelden.</summary>
    public static async Task PruefeCookieAsync(CookieValidatePrincipalContext kontext)
    {
        if (kontext.Principal?.Identity?.IsAuthenticated != true) return;
        var pruefung = kontext.HttpContext.RequestServices.GetRequiredService<SitzungsPruefung>();
        if (!await pruefung.IstGueltigAsync(kontext.Principal, kontext.HttpContext.RequestAborted))
        {
            kontext.RejectPrincipal();
            await kontext.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    }
}

/// <summary>Prüft laufende Blazor-Verbindungen alle 60 s mit derselben Regel.</summary>
public sealed class SitzungsRevalidierung(ILoggerFactory logger, SitzungsPruefung pruefung)
    : RevalidatingServerAuthenticationStateProvider(logger)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(60);

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState zustand, CancellationToken ct)
        => zustand.User.Identity?.IsAuthenticated == true ? pruefung.IstGueltigAsync(zustand.User, ct) : Task.FromResult(true);
}

/// <summary>Ergänzt Sitzung, Anzeigename, Schule und Hauptrolle als Claims.</summary>
public sealed class KontoClaimsFabrik(UserManager<Konto> users, RoleManager<Rolle> rollen, IOptions<IdentityOptions> optionen)
    : UserClaimsPrincipalFactory<Konto, Rolle>(users, rollen, optionen)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Konto konto)
    {
        var identitaet = await base.GenerateClaimsAsync(konto);
        if (konto.AktiveSitzungId is { } sitzung) identitaet.AddClaim(new Claim(SkoulouClaims.Sitzung, sitzung.ToString()));
        identitaet.AddClaim(new Claim(SkoulouClaims.Name, konto.VollerName));
        if (konto.SchuleId is { } schule) identitaet.AddClaim(new Claim(SkoulouClaims.Schule, schule.ToString()));
        if (!string.IsNullOrEmpty(konto.Rolle) && !identitaet.HasClaim(ClaimTypes.Role, konto.Rolle))
            identitaet.AddClaim(new Claim(ClaimTypes.Role, konto.Rolle));
        return identitaet;
    }
}
