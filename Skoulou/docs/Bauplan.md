# SKOULOU – Bauplan für den Neubau

Stand 13.11.2026. Verbindlich für alle, die am Neubau arbeiten. Fachliche Details (Regeln, Texte, Formeln der Alt-App) stehen in `docs/fachkonzept/01…05`. **Wo dieser Bauplan etwas anders entscheidet als die Alt-App, gilt der Bauplan.**

Leitbild: SKOULOU soll sich anfühlen wie eNOTE – aus einem Guss, ruhig, schnell, klar. Kleine Seiten, Logik in getesteten Services, ein Design-System, eine Art Listen/Dialoge/Formulare zu bauen. Zielmarkt Togo/Westafrika: langsame Netze, günstige Handys, gemeinsame PCs in Schulen, viele Nutzer ohne E-Mail.

---

## 1. Projekte und Ordner

```
Skoulou/
  Skoulou.Core/        Domäne ohne Infrastruktur: Entitäten, Enums, reine Rechner (Noten, Rang, Gebühren,
                       Plattformanteile, Zahl in Worten, Nummernformate), Kultur/Zeitzone. Keine EF-Abhängigkeit
                       außer Attributen; keine Services mit DB.
  Skoulou.WebApp/      Blazor Server (.NET 10, Interactive Server), EF Core SQL Server, Identity, Services, Seiten.
    Data/              SkoulouDbContext, Konfiguration je Bereich (IEntityTypeConfiguration), Migrations/, Seed/
    Services/<Bereich>/ Anwendungsdienste je Bereich (Plattform, Organisation, Paedagogik, Noten, Finanzen, Infrastruktur)
    Components/
      Layout/          MainLayout, NavMenu (rollenbasiert), AuthLayout, DruckLayout
      Shared/          Design-System-Bausteine (siehe §7)
      Pages/<Bereich>/ eine Seite = eine Aufgabe; große Bereiche in Unterkomponenten
    wwwroot/           app.css (Design-System), js/ (klein, modular), lib/ (Quill, KaTeX, selbst gehostet – kein CDN)
    Medien/videos/     Lernvideos (Range-Endpunkt, wie eNOTE)
  Skoulou.Tests/       xUnit; InMemory für Service-Regeln, SQL-Server-Tests opt-in, Simulation opt-in
  docs/                Fachkonzept, Bauplan, Handbuch-Texte
```

Namespaces `Skoulou.Core.*`, `Skoulou.WebApp.*`, `Skoulou.Tests`. Code-Bezeichner **deutsch** (wie eNOTE), UI-Texte **französisch** mit « vous ». Keine englisch/deutsch gemischten Namen.

Projektmappe `TabloSuiteMappe.slnx`: neuer Ordner `/Skoulou (neu)/` mit den drei Projekten, solange die Alt-App existiert. Bei der Umstellung werden die `SKOULOUAPP.*`-Projekte entfernt.

Lokale Datenbank: **`SkoulouDB`** (Windows-Auth, wie die Alt-App `SKOULOUAPPDB`, die unangetastet bleibt). Lokale Ports: `https://localhost:7164`, `http://localhost:5115` (Alt-App behält 7064/5015).

---

## 2. Technische Grundlagen

### 2.1 Konfiguration – gleiche Schlüssel wie die Alt-App (Azure-Settings bleiben gültig)
Siehe Fachkonzept 01 §5.5. Übernommen: `Database:ConnectionStringName`, `ConnectionStrings:DefaultConnection`, `ConnectionStrings:AzureDefaultConnection`, `Database:ApplyMigrationsOnStartup`, `Seed:SuperAdminEmail`, `Seed:SuperAdminPasswort`, `Seed:SuperAdminIdentifiant`, `Seed:SuperAdminName`, `App:BaseUrl`, `Postmark:Enabled|ServerToken|FromEmail|FromName|SupportEmail|MessageStream|InboundWebhookSecret|WeiterleitungEnoteUrl|WeiterleitungEnoteGeheimnis`, `Storage:*` (= `AzureBlobStorage:*`, `Storage` gewinnt): `ServiceUri`, `BlobConnectionString` (Fallback `ConnectionStrings:BlobConnection`), `ContainerName`, `FinanzBelegContainerName`, `LocalDataRootPath`.
Neu: `App:Zeitzone` (Standard `Africa/Lome`). Gestrichen: `OpenAI:*`, `MediaStorage:*`, `LessonPdf:BrowserPath`, `AzureBlobStorage:Enabled`.
KI-Hilfe « Poser une question » (19.11.2026, `Services/KiHilfe`): `Ki:AnthropicApiKey` – in Azure als App-Einstellung `Ki__AnthropicApiKey` (geheim, nie in appsettings/Repo, nie im Log); ohne Schlüssel sind Menüeintrag und Seite unsichtbar. **Zunächst nur für Administrateur und Responsable, ohne Fragen-Grenzen** (Nutzer 19.11.2026); `Ki__AlleRollen=true` schaltet alle Rollen frei. Optional: `Ki__Modell` (Standard `claude-haiku-5-5`), `Ki__MaxTokens` (2048), `Ki__TimeoutSekunden` (20), `Ki__WorkspaceId` (nur bei Schlüsseln ohne Workspace – `sk-ant-usr…`/`sk-ant-svc…` –: `wrkspc_…` aus der Console, Header `anthropic-workspace-id`; 20.11.2026). Kosten begrenzt das Ausgabelimit des Schlüssels in der Anthropic Console.
Optionen mit `ValidateOnStart`; in Production Abbruch, wenn `App:BaseUrl` fehlt.

### 2.2 Querschnitt
- **Kultur:** `SkoulouKultur` fr-FR mit Tausenderpunkt (« 150.000 FCFA ») wie heute; global beim Start gesetzt.
- **Zeit:** alle Zeitpunkte in UTC speichern, Kalenderdaten als `DateOnly`; Anzeige/Eingabe über `SchulZeit` (Zeitzone der Schule, Standard `Africa/Lome`). `TimeProvider` injizieren (testbar).
- **Beträge:** ganze FCFA als `long` (Betrag-Typ `Fcfa` optional als record struct). Keine Dezimalen bei Geld.
- **Logging:** Serilog (Konsole) + gepufferter, asynchroner DB-Sink `FehlerProtokoll` (Retention 90 Tage, Hintergrundbereinigung).
- **Mail:** Postmark-HTTP wie eNOTE; **Outbox** (`MailAusgang`) + Hintergrunddienst mit Wiederholung; Betreff-Präfix « [SKOULOU] »; ein gemeinsames HTML-Layout; Absender `Postmark:FromEmail`. Eingang: Webhook `/api/postmark/inbound` (Basic `skoulou:<Geheimnis>`), eigene Absender ignorieren, eNOTE-Weiterleitung bei « eNOTE » im Betreff (Regeln wie heute, Fachkonzept 01 §3.14), fremdes `To` → 200 + ignorieren.
- **Speicher:** `IMedienSpeicher` (Azure Blob mit Managed Identity/Connection String; lokal in Development unter `LocalDataRootPath` außerhalb wwwroot). Private Ablage; Auslieferung nur über geprüfte Endpunkte. Pfadschema `schulen/{schuleId:N}/{zweck}/{mediumId:N}`. Upload: Größenlimit serverseitig, Typ per Magic Bytes, Länge serverseitig gezählt.
- **PDF:** QuestPDF (Community) für Bulletin, Quittung, Plattformrechnung, Finanzbericht, Schülerkarten, Zugangsliste, Stundenplan (Wochenraster). Lektionen/Examen: **Druckansicht im Browser** (`/druck/...` mit Druck-CSS, KaTeX gerendert, `window.print()`) – kein Chrome auf dem Server.
- **Hintergrunddienste:** `MailVersandDienst`, `RechnungsDienst` (Plattformrechnungen nach Periodenende, Erinnerungen), `AufraeumDienst` (Fehlerprotokoll, verwaiste Medien), `PruefungsAbgabeDienst` (Auto-Abgabe abgelaufener Examen). Jeder Lauf idempotent.
- **Sicherheit:** Identity-Lockout (5 Fehlversuche / 15 min); Rate-Limiter je IP für Login, Registrierung, Passwort vergessen, Kontakt, Webhook, Bulletin-Prüfung; Antiforgery überall (auch Logout); Access-Denied-Seite für Angemeldete ohne Recht; HTML wird **beim Speichern** mit `Ganss.Xss.HtmlSanitizer` bereinigt (nur interne Medien-URLs, keine externen Bilder); Security-Header.
- **Sitzung:** eine aktive Sitzung je Konto (`AktiveSitzungId`) wie heute; Prüfung im Cookie-Event mit 60-s-Cache je Konto (kein DB-Zugriff pro Request) + Revalidierung im Circuit.
- **Statische Dateien:** keine CDNs. Quill 2, KaTeX, (kein flatpickr – eigener Datepicker wie eNOTE) unter `wwwroot/lib`.

