# SKOULOUAPP – Projekt- und Entwicklerübersicht

## Zweck

SKOULOUAPP ist eine schulbezogene Verwaltungs- und Lernplattform für Bildungseinrichtungen. Schulleitungen und Koordinatoren verwalten Schulen, Klassen, Lehrkräfte, Schüler, Kurszyklen und Zahlungen. Lehrkräfte erstellen Unterrichtsmaterialien und Prüfungen; Schüler bearbeiten freigegebene Inhalte und verfolgen ihren Lernstand. Die Plattform verwaltet außerdem Abonnements, Schulfinanzen, Provisionen und SKOULOUAPP-Facturation.

Die Anwendung nutzt .NET 10 und Blazor Interactive Server. Sichtbare Anwendungstexte sind Französisch; im Code kommen überwiegend deutsche fachliche Namen und historisch gewachsene englische Bezeichner vor. Verbindliche Projektvorgaben stehen in [`.github/copilot-instructions.md`](../.github/copilot-instructions.md); dieses Dokument beschreibt dagegen die aktuelle Implementierung.

## Solution-Struktur

| Projekt | Verantwortung |
|---|---|
| `SKOULOUAPP.WebApp` | Blazor-Oberfläche, Startup/DI, EF-Core-DbContext, Migrationen, Identity, API-/PDF-Endpunkte, Medien, E-Mail und statische Inhalte. |
| `SKOULOUAPP.Core` | Fachmodelle, Form-/View-Modelle, Schnittstellen, Repositories und Geschäftsservices. |
| `SKOULOUAPP.Tests` | xUnit-Tests für Services und WebApp-Funktionen mit EF Core InMemory und Testdoubles. |

Alle Projekte zielen auf `net10.0`, Nullable Reference Types und implizite Usings sind aktiviert.

## Laufzeit und Komposition

`SkoulouApp.WebApp/Program.cs` ist der Einstiegspunkt.

- Blazor wird mit `AddRazorComponents().AddInteractiveServerComponents()` betrieben.
- EF Core nutzt SQL Server über `AddDbContextFactory<ApplicationDbContext>`; datenbanknutzende Dienste erhalten über die Factory einen eigenen Context statt eines im Blazor-Circuit geteilten Contexts.
- `ISkoulouAppDbContext` abstrahiert den DbContext für die Core-Services und Tests.
- ASP.NET Core Identity verwendet `AppUser`, `Role` und GUID-Schlüssel.
- Serilog protokolliert auf Konsole und über `DatabaseExceptionLogSink` zusätzlich Exceptions in der Datenbank.
- Beim Start ruft `Program.cs` `ApplicationDbInitializer.SeedAsync(...)` auf. Nur wenn `Database:ApplyMigrationsOnStartup=true` gesetzt ist, werden zuvor Migrationen angewendet. Der Initializer legt Berechtigungen/Rollen und den initialen SuperAdmin an bzw. gleicht sie ab; er importiert keinen Kurskatalog.

### Datenbankverbindung

Die Verbindung wird über `Database:ConnectionStringName` ausgewählt:

- `DefaultConnection`: lokale Entwicklung; in `appsettings.Development.json` für SQL Server `localhost` konfiguriert.
- `AzureDefaultConnection`: Standardname in `appsettings.json`; produktive Verbindungsdaten gehören in User Secrets/Umgebungsvariablen.

Die Azure-Produktionsverbindung gehört nicht in versionierte Dateien. `Database:ApplyMigrationsOnStartup` steht standardmäßig auf `false`; Migrationen müssen bei Schemaänderungen bewusst für die richtige Umgebung ausgeführt werden.

### Lokaler Einstieg

1. .NET-10-SDK und SQL Server bereitstellen. Prüfen, dass der in `SKOULOUAPP.WebApp/appsettings.Development.json` konfigurierte `DefaultConnection` erreichbar ist; Zugangsdaten für andere Umgebungen über User Secrets oder Umgebungsvariablen setzen.
2. `dotnet restore SKOULOUAPPMAPPE.slnx` und `dotnet build SKOULOUAPPMAPPE.slnx` im Repository-Stamm ausführen.
3. Ausstehende Migrationen prüfen und gezielt auf die lokale Development-Datenbank anwenden, z. B. `dotnet ef migrations list --project SKOULOUAPP.WebApp --startup-project SKOULOUAPP.WebApp --context ApplicationDbContext -- --environment Development` und danach `dotnet ef database update --project SKOULOUAPP.WebApp --startup-project SKOULOUAPP.WebApp --context ApplicationDbContext -- --environment Development`. Vor jeder Anwendung die Zielverbindung prüfen; nie eine Datenbank zurücksetzen.
4. `dotnet run --project SKOULOUAPP.WebApp --launch-profile https` startet die App lokal laut `launchSettings.json` unter `https://localhost:7064`. Die Anmeldedaten des Seed-Admins stehen in `Data/Seed/ApplicationDbInitializer.cs`; nach dem ersten Start Zugangsdaten sichern/ändern und nicht in Dokumentationen übernehmen.
5. `dotnet test SKOULOUAPPMAPPE.slnx` führt die automatisierten Tests aus. Browserabläufe werden zusätzlich manuell geprüft.

