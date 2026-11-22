# TabloSuite – Projektübersicht

Stand 22.11.2026. Einstieg für alle, die im Repository arbeiten. Verbindliche Team-Regeln stehen in [`CLAUDE.md`](../CLAUDE.md),
der laufende Arbeitsstand von Chris in [`STAND.md`](../STAND.md). Die frühere SKOULOUAPP ist nicht mehr im Repository (nur Git-Historie).

## Produkte

| Produkt | Ordner | Live | Was |
|---|---|---|---|
| **eNOTE** | `eNOTE/` | https://enote.tablosuite.com | Kasse und Verwaltung für kleine Geschäfte in Westafrika (Salon, Institut, Boutique, Cabinet, Maquis …): Kasse mit Bon, Kunden, Lager, Team mit PIN, Finanzen, Abonnement; Plattform-Verwaltung für Administrateur und Responsables. |
| **SKOULOU** | `Skoulou/` | https://skoulou.tablosuite.com | Schulverwaltung (Neubau seit 13.11.2026, live seit 14.11.): Schuljahr und Perioden, Klassen, Einschreibungen, Noten, Conseil, Bulletins, Passage, Schulgebühren und Kasse, Leçons/Exercices/Examens, Messagerie, Plattformrechnung je Periode (500 F je Schüler und Monat). |
| **TabloSuiteHome** | `TabloSuiteHome/` | https://tablosuite.com | Statische Firmenseite mit Katalog der Apps, ohne Datenbank. |

Sichtbare Texte der Apps nur auf Französisch (« vous »); Code, Kommentare und Entwicklerdoku auf Deutsch.

## Technik

- Projektmappe `TabloSuiteMappe.slnx`, .NET 10, Blazor (Interactive Server), EF Core mit SQL Server, Tests mit xUnit.
- Je Datenbankoperation ein eigener Context über `IDbContextFactory`; Rechte immer serverseitig im Service.
- Keine Transaktionen mit `Serializable` (Deadlocks unter Last). eNOTE regelt Gleichzeitigkeit über Anwendungssperren je Laden
  (`sp_getapplock`: `KassenSperre` für Kasse und Lager, `VertragsSperre` für Abo-Perioden); SKOULOU über Transaktionen
  (`TransaktionAsync`) und Prüfungen beim Speichern, die gleichzeitige Änderungen erkennen statt still zu überschreiben.
- « Jetzt » kommt in beiden Apps nur aus `AppUhr` (eNOTE `ENote.Core.Services.AppUhr`, SKOULOU `Skoulou.Core.Werkzeuge.AppUhr`);
  in Produktion die echte Uhr, in Klicktests über Jahre per Startup-Hook verstellbar.
- PDFs (Bon, Rapport financier, Bulletins, Rechnungen) ohne Ligaturen in der Textebene, damit Kopieren und Suchen funktionieren.

## Azure (Produktion)

Ressourcengruppe `TabloSuiteRGruppe` (France Central). **Lesen ist erlaubt, jede Änderung nur nach ausdrücklichem OK** (siehe `CLAUDE.md`).

| Ressource | Name | Hinweis |
|---|---|---|
| Web-App eNOTE | `enote` (Plan `ENote-AppServicePlan`, B1) | Deploy durch den Nutzer aus Visual Studio |
| Web-App SKOULOU | `skoulou` (Plan `Skoulou-AppServicePlan`, B1) | Startbefehl `dotnet Skoulou.WebApp.dll` |
| Web-App Firmenseite | `tablosuite-home` (auf dem SKOULOU-Plan) | |
| SQL | `tablosuite-sqlserver`: `ENote-Database`, `Skoulou-Database` (je Basic) | Migrationen erst unmittelbar vor dem Deploy |
| Speicher | `tablosuiteblobbtorage` (Standard_LRS) | Belege und Medien, Zugriff per Managed Identity |
| Sprache | `enote-speech` (F0) | Vorlesen in SKOULOU, Videoproduktion |

Mails über Postmark, KI-Hilfe über Anthropic – Schlüssel nur in den App-Einstellungen der Web-Apps bzw. lokal in den User-Secrets,
nie im Repository. Vor dem echten Produktivbetrieb gemeinsam prüfen: Tarifstufen (SQL, App Service, Speech), Redundanz des Speichers,
Überwachung, Budget-Warnung, Sicherungen.

## Lokal entwickeln

- User-Secrets mit eigenen Seed-Anmeldedaten (siehe `CLAUDE.md`), dann `scripts\Datenbanken-anlegen.cmd` (eNOTE `ENOTEDB`, SKOULOU `SkoulouDB`).
- Start: eNOTE https://localhost:62148, SKOULOU https://localhost:7164 (Profil https). In Development werden Dateien lokal gespeichert,
  ohne Schlüssel gehen keine Mails raus, KI-Hilfe und Vorlesen sind aus.
- eNOTE migriert beim Start nicht; SKOULOU-Migrationen mit `dotnet ef database update --connection "<Ziel>"`.

## Tests und Simulationen

| Was | Aufruf |
|---|---|
| Alle Unit-Tests | `dotnet test TabloSuiteMappe.slnx` |
| SKOULOU mit SQL Server | Umgebungsvariable `SKOULOU_SQL_TESTS=1` |
| SKOULOU-Simulation (viele Schulen, mehrere Jahre) | `SKOULOU_SIM=1` (Bericht `Skoulou/docs/Simulation-Bericht.md`) |
| eNOTE-SQL-Tests und Simulation (viele Läden, 18 Monate) | `ENOTE_SIM=1` (Bericht `docs/Simulation-eNOTE.md`) |
| Beispiel-PDFs zum Ansehen ablegen | `SKOULOU_PDF_BILDER=<Ordner>`, `ENOTE_PDF_BILDER=<Ordner>` |

Klicktests « wie ein Mensch » (Playwright) liegen bewusst außerhalb des Repositorys; sie laufen gegen eigene Test-Datenbanken und
eigene Ports, nie gegen die lokalen Entwickler-Datenbanken oder Azure.

## Wo steht was

| Thema | Datei |
|---|---|
| eNOTE: Architektur, Fachregeln, Abläufe | `eNOTE/Projektuebersicht.md`, `eNOTE/copilot-instructions.md` |
| eNOTE: Azure-Betrieb und Migrationsablauf | `eNOTE/Deployment-Azure.md` |
| eNOTE: Bondrucker | `eNOTE/Bon-Drucken.md` |
| SKOULOU: Planung und Entscheidungen (verbindlich) | `Skoulou/docs/Bauplan.md` (§11 Entscheidungen), `Skoulou/docs/fachkonzept/01…05` |
| SKOULOU: Simulationen | `Skoulou/docs/Simulation-Bericht.md` |
| Firmenseite | `TabloSuiteHome/README.md` |
| Claude in Visual Studio | `docs/Claude-in-Visual-Studio-einrichten.md` |
| Auftrag der Simulationen | `docs/Simulationen-Auftrag.md` |

## Team und Git

Chris arbeitet im Branch `Etse.TabloSuite.Muster`, Sado im eigenen Branch. `master` ist der gemeinsame Stand und wird nur nach
ausdrücklicher Zustimmung zusammengeführt; vorher `git fetch` und neue Commits aus `master` in den eigenen Branch holen.
Commit-Nachrichten auf Deutsch mit Bereich vorne (`eNOTE: …`, `SKOULOU: …`, `TabloSuiteHome: …`, `Skript: …`).
