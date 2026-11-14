# Auftrag « Echte Simulationen » für eNOTE und SKOULOU (Stand 14.11.2026, abends)

Nutzer 14.11.: « Führe für beide Apps echte Simulationen mit möglichst vielen Daten und Perioden durch. Dabei bitte alle
Features testen, möglichst viele Klicks, um mögliche Fehler zu entdecken. Man weiß nie. » – « Ich meine damit, die Apps im
Hintergrund starten, nicht normale Tests. Oder vielleicht beides. » Danach deployt der Nutzer, wenn alles ok ist.

Vier Läufe, **nacheinander, immer nur 1 Agent** (Limit des Nutzers). Stand je Lauf in der Tabelle pflegen.

| Lauf | Inhalt | Stand |
|---|---|---|
| S1 | eNOTE Daten-Simulation (Massendaten über echte Services, viele Monate, Invarianten, Antwortzeiten), DB `ENOTEDB_SIM` behalten | offen |
| S2 | eNOTE Klick-Simulation: App im Hintergrund auf `ENOTEDB_SIM`, viele simulierte Nutzer klicken alle Funktionen aller Rollen | offen |
| S3 | SKOULOU Daten-Simulation in voller Größe mit den neuen Regeln (Session-Ende, Gebühr je Session, Hauptadmin), `SkoulouDB_SIM` neu | offen |
| S4 | SKOULOU Klick-Simulation: App im Hintergrund auf `SkoulouDB_SIM`, simulierte Nutzer aller Rollen führen echte Abläufe über Perioden aus | offen |

Falls ein Lauf unterbrochen wird: neuen Agenten mit diesem Text + Laufnummer starten und ergänzen « Continue: check git
log/status and what already exists for this run, then finish it. »

---

## Gemeinsame Regeln (alle Läufe)

Repo `C:\ETSE\NETAPPS\TabloSuiteData\TabloSuiteProjekt` (Hauptarbeitsbaum, kein anderer Agent arbeitet gleichzeitig).
Blazor Server .NET 10, EF Core SQL Server, französische UI mit « vous », deutsche Bezeichner. Markt Togo/Westafrika, FCFA.

- **Nur lokal.** Eigene Datenbanken `ENOTEDB_SIM` / `SkoulouDB_SIM` bzw. zufällige `*_SIM_*`/`*_TEST_*` auf localhost
  (Windows-Auth). Die Entwicklungs-DBs `ENOTEDB` und `SkoulouDB` nur lesen. **Nie Azure** (kein `az`, keine Azure-DB, kein
  Azure-Blob), kein Push, keine Deploys.
- **Nichts nach außen:** keine echten Mails (Postmark aus bzw. nur Outbox – vor dem Start prüfen, z. B. `Postmark__Enabled=false`
  / Mail-Status « Envoi inactif »), keine SMS/WhatsApp, keine echten Zahlungsanbieter. eNOTE-Dateien nur in den lokalen
  Speicher (`Storage:LocalDataRootPath` aus appsettings.Development.json bzw. ein eigener Temp-Ordner); falls ein Pfad zu
  echtem Azure-Speicher führen würde: abbrechen und melden.
- App im Hintergrund: eigener Build-Ordner (`dotnet build <proj> -o %TEMP%\sim\<app>\build`), Start mit
  `ASPNETCORE_ENVIRONMENT=Development`, Connection-String per Umgebungsvariable auf die SIM-DB, eigener Port
  (eNOTE 5291, SKOULOU 5181), Server-Log in eine Datei mitschreiben. Visual Studio des Nutzers nicht stören; nur selbst
  gestartete Prozesse beenden (Elternprozess prüfen, nie devenv/VsDebugConsole).
- Playwright: `playwright-core` in `C:\Users\etsec\AppData\Local\Temp\skoulou-pw\node_modules` (Skripte dort oder per
  Junction), `chromium.launch({ channel: 'msedge', headless: true })`, Desktop 1440×900 und Handy 390×844.
- **Fehler finden:** Konsolenfehler, `pageerror`, Antworten ≥ 400 (außer erwartete), sichtbares Blazor-Fehlerband
  (`#blazor-error-ui`), getrennte Verbindung/Reconnect, Fehler/Exceptions im Server-Log, Aktionen > 2 s, Seiten > 1 s,
  horizontales Überlaufen am Handy, falsche Beträge/Summen, doppelte oder fehlende Nummern, Texte ohne Akzente/englische
  Wörter/Platzhalter, leere Zustände ohne Erklärung, Knöpfe ohne Wirkung, Dialoge, die sich nicht schließen lassen.