## Rollen und Zugriffsmodell

| Rolle | Hauptbereich |
|---|---|
| `Schüler` | Freigeschaltete Lektionen, Übungen, Lehrerunterlagen, Prüfungen, Quizze und eigene Fortschritte. |
| `Lehrer` | Pädagogischer Bereich: Schülerfortschritt, Lektionen, Unterrichtsmaterial, Prüfungen und Quizze. |
| `SchulLeiter` | Lehrerrechte plus Schulverwaltung, Klassen-Governance, Finanzen und Abrechnung der eigenen Schule. |
| `Koordinator` | Schulbezogene Verwaltung; Finanz-Tabs werden von der Schulleitung pro Schulzuordnung freigegeben. |
| `ProduktOwner` | Zugewiesene Schulen und berechtigte Plattform-/Facturation-Funktionen. |
| `SuperAdmin` | Globale Verwaltung, Schulen, Benutzer, Preis-/Kommissionskonfiguration, Broadcast-E-Mails, Fehlerprotokolle und schulübergreifende Finanzen. |

`Routes.razor` setzt `AuthorizeRouteView` ein. Einzelne Seiten ergänzen dies mit Rollen oder Policies (`RequireSuperAdmin`, `RequireSchulLeiter`, `RequireTeacher`, `RequireStudent`). Die Navigation in `Components/Layout/NavMenu.razor` blendet Einträge rollenbasiert ein; sie ersetzt keine serverseitige Prüfung. `UserSchool` bindet Nutzer an einzelne Schulen und hält die Finanzrechte `CanAccessFinances`, `CanViewFinanceDashboard`, `CanCreateFinanceEntries`, `CanManageCyclePayments` und `CanAccessBilling`. Beim Koordinator müssen Schule und jeweiliges Tab-/Aktionsrecht geprüft werden, auch bei direktem Aufruf von `/abrechnung`.

## Blazor-Oberfläche

### Konventionen

- Die größeren Seiten verwenden `.razor` plus `.razor.cs`-Code-behind; einzelne einfache Komponenten sind reine Razor-Dateien.
- Gemeinsam genutzte UI-Bausteine liegen unter `Components/Shared`, u. a. `AsyncActionButton`, `ProgressRing`, `ModalDialog`, `Toast`, `CollapsibleCard`, `RichTextEditor` und Listen-/Filterhilfen.
- Datenladende Seiten sollen von `CancelableComponentBase` erben und `ComponentToken` an Service-/EF-Aufrufe weiterreichen. Das verhindert Fehler beim Navigieren während noch laufender DB-Abfragen.
- `App.razor` bindet Styles, Quill und eigene JavaScript-Dateien ein; `wwwroot` enthält zudem Manifest und Service Worker. PDF-Dateien werden serverseitig erstellt.

### Seitenbereiche

- Öffentlich/Auth: Home, Kontakt, Login, Registrierung, Passwort-Reset, Profil.
- Schüler: Dashboard, DB-Lektionen, Übungen, Lehrerunterlagen, Prüfungen, Quizze.
- Lehrkräfte: Schüler-/Kursverwaltung, Lehrerlektionen, Prüfungen und PDF-Ausgaben.
- Schulleitung/Koordinatoren: Schulverwaltung, Klassen, Schülerfreigabe und Lehrkräfte; unter „Comptabilité et paiements“ befinden sich die je Schule freigegebenen Finanz-Tabs einschließlich „Facturation SKOULOUAPP“.
- SuperAdmin/ProduktOwner: globale bzw. zugewiesene Verwaltungs- und Finanzansichten, Facturation und Exception-Logs entsprechend der Rolle.

## Fachmodell und Persistenz

