# HALLO-APP – technische Projektübersicht

## Zweck

HALLO-APP ist eine mandantenfähige Lernplattform für Deutschunterricht. Schulen, Lehrkräfte und Schüler arbeiten rollenbasiert mit Kursen/Klassen, Lektionen, Lehrerunterlagen, Übungen, Quizzen, Prüfungen, Abonnements, Abrechnung, Provisionen und Schulfinanzen.

Die Anwendung ist eine .NET-10-Blazor-Web-App mit interaktivem Server-Rendering. Die sichtbaren Texte sind überwiegend Französisch; der Projektcode und die fachlichen Datenmodelle verwenden überwiegend deutsche Namen.

## Solution-Struktur

| Projekt | Verantwortung |
|---|---|
| `HalloApp.WebApp` | Blazor-Oberfläche, Startup/DI, EF-Core-DbContext, Migrationen, Authentifizierung, Seed-Ablauf, JavaScript und statische Inhalte. |
| `HalloApp.Core` | Fachmodell, View-/Form-Modelle, Schnittstellen, Repositories, Services sowie eingebettete Vokabel-JSON-Ressourcen. |
| `HalloApp.Tests` | xUnit-Tests der Core-Services mit einem EF-Core-InMemory-DbContext und Testdoubles. |

Alle Projekte zielen auf `net10.0`, Nullable Reference Types und implizite Usings sind aktiviert.

## Laufzeit und Komposition

`HalloApp.WebApp/Program.cs` ist der Einstiegspunkt.

- Blazor wird mit `AddRazorComponents().AddInteractiveServerComponents()` betrieben.
- EF Core nutzt SQL Server über `AddDbContextFactory<ApplicationDbContext>`; pro Scope wird daraus ein `ApplicationDbContext` erstellt.
- `IHalloAppDbContext` abstrahiert den DbContext für die Core-Services und Tests.
- ASP.NET Core Identity verwendet `AppUser`, `Role` und GUID-Schlüssel.
- Serilog protokolliert auf Konsole und über `DatabaseExceptionLogSink` zusätzlich Exceptions in der Datenbank.
- Beim Start führt `ApplicationDbInitializer.SeedAsync(...)` Migrationen, Basisdaten-Seed und Rückfüllungen aus.

### Datenbankverbindung

Die Verbindung wird über `Database:ConnectionStringName` ausgewählt:

- `DefaultConnection`: lokale Entwicklung (normaler Standard).
- `AzureDefaultConnection`: Azure SQL Database, Wert liegt in User Secrets.

Die Azure-Produktionsverbindung gehört ausschließlich in User Secrets, nicht in versionierte Konfigurationsdateien.

## Rollen und Zugriffsmodell

| Rolle | Hauptbereich |
|---|---|
| `Schüler` | Freigeschaltete Lektionen, Übungen, Lehrerunterlagen, Prüfungen, Quizze und eigene Fortschritte. |
| `Lehrer` | Pädagogischer Bereich: Schülerfortschritt, Lektionen, Unterrichtsmaterial, Prüfungen und Quizze. |
| `SchulLeiter` | Lehrerrechte plus Schulverwaltung, Klassen-Governance, Finanzen und Abrechnung der eigenen Schule. |
| `SuperAdmin` | Globale Verwaltung, Schulen, Benutzer, Preis-/Kommissionskonfiguration, Broadcast-E-Mails, Fehlerprotokolle und schulübergreifende Finanzen. |

`Routes.razor` setzt `AuthorizeRouteView` ein. Einzelne Seiten ergänzen dies mit Rollen oder Policies (`RequireSuperAdmin`, `RequireSchulLeiter`, `RequireTeacher`, `RequireStudent`). Die Navigation in `Components/Layout/NavMenu.razor` blendet Einträge rollenbasiert ein.

## Blazor-Oberfläche

### Konventionen

- Jede Seite besteht aus `.razor` plus separater `.razor.cs`-Datei.
- Gemeinsam genutzte UI-Bausteine liegen unter `Components/Shared`, u. a. `AsyncActionButton`, `ProgressRing`, `ModalDialog`, `Toast`, `CollapsibleCard`, `RichTextEditor` und Listen-/Filterhilfen.
- Datenladende Seiten sollen von `CancelableComponentBase` erben und `ComponentToken` an Service-/EF-Aufrufe weiterreichen. Das verhindert Fehler beim Navigieren während noch laufender DB-Abfragen.
- `App.razor` bindet Tailwind, Bootstrap, Flatpickr, Quill und eigene JavaScript-Dateien ein. Ein Service Worker und Manifest bilden die PWA-Grundlage.

