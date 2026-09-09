# 01-prerequisites: Verify .NET 10 toolchain and workspace readiness

Verify that the .NET 10 SDK is available to the build, the solution can be restored, and repository state is ready before changing project files. This covers `HALLOAPPMAPPE.slnx` and all three projects so the later atomic upgrade has a known baseline.

**Done when**: .NET 10 SDK compatibility is confirmed, restore prerequisites are known, and no blocking workspace or global.json issue prevents upgrading to `net10.0`.

## Research Findings

### Projects Affected
- `HALLOAPPMAPPE.slnx` — Prerequisite-Check betrifft die gesamte Solution.
- `HalloApp.Core/HalloApp.Core.csproj`, `HalloApp.WebApp/HalloApp.WebApp.csproj`, `HalloApp.Tests/HalloApp.Tests.csproj` — spätere Upgrade-Ziele; in diesem Task keine Projektdateiänderungen.

### Findings
- Installierte SDKs geprüft: `10.0.302` ist verfügbar; `9.0.316` bleibt ebenfalls installiert.
- `global.json` wurde nicht gefunden; keine SDK-Pinning-Konfiguration blockiert `net10.0`.
- `dotnet restore HALLOAPPMAPPE.slnx` läuft erfolgreich vor dem Upgrade.

### Decisions Made
- Für diese SDK-style moderne Solution wird `dotnet build`/`dotnet restore` als Standard-Buildtool verwendet.
- Keine Decomposition nötig: der Task enthält nur einen klaren Prerequisite-Gate ohne Codeänderungen.
