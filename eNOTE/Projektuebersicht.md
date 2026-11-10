# eNOTE – Projektübersicht & Entwicklungsbeschreibung

> Kassen- und Verwaltungs-App für kleine Geschäfte in Westafrika (Salon, Institut, Barbershop, Boutique, Atelier …), optimiert für Tablet und PC, nutzbar auf dem Handy. Französische Endnutzertexte, deutsche Entwicklerdoku. Regeln für die Zusammenarbeit: [copilot-instructions.md](copilot-instructions.md).

## Aktueller Stand (10.11.2026, abends)

- **Code:** Lösungsbuild erfolgreich, `eNOTE.Tests` **270/270** grün, letzter Commit `2b0f40d`.
- **Datenbank:** Migrationskette bis `20261110173221_Kundenkartei`; lokale `ENOTEDB` **und** Azure `ENote-Database` sind auf diesem Stand. Der normale App-Start migriert und seedet nie.
- **Betrieb:** Azure App Service `enote` (Linux, .NET 10, Tarif B1, 1 Instanz) unter https://enote.tablosuite.com, Always On und Sitzungsaffinität an, E-Mails über Postmark, Nutzer-Medien im privaten Blob Storage (Managed Identity). Details: [Deployment-Azure.md](Deployment-Azure.md).
- **Nächste Schritte:** Der Nutzer testet ab 11.11.2026 alles manuell und sammelt Fehler. Danach offen: Bondrucker und Scanner mit echter Hardware testen, Grundpreis auf Azure auf 3.600 F setzen (Administration → Tarifs), optional Treueprogramm.

## 1. Zweck

eNOTE ist ein **Kassensystem mit Verwaltung** pro Geschäft (`Salon` = Mandant):
- **Kasse:** Touch-Kacheln (Kategorien/Unterkategorien, Suche, Barcode-Scanner), freie Beträge (« Divers »), Remises, optionaler Kunde, Zahlung **bar, Mobile Money, Karte oder gemischt**, Bon (PDF/Druck), Kunden-Display, Rückerstattung, Kassenabschluss.
- **Besitzer:** Dashboard, Prestations & Stock, Remises, Clients, Équipe & Rechte, Finances, Dépenses, Abonnement, Paramètres (Bon-Branding, Mobile Money).
- **Plattform:** SuperAdmin und ProduktOwner (« Responsable ») verwalten Läden, Freigaben, Probezeit, Tarife, Provisionen, Facturation, Annonces und E-Mails.
- **Öffentlich:** Startseite mit App-Bildern und Werbevideo, Registrierung.

## 2. Technologie-Stack

- **.NET 10**, **Blazor Interactive Server** (global `@rendermode InteractiveServer` in `Routes.razor`).
- **EF Core + SQL Server** (Code-First, Migrationen in `eNOTE.WebApp/Migrations`), **ASP.NET Core Identity** (Anmeldung per Pseudonym, eine aktive Sitzung pro Konto über `ActiveSessionId`, Prüfung bei jeder Anfrage).
- **SignalR** für Live-Umsatz (`/hubs/umsatz`) und Kunden-Display.
- **QuestPDF** (Bon-, Berichts-, Etikett-PDFs), **QRCoder** (Bon-QR), **ZXing** (SVG-Barcodes).
- **Postmark** (HTTP-API) für E-Mails, Warteschlange `MailNachrichten` mit Wiederholung.
- **Azure Blob Storage** (Managed Identity) für Nutzer-Medien in Production; Development lokal `C:\DevBlob\ENoteDataLibrary`, außerhalb von `wwwroot`.
- **PWA** (`manifest.webmanifest`, `service-worker.js`) – installierbar auf dem Tablet.

## 3. Projektstruktur

