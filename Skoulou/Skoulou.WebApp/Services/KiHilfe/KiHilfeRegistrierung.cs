namespace Skoulou.WebApp.Services.KiHilfe;

/// <summary>KI-Hilfe « Poser une question » (Claude Haiku 5.5). Ohne « Ki:AnthropicApiKey » registriert, aber unsichtbar.</summary>
public static class KiHilfeRegistrierung
{
    public static IServiceCollection AddKiHilfe(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<KiHilfeOptionen>().Bind(config.GetSection(KiHilfeOptionen.Abschnitt));
        services.AddSingleton<KiNutzungsZaehler>();
        services.AddSingleton<IKiClient, AnthropicKiClient>();
        services.AddSingleton<KiHilfeDienst>();
        return services;
    }
}
