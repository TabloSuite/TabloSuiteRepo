# SKOULOUAPP – Projekt- und Entwicklerübersicht

## Zweck

SKOULOUAPP ist eine schulbezogene Verwaltungs- und Lernplattform für gleichwertig behandelte Bildungseinrichtungen, etwa Schulen, Universitäten und Nachhilfeeinrichtungen. Schulleitungen und Koordinatoren verwalten Schulen, Klassen, Lehrkräfte, Schüler, Schuljahre, Stundenpläne, Noten, Bulletins, Kurszyklen und Zahlungen. Lehrkräfte erstellen Unterrichtsmaterialien und Prüfungen; Schüler bearbeiten freigegebene Inhalte und verfolgen ihren Lernstand. Die Plattform trennt Schulgebühren von automatischer zyklusbezogener Plattformabrechnung. Historische Abonnements und Provisionsdaten bleiben für Bestandsfälle erhalten, sind aber nicht das Abrechnungsmodell neuer Zyklen.

Die Anwendung nutzt .NET 10 und Blazor Interactive Server. Sichtbare Anwendungstexte sind Französisch; im Code kommen überwiegend deutsche fachliche Namen und historisch gewachsene englische Bezeichner vor. Verbindliche Projektvorgaben stehen in [`.github/copilot-instructions.md`](../.github/copilot-instructions.md); dieses Dokument beschreibt dagegen die aktuelle Implementierung.

## Solution-Struktur

| Projekt | Verantwortung |
|---|---|
| `SKOULOUAPP.WebApp` | Blazor-Oberfläche, Startup/DI, EF-Core-DbContext, Migrationen, Identity, schulische Unterrichts-/Bulletinservices, API-/PDF-Endpunkte, Medien, E-Mail und statische Inhalte. |
| `SKOULOUAPP.Core` | Fachmodelle, Form-/View-Modelle, Schnittstellen, Repositories und Geschäftsservices. |
| `SKOULOUAPP.Tests` | xUnit-Tests für Services und WebApp-Funktionen mit EF Core InMemory und Testdoubles sowie explizit aktivierbare lokale SQL-Integrationstests. |

Alle Projekte zielen auf `net10.0`, Nullable Reference Types und implizite Usings sind aktiviert.

Paketversionen werden zentral in `Directory.Packages.props` festgelegt; die Projektdateien verwenden versionslose `PackageReference`-Einträge. Fachservices liegen sowohl in `SKOULOUAPP.Core/Services` als auch in `SKOULOUAPP.WebApp/Services`. Insbesondere Stundenplan, Noten, Anwesenheit und Bulletins sind in der WebApp implementiert.

## Laufzeit und Komposition

`SKOULOUAPP.WebApp/Program.cs` ist der Einstiegspunkt; der zugehörige Namespace lautet `SkoulouApp.WebApp`.

- Blazor wird mit `AddRazorComponents().AddInteractiveServerComponents()` betrieben.
- EF Core nutzt SQL Server über `AddDbContextFactory<ApplicationDbContext>`; datenbanknutzende Dienste erhalten über die Factory einen eigenen Context statt eines im Blazor-Circuit geteilten Contexts.
- SQL-Server-Aufrufe verwenden eine Wiederholungsstrategie für transiente Fehler (bis zu sechs Wiederholungen, maximal 30 Sekunden Verzögerung je Wiederholung) und 60 Sekunden Command-Timeout. Mehrschrittige Schreibvorgänge müssen mit dieser Strategie und ihrer Transaktions-/Commit-Prüfung zusammenarbeiten.
- `ISkoulouAppDbContext` abstrahiert den DbContext für die Core-Services und Tests.
- ASP.NET Core Identity verwendet `AppUser`, `Role` und GUID-Schlüssel.
- Die Cookie-Prüfung berücksichtigt Kontosperren, schulbezogene Zugangsrechte und die aktive Sitzungskennung. UI-Sichtbarkeit allein ist keine Autorisierung.
- Serilog protokolliert auf Konsole und über `DatabaseExceptionLogSink` zusätzlich Exceptions in der Datenbank.
- Beim Start ruft `Program.cs` `ApplicationDbInitializer.SeedAsync(...)` auf. Nur wenn `Database:ApplyMigrationsOnStartup=true` gesetzt ist, werden zuvor Migrationen angewendet. Der Initializer legt Berechtigungen/Rollen und den initialen SuperAdmin an bzw. gleicht sie ab; er importiert keinen Kurskatalog.

