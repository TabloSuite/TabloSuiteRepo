# eNOTE – sichere Azure-Deployment-Vorbereitung

## Aktueller Betriebsstand (10.11.2026, abends)

Dieser Abschnitt hat Vorrang vor älteren Aussagen weiter unten (die aus der Vorbereitungsphase stammen).

- **Veröffentlicht:** App Service `enote` (Linux, .NET 10, Plan `ENote-AppServicePlan`, Tarif **B1**, 1 Instanz), Veröffentlichung durch den Nutzer per Visual Studio (Zip Deploy). Ziel `https://enote.tablosuite.com`.
- **Einstellungen (geprüft):** **Always On = Ein** (sonst schläft die App ein und die Hintergrunddienste – stündliche Abo-Synchronisierung, Mail-Wiederholung, fällige Tarifänderungen – stoppen), **Sitzungsaffinität = Ein**. WebSockets sind unter Linux immer aktiv (der Schalter existiert nur für Windows-Apps). **Kein Azure SignalR Service nötig**, solange nur eine Instanz läuft.
- **Datenbank:** `ENote-Database` steht auf `20261110173221_Kundenkartei` (gleich wie lokal). Ablauf für weitere Migrationen siehe §5 „Praxis-Ablauf“.
- **Videos:** Erklär- und Werbevideos liegen in `eNOTE.WebApp/Medien/videos/` und werden mit dem Publish ausgeliefert (csproj `CopyToPublishDirectory`). Das ist eine bewusste Ausnahme zur Blob-Regel: es sind Programm-Assets, keine Nutzerdaten. Ausgabe über `/medien/videos/{datei}` (Range-fähig, Rollenprüfung); Adressen tragen `?v=<Dateidatum>` gegen veralteten Browser-Cache.
- **Tests:** 270/270 grün (Stand Commit `2b0f40d`).

## Status und Grenzen (Vorbereitungsphase, historisch)

Die aktive Solution `SKOULOUAPPMAPPE.slnx` enthält **eNOTE.Core, eNOTE.WebApp und eNOTE.Tests**. Veröffentlicht werden soll die Kassen-/Geschäftsverwaltungs-App eNOTE, nicht die daneben im Repository vorhandene Schul-App SKOULOUAPP. Zieladresse ist `https://enote.tablosuite.com`.

Die Infrastruktur ist im Quellcode umgesetzt. Dies ist **keine Freigabe zur Veröffentlichung**. Aktuelle lokale Prüfung im Hosting-Vorbereitungsauftrag:

- Dateidiagnostik der geänderten Dateien: keine Probleme gemeldet.
- Projektbuild `eNOTE.WebApp`: erfolgreich, einschließlich plattformunabhängiger SVG-Barcodes.
- Lösungsbuild (Stand 10.11.2026): **erfolgreich**, 0 Fehler, 73 Warnungen.
- Testausführung für `eNOTE.Tests`: **200/200 bestanden**, einschließlich der SVG-Regressionstests.
- Linux-Publish-Profil vorbereitet, noch kein Publish-Paket erzeugt und kein Linux-Laufzeittest durchgeführt. Ein Windows-Projektbuild ist kein Linux-Betriebsnachweis.

Die Migrationskette ist bereinigt (`InitialCreate` bis `KategorieUnterkategorien`, ohne konkurrierende Dateien); trotzdem nicht ungeprüft gegen Azure ausführen. Die Hosting-Vorbereitung benötigt selbst keine neue EF-Migration. Testinkonsistenzen vor Veröffentlichung fachlich beheben, nicht durch Ausschluss von Tests oder Wiederherstellen veralteter APIs verdecken.

Es wurden keine Azure-Ressourcen verändert, keine Datenbank migriert/befüllt und keine Secrets gelesen. Angaben zu Ressourcen und Berechtigungen unten stammen vom Nutzer, nicht aus einer aktuellen Cloudprüfung.

