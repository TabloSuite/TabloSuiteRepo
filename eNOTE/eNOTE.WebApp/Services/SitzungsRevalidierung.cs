using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace ENote.WebApp.Services;

/// <summary>
/// « Déconnecter » wirkt sofort (21.11.2026): Bisher prüfte nur das Cookie bei jeder HTTP-Anfrage die Anmeldung – eine offene
/// Blazor-Verbindung arbeitete nach « Déconnecter » unter « Appareils connectés » (oder nach einer Sperre) weiter, bis die Seite
/// neu geladen wurde. Diese Prüfung läuft je offener Verbindung alle <see cref="Intervall"/> (Vorbild SKOULOU, 60 s) mit
/// <see cref="AnmeldeSitzungen.IstNochGueltigAsync"/>; ist die Anmeldung ungültig, wird der Zustand anonym und
/// <c>SitzungsWaechter</c> lädt die Anmeldeseite neu. Fehler der Prüfung (z. B. Datenbank kurz weg) melden niemanden ab.
/// </summary>
public sealed class SitzungsRevalidierung(ILoggerFactory loggerFactory, AnmeldeSitzungen sitzungen)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    /// <summary>Spätestens nach dieser Zeit merkt eine offene Seite, dass ihr Gerät abgemeldet wurde.</summary>
    public static readonly TimeSpan Intervall = TimeSpan.FromSeconds(30);

    private readonly ILogger logger = loggerFactory.CreateLogger<SitzungsRevalidierung>();

    protected override TimeSpan RevalidationInterval => Intervall;

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
        => PruefeAsync(authenticationState.User, cancellationToken);

    /// <summary>true = angemeldet bleiben. Bei Fehlern ebenfalls true (die nächste Prüfung oder das Cookie entscheidet).</summary>
    public async Task<bool> PruefeAsync(ClaimsPrincipal benutzer, CancellationToken ct = default)
    {
        try
        {
            return await sitzungen.IstNochGueltigAsync(benutzer, ct);
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                logger.LogWarning(ex, "Sitzungsprüfung der offenen Verbindung fehlgeschlagen – Anmeldung bleibt bis zur nächsten Prüfung bestehen.");
            return true;
        }
    }
}