### Datenbankverbindung

Die Verbindung wird über `Database:ConnectionStringName` ausgewählt:

- `DefaultConnection`: lokale Entwicklung; in `appsettings.Development.json` für SQL Server `localhost` konfiguriert.
- `AzureDefaultConnection`: Standardname in `appsettings.json`; produktive Verbindungsdaten gehören in User Secrets/Umgebungsvariablen.

Ohne den Konfigurationsschlüssel verwendet `Program.cs` den Namen `DefaultConnection`. Eine leere ausgewählte Verbindung führt zum Startabbruch; es gibt keinen stillen Wechsel auf eine andere Datenbank. Die tatsächliche Auswahl hängt von Umgebung, User Secrets und Umgebungsvariablen ab, nicht allein von `appsettings.json`. Als Umgebungsvariablen entsprechen beispielsweise `Database__ConnectionStringName` und `ConnectionStrings__AzureDefaultConnection` den jeweiligen Konfigurationsschlüsseln. Zugangsdaten gehören weder in diese Übersicht noch in Logs oder Kommandoausgaben.

Die Azure-Produktionsverbindung gehört nicht in versionierte Dateien. `Database:ApplyMigrationsOnStartup` steht standardmäßig auf `false`; Migrationen müssen bei Schemaänderungen bewusst für die richtige Umgebung ausgeführt werden.

### Lokaler Einstieg

1. .NET-10-SDK und SQL Server bereitstellen. Prüfen, dass der in `SKOULOUAPP.WebApp/appsettings.Development.json` konfigurierte `DefaultConnection` erreichbar ist; Zugangsdaten für andere Umgebungen über User Secrets oder Umgebungsvariablen setzen.
2. `dotnet restore SKOULOUAPPMAPPE.slnx` und `dotnet build SKOULOUAPPMAPPE.slnx` im Repository-Stamm ausführen.
3. EF-CLI bereitstellen und ausstehende Migrationen prüfen, z. B. `dotnet ef migrations list --project SKOULOUAPP.WebApp --startup-project SKOULOUAPP.WebApp --context ApplicationDbContext -- --environment Development`. Erst nach Prüfung der tatsächlich aufgelösten Zielverbindung gezielt aktualisieren: `dotnet ef database update --project SKOULOUAPP.WebApp --startup-project SKOULOUAPP.WebApp --context ApplicationDbContext -- --environment Development`. Ein Reset ist keine Schemafehlerbehebung und benötigt eine ausdrückliche Freigabe für die konkrete Datenbank. Ein bereits ausgeführter lokaler Neuaufbau ist keine erneut auszuführende Startvoraussetzung und kein Nachweis für den Azure-Datenbankstand.
4. `dotnet run --project SKOULOUAPP.WebApp --launch-profile https` startet die App lokal laut `launchSettings.json` unter `https://localhost:7064` und `http://localhost:5015`; das Profil `http` nutzt nur den HTTP-Port. Beide Profile setzen `Development`. Bei Bedarf das lokale HTTPS-Entwicklungszertifikat vertrauen. Seed-Verhalten steht in `Data/Seed/ApplicationDbInitializer.cs`; initiale Zugangsdaten nach der Einrichtung ändern und nicht in Dokumentationen übernehmen.
5. `dotnet test SKOULOUAPPMAPPE.slnx` führt die automatisierten Tests aus. Browserabläufe werden zusätzlich manuell geprüft.

### Weitere Laufzeitkonfiguration