### 2.3 Programmstart
`Program.cs` schlank: `builder.Services.AddSkoulouInfrastruktur(config, env)` + je Bereich `AddPlattform()`, `AddOrganisation()`, `AddPaedagogik()`, `AddNoten()`, `AddFinanzen()` (Extension-Methoden in der jeweiligen `Services/<Bereich>/…Registrierung.cs`). Endpunkte ebenso je Bereich `MapXyzEndpunkte()`.
Start: Migrationen nur bei `Database:ApplyMigrationsOnStartup`; Rollen anlegen; Seed-Admin aus `Seed:*` nur anlegen, falls er fehlt (nie ändern); Plattform-Einstellungen anlegen, falls leer. Kommandozeile `--seed-demo` legt die Demo-Schule an (nur Development/explizit).

---

## 3. Rollen, Konten, Mandant, Rechte

### 3.1 Rollen (genau **eine** Rolle je Konto)
| Code | UI | Schule |
|---|---|---|
| `SuperAdmin` | Administrateur | keine (plattformweit) |
| `Responsable` | Responsable SKOULOU | keine; betreut Schulen über `Schule.ResponsableId` |
| `Direktor` | Directeur / Direction | genau eine |
| `Koordinator` | Coordinateur | genau eine |
| `Lehrer` | Enseignant | genau eine |
| `Schueler` | Élève | genau eine |
| `Eltern` | Parent | genau eine (Kinder derselben Schule) |

Direktor und Koordinator dürfen **zusätzlich unterrichten** (sie können Lehraufträge haben) – ohne zweite Rolle. „Hauptrolle“ ist die einzige Rolle; eine Funktion `Rollen.Bezeichnung(rolle)` für alle Texte.

### 3.2 Konto
`Konto : IdentityUser<Guid>`: `UserName` = **Identifiant** (klein, ohne Akzente, 3–40, `[a-z0-9._-]`, Vorschlag `vorname.nachname`, dann `…2`), `Vorname`, `Nachname`, `Email` (optional, **mehrfach erlaubt**, nicht eindeutig; Pflicht für Direktor/Responsable/SuperAdmin), `Telefon`, `Geschlecht`, `Geburtsdatum`, `Status` (`EnAttente | Actif | Bloque | Archive`), `SchuleId?`, `MussPasswortAendern`, `AktiveSitzungId`, `FotoMediumId?`, `AgbVersion?`, `LetzteAnmeldungUtc`, `ErstelltVonId?`, `SelbstRegistriert`.
- Startpasswörter: **8 Zeichen, gut lesbar** (ohne 0/O/1/l/I), einmal angezeigt bzw. als **Zugangsliste-PDF** gedruckt; `MussPasswortAendern = true` → Pflichtdialog beim ersten Login.
- Passwortregeln: min. 6, eine Ziffer, ein Kleinbuchstabe (wie heute).
- Zurücksetzen durch die Direktion: neues Startpasswort, beendet Sitzungen, **Audit-Eintrag**.
- Login-Meldungen getrennt: falsche Daten / Konto gesperrt / Schule suspendiert / Anmeldung in Prüfung.

### 3.3 Schule als Mandant
`Schule.Status`: `EnAttente` (selbst registriert, wartet auf Admin) → `Active` | `Refusee`; `Active` ↔ `Suspendue` (nur Admin, mit Grund und Mail an die Direktion). Konten einer nicht aktiven Schule können sich nicht anmelden (Ausnahme: Direktor einer `EnAttente`-Schule sieht eine Warteseite). Schülerregistrierung nur bei `Active`. Ein gesperrter Direktor sperrt **nicht** die Schule.

**Mandantenkontext:** Jede Schul-Operation läuft über `SchulKontext` (aus dem angemeldeten Konto: KontoId, Rolle, SchuleId, Rechte). Services laden den Akteur **immer selbst** aus der DB über `Zugriff.LadeAkteurAsync(kontoId)` und prüfen Fähigkeit + Schulzugehörigkeit der Ressource. Kein Service vertraut UI-Flags; kein „Guid.Empty = erlaubt“.

### 3.4 Fähigkeiten (zentral in `Zugriff`, getestet als Matrix)
`enum Faehigkeit`: SchuleVerwalten, KontenVerwalten (Lehrer/Schüler/Eltern), KoordinatorenVerwalten, SchuljahrVerwalten, KlassenVerwalten, StundenplanVerwalten, AnwesenheitErfassen, NotenErfassen, NotenAllerFaecher, KlassenratErfassen, BulletinsVeroeffentlichen, InhalteErstellen, InhalteFuerLehrerErfassen, Korrigieren, FinanzenEinsehen, ZahlungenErfassen, BuchhaltungErfassen, GebuehrenVerwalten, RechnungSkoulouBearbeiten, PlattformVerwalten, SchulenBetreuen.

| Fähigkeit | Direktor | Koordinator | Lehrer | Schüler/Eltern | Responsable | SuperAdmin |
|---|---|---|---|---|---|---|
| Schule (Stammdaten, Logo, Dokumente) | ja | nein | – | – | lesen | ja |
| Konten Lehrer/Schüler/Eltern | ja | ja | – | – | – | ja |
| Koordinatoren + deren Rechte | ja | nein | – | – | – | ja |
| Schuljahr, Perioden, Klassen, Fächer, Lehraufträge, Stundenplan, Jahreswechsel | ja | ja | – | – | – | – |
| Anwesenheit | ja | ja | eigene Klassen (Lehrauftrag) | – | – | – |
| Noten | alle | alle | eigene Klassenfächer | lesen eigene | – | – |
| Conseil, Bulletins veröffentlichen/berichtigen | ja | ja | Klassenlehrer: Conseil vorbereiten, Bulletins seiner Klasse lesen | eigene lesen | – | – |
| Inhalte (Leçons, Examens) | für Lehrer erfassen, alle lesen | dito | eigene erstellen/zuweisen | zugewiesene bearbeiten | – | – |
| Korrigieren | ja (für Lehrer) | ja | eigene Zuweisungen | – | – | – |
| Finanzen einsehen | ja | Recht `FinanzenEinsehen` | – | eigene Gebühren/Quittungen | seine Schulen lesen | alle lesen |
| Zahlungen erfassen / stornieren | ja | Recht `ZahlungenErfassen` | – | – | – | – |
| Buchhaltung (Écritures) | ja | Recht `BuchhaltungErfassen` | – | – | – | – |
| Gebührenposten | ja | Recht `ZahlungenErfassen` | – | – | – | – |
| Rechnung SKOULOU melden/anfechten | ja | Recht `RechnungSkoulou` | – | – | lesen | bestätigen, stornieren, Preise |
| Plattform (Schulen validieren, Konten, Preise, Annonces, Messagerie, Journal) | – | – | – | – | Konten seiner Schulen freischalten, Direktoren anlegen, Messagerie | ja |

Koordinator-Rechte = Flags am Konto (`KoordinatorRechte`: FinanzenEinsehen, ZahlungenErfassen, BuchhaltungErfassen, RechnungSkoulou), gesetzt nur vom Direktor.