- **Beheben** an der Wurzel (kleine Änderungen, nicht umbauen) mit Regressionstest; danach den betroffenen Ablauf erneut
  klicken. Schemaänderungen vermeiden; falls unvermeidbar: NEUE inkrementelle Migration (eNOTE: `eNOTE/eNOTE.WebApp/Migrations`;
  SKOULOU: `dotnet ef migrations add … --project Skoulou/Skoulou.WebApp --configuration Release`), nie bestehende ändern,
  im Bericht genau nennen (Azure spielt die Leitung ein).
- Vor jedem Commit: Build 0 neue Warnungen, Tests der App grün (`dotnet test eNOTE/eNOTE.Tests/eNOTE.Tests.csproj` bzw.
  `Skoulou/Skoulou.Tests/Skoulou.Tests.csproj`, SKOULOU einmal zusätzlich mit `SKOULOU_SQL_TESTS=1`). Grüne Stände selbst
  committen (« eNOTE Simulation S1: … » / « SKOULOU Simulation S3: … », letzte Zeile
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`), **nicht pushen**.
- Geheimnisse nie ausgeben (`eNOTE/.env` nie lesen/sourcen); Zugangsdaten der Sim-Konten nur in `%TEMP%\sim\<app>\konten.json`.
  E-Mails in Bildern maskiert, nur erfundene Namen (französisch/togoisch).
- Temp nach `%TEMP%\sim\<app>\`, am Ende in den Papierkorb (PowerShell Microsoft.VisualBasic FileSystem SendToRecycleBin;
  Junctions vorher mit `cmd /c rmdir` lösen). Bilder nur sparsam, Stichproben nach `C:\Users\etsec\Pictures\Simulation-<app>\`.
- Sparsam arbeiten: Dateien in Ausschnitten lesen, Auswertung über Logs/Zähler statt über viele Bilder.
- Bericht der Simulation als `docs/Simulation-eNOTE.md` (eNOTE) bzw. Abschnitt in `Skoulou/docs/Simulation-Bericht.md` (SKOULOU),
  Deutsch, kurz: Datenmenge, Laufzeit, Abläufe/Klicks, Invarianten, gefundene + behobene Fehler (mit Commit), offene Punkte.
  Am Ende die Zeile des Laufs in der Tabelle oben auf « erledigt » setzen.

---

## S1 – eNOTE Daten-Simulation

Erst lesen: `eNOTE/Projektuebersicht.md`, die Services in `eNOTE/eNOTE.WebApp/Services`, `eNOTE/eNOTE.Tests` (Hilfen,
z. B. `KassenAblaufEndToEndTests.cs`). Vorbild für Aufbau und Invarianten: `Skoulou/Skoulou.Tests/Simulation/*` und
`Skoulou/docs/Simulation-Bericht.md`.

1. Neuer Ordner `eNOTE/eNOTE.Tests/Simulation/` mit Opt-in-Test (`ENOTE_SIM=1`, sonst übersprungen; `ENOTE_SIM_BEHALTEN=ENOTEDB_SIM`
   behält die DB; `ENOTE_SIM_FAKTOR` für kleine Läufe), echte SQL-Server-DB, fester Zufalls-Seed, globale Uhr.
2. Alles über die **echten Services** (keine Roh-Inserts), damit Rechte und Regeln greifen: SuperAdmin + weitere Admins,
   ≥ 3 Responsables; **≥ 30 Läden** verschiedener Art (Salon de coiffure, Institut, Boutique, Restaurant/Maquis, Pharmacie-
   ähnlich, Praxis mit Aufenthalten …) in mehreren Ländern/Währungseinstellungen, Registrierung → Validierung/Ablehnung,
   Probezeit → Abo, Kündigung einzelner Läden; Besitzer, ≥ 150 Mitarbeitende mit verschiedenen Rechten (Kasse, Erstatten …),
   Kassen-PIN; Katalog mit Kategorien/Farben/Favoriten/Barcodes, Lager/Bestände, Leistungen; Kunden + Kundenkartei, Besuche,
   Notizen, Präsenz/Anwesende, Räume/Plätze, Aufenthalte mit « Enregistrer sans encaisser ».
3. **≥ 18 Monate** Betrieb: täglich Kassen öffnen/schließen, ≥ 100.000 Verkäufe (Bar, Mobile Money, Karte, gemischt), Rabatte,
   Tickets « en attente », Erstattungen/Stornos, Ausgaben/Entnahmen/Einlagen, Kassendifferenzen, Abschlüsse; Abo-Rechnungen,
   gemeldete/bestätigte/abgelehnte Zahlungen, Preisänderungen (Senkung sofort, Erhöhung zum Monatsanfang), individuelle Tarife,
   Responsable-Kommissionen + Auszahlungen; Annonces, Mails (nur Outbox), Kontaktformular, Journale.
4. **Invarianten** (mit klaren Meldungen): Bon-/Belegnummern je Laden lückenlos und eindeutig; Kassenstand = Summe der
   Bewegungen; Tages-/Monats-/Finanzberichte = Summe der Verkäufe − Erstattungen; Bestände = Zugänge − Abgänge; Kundenumsätze
   = Summe ihrer Verkäufe; Abo-Rechnungen je Laden und Monat höchstens einmal, Preis/Tarif zum richtigen Datum; Kommissionen
   nur aus bestätigten Zahlungen; Rechte: jede Rolle bekommt « refusé » für fremde Läden (Stichproben); gekündigte/gesperrte
   Läden können nicht mehr kassieren.
5. **Antwortzeiten** der Hauptlisten/Berichte (Verkäufe, Kasse, Finanzen, Kunden, Admin-Läden, Facturation) auf vollen Daten;
   > 500 ms → beheben (Index, Projektion, Seiten) und erneut messen.
6. Bericht `docs/Simulation-eNOTE.md`, commit. DB `ENOTEDB_SIM` mit Endstand behalten (für S2).

FINAL REPORT S1 (≤ 30 Zeilen, Deutsch): Commits, Datenmenge, Laufzeit, Invarianten, Fehler gefunden/behoben, Schemaänderungen,
Antwortzeiten vorher/nachher, offene Punkte.

## S2 – eNOTE Klick-Simulation (App im Hintergrund)

1. App im Hintergrund auf `ENOTEDB_SIM` starten (siehe Regeln), Konten je Rolle mit bekannten Passwörtern per kleinem Helfer
   (nur in `%TEMP%\sim\enote\konten.json`).
2. Playwright-Skripte mit **mehreren gleichzeitigen simulierten Nutzern** (z. B. 6–10 Browser-Kontexte parallel, Desktop und
   Handy gemischt): Employé (Kasse öffnen, viele Verkäufe mit allen Zahlarten, Rabatt, Ticket parken/fortsetzen, Kunde zuordnen,
   Anwesende/Aufenthalt « Enregistrer sans encaisser » → « À la caisse », Erstattung, Ausgabe, Kassenabschluss), Propriétaire
   (Katalog/Kategorien/Leistungen anlegen/ändern/archivieren, Lager, Équipe + Rechte + PIN, Kunden/Kundenkartei, Finanzen und
   Berichte mit Zeiträumen, Paramètres, Abonnement, Profil, Kündigung bis zum letzten Bestätigungsschritt und abbrechen),
   Responsable (Établissements, Nouvel établissement, Valider, Essai, Demande, Facturation), Admin/Hauptadmin (Établissements,
   Tarif personnalisé, Tarifs + Preisänderung, Mobile Money, Administrateurs, Facturation Confirmer/Refuser, E-mails nur Entwurf/
   Vorschau, Annonces publizieren/ausblenden, Journaux), Visiteur (Startseite, Registrierung, Kontaktformular, Videos, Hilfe).
   Jeder Ablauf mehrfach mit wechselnden Daten; danach Stichproben-Prüfung, ob die UI-Aktionen in der DB richtig ankamen
   (Summen, Nummern, Kassenstand).
3. Zusätzlich ein **« Monkey »-Durchlauf** je Rolle: auf jeder erreichbaren Seite alle sichtbaren Knöpfe/Links/Reiter/Filter
   der Reihe nach anklicken, Dialoge öffnen und wieder schließen, Formulare leer und mit Unsinn absenden (Pflichtfelder,
   negative Beträge, sehr lange Texte, Sonderzeichen/Akzente), Zurück-Taste, Doppelklick auf Speichern, Seite neu laden mitten
   im Ablauf. Destruktive Endschritte (Konto löschen, Kündigung endgültig) nur auf Wegwerf-Konten.
4. Alle Funde beheben (siehe Regeln), Abläufe erneut klicken, bis ein vollständiger Durchlauf sauber ist. Anzahl Klicks/Aktionen
   je Rolle zählen. Abschnitt « Klick-Simulation » in `docs/Simulation-eNOTE.md`, commit. App stoppen.

FINAL REPORT S2 (≤ 30 Zeilen, Deutsch): Commits, simulierte Nutzer/Abläufe/Klicks je Rolle, Fehler gefunden/behoben (gruppiert),
langsame Aktionen, Schemaänderungen, offene Punkte, 6–8 aussagekräftige Bilder.

## S3 – SKOULOU Daten-Simulation (volle Größe, neue Regeln)

Basis: `Skoulou/Skoulou.Tests/Simulation/*`, `Skoulou/docs/Simulation-Bericht.md`, `Skoulou/docs/Bauplan.md`. Seit E7a neu
(Commits f67552d … 495ac44): `SessionRegel` (Session-Klassen enden automatisch), Gebühr je Session (`GebuehrenPosten.PeriodeId`),
Wiederholversuch bei gleichzeitiger Kontoanlage, `/direction/journal`, Bedingungen bei der Registrierung, Hauptadmin +
`/admin/administrateurs`.
1. Simulation erweitern: Gebühr je Session in den Sprachschulen (inkl. unabhängigem Gebührenbuch, das Session-Posten
   berücksichtigt), Session-Teilnehmer ohne Wechsel, Hauptadmin legt weitere Admins an/sperrt/archiviert, gleichzeitige
   Kontoanlagen (ohne stilles Wiederholen in der Simulation), mehr Schulen/Jahre wenn die Laufzeit es erlaubt (Ziel ≥ 30 Schulen,
   ≥ 4 Schuljahre, ≥ 12.000 Schüler).
2. Invarianten ergänzen: Session-Posten nur für Teilnehmer der Session; kein Schüler nach Session-Ende in Listen/Abrechnung;
   Hauptadmin kann nicht gesperrt/archiviert werden; nur Hauptadmin verwaltet Admins.
3. Voller Lauf (Faktor 1) mit `SKOULOU_SIM_BEHALTEN=SkoulouDB_SIM` (alte SIM-DB vorher ersetzen), Antwortzeiten neu messen.
4. Bericht-Abschnitt « Simulation S3 » in `Skoulou/docs/Simulation-Bericht.md`, commit.

FINAL REPORT S3 (≤ 30 Zeilen): wie S1.

## S4 – SKOULOU Klick-Simulation (App im Hintergrund)

1. App im Hintergrund auf `SkoulouDB_SIM` (Port 5181), Konten je Rolle (nur in `%TEMP%\sim\skoulou\konten.json`).
2. Mehrere gleichzeitige simulierte Nutzer **führen echte Abläufe über mindestens eine ganze Periode** aus, nicht nur ansehen:
   Visiteur (Registrierung Schule mit Bedingungen, Schüler-Selbstregistrierung, Kontakt), Hauptadmin/Admin (Validierung,
   Responsables, Administrateurs, Tarife, Facturation, Annonces, Messagerie, Journal/Audit, Médias), Responsable (Mes parts),
   Direction (Schuljahr/Perioden, Klassen, Fächer, Personal, Einschreibungen, Klassenwechsel, Austritt, Gebühren inkl. Session-
   Posten, Raten, Ermäßigungen, Zahlungen bar/Mobile Money, Storno, Journal, Präsenz, Stundenplan, Conseil, Bulletins
   veröffentlichen/berichtigen, Plattformrechnung melden, Jahreswechsel-Assistent bis zur Vorschau), Coordinateur mit/ohne
   Finanzrecht, Enseignant (Notenraster, Lektionen, Prüfungen, Appell), Élève (Lernen, Prüfungen ablegen, Bulletins), Parent
   (Kinder, Noten, Frais, Reçus). Danach Stichproben in der DB (Summen, Nummern, Ränge).
3. « Monkey »-Durchlauf je Rolle wie in S2.3.
4. Funde beheben, wiederholen bis sauber; Abschnitt « Klick-Simulation » im Simulationsbericht, commit, App stoppen.

FINAL REPORT S4 (≤ 30 Zeilen): wie S2.