| Projekt | Zweck |
|---|---|
| `eNOTE.Core` | Datenmodelle (`DataModels/{Abo,Finanzen,Identity,Infrastruktur,Kasse,Katalog,Salons}`), domänenreine Rechner (`KassenRechner`, `StornoRechner`, `VertragsRegeln`, `BarcodeRegeln`), Storage-Abstraktion. Keine EF-/Web-Abhängigkeiten. |
| `eNOTE.WebApp` | Blazor-App: `Components/{Pages,Shared,Layout}`, `Services` (Anwendungs-Services, `Mails/`, `Storage/`), `Data` (DbContext, Seed), `Medien/videos`, `Program.cs`, `wwwroot`. |
| `eNOTE.Tests` | xUnit-Tests (EF InMemory) für Rechner, Services und ausgewählte UI-Zustände. |

### Wichtige Orte in `eNOTE.WebApp`
- `Program.cs` – DI-Registrierung, Identity/Cookie, Endpunkte `/auth/*`, `/bon/{id}`, `/bon/{id}/druck`, `/finances/rapport`, `/medien/videos/{datei}`, SignalR-Hub, Datenbank-Administrationsmodi (`--database-admin=…`). `Services/Storage/MedienEndpunkte.cs` registriert `/media/{**blobName}` mit Mandantenprüfung; `EnoteInfrastruktur.cs` prüft die Production-Konfiguration.
- `Data/ApplicationDbContext.cs` – EF-Modell; `Data/Seed/ApplicationDbInitializer.cs` (Rollen, SuperAdmin aus sicherer Konfiguration, Standardpreis 3.600 F/Monat für neue Datenbanken); `Data/Seed/DemoDatenSeeder.cs` (Demo-Laden, nur Development).
- `Components/App.razor` – `app.css`, Theme-Skript (Dunkelmodus ohne Aufblitzen). Bewusst **keine** `*.razor.css`; wer wieder eine anlegt, muss `eNOTE.WebApp.styles.css` einbinden.
- `Components/Layout/MainLayout.razor` – Topbar, Benutzermenü, Ankündigungs-Banner; `NavMenu.razor` – Navigation je Rolle (Sidebar, Icon-Leiste, Handy-Leiste + « Plus »).
- `wwwroot/app.css` – Design-System „Soft Modern“; `wwwroot/js/enote.js` – JS-Interop (Passwort-Toggle, Popup-Closer, Bildzuschnitt, Tab-Öffnen, Scroll-Animation der Startseite, Barcode-Scanner).

## 4. Kern-Domänen & Fachregeln

### Konten, Läden, Rollen
- **Ein Konto pro Laden.** Die Registrierung legt Besitzerkonto + Laden an (`IstFreigegeben=false`, Probezeit) → Admin gibt frei. Ein weiterer Laden (Paramètres → « Nouvel établissement », `SalonService.FuegeSalonHinzuAsync`) ist erst nach abgelaufener Probezeit eines freigegebenen Ladens möglich und bekommt ein **neues Besitzerkonto** (eigenes Pseudonym + Passwort, gleiche E-Mail erlaubt). Der neue Laden ist aktiv, nicht freigegeben, ohne neue Probezeit.
- `Salon`: `IstAktiv`, `IstFreigegeben`, `ProbeBisUtc`, Branding (Slogan, Telefon, Adresse, Logo, Bon-QR-Link), Land, optionaler individueller Monatspreis.
- `UserSalon` verknüpft Konto ↔ Laden mit delegierbaren Rechten (`KannFinanzenSehen`, `KannLeistungenVerwalten`, `KannRabatteVerwalten`), Kassen-PIN sowie Besitzer-PIN-Hash mit Fehlversuchs-Sperre. `AktiverSalonState` liefert die Zuordnung; `SalonSwitcher` erscheint nur bei mehreren Zuordnungen.
- **Rollen** (`AppRollen`): `Besitzer`, `Kassierer`, `ProduktOwner` (« Responsable »), `SuperAdmin`. Seiten per `[Authorize(Roles=…)]`, Rechte zusätzlich im Service (`LadenZugriff`).
- **Kassierer** sind eigene Konten (Pseudonym, Initialpasswort, Kassen-PIN), angelegt in Équipe; kein Profil und keine Passwort-Wiederherstellung per Mail.
- Anmeldung per Pseudonym; die E-Mail ist nur Kontakt-/Wiederherstellungsadresse und gewährt nie Zugriff auf andere Konten. **Abmelden** führt auf die öffentliche Startseite; Anmeldung und Registrierung haben « Retour à l'accueil ».

