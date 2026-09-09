# HALLO-APP Projektstand

Stand: aktuelle Prüfung im Branch `upgrade-dotnet-10`

## Kurzfazit

Das Projekt ist in den zentralen Rollen- und Schulkontexten mehrschulfähig aufgestellt. Die wichtigsten Blazor-Seiten führen den Schulkontext sichtbar, und die zentralen Services kapseln die fachlichen Regeln überwiegend serverseitig. Im Rahmen dieser Prüfung wurde eine zusätzliche serverseitige Absicherung für Abrechnungsreaktionen durch Schulleiter ergänzt.

## Geprüfter Umfang

### Projekte

- `HalloApp.WebApp` - Blazor Web App mit rollenbasierten Seiten.
- `HalloApp.Core` - Domänenmodelle, Services, Geschäftslogik und EF-Core-Kontext.
- `HalloApp.Tests` - xUnit-Regressionstests für Services und zentrale Abläufe.

### Zentrale Seiten

- `Admin.razor` / `Admin.razor.cs`
  - SuperAdmin-Übersicht über Schulen, Nutzer, Inhalte und Abos.
  - Mehrschul-Zuweisung für Schulleiter.
  - Schul- und Klassenzuweisung für Schüler und Lehrer.
- `SchulLeiter.razor` / `SchulLeiter.razor.cs`
  - Schulleiter verwalten eigene Schulen, Nutzer und Klassen.
  - Schüler erhalten keine automatische Abo-Zuweisung.
- `Finanzen.razor` / `Finanzen.razor.cs`
  - Schulleiter und SuperAdmin arbeiten in einem expliziten Schulkontext.
  - Bei mehreren Schulen wird eine Schule aktiv ausgewählt.
- `Abrechnung.razor` / `Abrechnung.razor.cs`
  - Abrechnungen sind je Schule auswählbar.
  - Schulleiter können nur im Kontext ihrer sichtbaren Schulen reagieren.
- `Provisionen.razor` / `Provisionen.razor.cs`
  - Provisionen werden je Schule bzw. je eigener Rolle angezeigt.
- `Teacher.razor` / `Teacher.razor.cs`
  - Lehreransicht unterstützt Schulfilter bei mehreren Schulzuweisungen.
- `AdminBroadcast.razor` / `AdminBroadcast.razor.cs`
  - Broadcast kann global oder auf eine Schule eingeschränkt erfolgen.

## Mehrschul-Konzept

### Rollen

- `SuperAdmin`
  - Sieht globale Plattformdaten.
  - Kann Schulen, Nutzer, Klassen, Inhalte, Preise und Abos global verwalten.
  - Kann Schulleiter mehreren Schulen zuweisen.
- `SchulLeiter`
  - Verwaltet nur zugewiesene Schulen.
  - Kann bei mehreren Schulen aktiv zwischen Schulen wählen.
  - Darf keine fremden Schulen in Finanzen und Abrechnung bearbeiten.
- `Koordinator`
  - Wird wie ein schulgebundener Verwaltungsnutzer behandelt.
- `Lehrer`
  - Arbeitet mit zugewiesenen Schulen und Klassen.
  - Sieht Schüler- und Fortschrittsdaten nur im zugewiesenen Kontext.
- `Schüler`
  - Hat eine aktuelle Schule und optional eine aktuelle Klasse.
  - Zugriff auf Lern- und Lehrmaterial folgt der aktuellen bzw. historischen Klassenzugehörigkeit.

### Fachliche Regeln

- Schüler erhalten bei Anlage keine automatische Klasse `A1.1`.
- Schüler erhalten bei Anlage kein automatisches HALLO-APP-Abo.
- Abos und Zahlungen sind an Schule, Klasse und Durchlauf gebunden.
- Abgeschlossene Finanzzyklen sind für Abo-Änderungen gesperrt.
- Finanzdaten sind schulbezogen und werden serverseitig autorisiert.
- Abrechnungsreaktionen durch Schulleiter werden jetzt ebenfalls serverseitig gegen die Schulzuweisung geprüft.

## Umgesetzte Verbesserung in dieser Prüfung

### Abrechnung: serverseitige Schulleiter-Autorisierung

Vorher prüfte die UI die sichtbaren Schulen, die Service-Methode `SchulLeiterStatusSetzenAsync` erhielt jedoch nur die Perioden-ID. Dadurch war die fachliche Absicherung im Service nicht vollständig.

Jetzt kann die Methode zusätzlich den aufrufenden Schulleiter erhalten und prüft dann:

- Die Abrechnungsperiode existiert.
- Die Periode ist noch nicht bestätigt.
- Der Schulleiter ist der Schule der Periode mit `CanManageStudents` zugeordnet.
- Erst danach wird akzeptiert oder abgelehnt.

Betroffene Dateien:

- `HalloApp.Core/Services/AbrechnungService.cs`
- `HalloApp.WebApp/Components/Pages/Abrechnung.razor.cs`
- `HalloApp.Tests/Services/AbrechnungServiceTests.cs`

### UI-Komponenten: moderne Dropdowns und Datumsfelder

Die sichtbaren Browser-Standardcontrols aus den Finanz- und Admin-Hilfsseiten wurden in wiederverwendbare Blazor-Komponenten überführt.

Neu:

- `HalloApp.WebApp/Components/Shared/ModernDropdown.razor`
- `HalloApp.WebApp/Components/Shared/ModernDropdown.razor.cs`
- `HalloApp.WebApp/Components/Shared/ModernDropdownOption.cs`

Modernisiert:

