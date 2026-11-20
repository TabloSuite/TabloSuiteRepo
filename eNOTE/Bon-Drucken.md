# Bondruck ohne lokale Zusatzsoftware

## Aktueller Stand

QZ Tray und die zugehörige ESC/POS-Integration sind entfernt. „Reçu“ öffnet den bestehenden HTML-Bon und ruft den Browserdruck auf. QR-Code und PDF-Endpunkt bleiben erhalten. Es wird keine Drucker-Erkennung oder erfolgreiche physische Ausgabe vorgetäuscht.

## Windows: dialoglosen Browserdruck vorbereiten

1. Einen kompatiblen 80-mm-Bondrucker mit seinem Windows-Treiber installieren. USB oder Netzwerk sind möglich. Eine Windows-Testseite drucken.
2. Den Bondrucker als Standarddrucker festlegen und im Treiber das passende Rollenformat einstellen. Automatische Standarddruckerverwaltung von Windows gegebenenfalls deaktivieren.
3. Vom Repository aus das separate Kassenprofil zunächst zur Einrichtung starten:

   `powershell.exe -File .\eNOTE\Start-Kasse.ps1 -Browser Edge -Einrichten`

   Für Chrome `-Browser Chrome` verwenden. Eine veröffentlichte Kassenadresse mit `-Url https://meine-domain/caisse` übergeben. Die voreingestellte localhost-Adresse gilt nur auf dem Entwicklungs-PC; sie ist keine Tablet-Adresse.
4. Anmelden, einen Bon öffnen und im Druckdialog den echten Bondrucker, Rollenformat, geeignete Ränder und Skalierung auswählen. Browser-Kopf-/Fußzeilen ausschalten. Einen Testbon drucken. „Als PDF speichern“ ist kein physischer Bondrucker.
5. Alle Fenster dieses separaten Kassenprofils schließen. Anschließend ohne `-Einrichten` starten:

   `powershell.exe -File .\eNOTE\Start-Kasse.ps1 -Browser Edge`

   Der Starter aktiviert `--kiosk-printing`. Im unterstützten Desktop-Browser wird der Druckdialog damit übersprungen; Ziel und Format sind vorher im selben Profil einzurichten. Das Skript startet weder den Server noch ändert es Windows-Druckereinstellungen. Falls die lokale PowerShell-Richtlinie das Skript blockiert, die organisationskonforme Freigabe verwenden; keine Sicherheitsrichtlinien pauschal deaktivieren.

Der aktuelle Browserdruck öffnet weiterhin einen Bon-Tab. `--kiosk-printing` unterdrückt den Druckdialog, nicht zwingend das sichtbare Öffnen dieses Tabs. Im normalen Browser bleibt der Druckdialog bestehen. Eine vollständige unsichtbare, geräteübergreifende Ausgabe wird damit nicht zugesichert. Ohne angeschlossenen Drucker ist die physische Ausgabe nicht testbar.

## Android und Druckerkauf

Normales Android-Chrome bietet keinen entsprechenden Desktop-Kioskparameter. Der bestehende Weg verwendet dort den Systemdruckdialog und benötigt einen kompatiblen Druckdienst/Treiber des Geräts. Dialogloser Direktdruck ohne Zusatzsoftware benötigt eine zum Browser passende Druckerschnittstelle.

Empfehlung vor dem Kauf: 80-mm-Thermodrucker mit USB plus LAN/WLAN. Für Android-Direktdruck zusätzlich beim konkreten Modell eine dokumentierte Web-Druckschnittstelle prüfen, beispielsweise kompatible Epson-ePOS-Print-Unterstützung. HTTPS, Zertifikate, erlaubte Browser-Ursprünge und lokale Netzwerkzugriffe müssen zum Einsatz passen. WLAN und ESC/POS allein garantieren keinen Browser-Direktdruck. Erst nach Wahl eines konkreten Modells dessen Integration umsetzen; keine beliebigen Netzwerkdrucker über einen offenen Server-Druckproxy ansprechen.

## Empfehlung für die Läden (20.11.2026, Lernvideo « Scanner et imprimantes », Administrateur)

- **Scanner: immer 2D** (liest auch alle Strichcodes). 1D/CCD/Laser liest EAN-13, interne Codes (EAN-13 « 2… ») und
  Code-128-Etiketten, aber **keine QR-Codes** – eNOTE nimmt Hersteller-QR an und druckt Codes > 40 Zeichen als QR
  (`BarcodeRegeln.QrAbLaenge`). Tastatur-Modus (HID), Enter am Ende, USB oder Funk/Bluetooth, auf die Tastatursprache des
  Geräts (AZERTY) einstellen.
- **Bondrucker: 80 mm Thermo**, aber entscheidend ist das Kassengerät, weil eNOTE über den Browser druckt:
  Windows-PC → USB + Windows-Treiber (einfach, günstig); iPhone/iPad → nur **AirPrint**-zertifizierte Bondrucker (selten,
  teurer, z. B. Star Micronics TSP654II AirPrint); Android → Druckdienst-App des Herstellers nötig (vor dem Kauf prüfen).
  Billige ESC/POS-Drucker ohne AirPrint drucken nicht vom iPhone. Vor Sammelkäufen immer mit dem echten Gerät testen.
- **Etiketten:** A4-Bogen **70 × 37 mm, 24 je Bogen (3 × 8)** – `EtikettPdfService.ErzeugeA4Bogen` ist exakt darauf
  ausgerichtet (Nutzerentscheidung 20.11.2026), Druck in « Taille réelle » (100 %); oder Rolle 62 × 29 mm am
  Thermo-Etikettendrucker.

## Barcode-Scanner

Kein Treiber und keine Zusatzsoftware nötig: USB- oder Bluetooth-Handscanner im **Tastatur-Modus (HID)** mit **Enter als Abschluss** (Werkseinstellung bei fast allen Geräten). Die Kasse erkennt die schnelle Ziffernfolge überall (`enoteScanner` in `wwwroot/js/enote.js`), auch ohne Fokus im Suchfeld. Wichtig: Nutzt PC/Tablet eine französische Tastatur (AZERTY), den Scanner ebenfalls auf AZERTY stellen (Konfigurationscode aus der Anleitung), sonst kommen Sonderzeichen statt Ziffern an. Mit echter Hardware noch nicht getestet (Stand 10.11.2026, simuliert per Playwright).
