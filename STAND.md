# TabloSuite – Arbeitsstand 20.11.2026 mittags (KI läuft mit Workspace-Header – Einstellung + Deploy beim Nutzer)

Diese Datei ist der Einstieg für ein neues Chat-Fenster. Erst lesen, dann weitermachen.
Die vollständige Planung für SKOULOU steht in `Skoulou/docs/Bauplan.md` (verbindlich) und `Skoulou/docs/fachkonzept/01…05`.

---

## 0. HIER WEITERMACHEN (neuer Chat) – Stand 20.11.2026 mittags

**>>> 20.11. NACHMITTAGS – Team-Einrichtung (erledigt, alles in master):**
- Partner **Sado** (`sado.tablosuite@outlook.de`): Azure **Owner** (Gast, angenommen); GitHub-Organisation **TabloSuite** angelegt, Repo dorthin
  verschoben und umbenannt → **`github.com/TabloSuite/TabloSuiteRepo`** (origin umgestellt), Sado = Owner (`SadoTablosuite`).
- **Branch-Regel:** Chris arbeitet nur in `Etse.TabloSuite.Muster`; `master` nur nach Zustimmung. Regeln für alle in **`CLAUDE.md`** (Repo-Wurzel).
- Neu: `scripts\Datenbanken-anlegen.cmd` (lokale DBs eNOTE + SKOULOU per Doppelklick), `docs\Claude-in-Visual-Studio-einrichten.md`.
  Sados persönliche Anleitung: `C:\ETSE\NETAPPS\TabloSuiteData\Infos\Sado-Einrichtung.md` (außerhalb des Repos).
- **Deploy erledigt:** eNOTE 10:04, SKOULOU 10:06 (Ortszeit, nach bf3caf5). Fehlerprotokolle seit Deploy: SKOULOU 0, eNOTE 1
  (12:02, kurzer Verbindungsabbruch zur DB beim Anmelden, vorübergehend, nicht KI). → Punkt 1 unten: nur noch der KI-Test des Nutzers
  (macht er später; Beispielfragen mit erwarteten Antworten stehen im Chat vom 20.11. nachmittags und unten in Punkt 1). Punkt 3 später.

**>>> 21.11. nachts – Empfehlungen vor dem Deploy umgesetzt (Nutzer: « vor Deploy alles nach deinen Empfehlungen »):**
- SKOULOU Datumsfeld Monat/Jahr wählbar (35095a4, am Handy geprüft); Frais « Accès réservé » für Koordinator ohne Finanzrecht (bcb6b4f).
- eNOTE: Laden zurück an denselben PO → seine Commission gilt wieder (08cd166); Sperr-Hinweis 5 Minuten (bcb6b4f).
- Beide: Registrierungsbremse je Anschluss 10 → 50/Tag (31390cb). « Au moins 6 caractères » doppelt war ein Testskript-Artefakt.
- TabloSuiteHome (2775e38): eNOTE-Preis live über neuen eNOTE-Endpunkt `/tarifs/public`, SKOULOU-Punkt « Emplois du temps ».
- Tests: eNOTE 566, SKOULOU 721, Home 9+2. **Deploy-Reihenfolge: eNOTE, SKOULOU, dann TabloSuiteHome.** Keine Migration.
- **Bewusst nach dem Deploy (Empfehlung):** Koeffizient-Korrektur über Rectification, Rectification nur geänderte Bulletins,
  Registrierung langsames Netz Eingaben merken, Schulstart mitten im Jahr, Mailversand über Warteschlange, Gerät sofort aussperren,
  F5 in der Kasse. **Bewusst nicht:** PO öffnet keine Reçus (Regel « PO nie in Kasse/Kundendaten »).

**>>> 20.11. abends – Klicktest « wie ein Mensch » beider Apps (vor dem Nacht-Test des Nutzers mit Sado):**
- Zwei Agenten (Worktrees, eigene Test-DBs `ENOTE_E2E_2011` / `SKOULOU_E2E_2011`, Berichte `%TEMP%\e2e-2011\<app>\BERICHT.md`).
  Regeln seit heute: nur klicken, nach jedem Schritt sichtbaren Inhalt prüfen (Lehre aus Fatal 2), Normaltest + Fehlersuche.
- **SKOULOU** (534 Schritte, ganzes Schuljahr bis Jahresbilanz + Semester-Schule): Bulletin-Zahlen alle richtig (Bildschirm + PDF).
  Behoben: Koeffizient nach Veröffentlichung fest (107c08f), Gruppen-Bilan nur bewertete Fächer + « Rang : Non classé » (936a28c),
  Fehlerjournal ohne Abbrüche verlassener Verbindungen (cf4779d, 76f2f4b).
- **eNOTE** (285 Schritte Endlauf): behoben Admin-Zuweisung an Responsable (Liste leer, Regression aus 9c9a229; 34ab14e),
  Registrierung lange Eingaben/englische Meldungen (91fd0bd), überlange Mitarbeiternamen blockierten Kasse (b88d1b4),
  Preis « 1.500 » = 1,50 F (0ca14fe; dazu Tarife/Commission 2aab421), langsames Netz « Connexion en cours… » (7e7582e).
  Produktion lesend: keine Namen > 60, keine Preise mit Nachkomma.
- Tests: eNOTE 565, SKOULOU 718, Home 9 grün. **Alles nur lokal committet (Nutzer: « pushen wenn ich wieder da bin »).**
- **Offene Fragen an den Nutzer:** SKOULOU Datumsfeld nur Monatsblättern (Geburtsdatum = 152 Klicks) → Jahr/Monat-Wahl?;
  Koeffizient ganzes Jahr fest oder in Rectification änderbar?; Registrierung bei Serverablehnung am langsamen Netz Eingaben merken
  (nach Deploy); Rectification nur geänderte Bulletins?; Schulstart mitten im Jahr. eNOTE: F1 Commission geht bei erneuter Zuweisung
  an früheren PO verloren (übernehmen?); Mailversand über Warteschlange statt im Klick?; « Déconnecter » Gerät sofort aussperren?;
  Registrierungsbremse 10/Tag je Anschluss (Mobilfunk-NAT)?; PO darf Reçus öffnen?; Ticket nach F5 leer.

**>>> 20.11. abends – KI-Hilfe: Admin und PO dürfen alles fragen (gebaut, Deploy beider Apps nötig):**
- Nutzer: « nur Admin und PO benutzen die KI zunächst, aber sie dürfen alles fragen »; « die KI bei POs soll nichts von Admin-Menüs
  kennen »; « Besitzer soll später, wenn die KI frei ist, auch nichts von Admin/PO wissen » – Prinzip **nach unten**.
- Umgesetzt (nur Wissen der KI, Menüs/Rechte in der App unverändert): **Administrateur** kennt Menü + Hinweise/Guide **aller** Rollen,
  **Responsable/PO** alle **außer dem Administrateur**; eigene Rolle zuerst, Regel 3 « dis pour quel rôle vaut ta réponse ».
  Laden-/Schulrollen unverändert (nur das eigene). Seite « Poser une question »: Untertitel + Beispielfrage für Admin/PO.
  Code: `KiWissensbasis` (`AlleRollenFuerPlattform`, `PlattformReihenfolge`, `Regel3Plattform`) in beiden Apps; Tests grün
  (eNOTE 545, SKOULOU 708, Home 9).
- **Später bei Freigabe für alle:** gleiches Prinzip nach unten (z. B. Propriétaire + Employé; Direction + Coordinateur, Enseignant,
  Élève, Parent) – vorher mit dem Nutzer festlegen. Statt `Ki__AlleRollen` ein **Schalter im Admin-Bereich** (Nutzerwunsch,
  braucht Migration: SKOULOU Spalte in `PlattformEinstellung`, eNOTE neue Einstellung/Tabelle).

**>>> ZUERST – beim Nutzer offen (Reihenfolge):**
1. **KI-Hilfe:** ~~`Ki__WorkspaceId` setzen~~ erledigt (Nutzer 20.11., in beiden Apps geprüft: mit Schlüssel + Workspace HTTP 200).
   Offen: **beide Apps deployen** (Code bf3caf5 schickt dann den Header
   `anthropic-workspace-id`). Danach prüfen: `%TEMP%\ki-pruefen\ki-pruefen.ps1 -App enote|skoulou` (liest `Ki__WorkspaceId` mit) und
   Nutzer testet als Admin Aide › « Poser une question » (Testfragen siehe unten « MORGEN (20.11.) ZUERST », Punkt 1). Danach das Prüfskript
   `%TEMP%\ki-pruefen` in den Papierkorb (die übrigen Proben sind schon weg).
2. ~~**Nach dem Deploy am iPhone bestätigen:**~~ **erledigt – Nutzer 20.11.: « War OK ».** Menü « Plus » – Seite dahinter rollt nicht mehr / lädt nicht neu (beide Apps); eNOTE-Reçu
   mittig; neues Video unter Vidéos d'aide › Administrateur. Weiter offen von gestern: iPhone-Login (Ladekreis), Registrierung,
   erneute Zuweisung an früheren Responsable, Mail an neuen Responsable.
3. **Gerätekauf:** Scanner NETUM NT-1228BC ist **1D/CCD – liest keine QR-Codes** → 2D-Modell empfohlen; Bondrucker Bisofice 80 mm
   USB+LAN passt nur am Windows-PC (vom iPhone kein Druck, kein AirPrint). Etiketten: A4 70 × 37 mm, 24 je Bogen.

**>>> ERLEDIGT 20.11. (alles committet und gepusht bis c427071; zwei Chat-Sitzungen arbeiteten parallel, alles zusammengeführt):**
- **KI-Fehler « momentanément indisponible » – drei Ursachen nacheinander:** (a) erster Wert war gar kein Schlüssel (31 Zeichen) → 401;
  (b) dann Service-Schlüssel `sk-ant-svc…`, (c) dann Benutzer-Schlüssel `sk-ant-usr…` – beide **ohne Workspace** → 400 « not scoped to
  a workspace ». **Gelöst per Code (bf3caf5):** optionale Einstellung `Ki__WorkspaceId` → Header `anthropic-workspace-id`
  (`ClientOptions.ExtraHeaders`, beide Apps). Mit dem Schlüssel aus Azure + Workspace `wrkspc_01JfujuGvN9AzxanMQNRh3uS` direkt geprüft:
  **HTTP 200, Haiku 5.5 antwortet.** Konfigurationsfehler (401/403/404/400) werden jetzt mit Ausnahme geloggt → Journaux/Fehlerjournal
  (f1cccf0). Doku: `eNOTE/Deployment-Azure.md`, `Skoulou/docs/Bauplan.md` §2.1.
- **Menü-Blatt « Plus » am Handy** (f1cccf0, beide Apps): Seite dahinter rollte mit, Ziehen nach unten lud die Seite neu (iPhone,
  Google-App) → `html:has(…backdrop)` overflow hidden + overscroll-behavior none, Wisch-Sperre in `enote.js`/`skoulou.js` (touchmove
  nur blockiert, solange Blatt/Fenster/Video offen). Playwright-Touch-Test (CDP) grün; am iPhone noch bestätigen.
- **eNOTE Kaltstart-Absturz** (73c9c6b, andere Sitzung): JSException « enoteRegisterUserMenu is not a function » beendete die
  Blazor-Verbindung → MainLayout fängt JS-Fehler ab.
