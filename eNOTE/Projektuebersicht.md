# eNOTE – Projektübersicht & Entwicklungsbeschreibung

> Kassen- und Verwaltungs-App für kleine Geschäfte (Salon, Boutique, Atelier …), optimiert für Tablet und PC. Französische Endnutzertexte, deutsche Entwicklerdoku.

## Aktueller Infrastrukturstand

Deployment-Konfiguration, private Medienrechte und der getrennte Administrationspfad stehen in [Deployment-Azure.md](Deployment-Azure.md). Development speichert Medien lokal außerhalb von `wwwroot`; Production ausschließlich per Managed Identity in privaten Blobs, ohne lokalen Fallback. Der normale App-Start migriert und seedet nicht.

Stand 10.11.2026: Lösungsbuild `SKOULOUAPPMAPPE.slnx` erfolgreich (0 Fehler, 73 Warnungen), `eNOTE.Tests` 200/200 grün. `Migrations` enthält eine konsistente Kette (`InitialCreate` → `ProfilBildHinzugefuegt` → `LeistungsTypHinzugefuegt` → `KategorieUnterkategorien`), die lokale `ENOTEDB` ist darauf aktuell, das Modell hat keine offenen Änderungen. Lokaler App-Start und visuelle Prüfung (Headless-Browser-Screenshots) gegen die Entwicklungs-DB sind für Design-Arbeit freigegeben; Azure-Datenbanken weiterhin nicht ungeprüft migrieren. Die fachlichen Bestandsbeschreibungen unten sind kein Nachweis einer abgeschlossenen Ein-Geschäft-/Pseudonym-/PIN-Umstellung und keine Deployment-Freigabe. Datenbankstände wurden nicht geprüft oder geändert. Ein rein lokales Linux-Publish-Profil und SVG-basierte Etiketten-Barcodes sind vorbereitet; Linux-Laufzeit und Veröffentlichung wurden nicht getestet. Ziel ist `enote.tablosuite.com`, E-Mail bleibt bis zur späteren IONOS-/Postmark-Einrichtung unverändert.

## 1. Zweck

