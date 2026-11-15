# TabloSuite – Arbeitsstand 15.11.2026, ~01:10

Diese Datei ist der Einstieg für ein neues Chat-Fenster. Erst lesen, dann weitermachen.
Die vollständige Planung für SKOULOU steht in `Skoulou/docs/Bauplan.md` (verbindlich) und `Skoulou/docs/fachkonzept/01…05`.

---

## 0. HIER WEITERMACHEN (neuer Chat): SKOULOU – Simulationen S3 + S4 + Ladekreis

**eNOTE ist fertig** (Simulationen S1 + S2 + Nutzerwünsche W1–W3, alles gepusht bis 282c0b8, 435 Tests grün, Azure-DB =
lokal = `20261114233118_PreisAnpassungEntwarnung`). eNOTE kann der Nutzer jederzeit deployen.
**Der Nutzer deployt SKOULOU erst, wenn S3 + S4 sauber sind** (Nutzer 14.11.: « Danach mache ich Deploy, wenn alles ok ist »).

Reihenfolge, immer nur **1 Agent**, Agent committet, Lead prüft + pusht, zwischendurch kurz auf Deutsch berichten:
1. **S3 SKOULOU Daten-Simulation** in voller Größe mit den neuen Regeln – Auftrag `docs/Simulationen-Auftrag.md` (Gemeinsame
   Regeln + Abschnitt S3; Tabelle dort: S1/S2 erledigt). Vorbild/Erfahrung aus eNOTE: `docs/Simulation-eNOTE.md`.
2. **S4 SKOULOU Klick-Simulation** (App im Hintergrund auf `SkoulouDB_SIM`, Port 5181, mehrere gleichzeitige Nutzer aller
   Rollen, echte Abläufe über eine Periode + « Monkey » je Rolle). In S2 bei eNOTE besonders ergiebig – in SKOULOU gezielt
   prüfen: **Zeichenverlust bei langsamer Verbindung** (Felder mit `@bind:event="oninput"`/Wert-Überschreiben, mit 250/400 ms
   Latenz tippen), **nicht registrierte Dienste** (jede Seite/jeder Dialog einmal öffnen → 500?), **gleichzeitige Buchungen**
   (zwei Geräte derselben Schule: Zahlungen/Quittungsnummern, Kasse, Noten), « Mot de passe oublié », Doppelklick auf Speichern.
3. **Ladekreis am Handy** (Nutzer 15.11.): Knopf « S'inscrire »/Registrieren zeigt am Handy keinen Ladekreis (vermutlich
   auch Connexion und andere klassische Formulare). In eNOTE behoben mit 7b68100 « Ladeanzeige überall »: globale Ladeanzeige
   für klassische Formulare (`data-ladeanzeige`) statt Inline-Skript (lief nach Blazor-Navigation am Handy nie),
   bfcache/« Zurück » gibt den Knopf frei, Spinner-Fallback ohne `color-mix` für ältere Mobil-Browser, JS versioniert
   einbinden; Muster `eNOTE/eNOTE.WebApp/Components/Pages/Register.razor` (`isSubmitting` + `btn-spinner`). In SKOULOU alle
   Formular-/Aktionsknöpfe prüfen (Registrierung Schule, Schüler-Selbstregistrierung, Connexion, Mot de passe oublié), am
   Handy 390×844 auch mit langsamer Verbindung testen. Kann in S4 mit erledigt werden (dem Agenten mitgeben).
4. **Abschluss SKOULOU:** neue Migrationen (falls S3/S4 welche bringen) auf `SkoulouDB` + Azure `Skoulou-Database`
   (OK des Nutzers für den Abschluss liegt vor; nur additive Änderungen, vorher Up() prüfen) per
   `dotnet ef database update <Migration> --project Skoulou/Skoulou.WebApp --configuration Release --connection
   "Server=tcp:tablosuite-sqlserver.database.windows.net,1433;Database=Skoulou-Database;Authentication=Active Directory Default;Encrypt=True;…"`,
   lesend prüfen (`%TEMP%\enote-scripts\azure-sql.ps1 -Datenbank Skoulou-Database -Sql "…"`), Tests, push → Nutzer deployt
   SKOULOU (und eNOTE, falls noch nicht). Nach dem Deploy: Login DodjiEtse, Menü « Administrateurs », Startseiten-Video;
   bei 0 Konten `az webapp restart -g TabloSuiteRGruppe -n skoulou`.

