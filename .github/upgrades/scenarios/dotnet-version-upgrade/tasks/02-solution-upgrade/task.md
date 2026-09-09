# 02-solution-upgrade: Upgrade projects and NuGet packages to .NET 10

Upgrade `HalloApp.Core`, `HalloApp.WebApp`, and `HalloApp.Tests` together from `net9.0` to `net10.0`. Update Microsoft EF Core, ASP.NET Core Identity EF, EF provider/tooling packages, and test packages where supported. Resolve package incompatibility and the API compatibility findings in the Blazor WebApp inline rather than deferring stubs.

The assessment highlights package updates for EF Core 9.x packages to 10.x and flags `xunit` as outdated in the test project. It also reports API compatibility findings around configuration/identity extension methods, `TimeSpan.FromSeconds`, `System.Uri`, and exception handling behavior in the WebApp; these should be researched and fixed only where code or build output requires changes.

**Done when**: all project files target `net10.0`, relevant NuGet packages are updated to supported versions, restore succeeds, and the solution builds without errors or warnings caused by the upgrade.

## Research Findings

### Projects Affected
- `HalloApp.Core/HalloApp.Core.csproj` — TargetFramework von `net9.0` auf `net10.0`; EF Core/Identity EF Pakete von `9.0.0` auf `10.0.10`.
- `HalloApp.WebApp/HalloApp.WebApp.csproj` — Blazor WebApp; TargetFramework von `net9.0` auf `net10.0`; EF Core/Identity/SqlServer/Design/Tools Pakete von `9.0.0` auf `10.0.10`.
- `HalloApp.Tests/HalloApp.Tests.csproj` — Testprojekt; TargetFramework von `net9.0` auf `net10.0`; `Microsoft.EntityFrameworkCore.InMemory` auf `10.0.10`, `Microsoft.NET.Test.Sdk` auf `18.8.1`, `xunit` auf `2.9.3`.

### Files Modified
- `HalloApp.Core/HalloApp.Core.csproj` — TFM und PackageReference-Versionen.
- `HalloApp.WebApp/HalloApp.WebApp.csproj` — TFM und PackageReference-Versionen.
- `HalloApp.Tests/HalloApp.Tests.csproj` — TFM und PackageReference-Versionen.
- `HalloApp.WebApp/Components/Pages/Login.razor.cs` — Blazor-Warnung BL0008 behoben, indem das Formularmodell ohne `[SupplyParameterFromForm]`-Initializer bereitgestellt wird.
- `HalloApp.Tests/Services/ProvisionsServiceTests.cs` — xUnit Analyzer-Warnung xUnit2031 behoben.

### Packages Updated
| Package | Current | Target | Notes |
|---------|---------|--------|-------|
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 9.0.0 | 10.0.10 | Assessment-Empfehlung für .NET 10 |
| Microsoft.EntityFrameworkCore | 9.0.0 | 10.0.10 | Assessment-Empfehlung für .NET 10 |
| Microsoft.EntityFrameworkCore.Design | 9.0.0 | 10.0.10 | WebApp tooling package |
| Microsoft.EntityFrameworkCore.SqlServer | 9.0.0 | 10.0.10 | WebApp provider package |
| Microsoft.EntityFrameworkCore.Tools | 9.0.0 | 10.0.10 | WebApp tooling package |
| Microsoft.EntityFrameworkCore.InMemory | 9.0.0 | 10.0.10 | Test provider package |
| Microsoft.NET.Test.Sdk | 17.11.1 | 18.8.1 | Unterstützte Version für net10.0 |
| xunit | 2.9.2 | 2.9.3 | Assessment-Empfehlung; v2 beibehalten statt v3-Migration |
| xunit.runner.visualstudio | 2.8.2 | 3.1.5 | Verbleibendes stabiles Update aus finaler Paketprüfung |

### API Changes / Migration Patterns
- Assessment-Hinweise in `Program.cs`, `StudentExam.razor.cs` und generiertem Razor-Code führten nach Paket-/TFM-Upgrade zu keinen Buildfehlern.
- `Login.razor.cs`: `[SupplyParameterFromForm]`-Property nicht mehr mit Initializer versehen; stattdessen nullable Backing-Property plus nicht-null Modellzugriff.
- `ProvisionsServiceTests.cs`: `Assert.Single(collection.Where(...))` auf `Assert.Single(collection, predicate)` umgestellt.

### Validation
- `dotnet restore HALLOAPPMAPPE.slnx` erfolgreich.
- `dotnet build HALLOAPPMAPPE.slnx` erfolgreich mit 0 Fehlern und 0 Warnungen.
- `dotnet test HalloApp.Tests/HalloApp.Tests.csproj` erfolgreich: 106 Tests bestanden, 0 fehlgeschlagen.

### Decisions Made
- `xunit.runner.visualstudio` wurde nicht auf `4.0.0-pre.5` aktualisiert, weil diese Version als Preview gemeldet wurde; stattdessen wurde das stabile verfügbare Update `3.1.5` übernommen.
- Keine Decomposition nötig: die Änderungen folgen einem einheitlichen TFM-/Package-Update-Muster über 3 SDK-style Projekte.