## Hostingentscheidung und unveränderte Dienste

Empfohlen ist der bereits dokumentierte **Linux Azure App Service mit .NET 10 und einer Instanz**. Das passt zu Blazor Interactive Server und den vorhandenen SignalR-Hubs; Static Web Apps ist kein alleiniger Host für diese serverseitige Anwendung. Keine Containerplattform, kein zusätzlicher SignalR-Dienst und keine neuen kostenpflichtigen Ressourcen für den ersten Betrieb einführen. Den genannten B1-Plan nur nach Bestätigung des tatsächlichen Bestands und der laufenden Kosten verwenden; Tarif, Runtime-Verfügbarkeit und Region wurden nicht online geprüft.

Die vorhandene ASP.NET-Core-Identity-Anmeldung bleibt bestehen. Managed Identity ist im Produktionscode bereits für Blob und SQL vorgesehen und bedeutet **keine Umstellung der Benutzeranmeldung auf Entra ID**. Serilog bleibt unverändert; keine pauschale OpenTelemetry-, Key-Vault- oder Authentifizierungsmigration. Der historische Plan unter `.appmod/.migration/` wird nicht wiederaufgenommen: seine Forderung nach Entfernen des lokalen Development-Speichers widerspricht den aktuellen Vorgaben. Kein Modernize, Branchwechsel oder automatischer Commit.

**E-Mail:** Versand über **Postmark** (HTTP-API, Domain `tablosuite.com` mit DKIM und Return-Path verifiziert); das Postfach `contact@tablosuite.com` bleibt bei **IONOS** (MX unverändert) und empfängt Antworten. Kein Azure Communication Email, kein Domaintransfer. Details siehe Abschnitt „E-Mail (Postmark)“ unten.

## 1. Speicher und Authentifizierung

- **Development:** ausschließlich `LocalMediaStorageService`, standardmäßig `C:\DevBlob\ENoteDataLibrary` aus `appsettings.Development.json`. Dateien liegen außerhalb von `wwwroot` und werden nicht statisch veröffentlicht. Ein Azure-Login ist nicht erforderlich.
- **Production und andere Nicht-Development-Umgebungen:** ausschließlich `AzureBlobMediaStorageService` mit einem wiederverwendeten `BlobServiceClient` und `ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)`.
- Keine Shared-Key-Verbindung, kein SAS, keine Entwickler-Credential-Kette und kein lokaler Fallback. Fehler werden protokolliert; fehlgeschlagene Uploads liefern einen französischen Fehler, fehlende/nicht lesbare Dateien keine Ersatzdatei.
- Der Container wird **nicht** von der Runtime erzeugt oder öffentlich freigegeben. Er muss vorher privat vorhanden sein. `Storage Blob Data Contributor` für die App-Identität ist erforderlich; kein Account-Key.
- SQL nutzt `Authentication=Active Directory Managed Identity`, TLS und Zertifikatsprüfung. Production verweigert SQL-Passwörter, Integrated Security und `TrustServerCertificate=True`.
- Das bestehende Serilog-/Datenbanklogging bleibt unverändert. Kein neuer Logging-, Mail- oder kostenpflichtiger Dienst.

Die früheren Schalter `AzureBlobStorage:Enabled` und `BlobConnectionString` werden nicht mehr ausgewertet und sollen aus Deployment-Konfigurationen entfernt werden. `LocalDataRootPath` hat außerhalb Development keinerlei Auswahlwirkung.

## 2. Ressourcen (Nutzerangaben, nicht erneut geprüft)

| Zweck | Angabe |
|---|---|
| Subscription | `43195896-0a8b-4125-906c-776405daee76` |
| Resource Group / Region | `TabloSuiteRGruppe` / France Central |
| App Service | `enote`, Linux, .NET 10, Basic B1, eine Instanz |
| Plan | `ENote-AppServicePlan` |
| System-Identity PrincipalId | `132ec3af-521b-4b81-bb7a-969fc38e35e4` |
| SQL | `tablosuite-sqlserver.database.windows.net` / `ENote-Database` |
| Storage | `tablosuiteblobbtorage` (Schreibweise ausdrücklich beibehalten) |
| Privater Container | `enote-blobuploads` – Existenz noch zu bestätigen |