### Seitenbereiche

- Öffentlich/Auth: Home, Kontakt, Login, Registrierung, Passwort-Reset, Profil.
- Schüler: Dashboard, DB-Lektionen, Übungen, Lehrerunterlagen, Prüfungen, Quizze.
- Lehrkräfte: Schüler-/Kursverwaltung, Lehrerlektionen, Prüfungen und PDF-Ausgaben.
- Schulleitung: Schulverwaltung, Klassen, Schülerfreigabe, Lehrkräfte, Finanzen und Abrechnung.
- SuperAdmin: globale Verwaltung, E-Mail-Broadcast und Exception-Logs.

## Fachmodell und Persistenz

`ApplicationDbContext` kombiniert ASP.NET-Core-Identity mit den HALLO-APP-Tabellen. Die aktuelle Basismigration ist `20260729141230_InitialCreate`.

### Schule, Benutzer und Klassen

- `School`: Mandant mit Standort, Aktivstatus, Schultyp, individuellen HALLOBASIS-/HALLOPLUS-Preisen und PDF-Export-Freigabe.
- `SchulTyp`: `Sprachschule` oder `NormaleSchule`.
- `AppUser`, `StudentProfile`, `UserSchool`: Identity, Schülerprofil und Schulzuordnung.
- `Klasse`: Kurs/Klasse. Globale Systemniveaus einer Sprachschule besitzen keine `SchoolId` und sind unveränderlich (`IstSystemNiveau`). Schulbezogene Klassen gehören genau einer Schule.
- `LevelEnrollment`: Freischaltung und Zahlungszustand eines Schülers für eine Klasse.

Die Datenbank erzwingt wichtige Eindeutigkeiten, etwa School-Name, Benutzer-E-Mail, `(SchoolId, Klasse.Code)`, Lernfortschritt pro Schüler/Lektion und PDF-Exportfreigaben. Die Beziehung `Klasse -> School` nutzt bewusst `Restrict`, um SQL-Server-Kaskadenpfade zu vermeiden.

### Lerninhalte

- `Lesson`, `LessonMedia`, `Exercise`, `ExerciseOption`, `ExerciseAttempt`, `LessonProgress` bilden den systemweiten Lernkatalog und den Schülerfortschritt.
- `TeacherLesson`, `TeacherLessonPage` und `TeacherLessonPdfExportPermission` bilden von Lehrkräften geschriebene, paginierte Unterrichtsmaterialien ab.
- Inhalte sind HTML/Rich Text; `HtmlSanitizer` verhindert unsichere Inhalte. Die Bildvariante erlaubt gezielt Base64-Bilder (`data:image/...`) und HTTPS-Bilder.

### Beurteilungen

Quiz und Exam sind getrennte Modelle, verwenden jedoch dieselbe Struktur:

- Aufgabenblock: `QuizExercise` bzw. `ExamExercise`.
- Einzelaufgabe: `QuizQuestion` bzw. `ExamQuestion`.
- Antwortoptionen und Versuche: eigene Tabellen je Subsystem.

Unterstützt werden u. a. Single Choice, Multiple Choice, Freitext, Lückentext sowie Audio-Single-/Multiple-Choice. Mehrfachauswahl kann Teilpunkte oder All-or-Nothing verwenden. Audio wird derzeit über den Browser-Provider (`Web Speech API`) bereitgestellt.

### Abonnement und Finanzen

- `AboService`, `AbrechnungService` und `ProvisionsService` steuern Freischaltungen, Zahlungs-/Abrechnungsperioden und prozentuale Kommissionsverteilung.
- `FinanzService` verwaltet Einnahmen/Ausgaben, globale oder schulbezogene Kategorien, Kursdurchläufe und Ratenzahlungen.
- Kursdurchläufe (`KursNiveauDurchlauf`) frieren Preis und Zeitraum ein; Ratenzahlungen sind nur im aktiven Durchlauf zulässig.

## Zentrale Services

