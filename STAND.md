# TabloSuite – Arbeitsstand 16.11.2026

Diese Datei ist der Einstieg für ein neues Chat-Fenster. Erst lesen, dann weitermachen.
Die vollständige Planung für SKOULOU steht in `Skoulou/docs/Bauplan.md` (verbindlich) und `Skoulou/docs/fachkonzept/01…05`.

---

## 0. HIER WEITERMACHEN (neuer Chat) – Stand 16.11.2026

**NÄCHSTE AUFGABE im neuen Chat (Nutzer 15.11. abends: « Ja, SKOULOU-Videos neu erstellen »; am 16.11. bestätigt: « danach
mit dem Stand aus STAND.md weitermachen – es ging um SKOULOU-Videos »):** die 9 alten SKOULOU-Lernvideos (alte Oberfläche,
KI-Sprecherin, 0:42–2:03) im v3-Stil der eNOTE-Videos neu machen. Vollständiger Auftrag (Stil, Pipeline,
Aufnahme-Umgebung, Videoliste P/A1–D2, Ablauf) im Videoordner:
`C:\ETSE\NETAPPS\TabloSuite Videos\Skoulou Videos\projekte\lernen-v3\AUFTRAG.md` – **zuerst ganz lesen**; Vorlage der Pipeline:
`C:\ETSE\NETAPPS\TabloSuite Videos\ENote Videos\projekte\lernen-v3\README.md`. Reihenfolge: (1) Pipeline nach
`Skoulou Videos\projekte\lernen-v3\` kopieren und an SKOULOU anpassen, (2) Aufnahme-Datenbank `SkoulouDB_VIDEO` +
Demo-Schule anlegen, (3) **Pilot** « Préparer l'année scolaire » (Direction) fertigstellen und dem Nutzer zeigen (Pfad,
Länge, Szenenfolge, 2–3 Standbilder), (4) erst nach seinem OK die übrigen (Pakete A–D), je Video einbauen +
`VideoKatalog.cs` + commit/push. Keine Agenten, eine Aufgabe nach der anderen, sparsam arbeiten (Limit).
**Stand 16.11. vormittags: (1)–(3) erledigt** – Pipeline + Aufnahme-Umgebung eingerichtet (Einzelheiten AUFTRAG.md §6:
App `http://localhost:5191` aus `%TEMP%\skoulou-video\build`, DB `SkoulouDB_VIDEO` mit Basis-Sicherung, frische Schule
« Collège Démo Les Cocotiers », Direktorin `afiwa.kodjovi`); Pilot gerendert (5:54, 6 Schritte), **wartet auf das OK des
Nutzers**. Noch NICHT in der App (`einbau.py` + `VideoKatalog.cs` erst nach Freigabe). Endfassung
`Skoulou Videos\projekte\lernen-v3\direction-preparer\renders\skoulou-direction-preparer.mp4` (−16,7 LUFS), Standbilder
`Pictures\SKOULOU-Videos-v3\pilot\`. Aufnahme-App läuft gerade nicht (bei Bedarf neu starten, siehe AUFTRAG.md §6).

**Zwei Nutzerwünsche für später (16.11.):** (1) SKOULOU-Audio-Aufgaben (Fragetypen `AudioTexte`, `AudioChoixUnique`,
`AudioChoixMultiple`) mit **Azure-TTS** statt der Browser-Stimme (`paedagogik.js` `vorlesen` → `speechSynthesis`); Sprache
je Aufgabe wählbar (heute `FragenTypen.Sprachen`: fr, en, de, es). **Vor** den Videos C2 « Leçons et examens » und
D1 « Apprendre avec SKOULOU » erledigen, weil diese die Audio-Aufgaben zeigen. (2) ✅ **Erledigt 16.11. (24039f0)**:
Seite hinter offenen Fenstern rollt in eNOTE und SKOULOU nicht mehr mit (CSS `html:has(...)` sperrt Seite/Hauptbereich,
`overscroll-behavior: contain`; im Browser Computer + Handy geprüft).
**Pilot freigegeben** (Nutzer 16.11.: « sehr gut, weiter so ausführlich !! … Weitermachen wie es dir passt ») und
eingebaut (de03adb). Die übrigen Videos ohne Einzel-Freigabe im selben Stil, Reihenfolge: A1, A2, B1–B4, dann Azure-TTS,
dann C1, C2, D1, D2.
✅ **A1 « Premiers pas : se repérer »** (16.11. mittags, 089f369): 5:20, 7 Schritte, Lehrer Yao Kpodar der Demo-Schule
« Les Palmiers » (Anmeldung, erstes Passwort, Startseite, Menü, **echte Handy-Aufnahme im Gerätrahmen**, Schuljahr oben,
Konto + Hilfe); in der App zuerst unter « Pour bien commencer ». Pipeline-Neuerungen (Handy, `nachOben`, Startskript der
Aufnahme-App) in `lernen-v3/AUFTRAG.md` §6. Tests SKOULOU 572 grün. **Nächstes: A2 « La visite guidée ».**
Noch nicht deployt (zusammen mit den übrigen Videos am Ende).
✅ **Stand 16.11. abends (alles gepusht, noch nicht deployt):** A1 « Premiers pas » 5:20 · A2 « La visite guidée » 3:57 (a3b8d8f) · B1 « Élèves et inscriptions » 4:42 · **K1 « Le concept : l'année scolaire » 6:23** (Folien-Theorievideo, neuer Szenentyp `folie` in `lernen-v3/folie.py`). Danach ✅ B2 « Équipe et emplois du temps » 4:30, ✅ B3 « Notes, conseil et bulletins » 4:13, ✅ K2 « Le concept : frais et paiements » 4:16 (alle gepusht; Nutzer hat am 16.11. abends schon einmal deployt – Stand bis K1). **Nächstes: B4**, dann Azure-TTS, C1, K3 + C2, D1, D2, danach eNOTE-Konzeptvideos. Einzelheiten und Fallen: `lernen-v3/AUFTRAG.md` §6.
✅ **Zwei Nutzerwünsche 16.11. nachmittags** (gepusht, noch zu deployen): (1) **Logo « S » im eNOTE-Blau** statt Blau→Türkis
(34cfd25: Logo, Favicon SVG/PNG, Mail-Kopf, Firmenseite; 0af1e94: Pilot, « Premiers pas », « Pourquoi SKOULOU ? » +
Startseiten-Video ohne neue Aufnahme neu gerendert). (2) **Menü « Établissement » in der Reihenfolge des Jahresstarts**
(f41e9e9): Année scolaire → Classes → Matières → Équipe → Emplois du temps → Élèves → Parents; Startseiten-Kacheln folgen,
Guide « Démarrer l'année » angeglichen (Classes vor Matières). Kein neues Video (Nutzer).

**NEU 16.11. nachmittags – Theorie-Folienvideos (Nutzer):** « Préparer l'année scolaire » ist « ziemlich gut »; davor soll ein ausführliches Folien-Video (« PowerPoint » als Video, animierte Folien, Stimme Vivienne) die Direktion **theoretisch** auf das Konzept vorbereiten – Umfang **ganzes Schuljahr**. Danach (Nutzer: « bei den anderen großen Prozessen auch und bei eNOTE auch. Du entscheidest, sei kreativ und ausführlich, die Nutzer in Togo usw. sind mit großen Apps nicht vertraut »). **Plan:**
- SKOULOU: ✅ K1 « Le concept : l'année scolaire » (6:23, vor dem Pilot, gepusht) · ✅ K2 « Le concept : frais et paiements » (vor B4) · K3 « Le concept : leçons, exercices et examens » (vor C2) · danach die übrigen Praxisvideos B2–D2 wie geplant.
- eNOTE (danach): Konzeptvideos vor den großen Abläufen – Kasse von der Öffnung bis zur Clôture (Écart), Catalogue/Stock/Prix, Clients/Fidélité, Équipe/Droits/Code PIN, Finances. Stil gleich (Folien-Typ `art: "folie"` in `lernen-v3/bau.py`).

**OFFEN – am Ende prüfen (Nutzer 16.11. abends):** Auf der Seite « Vidéos d'aide » **springt die Scrollleiste hin und her** – in **beiden Apps** (eNOTE + SKOULOU) prüfen und beheben (Verdacht: Seitensperre bei offenem Fenster `html:has(...)` aus 24039f0 blendet die Scrollleiste aus/ein → Seite verschiebt sich; oder gemerkte Scrollposition aus 630ad20). Am Computer und am Handy testen. **Nutzer-Detail:** am Handy – beim Herunterscrollen springt die Seite wieder nach oben (Verdacht: Adressleiste blendet aus → Fenstergröße ändert sich → gemerkte Position wird zurückgesetzt). Analyse: `skVideoPosition` (skoulou.js ~Z. 258, enote.js ~Z. 200) horcht auf `resize`, solange ein Video offen ist, und hört erst 2,5 s nach `zurueck()` auf – wird das Fenster anders geschlossen (Zurück-Taste, Wischen), bleibt es aktiv und jede Adressleisten-Größenänderung springt zurück. Fix: nur bei echtem Vollbild/Drehung setzen, beim eigenen Scrollen des Nutzers abschalten, Schließen des Dialogs zuverlässig erkennen.

✅ **Azure-TTS für SKOULOU-Audio-Aufgaben übernommen (a2fb7b9, 16.11. abends, per Agent, 631 Tests grün):** Server-Synthese über Azure Speech mit Zwischenspeicher je Schule (Blob), Endpunkt `/vorlesen/{hash}` (nur angemeldet, eigene Schule, Rate-Limit), Rückfall auf die Browser-Stimme. Im Editor « Langue du texte » + **« Niveau » A1–C2** (A1 −35 % … B1 −15 % Standard … C2 +5 %) + « Écouter »; neue Sprachen it, pt, ar, zh (8 Stimmen). Keine Migration (Niveau steckt in `Frage.Sprache`, z. B. « en-US/A2 »). **Vom Nutzer in Azure (Web-App SKOULOU, Konfiguration) einzutragen:** `AzureSpeech__Key` (Schlüssel der Speech-Ressource, derselbe wie für die Videos) und `AzureSpeech__Region` (z. B. `westeurope`) – ohne diese Einträge bleibt die Browser-Stimme. **Am Ende gemeinsam prüfen:** Tempi A1–C2 anhören, iPhone/Safari testen.

**Deploy:** Beide Apps wurden am 15.11. abends deployt (Stand 630ad20, öffentliche Seiten danach geprüft: in Ordnung).
**Die drei Änderungen vom 16.11. (60de7d4, 5b1b071, d58fd5c – siehe « Erledigt am 16.11. ») sind gepusht, aber noch NICHT
deployt** → der Nutzer deployt eNOTE und SKOULOU noch einmal (Visual Studio). Danach zu prüfen: Registrierung verlangt
« Téléphone »; Knöpfe zeigen sofort den Ladekreis; eNOTE « Vidéos d'aide » öffnet das Video im Dialogfenster.
Keine neue Migration seit dem 14.11. → Azure-Datenbanken wurden nicht angefasst und sind aktuell:
eNOTE `ENote-Database` = lokal = `20261114233118_PreisAnpassungEntwarnung`; SKOULOU `Skoulou-Database` = lokal `SkoulouDB` =
`20261114182350_SessionGebuehrUndHauptAdmin`.

**Arbeitsweise seit 15.11. (Nutzer: « Immer nur mit dir. Eine Aufgabe nach der anderen »):** keine Agenten mehr starten –
alles selbst im Hauptchat, streng nacheinander, nach jedem fertigen Schritt committen + pushen, kurz auf Deutsch berichten.
(Das 5-Stunden-Limit wurde am 15.11. fünfmal erreicht; Agenten blieben mitten in der Arbeit stehen.)

### Erledigt am 16.11. (drei Wünsche des Nutzers, alle gepusht, noch zu deployen)
- **Telefonnummer Pflicht bei der Registrierung eines Besitzers** (60de7d4): eNOTE auf allen drei Wegen (öffentliche
  Registrierung, Dialog « Nouvel établissement » des Responsable, « Ajouter un propriétaire » des Administrateur;
  `TelefonRegel`: mindestens 8 Ziffern; Nummer in der Admin-Übersicht und in der Mail ans Team); SKOULOU bei der
  Registrierung einer Schule (Schüler bleiben frei). Keine Schemaänderung.
- **eNOTE « Vidéos d'aide » wie SKOULOU** (5b1b071): Video öffnet im Dialogfenster (Titel, Video, Beschreibung); die Seite
  bleibt beim Schließen ruhig.
- **Ladekreis sofort beim Tippen, beide Apps** (d58fd5c): Ursache des « man merkt nichts »: der Kreis kam erst mit der
  Antwort des Servers (bei 0,7 s Netz-Laufzeit 0,7 s nichts). Jetzt Kreis nach ~0,14 s im Browser für jeden Aktionsknopf
  (`enote-btn`/`btn-primary`, `sk-btn`; Kassen-Kacheln ausgenommen), per Attribut + CSS (`enoteKlickRing`, `skKlickRing`,
  Ausnahme `data-ohne-ladekreis`). eNOTE-Registrierung meldet fehlende Pflichtfelder jetzt direkt über dem Knopf.
- Tests: eNOTE 465 grün, SKOULOU 572 grün (+ SQL 591). Im Browser mit verzögertem Netz geprüft. Bilder
  `Pictures\Paket-2026-11-16\`. Kontrollinstanzen gestoppt, DB-Kopie gelöscht.

### Erledigt am 15.11.
- **eNOTE fertig** (S1 + S2 + W1–W3, 435 Tests grün).
- **Video « Pourquoi eNOTE ? » v4** (Nutzerwunsch, zwei Entwürfe, freigegeben « Video ist gut »): 5:34, Heft-Szene →
  Merkliste « À régler » → A Boutique / B Salon / C Cabinet (Kunden empfohlen, nie Pflicht) → Kategorien + Artikel →
  Kasse → Clôture « Écart 0 F » → « Tout est réglé ». In der App (59995f1): `enote-presentation.mp4`, VideoKatalog 5:34,
  Startseite « En cinq minutes ». Projekt `TabloSuite Videos\ENote Videos\projekte\lernen-v3\presentation-v4\`
  (README dort + `lernen-v3\README.md`), Bilder `Pictures\eNOTE-Pourquoi-v4\`. Aufnahme-DB-Kopie und Temp gelöscht.
- **S3 SKOULOU Daten-Simulation** (ff476bb, 0ec535b, 3540fad, dfba09e): 32 Schulen, 4 Jahre + laufendes, 15.052 Schüler,
  0 Befunde; Session-Ende in Listen/Eltern-Sicht/Archiv/Jahreswechsel/Vorjahreskopie behoben, Schulliste Admin 383 → 18 ms.
- **S4 SKOULOU Klick-Simulation + Ladekreis** (7e52c30, 825a1c6, 265d9a2, d9a517c, 1d27ef1, 55abdad, 35f1220, 3ac7a37):
  ~3.000 Klicks, Rundgang 227 Seiten aller Rollen, Monkey, Abläufe einer Wegwerf-Schule über eine Periode mit gleichzeitigen
  Geräten. Behoben: Ladekreis am Handy, Zeichenverlust bei langsamer Verbindung, « Page introuvable », französisches
  Fenster bei getrennter Verbindung, Handy-Überläufe/verdeckte Knöpfe, **Schutzgrenzen je Anschluss** (eine Klasse am
  Schul-WLAN sperrte sich aus: Anmeldung, erstes Passwort, Selbstregistrierung 10/Tag), Sperre nach Fehlversuchen fällt
  beim Zurücksetzen per Link, Gebührenkarte Sprachschule am Handy. Abschlusslauf sauber, keine Seite über 1 s.
- Berichte: `Skoulou/docs/Simulation-Bericht.md` (§ Simulation S3, § Klick-Simulation S4), `docs/Simulation-eNOTE.md`.
  Bilder `Pictures\Simulation-SKOULOU\`. Tests SKOULOU nach S4: 562 grün (+ SQL 581). `SkoulouDB_SIM` (lokal) behalten.

- **Abends nachgereicht (Nutzer 15.11.):** SKOULOU `Anschluss.Adresse` (e77befa); **eNOTE Schutz der öffentlichen Formulare**
  wie SKOULOU (9652024: Anmeldung 60/Minute je Anschluss, Registrierung 10 je Anschluss + 3 je Adresse am Tag, « Mot de
  passe oublié » 10 in 10 Minuten je Anschluss + 3 je Stunde je Konto, Sperre fällt beim Zurücksetzen); **« Vidéos d'aide »**
  in beiden Apps (a9aec4e, 630ad20: Seite sprang am Handy nach einem Video im Querformat nach oben – Position wird gemerkt;
  Nutzerbereiche als farbige Felder). Tests eNOTE 452, SKOULOU 571 (+ SQL 590). Bilder `Pictures\Videos-d-aide-2026-11-15\`.

### Offene Ideen (keine Fehler, Entscheidung des Nutzers)
- **SKOULOU-Lernvideos** (→ inzwischen die nächste Aufgabe, siehe oben) sind noch die alten mit der KI-Sprecherin (z. B. « La visite guidée » 2:03, « Suivre votre enfant »
  0:42) – nach den Regeln des Nutzers (ohne Sprecherin, jeder Klick beschriftet) auf der neuen Oberfläche neu aufnehmen.
- SKOULOU QR-Prüfseite eines Bulletins zeigt bewusst nur Schule, Schüler, Klasse, Periode, Moyenne – Nummer/Datum ergänzen?
- SKOULOU « Comptes élèves » rechnet je Seite alle Konten des Jahres (160–170 ms an der größten Schule) – beobachten.
- Werbevideo-Muster, das dem Nutzer gefiel (Problem → Versprechen → Beweis in der App → Rückbezug), für ein neues
  « Pourquoi SKOULOU ? » wiederverwenden, falls gewünscht.

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

Siehe §0: SKOULOU-Lernvideos neu (Pilot zuerst); vorher deployt der Nutzer die Änderungen vom 16.11. Zwischendurch dem Nutzer auf Deutsch berichten, was gerade läuft.