Angegeben sind: StorageV2, Standard LRS/Hot, HTTPS/TLS 1.2, Shared Key und anonymer Zugriff aus, OAuth bevorzugt, Blob-/Container-Soft-Delete sieben Tage; keine Versionierung/HNS/SFTP/NFS. Die App besitzt laut Nutzer die Blob-Datenrolle auf Account-Ebene. Keine dieser Einstellungen wurde hier ausgelesen oder geändert.

SQL ist laut Nutzer Entra-only; Benutzer `enote` besitzt `db_datareader` und `db_datawriter`, **kein `db_owner` und kein DDL**. Firewall-Ausgangs-IP-Regeln sind laut Nutzer gesetzt; „Allow Azure services“ bleibt aus. Datenbankschema und tatsächlicher Zugang sind noch unbestätigt.

## 3. App-Service-Konfiguration

Folgende Einstellungen erst nach Behebung der Build-/Modellblockaden setzen bzw. prüfen. Es werden hier keine Befehle gegen Azure ausgeführt.

### App Settings

| Name | Wert |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` (HTTPS-Schema hinter dem App-Service-Reverse-Proxy) |
| `Database__ConnectionStringName` | `DefaultConnection` |
| `Database__ApplyMigrationsOnStartup` | `false` |
| `Database__SeedOnly` | `false` oder nicht gesetzt |
| `AzureBlobStorage__ServiceUri` | `https://tablosuiteblobbtorage.blob.core.windows.net` |
| `AzureBlobStorage__ContainerName` | `enote-blobuploads` |
| `DataProtection__ApplicationName` | `eNOTE` – bei Folgeversionen unverändert lassen |
| `DataProtection__KeysPath` | `/home/data/enote/keys` |
| `WEBSITES_ENABLE_APP_SERVICE_STORAGE` | `true`; Persistenz von `/home` sicherstellen |
| `App__PublicBaseUrl` | `https://enote.tablosuite.com` – Basis für alle Links in E-Mails (Passwort, Abonnement, Admin) |
| `Mail__Provider` | `Postmark` |
| `Mail__PostmarkServerToken` | **Geheim** – Postmark → Server → *API Tokens* → „Server API token“. Als App Setting (oder Key-Vault-Referenz), nie ins Repository |
| `Mail__Absender` | `contact@tablosuite.com` (jede Adresse der verifizierten Domain ist möglich) |
| `Mail__AbsenderName` | `eNOTE · TabloSuite` |
| `Mail__AntwortAn` | `contact@tablosuite.com` (Antworten landen im IONOS-Postfach) |
| `Mail__AdminEmpfaenger` | `contact@tablosuite.com` – Hinweise „Nouvel établissement“ und „Paiement signalé“ |
| `Mail__MessageStream` | `outbound` (Standard-Transaktionsstream in Postmark) |

Zusätzlich vor Freigabe am bestehenden App Service prüfen: .NET-10-Linux-Runtime, **Always On**, funktionsfähige WebSocket-Verbindungen für Blazor/SignalR, eine Instanz und HTTPS Only. Bei späterer Skalierung sind Circuits, Sitzungsaﬃnität und gemeinsamer Schlüsselring neu zu planen. `AllowedHosts` auf die eigene Domain und den **tatsächlich bestätigten** Azure-Standardhostname begrenzen; keinen unbestätigten Standardhostname raten. Keine dieser Einstellungen wird durch das lokale Publish-Profil in Azure gesetzt.

Keine `Seed__SuperAdminPasswort`-Konfiguration für den dauerhaften Webbetrieb hinterlegen. Administrative Seed-Zugangsdaten nur im getrennten Einmalprozess bereitstellen.

