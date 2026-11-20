<#
  TabloSuite – lokale Datenbanken von eNOTE und SKOULOU anlegen bzw. aktualisieren.

  Start:  Doppelklick auf « scripts\Datenbanken-anlegen.cmd »
  oder:   powershell -ExecutionPolicy Bypass -File scripts\Datenbanken-anlegen.ps1 [-Demo] [-Pruefen] [-Projektordner <Pfad>]

  Voraussetzung: SQL Server lokal (Standard: localhost, Windows-Anmeldung) und die Seed-Anmeldedaten in den
  User-Secrets (Seed:SuperAdminEmail/-Passwort und bei eNOTE -Benutzername, bei SKOULOU -Identifiant).

  -Demo     legt zusätzlich die eNOTE-Demo-Daten an (Beispiel-Salons)
  -Pruefen  zeigt nur an, was passieren würde – ändert nichts

  Ändert NUR lokale Datenbanken (Umgebung Development). Darf beliebig oft laufen:
  vorhandene Daten und Konten bleiben erhalten, es kommen nur fehlende Tabellen/Grunddaten dazu.
  Die Verbindung kommt wie in den Apps aus User-Secrets bzw. appsettings.Development.json.
#>
param(
    [string]$Projektordner,
    [switch]$Demo,
    [switch]$Pruefen,
    [string]$EnoteVerbindung,     # nur für Sonderfälle; sonst aus der App-Konfiguration
    [string]$SkoulouVerbindung
)

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'

function Schritt([string]$text) { Write-Host ''; Write-Host "== $text" -ForegroundColor Cyan }
function Ok([string]$text) { Write-Host "   $text" -ForegroundColor Green }
function Hinweis([string]$text) { Write-Host "   $text" -ForegroundColor Yellow }
function Abbruch([string]$text) { Write-Host ''; Write-Host "FEHLER: $text" -ForegroundColor Red; exit 1 }

# --- 1. Projektordner finden (Ordner mit TabloSuiteMappe.slnx) ---------------------------------
function Finde-Projektordner {
    $kandidaten = @()
    if ($Projektordner) { $kandidaten += $Projektordner }
    # aktueller Ordner und Skript-Ordner samt übergeordneten Ordnern (Skript darf im Projekt liegen, z. B. in scripts\)
    foreach ($start in @((Get-Location).Path, $PSScriptRoot)) {
        $ort = $start
        while ($ort) { $kandidaten += $ort; $ort = Split-Path $ort -Parent }
    }
    # Skript liegt neben dem Projektordner (z. B. TabloSuiteData\Infos neben TabloSuiteData\TabloSuiteProjekt)
    if ($PSScriptRoot) { $kandidaten += (Join-Path (Split-Path $PSScriptRoot -Parent) 'TabloSuiteProjekt') }
    foreach ($k in $kandidaten) {
        if ($k -and (Test-Path (Join-Path $k 'TabloSuiteMappe.slnx'))) { return (Resolve-Path $k).Path }
    }
    $eingabe = Read-Host 'Projektordner nicht gefunden. Bitte den Pfad eingeben (Ordner mit TabloSuiteMappe.slnx)'
    if ($eingabe -and (Test-Path (Join-Path $eingabe.Trim('"') 'TabloSuiteMappe.slnx'))) { return (Resolve-Path $eingabe.Trim('"')).Path }
    Abbruch 'In diesem Ordner liegt keine TabloSuiteMappe.slnx.'
}

# --- Hilfsfunktionen ---------------------------------------------------------------------------
function Lies-Secrets([string]$projekt) {
    $werte = @{}
    $zeilen = & dotnet user-secrets list --project $projekt 2>$null
    foreach ($z in $zeilen) {
        if ($z -match '^\s*([^=]+?)\s*=\s*(.*)$') { $werte[$Matches[1]] = $Matches[2] }
    }
    return $werte
}

