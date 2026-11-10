# UI-Rundgang (Screenshots)

Automatischer Screenshot-Rundgang durch eNOTE für die visuelle Prüfung von Design-Änderungen.

1. Demo-Daten anlegen (nur Development, setzt den Demo-Laden komplett zurück):
   `ASPNETCORE_ENVIRONMENT=Development dotnet run --project eNOTE/eNOTE.WebApp --no-launch-profile -- --database-admin=demo --database-target=localhost/ENOTEDB`
2. App starten (https://localhost:62148).
3. Einmalig `npm install` in diesem Ordner, dann `node shots.mjs <ausgabeordner> [tablet|desktop|mobile]`.

Demo-Zugänge: `demo.proprio` / `demo2026` (Besitzer-PIN 1234), Kassierer `demo.awa` (PIN 2222) und `demo.kofi` (PIN 3333), lokaler Admin `demo.admin` / `demo2026`.
Der Rundgang öffnet eine Kassensitzung im Demo-Laden; Schritt 1 setzt das jederzeit zurück.