### Connection String

Entweder die App-Service-**Connection String** `DefaultConnection` (Typ `SQLAzure`) oder die App Setting `ConnectionStrings__DefaultConnection` verwenden, nicht widersprüchlich beide:

```text
Server=tcp:tablosuite-sqlserver.database.windows.net,1433;Database=ENote-Database;Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```

Dieser Wert enthält kein Passwort. Die tatsächliche System-Identity wird vom App-Service-Host bereitgestellt. Kein `User Id`, `Password` oder zusätzliches Client-Secret eintragen.

### Data Protection und Betrieb

Cookies, Identity-Tokens und verschlüsselte PIN-Daten benötigen denselben Schlüsselring über Neustarts/Deployments. Der Code persistiert ihn am konfigurierten Pfad; ohne expliziten Pfad wird in App Service `$HOME/data/enote/keys` genutzt. Ohne absoluten Pfad bzw. HOME bricht der Nicht-Development-Start ab. `wwwroot` ist verboten.

Der Ordner muss persistent und nur für den App-Prozess/administrativ berechtigte Betreiber zugänglich sein. Nicht in ein Publish-Paket aufnehmen, öffentlich bereitstellen, löschen oder bei jedem Release neu erzeugen. Falls bereits produktive geschützte Daten existieren, den bisherigen Schlüsselring und ApplicationName erhalten bzw. kontrolliert übernehmen; neue Schlüssel machen alte Daten nicht entschlüsselbar.

Es wird kein zusätzlicher Key-Vault-/Blob-Schlüsselring-Dienst eingeführt. Die Schlüsseldateien erhalten hier keine zusätzliche XML-Verschlüsselung durch eine App-Zertifikatkonfiguration; Schutz und Verschlüsselung des persistenten App-Service-Speichers sowie restriktiver Betreiberzugriff sind daher Voraussetzung. Ein entsprechender DataProtection-Laufzeithinweis ist nicht durch Warnungsunterdrückung versteckt. Das beschriebene Betriebsmodell gilt für die genannte **eine Instanz**; vor späterer Skalierung oder Slotwechseln Schlüsselring-/Cookie-Verhalten erneut planen.

### E-Mail (Postmark)

1. **Postmark-Konto freischalten lassen:** Neue Konten sind im *Test-/Approval-Modus* und dürfen nur an Adressen der eigenen Domain senden. In Postmark „Request approval“ stellen (kurze Beschreibung: transaktionale Mails einer Kassen-App, keine Werbung). Der Gratis-Plan umfasst 100 Mails/Monat – bei mehreren Läden auf einen bezahlten Plan wechseln.
2. **DNS bei IONOS:** DKIM und Return-Path sind verifiziert (✓). Empfohlen zusätzlich ein DMARC-Eintrag: TXT `_dmarc` = `v=DMARC1; p=none; rua=mailto:contact@tablosuite.com` (später auf `p=quarantine` verschärfen). SPF muss für Postmark nicht geändert werden (Return-Path-Domain übernimmt das); den bestehenden IONOS-SPF/MX nicht anfassen.
3. **App Settings** wie oben setzen und die App neu starten.
4. **Prüfen:** Als SuperAdmin `Administration → E-mails` öffnen: Status muss „Envoi actif via Postmark“ zeigen. Eine Test-Mail an eine externe Adresse (z. B. Gmail) senden und Posteingang/Spam prüfen. Fehler stehen im Historique (Postmark-Fehlercode + Meldung) und im Postmark-Dashboard unter *Activity*.

Verhalten: Jede Mail wird zuerst in `MailNachrichten` gespeichert und sofort versendet; fehlgeschlagene Mails wiederholt der Hintergrunddienst alle 5 Minuten (max. 5 Versuche, 3 Tage). Stündlich prüft er Rechnungen (neue Rechnung, Erinnerung 3 Tage vor der Kassensperre, Sperrhinweis) und das Probe-Ende (7 Tage vorher) – jeweils einmal pro Datensatz. Rechnungen, die beim ersten Einschalten älter als 3 Tage sind, lösen keine Rechnungsmail mehr aus. Ohne Konfiguration bleiben Mails nur in der Queue.