function Verbindung([string]$projekt, $secrets, [string]$vorgabe) {
    if ($vorgabe) { return $vorgabe }
    if ($env:ConnectionStrings__DefaultConnection) { return $env:ConnectionStrings__DefaultConnection }
    if ($secrets.ContainsKey('ConnectionStrings:DefaultConnection') -and $secrets['ConnectionStrings:DefaultConnection']) {
        return $secrets['ConnectionStrings:DefaultConnection']
    }
    $datei = Join-Path $projekt 'appsettings.Development.json'
    $json = Get-Content -Raw -Encoding UTF8 $datei | ConvertFrom-Json
    return $json.ConnectionStrings.DefaultConnection
}

function Ziel([string]$verbindung) {
    $server = ''; $db = ''
    if ($verbindung -match '(?i)(?:^|;)\s*(?:Server|Data Source)\s*=\s*([^;]+)') { $server = $Matches[1].Trim() }
    if ($verbindung -match '(?i)(?:^|;)\s*(?:Database|Initial Catalog)\s*=\s*([^;]+)') { $db = $Matches[1].Trim() }
    if (-not $server -or -not $db) { Abbruch "Verbindung ohne Server oder Datenbank: $verbindung" }
    if ($server -match '(?i)database\.windows\.net') { Abbruch "Das ist eine Azure-Datenbank ($server). Dieses Skript ist nur für lokale Datenbanken." }
    return "$server/$db"
}