`ApplicationDbContext` kombiniert ASP.NET-Core-Identity mit den Anwendungsdaten. Migrationen liegen unter `SKOULOUAPP.WebApp/Migrations`; maßgeblich für die eigene Datenbank ist `__EFMigrationsHistory`. Zuletzt wurde das schulbezogene Koordinatorrecht `CanAccessBilling` in `20261006062702_CoordinatorBillingAccess` ergänzt (Standardwert für bestehende Zuordnungen: `false`). Modelländerungen erfordern eine neue EF-Migration; ein erfolgreicher Build allein ändert das SQL-Schema nicht.

### Schule, Benutzer und Klassen

- `School`: Mandant mit Standort, Beschreibung, Aktivstatus und optionalem schulspezifischem Abo-Preis sowie optionalen Kommissions-Prozentwerten.
- `AppUser`, `StudentProfile`, `UserSchool`: Identity, Schülerprofil und Schulzuordnung.
- `Klasse`: schulbezogene Klasse; Klassenzyklen und Teilnehmerzuweisungen werden separat geführt.
- `LevelEnrollment`: klassen-/zyklusbezogene Abo-Freischaltung mit Zahlungs- und Abrechnungszustand. Der technische Abo-Typ ist `AboTyp.SkoulouApp`.

Die Datenbank erzwingt wichtige Eindeutigkeiten, etwa School-Name, Benutzer-E-Mail, `(SchoolId, Klasse.Code)`, Lernfortschritt pro Schüler/Lektion und PDF-Exportfreigaben. Die Beziehung `Klasse -> School` nutzt bewusst `Restrict`, um SQL-Server-Kaskadenpfade zu vermeiden.

### Lerninhalte

- `Lesson`, `LessonMedia`, `Exercise`, `ExerciseOption`, `ExerciseAttempt`, `LessonProgress` bilden den systemweiten Lernkatalog und den Schülerfortschritt.
- `TeacherLesson`, `TeacherLessonPage` und `TeacherLessonPdfExportPermission` bilden von Lehrkräften geschriebene, paginierte Unterrichtsmaterialien ab.
- Inhalte sind HTML/Rich Text; `HtmlSanitizer` filtert unsichere Inhalte. Bilder dürfen nur im freigegebenen Lektion-/Übungserstellungsbereich über die gespeicherte Medienbibliothek eingefügt werden; der Lektionsmedienpfad verwendet Azure-Blob-Abstraktionen (in Development lokal konfiguriert).

### Beurteilungen

Quiz und Exam sind getrennte Modelle, verwenden jedoch dieselbe Struktur:

- Aufgabenblock: `QuizExercise` bzw. `ExamExercise`.
- Einzelaufgabe: `QuizQuestion` bzw. `ExamQuestion`.
- Antwortoptionen und Versuche: eigene Tabellen je Subsystem.

Unterstützt werden u. a. Single Choice, Multiple Choice, Freitext, Lückentext sowie Audio-Single-/Multiple-Choice. Mehrfachauswahl kann Teilpunkte oder All-or-Nothing verwenden. Audio wird derzeit über den Browser-Provider (`Web Speech API`) bereitgestellt.

### Abonnement und Finanzen

- `AboService`, `AbrechnungService` und `ProvisionsService` steuern das eine SKOULOUAPP-Abo, Zahlungs-/Abrechnungsperioden und prozentuale Kommissionsverteilung.
- `FinanzService` verwaltet Einnahmen/Ausgaben, globale oder schulbezogene Kategorien, Kursdurchläufe und Ratenzahlungen.
- Klassenzyklen (`KursNiveauDurchlauf`) bilden Start und Abschluss ab; Schulgebühren können sofort oder in Raten bezahlt werden. Die offene Plattformforderung zählt bezahlte, noch nicht abgerechnete Abos. Für den Schulsaldo wird nur der Plattformanteil abgezogen; Direktions- und Lehreranteile verbleiben bei der Schule.

## Zentrale Services

| Service | Verantwortung |
|---|---|
| `DashboardService` | Administrative, Lehrer-, Schulleiter- und Schülerprojekte; DB-Katalog für Home und `/lektionen`. |
| `LearningService` | Freigeschaltete Lektionen, Übungsabgaben, automatische Bewertung und Fortschritt. |
| `QuizService` | Quiz erstellen, zuweisen, durchführen und bewerten; Klassenzuweisung und Abo-Berechtigung beachten. |
| `ExamService` | Prüfungen, Versuche, Bewertung und Ergebnisdarstellung. |
| `TeacherLessonService` | Lehrerlektionen, Seiten, Berechtigungen und PDF-Export. |
| `AboService` | SKOULOUAPP-Abo, Schulpreis, Freischaltung und Zahlstatus. |
| `AbrechnungService` / `ProvisionsService` | Abrechnung und Kommissionsanteile. |
| `FinanzService` | Buchungen, Kategorien, Kursdurchläufe und Ratenzahlungen. |
| `AuthService` | Registrierung, Schule, Rollen und Profilzuordnung. |
| `HomePageService` | Startseitennahe Daten/Logik. |