### Besitzer-Zone (PIN-Sperre)
- Vente (Prestations, Remises, Clients), Gestion (Finances, Dépenses, Équipe) und Compte (Abonnement, Paramètres) liegen hinter der Besitzer-PIN, auch für den angemeldeten Besitzer (gemeinsames Tablet). In der Navigation sind sie hinter « Zone propriétaire » eingeklappt; entsperren per PIN-Dialog (`KassenService.PruefeBesitzerPinAsync`), sperren über « Verrouiller ». Zustand circuit-weit in `BesitzerPinState`, pro Sitzung zunächst gesperrt.
- Die eigentliche Absicherung ist `BesitzerPinGuard` auf den Seiten; SuperAdmin/ProduktOwner sind ausgenommen; ohne konfigurierte PIN gibt es keine Sperre.

### Kasse (`Caisse.razor`)
- Zustände: minimiert (schwebender Knopf), ohne Sitzung (Start: Kassierer + PIN + Fond de caisse), aktiv. Höchstens eine offene Sitzung pro Laden (`KassenService.StarteSitzungAsync`); beim erneuten Öffnen Hinweis « Caisse encore ouverte » (Kassierer, Start, Soll).
- Ticket: Kacheln mit Kategorien, Suche, Mengen, freier Betrag « Divers », Remise (aktivierbar, ggf. Bedingung, auf eine Leistung begrenzbar). **Kunde optional** über « Client (facultatif) » (siehe Kundenkartei). « Vider » leert nach Rückfrage.
- **Barcode-Scanner** (USB/Bluetooth im Tastatur-Modus, Enter am Ende): `enoteScanner` erkennt eine schnelle Ziffernfolge + Enter überall in der Kasse, auch ohne Fokus im Suchfeld, ruft `Caisse.ScanErkannt` auf und fängt das Enter ab, damit keine Kachel doppelt gebucht wird. Bei offenem Dialog wird nichts gebucht; Eingabefelder (PIN, Beträge, Suche) bleiben unberührt. Mit echter Hardware noch nicht getestet; Scanner auf AZERTY einstellen, wenn das Gerät AZERTY nutzt.
- Aktionen oben: « Ventes » (letzte Verkäufe, Bon-Nachdruck, Rückerstattung), « Écran client » (QR/Link für eine zweite Tablette), « Retrait » (Entnahme des Besitzers), « Réduire », « Clôturer ». `KassenAnzeigeState` steuert Minimieren und Vollbild.
- Kassenabschluss: Bar-Soll = Startbestand + Baranteile der Verkäufe (Rückerstattungen bar negativ) + Einlagen − Bar-Entnahmen (`KassenRechner.BerechneBarSoll`); das Gezählte wird eingetragen, Écart + Notiz dokumentiert.
- Kassensperre bei Zahlungsverzug (siehe Abo) – serverseitig beim Start und beim Buchen.

### Zahlungen & Mobile Money
- Ein `Verkauf` hat 1..n `VerkaufZahlung`-Anteile (`Bar`, `Karte`, `MobileMoney` mit `MobileMoneyAnbieterId`, Namenskopie `AnbieterName` und optionaler `Referenz`). `Verkauf.ZahlungsArt` ist nur die Zusammenfassung (`Gemischt` bei mehreren Arten). Summe der Anteile = `GesamtSumme` (`KassenRechner.PruefeZahlungen`), höchstens ein Baranteil, Rückgeld nur daraus. Auswertungen immer über `KassenRechner.Anteile(...)`.
- Anbieter sind **Stammdaten** (`MobileMoneyAnbieter`: Name, Land, Farbe, aktiv), gepflegt vom SuperAdmin (Administration → Mobile Money); Startdaten für Togo, Bénin, Sénégal, Côte d'Ivoire, Burkina Faso, Mali. Ein Laden akzeptiert standardmäßig alle aktiven Anbieter seines Landes; der Besitzer kann in den Paramètres auswählen (`Salon.MobileMoneyIndividuell` + `SalonMobileMoneyAnbieter`). Validierung serverseitig beim Buchen.
- Keine Aggregator-API (CinetPay, FedaPay …): Der Kassierer bestätigt nach Eingang der SMS. Mobile Money speichert keine Telefonnummern.

