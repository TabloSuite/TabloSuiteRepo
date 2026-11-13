namespace Skoulou.WebApp.Services.Finanzen;

/// <summary>Bereich Finanzen: Dienste und Endpunkte (folgen in der jeweiligen Etappe).</summary>
public static class FinanzenRegistrierung
{
    public static IServiceCollection AddFinanzen(this IServiceCollection services) => services;

    public static WebApplication MapFinanzenEndpunkte(this WebApplication app) => app;
}
