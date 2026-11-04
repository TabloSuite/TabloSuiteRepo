param(
	[uri]$Url = 'https://localhost:62148/caisse',
	[ValidateSet('Edge', 'Chrome')]
	[string]$Browser = 'Edge',
	[switch]$Einrichten
)

$ErrorActionPreference = 'Stop'
if ($Url.Scheme -notin @('http', 'https')) {
	throw 'Die Kassenadresse muss eine HTTP- oder HTTPS-Adresse sein.'
}

$relativerPfad = if ($Browser -eq 'Edge') { 'Microsoft\Edge\Application\msedge.exe' } else { 'Google\Chrome\Application\chrome.exe' }
$wurzeln = @(${env:ProgramFiles(x86)}, $env:ProgramFiles, $env:LOCALAPPDATA) | Where-Object { $_ }
$programm = $wurzeln | ForEach-Object { Join-Path $_ $relativerPfad } | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $programm) {
	throw "$Browser wurde nicht gefunden. Bitte einen installierten Browser mit -Browser Edge oder -Browser Chrome auswählen."
}

$profil = Join-Path $env:LOCALAPPDATA "eNOTE\KassenBrowser-$Browser"
$argumente = @("--user-data-dir=`"$profil`"", '--no-first-run')
if (-not $Einrichten) {
	$argumente += '--kiosk-printing'
}
$argumente += "`"$($Url.AbsoluteUri)`""

Write-Host 'Vor einem Wechsel zwischen Einrichtung und Direktdruck alle Fenster dieses Kassenprofils schließen.'
Write-Host 'Der Starter startet nur den Browser, nicht den Webserver.'
Start-Process -FilePath $programm -ArgumentList $argumente