- **eNOTE Étiquettes** (dfc5807, Nutzerentscheidung): A4-Bogen exakt **70 × 37 mm, 3 × 8 = 24** (vorher eigenes Raster, passte auf keinen
  Bogen), ohne Rahmenlinien, Dialog nennt Bogen + « Taille réelle (100 %) ». Reçu am Bildschirm mittig als Papierstreifen (421d33a,
  Druck unverändert). « le chiffre de contrôle » (statt « la »). KI-Wissensbasis « Bon à savoir » kennt die Hardware-Empfehlung.
- **Neues Lernvideo « Scanner et imprimantes : quel matériel, et pourquoi »** (c427071, 7:20, Administrateur + Responsable, Nutzer:
  « sehr ausführlich ») – erstes gemischtes Video (Folien + Aufnahmen), Projekt `lernen-v3/admin-materiel`, Notizen README lernen-v3
  « 20.11.2026 ». Inhalt = `eNOTE/Bon-Drucken.md` « Empfehlung für die Läden ». Pipeline-Fallen behoben: Video-Ordner-Pfad in
  `lernen3-film.mjs`/`render.sh` (seit 19.11. unter TabloSuiteData); Folien-Videos mit `python bau.py` (ohne `-I`) bauen.
  Aufnahme-App gestoppt; lokale DB zurückgesetzt (Prüfzeile: Code/Bestand wie vorher, keine Kasse offen).
- Tests: eNOTE 544, SKOULOU 707 grün. Keine Migration.

**>>> MORGEN (20.11.) ZUERST – Reihenfolge:**
1. **KI-Hilfe « Poser une question »**: Nutzer nach seinem Test als Admin fragen (beide Apps, Aide › « Poser une question »; nur Admin +
   Responsable/PO sichtbar, `Ki__AlleRollen=false`). Testfragen aus dem Chat: SKOULOU Wahlfach anlegen/Schüler wählen, Note nach der
   Veröffentlichung korrigieren, Preis für 300 Schüler (500 F/Monat, ≈ 5.000 F/an); eNOTE Mobile Money kassieren, Kasse auf anderem Gerät
   übernehmen, Mitarbeiter ohne Passwort; Frage außerhalb des Themas. Danach Fehlerprotokolle lesen (`%TEMP%\enote-lesen\lesen.ps1`,
   Spalten eNOTE `ExceptionLogs`: ZeitUtc/Nachricht/Ausnahme/Pfad; SKOULOU `FehlerProtokolle`: ZeitUtc/Stufe/Typ/Nachricht/Details/Pfad) und
   bei falschen/unklaren Antworten die Wissensbasis (`Services/KiHilfe` je App) nachziehen. Ich kenne das Admin-Passwort nicht → Nutzer testet.
   Erinnern: Ausgabelimit in der Anthropic Console (App hat keine Fragen-Grenze). `Ki__AlleRollen=true` erst nach Rücksprache.
2. **Beim Nutzer offen (am Gerät):** iPhone-Login (Ladekreis « Se connecter »), eine Registrierung, eNOTE erneute Zuweisung an einen früheren
   Responsable (Fatal 1 vom Testabend), Mail an neuen Responsable.
3. Später / nur gemeinsam: `WEBSITE_RUN_FROM_PACKAGE=1` gegen Deploy-Fehler (vorher prüfen, ob die Apps in ihr Verzeichnis schreiben);
   Azure-Upgrades vor Produktion (SQL Basic, B1, Speech S0, Blob LRS – Memory « vor-produktion »); Vorschläge aus dem Funktionsvergleich
   eNOTE↔SKOULOU (siehe « ERLEDIGT 19.11. », Nutzerpunkt 5).

**Stand Git/Azure (19.11. ~21:45 UTC):** alles gepusht (bis 3906333 + dieser STAND). Alle drei Apps deployt, Azure-DBs = lokal (keine offene
Migration). Video-DB `SkoulouDB_VIDEO`: Basis mit `Wahlfaecher` gesichert. Aufnahme-App gestoppt, Temp-Dateien im Papierkorb.


**>>> (19.11. ~21:40 UTC) SKOULOU deployt + KI-Schlüssel gesetzt (Nutzer):** `Ki__AnthropicApiKey` und `Ki__AlleRollen=false` in eNOTE + SKOULOU.
- Live geprüft (lesend): SKOULOU-Startseite 200 (erster Aufruf nach dem Neustart > 90 s Kaltstart), Vorschaubild `skoulou-direction-facultatives.jpg`
  200 und byte-gleich, Video ohne Anmeldung 401, `/aide/question` beider Apps → Login. Fehlerprotokolle seit 16:52 UTC: je 2 Einträge
  21:29–21:31 UTC (SQL-Zeitüberschreitung in Hintergrunddiensten: eNOTE Mail-Dienst, SKOULOU automatische Abgabe) genau beim Neustart
  durch die neuen Einstellungen – danach keine weiteren; nicht gelöscht.
- **KI noch nicht mit echtem Konto angesehen** (Admin-Passwort kenne ich nicht): Nutzer testet als Admin (Testfragen im Chat 19.11.), danach
  Fehlerprotokolle lesen.

**>>> (19.11. nachts) – Videos « sehr informationsreich » erledigt, gepusht (3b9b840, efbcfe2), SKOULOU inzwischen deployt:**
- Neu **« Les matières facultatives »** (Direction, 5:37, `skoulou-direction-facultatives.mp4`, im Katalog nach « Publier les bulletins »):
  2 Folien (Prinzip, drei Regeln), Ewe facultative (Standardregel), Culture « Ne compte pas », Wahl je Schüler, Noten nur Wählende,
  Bulletin Mawuli 16,00 → 16,92 (+12), Dela +0, Abla ohne Block, Sperre nach der Veröffentlichung.
- **« Publier les bulletins »** 5:07: Modell mit « Groupes de matières » (Titel + Bilan); **« Préparer l'année »** 6:20: Schalter
  « Matière facultative » unter jedem Fach. Pipeline-Notizen: `lernen-v3/AUFTRAG.md` §12. Video-DB: Basis mit Migration `Wahlfaecher` erneuert.
- App-Kleinigkeiten (gleicher Commit, keine Migration): Fächer-Dialog « Total … coefficients » ohne Wahlfächer; nach der Veröffentlichung
  kein gesperrter Schalter mehr unter Pflichtfächern; « Tous / Aucun » nur bei ungesperrten Schülern. Tests SKOULOU 707 grün.
- ~~Beim Nutzer: SKOULOU deployen~~ erledigt (siehe oben). eNOTE/Home unverändert. Offen wie unten: iPhone-Login, KI-Schlüssel je App
  (Code ist live, ohne `Ki__AnthropicApiKey` unsichtbar – Nutzer fragte am 19.11. danach).
- « Équipe et emplois du temps » (4:40): Szene `b2-classe` neu mit dem Schalter « Matière facultative » (Nutzer « mache das »).


**>>> ALLES DEPLOYT UND LIVE GEPRÜFT (19.11., ~16:50 UTC) – nichts offen außer den Videos:**
- Nutzer hat **eNOTE, SKOULOU und TabloSuiteHome deployt**. Azure-DBs beider Apps = lokal (alle Migrationen bis
  `KundenAktenRecht` bzw. `Wahlfaecher`). Git: alles gepusht.
- Live geprüft (nur lesend): alle drei Startseiten 200; `skoulou…/tarifs/public` → `{"monatspreisProSchueler":500,"waehrung":"FCFA"}`;
  SKOULOU-Startseite « Tarifs » 500 F/mois + « ≈ 5.000 FCFA » (auch JSON-LD, unitCode MON); tablosuite.com-Karte « 500 FCFA / élève /
  mois » + « environ 5.000 FCFA » (beim allerersten Aufruf nach dem Kaltstart nur Hinweis – Fehlschlag 2 min gemerkt, dann korrekt);
  eNOTE 2.500 F; Anmelde-/Registrierungsseiten beider Apps 200 (eNOTE `/auth/register` mit Knopf-Sperre); eNOTE « Page introuvable »
  bei 404; `/aide/question` ohne Anmeldung → Login (beide Apps).
- Fehlerprotokolle nach dem Deploy: **SKOULOU 0**. **eNOTE 10**: 9× `BadImageFormatException` 16:33:13–17 UTC (Anfragen genau
  während des Datei-Austauschs beim Zip-Deploy, danach nie wieder – kein App-Fehler) + 1× SQL-Anmeldefehler 35 um 16:43 beim
  Aufwachen der DB (Wiederholung aus fb24965 griff, Seite kam mit 200). Bis 16:51 UTC keine weiteren. Einträge NICHT gelöscht.
  Idee für später (nur mit Nutzer, Azure-Einstellung): `WEBSITE_RUN_FROM_PACKAGE=1` verhindert die Deploy-Fehler (vorher prüfen,
  ob die App in ihr Verzeichnis schreibt).
- **Beim Nutzer offen (am Gerät):** iPhone-Login (Ladekreis « Se connecter »), eine Registrierung, eNOTE erneute Zuweisung an einen
  früheren Responsable (Fatal 1 vom Testabend), Mail an neuen Responsable. KI-Hilfe: erst nach Schlüssel (`Ki__AnthropicApiKey`
  je App) – dann Seite ansehen + echte Fragen testen (nur Admin/PO sichtbar).
- **NÄCHSTER SCHRITT: Videos (Nutzer: « später », « sehr informationsreich »)** – siehe Punkt unten « Danach: Videos ».

**>>> (19.11. spätabends) Verlauf der Sitzung:**
- ✅ Beide Agenten-Arbeiten übernommen, Worktrees entfernt, alles gepusht (bis 5f3ebca). Tests: eNOTE 540, SKOULOU 704
  (mit `SKOULOU_SQL_TESTS=1`: 725).
- ✅ **Azure eNOTE eingespielt** (OK « azuredb ok »): ProduktOwnerSalonVerlauf + KundenAktenRecht, lesend geprüft → Azure = lokal.
- ✅ **KI-Hilfe auf Nutzerwunsch: keine Fragen-Grenzen, zunächst nur Admin + Responsable/PO** (1d8cd16); `Ki__AlleRollen=true`
  schaltet später alle frei. Kosten begrenzt das Ausgabelimit des Schlüssels in der Anthropic Console.
- ✅ **Matières facultatives übernommen** (54b56f1 … 5f3ebca), Migration `20261119151242_Wahlfaecher` lokal SkoulouDB eingespielt
  (SkoulouDB_VIDEO noch nicht – vor der nächsten Videoaufnahme mitziehen + Basis-Sicherung erneuern, AUFTRAG.md §9).
- ✅ **Wahlfach-Regeln 3–5 umgesetzt** (797495e, Nutzer « ja für alle »): Hinweis auf vorhandene Noten ohne Wahl; Sperre nach der
  ersten Veröffentlichung (Schalter/Regel fest, Wahl nur in Rectification, Neuankömmlinge frei); Jahreswechsel übernimmt die Wahl.
  Punkte 1+2 bleiben wie gebaut (Kopfzeilen bei « Groupes de matières », Schalter auch bei Sprachschulen). Keine neue Migration.
  Tests 707 / SQL 728. Oberfläche nur über Dienst-Tests geprüft, nicht im Browser angesehen.
