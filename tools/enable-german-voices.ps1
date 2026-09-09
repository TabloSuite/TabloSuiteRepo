# =====================================================================
#  Deutsche OneCore-Stimmen (Katja, Stefan) fuer System.Speech (SAPI)
#  freischalten.
#
#  ANLEITUNG:
#  1. Rechtsklick auf diese Datei > "Mit PowerShell ausfuehren"
#     ODER PowerShell als Administrator oeffnen und Skript starten.
#  2. Falls Ausfuehrung blockiert wird, vorher einmalig:
#     Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
#
#  Danach stehen "Microsoft Katja" und "Microsoft Stefan" auch
#  in System.Speech (und damit in der Audio-Generierung) bereit.
# =====================================================================

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
	Write-Host "Dieses Skript muss als Administrator ausgefuehrt werden." -ForegroundColor Red
	Write-Host "Rechtsklick auf PowerShell > 'Als Administrator ausfuehren' und erneut starten."
	exit 1
}

$voices = @('MSTTS_V110_deDE_KatjaM', 'MSTTS_V110_deDE_StefanM')
$srcBase = 'HKLM\SOFTWARE\Microsoft\Speech_OneCore\Voices\Tokens'
$dstBase = 'HKLM\SOFTWARE\Microsoft\Speech\Voices\Tokens'

foreach ($v in $voices) {
	Write-Host "Kopiere $v ..." -ForegroundColor Cyan
	reg copy "$srcBase\$v" "$dstBase\$v" /s /f
	if ($LASTEXITCODE -eq 0) {
		Write-Host "  OK: $v freigeschaltet." -ForegroundColor Green
	} else {
		Write-Host "  FEHLER bei $v (ExitCode $LASTEXITCODE)." -ForegroundColor Red
	}
}

Write-Host ""
Write-Host "Fertig. Verfuegbare Stimmen laut System.Speech:" -ForegroundColor Yellow
Add-Type -AssemblyName System.Speech
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
$s.GetInstalledVoices() | ForEach-Object {
	$info = $_.VoiceInfo
	"  $($info.Name) | $($info.Culture) | $($info.Gender)"
}
$s.Dispose()
