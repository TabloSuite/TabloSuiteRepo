# eNOTE – Projektübersicht & Entwicklungsbeschreibung

> Kassen- und Verwaltungs-App für kleine Geschäfte (Salon, Boutique, Atelier …), optimiert für Tablet und PC. Französische Endnutzertexte, deutsche Entwicklerdoku.

## Aktueller Infrastrukturstand

Deployment-Konfiguration, private Medienrechte und der getrennte Administrationspfad stehen in [Deployment-Azure.md](Deployment-Azure.md). Development speichert Medien lokal außerhalb von `wwwroot`; Production ausschließlich per Managed Identity in privaten Blobs, ohne lokalen Fallback. Der normale App-Start migriert und seedet nicht.

Aktuelle Hosting-Prüfung: `eNOTE.WebApp` baut erfolgreich; der Lösungsbuild scheitert an 73 Compilerfehlern in bestehenden Tests mit veralteten Service-Aufrufen. Der Testlauf wurde deshalb abgebrochen (0 Tests ausgeführt). Konkurrierende Migrationsdateien bleiben vor Datenbankaktionen zu klären. Die fachlichen Bestandsbeschreibungen unten sind kein Nachweis einer abgeschlossenen Ein-Geschäft-/Pseudonym-/PIN-Umstellung und keine Deployment-Freigabe. Datenbankstände wurden nicht geprüft oder geändert. Ein rein lokales Linux-Publish-Profil und SVG-basierte Etiketten-Barcodes sind vorbereitet; Linux-Laufzeit und Veröffentlichung wurden nicht getestet. Ziel ist `enote.tablosuite.com`, E-Mail bleibt bis zur späteren IONOS-/Postmark-Einrichtung unverändert.

## 1. Zweck

