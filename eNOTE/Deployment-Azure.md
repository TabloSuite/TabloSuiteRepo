# eNOTE – sichere Azure-Deployment-Vorbereitung

## Status und Grenzen

Die Infrastruktur ist im Quellcode umgesetzt. Dies ist **keine Freigabe zur Veröffentlichung**: Der aktuelle Arbeitsstand enthält unabhängige Compiler- und Geschäftsmodellinkonsistenzen. Insbesondere fehlen `AboIntervall.SechsMonate` für die vorhandenen Vertragsregeln sowie passende Registrierungs-/Benutzermodellteile. Alte Migrationen und die unversionierte `20261105144750_InitialNeustart` koexistieren; keine davon ungeprüft gegen Azure ausführen. Die Infrastrukturänderung benötigt selbst keine neue EF-Migration.

Es wurden keine Azure-Ressourcen verändert, keine Datenbank migriert/befüllt und keine Secrets gelesen. Angaben zu Ressourcen und Berechtigungen unten stammen vom Nutzer, nicht aus einer aktuellen Cloudprüfung.

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

Nach Wiederherstellung des vollständigen fachlichen Arbeitsstands:

```powershell
dotnet restore eNOTE/eNOTE.Tests/eNOTE.Tests.csproj
dotnet test eNOTE/eNOTE.Tests/eNOTE.Tests.csproj --no-restore
dotnet build SKOULOUAPPMAPPE.slnx --no-restore
dotnet publish eNOTE/eNOTE.WebApp/eNOTE.WebApp.csproj -c Release --no-restore -o <lokaler-Ausgabeordner>
```

Kein `az webapp deploy`, kein automatischer DB-Startschritt und keine Veröffentlichung durch diese Anleitung. Linux-Deployment vor Freigabe außerdem hinsichtlich nativer PDF-/Grafikabhängigkeiten und realem App-Service-Start prüfen; ein erfolgreicher Windows-Build allein belegt das nicht. Die Domain `enote.tablosuite.com` ist noch nicht eingerichtet. Auch die mögliche Standard-Hostname-Angabe aus dem bisherigen Gespräch ist unbestätigt.

Vor späterem Deployment offen: vollständiger aktueller Build/Tests, konsistente Fachmodelle und Migrationskette, tatsächlicher SQL-Schemastand, privater Container, Runtime-MI-Zugang und Firewall, persistenter privater Schlüsselpfad, HTTPS/Forwarded-Headers, PDF-Betrieb unter Linux und Domainbindung. Kein Browser-Praxistest wird hier eigenständig gestartet.
