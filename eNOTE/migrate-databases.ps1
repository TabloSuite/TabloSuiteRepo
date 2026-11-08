# Migriert LokalDB und/oder Azure SQL Database auf Wunsch
# Verwendung:
#   .\migrate-databases.ps1 -Local        # Nur lokale DB
#   .\migrate-databases.ps1 -Azure        # Nur Azure DB
#   .\migrate-databases.ps1 -Both         # Beide

param(
	[switch]$Local,
	[switch]$Azure,
	[switch]$Both,
	[switch]$Seed  # Optional: auch Seed ausführen
)

$ErrorActionPreference = "Stop"
$projectPath = Join-Path $PSScriptRoot "eNOTE.WebApp"

if (-not $Local -and -not $Azure -and -not $Both) {
	Write-Host "Bitte Parameter angeben: -Local, -Azure oder -Both" -ForegroundColor Yellow
	Write-Host "Beispiel: .\migrate-databases.ps1 -Both" -ForegroundColor Cyan
	exit 1
}

if ($Both) {
	$Local = $true
	$Azure = $true
}

# ==================== LOKAL ====================
if ($Local) {
	Write-Host "`n=== LOKALE DATENBANK ===" -ForegroundColor Green
	Write-Host "Ziel: localhost / ENOTEDB" -ForegroundColor Cyan

	$confirm = Read-Host "Lokale DB migrieren? (j/n)"
	if ($confirm -ne "j") {
		Write-Host "Übersprungen." -ForegroundColor Yellow
	} else {
		$mode = if ($Seed) { "migrate-and-seed" } else { "migrate" }
		Write-Host "Führe Migration aus..." -ForegroundColor Cyan

		dotnet run --project $projectPath --no-launch-profile -- `
			--environment Development `
			--database-admin=$mode `
			--database-target=localhost/ENOTEDB

		if ($LASTEXITCODE -eq 0) {
			Write-Host "✅ Lokale DB erfolgreich migriert." -ForegroundColor Green
		} else {
			Write-Host "❌ Fehler bei lokaler Migration." -ForegroundColor Red
		}
	}
}

# ==================== AZURE ====================
if ($Azure) {
	Write-Host "`n=== AZURE DATENBANK ===" -ForegroundColor Green
	Write-Host "Ziel: tablosuite-sqlserver.database.windows.net / ENote-Database" -ForegroundColor Cyan

	# AdminConnection aus User Secrets lesen
	$adminConn = dotnet user-secrets list --project $projectPath 2>$null | Select-String "ConnectionStrings:AdminConnection"

	if (-not $adminConn) {
		Write-Host "⚠️  AdminConnection nicht in User Secrets gefunden." -ForegroundColor Yellow
		Write-Host "Bitte einmalig setzen:" -ForegroundColor Cyan
		Write-Host '  dotnet user-secrets set "ConnectionStrings:AdminConnection" "Server=tcp:tablosuite-sqlserver.database.windows.net,1433;Database=ENote-Database;Authentication=Active Directory Interactive;Encrypt=True;TrustServerCertificate=False;"' -ForegroundColor White
		exit 1
	}

	$confirm = Read-Host "Azure DB migrieren? (j/n)"
	if ($confirm -ne "j") {
		Write-Host "Übersprungen." -ForegroundColor Yellow
	} else {
		$mode = if ($Seed) { "migrate-and-seed" } else { "migrate" }
		Write-Host "Führe Migration aus (Browser-Anmeldung erforderlich)..." -ForegroundColor Cyan

		dotnet run --project $projectPath --no-launch-profile -- `
			--environment Production `
			--Database:ConnectionStringName=AdminConnection `
			--database-admin=$mode `
			--database-target=tcp:tablosuite-sqlserver.database.windows.net,1433/ENote-Database

		if ($LASTEXITCODE -eq 0) {
			Write-Host "✅ Azure DB erfolgreich migriert." -ForegroundColor Green
		} else {
			Write-Host "❌ Fehler bei Azure Migration." -ForegroundColor Red
		}
	}
}

Write-Host "`n=== FERTIG ===" -ForegroundColor Green
