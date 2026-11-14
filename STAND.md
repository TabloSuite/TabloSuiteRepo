# TabloSuite – Arbeitsstand 14.11.2026, morgens

Diese Datei ist der Einstieg für ein neues Chat-Fenster. Erst lesen, dann weitermachen.
Die vollständige Planung für SKOULOU steht in `Skoulou/docs/Bauplan.md` (verbindlich) und `Skoulou/docs/fachkonzept/01…05`.

---

## 1. Stand 14.11. morgens

- **SKOULOU-Neubau E1–E6 fertig und zusammengeführt** (E2 Plattform, E5 Noten & Bulletins, E6 Finanzen dazu), Migration « Fundament » neu (20261114004546), 540 Tests grün inkl. SQL, eNOTE 409 grün, Rundgang 21 neue Seiten ohne Fehler. Lokale `SkoulouDB` und Azure `Skoulou-Database` mit diesem Schema neu aufgebaut (leer, Admin aus Seed-Einstellungen beim Start).
- Alles gepusht (Nutzer, 14.11.). Veröffentlichen beider Apps macht der Nutzer (eNOTE wie gewohnt; SKOULOU aus `Skoulou/Skoulou.WebApp`, Profil « skoulou - Zip Deploy »).
- **Läuft:** Video-Pilot v3.1 (Agent fortgesetzt; Ziel ≈ 5 min, Zeiger synchron zur Stimme, Klick-Schilder ohne Überdeckung – `lernen-v3`, Bilder `Pictures/eNOTE-Videos-v3-Pilot/v3.1`). Falls unterbrochen: neuen Agenten mit `lernen-v3/AUFTRAG-Pilot.md` + diesen drei Zielen starten.
- **14.11. mittags: Video-Pilot v3.1 vom Nutzer freigegeben** (« Stil ist gut, sehr gut ausführlich »). Danach Klick-Kreis durchsichtiger (`RING_DECKKRAFT` 0,5 in bau.py) → `enote-encaisser-v3.2.mp4`. **Nächste Video-Arbeit:** alle eNOTE-Videos nach `lernen-v3/AUFTRAG-Alle.md` in Paketen A–E (je Lauf 1 Paket, erst nach E7a/E7b bzw. wenn kein anderer Agent läuft), einbauen, dann unbenutzte Videos löschen. Erste Aufräumrunde 14.11.: 43 alte Videos (530 MB: ENote Temp, Lernvideos, erklaer, lernen v1, werbung-v2, Skoulou lernen v1) im Papierkorb.
- **E7a fertig (14.11.)**: 256da0a (Jahreswechsel übernimmt Session der Klassen), b850faf Simulation (`SKOULOU_SIM=1`, 22 Schulen, 3 Jahre, ~9.900 Schüler, 0 Invarianten-Verstöße, alle Listen < 150 ms), 6a46a25 `Skoulou/docs/Simulation-Bericht.md` (offene Punkte dort). Keine Schemaänderung. `SkoulouDB_SIM` lokal für E7b behalten.
- **Läuft: Video-Paket A** (Employé, `lernen-v3/AUFTRAG-Alle.md`). Danach E7b, dann Pakete B–E.
- **Danach:** E7 nach `Skoulou/docs/auftraege/E7-Abschluss.md` – erst E7a Simulation, dann E7b Rundgang + Feinschliff (je 1 Agent, im Hauptbaum; Schemaänderungen nur als neue Migration, « Fundament » nicht neu erzeugen, da live). Immer nur 1 Agent gleichzeitig (Limit des Nutzers).

## 2. SKOULOU-Neubau (Hauptprojekt)

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
| E2 Plattform (Registrierung/Validierung, Admin, Responsables, Annonces, Messagerie, Journal, Guide) | ⏳ noch nicht gestartet – Auftrag analog E3 schreiben |
| E3 Organisation | ✅ zusammengeführt (736399e → b15c8ec) |
| E4 Pädagogik | ✅ zusammengeführt (7268bd1 → 08c1556); Migration « Fundament » neu (8f00a3f), 373 Tests grün inkl. SQL, Rundgang 25 Seiten ohne Fehler (Bilder `Pictures\SKOULOU-Neubau-E3E4-Rundgang`) |
| E5 Noten & Bulletins (+ Jahreswechsel-Assistent `/direction/annee/passage`) | ⏳ noch nicht gestartet |
| E6 Finanzen (Gebühren, Zahlungen, Journal, Plattformrechnung je Periode, Responsable-Anteile, Tarife) | ⏳ noch nicht gestartet |
| E7 Simulation (≥ 20 Schulen, 3 Jahre, Sprachschule mit Sessions) + Browser-Rundgang | ⏳ |
| E8 Umstellung (Alt-Projekte entfernen, Publish-Profil, neue Azure-DB **nur mit OK**) | ⏳ |

**Zusammenführen der Bereiche (Leitung):** Agent committet im Worktree-Branch → in den Hauptbranch mergen → Bereichs-Migrationen gibt es nicht im Repo; nach den Merges den Ordner `Skoulou/Skoulou.WebApp/Data/Migrations` neu erzeugen (eine Migration, keine Bestandsdaten), lokale `SkoulouDB` neu aufbauen, alle Tests + SQL-Tests (`SKOULOU_SQL_TESTS=1`), Browser-Check, committen + pushen.

**Lokal starten (neue App):** DB `SkoulouDB` (localhost, Windows-Auth). `dotnet run --project Skoulou/Skoulou.WebApp --launch-profile http -- --seed-demo` (legt Demo-Schule « Collège Démo Les Palmiers » an, Zugänge nur in `%TEMP%\skoulou-neu\demo-konten.json`), dann `dotnet run … --launch-profile http` → http://localhost:5115. Gleiche UserSecretsId wie die Alt-App (Seed/Postmark lokal vorhanden). Bilder E1: `Pictures\SKOULOU-Neubau-E1`.