### 3.5 Audit
`AuditEintrag` (Zeit, AkteurId, SchuleId?, Aktion, ZielTyp, ZielId, Text) für: Kontoanlage/-sperre/-archiv, Passwort-Reset, Rollen-/Rechteänderung, Notenänderung (zusätzlich eigene Historie), Veröffentlichung/Berichtigung, Zahlung/Storno, Rechnungsstatus, Schulstatus, Jahreswechsel. Anzeige: Direktion (eigene Schule), Admin (alle).

---

## 4. Datenmodell

Alle Entitäten: `Id` (Guid, sequenziell erzeugt), `ErstelltAmUtc`, `GeaendertAmUtc`. Schulgebundene Entitäten tragen `SchuleId` (Index). **Keine kaskadierenden Löschungen** auf Noten-, Bulletin-, Finanz- und Versuchsdaten (Restrict); Archivieren statt Löschen. Eindeutigkeiten als echte Unique-Indizes (mit SQL-Test).

### 4.1 Plattform
- `Schule`: Name (eindeutig normalisiert), Ort, Land („Togo“), Status, Zeitzone, `ResponsableId?`, `MonatspreisOverride?` (FCFA je Schüler und Monat; bis 19.11.2026 `JahrespreisOverride`), Dokumentdaten (`OffizielleBezeichnung`, `Slogan`, `Adresse`, `Telefon`, `Email`, `Zulassungsnummer`, `Ministerium`, `RepublikZeile`, `StaatsMotto`, `DirectionRegionale`, `Statut`), `LogoMediumId?`, `MatrikelPraefix`, `AbsenzOhneEntschuldigungZaehltNull` (bool, Standard true), `ResponsableProzent?`.
- `PlattformEinstellung` (eine Zeile): `MonatspreisProSchueler` (Standard 500 je Schüler und Monat, seit 19.11.2026; vorher `JahrespreisProSchueler` 3.600 bzw. 12.500), `ZahlungszielTage` (30), `ResponsableProzentStandard` (0), `AgbVersion`.
- `Nummernkreis` (SchuleId?, Art, Jahr, Letzte) – fortlaufende Nummern: Quittung `REC-2026-00042` (je Schule+Jahr), Écriture `ECR-2026-00042`, Bulletin `BUL-2026-00042`, Plattformrechnung `SKL-2026-0001` (global), Matrikel `{Präfix}{JJ}{0001}`. Vergabe in derselben Transaktion (Update mit Sperre).
- `Ankuendigung`: Titel, InhaltHtml, Stufe (Info/Wichtig), Zielrollen [Flags], `SchuleId?`, StartUtc, EndeUtc. Gecacht (MemoryCache, Invalidierung beim Speichern).
- `Nachricht` (Messagerie): Richtung (Eingang/Ausgang), Von, An, Betreff, TextHtml (bereinigt), `SchuleId?` (auto-zugeordnet über bekanntes Absenderkonto), `GelesenAmUtc`, `AntwortAufId?`, `PostmarkId`. Kontaktformular → Nachricht (Eingang) + Mail an Support.
- `MailAusgang` (Outbox): An, Betreff, Html, Text, Status, Versuche, NächsterVersuch, Fehler, Bezug.
- `FehlerProtokoll`, `AuditEintrag`.
- `Medium`: SchuleId?, BesitzerId, Art (Bild/Video/Dokument), Zweck (Inhalt/Logo/Foto/Beleg/Video), BlobName, Dateiname, ContentType, Bytes, DauerSek?, Breite?, Hoehe?. `MedienNutzung` (MediumId, Typ, ZielId) → Löschschutz und Zugriffsprüfung (Schüler darf ein Bild sehen, wenn es in einem ihm zugewiesenen Inhalt genutzt wird).
- `ResponsableAnteil` (nur falls die Vergütung bestätigt wird, siehe §11): ResponsableId, RechnungId, Prozent, Betrag, AusgezahltAmUtc.

### 4.2 Organisation
- `Schuljahr`: SchuleId, Bezeichnung (« 2026-2027 », eindeutig je Schule), Beginn, Ende (keine Überlappung), Status (`Vorbereitung | Laufend | Abgeschlossen`), PeriodenArt (`Trimestre | Semestre | Session | Libre`), `PlattformMonatspreis?` (beim Start eingefroren), `AbgeschlossenAmUtc`, `WiedereroeffnetGrund?`. Höchstens ein Jahr `Laufend` je Schule.
- `Periode`: SchuljahrId, Nr (1…), Titel (« 1er trimestre »), Beginn, Ende (innerhalb des Jahres, ohne Überlappung), `Jahresgewicht` (Standard 1), `ZaehltFuerJahr` (false = « Examen blanc »), `Status` (`Offen | Geschlossen`). Abrechnungsperioden = Perioden mit `ZaehltFuerJahr`. Sessions (Sprachschulen) sind Perioden mit Art Session.
- `Stufe` (« Niveau »): SchuleId, Name (« 6e »), Reihenfolge, `NachfolgerId?` (für Versetzungsvorschläge), `Zyklus?` (Primaire/Collège/Lycée/Formation).
- `Klasse`: SchuljahrId, SchuleId, StufeId?, Name (1–60, eindeutig je Jahr), `PeriodeId?` (Klasse nur für eine Session), Kapazitaet?, `KlassenlehrerId?` (Professeur principal), Raum?. **Klassen gehören zu einem Schuljahr**; der Jahreswechsel legt die neuen Klassen an.
- `Fach`: SchuleId, Name (eindeutig), Kuerzel, Gruppe (`Lettres | Sciences | Autres | Aucune`), Reihenfolge (mit Editor), Aktiv.
- `Klassenfach`: KlasseId, FachId, Koeffizient (>0, 2 Dezimalen), Reihenfolge?, `IstWahlfach` (« Matière facultative », Standard false), `WahlfachRegel` (`PunkteUeber10` Standard | `WieNormal` | `NurInformation`, siehe §5 Regel 2a). Eindeutig (Klasse, Fach). Kopieren, « Appliquer au niveau » und Jahreswechsel übernehmen die Wahlfach-Einstellung (nicht die Wahl der Schüler).
- `Fachwahl` (seit 19.11.2026): KlassenfachId, SchuelerId, ErfasstVonId. Eindeutig (Klassenfach, Schüler). Wahl eines Wahlfachs **für das ganze Schuljahr**, gesetzt von der Direktion (Fähigkeit `KlassenVerwalten`) in Classes › « Matières et enseignants » › « Matières facultatives ». Klassenwechsel überträgt die Wahl auf dasselbe Fach der neuen Klasse, wenn es dort auch Wahlfach ist; die Wahl in der alten Klasse bleibt für deren Perioden. Ausschalten des Wahlfachs löscht keine Wahl (sie zählt dann nur nicht).
- `Lehrauftrag`: KlassenfachId, LehrerId (Konto mit Rolle Lehrer/Direktor/Koordinator), Wochenstunden. Eindeutig (Klassenfach, Lehrer). **Einzige Quelle** für: welche Klassen/Schüler ein Lehrer sieht, Notenrecht, Lehrername im Bulletin, Stundenplan.
- `SchuelerProfil` (1:1 Konto): Matrikel (eindeutig je Schule, automatisch lesbar), Geburtsort, Nationalitaet, IstInterne, IstAffecte, ElternTelefon, ElternEmail, `StimmeAus` (gesprochenes Feedback).
- `Einschreibung`: SchuelerId, SchuljahrId, KlasseId, Von, Bis?, Status (`Active | Changee | Partie | Terminee`), IstWiederholer, Austrittsgrund?. Höchstens eine `Active` je Schüler. Klassenwechsel = alte mit Bis/`Changee` schließen + neue anlegen (eine Aktion « Changer de classe »). **„Aktuelle Klasse“ wird nur hieraus abgeleitet.**
- `ElternKind`: ElternId, SchuelerId, Beziehung (« Mère », « Père », « Tuteur », « Autre »).
- `Stundenplan`: KlasseId, GueltigAb, GueltigBis?, Veroeffentlicht; `StundenplanEintrag`: StundenplanId, LehrauftragId, Wochentag, Beginn, Ende, Raum. Konfliktprüfung Lehrer und Raum.
- `Anwesenheit`: EinschreibungId, Datum, Status (`Present | Absent | Retard`), Entschuldigt, Minuten, Bemerkung, ErfasstVonId. Eindeutig (Einschreibung, Datum). Erfassung als **Klassenappell** (Standard « présent »).