| Service | Verantwortung |
|---|---|
| `DashboardService` | Administrative, Lehrer-, Schulleiter- und Schülerprojekte; DB-Katalog für Home und `/lektionen`. |
| `LearningService` | Freigeschaltete Lektionen, Übungsabgaben, automatische Bewertung und Fortschritt. |
| `QuizService` | Quiz erstellen, zuweisen, durchführen und bewerten. Quizzuweisungen sind auf HALLOPLUS ausgerichtet. |
| `ExamService` | Prüfungen, Versuche, Bewertung und Ergebnisdarstellung. |
| `TeacherLessonService` | Lehrerlektionen, Seiten, Berechtigungen und PDF-Export. |
| `AboService` | Abo-Typ, Preis und Level-Freischaltung. |
| `AbrechnungService` / `ProvisionsService` | Abrechnung und Kommissionsanteile. |
| `FinanzService` | Buchungen, Kategorien, Kursdurchläufe und Ratenzahlungen. |
| `AuthService` | Registrierung, Schule, Rollen und Profilzuordnung. |
| `HomePageService` | Startseitennahe Daten/Logik. |

Services verwenden `IHalloAppDbContext`, EF-Core-Async-Methoden und optional `CancellationToken`. Leseabfragen verwenden häufig `AsNoTracking()` und View-Model-Projektionen.

## Katalog und Seeding

Standardlektionen und deren Übungen werden beim Seeding aus CSV-Dateien im externen Ordner `C:\ETSE\HalloAppSeed` gelesen. Erwartet werden `lektionen.csv`, `uebungen.csv` und `antworten.csv`. Audio-Übungen verwenden `AudioText` für Text-to-Speech; es werden keine Audio- oder Bilddateien importiert.

Der Ablauf in `ApplicationDbInitializer` lautet:

1. `MigrateAsync()`.
2. Berechtigungen und Rollen anlegen/aktualisieren.
3. Beispielschulen und Basisbenutzer anlegen/aktualisieren.
4. Bei `Seeding:ReseedCourseCatalog=true`: Katalog und abhängigen Fortschritt gezielt löschen.
5. Feste System-Niveaus (`A1.1`, `A1.2`, `A2.1`, `A2.2`, `B1.1`, `B1.2`, `B2.1`, `B2.2`, `C1`, `C2`) anlegen/aktualisieren.
6. Lektionen, Übungen und Antworten aus den CSV-Dateien importieren und validieren.
7. Historische Einschreibungen rückfüllen.

`ReseedCourseCatalog` betrifft den Kurskatalog, nicht Benutzer oder Schulen. Nach einem bewussten Reseed muss die Einstellung wieder auf `false` stehen.

## Tests

`HalloApp.Tests` ist eine xUnit-Testsuite für die Services. `TestDbContext` implementiert dieselbe `IHalloAppDbContext`-Schnittstelle mit EF Core InMemory; zusätzliche Fakes stehen für Medien, Unit of Work und UserManager bereit.

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
| Seite oder UI-Änderung | Passende Dateien unter `HalloApp.WebApp/Components/Pages`, anschließend zugehörige `.razor.cs` und Shared Components. |
| Fachregel | Zuständigen Service unter `HalloApp.Core/Services` und dessen Tests. |
| Neue/angepasste Daten | Datenmodell unter `HalloApp.Core/DataModels`, danach `ApplicationDbContext`, Migration und Tests. |
| Rechte und Rollen | `Program.cs`, `NavMenu.razor`, Seitenattribut `[Authorize]`, plus die Service-Prüfung. |
| Katalog-/Übungsinhalte | CSV-Dateien unter `C:\ETSE\HalloAppSeed` und `ApplicationDbInitializer`. |
| Finanz-/Abo-Thema | `AboService`, `AbrechnungService`, `ProvisionsService`, `FinanzService` und die dazugehörigen Datenmodelle. |
| Fehlerprotokollierung | `DatabaseExceptionLogSink`, `ExceptionLog`, `ExceptionLogs.razor`. |

## Aktuelle Leitlinien

- Keine horizontalen Scrollbars; Tablet/Mobil responsiv halten.
- Endanwendertexte und technische Interna sauber trennen; öffentliche Übungsinformationen deutsch, Erklärtexte überwiegend französisch.
- Core-Datenmodelle und Properties deutsch benennen; IDs folgen der vereinbarten Benennung (`Id`, sonst zusammengesetzte deutsche Namen mit `ID`).
- Neue Blazor-Logik bevorzugt im Code-behind, nicht in `@code`-Blöcken.
- Build-Warnungen wie Fehler behandeln und relevante Tests nach jeder fachlichen Änderung ausführen.
