# .NET Version Upgrade Plan

## Overview

**Target**: Upgrade der HALLOAPPMAPPE-Solution von .NET 9 auf .NET 10 LTS und Aktualisierung der NuGet-Pakete.
**Scope**: 3 SDK-style Projekte mit Blazor-WebApp, Core-Bibliothek und Testprojekt; Assessment meldet 7 Paket-Upgrades und API-Kompatibilitätshinweise in der WebApp.

### Selected Strategy
**All-At-Once** — All projects upgraded simultaneously in a single operation.
**Rationale**: 3 projects, all on .NET 9, SDK-style, clear dependency structure through HalloApp.Core.

## Tasks

### 01-prerequisites: Verify .NET 10 toolchain and workspace readiness

Verify that the .NET 10 SDK is available to the build, the solution can be restored, and repository state is ready before changing project files. This covers `HALLOAPPMAPPE.slnx` and all three projects so the later atomic upgrade has a known baseline.

**Done when**: .NET 10 SDK compatibility is confirmed, restore prerequisites are known, and no blocking workspace or global.json issue prevents upgrading to `net10.0`.

---

### 02-solution-upgrade: Upgrade projects and NuGet packages to .NET 10

Upgrade `HalloApp.Core`, `HalloApp.WebApp`, and `HalloApp.Tests` together from `net9.0` to `net10.0`. Update Microsoft EF Core, ASP.NET Core Identity EF, EF provider/tooling packages, and test packages where supported. Resolve package incompatibility and the API compatibility findings in the Blazor WebApp inline rather than deferring stubs.

The assessment highlights package updates for EF Core 9.x packages to 10.x and flags `xunit` as outdated in the test project. It also reports API compatibility findings around configuration/identity extension methods, `TimeSpan.FromSeconds`, `System.Uri`, and exception handling behavior in the WebApp; these should be researched and fixed only where code or build output requires changes.

**Done when**: all project files target `net10.0`, relevant NuGet packages are updated to supported versions, restore succeeds, and the solution builds without errors or warnings caused by the upgrade.

---

### 03-final-validation: Validate build, tests, and deferred cleanup recommendations

Run full solution validation after the atomic upgrade. This includes building the solution, running available tests, and documenting any non-blocking follow-up such as optionally introducing Central Package Management after all projects are stable on the single `net10.0` target.

**Done when**: the solution builds successfully, affected tests pass or any pre-existing failures are clearly documented, and deferred recommendations are captured in task progress details.
