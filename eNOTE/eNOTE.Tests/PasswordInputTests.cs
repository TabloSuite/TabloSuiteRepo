using ENote.WebApp.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ENote.Tests;

public class PasswordInputTests
{
    private static async Task<string> RenderAsync(Dictionary<string, object?> parameter)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var ausgabe = await renderer.RenderComponentAsync<PasswordInput>(ParameterView.FromDictionary(parameter));
            return ausgabe.ToHtmlString();
        });
    }

    [Fact]
    public async Task PinFeld_Bewahrt_Null_Und_Reicht_Format_Und_Autofill_Hinweise_Weiter()
    {
        var html = await RenderAsync(new Dictionary<string, object?>
        {
            [nameof(PasswordInput.Value)] = "0707",
            [nameof(PasswordInput.Name)] = "nouveau-pin-proprietaire-salon-b",
            [nameof(PasswordInput.Autocomplete)] = "new-password",
            [nameof(PasswordInput.AdditionalAttributes)] = new Dictionary<string, object>
            {
                ["id"] = "besitzer-pin", ["inputmode"] = "numeric", ["maxlength"] = "6",
                ["pattern"] = "[0-9]{4,6}", ["data-lpignore"] = "true", ["data-1p-ignore"] = "true",
                ["data-bwignore"] = "true", ["aria-describedby"] = "besitzer-pin-hinweis"
            }
        });

        Assert.Contains("type=\"password\"", html);
        Assert.Contains("value=\"0707\"", html);
        Assert.Contains("name=\"nouveau-pin-proprietaire-salon-b\"", html);
        Assert.Contains("autocomplete=\"new-password\"", html);
        Assert.Contains("inputmode=\"numeric\"", html);
        Assert.Contains("maxlength=\"6\"", html);
        Assert.Contains("pattern=\"[0-9]{4,6}\"", html);
        Assert.Contains("data-lpignore=\"true\"", html);
        Assert.Contains("data-1p-ignore=\"true\"", html);
        Assert.Contains("data-bwignore=\"true\"", html);
        Assert.Contains("aria-describedby=\"besitzer-pin-hinweis\"", html);
        // Fehlersuche 21.11.2026: Umschalter zeigte das Emoji « 👁 » (Projektregel: nur <Icon>) – jetzt SVG-Symbole
        Assert.DoesNotContain("👁", html);
        Assert.Contains("enote-pw-zeigen", html);
        Assert.Contains("<svg", html);
    }

    [Fact]
    public async Task Normales_Passwortfeld_Behaelt_Seine_Bisherigen_Eigenschaften()
    {
        var html = await RenderAsync(new Dictionary<string, object?>
        {
            [nameof(PasswordInput.Name)] = "password",
            [nameof(PasswordInput.Required)] = true,
            [nameof(PasswordInput.Autocomplete)] = "current-password"
        });

        Assert.Contains("type=\"password\"", html);
        Assert.Contains("name=\"password\"", html);
        Assert.Contains("autocomplete=\"current-password\"", html);
        Assert.Contains("required", html);
        Assert.Contains("enoteTogglePassword(this)", html);
        Assert.DoesNotContain("maxlength", html);
        Assert.DoesNotContain("inputmode", html);
    }

    [Fact]
    public async Task Leerer_PinEntwurf_Enthaelt_Keinen_Vorherigen_Code()
    {
        var html = await RenderAsync(new Dictionary<string, object?>
        {
            [nameof(PasswordInput.Value)] = string.Empty,
            [nameof(PasswordInput.Autocomplete)] = "new-password"
        });

        Assert.DoesNotContain("value=\"0707\"", html);
        Assert.DoesNotContain("value=\"Passwo\"", html);
        Assert.DoesNotContain("current-password", html);
    }
}