### Rückerstattung (`StornoService`)
- Verkäufe werden nie geändert oder gelöscht. « Rembourser » (Kasse → Ventes) erzeugt in der offenen Kasse einen Gegenbon mit negativen Mengen/Beträgen (`StornoVonVerkaufId`, `StornoGrund`, `StornoErfasstVonName`, Positionen mit `StornoVonPositionId`). Umsätze, Bar-Soll und Bestseller heben sich automatisch auf; Zählungen schließen Rückerstattungen aus (`StornoVonVerkaufId == null`).
- Teil-Erstattung nach Zeile/Menge (anteilig inkl. Rabatt, Rest auf die letzte Menge – `StornoRechner`), über einen Zahlweg des Originals bis zum dort bezahlten Rest; bar nur bei genug erwartetem Bargeld. Pflicht-Motiv, Freigabe per Besitzer-PIN, Ware optional zurück ins Lager. Bon « REMBOURSEMENT »; die Rückerstattung übernimmt den Kunden des Originals und zählt am Tag, an dem sie passiert.

### Kundenkartei (`/clients`, `KundenService`, `KundenAuswahl`)
- Kasse: « Client (facultatif) » über dem Ticket – Suche nach Name oder Nummer (mit Besuchszahl), wählen oder mit einem Tipp neu anlegen. `Verkauf.KundeId` ist optional; der Bon zeigt « Client : … ».
- **Telefonnummer nur mit Zustimmung des Kunden** (Pflicht-Häkchen « Le client est d'accord pour enregistrer son numéro », Zeitpunkt in `TelefonEinwilligungUtc`, Dubletten je Laden verhindert). Kassierer sehen nur die letzten 4 Ziffern und keine Kundenliste.
- Besitzer: Kennzahlen (Kunden, neu im Monat, Stammkunden ≥ 2 Besuche, Anteil Verkäufe mit Kunde der letzten 30 Tage), Liste mit Besuchen/Umsatz/letztem Besuch, Kundenblatt mit Lieblingsleistungen, Notiz, letzten 10 Besuchen und Bearbeiten. **Löschen** entfernt die Personendaten; Verkäufe bleiben ohne Kundenbezug.
- Später möglich: Treueprogramm (z. B. 10. Besuch −10 % über Remises) – erst nach Rückfrage bauen.

### Katalog, Stock & Team
- Leistungen pro Laden: Typ Prestation/Produit, Preis, Kategorie, Bild, Barcode (oder automatisch erzeugt), bei Produkten Lager (`HatStueckzahl`, Mindestbestand-Warnung, Einkaufspreis), Sammel-Artikel (Basis-Leistung + Abzugsmenge), Favoriten pro Gerät. Das Inventar zeigt Lagerwert und möglichen Gewinn. Standard-Artikel « Divers » wird pro Sitzung sichergestellt.
- Remises: Prozent oder Festbetrag, optional auf eine Leistung begrenzt, optional mit Bedingung (der Kassierer bestätigt sie), aktivierbar direkt auf der Karte; bereits genutzte Remises werden beim Löschen nur deaktiviert.
- Équipe: Kassierer mit PIN, letzte Kasse, Verkäufe im Monat; « Droits » delegiert Finanzen, Prestations, Remises.

### Finanzen & Ausgaben
- `/finances`: Zeitraum-Filter, Entrées je Zahlungsart (inkl. Mobile-Money-Anbieter und Remboursements), Dépenses je Kategorie, Solde; Tabs Par jour, Dernières ventes, Clôtures de caisse, Mouvements; « Apport / aide » (Einlagen außerhalb der Verkäufe); PDF-Bericht `/finances/rapport`. Live-Umsatz via SignalR.
- `/ausgaben`: Zeiträume, Summe je Kategorie, Ausgaben mit optionalem Beleg (Bild/PDF im Blob), eigene Kategorien.

### Bon & Druck
- `/bon/{id}` → PDF (QuestPDF, 80 mm, Logo, QR, Kunde), `/bon/{id}/druck` → HTML-Druckansicht (80 mm, ruft `window.print()`).
- QR-Ziel (`BonQr`): eigener Link des Ladens (Paramètres), sonst **https://enote.tablosuite.com** (`App:BonQrStandard`).
- **Kein QZ Tray / keine lokale Zusatzsoftware.** Dialogloser Druck unter Windows über Browser-Kiosk-Modus (`--kiosk-printing`), siehe [Bon-Drucken.md](Bon-Drucken.md) (mit Hinweisen zum Scanner) und `Start-Kasse.ps1`. Mit echtem Bondrucker noch nicht getestet.

### Abo, Vertrag & Tarife
- **Eine Preisregel** (`AboService.MonatsBasisAsync`, `VertragsRegeln`): Monatsbasis (individueller Ladenpreis, sonst Standard-Monatspreis) × Monate − Rabatt (Trimestriel −5 %, Semestriel −7,5 %, Annuel −10 %). Nur der Monatspreis wird gepflegt; die übrigen Formeln werden abgeleitet.
- **Probezeit:** angefangener Registrierungsmonat + 2 volle Monate, gespeichert als Tagesende; ein vom Admin gewähltes Datum gilt einschließlich des Tages. Das Abo beginnt am Tag danach um 00:00 (`VertragsRegeln.BeginnNachProbe`). Rechnungen über 0 F gibt es nie.
- **Perioden entstehen automatisch** (`VertragsService.SynchronisiereAsync`, idempotent): beim Kassenstart, beim Öffnen von Abonnement/Facturation und stündlich durch `VertragsSynchronisierungsDienst` (der zuvor fällige Tarifänderungen übernimmt). Anteilige erste Periode bis zum Monatsersten. Jede Periode speichert Preis, Basis und Rabatt als Kopie; maßgeblich ist der am Periodenbeginn gültige Preis (datierte `MonatsPreisStaende`).
- **Formelwechsel** (`WechsleAsync`): Eine offene oder abgelehnte volle Rechnung wird sofort umgewandelt (Fälligkeit und Sperrfrist bleiben, Eintrag in `VertragsHistorien`); sonst gilt die neue Formel ab der nächsten Periode.
- **Zahlung:** Der Besitzer meldet « J'ai payé », nur der **SuperAdmin** bestätigt; ProduktOwner sehen in Facturation nur ihre Läden.
- **Kassensperre** (`KassenZugangService`): ab dem 15. Tag nach Fälligkeit ohne Bestätigung keine neuen Sitzungen/Verkäufe (der Abschluss bleibt möglich); wäre die Frist beim Erzeugen schon abgelaufen, gibt es 15 Tage ab Erzeugung.
- **Tarifänderung für alle** (Administration → Tarifs, `PreisAnpassungService`): Standard-Monatspreis = Mindestpreis, individuelle Preise nie darunter. Eine Erhöhung um X F gilt ab dem 1. eines kommenden Monats und erhöht auch alle individuellen Preise um X F; eine Senkung geht auch sofort, individuelle Preise bleiben (nie unter Standard). Dazu Vorschau je Laden, Textvorlagen mit Platzhaltern (`{ancien}`, `{nouveau}`, `{ecart}`, `{date}`, `{quand}`) für Banner und optionale Mail `changement-tarif`, Verlauf, und eine geplante Änderung ist aufhebbar. Bestehende Rechnungen ändern sich nie. Startpreis neuer Datenbanken: 3.600 F.

### Plattform (Administration)
- `/admin` (SuperAdmin; ProduktOwner sieht seine Läden): Reiter **Établissements** (Suche, Filter Status/Land/Besitzer, Freigabe, Deaktivieren, Probezeit, individueller Tarif, Besitzer anlegen), **Propriétaires plateforme** (Responsables, Zuweisung, Commissions), **Tarifs**, **Mobile Money**, **Administrateurs**.
- **Administrateurs** (`AdminKontenService`, Reiter nur für den Hauptadministrator `IstErsterAdmin`): Admins anlegen, bearbeiten, sperren/entsperren (gesperrt → beim nächsten Seitenaufruf abgemeldet), löschen. Fehlt die Kennzeichnung, wird automatisch das älteste echte Admin-Konto Hauptadmin (nie `demo.*`); das eigene Konto ist geschützt.
- Responsable (`/mes-etablissements`): Ventes du jour/du mois und Commission je Laden, « Nouvel établissement » für einen Besitzer (Admin gibt frei).
- Weitere Seiten: Facturation (Abonnements, gemeldete Zahlungen bestätigen), Journaux (zentrales Fehlerprotokoll `ExceptionLogs`), E-mails, Annonces.

### Annonces (`/admin/annonces`, `AnkuendigungService`, `AnkuendigungsBanner`)
- Banner oben in der App, pro Nutzer schließbar, Zielgruppe Besitzer / Besitzer + Kassierer / alle, optional per Mail an alle Besitzer.
- **Mise à jour planifiée:** Tag, Beginn und Ende (heure de Lomé = UTC; Bénin +1 h), Vorlagen « Courte », « Avec conseil », « Nouveautés » mit Platzhaltern `{jour}`, `{debut}`, `{fin}`, `{duree}`. Während des Fensters erscheint « Mise à jour en cours », danach verschwindet das Banner, optional drei Tage « eNOTE a été mis à jour ». Weitere Arten: Nouveauté, Information, Tarif (von der Tarifänderung erzeugt).

### E-Mails (`Services/Mails`)
- `EmailService`: Postmark-HTTP-API (`Mail:Provider=Postmark`, `Mail:PostmarkServerToken`) oder SMTP; jede Mail landet zuerst in `MailNachrichten`. `MailVersandDienst` wiederholt Fehlversuche und prüft stündlich die Abo-Mails; zeitgesteuerte Mails je Datensatz nur einmal.
- **Schalter** (`MailArten`, Tabelle `MailRegeln`): Pflicht-Mails gehen immer (Mot de passe oublié, Compte créé, Établissement validé, Nouvelle facture, Caisse suspendue, Paiement non retrouvé, Changement de tarif). Alle anderen sind abschaltbar; an: Rappel, Nouvel établissement (Admin), Paiement signalé (Admin), Statut établissement; aus: Bienvenue, Paiement confirmé, Nouveau caissier, Mot de passe modifié, Fin de l'essai.
- Manuelle Nachrichten (`NachrichtenService`) an Personen, alle Besitzer, Besitzer oder Teams bestimmter Läden oder ProduktOwner; jede Mail einzeln, max. 200 pro Versand. Vorlagen in `TabloSuiteMails`; Monatszähler gegen `Mail:MonatsLimit` (Standard 100).

### Vidéos d'aide & Werbevideo
- `/aide/videos` (Menü « Aide » + Benutzermenü, `VideoKatalog`): Erklärvideos Caissier (2:30), Propriétaire (2:27), Responsable (1:04), Administrateur (2:00). Sichtbar: Admin/Responsable alle, Propriétaire seins + Caissier, Caissier nur seins.
- Werbevideo « Une journée au salon » (1:30, mit Sprecherin) auf der öffentlichen Startseite.
- Dateien in `Medien/videos/` (nicht `wwwroot`), ausgeliefert über `/medien/videos/{datei}` mit Range-Unterstützung, Rollenprüfung (fremde Videos 403, ohne Anmeldung 401, Werbevideo öffentlich) und `?v=<Dateidatum>` gegen veralteten Browser-Cache. Vorschaubilder in `wwwroot/videos/*.jpg`.
- Herstellung außerhalb des Repos: `C:\ETSE\NETAPPS\TabloSuite Videos\ENote Videos\projekte\erklaer\README.md` (Aufnahmen → `skripte.json` → `tts.py` → `bau.py` → Render → `fertig.py`) und `projekte\werbung-lang\promo.py`. Stimme Azure Vivienne (−4 %, « eNOTE » als « eunote »).

### Öffentliche Startseite
- `Components/Shared/Startseite.razor` (über `Home.razor` für nicht Angemeldete): Hero mit echten App-Bildern, Zahlwege aus den Mobile-Money-Stammdaten, Funktionen, Werbevideo, drei Schritte, Preise aus der Datenbank, FAQ; sanfte Scroll-Animation (`enote.js`).

### Dunkelmodus
- Benutzermenü → « Apparence »: Clair / Sombre / Auto, gespeichert in `localStorage` (`enote-theme`), gesetzt vor dem ersten Zeichnen (`<html data-theme>`). Farben nur über Tokens (`--enote-primary-text`, `--enote-danger-text`, `--enote-warning-text`, `--enote-accent-text`, `--enote-glass`, `--enote-inverse-bg` …); der Bon bleibt immer weiß.

## 5. Lokal starten

1. **Voraussetzungen:** .NET 10 SDK, SQL Server lokal, User Secrets für den Seed-Admin.
2. **Datenbank:** `appsettings.Development.json` → `Server=localhost;Database=ENOTEDB;Trusted_Connection=True…`. Migrationen: `dotnet run --project eNOTE/eNOTE.WebApp --no-launch-profile -- --environment Development --database-admin=migrate --database-target=localhost/ENOTEDB`.
3. **Seed:** `--database-admin=seed` (Rollen, SuperAdmin aus `Seed:SuperAdminEmail` / `Seed:SuperAdminPasswort` / optional `Seed:SuperAdminBenutzername`, Standardpreise). Ohne Zugangsdaten wird kein Admin angelegt.
   **Demo (nur Development):** `--database-admin=demo` legt « Salon Élégance (Démo) » neu an: Katalog, Team, Remises, Ausgaben, 6 Wochen Verkäufe, 14 Beispielkundinnen, heute offene Kasse von Awa. Zugänge (Passwort jeweils `demo2026`): `demo.proprio` (Besitzer-PIN 1234, Kassen-PIN 1000), `demo.awa` (PIN 2222), `demo.kofi` (PIN 3333), `demo.po` (Responsable), `demo.admin` (SuperAdmin, nur lokal). Screenshot-Rundgang: [`tools/ui-rundgang/README.md`](tools/ui-rundgang/README.md).
4. **Start:** `dotnet run --project eNOTE/eNOTE.WebApp` → https://localhost:62148 (Environment Development).
5. **Tests:** `dotnet test eNOTE/eNOTE.Tests`. Läuft die App parallel in Visual Studio (gesperrte `bin`-Dateien), in eigene Ordner bauen: `-p:OutDir=<Temp>/enote-tests/` bzw. `-p:OutDir=<Temp>/enote-verify/`. Eine so gestartete Instanz liest `Medien/videos` aus ihrem Build-Ordner.

## 6. Architektur-Regeln & Konventionen

- **IDbContextFactory** für jede DB-Operation; Rechte serverseitig im Service (`LadenZugriff`, `AdminKontenService.PruefeAsync`).
- **Deutsch** für Code/Core-Modelle, **Französisch** für alle sichtbaren Nutzertexte (« vous »).
- ID-Benennung: `Id` für die Basis, zusammengesetzt `…Id` (z. B. `SalonId`, `VerkaufId`).
- Bei Modelländerungen EF-Migration erstellen; Geldbeträge immer mit `HasPrecision(18, 2)`.
- Neue Services in `Program.cs` registrieren und die Seite einmal live öffnen (Unit-Tests registrieren Dienste selbst und merken eine fehlende Registrierung nicht).
- Design-System „Soft Modern“ in `wwwroot/app.css`: Tokens in `:root` und `html[data-theme="dark"]` (inkl. farbsehschwächen-geprüfter Zahlart-Farben `--enote-pay-*`), keine Farben hart codieren. Icons nur über `<Icon Name="…" />` (SVG, offline), keine Emojis. Wiederverwendbar: `PinEingabe`, `BetragEingabe`, `AnbieterAuswahl`, `ZahlungsTag`, `SaeulenDiagramm`, `ZahlungsMix`, `KundenAuswahl`, `TarifAnpassung`, `AnkuendigungsBanner`. Präfixe je Bereich: `kx-` Kasse, `ku-` Kunden, `db-` Dashboard, `fi-` Finanzen, `dp-` Dépenses, `pr-` Prestations, `rm-` Remises, `eq-` Équipe, `st-` Paramètres, `ab-` Abonnement, `ad-` Admin, `ak-` Administrateurs, `ta-` Tarifs, `an-` Annonces, `em-` E-mails, `vh-` Vidéos, `lp-` Startseite, `mm-` Mobile-Money-Admin.
- Responsiv: Desktop ≥ 1200 px Sidebar, Tablet 641–1199 px Icon-Leiste, Handy ≤ 640 px untere Leiste + « Plus »; Kasse ≤ 900 px mit aufklappbarer Ticket-Leiste; Reiter brechen auf dem Handy um. Keine horizontalen Scrollbars (`minmax(0, 1fr)`), Touch-Targets ≥ 44 px, `EnoteSelect`/`EnoteDatePicker`/`EnoteCheckbox`, Modals über `ModalDialog` (Escape schließt), async Aktionen über `AsyncActionButton`, Toasts blenden sich selbst aus.
- Scoped-States für circuit-weite Zustände (`KassenAnzeigeState`, `AktiverSalonState`, `BesitzerPinState`): `OnChange` abonnieren, `IDisposable`/`IAsyncDisposable`.
- Visuelle Prüfung per Headless-Browser (Playwright/Edge, `tools/ui-rundgang`) gegen den Demo-Laden; Aufnahmen anonymisieren (E-Mails maskieren, echte Namen ersetzen).

## 7. Wichtige Abläufe (Kurzüberblick)

- **Verkauf:** Kasse starten → Kachel/Scan → (optional Kunde) → Ticket → Espèces / Mobile Money / Carte / Mixte → bestätigen → Bon, Bestand mindern, Live-Umsatz pushen.
- **Rückerstattung:** Kasse → Ventes → « Rembourser » → Zeilen/Mengen, Zahlweg, Motiv → Besitzer-PIN → Gegenbon.
- **Kassenabschluss:** « Clôturer » → Espèces comptées → Écart + Notiz → Sitzung abgeschlossen, zurück zum Start.
- **Neuer Kunde:** Kasse → « Client » → Name tippen → « + Nouveau client » → optional Telefon mit Zustimmung → am Ticket.
- **Zweiter Laden:** Paramètres → « Nouvel établissement » (neues Besitzerkonto) → Admin gibt frei.
- **Tarif ändern:** Administration → Tarifs → « Modifier le tarif » → Betrag + Startmonat → Vorschau → Textvorlage → planen (Banner, optional Mail).
- **Wartung ankündigen:** Administration → Annonces → « Mise à jour planifiée » → Tag + Uhrzeit → Vorlage → veröffentlichen.
- **Azure-Deploy mit Datenbankänderung:** Migrationshistorie lesen → Skript erzeugen und prüfen → nach OK ausführen → prüfen → Publish (siehe [Deployment-Azure.md](Deployment-Azure.md) §5).