- ✅ **Azure SKOULOU eingespielt** (19.11. spätabends, direkt vor dem Deploy, Nutzer « ja jetzt deploy »): PreisJeSchuelerUndMonat +
  Wahlfaecher, lesend geprüft (500 F, CGU 2026-11-19, 4 Spalten, Fachwahlen mit eindeutigem Index) → **Azure = lokal (beide Apps)**.
  **Nutzer deployt jetzt eNOTE, SKOULOU, TabloSuiteHome.** Bis zum SKOULOU-Deploy zeigen Preis-Seiten/Registrierung auf Azure Fehler.
  Nach dem Deploy prüfen: skoulou.tablosuite.com Abschnitt « Tarifs » (500 F/mois), tablosuite.com-Karte, `/tarifs/public`,
  iPhone-Login (Ladekreis), Registrierung, Zuweisung an früheren Responsable (eNOTE), KI-Menü unsichtbar (noch kein Schlüssel).
- **Danach: Videos sehr informationsreich** (Nutzer 19.11.): neu « Les matières facultatives » (Direction), Szenen in
  « Préparer l'année » (Matières et enseignants) und « Publier les bulletins » (Vorlage « Groupes de matières »). Vorher
  SkoulouDB_VIDEO migrieren + Basis-Sicherung erneuern.
- SKOULOU-Azure-Skript wurde vorher auf einer Wegwerf-DB zweimal geprobt (eingespielt + Skript gelöscht). Werkzeug:
  `azure-anwenden.ps1 -Skript … -Datenbank Skoulou-Database` (XACT_ABORT + Abbruch beim ersten Fehler; `-Db` geht nicht, Konflikt mit
  `-Debug`). Falle: `sqlcmd` nur mit `-I` (QUOTED_IDENTIFIER), sonst scheitert das UPDATE auf Schuljahre und die Migration wird
  trotzdem eingetragen. Spalten-Umbenennungen immer **unmittelbar vor dem Deploy** einspielen.
- Wahlfach-Fragen 1–5 entschieden (Nutzer « ja für alle », siehe Punkt « Wahlfach-Regeln 3–5 » oben).

**>>> STAND 19.11. spätabends (neue Sitzung, Anfang):**
- Beide Agenten-Arbeitskopien waren **unfertig und uncommittet** (Wahlfächer: Core/Daten/Dienste fertig, Oberfläche/Bulletin/
  Migration/Tests fehlten; KI-Hilfe: nur SKOULOU-Dienste). Je ein neuer Agent baut in **derselben** Arbeitskopie fertig und committet
  dort im Branch. Danach: prüfen, bauen, Tests, cherry-pick, Worktree entfernen (siehe Punkt 1 unten).
- Push war schon erledigt. Lokal eingespielt: eNOTE `KundenAktenRecht` (ENOTEDB), SKOULOU `PreisJeSchuelerUndMonat` (SkoulouDB: 500 F,
  CGU 2026-11-19). Azure lesend: eNOTE auf `FormationResponsables`, SKOULOU auf `SchuleEssaiPerioden` (3.600 F, 0 Schuljahre).
- Azure-Skript eNOTE fertig: `%TEMP%\enote-scripts\azure-enote-19-11.sql` (idempotent: ProduktOwnerSalonVerlauf + KundenAktenRecht;
  nur Index-Tausch + neue Spalte) – **wartet auf OK**. SKOULOU-Skript erst nach der Wahlfach-Migration (Preis + Wahlfächer zusammen).
- 8db4570 SKOULOU Encaisser: Platzhalter « facultatif » bei Pflicht-Référence (Virement) behoben.
- ✅ **KI-Hilfe « Poser une question » übernommen** (7e7e894 … e1b0b1a + 715220e « Dossiers clients » in der eNOTE-Wissensbasis),
  Worktree entfernt, gepusht. Tests eNOTE 539, SKOULOU 684 grün. Seite `/aide/question` (beide Apps), ohne `Ki__AnthropicApiKey`
  unsichtbar (→ `/introuvable`). Grenzen: 20/Konto/Tag fest, App 300/Tag (`Ki__FragenJeTag`); Zähler nur im Speicher.
  Kosten laut Schätzung ≈ 0,03–0,2 US-Cent je Frage, schlimmstenfalls ≈ 0,60 $/Tag. Doku: `Skoulou/docs/Bauplan.md` §2.1,
  `eNOTE/Deployment-Azure.md`. **Noch nie mit echtem Schlüssel/Bildschirm geprüft** → nach dem Freischalten Sichtprüfung + echte Fragen.
  Offene Fragen an den Nutzer: eNOTE-Datenschutztext nennt « Aide & contact » statt Guide (eNOTE hat keinen Guide); 300/20 ok?;
  ein Schlüssel je App (Kostenübersicht)?

**>>> (19.11. abends, Sitzung endete) – Reihenfolge:**
1. **Zwei Agenten-Arbeitskopien prüfen und übernehmen** (liefen beim Sitzungsende noch; Ergebnis evtl. unvollständig/uncommittet):
   - `.claude/worktrees/agent-ae86b4675452b3aff` (Branch `worktree-agent-ae86b4675452b3aff`): **SKOULOU « Matières facultatives »**
     (Nutzerpunkt 3, freigegeben): Klassenfach « facultative » mit Regel (Standard « nur Punkte über 10 zählen », sonst normal/
     nur Info), Wahl je Schüler fürs ganze Jahr (Direktion, an der Klasse), Noten nur für Wählende, Bulletin: Bereich
     « MATIÈRES FACULTATIVES » nur wenn gewählt; Gruppen-Kopfzeilen (« MATIÈRES LITTÉRAIRES » …) NUR wenn Vorlage
     `ZeigeFachGruppen` an (Standard aus) – **ohne Bereiche muss weiter gehen (Sprachschulen, Nutzer)**. Migration nur erzeugt.
   - `.claude/worktrees/agent-af053356bc40b5189` (Branch `worktree-agent-af053356bc40b5189`): **KI-Hilfe « Poser une question »**
     (Nutzerpunkt 4, freigegeben: **Claude Haiku 5.5** `claude-haiku-5-5`) in eNOTE + SKOULOU: Wissensbasis aus Menü/Guide/
     Formation/Videos, nur Hilfetexte + Frage an Anthropic, 20 Fragen/Konto/Tag, ohne Schlüssel `Ki:AnthropicApiKey` unsichtbar.
     Nutzer braucht ein Anthropic-API-Konto + Schlüssel (Azure-Einstellung `Ki__AnthropicApiKey`); vor Freischalten mit echten
     Fragen testen. Kosten Haiku 5.5 ≈ < 1 F je Frage (siehe Chat 19.11.).
   - Je Branch: `git log`, Diff prüfen, bauen, Tests (eNOTE 525, SKOULOU 659), `git cherry-pick`, Worktree entfernen
     (`git worktree remove --force …`, `git branch -D …`). Unfertiges selbst zu Ende bauen.
2. **Push**: 2 Commits warteten beim Sitzungsende auf die GitHub-Anmeldung (f3570c0 u. a.) – `git status -sb`, ggf. `git push`.
3. **Migrationen** (lokal + Azure, Azure NUR mit ausdrücklichem OK des Nutzers): eNOTE `20261119101000_ProduktOwnerSalonVerlauf`
   (Index, lokal SCHON drauf; Skript `%TEMP%\enote-scripts\azure-ProduktOwnerSalonVerlauf.sql`), eNOTE `20261119131056_KundenAktenRecht`
   (neue Spalte, lokal noch NICHT), SKOULOU `20261119113708_PreisJeSchuelerUndMonat` (Spalten umbenennen + Werte ÷ 10/3.600→500,
   CGU-Version; lokal SkoulouDB noch NICHT, SkoulouDB_VIDEO schon), dazu die Wahlfach-Migration aus Punkt 1. Idempotente Skripte mit
   `dotnet ef migrations script <von> <bis> --idempotent`; Azure-Stand vorher lesen (`%TEMP%\enote-lesen\lesen.ps1`).
4. **Dann deployt der Nutzer alle drei Apps** (eNOTE, SKOULOU, TabloSuiteHome – Preis-Karte braucht SKOULOU + Home zusammen).
   Danach: Login am iPhone (Ladekreis), Registrierung, Zuweisung an früheren Responsable, Mail an neuen Responsable prüfen.

**>>> ERLEDIGT 19.11. (nachmittags/abends), alles committet:**
- Punkt 15 SKOULOU **500 F je Schüler und Monat** (0c76619, Memory « skoulou-preis-monat »), Formation-Bilder (ea3505b),
  **7 Videos neu** (68c85cf; Pipeline-Notizen AUFTRAG.md §11: Videos-Ordner jetzt unter `TabloSuiteData`, Aufnahmen aus
  `%TEMP%\skoulou-shot-tool` starten, Video-Basis-DB neu auf 500).
- Punkt 14 (Drucker/Scanner/Papier): Kaufberatung im Chat; Video erst mit echten Geräten. eNOTE-Bon = 80 mm, Etikett 62×29 mm
  (Brother QL) oder A4-Raster (noch an keinen Standardbogen angepasst – Format vom Nutzer erfragen), Scanner HID + Enter, 2D, AZERTY.
- Nutzerpunkt 1 Fehlerprotokolle: Fehler 35 (Azure-SQL-Anmeldung) wird jetzt wiederholt (fb24965); Protokolle gelöscht
  (Azure eNOTE 27, SKOULOU 24, lokal 19 + 25). Lösch-Werkzeug `%TEMP%\enote-lesen\protokolle-loeschen.ps1` (nur DELETE auf Protokolle).
- Nutzerpunkt 2 eNOTE-Recht **« Dossiers clients »** (f3570c0): angestellter Arzt mit « Connexion personnelle » sieht/bearbeitet
  Kundenkarten + Notizen, nie Kasse/Löschen/PIN.
- Nutzerpunkt 5: eNOTE-Fehlerseite « /Error » + « Page introuvable » (1d7cdab), toter Dienst VertragsAktualisierung entfernt.
  Funktionsvergleich eNOTE↔SKOULOU im Chat; Vorschläge für ein späteres Paket: eNOTE Sicherheits-Header/Rate-Limits/Honeypot,
  Neuprüfung offener Verbindungen, Audit, Startpasswort-Pflichtwechsel + Kontenliste; SKOULOU Mail-Schalter/Historique,
  Wartungsannonce, Kündigung/Löschung mit Bedenkzeit.
- Nutzerpunkt 6: HyperFrames-Abo kann gekündigt werden (lokales Rendern ist Open Source, Apache-2.0; Avatare nutzen wir nicht).

**>>> NEUE NUTZERPUNKTE (19.11. nachmittags) – VOR Migration/Deploy erledigen (Nutzer: « merken für später vor Migrations und Deployen »):**
1. Fehlerprotokolle in den DBs lesen (eNOTE `ExceptionLogs`, SKOULOU `FehlerProtokolle`; Azure + lokal), Ursachen beheben, danach aus der DB löschen.
2. eNOTE für Arztpraxen: Kann ein Mitarbeiter/Arzt im Büro eNOTE öffnen und Patienten-Notizen bearbeiten, während die Kasse läuft?
3. SKOULOU-Bulletin: Bereich « Matières facultatives » fehlt (Togo: Culture, Ewe … nach Wahl des Schülers) – Umsetzung vorschlagen.
4. KI nur als Hilfe, um sich schnell in der App zurechtzufinden – möglich? Kosten?
5. Was ist in SKOULOU umgesetzt und in eNOTE nicht – und umgekehrt?
6. HyperFrames-Abo kündigen? Können wir ohne weiter wie jetzt Videos machen?
Stand: Punkt 15 (Monatspreis) Code fertig (0c76619, ea3505b), 7 Videos neu aufgenommen, Rendern läuft; danach einbauen.