### 4.3 Noten, Conseil, Bulletins
- `PeriodenKomponente` (Schema-Vorlage je Periode): Titel (« Interrogation », « Devoir », « Composition »), Art, Gewicht, Bareme. Dient als Vorschlag beim Anlegen von Bewertungen.
- `Bewertung` (Évaluation): KlassenfachId, PeriodeId, Titel, Art (`Interrogation | Devoir | Composition | Oral | TP | Autre`), Datum?, Bareme (Standard 20), Gewicht (Standard 1), ErstelltVonId. Bearbeitbar/löschbar, solange keine Note existiert.
- `Note`: BewertungId, EinschreibungId, Punkte?, Status (`Notee | AbsentJustifie | Absent | Dispense`), ErfasstVonId. Eindeutig (Bewertung, Einschreibung). `NotenAenderung` (Historie, nur anhängen).
- `FachBemerkung` (Appréciation des Lehrers): KlassenfachId, PeriodeId, EinschreibungId, Text.
- `KlassenratEintrag`: PeriodeId, EinschreibungId, Distinktion?, Sanktionen [Flags], Bemerkung.
- `Jahresentscheidung`: EinschreibungId (letzte des Jahres), Entscheidung (`Offen | Admis | Redouble | Exclu | Orientation | Partie`), ZielStufeId?, Bemerkung. **Wird vom Jahreswechsel ausgeführt.**
- `BulletinVorlage`: SchuleId, StufeId? (null = Standard der Schule), Name, Farbe, Schalter (wie Alt-App), Schrift, Rand, Fusstext.
- `KlassenVeroeffentlichung`: KlasseId, PeriodeId, Status (`Publiee | EnRectification`), VeroeffentlichtAmUtc/VonId, RectificationGrund?.
- `Bulletin`: SchuleId, PeriodeId, EinschreibungId, Nummer (fortlaufend), PruefToken (64 hex), Status (`Publie | Remplace | Retire`), `ErsetztId?`, `IstRectificatif`, Grund?, SnapshotJson (ohne Bildbytes; Logo/Foto als unveränderliche Medium-Referenz), Hash.
- `BulletinUebergabe`: BulletinId, Art (Notification/Remise), Vermerk, VonId.

### 4.4 Pädagogik
- `Lecon`: SchuleId, AutorId, `ErfasstVonId?` (« Saisi par la direction »), FachId, StufeId, Titel, Zusammenfassung, Thema, VideoMediumId?, Status (`Brouillon | Prete`). `LeconSeite` (Nr, Html). Inhalte gehören zu **Stufe + Fach** und überleben Schuljahre.
- `Pruefung` (Examen): SchuleId, AutorId, ErfasstVonId?, FachId, StufeId, Titel, Beschreibung, DauerMinuten, Bestehensgrenze (%), Status.
- `AufgabenBlock`: LeconId **oder** PruefungId, Nr, Titel, Consigne.
- `Frage`: BlockId, Nr, Typ (`ChoixUnique | ChoixMultiple | Texte | ConstructionPhrase | TexteATrous | AudioTexte | AudioChoixUnique | AudioChoixMultiple`), TextHtml, Punkte, AudioText?, Sprache (fr-FR Standard), AkzeptierteAntworten (Liste), Luecken (Liste von Listen), Wortbank (Liste), `FrageOption` (Text, Richtig, Nr). Fragen sind nach der ersten Abgabe **unveränderlich** (Bearbeiten erzeugt neue Revision; alte Versuche bleiben gültig).
- `Zuweisung`: Inhalt (Lecon/Pruefung), KlasseId, LehrerId, Ab (Veröffentlichung), Frist?, `Versuchsregel` (`Illimite | MaxN` + `Wertung`: `Meilleur | Premier | Dernier`), `LoesungenSichtbar` (`Immediat | ApresFrist | Jamais`), Status (`Active | Archivee`). Examen: Fenster (Ab…Frist) + Dauer, genau ein Versuch, Lösungen nach Frist.
- `Versuch`: ZuweisungId, SchuelerId, Nr, StartUtc, AbgabeUtc?, DeadlineUtc?, Punkte, MaxPunkte, Status (`EnCours | Soumis | ACorriger | Corrige`). `Antwort`: VersuchId, FrageId, AntwortJson, Punkte, Korrektur (`Auto | Manuelle`), KorrigiertVonId?, Feedback. Eindeutig (Versuch, Frage).
- `LernStand` (materialisiert je Schüler × Zuweisung): BestePunkte, Prozent, Abgeschlossen, LetzteAktivitaet.
- `FormelFavorit`: KontoId, LaTeX, Bezeichnung (Limit 200 je Konto).

### 4.5 Finanzen
- `GebuehrenPosten`: SchuljahrId, SchuleId, Bezeichnung (« Inscription », « Scolarité », « APE », « Tenue »…), Betrag, Geltung (`StufeId?`, `KlasseId?`; beide null = alle), Pflicht, Reihenfolge. `GebuehrenRate`: PostenId, Nr, Betrag, Faellig (Summe = Postenbetrag).
- `Ermaessigung`: SchuelerId, SchuljahrId, PostenId?, Art (`Montant | Pourcentage`), Wert, Grund (« Bourse », « Fratrie »…), ErfasstVonId.
- `Zahlung`: SchuleId, SchuelerId, SchuljahrId, Betrag, Datum, Kanal (`Especes | MobileMoney | Virement | Cheque`), Referenz?, Quittungsnummer, Bemerkung, ErfasstVonId, `StorniertAmUtc/VonId/Grund`. Verteilung auf offene Raten nach Fälligkeit (ältestes zuerst, inkl. Rückstand Vorjahr) – berechnet, nicht gespeichert.
- `Kategorie`: SchuleId? (null = Systemkategorie), Name, Richtung (`Recette | Depense`), SystemCode? (`Scolarite`, `FactureSkoulou`, …).
- `Buchung` (Kassenjournal, nur echte Geldbewegungen): SchuleId, Datum, Richtung, KategorieId, Betrag, Text, Nummer, Herkunft (`Manuelle | Paiement | AnnulationPaiement | FactureSkoulou | Annulation`), HerkunftId?, BelegMediumId?, ErfasstVonId, `StornoVonId?`. **Nie löschen**; Korrektur = Gegenbuchung.
- `Plattformrechnung`: Nummer, SchuleId, SchuljahrId, PeriodeId, Datum, Faellig, Anzahl, Betrag, Status (`Ouverte | PaiementDeclare | Payee | Contestee | Annulee`), Grund?, `ErgaenztId?` (Nachtrag), BestaetigtVonId/Am. `Rechnungsposition`: RechnungId, SchuelerId, EinschreibungId, Betrag, `StorniertGrund?`. Eindeutig (PeriodeId, SchuelerId) über nicht stornierte Positionen. `Zahlungsmeldung`: RechnungId, Betrag, Datum, Referenz, BelegMediumId?, GemeldetVonId → Buchung `FactureSkoulou` (Ausgabe) in der Schule.

---

## 5. Zentrale Rechenregeln (Skoulou.Core, rein und unit-getestet)

