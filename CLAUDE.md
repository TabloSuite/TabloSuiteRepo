# TabloSuite – Regeln für Claude (gilt für alle im Team)

Diese Datei liest Claude Code bei jedem Start automatisch. Sie gilt für alle, die an diesem Repository arbeiten.
Persönliche Vorlieben gehören in die eigenen Claude-Einstellungen, nicht hierher.

## Team und Git (verbindlich)

- Wir sind zu zweit: **Chris** arbeitet im Branch `Etse.TabloSuite.Muster`, **Sado** in seinem eigenen Branch (z. B. `Sado.Garros`).
  Jeder ändert, committet und pusht **nur im eigenen Branch**.
- **`master` ist der gemeinsame Stand.** Dorthin wird nur nach **ausdrücklicher Zustimmung im Chat** zusammengeführt, committet oder gepusht.
  Vorher `git fetch` und neue Commits aus `master` zuerst in den eigenen Branch holen. Kein Force-Push, keine fremden Branches umschreiben.
- Vor jedem Commit: `git status -sb` (richtiger Branch?), nur die eigenen Änderungen aufnehmen, Build und betroffene Tests grün.
- Commit-Nachrichten auf Deutsch mit Bereich vorne: `eNOTE: …`, `SKOULOU: …`, `TabloSuiteHome: …`, `Skript: …`.

## Geheimnisse und Azure (verbindlich)

- **Keine Passwörter, Schlüssel, Tokens oder Verbindungszeichenfolgen mit Geheimnissen in Dateien des Repositorys.**
  Lokal gehören sie in die User-Secrets, in Azure in die App-Einstellungen der Web-App.
- **Azure ist Produktion:** Ressourcengruppe `TabloSuiteRGruppe`, Web-Apps `enote` (enote.tablosuite.com), `skoulou` (skoulou.tablosuite.com),
  `tablosuite-home` (tablosuite.com), Datenbanken `ENote-Database` und `Skoulou-Database` auf `tablosuite-sqlserver`.
  **Lesen ist erlaubt. Jede Änderung** – Migration oder SQL, App-Einstellung, Deploy, Ressourcen anlegen/löschen, Tarifstufen – **nur nach ausdrücklichem OK.**
- Migrationen in Azure erst unmittelbar vor dem Deploy einspielen: Spalten-Umbenennungen brechen sonst die laufende Version.
- Keine Daten löschen oder Datenbanken zurücksetzen, um Fehler zu verdecken.

## Projektkarte

Projektmappe `TabloSuiteMappe.slnx`, .NET 10, Blazor (Interactive Server), EF Core mit SQL Server, Tests mit xUnit.

| Ordner | Was | Einstieg |
|---|---|---|
| `eNOTE/` | Software für Salons und Läden (Kasse, Kunden, Lager, Abos) | `eNOTE/Projektuebersicht.md`, Fachregeln `eNOTE/copilot-instructions.md`, Azure `eNOTE/Deployment-Azure.md` |
| `Skoulou/` | Schulverwaltung SKOULOU (Neubau seit 13.11.2026) | verbindlich `Skoulou/docs/Bauplan.md` und `Skoulou/docs/fachkonzept/01…05` |
| `TabloSuiteHome/` | Firmenseite tablosuite.com (statisch, ohne Datenbank) | `TabloSuiteHome/README.md` |
| `scripts/` | Hilfsskripte, u. a. lokale Datenbanken anlegen | – |
| `docs/` | Allgemeine Anleitungen, u. a. Claude in Visual Studio einrichten | `docs/Claude-in-Visual-Studio-einrichten.md` |

- `STAND.md` ist der Arbeitsstand von Chris – nur nach Absprache ändern.
- `.github/copilot-instructions.md` beschreibt überwiegend das frühere SKOULOUAPP, das nicht mehr im Repository ist.
  Bei Widersprüchen gelten der Code, die Dokumente aus der Tabelle und diese Datei.

## Konventionen

- **Sichtbare Texte der Apps nur auf Französisch** (Anrede « vous »). Code, Kommentare, Entwicklerdoku und Kommunikation auf Deutsch.
- In Blazor je Datenbankoperation einen eigenen Context über `IDbContextFactory`.
- Rechte immer serverseitig im Service prüfen, nie nur in der Oberfläche.
- Fachregeln vor Änderungen mit dem tatsächlichen Code abgleichen; ältere Dokumente können veraltet sein.

## Lokal entwickeln

- Jeder trägt in seinen **User-Secrets eigene Seed-Anmeldedaten** ein; damit legt die App beim Start das lokale Admin-Konto an:
  eNOTE `Seed:SuperAdminEmail`, `Seed:SuperAdminBenutzername`, `Seed:SuperAdminPasswort` · SKOULOU `Seed:SuperAdminEmail`, `Seed:SuperAdminIdentifiant`, `Seed:SuperAdminPasswort`.
- **Lokale Datenbanken anlegen oder aktualisieren:** `scripts\Datenbanken-anlegen.cmd` (eNOTE `ENOTEDB`, SKOULOU `SkoulouDB`; Option `-Pruefen` ändert nichts).
- In Development speichern beide Apps Dateien lokal. Ohne Schlüssel gehen keine echten Mails raus, KI-Hilfe und Vorlesen sind aus.
- Start: eNOTE https://localhost:62148, SKOULOU https://localhost:7164 (Profil https).

## Datenbank-Fallen

- **eNOTE** wendet beim Start keine Migrationen an. Anlegen/Aktualisieren:
  `dotnet run --project eNOTE/eNOTE.WebApp -- --database-admin=migrate-and-seed --database-target=localhost/ENOTEDB` (das Ziel muss exakt zur Verbindung passen).
- **SKOULOU:** `SkoulouDbContextDesignFactory` hat fest `localhost/SkoulouDB` eingetragen. `dotnet ef database update` deshalb **immer mit `--connection "<Ziel>"`**,
  sonst trifft es die lokale `SkoulouDB`.

## Tests

- `dotnet test TabloSuiteMappe.slnx`
- Zusätzliche SQL-Server-Tests für SKOULOU nur mit der Umgebungsvariable `SKOULOU_SQL_TESTS=1`.

## Kommunikation

- Auf Deutsch antworten, kurz und verständlich; bei längeren Arbeiten zwischendurch melden, was gerade läuft.
