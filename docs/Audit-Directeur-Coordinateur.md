# Technische Prüfung: Espace directeur und Espace coordinateur

## Umfang und Methode

Geprüft wurden die gemeinsamen Blazor-Einstiegspunkte (`SchulLeiter.razor[.cs]`), Navigation, Finanzseite/Bericht, die Benutzer- und Klassenverwaltung im `DashboardService`, Finanzrechte und Mutationen im `FinanzService`, gemeinsam freigegebene Lehrinhalte im `TeacherLessonService` sowie Import- und Regressionstestpfade. Die Prüfung basiert auf Code und automatisierten Tests; kein Browsertest und keine Prüfung realer Schuldaten.

## Rollen und Datenfluss

- Beide Rollen nutzen `SchulLeiter.razor`. Koordinatoren können Lehrer und Schüler verwalten; Schulleiter zusätzlich Koordinatoren. Schulzuordnungen werden anhand von `UserSchool.CanManageStudents` für die Direktionsverwaltung ermittelt.
- Schüler haben eine ausgewählte Schule und optional eine aktuelle Klasse dieser Schule. Lehrer und Koordinatoren können mehrere Schulen haben. Beim Koordinator gelten die im Formular gewählten Finanzrechte einheitlich für die vom bearbeitenden Schulleiter ausgewählten Schulen; Zuordnungen anderer Leiter bleiben unberührt.
- `Finanzen.razor` wählt eine Schule aus. `FinanzService.GetFinanceAccessAsync` prüft pro Schule die Rollen- und Registerrechte; Buchungs- und Zyklusmutationen prüfen die jeweiligen Rechte nochmals serverseitig. Der Bericht prüft Dashboard-Zugriff für die ausgewählte Schule.
- Direktion und Koordination sehen mit ihrer Schule geteilte Lehrerlektionen und Prüfungen über `TeacherLessonService.GetSharedContentForSchoolLeaderAsync`.

## Bestätigte Befunde und Korrekturen

1. **Statusaktionen ohne Schul-/Rollenprüfung (behoben).** Beide Aktionen auf der Direktionsseite riefen `SetStudentBlockedStateAsync` ohne Managerkontext auf. `SetSchoolUserBlockedStateAsync` prüft jetzt Schulzuordnung und verwaltbare Zielrolle; beide UI-Pfade benutzen diesen Einstieg.
2. **Falsche Erfolgsmeldung beim Blockieren bezahlter Schüler (behoben).** Die bisherige Methode übersprang das Blockieren, die UI meldete dennoch Erfolg. Der Dienst gibt den tatsächlichen Erfolg zurück; die UI zeigt bei Ablehnung die Fehlermeldung.
3. **Klassenzuordnung beim Bearbeiten nicht auf Manager-Schule gefiltert (behoben).** `GetSchoolUserForEditAsync` lädt die aktuelle Klasse nur aus einem Profil der zugeordneten Schulen.
4. **Klassenauswahl beim ersten Öffnen/Rollenwechsel (zuvor behoben).** Klassen werden beim Öffnen und beim Wechsel zur Schülerrolle für die ausgewählte Schule neu geladen.

## Noch offene Risiken und Empfehlungen

- **Service-Autorisierung:** `DashboardService.SaveUserAsync` und `SaveSchoolKlasseAsync` sind bewusst auch für interne/Admin-Pfade aufrufbar und prüfen keinen authentifizierten Manager selbst. Web-Aufrufe müssen ausschließlich die autorisierten Wrapper verwenden; bei neuen Endpunkten diese Regel explizit testen. Gleiches gilt für unautorisierte Lese-Methoden mit frei übergebener Schul-ID.
- **Mehrfach-Schulbearbeitung (nachträglich behoben):** `SaveSchoolUserAsync` bewahrt jetzt sowohl bei Koordinatoren als auch bei Lehrern Zuordnungen zu Schulen außerhalb der Zuständigkeit des Bearbeiters. Beim Lehrer bleiben zudem fremde Klassen-, Fachzuordnungen und Schulberechtigungsflags erhalten; ein Regressionstest deckt die Abwahl einer eigenen Schule bei gleichzeitiger Fremdschulzuordnung ab.
- **Freigegebene Lehrinhalte:** `TeacherLessonService.GetSharedContentForSchoolLeaderAsync` filtert auf vorhandene `UserSchool`-Links, nicht zusätzlich auf `CanManageStudents`. Die Seitenroute ist rollenbeschränkt, doch bei anders gearteten Schulverknüpfungen derselben Rolle sollte der Dienst auch die Berechtigung prüfen.
- **Gesamtablauf-Test blockiert:** `GesamtAblaufTests.Schulstartfluss_RegistrierungFreigabeKlassenLehrerSchuelerAufgabenUndScores_FunktioniertAlsEinAblauf` scheitert reproduzierbar beim Speichern einer Finanzbuchung. Das Modell verwendet für `Buchungsnummer` eine SQL-berechnete Pflichtspalte (`ApplicationDbContext`), die der EF-InMemory-Provider nicht berechnet. Empfohlen: diesen Integrationstest mit einem geeigneten relationalen Testprovider für die verwendete SQL-Computed-Column ausführen bzw. testseitig einen realistischen Wert bereitstellen; die produktive Buchungsnummer nicht nur für den Testpfad umgestalten.
- **Validierung:** Build erfolgreich; 107 Dashboard-/Finanztests bestanden. Der Gesamtablauf-Test schlägt wie oben beschrieben fehl; die übrige Suite ohne diesen Test läuft erfolgreich. Browser-/Rolleninteraktion, PDF-Ausgabe, produktive Datenbank und reale Mehrfachschul-Daten wurden nicht manuell geprüft.