1. **Fachmittel einer Periode** = Σ(Punkte/Barème·20·Gewicht)/ΣGewicht über Noten mit Status `Notee` (+ `Absent` als 0, wenn die Schule das so eingestellt hat); `AbsentJustifie`/`Dispense` zählen nicht; **fehlt eine Note (kein Eintrag)** → unvollständig (`null`). Keine Bewertung → kein Fachmittel (Fach erscheint als « Non évalué »).
2. **Moyenne générale** = Σ(Fachmittel·Koeff)/ΣKoeff über Fächer mit Mittel; ist ein Fach unvollständig → `null` (« Notes incomplètes »). Fächer « Non évalué » zählen nicht.
   2a. **« Matières facultatives »** (19.11.2026, z. B. Culture, Ewe in Togo): ein Wahlfach zählt nur für Schüler mit `Fachwahl`; nur sie werden darin erwartet, benotet, im Fachrang und in der Fachstatistik gezählt. Für alle anderen ist das Fach « non choisi » (Anzeige « — »): keine Note, nie « Notes incomplètes », nicht auf ihrem Bulletin.
   Moyenne = (Σ Punkte der Pflichtfächer + Σ Punkte « comme une matière normale » + Σ Bonus) / (Σ Koeff. der Pflichtfächer + Σ Koeff. « comme une matière normale »), **höchstens 20**.
   - « Seuls les points au-dessus de 10 comptent » (Standard): Bonus = (Fachmittel − 10) × Koeff., nur wenn > 0 (ungerundetes Fachmittel wie Regel 4); der Koeffizient zählt **nicht** zur Summe der Koeffizienten. Beispiel: Pflicht 64 Punkte / 5 Koeff. = 12,80; Ewe 16 (coef 2) → Bonus 12 → (64 + 12) / 5 = 15,20. Ewe 8 → Bonus 0 → 12,80 (kein Abzug).
   - « Compte comme une matière normale »: wie ein Pflichtfach mit Koeffizient ((64 + 32) / 7 = 13,71).
   - « Ne compte pas (information seulement) »: steht auf dem Bulletin, zählt nie (auch unvollständig nicht).
   Ein unvollständiges Wahlfach, das zählt (Bonus oder « normale »), macht die Moyenne unvollständig. Jahresmittel und Rang wirken über die Periodenmoyennes (Regeln 3 und 5). Bulletin: Zeile « TOTAUX » = Σ Koeff. (ohne Bonus-Koeffizienten) und Σ Punkte inkl. Bonus (`NotenRechner.Beitrag`); Bereich « MATIÈRES FACULTATIVES » mit kurzer Regel nur, wenn der Schüler mindestens ein Wahlfach gewählt hat; Gruppenköpfe (« MATIÈRES LITTÉRAIRES » …) und Gruppen-Bilan nur mit der Vorlage « ZeigeFachGruppen » und nur über Pflichtfächer. Ohne Wahlfächer bleiben Inhalt und Hash eines Bulletins wie bisher.
3. **Jahresmittel** = Σ(Periodenmittel·Jahresgewicht)/ΣJahresgewicht über Perioden mit `ZaehltFuerJahr`; fehlt eine → `null`. **Seit 22.11.2026 (Nutzer):** Perioden, in denen der Schüler in keiner Klasse der Schule eingeschrieben war (Neuzugang im Lauf des Jahres, Lücke zwischen Abgang und Rückkehr), zählen für ihn nicht – Jahresmittel und Jahresrang nur aus seinen Perioden; im Bilan steht die Periode als « — (non inscrit(e)) ». Fehlt in einer eingeschriebenen Periode eine Note, bleibt er « Notes incomplètes »; ein Klassenwechsel innerhalb der Schule zählt als eingeschrieben.
4. **Rundung:** intern ungerundet; Anzeige und **Schwellen** auf `Round(x, 2, AwayFromZero)` (gleicher Wert für Anzeige, Rang, Distinktion, Appréciation).
5. **Rang (eine Regel überall):** Wettbewerbsrang über die gerundeten Werte der **klassierten** Schüler (vollständige Werte); Gleichstand gleicher Rang mit « ex » (« 3e ex »), nächster übersprungen; Unvollständige « Non classé ». Effectif = aktive Einschreibungen der Klasse am Periodenende.
6. **Appréciation/Distinktion-Schwellen** wie Alt-App (Fachkonzept 04 §3.10).
7. **Plattformanteil je Periode** (seit 19.11.2026 Preis je Schüler und Monat statt Jahrespreis ÷ Perioden): Anteil = Monatspreis M (eingefroren am Schuljahr beim Start bzw. mit der ersten Rechnung eines nie gestarteten Jahres, Entscheidung 27; Schulpreis vor Plattformpreis, Standard 500) × Monate der Periode; Monate = max(1, round((Ende − Beginn + 1) / 30,4375, AwayFromZero)) – z. B. 01.10.–20.12. = 3, 01.10.–15.02. = 5. Ferien zwischen den Perioden kosten nichts; ein ganzes Schuljahr ≈ 10 Monate (≈ 5.000 bei 500). Abgerechnet wird weiter je Abrechnungsperiode (Examen blanc nicht; Probezeit zählt Perioden). Abrechnungspflichtig: jeder Schüler mit einer nicht stornierten Einschreibung, deren Zeitraum die Periode schneidet – **einmal je Periode**, auch bei Klassenwechsel. Rechnung nach Periodenende (Dienst, täglich) bzw. vom Admin vorzeitig; spätere rückwirkende Einschreibungen → Nachtragsrechnung. **Seit 21.11.2026 (Schulstart mitten im Jahr, Nutzerentscheidung):** Einschreibungen beginnen standardmäßig am Schuljahresbeginn – aber nur in der **Startphase** einer mitten im Jahr angemeldeten Schule (bis zum Ende ihrer Anmeldeperiode), sonst **heute** (Nachzügler einer etablierten Schule sind echte Neuzugänge; `EinschreibungsBeginn.Standard`); immer änderbar beim Anlegen/Import/Validieren, Korrektur im Dossier durch Direktor oder Koordinator, mit Journal; Perioden, die **vor der Anmeldung der Schule** (`Schule.ErstelltAmUtc`, bei der Registrierung nach der Uhr der App gesetzt) endeten, werden nie berechnet; die Periode der Anmeldung zählt erst **ab dem 1. des Anmeldemonats** (`PlattformAnteil.AbrechnungsBeginn`), z. B. 1er trimestre 01.09.–31.12., Anmeldung 21.11. → 2 statt 4 Monate. Rechnung, Vorschau « Facturer maintenant », PDF-Aufschlüsselung und Journal zeigen dieselben Monate.
8. **Gebühren je Schüler und Schuljahr:** Soll = Σ geltende Posten − Ermäßigungen (nie < 0); Raten mit Fälligkeit; Zahlungen werden nach Fälligkeit verteilt (Rückstand Vorjahr zuerst); « en retard » = fällige Raten nicht gedeckt; Überzahlung = Guthaben (nie stillschweigend). Rückstand am Jahresende bleibt Forderung (« Reste année précédente »), Abschreibung nur manuell mit Grund (nicht im Kassenjournal).
9. **Kassensaldo** = Σ Recettes − Σ Dépenses des Journals (nur echte Geldbewegungen). Offene Elternforderungen und offene SKOULOU-Rechnungen sind **eigene Kennzahlen**, nie Buchungen.
10. **Zahl in Worten** (französisch, bis 999.999.999) und Quittungs-/Bulletinnummern wie Alt-App-Tests (Fachkonzept 05 §6.5).
11. **Bewertungs-Engine** (eine für Übung und Examen): punktbasiert; ChoixMultiple mit Teilpunkten (richtige minus falsche, ≥ 0); Lücken mit Teilpunkten je Lücke; Textvergleich normalisiert (Groß/Klein, Akzente optional, Leerzeichen, typografische Zeichen); Texte ohne Musterlösung → manuelle Korrektur. Score einer Lektion/eines Schülers = Punkte-gewichtet über bewertete Versuche gemäß Versuchsregel.

---

## 6. Seiten und Navigation (französische Routen)

**Öffentlich:** `/` (Startseite mit Video, Funktionen, Ablauf), `/auth/connexion`, `/auth/inscription` (Direction oder Élève), `/auth/mot-de-passe-oublie`, `/auth/reinitialiser`, `/contact`, `/verification/{token}` (Bulletin-QR), `/conditions`.
Alt-Routen `/auth/login`, `/auth/register`, `/auth/forgot-password`, `/auth/reset-password`, `/verification-bulletin/{token}` leiten weiter (alte Links in Mails/QR bleiben gültig).