eNOTE ist ein **Kassensystem mit Verwaltung** pro Geschäft („Salon"/„Laden" als Mandant):
- **Kasse (Caisse):** Verkauf per Touch-Kacheln, Barcode-Scan, freie Beträge („Divers"), Rabatte, Bar-/Kartenzahlung, Kassenbon (PDF/Druck), Kunden-Display.
- **Verwaltung:** Leistungen, Kategorien, Rabatte, Team (Kassierer mit PIN), Ausgaben, Finanzen (Umsatz, Kassenabschlüsse), Abonnement, Einstellungen (Bon-Branding).
- **Plattform:** SuperAdmin/ProduktOwner verwalten Läden, Freigaben, Probezeit, Preise/Provisionen.

## 2. Technologie-Stack

- **.NET 10**, **Blazor Interactive Server** (Echtzeit-UI, serverseitig).
- **EF Core + SQL Server** (Code-First, Migrationen in `eNOTE.WebApp/Migrations`).
- **ASP.NET Core Identity** (AppUser + Rollen).
- **SignalR** für Live-Umsatz (`/hubs/umsatz`) und Kunden-Display (`DisplayBenachrichtigungService`).
- **QuestPDF** (Bon-/Berichts-/Etikett-PDFs), **QRCoder** (Bon-QR), **ZXing** (Barcode-Etiketten).
- **Azure Blob Storage** für private Medien (Logos, Produktbilder, Belege) außerhalb Development per systemseitiger Managed Identity; Development lokal `C:\DevBlob\ENoteDataLibrary`, außerhalb von `wwwroot`.
- **PWA** (`manifest.webmanifest`, `service-worker.js`) – installierbar auf dem Tablet.

## 3. Projektstruktur

| Projekt | Zweck |
|---|---|
| `eNOTE.Core` | Datenmodelle (`DataModels`), domänenreine Services (`Services/Kasse/KassenRechner`), Storage-Abstraktion, Options. Keine EF-/Web-Abhängigkeiten. |
| `eNOTE.WebApp` | Blazor-App: `Components/Pages` (Seiten + Code-Behind), `Components/Shared`, `Components/Layout`, `Services` (Anwendungs-Services), `Data` (DbContext, Seed), `Program.cs`, `wwwroot`. |
| `eNOTE.Tests` | xUnit-Tests (InMemory/SQLite) für Rechner und Services. |

### Wichtige Orte in `eNOTE.WebApp`
- `Program.cs` – DI-Registrierung, Identity, Auth-Cookie, vorhandene Endpunkte (`/bon/{id}`, `/bon/{id}/druck`, `/finances/rapport`) und SignalR-Hub. `Services/Storage/MedienEndpunkte.cs` registriert `/media/{**blobName}` mit DB-basierter Mandantenprüfung; `EnoteInfrastruktur.cs` prüft Production-Konfiguration und persistenten Schlüsselpfad.
- `Data/ApplicationDbContext.cs` – EF-Modell (Salons, UserSalons, Leistungen, Rabatte, KassenSitzungen, Verkaeufe, Ausgaben, Abos, BlobMediaAssets, GeraetSalonFavoriten …).
- `Data/Seed/ApplicationDbInitializer.cs` – Rollen + SuperAdmin aus User Secrets (`Seed:SuperAdminEmail`/`Seed:SuperAdminPasswort`), Plattform-Standardpreise.
- `Components/App.razor` – bindet `app.css` **und** das Scoped-CSS-Bundle `eNOTE.WebApp.styles.css` ein (ohne Letzteres greifen `*.razor.css`-Stile nicht).
- `wwwroot/app.css` – komplettes Soft-UI/Neumorphismus-Design-System.
- `wwwroot/js/enote.js` – JS-Interop (Passwort-Toggle, Popup-Closer, Bildzuschnitt, Tab-Öffnen).

## 4. Kern-Domänen & Fachregeln

### Mandant = Salon (Laden)
- Jeder Laden ist ein `Salon` mit `IstAktiv`, `IstFreigegeben`, `ProbeBisUtc`, Branding (Slogan, Telefon, Adresse, Logo, Bon-QR-Link), Land, optionalem Monatspreis.
- `UserSalon` verknüpft Benutzer ↔ Salon mit delegierbaren Rechten (`KannFinanzenSehen`, `KannLeistungenVerwalten`, `KannRabatteVerwalten`), Kassen-PIN und Besitzer-PIN-Hash + PIN-Sperrlogik.

### Rollen (`AppRollen`)
`Besitzer`, `Kassierer`, `ProduktOwner`, `SuperAdmin`. Seiten sind per `[Authorize(Roles=…)]` abgesichert; sensible Verwaltungsseiten zusätzlich über `BesitzerPinGuard` (PIN-Sperre am gemeinsamen Tablet).

### Besitzer-Zone (PIN-Sperre der Verwaltungsbereiche)
- Die Besitzer-Bereiche **Vente** (Prestations, Remises), **Gestion** (Finances, Dépenses, Équipe) und **Compte** (Abonnement, Paramètres) sind hinter der Besitzer-PIN zusammengefasst: In Sidebar und mobiler Bottom-Nav erscheinen sie standardmäßig eingeklappt hinter einem Schloss-Eintrag („🔒 Zone propriétaire" oberhalb der Gruppen bzw. „🔒 Propriétaire" mobil).
- **Entsperren:** Klick auf das Schloss öffnet einen PIN-Dialog (`KassenService.PruefeBesitzerPinAsync` mit Fehlversuchs-Sperre). **Sperren:** 🔓 „Verrouiller" in der Sidebar. Der Zustand lebt circuit-weit in `BesitzerPinState` und startet pro Session gesperrt.
- Die Sperre gilt **auch für den angemeldeten Besitzer** (Tablet-Schutz: kein Kassierer soll in der offenen Session Verwaltungsseiten öffnen); SuperAdmin/ProduktOwner sind ausgenommen. Ohne konfigurierte Besitzer-PIN gibt es kein Schloss und keine Sperre.
- Die UI-Ebene (Navigation) ist dabei nur Sichtbarkeit; die eigentliche Absicherung bleibt `BesitzerPinGuard` auf den Seiten (greift jetzt ebenfalls für den Besitzer).

### Registrierung & Laden-Anlage
- Erste Registrierung legt Besitzer + ersten Laden an (`IstFreigegeben=false`, 3-Monats-Probezeit) → Admin muss freigeben.
- Weitere Läden: Besitzer kann nach **abgelaufener Probezeit** eines freigegebenen Ladens weitere anlegen (`SalonService.FuegeSalonHinzuAsync`). Neue Läden: aktiv, aber **nicht freigegeben**, **keine** erneute Probezeit → Admin-Freigabe nötig.

### Kasse (Caisse) – `Caisse.razor`
- Drei Zustände: minimiert (schwebender Button), kein aktiver Sitzung (Start: Kassierer + PIN + Wechselgeld), aktive Kasse.
- Kassenstart: PIN löst `UserSalon` (und damit den Laden) auf; `KassenService.StarteSitzungAsync` verhindert eine zweite offene Sitzung.
- Ticket: Positionen mit Menge, freie Beträge, Rabatt (aktivierbar, ggf. Bedingung), Barzahlung (Rückgeld) / Karte (Bestätigung). Abbrechen mit Bestätigungsdialog leert das Ticket.
- `KassenAnzeigeState` (scoped) steuert Minimieren/Kassen-Modus; `AktiverSalonState` (scoped) den gewählten Laden (Topbar-Umschalter, LocalStorage).
- Wiederherstellung: offene Sitzung wird beim Öffnen von `/caisse` geladen; ein modaler Hinweis (Kassierer, Start, Soll-Kassenstand) informiert über die nicht abgeschlossene Kasse.
- Kassenabschluss: Soll-Barbestand = Startbestand + nur Bar-Einnahmen (`KassenRechner.BerechneBarSoll`); Ist wird gezählt, Differenz + Notiz dokumentiert.

### Bon & Druck
- `/bon/{id}` → PDF (QuestPDF, 80mm, Logo, QR). `/bon/{id}/druck` → HTML-Druckansicht (80mm, QR, ruft `window.print()`).
- **Kein QZ Tray / keine lokale Zusatzsoftware** (festgelegte Regel). Dialogloser Druck unter Windows über Browser-Kiosk-Modus (`--kiosk-printing`), siehe `eNOTE/Bon-Drucken.md` und `eNOTE/Start-Kasse.ps1`.

### Katalog (Leistungen, Kategorien, Rabatte)
- Leistungen pro Laden: Preis, optional Lager (`HatStueckzahl`, Mindestbestand-Warnung), Barcode, Kategorie, Bild, Sammel-Artikel (Basis-Leistung + Abzugsmenge), Favoriten pro Gerät.
- Standard-Artikel „Divers" (freier Betrag) wird pro Sitzung sichergestellt.
- Rabatte: Prozent/Festbetrag, optional auf eine Leistung begrenzt, optional mit Bedingung (Checkbox).

### Finanzen & Ausgaben
- Finanzen (`/finances`, Tabs): Tages-Umsätze (Zeitraum-Filter + PDF-Export `/finances/rapport`), letzte Verkäufe (Suche/Zahlart-Filter), Kassenabschlüsse (Soll/Ist/Écart).
- **Solde de caisse (Besitzer-Übersicht)**: Prominent auf `/finances` unter dem Filter: Entrées (Verkäufe, Bar/Karte), Dépenses (mit Link nach `/ausgaben`) und Saldo = Entrées − Dépenses (grün/rot), dazu Ausgaben-Chips pro Kategorie. Zeitraum folgt dem Filter (Standard: letzte 14 Tage); im PDF enthalten (inkl. Dépenses par catégorie).
- Live-Umsatz via SignalR (`/hubs/umsatz`).
- Ausgaben (`/ausgaben`): Kategorien (immer sichtbar), Ausgaben mit optionalem Beleg (Bild/PDF aus Blob).

### Abo & Plattform
- `NutzungsAbo` pro Laden + Plattform-Standardpreis (`PlattformPreis`). SuperAdmin legt globalen/individuellen Monatspreis fest; ProduktOwner-Provision (Prozent/Festbetrag) pro Salon-Zuweisung.
- Admin (`/admin`): Läden-Übersicht (Suche, Status-, Land-, **Besitzer**-Filter), Freigabe/Deaktivieren, Probezeit setzen, Besitzer/ProduktOwner anlegen, Facturation.

## 5. Lokal starten

1. **Voraussetzungen:** .NET 10 SDK, SQL Server (lokal), ggf. User Secrets.
2. **Datenbank:** `appsettings.Development.json` → `Server=localhost;Database=ENOTEDB;Trusted_Connection=True…`. Vor administrativen Änderungen die derzeit konkurrierenden Migrationen fachlich abgleichen; kein Reset und keine automatische Startup-DDL.
3. **Seed:** separater Einmalmodus `--database-admin=seed --database-target=localhost/ENOTEDB`, mit ausdrücklich geprüftem Verbindungsziel. SuperAdmin aus sicherer Konfiguration (`Seed:SuperAdminEmail`, `Seed:SuperAdminPasswort`, optional `Seed:SuperAdminBenutzername`); ohne Zugangsdaten kein Admin. Details und Migrationsmodus in [Deployment-Azure.md](Deployment-Azure.md).
4. **Start:** `dotnet run --project eNOTE/eNOTE.WebApp` → https://localhost:62148 (http://localhost:62149), Environment Development.
5. **Tests:** `dotnet test eNOTE/eNOTE.Tests`.

## 6. Architektur-Regeln & Konventionen

- **IDbContextFactory** für jede DB-Operation (keine geteilten DbContext über parallele Zugriffe).
- **Deutsch** für Code/Core-Modelle, **Französisch** für alle sichtbaren Nutzertexte.
- ID-Benennung: `Id` für Basis-ID, zusammengesetzte deutsche Namen mit `…ID` (z. B. `SalonId`, `VerkaufId`).
- Rechte serverseitig im Service prüfen (nicht nur in der UI).
- Bei Modelländerungen EF-Migration erstellen (`dotnet ef migrations add …`).
- Relevante Tests + Lösungsbuild nach jeder Änderung.
- Design: Soft-UI/Neumorphismus in `app.css`; horizontale Scrollbars vermeiden; mobilfreundlich (Touch-Targets ≥ 44px); native Browser-Selects durch `EnoteSelect`/`EnoteDatePicker` ersetzen; Modals über `ModalDialog`.
- Eigene Scoped-States für circuit-weite Zustände: `KassenAnzeigeState`, `AktiverSalonState`, `BesitzerPinState`; Komponenten abonnieren `OnChange` und implementieren `IDisposable`.

## 7. Wichtige Flows (Kurzüberblick)

- **Verkauf:** Kasse starten → Kachel/Scan → Ticket → Espèces/Carte (Modal) → bestätigen → Bon, Bestand mindern, Live-Umsatz pushen.
- **Kassenabschluss:** „Clôturer" → Soll/Ist vergleichen → Écart + Notiz → Sitzung abgeschlossen, zurück zum Start.
- **Laden wechseln:** Topbar-Umschalter → alle Verwaltungsseiten laden pro Laden neu.
- **Zweiter Laden:** Paramètres → „+ Nouvel établissement" → Admin gibt frei → im Umschalter wählbar.