eNOTE ist ein **Kassensystem mit Verwaltung** pro Geschäft („Salon"/„Laden" als Mandant):
- **Kasse (Caisse):** Verkauf per Touch-Kacheln (Kategorien/Unterkategorien, Suche + Barcode-Scan), freie Beträge („Divers"), Rabatte, Zahlung **bar, Mobile Money, Karte oder gemischt**, Kassenbon (PDF/Druck), Kunden-Display.
- **Startseite:** Dashboard (Tagesumsatz vs. Durchschnitt, 14-Tage-Verlauf, Zahlungsmix, Bestseller, Lagerwarnungen, Kassenstatus) – nur Besitzer/Finanzrecht, hinter der Besitzer-PIN.
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
- `Components/App.razor` – bindet `app.css` ein. Es gibt bewusst **keine** `*.razor.css`-Dateien mehr (kein Scoped-CSS-Bundle); alle Stile liegen zentral in `app.css`. Wer wieder eine `.razor.css` anlegt, muss `eNOTE.WebApp.styles.css` in `App.razor` erneut einbinden.
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
- Kassenabschluss: Soll-Barbestand = Startbestand + Baranteile der Verkäufe (Rückerstattungen bar sind negativ) + Einlagen in die Kasse − Bar-Entnahmen des Besitzers (`KassenRechner.BerechneBarSoll`); Ist wird gezählt, Differenz + Notiz dokumentiert. Der Dialog zeigt Barverkäufe und Bar-Rückerstattungen getrennt.

### Zahlungen & Mobile Money
- Ein `Verkauf` hat 1..n `VerkaufZahlung`-Anteile (`Art` Bar/Karte/MobileMoney, `Betrag`, bei Mobile Money `MobileMoneyAnbieterId` + Namenskopie `AnbieterName` + optionale `Referenz`). `Verkauf.ZahlungsArt` ist nur noch die Zusammenfassung (`Gemischt` bei mehreren Arten). Summe der Anteile = `GesamtSumme` (`KassenRechner.PruefeZahlungen`), höchstens ein Baranteil; Rückgeld nur aus dem Baranteil.
- Auswertungen immer über `KassenRechner.Anteile(...)` (Altbestand ohne Anteile = ein Anteil in Höhe der Gesamtsumme). Bar-Soll zählt nur Baranteile.
- Anbieter sind **Stammdaten, kein Enum** (`MobileMoneyAnbieter`: Name, Land, Markenfarbe, aktiv), gepflegt vom SuperAdmin unter Administration → „Mobile Money“; Startdaten für Togo, Bénin, Sénégal, Côte d'Ivoire, Burkina Faso, Mali per Migration. Ein Laden akzeptiert standardmäßig alle aktiven Anbieter seines Landes; der Besitzer kann in den Paramètres eine eigene Auswahl treffen (`Salon.MobileMoneyIndividuell` + `SalonMobileMoneyAnbieter`, leere Auswahl = kein Mobile Money). Validierung serverseitig beim Buchen.
- Keine API-Anbindung an Aggregatoren (CinetPay, FedaPay …): der Kassierer bestätigt nach Eingang der SMS. Kundentelefonnummern werden bewusst nicht gespeichert.

### Bon & Druck
- `/bon/{id}` → PDF (QuestPDF, 80mm, Logo, QR). `/bon/{id}/druck` → HTML-Druckansicht (80mm, QR, ruft `window.print()`).
- **Kein QZ Tray / keine lokale Zusatzsoftware** (festgelegte Regel). Dialogloser Druck unter Windows über Browser-Kiosk-Modus (`--kiosk-printing`), siehe `eNOTE/Bon-Drucken.md` und `eNOTE/Start-Kasse.ps1`.

### Rückerstattung (Storno als Gegenbuchung) – `StornoService`
- Ein Verkauf wird nie gelöscht oder geändert. „Rembourser“ (Kasse → Ventes) erzeugt in der **aktuell offenen Kasse** einen eigenen Bon mit negativen Mengen/Beträgen: `Verkauf.StornoVonVerkaufId` (+ `StornoGrund`, `StornoErfasstVonName`), Positionen mit `StornoVonPositionId`. Umsätze, Bar-Soll und Bestseller heben sich dadurch automatisch auf; Zählungen („ventes“) schließen Rückerstattungen aus (`StornoVonVerkaufId == null`).
- Teil-Erstattungen nach Zeile/Menge, bis alles erstattet ist; Betrag anteilig inkl. Rabatt, die letzte offene Menge bekommt genau den Rest (`StornoRechner.Betrag`).
- Erstattet wird über einen Zahlweg des Originals (Bar, Karte, je Mobile-Money-Anbieter), höchstens bis zum dort bezahlten Rest; bar nur, wenn genug Bargeld in der Kasse erwartet wird. Pflicht-Motiv; Freigabe per Besitzer-PIN (ohne PIN: nur Besitzer). Ware optional zurück ins Lager (Sammel-Artikel → Basisbestand).
- Bon „REMBOURSEMENT“ mit Verweis auf den Originalbon (HTML-Druck + PDF); Finances zeigt „Remboursements“ in den Entrées und markiert Bons als „Remboursée“/„Remboursement“. Die Erstattung zählt am Tag, an dem sie passiert.

### Katalog (Leistungen, Kategorien, Rabatte)
- Leistungen pro Laden: Preis, optional Lager (`HatStueckzahl`, Mindestbestand-Warnung), Barcode, Kategorie, Bild, Sammel-Artikel (Basis-Leistung + Abzugsmenge), Favoriten pro Gerät.
- Standard-Artikel „Divers" (freier Betrag) wird pro Sitzung sichergestellt.
- Rabatte: Prozent/Festbetrag, optional auf eine Leistung begrenzt, optional mit Bedingung (Checkbox).

### Finanzen & Ausgaben
- Finanzen (`/finances`, Tabs): Tages-Umsätze (Zeitraum-Filter + PDF-Export `/finances/rapport`), letzte Verkäufe (Suche/Zahlart-Filter), Kassenabschlüsse (Soll/Ist/Écart).
- **Solde de caisse (Besitzer-Übersicht)**: Prominent auf `/finances` unter dem Filter: Entrées (Verkäufe, Bar/Karte), Dépenses (mit Link nach `/ausgaben`) und Saldo = Entrées − Dépenses (grün/rot), dazu Ausgaben-Chips pro Kategorie. Zeitraum folgt dem Filter (Standard: letzte 14 Tage); im PDF enthalten (inkl. Dépenses par catégorie).
- Live-Umsatz via SignalR (`/hubs/umsatz`).
- Ausgaben (`/ausgaben`): Kategorien (immer sichtbar), Ausgaben mit optionalem Beleg (Bild/PDF aus Blob).

### Abo & Plattform (automatischer Vertrag)
- **Eine Preisregel überall** (`AboService.MonatsBasisAsync` / `GetFormelPreiseAsync`, `VertragsRegeln`): Monatsbasis (individueller Ladenpreis, sonst globaler Monatspreis) × Monate − Rabatt (Trimestriel −5 %, Semestriel −7,5 %, Annuel −10 %). Der Admin pflegt nur den **Monatspreis**; die übrigen Formeln werden abgeleitet. Änderungen des globalen Monatspreises werden zusätzlich in `MonatsPreisStaende` historisiert.
- **Probezeit**: angefangener Registrierungsmonat + 2 volle Monate, gespeichert als Tagesende (z. B. Registrierung 10.11. → bis 31.01. 23:59:59); ein vom Admin gewähltes Datum gilt einschließlich des Tages. Das Abo beginnt am ersten Tag danach um 00:00 (`VertragsRegeln.BeginnNachProbe`), Rechnungen über 0 F werden nie angelegt.
- **Perioden entstehen automatisch** (`VertragsService.SynchronisiereAsync`, idempotent): nach Ende der Probezeit, beim Kassenstart, beim Öffnen von Abonnement/Facturation und stündlich durch `VertragsSynchronisierungsDienst`. Beginnt der Vertrag nicht am 1. eines Monats, gibt es zuerst eine anteilige Monatsperiode bis zum Monatsersten. Jede Periode speichert Preis, Monatsbasis und Rabatt als Kopie – spätere Preisänderungen ändern bestehende Rechnungen nie; maßgeblich ist der am Periodenbeginn gültige Preis (vor dem ersten erfassten Preis: frühester bekannter globaler Preis).
- **Formelwechsel** (`WechsleAsync`, Vorschau `GetWechselVorschauAsync`): Ist die laufende Rechnung eine volle Periode und noch **offen oder abgelehnt** (nicht gemeldet/bezahlt), wird sie **sofort umgewandelt** – gleicher Start, Monatspreis vom Periodenbeginn, Fälligkeit und Sperrfrist unverändert, Eintrag „Formule convertie“ in `VertragsHistorien`. Sonst (gemeldet/bezahlt, anteilige Startrechnung, Probezeit) gilt die Formel ab der nächsten Periode.
- **Zahlung**: Besitzer meldet („J'ai payé“, `MeldeAsync`), nur der **SuperAdmin** bestätigt (`AboService.SetzeZahlungsStatusAsync` mit Bearbeiter-Prüfung); ProduktOwner sehen in Facturation nur ihre zugewiesenen Läden.
- **Kassensperre** (`KassenZugangService`, serverseitig in `KassenService.StarteSitzungAsync`/`BucheVerkaufAsync`): ab dem 15. Tag nach Fälligkeit ohne Bestätigung keine neuen Sitzungen/Verkäufe; der Abschluss einer laufenden Sitzung bleibt möglich. Wäre die Frist beim (verspäteten) Erzeugen schon abgelaufen, gibt es 15 Tage ab Erzeugung. Ohne konfigurierten Tarif wird nicht gesperrt.

### Abo & Plattform (Allgemein)
- `NutzungsAbo` pro Laden + Plattform-Standardpreis (`PlattformPreis`). SuperAdmin legt globalen/individuellen Monatspreis fest; ProduktOwner-Provision (Prozent/Festbetrag) pro Salon-Zuweisung.
- Admin (`/admin`): Läden-Übersicht (Suche, Status-, Land-, **Besitzer**-Filter), Freigabe/Deaktivieren, Probezeit setzen, Besitzer/ProduktOwner anlegen, Facturation.

## 5. Lokal starten

1. **Voraussetzungen:** .NET 10 SDK, SQL Server (lokal), ggf. User Secrets.
2. **Datenbank:** `appsettings.Development.json` → `Server=localhost;Database=ENOTEDB;Trusted_Connection=True…`. Vor administrativen Änderungen die derzeit konkurrierenden Migrationen fachlich abgleichen; kein Reset und keine automatische Startup-DDL.
3. **Seed:** separater Einmalmodus `--database-admin=seed --database-target=localhost/ENOTEDB`, mit ausdrücklich geprüftem Verbindungsziel. SuperAdmin aus sicherer Konfiguration (`Seed:SuperAdminEmail`, `Seed:SuperAdminPasswort`, optional `Seed:SuperAdminBenutzername`); ohne Zugangsdaten kein Admin. Details und Migrationsmodus in [Deployment-Azure.md](Deployment-Azure.md).
   **Demo-Daten (nur Development):** `--database-admin=demo --database-target=localhost/ENOTEDB` legt den Laden „Salon Élégance (Démo)“ mit Katalog, Team, Rabatten, Ausgaben und 6 Wochen Verkaufshistorie an (wiederholbar, setzt den Demo-Laden zurück). Zugänge und Screenshot-Rundgang: [`tools/ui-rundgang/README.md`](tools/ui-rundgang/README.md).
4. **Start:** `dotnet run --project eNOTE/eNOTE.WebApp` → https://localhost:62148 (http://localhost:62149), Environment Development.
5. **Tests:** `dotnet test eNOTE/eNOTE.Tests`.

## 6. Architektur-Regeln & Konventionen

- **IDbContextFactory** für jede DB-Operation (keine geteilten DbContext über parallele Zugriffe).
- **Deutsch** für Code/Core-Modelle, **Französisch** für alle sichtbaren Nutzertexte.
- ID-Benennung: `Id` für Basis-ID, zusammengesetzte deutsche Namen mit `…ID` (z. B. `SalonId`, `VerkaufId`).
- Rechte serverseitig im Service prüfen (nicht nur in der UI).
- Bei Modelländerungen EF-Migration erstellen (`dotnet ef migrations add …`).
- Relevante Tests + Lösungsbuild nach jeder Änderung.
- Design-System „Soft Modern“ in `wwwroot/app.css`: Tokens in `:root` (Farben, Flächen, Radien, Elevation, Zahlart-Farben `--enote-pay-*` – farbsehschwächen-geprüft), keine Farben hart codieren. Icons nur über `<Icon Name="…" />` (SVG, offline), keine Emojis als Icons. Wiederverwendbar: `PinEingabe`, `BetragEingabe`, `AnbieterAuswahl`, `ZahlungsTag`, `SaeulenDiagramm`, `ZahlungsMix`. Präfixe je Bereich: `kx-` Kasse, `db-` Dashboard, `fi-` Finanzen, `pr-` Prestations, `st-` Paramètres, `mm-` Mobile-Money-Admin.
- Responsiv: Desktop ≥1200px Sidebar, Tablet 641–1199px Icon-Leiste, Handy ≤640px untere Leiste + „Plus“-Blatt; Kasse ≤900px mit aufklappbarer Ticket-Leiste. Keine horizontalen Scrollbars (Grids mit `minmax(0, 1fr)`), Touch-Targets ≥ 44px, native Selects durch `EnoteSelect`/`EnoteDatePicker`, Modals über `ModalDialog` (Escape schließt), Toasts blenden sich selbst aus.
- Visuelle Prüfung: `tools/ui-rundgang` (Screenshots Tablet/Desktop/Handy, meldet Querscrollen und Browserfehler).
- Eigene Scoped-States für circuit-weite Zustände: `KassenAnzeigeState`, `AktiverSalonState`, `BesitzerPinState`; Komponenten abonnieren `OnChange` und implementieren `IDisposable`.

## 7. Wichtige Flows (Kurzüberblick)

- **Verkauf:** Kasse starten → Kachel/Scan → Ticket → Espèces/Carte (Modal) → bestätigen → Bon, Bestand mindern, Live-Umsatz pushen.
- **Kassenabschluss:** „Clôturer" → Soll/Ist vergleichen → Écart + Notiz → Sitzung abgeschlossen, zurück zum Start.
- **Laden wechseln:** Topbar-Umschalter → alle Verwaltungsseiten laden pro Laden neu.
- **Zweiter Laden:** Paramètres → „+ Nouvel établissement" → Admin gibt frei → im Umschalter wählbar.