**>>> ZUERST (19.11.) – Testabend-Ergebnisse 1–13 umgesetzt, alles gepusht (86193a6 … 9bd72e5), NOCH NICHT DEPLOYT:**
- **Offen beim Nutzer:** (1) OK für Azure-Migration eNOTE `20261119101000_ProduktOwnerSalonVerlauf` (nur Index neu, Skript
  `%TEMP%\enote-scripts\azure-ProduktOwnerSalonVerlauf.sql`, idempotent, mit `azure-anwenden.ps1`); lokal ENOTEDB schon drauf.
  Reihenfolge egal (neuer Code ohne Migration: Meldung statt Absturz), voll behoben erst mit Migration. (2) Dann eNOTE + SKOULOU
  deployen (eNOTE enthält auch den Deadlock-Fix 8ba0e5b). (3) Mail-Art « Bienvenue » ist in Administration › E-mails
  standardmäßig AUS (Postmark-Kontingent, seit 10.11.) – darum bekam der selbst registrierte Besitzer « Salon B2 » keine
  Willkommensmail; einschalten = Nutzerentscheidung. (4) Punkte 14 (Video Drucker/Scanner/Papier) und 15 (SKOULOU-Gebühren
  pro Abschnitt statt Jahrespreis) besprechen – Vorschlag im Chat 19.11.
- Lesewerkzeug Azure (nur SELECT): `%TEMP%\enote-lesen\lesen.ps1 -Sql "SELECT …" [-Db ENote-Database]` (az-Token). Fehler stehen
  in `ExceptionLogs`, Mails in `MailNachrichten`.
- **Fatal 1** (Zuweisung an Poa blockierte die App, Azure-Protokoll 14× 19:02–19:23 UTC): eindeutiger Index (PO, Laden) ohne
  Filter → erneute Zuweisung an einen früheren PO = duplicate key → Blazor-Verbindung tot. Fix 9c9a229: Index nur über aktive
  Zeilen (Migration), « Décocher » archiviert, Hinweis beim PO-Wechsel, **FehlerGrenze** um den Seiteninhalt (MainLayout),
  SQL-Test `PoZuweisungSqlTests` (ENOTE_SIM=1).
