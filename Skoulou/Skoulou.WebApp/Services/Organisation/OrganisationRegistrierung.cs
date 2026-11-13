using Skoulou.WebApp.Services.Organisation.Druck;
using Skoulou.WebApp.Services.Organisation.Import;

namespace Skoulou.WebApp.Services.Organisation;

/// <summary>Bereich Organisation: Dienste und Endpunkte.</summary>
public static class OrganisationRegistrierung
{
    public static IServiceCollection AddOrganisation(this IServiceCollection services)
    {
        services.AddScoped<ElternKinderService>();
        services.AddScoped<KontoFabrik>();
        services.AddSingleton<ZugangsAblage>();
        services.AddSingleton<OrgMails>();
        services.AddScoped<SchulService>();
        services.AddScoped<SchuljahrService>();
        services.AddScoped<KlassenService>();
        services.AddScoped<FaecherService>();
        services.AddScoped<UnterrichtService>();
        services.AddScoped<KontoVerwaltungService>();
        services.AddScoped<PersonalService>();
        services.AddScoped<SchuelerService>();
        services.AddScoped<EinschreibungService>();
        services.AddScoped<ElternService>();
        services.AddScoped<StundenplanService>();
        services.AddScoped<AnwesenheitService>();
        services.AddScoped<CockpitService>();
        services.AddScoped<ImportService>();
        services.AddOrganisationDruck();
        return services;
    }

    public static WebApplication MapOrganisationEndpunkte(this WebApplication app)
    {
        app.MapImportEndpunkte();
        app.MapOrganisationDruckEndpunkte();
        return app;
    }
}