## 4. Private Medien und Rechte

- Alle UI-URLs lauten `/media/{Salon-GUID}/{Datei-GUID.ext}` (bzw. registrierte globale Assets), niemals öffentliche Azure-URLs.
- Der Catch-all-Endpunkt benötigt Anmeldung und prüft das vorhandene `BlobMediaAsset` sowie aktuelle Benutzer-/Ladenrechte in der Datenbank. Dateiprefix, E-Mail oder ursprünglicher Uploader verleihen keinen Zugang.
- Gesperrte/gelöschte Benutzer und inaktive Läden erhalten keinen Zugriff. Zugeordnete Ladenkonten dürfen Ladenbilder lesen. Belege benötigen Finanzrechte bzw. Besitzerrechte.
- SuperAdmin darf registrierte Medien aktiver Läden sowie globale Assets lesen. ProduktOwner benötigen ihre tatsächliche `ProduktOwnerSalon`-Zuordnung; für Belege zusätzlich `KannLadenUmsatzSehen`.
- Uploads prüfen die tatsächliche Besitzer-/delegierte Bearbeitungsberechtigung vor dem Speichern. Lokale Dateinamen lassen keine Pfadtraversierung zu; auch nicht-seekbare Streams sind auf acht MiB begrenzt.
- Antworten verwenden `private, no-store`, `nosniff` und eine Sandbox-CSP. Nur bekannte Rasterbild-Endungen werden inline ausgeliefert; PDF und andere Formate als Download. SVG/HTML werden nicht im App-Ursprung ausgeführt.
- Der Service Worker cached nur die expliziten statischen App-Dateien, keine angemeldeten Dokumente, Belege oder Medien; alte Cache-Versionen werden entfernt.
- Bon-PDF und HTML-Druck prüfen den Laden vor Daten-/Logoabruf. Der PDF-Service lädt private Logo-Bytes serverseitig über `IMediaStorageService`; keine öffentliche Blob-Freigabe nötig.

Development-Medien werden nicht automatisch nach Azure übertragen. Ein späterer expliziter Transfer müsste Dateien **und** passende Datenbank-Asset-Metadaten erhalten; lokale Ordner sind kein Production-Fallback.

## 5. Migrationen und Seed: separater administrativer Einmalprozess

### Praxis-Ablauf (so am 10.11.2026 angewendet)

1. **Stand auf Azure lesen** (nur lesend):
   `SELECT TOP 5 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC` – Verbindung per PowerShell `SqlConnection` mit `AccessToken` aus `az account get-access-token --resource https://database.windows.net/` (Konto mit Entra-Admin-Rechten auf dem SQL-Server).
2. **Skript erzeugen** ab der letzten Azure-Migration bis zur neuesten:
   `dotnet ef migrations script <letzteAzure> <neueste> --project eNOTE/eNOTE.WebApp --configuration Release -o <Temp>/enote-azure-migration.sql`
3. **Skript prüfen:** keine `DROP`/`DELETE`/`UPDATE` ohne fachliche Begründung; neue Tabellen/Spalten sind unkritisch.
4. **Nur nach ausdrücklichem OK des Nutzers** ausführen (Skript blockweise an `GO` trennen und nacheinander ausführen; EF-Skripte enthalten je Migration eine eigene Transaktion), danach Historie und neue Tabellen/Spalten erneut lesend prüfen.
5. **Erst dann deployen.** Neue Seiten, deren Tabellen fehlen, würden sonst auf Azure abstürzen.

