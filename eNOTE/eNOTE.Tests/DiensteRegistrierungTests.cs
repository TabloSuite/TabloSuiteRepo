using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Klick-Simulation S2: « Mot de passe oublié » lieferte 500, weil <c>KontoWiederherstellung</c> nie in Program.cs registriert
/// war. Unit-Tests legen Dienste selbst an und merken das nicht – darum hier gegen den Quelltext der Registrierung prüfen.
/// </summary>
public class DiensteRegistrierungTests
{
    [Fact]
    public void Alle_in_Komponenten_injizierten_eNOTE_Dienste_sind_registriert()
    {
        var webApp = Path.Combine(RepoWurzel(), "eNOTE", "eNOTE.WebApp");
        var registrierung = File.ReadAllText(Path.Combine(webApp, "Program.cs"))
            + File.ReadAllText(Path.Combine(webApp, "Services", "Storage", "EnoteInfrastruktur.cs"));
        var registriert = Regex.Matches(registrierung, @"Add(?:Scoped|Singleton|Transient|HostedService|DbContextFactory)<([\w.]+)")
            .Select(m => m.Groups[1].Value.Split('.').Last())
            .ToHashSet();

        var fehlend = typeof(ENote.WebApp.Services.AuthService).Assembly.GetTypes()
            .Where(t => typeof(IComponent).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(p => p.GetCustomAttribute<InjectAttribute>() is not null)
                .Select(p => (Komponente: t.Name, Typ: p.PropertyType)))
            .Where(x => x.Typ.Namespace?.StartsWith("ENote.", StringComparison.Ordinal) == true && !x.Typ.IsGenericType)
            .Where(x => !registriert.Contains(x.Typ.Name))
            .Select(x => $"{x.Komponente} → {x.Typ.Name}")
            .Distinct()
            .ToList();

        Assert.True(fehlend.Count == 0, "Nicht registriert: " + string.Join(", ", fehlend));
    }

    // Ausgehend von dieser Quelldatei (Tests laufen auch aus einem Build-Ordner unter %TEMP%).
    private static string RepoWurzel([System.Runtime.CompilerServices.CallerFilePath] string quelle = "")
    {
        foreach (var start in new[] { Path.GetDirectoryName(quelle), AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = string.IsNullOrEmpty(start) ? null : new DirectoryInfo(start);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "eNOTE", "eNOTE.WebApp", "Program.cs")))
                dir = dir.Parent;
            if (dir is not null) return dir.FullName;
        }
        throw new InvalidOperationException("Repo-Wurzel nicht gefunden.");
    }
}