| Bereich | Konfiguration und Verhalten |
|---|---|
| Medien | `AzureBlobStorage`: in Development deaktiviert und lokaler Medienservice, außerhalb Development aktivierter Blob-Zugriff. Verbindungsdaten kommen aus `ConnectionStrings:BlobConnection` oder `BlobConnection`. |
| Öffentliche Dokumentprüfung | `App:BaseUrl` muss für Bulletin-QR-Codes eine gültige absolute Basis-URL der erreichbaren App enthalten. |
| HTML-PDF-Export | Optional `LessonPdf:BrowserPath`; andernfalls sucht der Dienst bekannte Chrome-/Edge-/Chromium-Installationspfade. Auf dem ausführenden Host muss ein geeigneter Browser vorhanden sein. |
| E-Mail | Postmark-Konfiguration und Geheimnisse müssen zur Zielumgebung passen; `appsettings.json` deaktiviert den Versand zunächst. Der eingehende Webhook hat eine separate Authentifizierungsprüfung. |
| Rich Text | SignalR nimmt höchstens 8 MB pro Nachricht an. Diese Transportgrenze ist kein Ersatz für die Prüfungen beim Speichern oder für die Medienbibliothek. |

In Development meldet der Start offene Modelländerungen ohne Migration. Ein erfolgreicher App-Start oder Build belegt trotzdem weder ein aktuelles Datenbankschema noch einen erfolgreichen produktiven PDF-/Mail-/Medienzugriff.

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
- Unterrichtsverwaltung: Schuljahre, Fächer, Zuordnungen, Bewertungsperioden, manuelle Noten, Stundenpläne und Bulletins; Anwesenheit wird im Bulletin-Bereich erfasst.
- Schulleitung/Koordinatoren: Schulverwaltung, Klassen, Schülerfreigabe und Lehrkräfte; unter „Comptabilité et paiements“ befinden sich die je Schule freigegebenen Finanz-Tabs einschließlich „Facturation SKOULOUAPP“.
- SuperAdmin/ProduktOwner: globale bzw. zugewiesene Verwaltungs- und Finanzansichten, Facturation und Exception-Logs entsprechend der Rolle.

## Fachmodell und Persistenz

`ApplicationDbContext` kombiniert ASP.NET-Core-Identity mit den Anwendungsdaten. Migrationen liegen unter `SKOULOUAPP.WebApp/Migrations`; maßgeblich für die jeweilige Datenbank ist `__EFMigrationsHistory`. Im aktuellen Quellstand liegen 16 Migrationen von `20261002111952_InitialMigration` bis `20261102170932_SchulbezogeneKlassenOhneSprachgruppen`. Modelländerungen erfordern eine neue EF-Migration; ein erfolgreicher Build allein ändert das SQL-Schema nicht.

Die Kette umfasst neben dem Koordinatorrecht `CanAccessBilling` die Unterrichtsgrundlagen, Schul-Dokumentstammdaten, Stundenpläne, Bewertungen, Bulletin-Ausgaben und -Gestaltung, Notenhistorie, endgültige Bulletins und Berichtigungen sowie die automatische Zyklusteilnahme und Commit-Belege der Benutzeranlage. Die letzten drei Migrationen sind:

| Migration | Änderung |
|---|---|
| `20261102151047_AutomatischeZyklusteilnahme` | Ergänzt fünf nullable Felder für die automatische Zyklusabrechnung. |
| `20261102162907_BenutzerVorgangsBestaetigung` | Ergänzt die Tabelle für Commit-Belege atomarer Benutzervorgänge. |
| `20261102170932_SchulbezogeneKlassenOhneSprachgruppen` | Entfernt die historische Spalte `Klassen.CEFRGroup`. |

**Wichtig bei bestehenden Datenbanken:** Vor einer Aktualisierung Zielumgebung, angewandte Historie und ausstehendes SQL prüfen, Wiederherstellbarkeit und App-Kompatibilität sicherstellen. Vor dem Entfernen von `Klassen.CEFRGroup` noch benötigte historische Werte separat mit Klassen-ID sichern; die `Down`-Migration erstellt nur eine leere Spalte und rekonstruiert keine Werte. Keine Migrationskette ersetzen und kein Drop/Reset zur Fehlerbehebung. Diese Übersicht bestätigt keinen tatsächlich angewandten lokalen oder Azure-Migrationsstand.

### Schule, Benutzer und Klassen

