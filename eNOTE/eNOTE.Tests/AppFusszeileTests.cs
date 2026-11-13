using ENote.WebApp.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ENote.Tests;

public class AppFusszeileTests
{
    private static async Task<string> RenderAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
            System.Net.WebUtility.HtmlDecode((await renderer.RenderComponentAsync<AppFusszeile>(ParameterView.Empty)).ToHtmlString()));
    }

    [Fact]
    public async Task Footer_Zeigt_Franzoesische_Texte_Und_Aktuelles_Jahr()
    {
        var html = await RenderAsync();
        Assert.Contains("<footer", html);
        Assert.Contains("aria-label=\"Informations et contact TabloSuite\"", html);
        Assert.Contains("Votre activité, simplement connectée.", html);
        Assert.Contains($"© {DateTime.UtcNow.Year} TabloSuite", html);
        Assert.Contains("Tous droits réservés.", html);
    }

    [Fact]
    public async Task Footer_Kontakte_Sind_Klickbar_Und_Externe_Links_Geschuetzt()
    {
        var html = await RenderAsync();
        Assert.Contains("href=\"https://tablosuite.com\" target=\"_blank\" rel=\"noopener noreferrer\"", html);
        Assert.Contains("href=\"mailto:contact@tablosuite.com\"", html);
        Assert.Contains("Tablosuite.com", html);
        Assert.Contains("contact@tablosuite.com", html);
        Assert.Contains("Nous écrire", html);
        // Keine private Telefonnummer mehr in der Fußzeile.
        Assert.DoesNotContain("tel:", html);
        Assert.DoesNotContain("Nous appeler", html);
    }
}
