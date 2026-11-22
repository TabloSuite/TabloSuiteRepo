# TabloSuite – Arbeitsstand 22.11.2026 abends

Einstieg für ein neues Chat-Fenster: zuerst §0 lesen. Gesamtüberblick über alle Apps: [`docs/Projektuebersicht.md`](docs/Projektuebersicht.md).
SKOULOU verbindlich: `Skoulou/docs/Bauplan.md` (§11 Entscheidungen) und `Skoulou/docs/fachkonzept/01…05`.
Die ausführliche Fassung dieser Datei bis 22.11. nachmittags steht in der Git-Historie (`git show a646d2d:STAND.md`).

---

## 0. HIER WEITERMACHEN

**Stand:** Alles liegt im Branch `Etse.TabloSuite.Muster` und ist gepusht. **Nicht deployt** – live läuft noch der Stand vom 21.11.
(`81cba64`). Seit dem letzten Deploy gibt es **keine neue Migration**: Azure `ENote-Database` und `Skoulou-Database` bleiben
unverändert. TabloSuiteHome ist seit dem letzten Deploy unverändert.

**Beim Nutzer offen (22.11. spät, Freigabe « alle A–D nach Empfehlung »; beides von der Sicherheitsprüfung für Claude gesperrt):**
- **master:** `git push origin Etse.TabloSuite.Muster:master` im Projektordner (Fast-Forward, master hat nichts Eigenes) – oder in
  Visual Studio zu master wechseln, `Etse.TabloSuite.Muster` hineinführen, pushen.
- **Test-Datenbanken löschen:** `TabloSuiteData\Infos\Test-Datenbanken-loeschen.ps1` (13 Test-DBs, fragt vorher; `ENOTEDB`, `SkoulouDB`,
  `SkoulouDB_VIDEO` geschützt).

**Nächster Schritt – beim Nutzer:** kompletter Menschentest beider Apps (nicht mehr am 22.11.). Nutzer: « Solange Tests Fehler
liefern, kann ich die Apps nicht in Produktion nehmen. » Gefundene Fehler gründlich beheben (mit Test, auch in der Oberfläche prüfen).
Danach deployt der Nutzer **eNOTE und SKOULOU** aus Visual Studio; anschließend lesend prüfen (Startseiten, Anmeldung,
ExceptionLogs/FehlerProtokolle).

**Rückfragen R1–R10 entschieden (22.11. spät, alle nach Empfehlung):** R5, R7, R8, R9 umgesetzt (Bauplan §11 Nr. 45–48):
Eintrittsperiode eines Neuzugangs ohne jede Note zählt nicht fürs Jahresmittel (« arrivé(e) en cours de période, sans note »);
Warnung « l'année n'est pas terminée » nach dem Ende der letzten zählenden Periode; Hinweis, die Rückzahlung einer stornierten
bezahlten Rechnung nicht doppelt zu buchen (Mail + Rechnungsdetail); Menüeintrag lädt die offene Seite auch mit anderen
Adressparametern neu. So gelassen: R1 (kein Knopf « Nouveau lien »), R2, R3, R4, R6, R10.
Kleine Annahmen dabei (nur bei Bedarf ändern): « abs. »/« disp. » zählen als Note; Rückkehr nach einer Lücke in der Periode gilt als
Eintritt; wer wenige Tage nach Periodenbeginn eingeschrieben wird, gilt formal als eingetreten (wirkt nur ohne jede Note).