- `School`: Mandant mit Standort, Beschreibung, Aktivstatus und optionalem schulspezifischem Abo-Preis sowie optionalen Kommissions-Prozentwerten.
- `AppUser`, `StudentProfile`, `UserSchool`: Identity, Schülerprofil und Schulzuordnung.
- `Klasse`: schulbezogene Klasse; Klassenzyklen und Teilnehmerzuweisungen werden separat geführt.
- `LevelEnrollment`: dauerhafte klassen-/zyklusbezogene Teilnahme mit Preis-Snapshot, Abrechnungszuordnung und gegebenenfalls begründeter Stornierung. Historische Zahlungsfelder sind kein Zugangskriterium neuer Zyklen. Der technische Abo-Typ ist `AboTyp.SkoulouApp`.

Die Datenbank erzwingt wichtige Eindeutigkeiten, etwa School-Name, Benutzer-E-Mail, `(SchoolId, Klasse.Code)`, Lernfortschritt pro Schüler/Lektion und PDF-Exportfreigaben. Die Beziehung `Klasse -> School` nutzt bewusst `Restrict`, um SQL-Server-Kaskadenpfade zu vermeiden.

Fachbeziehungen verwenden unveränderliche Benutzer-GUIDs (`StudentId`/`AppUserId`), nicht E-Mail-Adressen oder Matrikelnummern. Die berechtigte Benutzerverwaltung kann E-Mail und Benutzernamen am bestehenden Konto gemeinsam ändern; Identity aktualisiert die normalisierten Suchwerte. Das Schülerprofil bietet derzeit keine selbstbedienbare E-Mail-Änderung. Passwortänderung im Profil und Passwort-Reset sind vorhanden; ein erfolgreicher Wechsel rotiert die aktive Sitzungskennung. `SchuelerIdentitaetTests` prüft mit echtem Identity-UserManager den Erhalt von Profil, Matrikel, Klasse, Fortschritt und Teilnahme bei E-Mail-/Passwortwechsel.

Die Matrikel ist eine schulbezogene Kennung, kein Fremdschlüssel und kein Passwort. Der automatische Standard ist `EL-` plus Benutzer-GUID ohne Bindestriche (35 Zeichen). Eine vierstellige Pflicht existiert nicht und wird nicht eingeführt. Bereits veröffentlichte Bulletin-Stammdaten bleiben Dokument-Snapshots und werden über den Berichtigungsprozess korrigiert.

### Lerninhalte

- `Lesson`, `LessonMedia`, `Exercise`, `ExerciseOption`, `ExerciseAttempt`, `LessonProgress` bilden den systemweiten Lernkatalog und den Schülerfortschritt.
- `TeacherLesson`, `TeacherLessonPage` und `TeacherLessonPdfExportPermission` bilden von Lehrkräften geschriebene, paginierte Unterrichtsmaterialien ab.
- Inhalte sind HTML/Rich Text; `HtmlSanitizer` filtert unsichere Inhalte. Bilder dürfen nur im freigegebenen Lektion-/Übungserstellungsbereich über die gespeicherte Medienbibliothek eingefügt werden; der Lektionsmedienpfad verwendet Azure-Blob-Abstraktionen (in Development lokal konfiguriert).

### Beurteilungen

Quiz und Exam sind getrennte Datenmodelle mit ähnlicher Struktur. Eine eigenständige `QuizService`-Implementierung ist im aktuellen Workspace nicht vorhanden; daraus darf kein vollständig implementierter separater Quiz-Workflow abgeleitet werden. Die aktiven Lern- und Prüfungsabläufe liegen in den Lektionsaufgaben- und Exam-Services:

- Aufgabenblock: `QuizExercise` bzw. `ExamExercise`.
- Einzelaufgabe: `QuizQuestion` bzw. `ExamQuestion`.
- Antwortoptionen und Versuche: eigene Tabellen je Subsystem.

Unterstützt werden u. a. Single Choice, Multiple Choice, Freitext, Lückentext sowie Audio-Single-/Multiple-Choice. Mehrfachauswahl kann Teilpunkte oder All-or-Nothing verwenden. Audio wird derzeit über den Browser-Provider (`Web Speech API`) bereitgestellt.

### Abonnement und Finanzen