function Pruefe-Seed([string]$app, [string]$projekt, $secrets, [string[]]$schluessel) {
    $fehlt = @($schluessel | Where-Object { -not $secrets.ContainsKey($_) -or -not $secrets[$_] })
    if ($fehlt.Count -gt 0) {
        Hinweis "$app : Es fehlen User-Secrets – ohne sie legt die App beim Start kein Admin-Konto an. Eintragen mit:"
        $relativ = $projekt.Substring($wurzel.Length).TrimStart('\')
        foreach ($f in $fehlt) { Hinweis "        dotnet user-secrets set `"$f`" `"WERT`" --project $relativ" }
        return $null
    }
    $name = $secrets[$schluessel[1]]
    Ok "$app : Seed-Anmeldedaten vorhanden (Anmeldung mit « $name »)."
    return $name
}

function Mit-Verbindung([string]$verbindung, [scriptblock]$aktion) {
    $vorher = $env:ConnectionStrings__DefaultConnection
    $env:ConnectionStrings__DefaultConnection = $verbindung
    try { & $aktion } finally { $env:ConnectionStrings__DefaultConnection = $vorher }
}

# eNOTE protokolliert jeden SQL-Befehl (Serilog, fest « Information »). Deshalb: alles in eine Protokolldatei,
# am Bildschirm nur Fehler und die Zusammenfassung; bei einem Fehler die letzten Zeilen.
function Enote-Admin([string]$modus, [string]$log, [switch]$OhneBuild) {
    $argumente = @('run', '--project', $enote)
    if ($OhneBuild) { $argumente += '--no-build' }
    $argumente += @('--', "--database-admin=$modus", "--database-target=$enoteZiel")
    Mit-Verbindung $enoteVerb { & dotnet @argumente 2>&1 | Out-File -FilePath $log -Encoding utf8 }
    $code = $LASTEXITCODE
    $zeilen = @(Get-Content $log -Encoding UTF8)
    $zeilen | Where-Object { $_ -match '\[\d\d:\d\d:\d\d (ERR|FTL)\]|error CS|Unhandled exception|Demo-Laden|Besitzer:|Kassierer:|Admin:|PO:' } |
        ForEach-Object { Write-Host "   $_" }
    if ($code -ne 0) {
        Write-Host ''; Write-Host '   Letzte Zeilen des Protokolls:' -ForegroundColor Yellow
        $zeilen | Select-Object -Last 25 | ForEach-Object { Write-Host "   $_" }
        Hinweis "Vollständiges Protokoll: $log"
    }
    return $code
}

# --- 2. Vorbereitung ---------------------------------------------------------------------------
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Abbruch '.NET SDK (dotnet) nicht gefunden. Ist Visual Studio 2026 installiert?'
}
$wurzel = Finde-Projektordner
$enote = Join-Path $wurzel 'eNOTE\eNOTE.WebApp'
$skoulou = Join-Path $wurzel 'Skoulou\Skoulou.WebApp'
Schritt "Projektordner: $wurzel"

$enoteSecrets = Lies-Secrets $enote
$skoulouSecrets = Lies-Secrets $skoulou
$enoteVerb = Verbindung $enote $enoteSecrets $EnoteVerbindung
$skoulouVerb = Verbindung $skoulou $skoulouSecrets $SkoulouVerbindung
$enoteZiel = Ziel $enoteVerb
$skoulouZiel = Ziel $skoulouVerb
Ok "eNOTE   -> $enoteZiel"
Ok "SKOULOU -> $skoulouZiel"

Schritt 'Seed-Anmeldedaten (User-Secrets)'
$enoteName = Pruefe-Seed 'eNOTE  ' $enote $enoteSecrets @('Seed:SuperAdminEmail', 'Seed:SuperAdminBenutzername', 'Seed:SuperAdminPasswort')
$skoulouName = Pruefe-Seed 'SKOULOU' $skoulou $skoulouSecrets @('Seed:SuperAdminEmail', 'Seed:SuperAdminIdentifiant', 'Seed:SuperAdminPasswort')

if ($Pruefen) {
    Schritt 'Nur geprüft (-Pruefen) – es wurde nichts geändert.'
    exit 0
}

# --- 3. eNOTE ----------------------------------------------------------------------------------
Schritt "eNOTE: Datenbank $enoteZiel anlegen/aktualisieren (Tabellen, Rollen, Grunddaten)"
Hinweis 'Beim ersten Mal wird eNOTE zuerst gebaut – das kann 1–2 Minuten dauern …'
$code = Enote-Admin 'migrate-and-seed' (Join-Path $env:TEMP 'TabloSuite-eNOTE-Datenbank.log')
if ($code -ne 0) {
    Abbruch 'eNOTE-Datenbank konnte nicht angelegt werden (Meldungen oben). Läuft der SQL Server? Ist eNOTE in Visual Studio noch gestartet? Dann zuerst beenden.'
}
Ok 'eNOTE-Datenbank ist bereit.'

if ($Demo) {
    Schritt 'eNOTE: Demo-Daten anlegen'
    $code = Enote-Admin 'demo' (Join-Path $env:TEMP 'TabloSuite-eNOTE-Demo.log') -OhneBuild
    if ($code -ne 0) { Abbruch 'eNOTE-Demo-Daten konnten nicht angelegt werden (Meldungen oben).' }
    Ok 'eNOTE-Demo-Daten sind angelegt.'
}

# --- 4. SKOULOU --------------------------------------------------------------------------------
Schritt 'SKOULOU: Werkzeug dotnet-ef prüfen'
& dotnet ef --version *> $null
if ($LASTEXITCODE -ne 0) {
    Hinweis 'dotnet-ef fehlt – wird einmalig installiert …'
    & dotnet tool install --global dotnet-ef
    if ($LASTEXITCODE -ne 0) { Abbruch 'dotnet-ef konnte nicht installiert werden (Meldungen oben).' }
    $env:PATH = "$env:PATH;$env:USERPROFILE\.dotnet\tools"
}
Ok 'dotnet-ef ist da.'

Schritt "SKOULOU: Datenbank $skoulouZiel anlegen/aktualisieren (Tabellen)"
# SKOULOU baut den Kontext für « dotnet ef » ohne App-Konfiguration; das Ziel kommt deshalb immer per --connection.
& dotnet ef database update --project $skoulou --connection $skoulouVerb
if ($LASTEXITCODE -ne 0) {
    Abbruch 'SKOULOU-Datenbank konnte nicht angelegt werden (Meldungen oben). Läuft der SQL Server? Ist SKOULOU in Visual Studio noch gestartet? Dann zuerst beenden.'
}
Ok 'SKOULOU-Datenbank ist bereit. Rollen, Grundeinstellungen und Admin legt SKOULOU beim ersten Start selbst an.'

# --- 5. Fertig ---------------------------------------------------------------------------------
Schritt 'Fertig'
Ok 'Jetzt in Visual Studio eNOTE.WebApp bzw. Skoulou.WebApp starten (F5).'
if ($enoteName) { Ok "eNOTE   https://localhost:62148  – Anmeldung: $enoteName" }
if ($skoulouName) { Ok "SKOULOU https://localhost:7164   – Anmeldung: $skoulouName" }
exit 0