**Alle Angemeldeten:** `/profil`, `/aide/videos`, `/aide/guide`, `/contact`, `/acces-refuse`.

**SuperAdmin** (`/admin`): Tableau de bord · `/admin/ecoles` (validieren, suspendieren, Preis, Responsable) · `/admin/comptes` · `/admin/responsables` · `/admin/facturation` (alle Rechnungen, Bank bestätigen, stornieren, « Facturer maintenant ») · `/admin/tarifs` · `/admin/annonces` · `/admin/messagerie` · `/admin/journal` (Fehler) · `/admin/audit` · `/admin/medias`.
**Responsable** (`/responsable`): Mes écoles · Facturation de mes écoles · (Mes parts) · Messagerie.
**Direktor/Koordinator** (`/direction`): Accueil (Cockpit) · `/direction/eleves` · `/direction/classes` · `/direction/equipe` · `/direction/matieres` · `/direction/annee` (Schuljahr, Perioden, Komponenten, Jahreswechsel-Assistent) · `/direction/emplois-du-temps` · `/direction/parents` · `/direction/contenus` (Inhalte der Lehrer, « Saisir pour un enseignant ») · `/direction/ecole` (Stammdaten, Dokumente, Logo, Rechte der Koordinatoren) · `/direction/journal` (Audit).
**Unterricht (Lehrer + Direktion):** `/notes` · `/presences` · `/bulletins` (Direktion; Klassenlehrer lesend/Conseil) · `/enseignant` (Accueil) · `/enseignant/lecons` · `/enseignant/examens` · `/enseignant/corrections` · `/enseignant/emploi-du-temps`.
**Finanzen (Direktion mit Recht):** `/finances` (Übersicht) · `/finances/paiements` · `/finances/frais` · `/finances/journal` · `/finances/facturation` · `/finances/rapport`.
**Schüler** (`/eleve`): Accueil · `/eleve/lecons` · `/eleve/examens` · `/eleve/resultats` · `/eleve/bulletins` · `/eleve/emploi-du-temps` · `/eleve/frais`.
**Eltern** (`/parents`): Kinderwahl, je Kind Notes, Bulletins, Présences, Emploi du temps, Frais & reçus.
**Endpunkte:** `/medien/videos/{datei}`, `/medien/{id}`, `/bulletins/{id}/pdf`, `/bulletins/klasse/{klasse}/{periode}/pdf`, `/finances/recu/{zahlungId}`, `/finances/facture/{rechnungId}`, `/finances/rapport/pdf`, `/finances/justificatif/{buchungId}`, `/direction/cartes/{klasseId}`, `/direction/acces/{stapelId}` (Zugangsliste), `/druck/lecon/{id}`, `/druck/examen/{id}`, `/api/postmark/inbound`, `/auth/deconnexion` (POST).

