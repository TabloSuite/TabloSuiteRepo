# TabloSuiteHome – Firmenseite tablosuite.com

Hauptseite der Firma TabloSuite: stellt alle Apps der TabloSuiteMappe vor und führt zu ihnen
(eNOTE → enote.tablosuite.com, SKOULOU → skoulou.tablosuite.com, später weitere).

- Blazor (.NET 10), nur statisches Server-Rendering: keine Datenbank, keine SignalR-Verbindung, schnell auch bei schwachem Netz.
- Sprache der Seite: Französisch (« vous »). Marke wie die TabloSuite-E-Mails: Nachtblau `#172554`, Blau `#2f4ec9`, « T »-Zeichen; Schrift Inter (lokal in `wwwroot/fonts`).
- Bilder der Apps sind echte Aufnahmen aus den Demo-Konten (erfundene Daten).

## Neue App hinzufügen (kein Code nötig)

1. Logo und ein Bildschirmbild (1600×1000, WebP) nach `TabloSuiteHome.WebApp/wwwroot/img/apps/` legen.
2. In `TabloSuiteHome.WebApp/appsettings.json` unter `TabloSuite:Apps` einen Eintrag ergänzen
   (`Id`, `Name`, `Kategorie`, `Beschreibung`, `Punkte`, `Url`, `Registrierung`, `RegistrierungText`, `Hinweis`,
   `Logo`, `Bild`, `BildAlt`, `Farbe`, `Status`, `Reihenfolge`).
   - `Status: "Bientot"` zeigt die App als « Bientôt disponible » (ohne Links, Adresse noch nicht nötig).
   - `Status: "Disponible"` braucht eine `https`-Adresse.
3. `dotnet test TabloSuiteHome/TabloSuiteHome.Tests` – prüft den Katalog (Pflichtfelder, https, doppelte Ids, Bilder vorhanden).
   Beim Start prüft die Seite den Katalog ebenfalls und startet bei Fehlern nicht.

Kontakt (E-Mail, Telefon, Ort) steht ebenfalls in `appsettings.json` unter `TabloSuite`.

## Lokal starten

`dotnet run --project TabloSuiteHome/TabloSuiteHome.WebApp` → https://localhost:7180

## Veröffentlichen (noch offen, nur mit OK)

Eigene Azure-Web-App (z. B. im App-Service-Plan von eNOTE/Skoulou), Zip-Deploy wie bei eNOTE.
Danach Domain: bei IONOS `tablosuite.com` (A-Record/CNAME + TXT `asuid`) auf die Web-App zeigen,
in Azure die Domain hinzufügen und das kostenlose verwaltete Zertifikat binden; `www.tablosuite.com`
leitet die Seite selbst auf `tablosuite.com` um.