**In diesem Auftrag nicht ausführen.** Zuerst den fachlich vollständigen Stand und eine eindeutige Migrationskette herstellen. Insbesondere nicht alte Migrationen plus konkurrierende Initialmigration ungeprüft anwenden. Keine `EnsureDeleted`-/Reset-/automatische Cloud-Initialisierung.

Der normale App-Start führt keine Migrationen und keinen Seed aus. Ein aktivierter alter Startup-Migrationsschalter ohne Administrationsmodus bricht ab, statt still DDL auszuführen.

Die administrativen Modi lauten:

- `--database-admin=migrate`: nur EF-Migrationen;
- `--database-admin=seed`: Rollen, konfigurierten SuperAdmin und bestehende Preis-Seedlogik, **keine DDL**;
- `--database-admin=migrate-and-seed`: beides ausdrücklich;
- immer `--database-target=<exakter DataSource>/<exakter InitialCatalog>`.

`--database-target` muss mit der ausgewählten Verbindungszeichenfolge übereinstimmen, einschließlich `tcp:` und Port, falls dort enthalten. Kein vereinfachter Alias. Ein fehlendes/abweichendes Ziel bricht vor jeder Datenbankaktion ab. Nach Abschluss beendet sich der Prozess ohne Webserver/Workerstart. Fehler liefern einen fehlgeschlagenen Prozess, keinen vorgetäuschten Seed-Erfolg.

### Administrativer Zugang

Migrationen durch einen separat berechtigten Entra-Administrator oder Deployment-Prozess ausführen, **nicht** durch Erweitern des Runtime-Benutzers `enote` auf `db_owner`. Der Einmalprozess kann über `Database__ConnectionStringName=AdminConnection` eine getrennte `ConnectionStrings__AdminConnection` aus sicherer Prozesskonfiguration nutzen. Zum Beispiel eine Entra-Interactive-Verbindung mit den nötigen administrativen Rechten, ohne SQL-Passwort. Diese Konfiguration nicht in den normalen App Service übernehmen. Der Administrationsmodus benötigt keine Blob-Credentials und keinen Runtime-Schlüsselring.

Für einen gezielt bestätigten späteren Lauf mit einer AdminConnection, deren DataSource `tcp:tablosuite-sqlserver.database.windows.net,1433` und deren Datenbank `ENote-Database` lautet, ist die **Form** des Einmalaufrufs:

```powershell
# Nur nach freigegebenem Build, geprüfter Migrationskette und expliziter DB-Freigabe:
# AdminConnection zuvor außerhalb des Repositorys sicher bereitstellen.
dotnet run --project eNOTE/eNOTE.WebApp --no-launch-profile -- --environment Production --Database:ConnectionStringName=AdminConnection --database-admin=migrate --database-target=tcp:tablosuite-sqlserver.database.windows.net,1433/ENote-Database
```

Für Seed den Modus auf `seed` ändern. `Seed:SuperAdminEmail`, `Seed:SuperAdminPasswort` und optional `Seed:SuperAdminBenutzername` ausschließlich über sichere Prozesskonfiguration bereitstellen. Keine Zugangsdaten in Befehle, Protokolle oder Repository schreiben. Ohne Email/Passwort wird kein Admin angelegt. Der Seed sucht den Admin nach Benutzernamen und erweitert ein vorhandenes Nicht-Admin-Konto nicht automatisch zum SuperAdmin. Identity-Fehler werden nicht verschluckt.

Development unterstützt weiterhin `Database:SeedOnly=true`; dabei ist ebenfalls `--database-target=localhost/ENOTEDB` erforderlich, sofern die Verbindung genau diesen Server und Datenbanknamen enthält. `Database:ApplyMigrationsOnStartup=true` zusammen mit Development-SeedOnly entspricht explizitem `migrate-and-seed`; der normale Start migriert auch in Development nicht automatisch.

## 6. Lokale Vorbereitung, keine Veröffentlichung

