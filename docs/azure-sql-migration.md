# Migration nach Azure SQL Database

Diese Anleitung beschreibt den Umzug der SKOULOU-APP-Datenbank von der lokalen
SQL-Server-Instanz auf eine Azure SQL Database.

## 1. Azure SQL Database anlegen

```powershell
az sql server create --name skoulouapp-sql --resource-group skoulouapp-rg --location westeurope --enable-ad-only-auth --external-admin-name "<Entra-Gruppe>" --external-admin-type Group --external-admin-sid "<Objekt-Id>"
az sql db create --resource-group skoulouapp-rg --server skoulouapp-sql --name SKOULOUAPPDb --service-objective S0 --backup-storage-redundancy Local
```

Firewall für Azure-Dienste und den eigenen Client freigeben:

```powershell
az sql server firewall-rule create --resource-group skoulouapp-rg --server skoulouapp-sql --name AllowAzure --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0
az sql server firewall-rule create --resource-group skoulouapp-rg --server skoulouapp-sql --name MeinClient --start-ip-address <eigene-IP> --end-ip-address <eigene-IP>
```

## 2. Verbindungszeichenfolge konfigurieren

Die Anwendung wählt die Verbindung über `Database:ConnectionStringName`. Für Azure
wird der Name **`AzureDefaultConnection`** verwendet.

In `appsettings.json` steht dort bewusst noch die lokale Verbindung als Fallback.
Der Azure-Wert wird zur Laufzeit überschrieben, damit keine Zugangsdaten im
Repository landen.

### Empfohlen: Managed Identity (ohne Passwort)

```
Server=tcp:skoulouapp-sql.database.windows.net,1433;Database=SKOULOUAPPDb;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```

### Alternative: SQL-Benutzer

```
Server=tcp:skoulouapp-sql.database.windows.net,1433;Database=SKOULOUAPPDb;User ID=<benutzer>;Password=<kennwort>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```

Wert lokal per User Secrets setzen (nicht in `appsettings.json` eintragen):

```powershell
dotnet user-secrets set "ConnectionStrings:AzureDefaultConnection" "<verbindungszeichenfolge>" --project SkoulouApp.WebApp
dotnet user-secrets set "Database:ConnectionStringName" "AzureDefaultConnection" --project SkoulouApp.WebApp
```

In Azure App Service stattdessen als App-Einstellung hinterlegen:

```powershell
az webapp config appsettings set --resource-group skoulouapp-rg --name skoulouapp-web --settings "Database__ConnectionStringName=AzureDefaultConnection"
az webapp config connection-string set --resource-group skoulouapp-rg --name skoulouapp-web --connection-string-type SQLAzure --settings AzureDefaultConnection="<verbindungszeichenfolge>"
```

Bei Managed Identity muss die Identität einmalig in der Datenbank berechtigt werden:

```sql
CREATE USER [skoulouapp-web] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [skoulouapp-web];
ALTER ROLE db_datawriter ADD MEMBER [skoulouapp-web];
ALTER ROLE db_ddladmin ADD MEMBER [skoulouapp-web];
```

`db_ddladmin` wird nur benötigt, wenn die Migrationen beim Start automatisch
ausgeführt werden sollen (siehe Abschnitt 3).

## 3. Schema ausrollen

Der Initializer ruft bereits `Database.MigrateAsync()` auf, das Schema entsteht
also beim ersten Start automatisch. Für Produktivumgebungen ist ein
kontrollierter Rollout über ein Skript vorzuziehen:

```powershell
dotnet ef migrations script --idempotent --project SkoulouApp.WebApp --startup-project SkoulouApp.WebApp --output azure-schema.sql
```

Das idempotente Skript kann gefahrlos mehrfach ausgeführt werden. Alternativ direkt anwenden:

```powershell
$env:ConnectionStrings__AzureDefaultConnection = "<verbindungszeichenfolge>"
$env:Database__ConnectionStringName = "AzureDefaultConnection"
dotnet ef database update --project SkoulouApp.WebApp --startup-project SkoulouApp.WebApp
```

## 4. Bestandsdaten übernehmen (optional)

Nur nötig, wenn vorhandene Daten aus der lokalen Datenbank übernommen werden
sollen. Empfohlen ist der BACPAC-Weg:

```powershell
SqlPackage /Action:Export /SourceConnectionString:"Server=localhost;Database=SKOULOUAPPDb;Trusted_Connection=True;TrustServerCertificate=True" /TargetFile:"SKOULOUAPPDb.bacpac"
SqlPackage /Action:Import /TargetConnectionString:"<azure-verbindungszeichenfolge>" /SourceFile:"SKOULOUAPPDb.bacpac"
```

Danach Abschnitt 3 ausführen, damit fehlende Migrationen nachgezogen werden.

## 5. Was im Code bereits vorbereitet ist

- `Program.cs` aktiviert `EnableRetryOnFailure` – Azure SQL trennt Verbindungen
  bei Failover, Wartung und Throttling; ohne Wiederholungslogik führt das zu
  Laufzeitfehlern.
- Das Befehls-Timeout ist auf 60 Sekunden erhöht, da Azure SQL unter Last
  langsamer antwortet als eine lokale Instanz.
- `DashboardService.HardDeleteSchoolLeaderForDevelopmentAsync` kapselt seine
  Transaktion in eine `ExecutionStrategy`. Das ist zwingend, weil EF Core
  benutzerdefinierte Transaktionen bei aktiver Wiederholungslogik sonst mit einer
  `InvalidOperationException` ablehnt.

## 6. Nach dem Umzug prüfen

- Anmeldung und Registrierung funktionieren.
- Der Kurskatalog wird angezeigt (Seed lief durch).
- Die CECR-Gruppen sind gepflegt: `SELECT Code, CEFRGroup FROM Klassen;`
- Ein Schüler sieht nur die Niveaus seiner CECR-Gruppe (z. B. nur A1.1 und A1.2
  bei CECR "A1").