Navigation: linke Leiste je Rolle mit Gruppen (wie eNOTE), oben Kopfzeile mit Schulname + **Schuljahr-Auswahl** (für Direktion/Lehrer; Standard = laufendes Jahr), Benutzer-Menü (Profil, Thème, Vidéos d'aide, Contact, Déconnexion). Handy: untere Leiste mit 4–5 Hauptzielen je Rolle.

---

## 7. UI-System (wie eNOTE, Marke SKOULOU)

- **CSS:** eine Datei `wwwroot/app.css` mit Tokens `--sk-*` (hell + dunkel über `[data-theme]`), keine Tailwind-/Bootstrap-Abhängigkeit, keine Inline-Farben. Marke: Primär `#1d4ed8` (Blau), Verlauf `#2563eb → #06b6d4` (Logo), Akzent Cyan `#0891b2`; Flächen/Text/Status wie eNOTE („Soft Modern“). Schrift Systemschrift wie eNOTE. Ziffern tabellarisch.
- **Bausteine (Components/Shared):** `SeitenKopf` (Titel, Untertitel, Aktionen, Icon), `Karte`, `Abschnitt`, `Reiter` (routenbasiert), `KennzahlKachel`, `Liste<T>` (Suche mit 350 ms Verzögerung, Filter-Chips, Server-Paging 50 + « Afficher plus », Scrollbereich `.sk-liste-scroll`, Leerzustand, Ladezustand), `Tabelle`, `ModalDialog` (schließt **nicht** bei Klick daneben), `BestaetigungsDialog`, `AsyncKnopf` (Doppelklickschutz, Spinner), `Toast`, `Icon` (SVG-Sprite), Formularfelder (`TextFeld`, `ZahlFeld`, `BetragFeld` FCFA, `DatumFeld` mit eigenem Picker, `AuswahlFeld` mit Suche, `MehrfachAuswahl`, `Schalter`, `RadioGruppe`, `PasswortFeld` mit Auge), `StatusBadge`, `NamensKreis` (Avatar-Initialen), `Leerzustand`, `LadeIndikator`, `Hinweis` (info/warn/fehler), `Druckknopf`.
- **Formulare:** Validierung im Service (Meldungen französisch, präzise), Anzeige am Feld; Speichern-Knopf unten rechts; Esc schließt Dialoge nicht ungewollt; keine Datenverluste bei Fehlern.
- **Seiten:** Razor ≤ ~250 Zeilen, Code-behind ≤ ~300 Zeilen; größere Bereiche als Unterkomponenten (`Pages/Direction/Eleves/…`). Jede Aktion lädt **nur das Geänderte** nach.
- **Listen wachsen:** jede potenziell große Liste serverseitig gesucht und geblättert (Muster `SucheAsync(…, suche, ueberspringen, anzahl=50) → (Eintraege, Mehr)` mit Take(anzahl+1)); Kennzahlen per COUNT/SUM.
- **Mobil zuerst** für Schüler/Eltern/Lehrer (Notenerfassung auch am Handy); Direktion/Finanzen PC-optimiert, aber nutzbar am Handy.
- **Barrierefreiheit:** Labels, Fokus sichtbar, Kontrast AA, Tastatur in Notenraster.
- **Notenerfassung als Raster** (Schüler × Bewertungen) mit Tastaturnavigation, Status-Kürzel (`abs`, `aj`, `disp`), Autosave je Zelle, Fortschritt.

---

## 8. Code-Konventionen

- **Service-Muster:** `public sealed class XyzService(IDbContextFactory<SkoulouDbContext> db, Zugriff zugriff, TimeProvider zeit, …)`; jede öffentliche Methode bekommt `Guid akteurId` als ersten Parameter, lädt den Akteur und prüft die Fähigkeit **vor** jedem Lesen/Schreiben; eigener DbContext je Operation; Schreibvorgänge in einer Transaktion; Nebenläufigkeit über Unique-Indizes/RowVersion statt globaler Locks (Sperren höchstens je Schule).
- **Ergebnisse/Fehler:** fachliche Fehler als `FachFehler` (Exception mit französischer Meldung) bzw. `Ergebnis<T>`; fehlende Rechte als `KeinZugriffFehler` → Seite zeigt Hinweis, Endpunkt 403. Seiten fangen beide zentral (Basisklasse `SkoulouSeite`).
- **DTOs:** Listen- und Detailzeilen als `record`s, per `Select` projiziert (kein Laden ganzer Graphen).
- **Keine toten Pfade:** nichts aus der Alt-App übernehmen, was nicht erreichbar ist (Abo/SKOULOU+, LevelEnrollment, Provisionen Lehrer/Direktion, System-Niveaus, Permissions-Tabellen, Audio-Test).
- **Kommentare** deutsch, kurz, nur wo nötig („warum“).

---

## 9. Tests

1. **Rechner-Tests** (Core): Noten, Rang, Rundung, Plattformanteile, Gebührenverteilung, Zahl in Worten, Bewertungs-Engine (jeder Fragetyp).
2. **Service-Tests** (InMemory, wie eNOTE `TestDbContextFactory`): je Service Rechte-Matrix (darf / darf nicht), Regeln, Fehlermeldungen.
3. **SQL-Server-Tests** (`SKOULOU_SQL_TESTS=1`): Migration, Unique-Indizes, Nebenläufigkeit (Nummernkreis, Doppelklick Zahlung, parallele Notenänderung), Abfragen übersetzbar. Zufällige DB `SKOULOU_TEST_*`, danach löschen.
4. **Simulation** (`SKOULOU_SIM=1`, Abschluss-Etappe): ≥ 20 Schulen (Primaire, Collège, Lycée, Sprachschule mit Sessions), 3 Schuljahre, mehrere Tausend Schüler; je Jahr: Schulregistrierung/Validierung, Personal, Klassen, Lehraufträge, Einschreibungen, Gebühren + Zahlungen + Stornos, Inhalte + Versuche, Noten + Conseil + Veröffentlichung + Berichtigung, Plattformrechnungen je Periode (Melden, Bestätigen, Anfechten, Storno), mehrere Admins/Responsables, Jahreswechsel mit Versetzung/Redoublement/Austritt. Prüft Invarianten (Summen, eindeutige Nummern, keine Doppelabrechnung, Ränge, Rechte) und misst Antwortzeiten der Listen.
5. **Browser-Rundgang** (Playwright, Edge headless) mit Bildern je Rolle.

---

## 10. Etappen

| Etappe | Inhalt | Ergebnis |
|---|---|---|
| E1 Fundament | Projekte, Program, Konfiguration, Identity/Login/Profil/Passwort, Sitzung, Zugriff/Fähigkeiten, gesamtes Datenmodell + erste Migration, Nummernkreise, Mail-Outbox, Medien-Speicher, Fehlerprotokoll/Audit, Design-System + alle Shared-Bausteine, Layout/Navigation aller Rollen (Platzhalterseiten), Startseite, Demo-Seed, Testinfrastruktur | App startet, Login aller Rollen, leere Bereiche |
| E2 Plattform | Registrierung + Validierung, Admin (Schulen, Konten, Responsables, Tarife, Annonces, Messagerie, Journal, Audit, Medien), Kontakt, Videos, Guide | |
| E3 Organisation | Schule, Schuljahr/Perioden, Stufen/Klassen/Fächer/Lehraufträge, Konten + Import (XLSX) + Zugangsliste, Einschreibungen/Klassenwechsel/Austritt, Eltern, Stundenpläne, Anwesenheit, Karten, Cockpit | |
| E4 Pädagogik | Leçons (Editor, Seiten, Blöcke, Fragen, Medien), Examens, Zuweisungen, Schülerseiten, Bewertungs-Engine, Korrektur, Feedback-Ton, Druckansichten, Direktion « Saisir pour » | |
| E5 Noten & Bulletins | Komponenten/Bewertungen, Notenraster, Conseil, Vorlagen, Veröffentlichung, Berichtigung, PDF, QR-Prüfung, Elternsicht, Jahreswechsel-Assistent | |
| E6 Finanzen | Gebührenposten/Raten/Ermäßigungen, Zahlungen + Quittungen + Storno, Journal + Belege, Bericht, Plattformrechnungen je Periode + Meldung/Bestätigung/Anfechtung + Erinnerungen, (Responsable-Anteile) | |
| E7 Abschluss | Simulation, Browser-Rundgang, Feinschliff, Handbuch/Guide-Texte | |
| E8 Umstellung | Alt-Projekte entfernen, Projektmappe, Publish-Profil, neue Azure-DB/Migration **nur mit OK**, Deploy | |

E2–E6 bauen parallel auf E1 auf, jeder Bereich in seinem Ordner (`Services/<Bereich>`, `Pages/<Bereich>`, `Data/Konfiguration/<Bereich>…`, `Tests/<Bereich>`). Gemeinsame Dateien (Program, NavMenu, app.css, DbContext, Shared) ändert nur, wer es im Auftrag ausdrücklich darf; Migrationen werden nach dem Zusammenführen zentral neu erzeugt (es gibt keine Bestandsdaten).

**Fertig heißt:** baut ohne Warnungen im neuen Code, alle Tests grün, Rechte je Rolle getestet, Seiten im Browser geprüft (Bilder), keine Platzhaltertexte, alle Texte französisch mit Akzenten.

---

## 11. Entscheidungen zu offenen Fragen der Fachkonzepte

Vom Nutzer entschieden (13.11.): alles pro Schuljahr; Plattformrechnung pro Periode (Sessions für Kurzschulen); Gebührenarten + Raten + Ermäßigungen; DB neu (nur Testdaten); alle Funktionen, neu durchdacht.
**Vergütung des Responsable (vom Nutzer bestätigt 13.11.):** Prozentsatz (Standard aus `PlattformEinstellung.ResponsableProzentStandard`, je Schule überschreibbar `Schule.ResponsableProzent`) der **bestätigt bezahlten** Plattformrechnungen seiner Schulen. Beim Bestätigen einer Rechnung entsteht ein `ResponsableAnteil` (Prozent und Betrag eingefroren, ganze FCFA, abgerundet). Der Responsable sieht « Mes parts » (offen/ausgezahlt, je Schule und Periode); der SuperAdmin markiert Auszahlungen (einzeln oder gesammelt, mit Datum und Referenz). Wird eine bestätigte Rechnung nachträglich storniert, entsteht ein negativer Ausgleichsanteil.

Von mir entschieden (änderbar):
1. Eine E-Mail darf zu mehreren Konten gehören (kein Unique-Index).
2. Eine Rolle je Konto; Direktor/Koordinator können unterrichten (Lehraufträge) ohne Lehrerrolle; ein Direktor zählt im Cockpit nicht als Lehrer.
3. Koordinator ändert keine Schulstammdaten; mit Recht darf er Gebühren/Zahlungen/Rechnung; Jahresabschluss und Veröffentlichung darf er wie der Direktor.
4. Schulstatus explizit; Direktorsperre sperrt nicht die Schule; Admin validiert/lehnt Schulen ab (Mail).
5. Archivierte Schüler können « réintégrés » werden; Archivieren auch für Personal.
6. Klassenwechsel im Jahr mit Datum: Noten bleiben an der alten Einschreibung; das Bulletin der Periode erstellt die Klasse, in der der Schüler am Periodenende ist; Noten aus der alten Klasse derselben Periode werden übernommen, wenn das Fach dort existiert (sonst Hinweis).
7. Lehrer erfassen Anwesenheit (Klassenappell je Tag) für Klassen mit Lehrauftrag.
8. « Partage avec la direction » entfällt: die Direktion sieht alle Inhalte ihrer Schule (transparent, so auch angezeigt). Inhalte können zwischen Lehrern übertragen werden (Direktion).
9. Matrikel automatisch lesbar `{Präfix}{JJ}{0001}`, frei überschreibbar, eindeutig je Schule; Karten-QR → Prüfseite ohne Personendaten.
10. Eltern sehen Stundenplan, Noten (nach Veröffentlichung der Periode bzw. live, Schuleinstellung, Standard: nur veröffentlichte Bulletins + Anwesenheit + Gebühren).
11. Klassen/Stufen/Fächer löschbar solange unbenutzt, sonst deaktivierbar.
12. Mails: Kontoanlage mit Mail → Willkommensmail je Rolle/Entstehung; ohne Mail → Zugangsliste-PDF; Zahlung → Quittung per Mail an Eltern-/Schülermail, falls vorhanden; Veröffentlichung Bulletin → Mail an Eltern/Schüler mit Adresse.
13. Periodenstrukturen: Trimestre (3), Semestre (2), Session (frei, z. B. 4×3 Monate), Libre; Jahresgewicht je Periode (Standard 1).
14. Komponenten: Vorlage je Periode, Bewertungen je Klassenfach frei ergänzbar; Status absent/dispensé wie §5.
15. Professeur principal sieht Bulletins seiner Klasse und bereitet den Conseil vor; veröffentlichen nur Direktion/Koordination.
16. Fortlaufende Bulletinnummer + QR-Token; Familienbenachrichtigung per Mail (falls Adresse).
17. Geschlossenes Schuljahr kann der Direktor mit Grund wiedereröffnen (Audit), solange kein Folgejahr läuft.
18. Übungen: Standard « illimité, meilleur essai compte », Lösungen sofort; einstellbar je Zuweisung. Examen: ein Versuch, Lösungen nach Frist, 30 s Karenz, Auto-Abgabe im Hintergrund.
19. Gesprochenes Feedback: feste MP3-Sätze (vorhanden, Vivienne) mit Schalter je Schüler; Audio-Aufgaben weiter per Browser-Sprachausgabe.
20. Medien-Grenzen: Bild 5 MB, Video 300 MB / 20 min, Beleg 10 MB (PDF/PNG/JPG).
21. Annonces: Zielrollen + optional Schule, Start/Ende, auch öffentlich (Rolle „Visiteurs“).
22. Aufbewahrung: Fehlerprotokoll 90 Tage, Mails 2 Jahre, Audit unbegrenzt.
23. « Rester connecté » bleibt; eine aktive Sitzung je Konto bleibt (Sicherheit bei geteilten PCs).
24. Plattformrechnung als PDF; Erinnerung per Mail bei Fälligkeit und +7 Tage; unbezahlte Rechnungen sperren nichts automatisch (Admin kann die Schule suspendieren).
25. Mobile Money: Kanal und Referenz erfassbar; automatische Anbindung später (Schnittstelle vorbereitet).

**Vom Nutzer entschieden 22.11.2026 (Fragen aus der Fehlersuche « Klicktest über Jahre », alle nach Empfehlung):**
26. **Zurückgezogenes Bulletin** bleibt zurückgezogen – auch eine Rectification der Klasse gibt es nicht neu aus. Neu nur über
    « Réémettre » (Direktion, mit Motif, nicht während einer offenen Rectification): neues Bulletin mit aktuellem Stand, neuer
    Nummer und QR-Code, das zurückgezogene gilt als ersetzt, Mail an die Familie.
27. **Nie gestartetes, aber genutztes Schuljahr** wird trotzdem abgerechnet (beendete Perioden, Tageslauf und « Facturer
    maintenant ») – zum aktuellen Preis, den die erste Rechnung für das Jahr einfriert; der spätere Start behält ihn. Die Seite
    « Année scolaire » warnt die Direktion, sobald ein Jahr begonnen hat, aber nicht gestartet ist.
28. **Passage:** « Admis » ohne Klasse im neuen Jahr (Klasse ohne Niveau, Niveau ohne Klasse, « Quittent l'établissement »
    gewählt) – außer am Ende des Zyklus – zeigt die Vorschau als Warnung mit Anzahl; « Effectuer » erst nach Bestätigung im
    Dialog (auch serverseitig, `PassagePlan.AbgaengeBestaetigt`). Eine **deaktivierte** Klasse des Folgejahres mit dem geplanten
    Namen wird nicht benutzt (Warnung, Ausführung lehnt ab).
29. **Lehrkraft ohne Lehrauftrag** in der Klasse: frühere Arbeiten und Versuche bleiben lesbar; « Résultats » zeigen nur Schüler
    mit Versuchen (keine aktuelle Klassenliste); korrigieren nur noch die Direktion.
30. **Messagerie des Responsable:** Empfänger nur aktive Direktion/Koordination der eigenen Schulen; eine einheitliche Meldung
    verrät nicht, ob eine Adresse zur Schule gehört.
31. **Abgerechnete Periode:** Beginn/Ende fest, solange eine nicht annullierte Plattformrechnung besteht; Titel und Koeffizient
    bleiben änderbar.
32. **Texte à trous:** mehrere akzeptierte Antworten einer Lücke mit « | » trennen (Brüche wie « 6/7 » sind Antworten).
33. Später (nicht gebaut): schnellerer Verbindungsaufbau bei sehr langsamem Netz; Eltern sehen Fortschritt bei Übungen/Examen.

**Vom Nutzer entschieden 22.11.2026 (Fragen aus dem Normaltest über drei Schuljahre, Bericht « skoulou-normal »):**
34. **Menüeintrag der offenen Seite** (auch Reiter/Logo mit derselben Adresse) lädt die Seite neu: das Hauptlayout erzeugt sie
    über einen neuen Schlüssel samt Unterkomponenten neu. Navigation aus dem Code (Filter in der Adresse) und Sprungmarken nicht.
35. **Jahresmittel bei Neuzugängen** nur aus den Perioden mit Einschreibung, normaler Jahresrang (§5 Regel 3).

---

## 12. Zusammenarbeit der Bereiche (E2–E6 parallel)

**Gemeinsam (aus E1, nur mit Absprache ändern):** `Program.cs`, `Data/SkoulouDbContext.cs`, `Components/Layout/*` (Navigation steht vollständig), `Components/Shared/*`, `wwwroot/app.css`, `Services/Infrastruktur/*` (u. a. `Zugriff`, `SchulAbfragen`, `NummernkreisService`, `AuditService`, Mail, Medien). Fehlt dort etwas Kleines, darf ein Bereich es **ergänzen** (neue Datei oder neue Methode), nie Bestehendes umbauen. Gemeinsame Abfragen (laufendes Schuljahr, aktuelle Klasse, Klassen eines Lehrers, Kinder der Eltern, Einschreibungen im Zeitraum) **immer** über `SchulAbfragen` – keine eigenen Varianten.

**Eigene Dateien je Bereich:** `Services/<Bereich>/*` (inkl. `<Bereich>Registrierung.cs`), `Data/Konfiguration/<Bereich>Konfiguration.cs`, Entitäten des Bereichs in `Skoulou.Core/Entitaeten/<Bereich>/`, Rechner in `Skoulou.Core/<Bereich>/`, Stile in `wwwroot/css/<bereich>.css`, Tests in `Skoulou.Tests/<Bereich>/`, und diese Seiten:

| Bereich | Seiten (Components/Pages/…) |
|---|---|
| E2 Plattform | `Admin/*` außer `Facturation`, `Tarifs`; `Responsable/ResponsableStart`, `Responsable/Messagerie`; `Auth/Inscription`; `Direction/EnAttente`; `Oeffentlich/Kontakt` (Responsable-Karte); `Aide/Guide`; Annonces-Banner im Layout (einzige erlaubte Layout-Änderung) |
| E3 Organisation | `Direction/*` außer `Contenus`, `Passage`; `Unterricht/Presences`, `Unterricht/EmploiDuTemps`; `Eleve/EmploiDuTemps`; `Parents/ParentsStart`, `Parents/Presences`, `Parents/EmploiDuTemps`; Schülerkarten, Zugangsliste, Import |
| E4 Pädagogik | `Unterricht/EnseignantStart`, `Lecons`, `Examens`, `Corrections`; `Direction/Contenus`; `Eleve/EleveStart`, `Lecons`, `Examens`, `Resultats`; Druckansichten `/druck/*` |
| E5 Noten | `Unterricht/Notes`, `Unterricht/Bulletins`; `Direction/Passage` (Jahreswechsel); `Eleve/Bulletins`; `Parents/Notes`, `Parents/Bulletins`; `Oeffentlich/Verification` |
| E6 Finanzen | `Finances/*`; `Admin/Facturation`, `Admin/Tarifs`; `Responsable/Facturation`, `Responsable/Parts`; `Eleve/Frais`; `Parents/Frais` |

Elternseiten nutzen `<KindAuswahl>` (Shared) + `ElternKinderService`; jeder Dienst prüft zusätzlich `SchulAbfragen.IstKindAsync`.

**Datenbank:** Entitäten/Konfiguration des eigenen Bereichs dürfen angepasst werden; **keine Migration einchecken**. Für Browser-Tests eine eigene lokale DB `SkoulouDB_<Bereich>` mit lokaler Migration verwenden; nach dem Zusammenführen erzeugt die Leitung eine gemeinsame Migration neu (keine Bestandsdaten).
**Demo-Daten:** `DemoSeed` erweitert jeder Bereich nur in einer eigenen Datei `Data/Seed/DemoSeed<Bereich>.cs`, aufgerufen am Ende von `DemoSeed` (ein Aufruf je Bereich ist bereits vorgesehen bzw. wird ergänzt).
