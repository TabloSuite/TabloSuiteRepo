namespace Skoulou.WebApp.Services.Organisation;

/// <summary>Bereich Organisation: Dienste und Endpunkte (folgen in der jeweiligen Etappe).</summary>
public static class OrganisationRegistrierung
{
    public static IServiceCollection AddOrganisation(this IServiceCollection services) => services;

    public static WebApplication MapOrganisationEndpunkte(this WebApplication app) => app;
}