Das Visual-Studio-Folder-Profil `eNOTE/eNOTE.WebApp/Properties/PublishProfiles/AzureLinuxLokal.pubxml` bereitet ausschließlich lokale Dateien vor:

- Release, `net10.0`, `linux-x64`, frameworkabhängig; der Host benötigt .NET 10.
- Ausgabe in `artifacts/enote-linux/` (bereits durch `.gitignore` ausgeschlossen).
- Kein Azure-Publish-Provider, keine Zugangsdaten, kein Upload und kein Browserstart.
- `appsettings.Development.json` wird nicht veröffentlicht. Produktionsverbindungen gehören weiterhin ausschließlich in sichere Host-Konfiguration; keine Secrets in andere versionierte Settings schreiben.
- Kein Trimming oder Single-File-Publish; native QuestPDF-Abhängigkeiten bleiben im normalen Publish-Layout.

Nach Behebung der Test-Compilerfehler zuerst Lösungsbuild und automatisierte Tests erneut erfolgreich ausführen. Danach kann in Visual Studio das **Folder-Profil** zur lokalen Paketvorbereitung verwendet werden. Es ist kein Azure-Veröffentlichungsprofil. Der aktuelle Auftrag hat dieses Profil nicht ausgeführt. Etiketten-Barcodes nutzen jetzt SVG aus dem vorhandenen ZXing statt Windows-only `System.Drawing`; PDF-Erzeugung und native Bibliotheken müssen trotzdem auf Linux validiert werden.

Kein `az webapp deploy`, kein automatischer DB-Startschritt und keine Veröffentlichung durch diese Anleitung. Die tatsächliche Domainbindung wurde nicht geprüft. Auch die mögliche Standard-Hostname-Angabe aus dem bisherigen Gespräch ist unbestätigt.

## 7. Domain und spätere Freigabe

`tablosuite.com` besteht bereits. Nur die Subdomain `enote` beim bestehenden DNS-Verwalter einrichten, **erst nach Zustimmung**:

1. Vorhandenen App Service, Subscription, Resource Group, Region, Tarif/Kosten und dessen echten Standardhostname bestätigen. Keine zweite App oder neuen Plan vorsorglich anlegen.
2. Privaten Container, Runtime-MI-Zugriff, SQL-Firewall und tatsächliches Schema separat bestätigen; keine DB-Resets. Eine Datenbankänderung benötigt eine eigene Freigabe für das genaue Ziel.
3. Bei Freigabe DNS-CNAME `enote` auf den bestätigten App-Service-Standardhostname und TXT `asuid.enote` auf dessen tatsächliche Domain-Verifizierungs-ID setzen. Keine MX- oder SPF-Einträge ändern (DKIM/Return-Path für Postmark sind bereits gesetzt, DMARC siehe „E-Mail (Postmark)“). Bei IONOS nur dann konfigurieren, wenn IONOS tatsächlich der autoritative DNS-Verwalter ist.
4. Eigene Domain am bestehenden App Service binden und ein unterstütztes App-Service-Managed-Certificate mit SNI/TLS aktivieren; Voraussetzungen und mögliche Kosten vorher prüfen. HTTPS Only und Weiterleitung kontrollieren.
5. Erst nach erfolgreichem Build/Tests, Linux-PDF-/Runtimevalidierung, bestätigter Konfiguration und ausdrücklicher Veröffentlichungsfreigabe das lokale Paket hochladen. Externe Prüfung nur mit gesondertem Auftrag; keinen Browser-Praxistest eigenständig starten.

Für den nächsten externen Schritt fehlen die Bestätigung, dass der unter Abschnitt 2 genannte App Service `enote` wirklich das gewünschte Ziel ist, dessen tatsächlicher Standardhostname, der DNS-Verwalter und eine ausdrückliche Kosten-/Änderungsfreigabe. Keine Secrets, Passwörter oder Publish-Credentials im Chat anfordern. Lokale Vorbereitungsarbeiten sind keine Zustimmung zur Veröffentlichung.
