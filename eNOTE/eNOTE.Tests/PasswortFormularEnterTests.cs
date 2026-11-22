using System.Text.RegularExpressions;
using ENote.WebApp.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Befund B-8 (Klicktest 02.01.2029, Nutzerentscheidung 22.11.2026): In « Mon profil » › « Changer le mot de passe » löste Enter
/// nichts aus – das Formular hatte keinen Submit-Knopf (<c>AsyncActionButton</c> mit type=button, zwei Passwortfelder → keine
/// implizite Übermittlung des Browsers). Jetzt senden die Passwortformulare per Enter ab: Submit-Knopf im bzw. über
/// <c>form="…"</c> am Formular, Absenden über <c>onsubmit</c>, höchstens einmal gleichzeitig; ein gesperrter Knopf (unvollständig
/// oder läuft) verhindert auch das Absenden per Enter.
/// </summary>
public class PasswortFormularEnterTests
{
    private static string WebApp()
    {
        var ordner = new DirectoryInfo(AppContext.BaseDirectory);
        while (ordner is not null && !Directory.Exists(Path.Combine(ordner.FullName, "eNOTE", "eNOTE.WebApp"))) ordner = ordner.Parent;
        Assert.NotNull(ordner);
        return Path.Combine(ordner!.FullName, "eNOTE", "eNOTE.WebApp");
    }

    private static string Seite(string name) => File.ReadAllText(Path.Combine(WebApp(), "Components", "Pages", name));

    private static async Task<string> KnopfAsync(Dictionary<string, object?> parameter)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<AsyncActionButton>(ParameterView.FromDictionary(parameter))).ToHtmlString());
    }

    [Fact]
    public async Task Submit_Knopf_Mit_Ladezustand_Von_Aussen_Ist_Gesperrt_Und_Zeigt_Den_Spinner()
    {
        var laeuft = await KnopfAsync(new() { ["Type"] = "submit", ["Text"] = "Enregistrer", ["Busy"] = true, ["form"] = "pf-email-form" });
        Assert.Contains("type=\"submit\"", laeuft);
        Assert.Contains("form=\"pf-email-form\"", laeuft);
        Assert.Contains("disabled", laeuft);
        Assert.Contains("aria-busy=\"true\"", laeuft);
        Assert.Contains("enote-spinner", laeuft);

        var bereit = await KnopfAsync(new() { ["Type"] = "submit", ["Text"] = "Enregistrer", ["Busy"] = false });
        Assert.DoesNotContain("disabled", bereit);
        Assert.DoesNotContain("enote-spinner", bereit);
        Assert.Contains("Enregistrer", bereit);

        var unvollstaendig = await KnopfAsync(new() { ["Type"] = "submit", ["Text"] = "Enregistrer", ["Disabled"] = true });
        Assert.Contains("disabled", unvollstaendig);
    }

    [Fact]
    public void Profil_Passwort_Email_Und_Identifiant_Senden_Per_Enter_Ab()
    {
        var profil = Seite("Profil.razor");
        // Passwort: EditForm mit OnValidSubmit und Submit-Knopf darin, kein zweiter Weg über OnClick (sonst doppelt).
        Assert.Contains("OnValidSubmit=\"SendePasswortAsync\"", profil);
        Assert.Matches(new Regex("<AsyncActionButton Type=\"submit\" Text=\"Changer le mot de passe\" Busy=\"pwLaeuft\"[^>]*Disabled=\"@\\(!PasswortVollstaendig\\)\""), profil);
        // E-Mail und Identifiant: Formular im Dialog, Knopf in der Fußzeile über form="…" verbunden.
        foreach (var (form, methode, laeuft, vollstaendig) in new[]
                 {
                     ("pf-email-form", "SendeEmailAsync", "emailLaeuft", "EmailVollstaendig"),
                     ("pf-pseudonym-form", "SendePseudonymAsync", "pseudonymLaeuft", "PseudonymVollstaendig"),
                 })
        {
            Assert.Contains($"<form id=\"{form}\" class=\"enote-form\" novalidate @onsubmit=\"{methode}\" @onsubmit:preventDefault>", profil);
            Assert.Matches(new Regex($"<AsyncActionButton Type=\"submit\" form=\"{form}\"[^>]*Busy=\"{laeuft}\"[^>]*Disabled=\"@\\(!{vollstaendig}\\)\""), profil);
        }
        Assert.DoesNotContain("OnClick=\"AenderePasswortAsync\"", profil);
        Assert.DoesNotContain("OnClick=\"AendereEmailAsync\"", profil);
        Assert.DoesNotContain("OnClick=\"AenderePseudonymAsync\"", profil);

        // Doppel-Absenden: je Formular eine Sperre, unvollständig wird nichts gesendet.
        var code = File.ReadAllText(Path.Combine(WebApp(), "Components", "Pages", "Profil.razor.cs"));
        Assert.Contains("if (pwLaeuft || !PasswortVollstaendig) return;", code);
        Assert.Contains("if (emailLaeuft || !EmailVollstaendig) return;", code);
        Assert.Contains("if (pseudonymLaeuft || !PseudonymVollstaendig) return;", code);
    }

    [Fact]
    public void Neues_Passwort_Per_Link_Sendet_Per_Enter_Ab()
    {
        // « Nouveau mot de passe » dient der Réinitialisation und der ersten Anmeldung (Link in « Compte créé »).
        var seite = Seite("PasswortZuruecksetzen.razor");
        Assert.Contains("<form class=\"auth-form\" novalidate @onsubmit=\"EnregistrerAsync\" @onsubmit:preventDefault>", seite);
        Assert.Contains("<button type=\"submit\" class=\"btn-primary\" disabled=\"@(laeuft || !Vollstaendig)\">", seite);
        Assert.DoesNotContain("@onclick=\"EnregistrerAsync\"", seite);
        Assert.Contains("if (laeuft || ok || !Vollstaendig) return;", seite);

        // « Mot de passe oublié » (Pseudonym eingeben) ebenso
        var vergessen = Seite("PasswortVergessen.razor");
        Assert.Contains("<form class=\"auth-form\" novalidate @onsubmit=\"EnvoyerAsync\" @onsubmit:preventDefault>", vergessen);
        Assert.Contains("<button type=\"submit\" class=\"btn-primary\" disabled=\"@(laeuft || string.IsNullOrWhiteSpace(pseudonym))\">", vergessen);
        Assert.DoesNotContain("@onclick=\"EnvoyerAsync\"", vergessen);
        Assert.Contains("if (laeuft) return;", vergessen);
    }
}