- Neue Zyklen verwenden `ZyklusTeilnahmeService` und einen beim Start eingefrorenen Plattformpreis pro Schüler. Ein expliziter Schulpreis hat Vorrang vor dem globalen Standard; die Preisverwaltung ist ausschließlich dem SuperAdmin vorbehalten.
- Angenommene Teilnehmer werden genau einmal je Zyklus erfasst, auch bei späterem Eintritt, Austritt, persönlicher Sperre oder Versetzung. Nicht angenommene Selbstregistrierungen zählen nicht. Eine Fehlzuordnung darf nur der SuperAdmin mit dokumentierter Begründung stornieren.
- `FinanzService` verwaltet Einnahmen/Ausgaben, globale oder schulbezogene Kategorien, Kursdurchläufe und Ratenzahlungen.
- Beim Zyklusabschluss entstehen die volle Schulrechnung und der zugehörige Plattformaufwand automatisch, unabhängig von Schülerzahlungen. Laufende Zyklen werden als vorläufige Verpflichtungen und abgeschlossene unbezahlte Rechnungen als offene Forderungen ausgewiesen. Es entstehen keine fiktiven Schülerzahlungen oder Schul-/Lehreranteile. Eine optionale ProductOwner-Vergütung darf die Schulforderung nicht mindern.
- Schulgebühren und Raten bleiben getrennt von Plattformkosten; offene Schulgebühren können beim ausdrücklich bestätigten Abschluss als Verlust gebucht werden. Zahlungsannahme und separate SuperAdmin-Bestätigung des Geldeingangs sind unterschiedliche Schritte.
- `AboService`, historische Abrechnungsabfragen und Provisionssnapshots bleiben für Bestandszyklen ohne Plattformpreis-Snapshot erhalten. Sie sind kein alternativer Vergabe- oder Abrechnungspfad für neue automatische Zyklen. Freigegebene Schüler behalten App-Zugang vor, während und nach einem Zyklus, unabhängig vom Zahlstatus; Konto-, Schul-, Klassen-, Veröffentlichungs- und PDF-Rechte werden weiterhin geprüft.

### Unterricht, Stundenplan und Bulletin scolaire

Die Modelle unter `SKOULOUAPP.Core/DataModels/Unterricht` bilden Schuljahre, Schüler-Jahreszuordnungen, Fächer, Unterrichtszuordnungen, Bewertungsperioden und -bestandteile, Fachnoten samt Änderungshistorie, Anwesenheit, Stundenpläne und Bulletin-Ausgaben ab. Die dazugehörigen Services liegen in `SKOULOUAPP.WebApp/Services`; `UnterrichtModellKonfiguration` ergänzt das EF-Mapping.

- `UnterrichtsVerwaltungService` verwaltet schulbezogene Grundlagen und Zuordnungen. `StundenplanService` trennt Bearbeitung und Veröffentlichung; `StundenplanPdfService` erzeugt die PDF-Ausgabe.
- `BewertungsService` verwaltet manuelle Bulletin-Noten einschließlich Änderungshistorie; `AnwesenheitsService` verwaltet Anwesenheitsangaben. Exam-Ergebnisse bleiben davon getrennt und werden nicht automatisch als Bulletin-Noten übernommen.
- `BulletinService` erzeugt versionierte Dokument-Snapshots mit den zum Erstellungszeitpunkt gültigen Stammdaten und Bewertungen. Spätere Änderungen am Benutzerkonto ändern ein bereits veröffentlichtes Dokument nicht stillschweigend.
- `BulletinAbschlussService` prüft die ganze Klasse und verlangt für die endgültige Veröffentlichung eine ausdrückliche Bestätigung sowie einen unveränderten Prüf-Hash. Danach ist die normale Änderung/Rücknahme gesperrt.
- Berichtigungen benötigen eine Begründung, einen Entwurf, eine Wirkungsprüfung und eine separat bestätigte Freigabe. Auswirkungen auf Ränge und Folgeperioden werden berücksichtigt; betroffene Originale bleiben archiviert, Ersatzdokumente werden verknüpft und unveränderte Mitschüler nicht unnötig ersetzt. `BulletinStammdatenService` unterstützt die gezielte Stammdatenberichtigung. Benachrichtigung und Abholung werden dokumentiert.
- `BulletinPdfService` erstellt PDFs mit optionalem Schullogo und freiem Bereich für echte Unterschrift und Schulstempel; ein Siegel-Upload gehört nicht zum aktuellen Ablauf.
- Endgültige Ausgaben erhalten Dokumentnummer, Prüftoken und QR-Code. `BulletinPruefungService` gibt öffentlich nur Schule, Dokumentnummer, Datum und Dokumentstatus einschließlich Ersatzverweis zurück, keine Schülernamen, Geburtsdaten oder Noten.