**Was seit dem letzten Deploy (21.11.) dazukam – rund 90 Commits, alle mit Tests:**
1. **Klicktest « wie ein Mensch » über Jahre** (21./22.11., Playwright, Test-Uhr): eNOTE 23.11.2026–02.01.2029 (Normaltest + Fehlersuche),
   SKOULOU 3 Schuljahre (Normaltest + Fehlersuche). Kein fataler Fehler, kein Datenverlust; alle Rechnungen, Provisionen, Bulletins und
   PDFs nachgerechnet. Rund 45 Fehler gefunden und behoben. Berichte und Skripte: `TabloSuiteData\Infos\Klicktests-2026-11\`.
2. **Fachfragen Runde 1** (22.11. vormittags, « alle nach Empfehlung »): eNOTE gleichnamige Mitarbeitende/Artikel/Remises abgelehnt, kein
   negativer Bestand per Eingabe, Symbol statt Emoji, Admin-Liste « Établissements » aktualisiert sich alle 30 s, PDFs ohne Ligaturen;
   SKOULOU « Réémettre », nie gestartetes Jahr abrechnen, Passage-Warnungen, Lehrkraft ohne Lehrauftrag, Messagerie Responsable,
   Periodensperre nach Abrechnung, Lückentext « | », PDFs ohne Ligaturen (Bauplan §11 Nr. 26–33).
3. **Fachfragen Runde 2** (22.11. abends, 22 Fragen, « alle nach Empfehlung »):
   - eNOTE: Vertragssperre je Laden statt `Serializable` (keine Deadlocks am Monatsanfang), Datum überall « 31/03/2028 », Beträge in
     Mails « 38.880 F », Mail « Mot de passe modifié » standardmäßig an, Écran client: nur der Link des aktuellen Kassengeräts gilt,
     Enter sendet Passwortformulare ab, Namen im Team ohne Akzent-Unterschied, Prestation und Produit nicht gleichnamig;
     Besitzer kann sich in « Mon profil » nicht wie ein Teammitglied nennen.
   - SKOULOU (Bauplan §11 Nr. 34–44): Menüeintrag lädt die offene Seite neu, Jahresmittel bei Neuzugängen nur aus eingeschriebenen
     Perioden, Passage mit Kapazitätswarnung, Archivierung der Abgänger-Konten (Schalter, Standard an) und Bestätigung vor Jahresende,
     Storno bezahlter Plattformrechnung mit Gegenbuchung + Mail, Rechnung/PDF « à partir du … (mois d'inscription) », Suche in
     « Facturation » über alle Status, Start-Rückfrage nennt den eingefrorenen Preis, « Corrections » ohne Lehrauftrag nur lesbar.
   - So gelassen: « 6 / 7 » = « 6/7 », neuer Mobile-Money-Anbieter sofort an allen Kassen ohne eigene Auswahl, « Remettre en stock »
     standardmäßig an, Ladenwechsler bleibt.
4. Beide Runden in der Oberfläche nachgetestet (eNOTE 51 Schritte, SKOULOU 36 Schritte, je ohne App-Fehler).

**Tests am 22.11. abends – alles grün** (nach R5–R9 erneut: Mappe Home 11, eNOTE 684, SKOULOU 845; SKOULOU-SQL 866; SKOULOU-Simulation
mit diesem Code grün). Erster Gesamtlauf: `dotnet test TabloSuiteMappe.slnx` (Home 11, eNOTE 684, SKOULOU 838);
SKOULOU mit `SKOULOU_SQL_TESTS=1` + `SKOULOU_SIM=1` 861 bestanden (22 min); eNOTE mit `ENOTE_SIM=1` (SQL-Tests + Simulation)
690 bestanden (13 min). Aufgeräumt: Worktrees der Agenten, alte Testreste in `%TEMP%` (Papierkorb).

---

## 1. Live-Betrieb (Azure)

- Ressourcengruppe `TabloSuiteRGruppe`: Web-Apps `enote` (enote.tablosuite.com), `skoulou` (skoulou.tablosuite.com),
  `tablosuite-home` (tablosuite.com); SQL `tablosuite-sqlserver` mit `ENote-Database` und `Skoulou-Database`.
  **Lesen erlaubt, jede Änderung nur nach ausdrücklichem OK.** Migrationen erst unmittelbar vor dem Deploy.
- Deploy macht der Nutzer aus Visual Studio. SKOULOU-Migrationen mit `dotnet ef database update --connection "<Ziel>"`;
  eNOTE-Migrationen nach dem Ablauf in `eNOTE/Deployment-Azure.md`.
- Admin-Konto in beiden Apps einheitlich (Seed-Einstellungen); Schlüssel (Postmark, KI, Speech) nur in den App-Einstellungen.
- Prüfen beim nächsten gemeinsamen Termin: Sind `AzureSpeech__Key`/`AzureSpeech__Region` in der Web-App `skoulou` gesetzt
  (sonst liest SKOULOU mit der Browser-Stimme vor)?

**Vor Produktion – nur gemeinsam mit dem Nutzer** (Nutzer 16.11.: « Vor Produktion machen wir alles zusammen »):
- App Service: `ENote-AppServicePlan` und `Skoulou-AppServicePlan` je B1 – reicht das für Blazor Server mit vielen Verbindungen?
- Azure SQL: beide Datenbanken Basic (5 DTU, 2 GB) – voraussichtlich erster Engpass; Optionen Standard S0/S1 oder vCore serverless.
- Speicher `tablosuiteblobbtorage` Standard_LRS: Redundanz und Soft Delete für Belege/Medien klären.
- Speech `enote-speech` F0 (geteilt mit der Videoproduktion): eigene S0-Ressource für SKOULOU, Produktbedingungen prüfen.
- Überwachung/Warnungen, Budget-Warnung, Sicherungen der Datenbanken.

## 2. Offen / später (keine Fehler, Entscheidung des Nutzers)

- eNOTE: Bondrucker und Scanner mit echter Hardware testen; mehrere Kassen/Schubladen je Laden (Option B) nur bei echtem
  Kundenbedarf; Treueprogramm zurückgestellt.
- Videos: Admin « Tarifs et facturation » zeigt noch die alte Provisionsberechnung; Employé-Videos (`encaisser`, `attente`,
  `clients`, `cloture`) bei Neuaufnahme mit `demo.proprio` + « Qui encaisse ? ». Ältere Videos zeigen Daten noch als « 31.03. »
  (nur Optik). Pipelines und Fallen: `TabloSuite Videos\…\projekte\lernen-v3\README.md` bzw. `AUFTRAG.md`.
- Optional: « Pourquoi SKOULOU ? » im Muster Problem → Beweis in der App → Rückbezug neu.

## 3. Feste Regeln (Kurzfassung, vollständig in `CLAUDE.md`)

- Antworten auf Deutsch, kurz; App-Texte Französisch mit « vous ». Zwischendurch melden, was gerade läuft.
- Offene Fragen immer vollständig in einer Nachricht, jede mit Empfehlung; vor dem Start größerer Arbeiten kurz Bescheid sagen.
- Nur im eigenen Branch arbeiten; fertige, grüne Schritte committen + pushen; `master` nur nach ausdrücklichem OK.
- Keine Geheimnisse in Repo-Dateien; `eNOTE/.env` nie ausgeben oder sourcen.
- Neue Funktionen auch in der Oberfläche prüfen; vor jedem Deploy alle Tests, SQL-Tests und beide Simulationen
  (`dotnet test TabloSuiteMappe.slnx`, `SKOULOU_SQL_TESTS=1`, `SKOULOU_SIM=1`, `ENOTE_SIM=1`).
- Aufräumen über den Papierkorb; Videos-Arbeitsordner nicht umräumen (Backup nur als Kopie).
- Vor dem Beenden eines eNOTE/SKOULOU-Prozesses den Elternprozess prüfen (Visual Studio = Nutzer, nicht anfassen).