- **Fatal 2** (Fehlerseite nach « S'inscrire »): Weiterleitung zur statischen Anmeldeseite ohne forceLoad → « Not found ».
  Dazu verlor die Registrierung bei schwachem Netz alle Eingaben. Fix 2dc89b6: Registrierung = statisches Formular
  ([ExcludeFromInteractiveRouting], native Länderauswahl, CGU als <details>, Knopf-Sperre per enote.js `data-pflicht-sperre`).
  Mit Proxy 700 ms geprüft (sofort/später tippen, Fehlerfall behält Eingaben).
- **Punkt 13** (Ladekreis « Se connecter » nur am Handy fehlt): beide Apps senden klassische Formulare jetzt zweistufig
  (erst zeichnen, dann requestSubmit) – Safari/iPhone zeichnete nach dem Absenden nichts mehr. In Chromium geprüft; **am iPhone
  vom Nutzer nach dem Deploy bestätigen lassen** (kein WebKit lokal).
- Punkt 2 (Sterne + gesperrte Knöpfe): eNOTE d8d770d + eigene Dateien (Admin, Administrateurs, Mes établissements, E-mails,
  Registrierung), SKOULOU 611e1aa (53 Dateien, `Sofort`-Modus für ZahlFeld/BetragFeld, Wächter-Tests). CSS `.enote-pflicht`
  (Text im Label in `<span class="enote-pflicht">` fassen). Offene Kleinigkeiten aus dem SKOULOU-Bericht: Encaisser/Virement
  Platzhalter « facultatif » obwohl Pflicht; Uhrzeitfelder Stundenplan nicht mit Proxy geprüft.
- Punkt 1: Reiter « Administrateurs » für alle Admins (nur Hauptadmin ändert); « + Ajouter un responsable plateforme ».
  Punkt 3: Mail « compte-responsable » (immer aktiv) + Zugangsmail für vom PO angelegte Besitzer. Punkte 4/5: Reiter
  « Propriétaires plateforme » nur Admin, Startkarte « Mes établissements », Knopf « Valider l'établissement » dort (4232ea2).
  Punkt 8: Mail-Vorschauen ohne klickbare Links. Punkt 12: Zahl-Plaketten der Kasse unter 1200 px sichtbar (9bd72e5).
- Punkte 6/7/9 (nicht nachstellbar): Mails sind 4–5 KB ohne Bilder, mit Textfassung – Anzeige hängt an der Mail-App.
  Cursor: automatische Prüfung hell + dunkel aller Eingabefelder beider Apps ohne Befund; verschwindender Mauszeiger beim
  Tippen = Windows « Zeiger während der Eingabe ausblenden » bzw. weißes Zeigerschema. Sperr-Knopf: im Block « En attente »
  gibt es nur Valider/Refuser, PO sieht « Demande » statt Schloss.


**>>> ZUERST (18.11. abends):**
- **Alle drei Apps deployt (18.11. nachmittags) und live geprüft:** Titel/og:image/JSON-LD/robots/sitemap 200 auf tablosuite.com, eNOTE
  (Preis 2.500), SKOULOU (Preis 3.600, Abschnitt « Tarifs », `/tarifs/public`); tablosuite.com-Karte zeigt 3.600 FCFA live; eNOTE-Fixes
  aus dem Klicktest online (CSS « Gérer les catégories », Hinweis « Mot de passe oublié »). Kein Azure-DB-Eingriff nötig.
- **Search Console:** Domain-Property `tablosuite.com` bestätigt (TXT bei IONOS), 3 Sitemaps eingereicht und « Erfolgreich » gelesen
  (SKOULOU 5, eNOTE 4, tablosuite.com 1 Seiten). Optional: « URL-Prüfung » → « Indexierung beantragen » für die drei Startseiten.
- **Nutzer testet heute Abend eNOTE mit 4–6 Personen** (Liste `C:\ETSE\NETAPPS\Test TabloSuite\Test Daten\Daten Nutzer und Leistungen.txt`).
  Im neuen Chat zuerst nach Auffälligkeiten fragen (Uhrzeit, Gerät, Bildschirmfoto).
- **Deadlock-Ursache gefunden + behoben (8ba0e5b, NOCH NICHT DEPLOYT, Push hing an der GitHub-Anmeldung):** SQL Server behält die
  Isolationsstufe gepoolter Verbindungen – nach `VertragsService.SynchronisiereAsync` (Serializable) liefen spätere Abfragen (auch Verkäufe)
  Serializable → Bereichssperren auf PK_Leistungen, 10–22 Deadlocks je 18-Monats-Simulation (automatisch wiederholt, keine Datenfehler).
  Fix: EF-Interceptor `Data/IsolationsRuecksetzer.cs` (setzt nach strengen Transaktionen READ COMMITTED), SQL-Test
  `IsolationsRuecksetzerSqlTests` (ENOTE_SIM=1). Kontroll-Simulation lief am 18.11. abends; Deadlock-Aufzeichnung = XE-Sitzung
  `enote_deadlocks` (danach wieder entfernen: `DROP EVENT SESSION enote_deadlocks ON SERVER`). Mit dem nächsten eNOTE-Deploy ausliefern.
- Mitarbeitende: E-Mail freiwillig (Nutzer: « so lassen »); eigener Login = keine Kasse, am Gerät des Besitzers kassieren sie mit Name + PIN.

**>>> (18.11. nachmittags) – Klicktest eNOTE vor dem Testabend des Nutzers (4–6 Personen), gepusht und deployt:**
- Testliste für den Abend: `C:\ETSE\NETAPPS\Test TabloSuite\Test Daten\Daten Nutzer und Leistungen.txt` (3 Läden: A Salon Lomé, B Barbershop
  Kara, C Boutique Cotonou; Responsable; Ablauf in 14 Schritten). Nutzer: nur Liste, Azure bleibt unberührt.
- Klicktest (Playwright, `%TEMP%\skoulou-shot-tool\enote-e2e\abend.mjs` + `lib.mjs`; App `%TEMP%\enote-e2e\app-starten.ps1` Port 5297,
  DB `ENOTE_E2E_ABEND`, `db-neu.ps1`; Mail aus): Phasen 1 Registrierung/Freigabe, 2 Einrichtung, 3 Kasse (3 Geräte, Übernahme, Écran
  client, Clôture mit Écart, Erstattung), 4 Responsable + Abo über Monate + Sammelbestätigung, 5 Geräte/Passwort vergessen/Connexion personnelle.
  **Ergebnis nach den Fixes: 146/146 Schritte grün, 0 Befunde** (Phasen 1–4: 138, Phase 5: 8). Geprüft u. a.: Teilrechnung A 18.–30.11.
  = Monatspreis × 13/30; B mit Essai-Ende 15.08. → 4 Rechnungen; Sammelbestätigung; 5 Provisionen à 10 % (1.422 F); Clôture-Écart −500 F
  mit Hinweis; PC sperrt sich nach « Reprendre la caisse ici » am Handy; Ama (ohne E-Mail) mit « Connexion personnelle »: Login ok, keine
  Kasse – bleibt aber in « Qui encaisse ? » am Gerät des Besitzers (KassenService.GetKassiererAsync: alle mit Kassen-PIN).
  Testapp-Mails: SMTP absichtlich 127.0.0.1:1 → nur Warteschlange (0 gesendet).
- Offene Frage an den Nutzer: Hinweis auf « Mot de passe oublié » für Mitarbeitende (« demandez au propriétaire … Équipe › Droits »),
  weil Kassierer-Konten dort bewusst keinen Link bekommen und die Seite neutral « envoyé » meldet.
- **Gefundene und behobene App-Fehler:** (1) Kategorien am Handy/Tablet hochkant nicht anlegbar → « Gérer les catégories » (cb3cf94);
  (2) « Nouvel employé » verlangte Pseudonyme + E-Mail → freiwillig, Vorschlag « prenom.nom » (cb3cf94); (3) Registrierung zeigte bei leerem
  Formular nur eine Browser-Sprechblase (cb3cf94); (4) JS « exception invoking 'SchliesseVonAussen' » bei jedem Klick (cb3cf94);
  (5) neue Remises waren inaktiv und fehlten in der Kasse → sofort aktiv (944cd44). Tests eNOTE 522 grün.
- Simulation S1 (18 Monate, 39 Läden, 110.985 Verkäufe) ohne echte App-Fehler; veraltete Teile an die Regeln vom 17.11. angepasst (944cd44).
- SKOULOU: 7 Videos mit Preis-Hinweis neu eingebaut (db018cc); Pipeline-Fix bau.py (`python -I`), AUFTRAG.md §10.

**>>> (18.11. später) – drei Nutzerpunkte + öffentlicher Preis, gepusht, NOCH NICHT DEPLOYT (alle drei Apps!):**
- eNOTE-Kasse: Symbol `ticket-pause` (Bon + Pause) für « En attente » statt zwei Balken (« sah aus wie wartende Kunden »), 4d393f9.
  Kein neues Video (Nutzer). Bild `Pictures\eNOTE-Symbol-En-attente-2026-11-18\`.
- SKOULOU « Conditions d'utilisation » bei der ersten Anmeldung: lokal nicht nachvollziehbar (Registrierung Schule → Wartesseite →
  Freigabe → Startseite, nirgends ein Fenster; CGU-Version wird bei der Registrierung gespeichert). Nutzer: « vielleicht bereits
  entfernt ». Falls es wieder auftaucht: Bildschirmfoto erbitten.
- **SKOULOU-Preis öffentlich** (45b78b3): Startseite Abschnitt « Tarifs » (Jahrespreis aus der DB, Aufteilung 3/2/4 Perioden,
  fairer Hinweis), Menü « Tarifs »; Endpunkt `GET /tarifs/public`; tablosuite.com-Karte holt den Preis live (`TarifAbfrage`,
  3 s, Cache 30 min; ohne Antwort nur Hinweis), Registrierungslink jetzt `/auth/inscription`. Bilder `Pictures\SKOULOU-Tarifs-oeffentlich-2026-11-18\`.
- **Anzeige bei Google** (9068b3b): Startseiten aller drei Apps mit Suchtitel, Beschreibung, Canonical, Open Graph/Twitter-Karte
  (eigene Vorschaubilder `img/partage.jpg` 1200×630, `Pictures\Google-Anzeige-2026-11-18\`), JSON-LD (WebSite + SoftwareApplication
  mit aktuellem Preis; tablosuite.com Organization + WebSite), robots.txt + sitemap.xml (eNOTE, SKOULOU), Favicons 192 px.
  **Search Console: Domain-Property `tablosuite.com` per TXT bei IONOS bestätigt (18.11., Nutzer).** Noch offen nach dem Deploy: Sitemaps
  `https://tablosuite.com/sitemap.xml`, `https://enote.tablosuite.com/sitemap.xml`, `https://skoulou.tablosuite.com/sitemap.xml` einreichen.
- SKOULOU-Lernvideos mit den neuen Preis-Hinweisen: in Arbeit (preparer, fermer, passage, facture ganz; langues `l-annee`,
  admin-facturation `ad-tarifs`, acces-inscription `s-ecole`) – danach einbau.py, VideoKatalog-Dauern, commit.
- Tests: eNOTE 521, SKOULOU 652, TabloSuiteHome 8 grün. Keine Migration.

**>>> (18.11., Nutzer: « deine 3 Punkte ») – drei Aufräumpunkte erledigt, gepusht, NOCH NICHT DEPLOYT:**
- SKOULOU-Mails ohne Türkis (f7c8d2f): Streifen unter dem Kopf, Spruch « APPRENDRE. PROGRESSER. RÉUSSIR. » und Fußtext in Blau,
  Logo-Verlauf wie `skoulou-logo.svg`. Bild `Pictures\SKOULOU-Mail-Blau-2026-11-18\mail-blau.png`. Mail-Tests 18 grün.
- eNOTE « Appareils connectés » (48881b4): Zeiten in der Ortszeit des aktiven Ladens (`ImLaden(AktiverSalon.Zeitzone)`) statt `ToLocalTime()`.
- eNOTE ungenutzter Barzahlungs-Ablauf entfernt (cf6489b): `PoAbrechnung.razor`, `PoAbrechnungService`, `BesitzerBetreuungVerwaltung.razor`
  (+ deren Tests). `BesitzerBetreuungsService` bleibt als statische Ergänzung neuer Läden (Hintergrunddienst `VertragsAktualisierung`);
  `VertragsService`/`KontaktService` lesen `BesitzerBetreuungen` weiter. **Tabellen unverändert, keine Migration** (PoZahlungseingaenge,
  PoZahlungsanforderungen, PoGeldbewegungen bleiben; Löschen nur per Migration mit OK). Simulation S1 auf `PoProvisionService` umgestellt
  (kompiliert; der lange Simulationslauf selbst nicht wiederholt). eNOTE 521 Tests grün (−11 entfernte Tests der gelöschten Teile).
- **Nächster Schritt beim Nutzer: eNOTE und SKOULOU deployen** (keine Azure-Migration nötig).
- Azure-Tarif SKOULOU steht jetzt auf 3.600 F (siehe unten). Allgemeiner eNOTE-Test des Nutzers: bisher nichts aufgefallen.
- SKOULOU-Hinweis « Ce prix peut être adapté de façon juste à chaque établissement (par exemple selon sa taille) » (Nutzer 18.11.) auf
  Année scolaire (Karte + Hinweis), Facturation SKOULOU, CGU Direction, Admin › Tarifs, Formation Responsable/Admin. Kein Versprechen
  « annoncé à l'avance » (SKOULOU kündigt Schulpreise nicht automatisch an) – nur: gilt ab Jahresstart, danach fest. Bilder
  `Pictures\SKOULOU-Preis-fair-2026-11-18\` (PC + Handy, kein Überlauf). 652 Tests grün. Videos zeigen die alten Untertitel (unwesentlich).
- Gefunden, nicht geändert: weitere `ToLocalTime()` auf Admin-Seiten (Admin, AdminAnnonces, AdminEmails, Facturation, PoProvisionen…,
  Journaux) – plattformweit, Server = UTC = Lomé, daher dort unkritisch.

**>>> ZUERST LESEN – was offen ist (18.11. mittags):**
- **Azure-Migration SKOULOU `20261118084334_SchuleEssaiPerioden` eingespielt** (18.11. mittags, OK « ok », lesend geprüft:
  Azure = lokal, 0 Schulen auf Azure). eNOTE hat keine neue Migration.
- **Beide Apps deployt (18.11. mittags) und live geprüft:** eNOTE Anmeldeseite mit « Mot de passe oublié ? », neue Wiederherstellungsseite,
  25 Vorschaubilder 200 (neue byte-gleich), Videos ohne Anmeldung 401; SKOULOU Werbevideo v4 live (11,06 MB), Startseite « En un peu plus
  de trois minutes », CGU mit Probezeit, 31 Vorschaubilder 200 / Videos 401. Erster Aufruf nach dem Deploy ~34 s (Kaltstart).
- Git: `git status -sb` – mehrere Commits warteten auf die GitHub-Anmeldung; ggf. pushen.
- ✅ Azure-Tarif SKOULOU auf 3.600 F (18.11., auf Nutzerwunsch per SQL: 12.500 → 3.600, Zahlungsziel 30 j und 0 % unverändert,
  Prüfprotokoll-Eintrag `plattform.tarife` im Namen von DodjiEtse; Azure = lokal).
- Später, nur gemeinsam: Azure-Upgrades vor Produktion (SQL Basic, B1, Speech S0, Blob LRS) – Memory « vor-produktion ».
- Aufnahme-Apps sind gestoppt, Aufnahme-Builds gelöscht (eNOTE: vorher `dotnet publish … -o %TEMP%\enote-video-app`, README lernen-v3).

**>>> ERLEDIGT am 18.11. (Nutzer: « alles gut umsetzen », « weiter bis alles fertig ist »):**
- SKOULOU-Werbevideo « Pourquoi SKOULOU ? » v4 (3:07) eingebaut (a1b507f).
- eNOTE-Fixes: Kasse hing bei « Ouverture de la caisse… » (c954050); « Mot de passe oublié ? » verlinkt + Seiten gestaltet (df02b37);
  « Nouvel établissement » (Responsable) mit Länderliste + Hinweis « après la validation » (in 7970e93).
- **eNOTE-Videos (7970e93):** neu aufgenommen premiers-pas 4:20, visite-guidee 5:50, encaisser 5:50, attente 3:06, clients 4:38,
  cloture 3:58, activite 3:15, responsable-etablissements 4:42, responsable-paiements 2:48, admin-etablissements 3:55; neu
  acces-inscription 2:57, appareils 3:12, responsable-formation 3:27. Pipeline-Notizen: ENote `lernen-v3/README.md` « 18.11.2026 (vormittags) »
  (Gerät-Falle `kasseFrei`, Fenstertitel jetzt maskiert, `render.sh`, `katalog-dauer.py`).
- **Formation geprüft und nachgezogen:** eNOTE (in 7970e93) und SKOULOU (c679979); Azure eNOTE: Formation-Zeilen von DodjiEtse
  gelöscht (Nutzer-OK), alle Formation-Tabellen 0; SKOULOU Azure hatte keine.
- **SKOULOU: Responsable sieht auf Rechnungen keine Schülerliste** (439d001, Nutzerentscheidung « Empfehlung umsetzen »); Rückfallpreis 3.600.
- **SKOULOU: Probezeit « Période d'essai »** (f8be337): 0–4 erste Abrechnungsperioden des ersten Schuljahres ohne Rechnung/Part,
  Standard 0 – der Admin entscheidet bei jeder neuen Schule (Nutzer 18.11.; Mail bei Anmeldung erinnert daran); Admin und Responsable (eigene Schulen, Mail an Admins) ändern, solange keine Rechnung existiert;
  Direktion sieht es unter « Facturation SKOULOU ». CGU + Formation ergänzt. 652 Tests grün.

**>>> (18.11. nachmittags) SKOULOU-Videos mit der Probezeit nachgezogen (e969454, deployt und live geprüft):**
- « Administrateur : écoles et comptes » 3:05 (neue Szene « La période d'essai », 6 Schritte), « Responsable : accompagner vos
  écoles » 2:59 (Zeile « Essai » + Fenster; Messagerie-Szene in drei Sätze geteilt – vorher kam der Zeiger dort bis 9 s zu spät).
  « La facture SKOULOU » und « Facturation, tarifs et parts » sehen ohne Probezeit unverändert aus (nicht neu aufgenommen).
- Video-DB: Basis-Sicherung `SkoulouDB_VIDEO_basis.bak` erneuert (mit Spalte EssaiPerioden) – AUFTRAG.md §9.
- SKOULOU erneut deployt (18.11. nachmittags); beide Vorschaubilder live byte-gleich. Nach dem Deploy ~1–2 min Kaltstart.

**>>> EMPFEHLUNGEN für den nächsten Chat:**
1. Beobachten: SKOULOU « Comptes élèves » (160–170 ms an der größten Schule).

**>>> (18.11. vormittags) Empfehlung 2 erledigt, Empfehlung 3 vorbereitet:**
- Live-Stichprobe OK: SKOULOU alle 31 Vorschaubilder 200, Lernvideos ohne Anmeldung 401, Werbevideo öffentlich 206; Prüfseite
  `/verification/…` anonym erreichbar (« Bulletin introuvable » bei fremdem Code) – Azure hat noch **0 Bulletins**, daher Nummer/Datum
  dort nicht an echtem Code prüfbar. eNOTE: Poster `enote-admin-tarifs/formation.jpg` live mit Zeitstempel 18.11. 04:45 = lokal.
- eNOTE-Videoliste « veraltet / fehlt » dem Nutzer vorgelegt, **wartet auf sein OK** (nichts aufnehmen ohne OK):
  Veraltet (ganz neu, aufgenommen 14.11. vor « Qui encaisse ? », Mitarbeiter ohne Passwort, Provisionen, Formation):
  premiers-pas, visite-guidee, encaisser, attente, clients, cloture, activite (Clôtures mit Notiz + « Par caissier »),
  responsable-etablissements (Startseite/Menü Formation, Provision), responsable-paiements (« PoProvisionenPo » fehlt),
  admin-etablissements (sagt « quatre onglets », jetzt + Formation/Administrateurs, Land).
  Passt noch: catalogue, fidelite, compte, admin-communication (nur neue Formation-Mails), alle Konzeptvideos, equipe,
  admin-tarifs, admin-formation; Werbevideo nur Kassenkopf ohne « Changer ».
  Fehlt: « S'inscrire et mot de passe oublié » (Pour tous), « Les appareils de la caisse » (Changer, Reprendre la caisse ici,
  Écran client per QR – seit deux-appareils raus in keinem Praxisvideo), Responsable « Votre formation » (Lektion, Prüfung,
  Ergebnis am Handy); Reiter « Administrateurs » in admin-etablissements mit aufnehmen.

**>>> EMPFEHLUNGEN für den nächsten Chat (Nutzer 18.11.: « damit machen wir deine Empfehlungen weiter ») – Reihenfolge:**
1. **Werbevideo « Pourquoi SKOULOU ? » v4**: Nutzer schaut es an → bei OK einbauen (Schritte oben), sonst Änderungen umsetzen.
   Erst fragen, nicht von selbst einbauen.
2. **Live-Stichprobe nach dem Deploy** (nur lesen): `skoulou.tablosuite.com` – Vorschaubilder der neuen Videos (200), Video ohne
   Anmeldung 401, öffentliche Prüfseite `/verification/…` zeigt Nummer + Datum; eNOTE – « Vidéos d'aide » Admin zeigt die neuen
   Dauern (3:58 / 4:38).
3. **eNOTE-Videos prüfen wie bei SKOULOU** (Abdeckung + aktuelle Oberfläche): alle Abläufe je Rolle mit `VideoKatalog.cs` und den
   Seiten vergleichen; Videos mit veralteter Oberfläche **ganz** neu aufnehmen (Quell-Aufnahmen älterer Videos sind aufgeräumt).
   Dem Nutzer zuerst eine Liste « fehlt / veraltet » vorlegen, dann auf sein OK arbeiten.
4. Beobachten: SKOULOU « Comptes élèves » (160–170 ms an der größten Schule) – nur bei Bedarf optimieren.
5. Vor Produktion gemeinsam: Azure-Upgrades (SQL Basic, B1, Speech S0, Blob LRS); Azure-Tarif 3.600 F bestätigen lassen.

**>>> (18.11. früh) – Punkte 5–8 des Nutzers (erledigt, deployt):**
- SKOULOU Bulletin-Prüfseite (QR) zeigt jetzt Nummer, « Délivré le » und beim Rectificatif die ersetzte Nummer (5ba2192);
  Video « Publier les bulletins » mit neuer QR-Szene (4:52, 44368fe). Bilder: `Pictures\SKOULOU-Pruefseite\`.
- eNOTE « Piloter la formation » (3:58) und « Tarifs et facturation » (4:38, neuer Schritt 6 « Les commissions des responsables »:
  Provision entsteht beim Bestätigen der Zahlung, « Marquer comme versées ») – a81a06c. Lokale ENOTEDB danach wieder im Ausgangszustand
  (Formation- und Provisionstabellen 0). Notizen: ENote `lernen-v3/README.md` Abschnitt 18.11.
- **Neues Werbevideo « Pourquoi SKOULOU ? » (Entwurf, NICHT eingebaut)**: `Skoulou Videos/projekte/lernen-v3/pourquoi-v4/renders/
  skoulou-pourquoi-v4.mp4` (~3:07): Sorgen am Trimesterende → Versprechen → 5 Beweise in der App (Noten, Bulletin + PDF + QR am Handy,
  Eltern am Handy, Kasse mit Reçu, Accueil/Finances) → abgehakte Liste → « skoulou.tablosuite.com ». Standbilder
  `Pictures\SKOULOU-Pourquoi-v4\`. **Wartet auf Freigabe des Nutzers**; danach als skoulou-pourquoi.mp4 + skoulou-presentation.mp4
  einbauen (einbau.py, VideoKatalog-Dauer, Startseite « En moins de … minutes »).

**>>> (17./18.11. nachts) – SKOULOU-Videos Runde 3 (Nutzer: « alle bitte … ausführliche Videos über alle Themen »):**
- Fünf neue Videos über bisher fehlende Abläufe (Pipeline-Notizen `lernen-v3/AUFTRAG.md` §8):
  « S'inscrire et retrouver son accès » 4:10 (Pour tous, f50d891), « Fermer et rouvrir » 3:55 (Direction, f643794),
  « Administrateur : la formation des responsables » 3:25, « Gérer vos contenus » 3:02 (Enseignant),
  « Finances : vue d'ensemble, catégories et reprise des frais » 3:02 (Direction) – Commits f643794, 007dfc7, 0eec2a6, 4c8e960.
- App-Fixes dabei: Wartesseite « en cours de validation » nennt den Schulnamen; **Gebührenkarten** (« Frais de scolarité »):
  Datum lief bei drei Spalten in den Betrag → Karten min. 22rem, Datum darf umbrechen (finanzen.css). Die betroffene Szene
  in « Frais de scolarité et paiements » und « Une école de langues » wurde neu aufgenommen.
- Alles gepusht und deployt.

**>>> (17.11. spätabends) – SKOULOU-Videos Runde 2 (Nutzer: « viele Sachen fehlen/werden übersprungen »):**
- **Standardpreis SKOULOU = 3.600 F je Schüler und Schuljahr** (61f55aa; Trimester 1.200, Semester 1.800, 4 Sessions 900).
  **Azure-Wert stellt der Nutzer selbst um: Administration › Tarifs.** Modell bleibt Jahrespreis ÷ Perioden (Nutzer « So lassen »).
  In Videos immer als « exemple ». Aufnahme-DB-Basis steht auf 3.600 (Memory « skoulou-videos-runde2 »).
- Fertig + gepusht: Bulletin mit echtem PDF (768bea3: « Notes et conseil de classe » 5:17, neu « Publier les bulletins » 4:48,
  Lehrer « Suivre la classe : appel, notes et bulletin » 4:30); « Une école de langues : l'année en sessions » 5:17 (7403753);
  App-Fix « MOYENNE DE LA SESSION 1 » (7350618, 647 Tests grün).
- Ebenfalls fertig + gepusht (e56cfe1): « La facture SKOULOU » 4:01, « Fin d'année et passage » 2:59, « Votre établissement » 3:11,
  « Les cartes d'élève » 1:58, « Responsable : accompagner vos écoles » 2:44, « Contenus des enseignants et journal » 1:53;
  « Vidéos d'aide » mit neuen Rubriken Responsable + Administrateur. Drei Admin-Videos (« écoles et comptes », « facturation,
  tarifs et parts », « annonces, messagerie et surveillance ») – Stand siehe letzter Commit. Pipeline-Neuerungen und Fallen in
  `lernen-v3/AUFTRAG.md` §7.
- **SKOULOU am 17.11. spätabends deployt** (Nutzer; live geprüft: alle neuen Vorschaubilder 200, Videos ohne Anmeldung 401).
  Offen beim Nutzer: Azure-Tarif auf 3.600 F (Administration › Tarifs), « Vidéos d'aide » je Rolle ansehen.

**>>> NEUESTES (17.11. spätnachmittags) – SKOULOU-Formation fertig (60c5982) + Fixes beider Apps:**
- **SKOULOU-Formation** wie eNOTE (Responsable 10 Lektionen/60 Fragen, Admin 6 Lektionen/37 Fragen, `Skoulou.WebApp/Services/Formation/`,
  Seiten `/formation…`, `/admin/formation`, Menü + Guide + Startkarte). « Retirer le rôle » = Schulen im selben Schritt neu zuweisen +
  Konto sperren. 661 Tests grün inkl. SQL, Rundgang 20/20. **Migration `20261117151012_FormationResponsables` lokal und Azure
  `Skoulou-Database` eingespielt (OK « Ja », Tabellen lesend geprüft) → SKOULOU kann deployt werden.**
  Azure hat 0 aktive Responsables.
- **Beide Apps (Nutzer: Ladekreis « überall prüfen »):** Anmeldeseite(n) als statisches Formular (`[ExcludeFromInteractiveRouting]`,
  Rendermodus je Seite in App.razor) – bei schwachem Netz gingen Eingaben + Ladekreis verloren; **Ladebalken oben für jedes Tippen**;
  Handy-Umbruch langer Knöpfe; Handy-Prüfung aller Seiten ohne Überlauf. eNOTE 55b011d, 7839898 (Abstand « Règles »).
- eNOTE « Retirer le rôle » mit Neuzuweisung der Läden (Sätze/Rechte übernommen). Admin-Video « Piloter la formation » zeigt noch
  den alten Kasten ohne Abstand und die alte « Retirer »-Ansicht → **bei Gelegenheit Szenen regeln/schwach neu aufnehmen**.
- **Beide Apps müssen deployt werden** (eNOTE: keine neue Migration seit FormationResponsables).
- (Push von 7839898 ff. erledigt.)

**>>> NEUESTES (17.11. nachmittags) – « Formation » der Responsables in eNOTE (d14cd90, 61c4761, 5ea83df, gepusht, 533 Tests grün):**
Nutzerwunsch: Lernpfad + Prüfung für POs, je App (eNOTE zuerst, danach SKOULOU mit eigenem Inhalt). Entscheidungen (Memory
« PO-Formation »): Note + Fehler nach jedem Versuch, nächster Versuch neue Fragen; bei Frist/Nichtbestehen **nur Meldung** (keine
Sperre); Inhalte **fest im Code** (`Services/Formation/FormationKatalog.cs`, bei jeder App-Änderung mitpflegen!); **Handy zuerst**;
Admins trainieren frei (ohne Frist, ohne Grenze). Gebaut: 10 Lektionen (Text, Schritte, 12 App-Fotos, Videos im Dialog, Übungen),
62 Prüfungsfragen; Runde alle 3 Monate, Frist 30 Tage, 3 Versuche, der letzte zählt, 20 Fragen, bestanden ab 70 % (Regeln in
Administration › Formation änderbar); Admin je PO: Intervall, Frist verlängern, dispenser, Pause, neue Session, Historique mit allen
Versuchen, **Rolle entziehen** (Zuweisungen enden, abgemeldet, Provisionen bleiben); Mails « Évaluation ouverte », « Rappel » (7 Tage
vorher), « non réussie/manquée » ans Team (in E-mails abschaltbar). Seiten `/formation`, `/formation/lecon/{id}`, `/formation/evaluation`,
`/formation/resultat/{id}`, Kachel auf der Startseite, Menü Aide › Formation. Browser-Rundgang Handy + PC 28/28 (dabei SQL-Fehler
« zyklisches Include » gefunden und behoben). Bilder `Pictures\eNOTE-Formation-2026-11-17\`.
**Dazu (1030bea, Nutzer « Lektionen nach Rollen getrennt? » → « Ja bitte »): eigener Lernpfad für Administrateure** (6 Lektionen
`admin-…`, 36 Fragen `a-…`, Datei `FormationKatalogAdmin.cs`): Admins sehen « Parcours administrateur » + « Parcours responsable » und
trainieren frei in beiden; POs sehen die Admin-Lektionen nicht. Keine Migration. Bilder `…\admin-pfad\`. 535 Tests grün.
**Danach (17.11. spätnachmittags):** Testkonto PO « Paul Pamala » auf Azure gelöscht (Nutzer; Azure hat jetzt **keinen PO**, Nutzer legt
später neue Test-POs an), Trainingsrunden von DodjiEtse auf Azure geleert. Fixes 7bfb749: Admin kann zwischen beiden Trainingspfaden
wechseln; Lektionsbilder in Handygröße (max. 20rem, antippen = groß). **Neues Lernvideo « Piloter la formation des responsables »**
(Administrateur, 3:55, a12bb51; README lernen-v3 Abschnitt 17.11. nachmittags). **eNOTE muss erneut deployt werden** (Fixes + Video).
**Neue Migration `20261117125830_FormationResponsables`** (nur CreateTable, 5 Tabellen) – lokal ENOTEDB und **Azure eingespielt
(OK « Ja zu Azure », Tabellen lesend geprüft): Azure = lokal → eNOTE kann deployt werden.** Nutzer schaut sich zuerst die Videos an. Nach dem Deploy öffnet sich für **jeden** PO sofort die erste Session
(30 Tage) und er bekommt die Mail « Évaluation ouverte ». **Nächster Schritt danach: SKOULOU-Formation** (Rolle `Responsable`).

**>>> 17.11. zuletzt: eNOTE (« eine Kasse, ein Gerät » + Videos) und TabloSuiteHome sind DEPLOYT.** Der Nutzer macht einen echten
allgemeinen Test und meldet sich mit Auffälligkeiten. Im neuen Chat: nachfragen, was ihm aufgefallen ist.

**>>> NEUESTES (17.11. spät) – Eine Kasse je Laden, an genau EINEM Gerät (6ca60a3, gepusht, 511 Tests grün):** Nutzer: zweites
Gerät in der Kasse des ersten = abhängig, « nicht professionell » → zurückgenommen. Kasse läuft am Gerät, das sie öffnet
(`KassenSitzung.GeraetSchluessel`); andere Geräte: « La caisse est ouverte sur un autre appareil » + « Reprendre la caisse ici »
(Besitzer-PIN), altes Gerät sperrt sich sofort. Behalten: « Qui encaisse ? »/« Changer »/« Par caissier », « Appareils connectés »,
Display je Gerät. Video « Deux appareils, une caisse » aus dem Katalog. **Neue Migration `20261117103949_KasseEinGeraet`** (eine
nullable Spalte) – lokal und **Azure (OK « OK für Azure », lesend geprüft): Azure = lokal → Nutzer deployt eNOTE.**
**Videos angepasst** (eigener Commit nach 815a2f2): Konzeptvideo « La caisse » 5:10, Konzeptvideo « Équipe » 4:39,
« Votre équipe et vos réglages » 4:12 (nur Szene Paramètres neu aufgenommen). Praxistest 18/18, Bilder
`Pictures\eNOTE-Eine-Kasse-ein-Geraet-2026-11-17\` (+ `videos\`). **Nach dem Deploy prüfen:** Tablet öffnet Kasse → Handy zeigt
« La caisse est ouverte sur un autre appareil » → « Reprendre la caisse ici » mit Besitzer-PIN → Tablet sperrt sich sofort.
Prüfliste unten Punkt 2/3 gilt so nicht mehr (zweites Gerät kassiert nicht mit).

**Kurzlage:** Alles vom 17.11. ist fertig, getestet (eNOTE **502 Tests grün**) und **gepusht**. Azure `ENote-Database` = lokal: die vier
Migrationen des Tages (`PoProvisionen`, `VerkaufKassierer`, `PersoenlicheAnmeldung`, `AnmeldeSitzungen`) sind mit OK eingespielt und
lesend geprüft. **Der Nutzer deployt eNOTE am 17.11. abends selbst (Visual Studio).** Im neuen Chat zuerst fragen, ob der Deploy durch
ist, und die Prüfliste unten mit ihm durchgehen.

**Nach dem Deploy prüfen (mit dem Nutzer):**
1. Besitzer an **zwei Geräten** anmelden → beide bleiben angemeldet; Paramètres › « Appareils connectés » zeigt beide (« Cet appareil »,
   « Déconnecter », « Déconnecter tous les autres appareils »).
2. Zweites Gerät › Caisse → Fenster « Qui encaisse ? » (Name + PIN); Kopf mit Name + « Changer »; Bon « Servi par : … »; Clôture mit
   « Par caissier »; Finances › « Clôtures de caisse » mit Aufstellung je Person; Équipe zählt Verkäufe je Person.
3. « Écran client » **je Kassengerät einmal neu per QR-Code verbinden** (alter Link zeigt « Nouveau lien nécessaire »); jedes Display zeigt
   nur das Ticket seines Geräts.
4. Équipe: « Nouvel employé » ohne Passwort; Recht « Connexion personnelle » ist für **alle aus** → bestehende Mitarbeiter-Logins werden mit
   Hinweis abgewiesen (gewollt). Mit Recht: nur die gewährten Bereiche, keine Kasse, keine Besitzer-PIN.
5. Facturation: mehrere gemeldete Zahlungen auf einmal bestätigen; Administration › Propriétaires plateforme › « Commissions des
   responsables » (entstehen automatisch je bestätigter Zahlung, mehrere als « versées »); Responsable › Mes établissements › « Mes commissions ».
6. Vidéos d'aide: neu « Deux appareils, une caisse » (Employé); neue Fassungen « Votre équipe et vos réglages » und Konzeptvideos
   « Votre équipe, les droits et les codes PIN » / « La caisse ».

**Gebaut am 17.11. (Commits, alle gepusht):**
| Thema | Commit | Migration |
|---|---|---|
| Provision der Responsables automatisch + Mehrfachbestätigung (Facturation, Admin, Responsable) | cf42099 | `20261117053328_PoProvisionen` |
| « Qui encaisse ? » – jeder Bon gehört der kassierenden Person, « Changer », « Par caissier » | 09360f8 | `20261117064107_VerkaufKassierer` |
| « Connexion personnelle » – Mitarbeitende ohne Passwort, Login nur mit Recht | e8c72cc | `20261117075157_PersoenlicheAnmeldung` |
| Kundendisplay je Gerät (Geräteschlüssel, `/display/{Laden}/{Gerät}`) | dc1d5df | – |
| Équipe-Statistik nach kassierender Person | 8193d68 | – |
| « Appareils connectés » – Besitzer auf mehreren Geräten angemeldet | 48d90cf | `20261117084016_AnmeldeSitzungen` |
| Videos: « Deux appareils, une caisse » (neu), « Équipe », Konzeptvideos Équipe + Caisse | 332758f, be265d7 | – |

Kassen-Gesamttest im Browser (Tablet + Android-Handy + 2 Displays + Mitarbeiter mit/ohne Login, Kasse bis Dashboard): Skript
`%TEMP%\enote-shot-tool\e2e-kasse.mjs` (läuft gegen eine Kopie der lokalen DB `ENOTEDB_E2E` auf Port 5299), alle Prüfungen bestanden.
Bilder `Pictures\eNOTE-Gesamttest-Kasse-2026-11-17\`, `…-Display-je-Geraet-…`, `…-Connexion-personnelle-…`, `…-Qui-encaisse-…`, `…-Commissions-…`.

**Entscheidungen des Nutzers (17.11.):**
- Abo-Zahlungen bestätigt **nur der Admin**; **keine Barzahlung an Responsables** (vorhandenen Ablauf `PoAbrechnung.razor`/`PoAbrechnungService`/
  `BesitzerBetreuungVerwaltung.razor` NICHT einbinden).
- **Mitarbeitende ohne Passwort**; kassiert wird nur an Geräten, an denen der Besitzer angemeldet ist (Name + PIN). Eigener Login nur mit
  « Connexion personnelle » – dann nur die gewährten Bereiche, **nie Kasse, nie Besitzer-PIN** (siehe Memory « Mitarbeiter ohne Passwort »).
- **Besitzer-Konto darf auf mehreren Geräten angemeldet sein**; alle anderen Konten eine Anmeldung.
- **Eine gemeinsame Kasse (Schublade, Fond, Clôture) je Laden** (Option A). Option B (mehrere Kassen mit eigener Schublade) erst bei echtem
  Kundenbedarf; A ist so gebaut, dass B bestehende Läden nicht stört (« Caisse principale »).
- Verworfen: automatische Kassen-Anmeldung mit eigenem Mitarbeiter-Konto (Sicherheitsrisiko; Patch nur in `%TEMP%`).

**Offen / später:**
- **TabloSuiteHome deployen** (309bc71, 17.11.): SKOULOU-Karte jetzt Blau `#1d4ed8` statt Türkis `#0f8f86` (Streifen, Untertitel,
  Haken, Bildhintergrund); Logos/Bilder über `@Assets[...]` mit Fingerabdruck → Browser zeigen nie mehr ein altes/kaputtes Bild
  aus dem Zwischenspeicher. Live war das neue Logo schon da, nur die Katalogfarbe war noch Türkis. Bilder `Pictures\TabloSuiteHome-SKOULOU-Blau-2026-11-17\`.
  Dazu (bd6b084, 09d573a): SKOULOU-Logo der Firmenseite im Stil des eNOTE-Logos (flach #1e3a8a, « S » so groß wie « eN »), neues
  Foto `skoulou-tableau-de-bord.webp` (Startseite Direktion, dunkelblaues Logo), Kopfbereich ohne Türkis (« votre activité »,
  Symbole, Schimmer). Bilder `…\v2\`, `…\v3\`. **Nutzer deployt TabloSuiteHome.**
- ✅ SKOULOU-Mails blau (18.11., f7c8d2f).
- Videos: Admin « Tarifs et facturation » (neue Provisionen, Mehrfachbestätigung) und evtl. Responsable « Paiements et accompagnement »
  (« Mes commissions ») neu. Die Employé-Videos (`encaisser`, `attente`, `clients`, `cloture`) wurden mit `demo.awa` aufgenommen – bei
  Neuaufnahme mit `demo.proprio` + « Qui encaisse ? » (README lernen-v3, Abschnitt 17.11.).
- ✅ « Appareils connectés » in der Zeitzone des Ladens (18.11., 48881b4).
- ✅ Ungenutzter Code (PoAbrechnung …) entfernt (18.11., cf6489b; Tabellen bleiben).
- « Vor Produktion » (Azure-Upgrades, unten) – unverändert, nur gemeinsam mit dem Nutzer.

### Details 17.11. (neueste zuerst)

**>>> NEU 17.11. abends – Videos (be265d7, 332758f, gepusht):** neu « Deux appareils, une caisse » (Employé, 3:01: « Qui encaisse ? »,
« Changer », Écran client je Gerät, Clôture « Par caissier »); « Votre équipe et vos réglages » neu (4:10: ohne Passwort,
« Connexion personnelle », « Appareils connectés »); Konzeptvideos « Votre équipe, les droits et les codes PIN » (4:28) und « La caisse »
(5:05) angepasst. Ablauf/Fallen in `ENote Videos/projekte/lernen-v3/README.md` (Abschnitt 17.11.). **Achtung bei Neuaufnahmen der
Employé-Videos:** sie melden sich als `demo.awa` an – das geht ohne « Connexion personnelle » nicht mehr; mit `demo.proprio` aufnehmen.
**Nächster Schritt beim Nutzer: eNOTE deployen** (Azure ist aktuell). Offen für später: Admin-Video « Tarifs et facturation »
(neue Provisionen/Mehrfachbestätigung).

**>>> NEU 17.11. spät – « Appareils connectés » (48d90cf, 502 Tests grün):** Besitzer-Konten bleiben auf mehreren Geräten
angemeldet (Tabelle `AnmeldeSitzungen`, Stempel = ActiveSessionId, andere Konten weiter eine Anmeldung), Liste in Paramètres mit
« Déconnecter » / « tous les autres ». Kassen-Gesamttest erneut: Tablet bleibt angemeldet, Bon-Druck ok. Migration
`20261117084016_AnmeldeSitzungen` – lokal und **Azure eingespielt (OK « azuredb aktualisieren », lesend geprüft): Azure = lokal mit
allen vier Migrationen von heute → Nutzer kann eNOTE deployen.** Alles gepusht. **Danach:** Videos (« Deux appareils, une caisse »,
« Équipe »/Konzeptvideo Droits mit « Connexion personnelle », Kassenvideos mit « Changer »/« Par caissier », später Admin-Provisionen).

**>>> NEU 17.11. nachmittags – Kundendisplay je Gerät (dc1d5df) + Gesamttest Kasse (8193d68), 493 Tests grün:** Display-Gruppe je
Geräteschlüssel (localStorage), Link `/display/{Laden}/{Gerät}`, bleibt über Clôture/Tage verbunden, alter Link zeigt « Nouveau lien ».
Azure hatte zu diesem Zeitpunkt alle drei Migrationen (PoProvisionen, VerkaufKassierer, PersoenlicheAnmeldung); seit « Appareils connectés » fehlt eine vierte (s. oben). Gesamttest im Browser (Tablet + Handy + 2 Displays + Mitarbeiter mit/ohne Login): 40/41 OK; Équipe-Statistik
korrigiert. **Offener Fund (Entscheidung des Nutzers):** bestehende Regel « eine aktive Sitzung je Konto » (`ActiveSessionId`) – meldet
sich der Besitzer an Gerät 2 an, ist Gerät 1 abgemeldet (Kasse läuft im offenen Fenster weiter, aber Bon-Druck/PDF/Neuladen →
Anmeldeseite). Vorschlag: mehrere Geräte je Besitzer-Konto + Liste « Appareils connectés » mit Abmelden. Push von dc1d5df/8193d68
wartete auf die GitHub-Anmeldung des Nutzers. Bilder `Pictures\eNOTE-Display-je-Geraet-2026-11-17\`, `Pictures\eNOTE-Gesamttest-Kasse-2026-11-17\`.

**>>> NEU 17.11. mittags – « Connexion personnelle » (e8c72cc, gepusht, 483 Tests grün):** Mitarbeitende standardmäßig ohne
Passwort (kassieren am Gerät des Besitzers mit Name + PIN); eigener Login nur mit neuem Recht `UserSalon.KannSichAnmelden`
(Équipe › Droits, Passwort beim Einschalten). Mit eigenem Konto: nur Bereiche der übrigen Rechte, keine Kasse, keine
« Clients présents », nie Besitzer-PIN (auch zentral in `PruefeBesitzerFreigabeAsync`). Migration `20261117075157_PersoenlicheAnmeldung` – lokal und Azure (mit OK). Azure hat bereits PoProvisionen + VerkaufKassierer (mit OK eingespielt). **Nächster Vorschlag (wartet auf OK):
Kundendisplay je Gerät** statt je Sitzung (heute mischen sich bei zwei gleichzeitigen Kassierern die Tickets). Bilder
`Pictures\eNOTE-Connexion-personnelle-2026-11-17\`. Videos « Équipe » / « Votre équipe, les droits et les codes PIN » später anpassen.

**>>> NEU 17.11. vormittags – « Qui encaisse ? » (Option A, Nutzer « Ja A », 09360f8, gepusht, 479 Tests grün):** Jeder Bon
speichert die kassierende Person (`Verkauf.KassiererId/KassiererName`); jedes Gerät fragt beim Einstieg in die offene Kasse
Name + PIN, Kopf « Changer »; Erstattungen + Recht « Remboursements » nach angemeldeter Person; Bon « Servi par », Clôture und
Finances › Clôtures zeigen « Par caissier ». Migration `20261117064107_VerkaufKassierer` – lokal und Azure eingespielt (mit OK). Option B (mehrere Kassen/Schubladen) bewusst später; A ist so gebaut,
dass B bestehende Läden nicht stört (Caisse principale). Bilder `Pictures\eNOTE-Qui-encaisse-2026-11-17\`. Lernvideos zur Kasse
zeigen den Kopf noch ohne « Changer » (bei Gelegenheit neu).

**>>> NEU 17.11. morgens – 6 Fragen des Nutzers beantwortet, Punkte 5+6 gebaut (cf42099, gepusht, 473 Tests grün):**
eNOTE-Provision der Responsables (PO) entsteht jetzt **automatisch je bestätigter Zahlung** (Tabelle `PoProvisionen`,
**neue Migration `20261117053328_PoProvisionen`**, nur CreateTable – lokal ENOTEDB und **mit OK (« 1 ja ») am 17.11. auch auf
Azure ENote-Database eingespielt und lesend geprüft: Azure = lokal**; Deploy aber erst nach VerkaufKassierer, s. oben).
Mehrfachbestätigung: Facturation (Zahlungen), Administration › Propriétaires plateforme › « Commissions des responsables »
(mehrere als versées, Datum + Referenz), Mes établissements › « Mes commissions » (Responsable bestätigt mehrere als reçues
oder « Pas reçue »). Alte Monatsberechnung (« Calculer les commissions », fror ein) entfernt, alte Einträge bleiben sichtbar.
Bilder `Pictures\eNOTE-Commissions-2026-11-17\`. **Entscheidungen des Nutzers 17.11.:** (a) **Keine Barzahlung an POs** – nur
der Admin bestätigt Abo-Zahlungen (« So lassen »); der ungenutzte Ablauf `PoAbrechnung.razor`/`PoAbrechnungService`/
`BesitzerBetreuungVerwaltung.razor` NICHT einbinden. (b) Mehrere Kassen je Laden: Nutzer fragte nach dem Grund für « vorerst
so lassen » (Antwort gegeben: eine Schublade reicht der Zielgruppe, mehrere Geräte gehen schon, Umbau berührt den
heikelsten Teil, nur bei echtem Kundenbedarf als Option) – seine Entscheidung steht noch aus. (c) tablosuite.com zeigt live noch das alte
Türkis-Logo → TabloSuiteHome deployen (lokal korrekt seit 34cfd25). SKOULOU-Seite + Mail-Logo live korrekt.
Video « Tarifs et facturation » (Admin) zeigt noch die alte Provisionsberechnung → bei Gelegenheit neu.

### Stand 16.11. nachts

**SKOULOU-Videos fertig + deployt. ALLE 5 eNOTE-KONZEPTVIDEOS FERTIG, eingebaut + gepusht** (Folien wie SKOULOU K1–K3, je vor dem passenden Praxisvideo): « La caisse, de l'ouverture à la clôture » 4:59 (Employé), « Catalogue, stock et prix » 4:40, « Vos clients et leur fidélité » 3:23, « Votre équipe, les droits et les codes PIN » 3:44, « Vos chiffres » 4:18 (Propriétaire). Ablauf in `ENote Videos/projekte/lernen-v3/README.md` (Abschnitt Konzeptvideos). Dabei eNOTE verbessert (gepusht): Clôture-Notiz in Finances › « Clôtures de caisse » sichtbar (ebd221b); schon verkaufte Prestation wird beim Löschen deaktiviert statt an der DB zu scheitern (a05bd72); **Écart bei der Clôture → Hinweis an den Besitzer auf der Startseite** (Nutzer « ja », bd8008a). Tests eNOTE 468 grün. Keine Migration. **Nächster Schritt beim Nutzer: eNOTE deployen** (danach prüfen: « Vidéos d'aide » zeigt die 5 neuen Videos, Clôture mit Écart → Hinweis). Azure-TTS in SKOULOU prüft der Nutzer später beim Testen (Tempi als MP3 schon angehört: « Super »). Optional: « Pourquoi SKOULOU ? » neu im Muster Problem → Beweis → Rückbezug.**

**VOR PRODUKTION – GEMEINSAM MIT DEM NUTZER (Nutzer 16.11.: « Vor Produktion machen wir alles zusammen. Wir werden gucken, welche Ressourcen wir upgraden müssen. »)** – nichts davon allein ändern. Bestand am 16.11. (alles France Central, Gruppe TabloSuiteRGruppe):
- App Service: `ENote-AppServicePlan` **B1** (eNOTE) und `Skoulou-AppServicePlan` **B1** (SKOULOU + tablosuite-home). Zu klären: reicht B1 (1 Kern, 1,75 GB, keine Slots/kein Autoscale) für Blazor Server mit vielen gleichzeitigen Verbindungen?
- Azure SQL: `ENote-Database` und `Skoulou-Database` je **Basic** (5 DTU, max. 2 GB, 7 Tage Wiederherstellung) – voraussichtlich der erste Engpass; Optionen Standard S0/S1 oder vCore serverless.
- Speicher: `tablosuiteblobbtorage` **Standard_LRS** – Redundanz (ZRS/GRS) und Soft Delete für Belege/Medien klären.
- Speech: `enote-speech` **F0** (500.000 Zeichen/Monat, 20 Anfragen/Minute, geteilt mit der Videoproduktion; November bis 16.11. schon 293.929 Zeichen). Vorschlag: eigene **S0**-Ressource für SKOULOU (Videos bleiben auf F0); Preis pro 1 Mio. Zeichen im Preisrechner prüfen; kommerzielle Nutzung der Neural-Stimmen laut Forum evtl. nur mit bezahlter Stufe → Produktbedingungen prüfen.
- Außerdem besprechen: Überwachung/Warnungen (Fehler, Auslastung), Budget-Warnung für die Kosten, Sicherungen der Datenbanken.

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

✅ **ERLEDIGT 16.11. spät abends (8c0d337), war: OFFEN – am Ende prüfen (Nutzer 16.11. abends):** Auf der Seite « Vidéos d'aide » **springt die Scrollleiste hin und her** – in **beiden Apps** (eNOTE + SKOULOU) prüfen und beheben (Verdacht: Seitensperre bei offenem Fenster `html:has(...)` aus 24039f0 blendet die Scrollleiste aus/ein → Seite verschiebt sich; oder gemerkte Scrollposition aus 630ad20). Am Computer und am Handy testen. **Nutzer-Detail:** am Handy – beim Herunterscrollen springt die Seite wieder nach oben (Verdacht: Adressleiste blendet aus → Fenstergröße ändert sich → gemerkte Position wird zurückgesetzt). Analyse: `skVideoPosition` (skoulou.js ~Z. 258, enote.js ~Z. 200) horcht auf `resize`, solange ein Video offen ist, und hört erst 2,5 s nach `zurueck()` auf – wird das Fenster anders geschlossen (Zurück-Taste, Wischen), bleibt es aktiv und jede Adressleisten-Größenänderung springt zurück. Fix: nur bei echtem Vollbild/Drehung setzen, beim eigenen Scrollen des Nutzers abschalten, Schließen des Dialogs zuverlässig erkennen.

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

Siehe §0 (Stand 17.11. abends): Deploy des Nutzers abfragen → Prüfliste nach dem Deploy durchgehen → dann « Offen / später » (TabloSuiteHome-Deploy, Frage SKOULOU-Mailfarben, Admin-/Responsable-Video zu den Provisionen). Zwischendurch dem Nutzer auf Deutsch berichten, was gerade läuft; fertige grüne Schritte committen + pushen; Azure-Migrationen nur mit ausdrücklichem OK.