- `Finanzen.razor`
  - Schulwahl
  - Filter `Du` / `Au`
  - Typ- und Sortierauswahl
  - Kategorie- und Niveauauswahl
  - Zahlungsmodal inklusive Schüler, Zahlungsart und Datum
  - Durchlauf-Start und Durchlauf-Abschlussdatum
- `AdminBroadcast.razor`
  - Empfänger- und Schul-Dropdown
- `ExceptionLogs.razor`
  - Zeitraumfilter und Schulfilter
- `Admin.razor`
  - Rolle, Schülerklasse, CEFR-Gruppe, Lektionskurs, Medienlektion und Medientyp
- `SchulLeiter.razor`
  - Durchlauf- und Schülerfilter
  - Schultyp, Nutzerrolle, Schul-/Klassenwahl
  - Klassenformular inklusive Schule, Klassenstufe und CEFR-Gruppe
  - Versetzungsziel, Durchlaufdaten und Zahlungsfelder
- `Teacher.razor`
  - Quiz-Klassenauswahl
- `TeacherLessons.razor`
  - Lektions-Niveauauswahl
- `TeacherExams.razor`
  - Prüfungsniveau und Prüfungstyp

Bereits modern bzw. nicht geändert:

- `Abrechnung.razor` nutzt bereits `TailwindDateTimePicker` und Schul-Cards.
- `Provisionen.razor` nutzt bereits Schul-Cards statt nativer Dropdowns.
- Dynamische Frage-Typ-Auswahlen in Quiz-/Prüfungseditoren bleiben vorerst native Controls, weil sie tief in verschachtelte Editorlisten eingebunden sind.

### Azure SQL Database

- Die aktive Datenbankkonfiguration wurde auf `Database:ConnectionStringName = AzureDefaultConnection` umgestellt.
- Der echte Azure-SQL-ConnectionString bleibt in User Secrets bzw. Azure App Settings und wird nicht in `appsettings.json` gespeichert.
- `dotnet ef database update` wurde gegen `AzureDefaultConnection` ausgeführt.
- Ergebnis: Die AzureDB war bereits auf dem aktuellen EF-Core-Migrationsstand; es mussten keine neuen Migrationen angewendet werden.

## Validierung

Erfolgreich ausgeführt:

- Build der Lösung: erfolgreich.
- `AbrechnungServiceTests`: 10/10 erfolgreich.
- `ProvisionsServiceTests`: 11/11 erfolgreich.
- `DashboardServiceTests`: 30/30 erfolgreich.
- Build nach UI-Komponentenmodernisierung: erfolgreich.
- Build nach zweiter UI-Migrationsrunde: erfolgreich.
- Build nach Umschaltung auf `AzureDefaultConnection`: erfolgreich.
- AzureDB-Migrationsupdate: erfolgreich, keine ausstehenden Migrationen.

## Aktueller Qualitätsstand

### Stark

- Rollenbasierte Blazor-Seiten sind klar getrennt.
- Zentrale Datenzugriffe laufen über Services.
- Mehrschul-Kontext ist in Admin, Schulleiter, Finanzen, Abrechnung, Provisionen und Lehrerbereich sichtbar.
- Finanzservice hat explizite serverseitige Autorisierung für schulbezogene Mutationen.
- Dashboard-Service enthält Regressionen für Schul-/Klassen-/Benutzerzuweisungen.
- Abrechnung hat jetzt zusätzliche serverseitige Absicherung für Schulleiterreaktionen.
- Wiederverwendbare UI-Komponenten reduzieren sichtbare Browser-Standardcontrols in priorisierten Bereichen.

### Beobachtungen

- Einige Services dokumentieren Sichtbarkeit als UI-vorgelagert. Für kritische Mutationen sollte langfristig bevorzugt immer ein aufrufender Benutzer bzw. Rollen-/Schulkontext an die Serviceebene übergeben werden.
- SuperAdmin-Funktionen sind bewusst global. Dort ist keine zusätzliche Schulbegrenzung nötig, aber die UI sollte weiterhin klar anzeigen, ob Daten global oder schulbezogen sind.
- Broadcasts sind SuperAdmin-only und können global senden. Das ist fachlich plausibel, sollte produktiv mit Vorsicht genutzt werden.

## Empfohlene nächste Verbesserungen

1. Serverseitige Autorisierung weiter vereinheitlichen.
   - Kritische Mutationen sollten konsequent den aufrufenden Benutzer und dessen Rolle erhalten.
   - Muster aus `FinanzService` kann als Vorlage dienen.

2. Testabdeckung für rollenübergreifende Zugriffsszenarien ausbauen.
   - Fremde Schule bei Abrechnung ist jetzt abgedeckt.
   - Ähnliche Tests können für weitere Mutationen ergänzt werden.

3. UI-Hinweise weiter schärfen.
   - Seiten mit aktiver Schule sollten durchgehend denselben Hinweistext verwenden: alle Daten beziehen sich auf die ausgewählte Schule.

4. UI-Komponenten schrittweise weiter migrieren.
	  - Die meisten sicheren Admin-, Schulleiter- und Teacher-Auswahlen sind migriert.
   - Übrig sind vor allem dynamische Frage-Typ-Auswahlen in verschachtelten Quiz-/Exam-Editoren.

5. Optional technische Schulden prüfen.
   - Kodierungsartefakte in einzelnen französischen Labels können bei Gelegenheit bereinigt werden.
   - Lange Services könnten später in fachliche Teilservices aufgeteilt werden, wenn sich der Umfang weiter erhöht.

## Standentscheidung

Der aktuelle Stand ist buildfähig und die geprüften zentralen Tests laufen erfolgreich. Die Mehrschul-Architektur ist für die wichtigsten Rollen nutzbar. Weitere Verbesserungen sind eher Härtung und Vereinheitlichung, nicht zwingende Blocker.