## HTTP-Endpunkte und PDF-Erzeugung

`Program.cs` definiert neben Blazor zusätzliche HTTP-Endpunkte. Die Endpunktrollen und die fachlichen Schul-/Eigentümerprüfungen der Services gelten zusammen:

| Bereich | Einstieg und Schutz |
|---|---|
| Bulletin-PDF | `/bulletins/{ausgabeID}/pdf` für berechtigte Direktion/Koordination und Schüler; Sammelausgaben nur für Direktion/Koordination. |
| Öffentliche QR-Prüfung | `/verification-bulletin/{token}` anonym, ohne personenbezogene Leistungsdaten, mit `no-store`, `noindex`, restriktiver Content Security Policy und Limit von 30 Anfragen pro Minute/IP. |
| Stundenplan-PDF | `/emplois-du-temps/{schulID}/pdf` mit Anmeldung und schulbezogener Prüfung. |
| Lerninhalte-PDF | Routen unter `/teacher-exams`, `/teacher/lesson-exercises`, `/teacher-lessons` und `/student`; Schülerexporte ohne Lösungen, zusätzliche Freigabeprüfungen im jeweiligen Ablauf. |
| Finanzen | `/finanzen/rapport/pdf-file` und `/finanzen/justificatif/{buchungId}` mit Finanz-/Schulberechtigungen. |
| Medien | `/media-library/{assetId}` und `/media-library/videos/{assetId}` mit Anmeldung und assetbezogener Zugriffsprüfung. |
| Anmeldung | `/auth/login/submit` mit Antiforgery-Prüfung, `/auth/logout` für Abmeldung. |
| Eingehende E-Mail | `POST /api/postmark/inbound` mit Prüfung des konfigurierten Basic-Auth-Geheimnisses; kein Browserformular und deshalb ohne Antiforgery. |

PDFs werden serverseitig erzeugt: QuestPDF wird unter anderem für Bulletins, Stundenpläne und Finanzberichte verwendet. Der A4-HTML-Export über `LessonExerciseA4PdfService` startet einen installierten Headless-Browser mit deaktivierten Browser-Kopf-/Fußzeilen. Dieser Dienst benötigt neben `LessonPdf:BrowserPath` beziehungsweise Browsererkennung auch Prozessstartrechte und ein beschreibbares temporäres Verzeichnis; ohne verfügbare Engine schlägt der Export mit einer fachlichen Fehlermeldung fehl. Ein erfolgreicher .NET-Build prüft diese Betriebsvoraussetzungen nicht.

## Zentrale Services

| Service | Verantwortung |
|---|---|
| `DashboardService` | Administrative, Lehrer-, Schulleiter- und Schülerprojekte; DB-Katalog für Home und `/lektionen`. |
| `LearningService` | Freigeschaltete Lektionen, Übungsabgaben, automatische Bewertung und Fortschritt. |
| `ExamService` | Prüfungen, Versuche, Bewertung und Ergebnisdarstellung. |
| `TeacherLessonService` | Lehrerlektionen, Seiten, Berechtigungen und PDF-Export. |
| `ZyklusTeilnahmeService` | Historisierte, einmalige Zyklusteilnahme und begründete SuperAdmin-Stornierung. |
| `SchuelerZugangService` / `SchulZugangService` | Zahlungsunabhängiger Schülerzugriff und schulbezogene Sperren. |
| `AboService` | Kompatibilität historischer Zyklen; manuelle Änderungen neuer Zykluspositionen sind gesperrt. |
| `AbrechnungService` / `ProvisionsService` | Rechnungs-/Zahlungsstatus, Verpflichtungsübersicht und historische Provisionsauswertungen. |
| `BenutzerArbeitsvorgang` | Atomare Aufnahme-/Importvorgänge mit eigenem Context je Versuch und Commit-Beleg bei verlorener Commit-Antwort. |
| `FinanzService` | Buchungen, Kategorien, Kursdurchläufe und Ratenzahlungen. |
| `AuthService` | Registrierung, Schule, Rollen und Profilzuordnung. |
| `HomePageService` | Startseitennahe Daten/Logik. |
| `UnterrichtsVerwaltungService` / `StundenplanService` | Schuljahre, Unterrichtszuordnungen und Stundenpläne. |
| `BewertungsService` / `AnwesenheitsService` | Manuelle Fachnoten, Notenhistorie und Anwesenheit. |
| `BulletinService` / `BulletinAbschlussService` | Dokument-Snapshots, endgültige Klassenveröffentlichung und geregelte Berichtigungen. |
| `BulletinStammdatenService` / `BulletinPruefungService` | Gezielte Stammdatenkorrektur und datensparsame öffentliche Dokumentprüfung. |

