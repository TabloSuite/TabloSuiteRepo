namespace Skoulou.WebApp.Services.Paedagogik;

/// <summary>Bereich Paedagogik: Dienste und Endpunkte (folgen in der jeweiligen Etappe).</summary>
public static class PaedagogikRegistrierung
{
    public static IServiceCollection AddPaedagogik(this IServiceCollection services) => services;

    public static WebApplication MapPaedagogikEndpunkte(this WebApplication app) => app;
}
