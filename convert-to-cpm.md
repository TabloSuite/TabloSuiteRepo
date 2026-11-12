# Central Package Management Conversion Report

## Overview

Scope: `TabloSuiteMappe.slnx`

Converted the solution from per-project NuGet package versions to NuGet Central Package Management using `Directory.Packages.props`.

## Projects Converted

- `SkoulouApp.Core/SkoulouApp.Core.csproj`
- `SkoulouApp.WebApp/SkoulouApp.WebApp.csproj`
- `SkoulouApp.Tests/SkoulouApp.Tests.csproj`

## Packages Centralized

| Package | Version |
|---------|---------|
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.10 |
| Microsoft.EntityFrameworkCore | 10.0.10 |
| Microsoft.EntityFrameworkCore.Design | 10.0.10 |
| Microsoft.EntityFrameworkCore.InMemory | 10.0.10 |
| Microsoft.EntityFrameworkCore.SqlServer | 10.0.10 |
| Microsoft.EntityFrameworkCore.Tools | 10.0.10 |
| Microsoft.NET.Test.Sdk | 18.8.1 |
| xunit | 2.9.3 |
| xunit.runner.visualstudio | 3.1.5 |

## Version Conflict Resolution

No version conflicts were detected. No `VersionOverride` entries were required.

## Baseline vs Result

Baseline package list was captured in `baseline-packages.json` before conversion. Post-conversion package list was captured in `after-cpm-packages.json`.

The conversion preserved the package versions already validated after the .NET 10 upgrade. No package upgrades or downgrades were introduced by the CPM conversion itself.

## Validation Results

| Check | Result |
|-------|--------|
| Baseline build before conversion | Passed |
| `Directory.Packages.props` created with `ManagePackageVersionsCentrally=true` | Passed |
| Project `PackageReference` entries no longer contain `Version` attributes | Passed |
| Restore/build after conversion | Passed |
| Test suite after conversion | 106 passed, 0 failed, 0 skipped |
| Outdated package audit | No updates reported |
| Vulnerable package audit | No vulnerable packages reported |

## Commands Run

```powershell
dotnet build "C:\ETSE\NETAPPS\TabloSuiteData\TabloSuiteProjekt\TabloSuiteMappe.slnx"
dotnet list "C:\ETSE\NETAPPS\TabloSuiteData\TabloSuiteProjekt\TabloSuiteMappe.slnx" package --format json > "C:\ETSE\NETAPPS\TabloSuiteData\TabloSuiteProjekt\baseline-packages.json"
dotnet build "C:\ETSE\NETAPPS\TabloSuiteData\TabloSuiteProjekt\TabloSuiteMappe.slnx"
dotnet list "C:\ETSE\NETAPPS\TabloSuiteData\TabloSuiteProjekt\TabloSuiteMappe.slnx" package --format json > "C:\ETSE\NETAPPS\TabloSuiteData\TabloSuiteProjekt\after-cpm-packages.json"
dotnet test "C:\ETSE\NETAPPS\TabloSuiteData\TabloSuiteProjekt\SkoulouApp.Tests\SkoulouApp.Tests.csproj"
dotnet list "C:\ETSE\NETAPPS\TabloSuiteData\TabloSuiteProjekt\TabloSuiteMappe.slnx" package --outdated
dotnet list "C:\ETSE\NETAPPS\TabloSuiteData\TabloSuiteProjekt\TabloSuiteMappe.slnx" package --vulnerable --include-transitive
```

## Risk Assessment

Low risk. The conversion changed package version placement only; package versions remained the same as the already validated .NET 10 state.

## Follow-Up Items

- Keep package additions centralized in `Directory.Packages.props`.
- When adding a new package, add `<PackageVersion>` centrally and use versionless `<PackageReference>` entries in projects.
- Re-run build and tests after future package version changes.
