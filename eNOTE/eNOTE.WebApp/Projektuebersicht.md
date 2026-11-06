# eNOTE – Technischer Einstieg

Die aktuelle Architektur, Fachregeln, Einrichtung und Validierung stehen zentral in [../Projektuebersicht.md](../Projektuebersicht.md).

Es gilt: ein Geschäft pro Besitzerkonto, eindeutiges Pseudonym als Anmeldung, wiederverwendbare Kontakt-E-Mail, interne Kassierer nur mit Name und PIN. Keine Mehrladenauswahl im Besitzerkonto.

Historischer Arbeitsstand: Für `localhost / ENOTEDB` wurde ein Neuaufbau gemeldet und `20261105144750_InitialNeustart` angelegt. Im aktuell vorliegenden Dateistand existieren daneben alte Migrationen; weder lokales noch entferntes Schema wurde im Infrastrukturauftrag geprüft. Die Migrationskette vor Ausführung klären; kein erneuter Reset.

Development-Seed ohne Webserver/Worker: `dotnet run --project eNOTE/eNOTE.WebApp --no-launch-profile -- --environment Development --database-admin=seed --database-target=localhost/ENOTEDB`. Vorher Verbindung und vollständigen Build prüfen. Kein automatischer Migrations-/Seedlauf beim normalen App-Start. Zugangsdaten stammen ausschließlich aus sicherer Konfiguration; dieser Modus löscht keine Daten. Deployment-Einstellungen und der getrennte administrative Migrationspfad stehen in [../Deployment-Azure.md](../Deployment-Azure.md).
