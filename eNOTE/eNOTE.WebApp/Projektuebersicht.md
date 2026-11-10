# eNOTE – Technischer Einstieg

Architektur, Fachregeln, Einrichtung und Abläufe stehen zentral in [../Projektuebersicht.md](../Projektuebersicht.md), Regeln für die Zusammenarbeit in [../copilot-instructions.md](../copilot-instructions.md), Azure-Betrieb und Migrationsablauf in [../Deployment-Azure.md](../Deployment-Azure.md).

Kurz (Stand 10.11.2026): ein Konto pro Laden (ein weiterer Laden bekommt ein neues Besitzerkonto), Anmeldung per Pseudonym, wiederverwendbare Kontakt-E-Mail, Kassierer mit eigenem Konto und Kassen-PIN. Migrationskette bis `20261110173221_Kundenkartei`, lokal und auf Azure angewendet. Der normale App-Start migriert und seedet nie; Datenbank-Administration nur über `--database-admin=migrate|seed|demo --database-target=<Server>/<Datenbank>`.