---

## 3. Videos

**Nutzer-Feedback 13.11.:** Die KI-Sprecherin wirkt unecht → **alle Videos ohne sie**; **ausführlich und informativ**; **jeden Klick zeigen** (welcher Knopf) und **welches Fenster sich öffnet**.
- Neue Pipeline `lernen-v3` (Kopie von lernen-v2, alte bleibt): Ring + Beschriftung « Clic sur « … » » vor jedem Antippen, Banner « Fenêtre ouverte : « … » », Titelkarte statt Sprecherin, keine Zooms, Render in delivery-Qualität.
- **Ablauf:** Pilot « encaisser » fertigstellen → Standbilder/Video dem Nutzer zeigen → bei OK alle eNOTE-Videos neu (15 in `lernen-v2/videos.json` + Visite guidée + Werbevideo ohne Sprecherin) → per einbau.py in die App (`eNOTE.WebApp/Medien/videos`, Poster `wwwroot/videos`) → Backup-Kopie in `ENote Videos/Backup` → committen, Nutzer deployt.
- **SKOULOU-Videos** erst nach dem Neubau auf der neuen Oberfläche aufnehmen (die alte App mit Sprecherin-Videos bleibt bis zur Umstellung live).
- Stimme bleibt Azure Vivienne; Schlüssel in `eNOTE/.env` – **nie ausgeben/sourcen**.

---

## 4. eNOTE und Live-Betrieb

- Alles von heute ist committet und gepusht (u. a. Listen seitenweise/durchsuchbar in eNOTE 4bf5156/2fa3f5c/0277bf3 und SKOULOU-alt c651544, Start-Admin-Seed eNOTE, öffentliches Kontaktformular, Dialoge schließen nicht beim Klick daneben, PIN-Einrichtung, Responsable-Rechte, Mail-Routing über contact@tablosuite.com mit [eNOTE]/[SKOULOU] im Betreff).
- **NEU 13.11. abends (f794f4f): eNOTE « Enregistrer sans encaisser »** – Leistungen anwesender Kunden (Praxis mit Übernachtung) fest erfassen, am Ende alles zusammen kassieren; Details in eNOTE/Projektuebersicht.md, Bilder Pictures/eNOTE-Prestations-a-payer-2026-11-13. Migration 20261113221524_AufenthaltsPositionen am 14.11. auch auf Azure eingespielt (Azure = lokal) → eNOTE kann deployt werden.
- **SKOULOU (alt) und eNOTE deployen** (Nutzer, VS). Neu 14.11.: zusätzliche Azure-DB **Skoulou-Neu-Database** (Basic, tablosuite-sqlserver) mit Schema « Fundament » + DB-User [skoulou] (Managed Identity); alte App bleibt auf Skoulou-Database. Nach jedem Neu-Erzeugen der Migration die neue DB leeren und neu einspielen (keine Daten).
- Azure: Hauptkonto web.de, Ressourcengruppe TabloSuiteRGruppe (enote, skoulou, tablosuite-home, SQL tablosuite-sqlserver). Admin-Konto überall DodjiEtse (Passwort ändert der Nutzer in der App; `Seed__SuperAdminPasswort` bleibt als Notfall-Startpasswort).

---

## 5. Feste Regeln (Kurzfassung)

- Antworten auf Deutsch; App-Texte Französisch mit « vous »; nie « francophone » sagen; keine Preise von HeyGen o. ä. ungefragt nennen.
- Fertige, grüne Schritte direkt committen + pushen (nicht fragen). Push braucht manchmal die GitHub-Anmeldung des Nutzers.
- **Azure-DB-Migrationen/Umschalten nur mit ausdrücklichem OK im Moment.** Classifier-Sperren nicht umgehen.
- Nie Passwörter/Schlüssel ausgeben; `eNOTE/.env` nie ausgeben oder sourcen; E-Mails in Aufnahmen maskieren, erfundene Namen.
- Vor dem Beenden eines eNOTE/SKOULOU-Prozesses Elternprozess prüfen (Visual Studio = Nutzer, nicht anfassen).
- Aufräumen immer über den Papierkorb; Videos-Arbeitsordner nicht umräumen (Backup nur als Kopie).
- Bei Seitenänderungen Bilder zeigen (Ordner unter `Pictures\…`).

---

## 6. Nächste Schritte (Reihenfolge)

1. Video-Pilot v3.1 abwarten (§1) → Nutzer zeigen → bei OK alle eNOTE-Videos im v3-Stil, Einbau, Deploy durch den Nutzer.
2. SKOULOU: E5 + E6 laufen (§1) → nach Fertigmeldung je prüfen, mergen, Migration « Fundament » neu erzeugen, Tests + SQL-Tests, Rundgang, push; dann E2 starten (Auftrag liegt bereit).
3. Lokale `SkoulouDB` am 14.11. neu aufgebaut (aktuelles Schema, Demo-Seed); alte Test-DBs gelöscht.
4. Agenten Video v3.1, E5, E6 am 14.11. ~00:50 vom Nutzungslimit gestoppt (Arbeit gespeichert) → nach 3:50 fortsetzen; künftig höchstens 1–2 Agenten gleichzeitig (Nutzer: Limit zu schnell erreicht).
5. E7 Simulation + Browser-Rundgang; E8 Umstellung (Azure nur mit OK); danach SKOULOU-Videos auf der neuen Oberfläche.