Services verwenden `ISkoulouAppDbContext`, EF-Core-Async-Methoden und optional `CancellationToken`. Leseabfragen verwenden häufig `AsNoTracking()` und View-Model-Projektionen. Bei neuen Blazor-Datenzugriffen Contexts per `IDbContextFactory` statt parallel aus einem gemeinsam genutzten Circuit-Context beziehen.

## Katalog und Seeding

`ApplicationDbInitializer` legt nur Rollen/Berechtigungen und den initialen SuperAdmin an bzw. aktualisiert sie. Schulen und andere Benutzer werden über die Anwendung erfasst. Es gibt im Initializer keinen automatischen CSV-Katalogimport und kein automatisches Zurücksetzen von Lernfortschritten. Änderungen am Lernkatalog sind getrennt von Datenbankmigrationen zu behandeln; Text-to-Speech verwendet Text statt importierter Audiodateipfade.

## Tests

`SKOULOUAPP.Tests` ist eine xUnit-Testsuite. `TestDbContext` implementiert dieselbe `ISkoulouAppDbContext`-Schnittstelle mit EF Core InMemory; zusätzliche Fakes stehen für Medien, Unit of Work und UserManager bereit. InMemory-Tests ersetzen keine SQL-Migrationsprüfung.

Wichtige Testbereiche sind:

- Abo, Abrechnung und Provisionen.
- Finanzverwaltung und Ratenzahlungen.
- Dashboard- und Schul-/Klassen-Governance.
- Lektionen, Übungsbewertung, Quiz, Exam und Mehrfachauswahl.
- Rich-Text-Sanitizing und Medien.
- Lehrerlektionen/PDF-Berechtigungen.
- Ein durchgehender Ablauf von Schule und Schulleiter über Schülerfreigabe/Abo bis zu Lernfortschritt und Abrechnung (`GesamtAblaufTests`).

Die Tests prüfen vor allem fachliche Service-Logik; UI- und Browser-Interaktionen werden nicht als End-to-End-Browser-Tests abgedeckt.

## Praktische Einstiegspunkte für neue TODOs

| Thema | Zuerst ansehen |
|---|---|
| Seite oder UI-Änderung | Passende Dateien unter `SKOULOUAPP.WebApp/Components/Pages`, anschließend zugehörige `.razor.cs` und Shared Components. |
| Fachregel | Zuständigen Service unter `SKOULOUAPP.Core/Services` und dessen Tests. |
| Neue/angepasste Daten | Datenmodell unter `SKOULOUAPP.Core/DataModels`, danach `ApplicationDbContext`, Migration und Tests. |
| Rechte und Rollen | `Program.cs`, `NavMenu.razor`, Seitenattribut `[Authorize]`, plus die Service-Prüfung. |
| Katalog-/Übungsinhalte | `SKOULOUAPP.Core/DataModels/Lessons`, `LearningService`, `TeacherLessonService` und die verantwortlichen Import-/Medienpfade. |
| Finanz-/Abo-Thema | `AboService`, `AbrechnungService`, `ProvisionsService`, `FinanzService` und die dazugehörigen Datenmodelle. |
| Fehlerprotokollierung | `DatabaseExceptionLogSink`, `ExceptionLog`, `ExceptionLogs.razor`. |

## Aktuelle Leitlinien

- Keine horizontalen Scrollbars; Tablet/Mobil responsiv halten.
- Endanwendertexte auf Französisch, technische Interna und Berechtigungsdetails nicht in die Endnutzeroberfläche übernehmen.
- Core-Datenmodelle und Properties deutsch benennen; IDs folgen der vereinbarten Benennung (`Id`, sonst zusammengesetzte deutsche Namen mit `ID`).
- Neue Blazor-Logik bevorzugt im Code-behind, nicht in `@code`-Blöcken.
- Vor Service-/Datenmodelländerungen auf abhängige UI-Flüsse, Rechte, Schultrennung und Finanzkonsistenz prüfen; relevante Tests und Lösungsbuild nach Änderungen ausführen.