**Stand SKOULOU jetzt:** Azure Skoulou-Database = lokal SkoulouDB = `20261114182350_SessionGebuehrUndHauptAdmin`; letzter
Code-Stand E7b komplett (bis 495ac44), 542 Tests grün (+ SQL 561). Noch nicht deployt seit E7a (enthält Werbevideo v3,
E7b Zusatz + Rundgang).

## 1. eNOTE – Simulationen und Wünsche (14./15.11., fertig)

- **S1 Daten** (5706474, e573224, cc2fae1): 39 Läden, 7 Länder, 187 Mitarbeitende, 7.136 Kunden, ~20 Monate, 110.982
  Verkäufe; 4 Fehler behoben (Bestand beim Produkt-Speichern zurückgesetzt, Gratis-Ticket 0 F, Mobile Money ohne Land, Duzen
  in Mail). DB `ENOTEDB_SIM` behalten.
- **S2 Klicks** (8f253df … 0344763): 13 gleichzeitige Nutzer, ~7.500 Klicks; 9 Fehler behoben (Zeichenverlust bei langsamer
  Verbindung in 8 Feldern, « Mot de passe oublié » 500 – Dienst nicht registriert, zwei Kassen verloren Lagerabzüge → Sperre,
  zweites Gerät nach Abschluss, Festbetrag-Remise je Zeile, Remise 0 %, Handy-Überlauf /remises …).
- **W1–W3** (e3df98a, 1062f91, 7aa6d0a, 424f7a5, 282c0b8): Entwarnung bei aufgehobener Tariferhöhung (Dialog + Mail an die
  Empfänger der Ankündigung), « Pays » bei Registrierung + änderbar (Besitzer/Admin), Kassensperre (`sp_getapplock`) für alle
  Kassenbuchungen, « En caisse »/Abschluss aus der DB, Rembourser-Grenze. Migration `20261114233118_PreisAnpassungEntwarnung`
  lokal + Azure eingespielt (15.11. ~01:05).
