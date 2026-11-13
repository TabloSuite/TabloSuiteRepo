namespace Skoulou.WebApp.Services.Organisation;

/// <summary>Bereich Organisation: Dienste und Endpunkte.</summary>
public static class OrganisationRegistrierung
{
    public static IServiceCollection AddOrganisation(this IServiceCollection services)
    {
        services.AddScoped<ElternKinderService>();
        return services;
    }

    public static WebApplication MapOrganisationEndpunkte(this WebApplication app) => app;
}
