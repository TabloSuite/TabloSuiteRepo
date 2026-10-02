# 03-final-validation: Validate build, tests, and deferred cleanup recommendations

Run full solution validation after the atomic upgrade. This includes building the solution, running available tests, and documenting any non-blocking follow-up such as optionally introducing Central Package Management after all projects are stable on the single `net10.0` target.

**Done when**: the solution builds successfully, affected tests pass or any pre-existing failures are clearly documented, and deferred recommendations are captured in task progress details.

## Research Findings

### Projects Affected
- `SKOULOUAPPMAPPE.slnx` — finaler Build- und Paketstatus-Gate.
- `SkoulouApp.Tests/SkoulouApp.Tests.csproj` — finales Paket-Audit meldete `xunit.runner.visualstudio` als stabil aktualisierbar; Update von `2.8.2` auf `3.1.5` wurde übernommen.

### Validation Performed
- `dotnet build SKOULOUAPPMAPPE.slnx` erfolgreich mit 0 Fehlern und 0 Warnungen.
- `dotnet test SkoulouApp.Tests/SkoulouApp.Tests.csproj` erfolgreich: 106 Tests bestanden, 0 fehlgeschlagen, 0 übersprungen.
- `dotnet list SKOULOUAPPMAPPE.slnx package --outdated` meldet keine verbleibenden Updates.
- `dotnet list SKOULOUAPPMAPPE.slnx package --vulnerable --include-transitive` meldet keine anfälligen Pakete.

### Deferred Recommendations
- Central Package Management (`Directory.Packages.props`) kann später separat eingeführt werden. Der Upgrade-Plan hat CPM bewusst zurückgestellt, damit das Framework-/Paketupgrade klein und überprüfbar bleibt.

### Decisions Made
- Finales stabiles Update für `xunit.runner.visualstudio` wurde im Validierungstask übernommen, weil es nach dem ersten erfolgreichen Upgrade-Gate als einziges verbleibendes Outdated-Paket gemeldet wurde.