- Bericht `docs/Simulation-eNOTE.md`, Bilder `Pictures\Simulation-eNOTE\`.

## 2. Frühere Stände (14.11.)

**Erledigt am 14.11. nachmittags/abends (Reihenfolge der Sitzung):**
1. ✅ **« Pourquoi SKOULOU ? » v3** (14.11. nachmittags): 1:39, −16,7 LUFS, neue Oberfläche, ohne Sprecherin; in der App
   als `skoulou-pourquoi.mp4` + `skoulou-presentation.mp4` (Startseite) mit neuem Poster, Katalog 1:39. Projekt
   `TabloSuite Videos\Skoulou Videos\projekte\pourquoi-v3\` (bau.py, renders/), Master-Kopie in `Skoulou Videos\Backup\Werbevideo`,
   Bilder `Pictures\SKOULOU-Pourquoi-v3`. Lokale SkoulouDB hat 1 Test-Zahlung (5.000 F, Kossi Adjo). Muss noch deployt werden.
2. ✅ **eNOTE-Videos Paket C** (14.11. abends): responsable-etablissements 3:41, responsable-paiements 2:46,
   admin-etablissements 3:31, admin-tarifs 3:49, admin-communication 3:24 in der App (lokale DB zurückgesetzt, geprüft).
   ✅ **Paket D** (14.11. abends): Visite guidée 5:32, « Pourquoi eNOTE ? » 2:03 ohne Sprecherin (Startseite angepasst).
   ✅ **Paket E** (14.11.): 333 unbenutzte Videodateien (~3,1 GB) im Papierkorb; « ENote Videos » enthält nur noch die
   17 App-Videos (Backup) + lernen-v3 (Renders, Quellaufnahmen). **Alle eNOTE-Videos sind v3, ohne Sprecherin.**
3. **SKOULOU E7b** nach `Skoulou/docs/auftraege/E7-Abschluss.md`, in zwei Läufen:
   ✅ **Teil 1 Zusatzpunkte** (14.11. abends, f67552d 7534ca9 d937116 8e809da): Session-Klassen enden automatisch
   (`SessionRegel`), Gebühr je Session (Feld « Période »), Kontoanlage wiederholt 3×, `/direction/journal`, Bedingungen im
   Registrierungsformular (Dialog nach Login entfernt), `/admin/administrateurs` (nur Hauptadmin; « Supprimer » = archivieren;
   nur Hauptadmin setzt Admin-Passwörter zurück). **Neue Migration `20261114182350_SessionGebuehrUndHauptAdmin`**
   (GebuehrenPosten.PeriodeId, Konten.IstHauptAdmin) – lokal SkoulouDB + SkoulouDB_SIM und Azure Skoulou-Database
   eingespielt. 558 Tests grün inkl. SQL. Bilder `Pictures\SKOULOU-Neubau-E7b-Zusatz`.
   ✅ **Teil 2 Rundgang** (98a13eb 4a5c59b 495ac44): 11 Rollen, 440 Aufrufe + 51 Dialoge, Desktop + Handy, am Ende sauber;
   Guide an echte Seiten angepasst (+ Tests Guide ↔ Navigation), « FCFA » einheitlich, Handy-Kopfzeile, Médias-Leerzustand;
   alle Seiten < 1 s; keine Schemaänderung. Bericht `Skoulou/docs/Simulation-Bericht.md` § Rundgang, Bilder
   `Pictures\SKOULOU-Neubau-E7-Rundgang`.
4. ✅ **Abschluss**: Migration auf Azure Skoulou-Database eingespielt + geprüft, eNOTE Azure schon aktuell, Tests grün, gepusht.

**Heute erledigt:** Video-Pilot v3.1/v3.2 freigegeben (« Stil ist gut, sehr gut ausführlich »; Klick-Kreis halb
durchsichtig). eNOTE-Videos Paket A (Premiers pas, Encaisser, Attente, Clients, Clôture) + B (Catalogue, Équipe,
Activité, Vos clients, Compte) in der App, gepusht bis d4ba620. E7a Simulation (22 Schulen, 3 Jahre, ~9.900 Schüler,
0 Verstöße, Listen < 150 ms, 1 Fehler behoben 256da0a, Bericht `Skoulou/docs/Simulation-Bericht.md`). 43 alte Videos
(530 MB) im Papierkorb. Entscheidungen des Nutzers: Treueprogramm eNOTE zurückgestellt; Session-Klassen automatisch
beenden; Gebühr je Session ja, keine Anteilsrechnung bei Austritt; stornierte bezahlte SKOULOU-Rechnung ohne Verrechnung.

## 3. SKOULOU-Neubau (Hauptprojekt)

**Seit 14.11. ~01:45 ist der Neubau LIVE** auf der bestehenden Web-App « skoulou » (skoulou.tablosuite.com, Startbefehl `dotnet Skoulou.WebApp.dll`), Azure-DB `Skoulou-Database` mit neuem Schema « Fundament » neu aufgebaut (leer, Admin aus Seed-Einstellungen). **Keine neuen Azure-Ressourcen anlegen** (Nutzer). Alte Projekte `SKOULOUAPP.*` sind aus Projektmappe und Repo entfernt (nur noch Git-Historie, z. B. f6c642e). Deploy durch den Nutzer: Profil « skoulou - Zip Deploy » liegt jetzt in `Skoulou/Skoulou.WebApp/Properties/PublishProfiles/` (lokal, nicht in Git). Nach jedem Neu-Erzeugen der Migration: Azure-DB vor dem Deploy neu aufbauen (löschen + `az sql db create` Basic 5 DTU 2 GB + `dotnet ef database update --connection` + `CREATE USER [skoulou] FROM EXTERNAL PROVIDER` mit datareader/datawriter). Eigenes Zip-Deploy: Zip mit `/`-Pfaden (PowerShell-Compress-Archive bricht auf Linux).

**Auftrag des Nutzers (13.11.):** SKOULOU komplett neu bauen „in deiner Art“ wie eNOTE (Code + UI), am Ende großer Test mit vielen Daten/Schulen/Schuljahren (Direktion eröffnet Schule … Abrechnungen mehrerer Admins … neues Schuljahr). Am Ende die alten `SKOULOUAPP.*`-Projekte im Repo durch die neuen **ersetzen**. « Ich vertraue dir ».

**Entscheidungen des Nutzers:**
- Azure-SKOULOU-DB hat nur Testdaten → Datenbank wird neu entworfen.
- Alle heutigen Funktionen, Abläufe neu durchdacht; nur Totes fällt weg.
- Parallel bauen in `Skoulou/`, alte App bleibt live bis zur Umstellung (gleiche Adresse skoulou.tablosuite.com).
- **Alles pro Schuljahr** (Noten, Bulletins, Schulgebühren, SKOULOU-Gebühr).
- **Plattformrechnung pro Periode** (Trimester/Semester; für Sprachschulen « Sessions » z. B. 3 Monate, Klassen ggf. nur eine Session). Jahrespreis je Schüler auf die Perioden verteilt (ganze FCFA, 4.166 + 4.167 + 4.167), jeder Schüler einmal je Periode, auch bei Klassenwechsel.
- **Scolarité:** Gebührenarten je Klasse + Raten mit Fälligkeit + Ermäßigungen, fortlaufende Quittungen, Storno mit Grund.
- **Responsable-Vergütung: JA** – % der bestätigt bezahlten SKOULOU-Rechnungen seiner Schulen, Seite « Mes parts », Admin markiert Auszahlungen.
- Meine übrigen 25 Entscheidungen: Bauplan §11 (Nutzer kann jederzeit widersprechen).

**Etappen (Bauplan §10):**

| Etappe | Stand |
|---|---|
| Fachkonzept 01–05 + Bauplan | ✅ committet (d1a58cd) |
| E1 Fundament (Projekte, 57 Entitäten, Login, Rechte, Mail, Speicher, Design-System, Navigation aller Rollen, Startseite, Demo-Seed) | ✅ committet + gepusht (4feab54), 143 Tests grün |
| Gemeinsame Vorbereitung (SchulAbfragen, IDs beim Anlegen, KindAuswahl, Stil-Dateien + Demo-Seed je Bereich, Bauplan §12 Zuständigkeiten, Demo:KontenDatei) | ✅ ff5ee74 (gepusht), f25b464 |
| E2 Plattform (Registrierung/Validierung, Admin, Responsables, Annonces, Messagerie, Journal, Guide) | ✅ zusammengeführt (7702551 → 7167a5b, 84dc8d7) |
| E3 Organisation | ✅ zusammengeführt (736399e → b15c8ec) |
| E4 Pädagogik | ✅ zusammengeführt (7268bd1 → 08c1556); Migration « Fundament » neu (8f00a3f), 373 Tests grün inkl. SQL, Rundgang 25 Seiten ohne Fehler (Bilder `Pictures\SKOULOU-Neubau-E3E4-Rundgang`) |
| E5 Noten & Bulletins (+ Jahreswechsel-Assistent `/direction/annee/passage`) | ✅ zusammengeführt (6c1226c → 3000005) |
| E6 Finanzen (Gebühren, Zahlungen, Journal, Plattformrechnung je Periode, Responsable-Anteile, Tarife) | ✅ zusammengeführt (2f23e29 → 695d76b), Migration neu f0301fe |
| E7 Simulation (≥ 20 Schulen, 3 Jahre, Sprachschule mit Sessions) + Browser-Rundgang | E7a ✅ (256da0a, b850faf, 6a46a25); E7b ⏳ Auftrag `docs/auftraege/E7-Abschluss.md` (inkl. Session-Regeln, Gebühr je Session, AGB bei Registrierung) |
| E8 Umstellung (Alt-Projekte entfernen, Publish-Profil, neue Azure-DB **nur mit OK**) | Alt-Projekte entfernt (ea623e6), Neubau live; offen: Migrationen am Ende auf Azure (OK liegt vor) + Deploy durch Nutzer |

**Zusammenführen der Bereiche (Leitung):** Agent committet im Worktree-Branch → in den Hauptbranch mergen → Bereichs-Migrationen gibt es nicht im Repo; nach den Merges den Ordner `Skoulou/Skoulou.WebApp/Data/Migrations` neu erzeugen (eine Migration, keine Bestandsdaten), lokale `SkoulouDB` neu aufbauen, alle Tests + SQL-Tests (`SKOULOU_SQL_TESTS=1`), Browser-Check, committen + pushen.

**Lokal starten (neue App):** DB `SkoulouDB` (localhost, Windows-Auth). `dotnet run --project Skoulou/Skoulou.WebApp --launch-profile http -- --seed-demo` (legt Demo-Schule « Collège Démo Les Palmiers » an, Zugänge nur in `%TEMP%\skoulou-neu\demo-konten.json`), dann `dotnet run … --launch-profile http` → http://localhost:5115. Gleiche UserSecretsId wie die Alt-App (Seed/Postmark lokal vorhanden). Bilder E1: `Pictures\SKOULOU-Neubau-E1`.

---

## 4. Videos

**Nutzer-Feedback 13.11.:** Die KI-Sprecherin wirkt unecht → **alle Videos ohne sie**; **ausführlich und informativ**; **jeden Klick zeigen** (welcher Knopf) und **welches Fenster sich öffnet**.
- Neue Pipeline `lernen-v3` (Kopie von lernen-v2, alte bleibt): Ring + Beschriftung « Clic sur « … » » vor jedem Antippen, Banner « Fenêtre ouverte : « … » », Titelkarte statt Sprecherin, keine Zooms, Render in delivery-Qualität.
- **Ablauf:** Pilot « encaisser » fertigstellen → Standbilder/Video dem Nutzer zeigen → bei OK alle eNOTE-Videos neu (15 in `lernen-v2/videos.json` + Visite guidée + Werbevideo ohne Sprecherin) → per einbau.py in die App (`eNOTE.WebApp/Medien/videos`, Poster `wwwroot/videos`) → Backup-Kopie in `ENote Videos/Backup` → committen, Nutzer deployt.
- **SKOULOU-Videos** erst nach dem Neubau auf der neuen Oberfläche aufnehmen (die alte App mit Sprecherin-Videos bleibt bis zur Umstellung live).
- Stimme bleibt Azure Vivienne; Schlüssel in `eNOTE/.env` – **nie ausgeben/sourcen**.

---

## 5. eNOTE und Live-Betrieb

- Alles von heute ist committet und gepusht (u. a. Listen seitenweise/durchsuchbar in eNOTE 4bf5156/2fa3f5c/0277bf3 und SKOULOU-alt c651544, Start-Admin-Seed eNOTE, öffentliches Kontaktformular, Dialoge schließen nicht beim Klick daneben, PIN-Einrichtung, Responsable-Rechte, Mail-Routing über contact@tablosuite.com mit [eNOTE]/[SKOULOU] im Betreff).
- **NEU 13.11. abends (f794f4f): eNOTE « Enregistrer sans encaisser »** – Leistungen anwesender Kunden (Praxis mit Übernachtung) fest erfassen, am Ende alles zusammen kassieren; Details in eNOTE/Projektuebersicht.md, Bilder Pictures/eNOTE-Prestations-a-payer-2026-11-13. Migration 20261113221524_AufenthaltsPositionen am 14.11. auch auf Azure eingespielt (Azure = lokal) → eNOTE kann deployt werden.
- **SKOULOU (alt) und eNOTE deployen** (Nutzer, VS). Neu 14.11.: zusätzliche Azure-DB **Skoulou-Neu-Database** (Basic, tablosuite-sqlserver) mit Schema « Fundament » + DB-User [skoulou] (Managed Identity); alte App bleibt auf Skoulou-Database. Nach jedem Neu-Erzeugen der Migration die neue DB leeren und neu einspielen (keine Daten).
- Azure: Hauptkonto web.de, Ressourcengruppe TabloSuiteRGruppe (enote, skoulou, tablosuite-home, SQL tablosuite-sqlserver). Admin-Konto überall DodjiEtse (Passwort ändert der Nutzer in der App; `Seed__SuperAdminPasswort` bleibt als Notfall-Startpasswort).

---

## 6. Feste Regeln (Kurzfassung)

- Antworten auf Deutsch; App-Texte Französisch mit « vous »; nie « francophone » sagen; keine Preise von HeyGen o. ä. ungefragt nennen.
- Fertige, grüne Schritte direkt committen + pushen (nicht fragen). Push braucht manchmal die GitHub-Anmeldung des Nutzers.
- **Azure-DB-Migrationen/Umschalten nur mit ausdrücklichem OK im Moment.** Classifier-Sperren nicht umgehen.
- Nie Passwörter/Schlüssel ausgeben; `eNOTE/.env` nie ausgeben oder sourcen; E-Mails in Aufnahmen maskieren, erfundene Namen.
- Vor dem Beenden eines eNOTE/SKOULOU-Prozesses Elternprozess prüfen (Visual Studio = Nutzer, nicht anfassen).
- Aufräumen immer über den Papierkorb; Videos-Arbeitsordner nicht umräumen (Backup nur als Kopie).
- Bei Seitenänderungen Bilder zeigen (Ordner unter `Pictures\…`).

---

## 7. Nächste Schritte

Siehe §0 (Reihenfolge 1–4). Zwischendurch dem Nutzer auf Deutsch berichten, was gerade läuft.