Services verwenden `ISkoulouAppDbContext`, EF-Core-Async-Methoden und optional `CancellationToken`. Leseabfragen verwenden häufig `AsNoTracking()` und View-Model-Projektionen. Bei neuen Blazor-Datenzugriffen Contexts per `IDbContextFactory` statt parallel aus einem gemeinsam genutzten Circuit-Context beziehen.

## Katalog und Seeding

`ApplicationDbInitializer` legt nur Rollen/Berechtigungen und den initialen SuperAdmin an bzw. aktualisiert sie. Schulen und andere Benutzer werden über die Anwendung erfasst. Es gibt im Initializer keinen automatischen CSV-Katalogimport und kein automatisches Zurücksetzen von Lernfortschritten. Änderungen am Lernkatalog sind getrennt von Datenbankmigrationen zu behandeln; Text-to-Speech verwendet Text statt importierter Audiodateipfade.

## Tests

`SKOULOUAPP.Tests` ist eine xUnit-Testsuite. `TestDbContext` implementiert dieselbe `ISkoulouAppDbContext`-Schnittstelle mit EF Core InMemory; zusätzliche Fakes stehen für Medien, Unit of Work und UserManager bereit. InMemory-Tests ersetzen keine SQL-Migrationsprüfung. Identity-Tests verwenden auch einen echten `UserManager` auf einer isolierten Testdatenbasis.

Wichtige Testbereiche sind:

- Abo, Abrechnung und Provisionen.
- Finanzverwaltung und Ratenzahlungen.
- Dashboard- und Schul-/Klassen-Governance.
- Lektionen, Übungsbewertung, Quiz, Exam und Mehrfachauswahl.
- Rich-Text-Sanitizing und Medien.
- Lehrerlektionen/PDF-Berechtigungen.
- Unterrichtsverwaltung, Stundenpläne, manuelle Noten, Anwesenheit, Bulletin-Snapshots, endgültige Veröffentlichung, Berichtigungen und PDF-Ausgaben (`UnterrichtsVerwaltungTests`, `StundenplanTests`, `BewertungsServiceTests`).
- Historisierte Zyklusteilnahme, Schulverpflichtungen, zahlungsunabhängiger Zugriff und stabile Schüleridentität (`ZyklusTeilnahmeServiceTests`, `ZyklusVerpflichtungsTests`, `SchuelerIdentitaetTests`).
- Ein durchgehender Ablauf von Schule und Schulleiter über Schülerfreigabe und Zyklus bis zu Lernfortschritt und Abrechnung (`GesamtAblaufTests`).

### Lokale SQL-Integrationstests

`UnterrichtSqlTests`, `BenutzerArbeitsvorgangSqlTests` und `ZyklusteilnahmeSqlTests` tragen `Category=SqlIntegration`. Sie werden nur bei `SKOULOU_SQL_TESTS=1` tatsächlich ausgeführt. **Ohne diese Variable kehren die Methoden sofort zurück und erscheinen trotzdem als bestanden**; ein grüner Standard-Testlauf belegt daher keine ausgeführte SQL-Prüfung.

