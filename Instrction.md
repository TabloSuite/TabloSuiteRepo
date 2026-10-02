# SKOULOUAPPMAPPE – Übergabe für neuen Chat

## Projektkontext
- Stack: Blazor (.NET 10)
- Solution: `SKOULOUAPPMAPPE.slnx`
- Aktueller Branch: `upgrade-dotnet-10`

## Zuletzt behobene Abo-Regressionsfälle
1. **Admin sieht Abo nach Storno wieder**
   - Anzeige wurde so angepasst, dass auch deaktivierte (unbezahlte) Enrollments sichtbar bleiben.
2. **Schulleiter kann nach Deaktivierung wieder Abo festlegen**
   - Reaktivierungslogik in `AboService.AboZuweisenAsync` erweitert.
3. **Spezialfall behoben**: 
   - Ablauf: Schulleiter weist Klasse zu → Abo festgelegt → Admin storniert → Schulleiter entfernt Klasse → ordnet Klasse erneut zu → Abo konnte nicht mehr festgelegt werden.
   - Fix: erneute Festlegung für aktuelle Klasse erlaubt, wenn unbezahlt und nicht abgerechnet.

## Relevante Dateien
- `SkoulouApp.Core/Services/AboService.cs`
- `SkoulouApp.Core/Services/DashboardService.cs`
- `SkoulouApp.WebApp/Components/Pages/Admin.razor`
- `SkoulouApp.WebApp/Components/Pages/SchulLeiter.razor.cs`
- `SkoulouApp.WebApp/Components/Pages/SchulLeiter.razor`
- `SkoulouApp.Tests/Services/AboServiceTests.cs`

## Hinzugefügter Regressionstest
- `AboZuweisenAsync_NachDeaktivierungUndErneuterKlassenzuordnung_KannWiederAktiviertWerden`

## Validierung
- Build: erfolgreich
- Relevante Tests: erfolgreich

## Offene Empfehlung
- Falls erneut ein Edge Case auftritt, zuerst prüfen:
  - `IsUnlocked`
  - `IsPaid`
  - `AbgerechnetInPeriodeId`
  - `CurrentKlasseId`

Diese Datei dient als Startpunkt für den nächsten Chat.
