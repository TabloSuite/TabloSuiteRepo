namespace Skoulou.WebApp.Services.Noten;

/// <summary>Bereich Noten: Dienste und Endpunkte (folgen in der jeweiligen Etappe).</summary>
public static class NotenRegistrierung
{
    public static IServiceCollection AddNoten(this IServiceCollection services) => services;

    public static WebApplication MapNotenEndpunkte(this WebApplication app) => app;
}