Die Tests verwenden fest `localhost` mit Windows-Authentifizierung und zufällig benannte `SKOULOUAPP_TEST_…`-Datenbanken. Sie wenden die vorhandene Migrationskette an und räumen ihre Testdatenbanken anschließend auf. Voraussetzungen sind ein erreichbarer lokaler SQL Server und Rechte zum Erstellen/Löschen dieser isolierten Datenbanken; weder die App-Datenbank `SKOULOUAPPDB` noch Azure sind Testziele.

In PowerShell die Variable für den Testprozess setzen: `$env:SKOULOU_SQL_TESTS='1'`. Danach führt `dotnet test SKOULOUAPPMAPPE.slnx --filter "Category=SqlIntegration"` nur die SQL-Tests aus; ohne Filter wird die gesamte Suite einschließlich SQL ausgeführt. Anschließend die Variable mit `Remove-Item Env:SKOULOU_SQL_TESTS` wieder entfernen beziehungsweise einen zuvor vorhandenen Wert wiederherstellen.

Für eine aktuelle Warnungserfassung ist `dotnet build SKOULOUAPPMAPPE.slnx --no-incremental` statt eines rein inkrementellen Builds zu verwenden. Historische TRX-Dateien unter `SKOULOUAPP.Tests/TestResults` sind Nachweise ihrer jeweiligen Läufe, keine automatisch aktuelle Validierung. Testzahlen und Datenbankstände müssen aus dem konkret ausgeführten Lauf stammen.

Die Tests prüfen vor allem fachliche Service-Logik; UI- und Browser-Interaktionen werden nicht als End-to-End-Browser-Tests abgedeckt.

## Praktische Einstiegspunkte für neue TODOs

| Thema | Zuerst ansehen |
|---|---|
| Seite oder UI-Änderung | Passende Dateien unter `SKOULOUAPP.WebApp/Components/Pages`, anschließend zugehörige `.razor.cs` und Shared Components. |
| Fachregel | Zuständigen Service unter `SKOULOUAPP.Core/Services` oder `SKOULOUAPP.WebApp/Services` und dessen Tests. |
| Neue/angepasste Daten | Datenmodell unter `SKOULOUAPP.Core/DataModels`, danach `ApplicationDbContext`, Migration und Tests. |
| Rechte und Rollen | `Program.cs`, `NavMenu.razor`, Seitenattribut `[Authorize]`, plus die Service-Prüfung. |
| Katalog-/Übungsinhalte | `SKOULOUAPP.Core/DataModels/Lessons`, `LearningService`, `TeacherLessonService` und die verantwortlichen Import-/Medienpfade. |
| Finanz-/Abo-Thema | `ZyklusTeilnahmeService`, `SchuelerZugangService`, `SchulZugangService`, `AbrechnungService`, `FinanzService`; historische Sonderfälle zusätzlich in `AboService`/`ProvisionsService`. |
| Stundenplan/Noten/Bulletin | `DataModels/Unterricht`, Services der WebApp und Seiten `UnterrichtsVerwaltung`, `Stundenplaene`, `NotenVerwaltung`, `Bulletins` einschließlich `Bulletins.Abschluss.cs`. |
| Registrierung/Import/Identität | `AuthService`, `BenutzerArbeitsvorgang`, administrative Benutzerverwaltung und die Import-/Identitätstests. |
| Fehlerprotokollierung | `DatabaseExceptionLogSink`, `ExceptionLog`, `ExceptionLogs.razor`. |

## Aktuelle Leitlinien

- Keine horizontalen Scrollbars; Tablet/Mobil responsiv halten.
- Endanwendertexte auf Französisch, technische Interna und Berechtigungsdetails nicht in die Endnutzeroberfläche übernehmen.
- Core-Datenmodelle und Properties deutsch benennen; IDs folgen der vereinbarten Benennung (`Id`, sonst zusammengesetzte deutsche Namen mit `ID`).
- Neue Blazor-Logik bevorzugt im Code-behind, nicht in `@code`-Blöcken.
- Vor Service-/Datenmodelländerungen auf abhängige UI-Flüsse, Rechte, Schultrennung und Finanzkonsistenz prüfen; relevante Tests und Lösungsbuild nach Änderungen ausführen.
