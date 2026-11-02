using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.Services;
using SkoulouApp.WebApp.Data;

namespace SkoulouApp.WebApp.Services;

/// <summary>Prüft auch laufende Blazor-Circuits, nicht nur den nächsten HTTP-Aufruf.</summary>
public sealed class SchulAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IDbContextFactory<ApplicationDbContext> factory) : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken token)
    {
        if (!Guid.TryParse(state.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userID)
            || !Guid.TryParse(state.User.FindFirstValue("skoulouapp_session"), out var sessionID)) return false;
        await using var db = await factory.CreateDbContextAsync(token);
        return await db.Users.AsNoTracking().AnyAsync(u => u.Id == userID && !u.IsBlocked && u.ActiveSessionId == sessionID, token)
            && await new SchulZugangService(db).DarfAnmeldenAsync(userID, token);
    }
}
