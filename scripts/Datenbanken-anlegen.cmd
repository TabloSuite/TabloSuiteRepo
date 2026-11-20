@echo off
rem TabloSuite - lokale Datenbanken von eNOTE und SKOULOU anlegen bzw. aktualisieren.
rem Doppelklick genuegt. Optional: scripts\Datenbanken-anlegen.cmd -Demo   oder   -Pruefen
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Datenbanken-anlegen.ps1" %*
echo.
pause
