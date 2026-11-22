using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace Skoulou.Tests.Plattform;

/// <summary>
/// Klicktest 21.11.2026: Seiten banden ?classe=, ?kind=, ?id=, ?solutions= … als Guid?/int?. Ein kaputter oder abgeschnittener
/// Link (« ?classe=kaputt ») brach die Seite mit « Une erreur inattendue » bzw. HTTP 500 ab. Adressparameter sind deshalb Text
/// und werden von der Seite selbst mit TryParse gelesen.
/// </summary>
public class AdressParameterTests
{
    [Fact]
    public void Adressparameter_sind_immer_Text()
    {
        var falsch = typeof(Program).Assembly.GetTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(p => p.GetCustomAttribute<SupplyParameterFromQueryAttribute>() is not null && p.PropertyType != typeof(string))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name} ({p.PropertyType.Name})")
            .ToList();
        Assert.True(falsch.Count == 0, "Adressparameter mit Nicht-Text-Typ: " + string.Join(", ", falsch));
    }
}
