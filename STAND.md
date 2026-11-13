# TabloSuite – Arbeitsstand 13.11.2026, 22:50

Diese Datei ist der Einstieg für ein neues Chat-Fenster. Erst lesen, dann weitermachen.
Die vollständige Planung für SKOULOU steht in `Skoulou/docs/Bauplan.md` (verbindlich) und `Skoulou/docs/fachkonzept/01…05`.

---

## 1. Was gerade unterbrochen ist (sofort fortsetzen)

Um ~20:30 haben **drei Hintergrund-Agenten das 5-Stunden-Nutzungslimit** erreicht (zurückgesetzt 22:50). Ihre Arbeit liegt auf der Festplatte, ist aber **nicht fertig und nicht committet**. In einem neuen Chat lassen sie sich nicht „fortsetzen“ – stattdessen je einen **neuen Agenten mit dem gespeicherten Auftrag** starten und ihm sagen, dass er im vorhandenen Ordner weitermacht.

| Arbeit | Ort | Stand | Auftrag zum Neustart |
|---|---|---|---|
| **eNOTE-Lernvideo v3 – Pilot « encaisser »** | `C:\ETSE\NETAPPS\TabloSuite Videos\ENote Videos\projekte\lernen-v3\` | Text, Stimme und alle 7 Aufnahmen fertig (inkl. Klick-Beschriftungen + Fenster-Erkennung). **Offen:** bau.py v3, Render, Standbilder | `lernen-v3\AUFTRAG-Pilot.md` |
| **SKOULOU E3 Organisation** | Worktree `.claude\worktrees\agent-a0e4dd2880c598ed0`, Branch `worktree-agent-a0e4dd2880c598ed0` | ~23 Dateien (v. a. Services/Organisation), keine Seiten | `Skoulou/docs/auftraege/E3-Organisation.md` |
| **SKOULOU E4 Pädagogik** | Worktree `.claude\worktrees\agent-a4f88e23ab997f232`, Branch `worktree-agent-a4f88e23ab997f232` | ~21 Dateien (Bewertungs-Engine, Services/Paedagogik), keine Seiten | `Skoulou/docs/auftraege/E4-Paedagogik.md` |

**Wichtig zum Limit:** höchstens 2–3 große Agenten gleichzeitig. Esc/„Stopp“ während Agenten laufen bricht sie ab (dann nur Neustart möglich); normale Nachrichten stören nicht.

---

## 2. SKOULOU-Neubau (Hauptprojekt)

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
| E3 Organisation | ⏸ unterbrochen (siehe §1) |
| E4 Pädagogik | ⏸ unterbrochen (siehe §1) |
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
- **NEU 13.11. abends (f794f4f): eNOTE « Enregistrer sans encaisser »** – Leistungen anwesender Kunden (Praxis mit Übernachtung) fest erfassen, am Ende alles zusammen kassieren; Details in eNOTE/Projektuebersicht.md, Bilder Pictures/eNOTE-Prestations-a-payer-2026-11-13. **Neue Migration 20261113221524_AufenthaltsPositionen nur LOKAL** → vor dem nächsten eNOTE-Deploy auf Azure einspielen (**nur mit OK**), sonst stürzt der Verkauf ab (neue Spalte VerkaufsPositionen.ErbrachtAm).
- **SKOULOU (alt) deployen**, falls noch nicht geschehen (Listen-Änderungen). eNOTE erst nach der Azure-Migration oben deployen.
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

1. Video-Pilot mit `lernen-v3\AUFTRAG-Pilot.md` neu starten (Aufnahmen sind fertig) → Ergebnis dem Nutzer zeigen.
2. E3 und E4 mit ihren Auftragsdateien neu starten (im vorhandenen Worktree weiterarbeiten).
3. Danach mergen + Migration neu erzeugen; dann E2, E5, E6 (Aufträge nach dem Muster von E3/E4 schreiben, Bauplan §12 für Dateizuständigkeit).
4. E7 Simulation + Browser-Rundgang; E8 Umstellung (Azure nur mit OK); danach SKOULOU-Videos auf der neuen Oberfläche.
5. Bei OK zum Pilot: alle eNOTE-Videos im v3-Stil, Einbau, Deploy durch den Nutzer.
