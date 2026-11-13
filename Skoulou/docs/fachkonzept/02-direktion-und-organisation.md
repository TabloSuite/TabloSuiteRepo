# Fachkonzept 02 – Direktion & Schulorganisation

Grundlage ist die Alt-App (`SKOULOUAPP.*`, Stand 13.11.2026). Das Dokument beschreibt, **was** die Alt-App tut, damit beim Neubau keine Regel verloren geht. Echte Daten gibt es nicht, das Schema darf sich frei ändern. Französische Begriffe sind exakt so geschrieben wie in der Oberfläche.

**Abkürzungen für die Code-Verweise** (Pfade relativ zum Repo-Root, Angabe als `Kürzel:Zeile`):

| Kürzel | Datei |
|---|---|
| `DS` | `SKOULOUAPP.Core/Services/DashboardService.cs` |
| `SLr` / `SLcs` | `SKOULOUAPP.WebApp/Components/Pages/SchulLeiter.razor` / `.razor.cs` |
| `UVS` | `SKOULOUAPP.WebApp/Services/UnterrichtsVerwaltungService.cs` |
| `SPS` | `SKOULOUAPP.WebApp/Services/StundenplanService.cs` |
| `ANW` | `SKOULOUAPP.WebApp/Services/AnwesenheitsService.cs` |
| `BWS` | `SKOULOUAPP.WebApp/Services/BewertungsService.cs` |
| `ELS` | `SKOULOUAPP.WebApp/Services/ElternService.cs` |
| `KAR` | `SKOULOUAPP.WebApp/Services/SchuelerkartenService.cs` |
| `COC` | `SKOULOUAPP.WebApp/Services/DirektionsCockpitService.cs` |
| `IMP` | `SKOULOUAPP.Core/Services/SchoolUserImportService.cs` |
| `XLS` | `SKOULOUAPP.WebApp/Services/ExcelImportConverter.cs` |
| `DES` | `SKOULOUAPP.Core/Services/DirektionsErfassungService.cs` |
| `TLS` | `SKOULOUAPP.Core/Services/TeacherLessonService.cs` |
| `AUTH` | `SKOULOUAPP.WebApp/Services/AuthService.cs` |
| `KBS` | `SKOULOUAPP.WebApp/Services/KontoBenachrichtigungService.cs` |
| `SZR` | `SKOULOUAPP.Core/Services/SchulZuordnungsRegel.cs` |
| `IDR` | `SKOULOUAPP.Core/Services/IdentifiantRegel.cs` |
| `SZS` | `SKOULOUAPP.Core/Services/SchulZugangService.cs` |
| `DBC` / `UMK` | `SKOULOUAPP.WebApp/Data/ApplicationDbContext.cs` / `UnterrichtModellKonfiguration.cs` |
| `PRG` | `SKOULOUAPP.WebApp/Program.cs` |
| `T/…` | `SKOULOUAPP.Tests/Services/…` |

---

## 1. Zweck & beteiligte Rollen

**Zweck:** Eine Schule ist ein Mandant. Die Direktion (« Direction ») bzw. die Koordination (« Coordination ») richtet die Schule ein und betreibt sie:
- Stammdaten, Klassen, Fächer, Personal, Schüler, Eltern
- Schuljahre, Lehraufträge, Stundenpläne, Anwesenheit, Schülerkarten
- Erfassung von Lerninhalten im Namen von Lehrkräften ohne Gerät

Finanzen, Zyklen, Bulletins und Lerninhalte gehören zu anderen Bereichen. Hier stehen nur die Berührungspunkte.

### 1.1 Rollen

| Rolle (Code) | UI-Bezeichnung | Rolle in diesem Bereich |
|---|---|---|
| `SchulLeiter` | Directeur / « Direction » | Verwaltet die eigene Schule vollständig, inklusive Koordinatoren und deren Finanzrechte. |
| `Koordinator` | Coordinateur / « Espace coordination » | Wie die Direktion, aber ohne Koordinatoren, ohne Finanzrechte-Vergabe und ohne Schulanlage (Details in 1.4). |
| `Lehrer` | Enseignant | Wird verwaltet. Sieht nur Schüler zugeordneter Klassen und eigene veröffentlichte Stundenplan-Einträge. |
| `Schüler` | Élève | Wird verwaltet. Liest veröffentlichte Stundenpläne seiner Jahresklasse. |
| `Eltern` | Parent | Wird von der Direktion angelegt und verknüpft. Liest nur verknüpfte Kinder (`/parents`). |
| `ProduktOwner` | Responsable | Plattformrolle mit mehreren Schulen. Legt nur Direktoren an, über `CreatePlatformUserAsync` (Admin-Bereich). |
| `SuperAdmin` | Administrateur | Global. Gibt Direktorkonten frei, pflegt Schulpreise. |

### 1.2 Zugriffsschichten (so im Code durchgesetzt)

1. **Seiten-Policy:** `RequireSchulLeiter` = Rolle `SchulLeiter` **oder** `Koordinator` (PRG:146).
   - `/direction`, `/direction/enseignement` und `/direction/parents` prüfen per `[Authorize(Roles="SchulLeiter,Koordinator")]`.
   - Die Navigation zeigt diese Einträge nur bei Direktion oder Koordination (NavMenu.razor:49-60). Das ist keine Autorisierung.
2. **„Direktionsschulen“ eines Kontos** (`GetDirectorSchoolIdsAsync`, DS:3933-3944). Alle drei Bedingungen müssen gelten:
   - Rolle SchulLeiter oder Koordinator,
   - `UserSchool.CanManageStudents = true`,
   - die Schule ist **freigegeben** (siehe 3).

   Fast alle Schreibmethoden der Direktionsseite filtern darüber.
3. **Schule freigegeben** (SZS:12-17): `School.IsActive` **und** an der Schule hängt kein gesperrter SchulLeiter.
   - Sperrt man den Direktor, ist die ganze Schule gesperrt: kein Login für ihre Konten (SZS:22-33), keine Inhalte.
   - Ein gesperrter Koordinator sperrt die Schule nicht.
   - Die Cookie-Validierung prüft bei jedem Request: Sperre, Schulzugang und aktive Sitzungs-ID (PRG:118-140).
4. **Teilweise eigene Prüfungen pro Service:**
   - `UVS`, `SPS`, `ANW` und `BWS` prüfen Rolle, UserSchool-Link, aktive und freigegebene Schule – aber **nicht** `CanManageStudents`.
   - `ELS` und `COC` nehmen den ersten Link mit `CanManageStudents && !IstPlattformZuordnung && !IsBlocked`, ohne die Freigabe der Schule zu prüfen. `KAR` verlangt einen solchen Link zur Schule der Klasse.

### 1.3 Welche Rollen eine Direktion verwalten darf (`GetManageableSchoolUserRolesAsync`, DS:4505-4530)

| Handelnder | darf anlegen, bearbeiten, sperren, Passwort neu setzen |
|---|---|
| SuperAdmin | SchulLeiter, Koordinator, Lehrer, Schüler, ProduktOwner, SuperAdmin (nur über interne Pfade) |
| SchulLeiter | Koordinator, Lehrer, Schüler |
| Koordinator | Lehrer, Schüler |
| alle anderen | nichts |

Die Rolle `Eltern` ist dort nicht enthalten. Eltern werden ausschließlich über `ELS` verwaltet.

### 1.4 Genaue Unterschiede SchulLeiter ↔ Koordinator

| Thema | SchulLeiter | Koordinator | Beleg |
|---|---|---|---|
| Koordinatoren anlegen, bearbeiten, sperren | ja | nein | DS:4519-4527, SLcs:434-439 |
| Finanzrechte eines Koordinators setzen | ja | nein (Formularblock ausgeblendet; Service lehnt ab) | SLr:1108, DS:4146-4155 |
| Eigene Finanzrechte | immer alle (Menu, Dashboard, Entries, Cycles) | nur die pro UserSchool vergebenen Flags | FinanzService.cs:24-56 |
| Menü « Finances » | immer | nur mit mindestens einer Finanzfreigabe | NavMenu.razor.cs |
| Cockpit-Finanzkarte | immer | nur mit Menu **und** Dashboard | COC:86 |
| Schule anlegen | ja, nur wenn das Konto noch keine Schule hat | nein | DS:1471-1486 |
| Schule bearbeiten (« Modifier l'établissement ») | ja | **ja** (Name, Stadt, Land, Beschreibung) | DS:1531, SLr:18-28 |
| Zyklus schließen | mit automatischer SKOULOU-Rechnung | **ohne** automatische Rechnung | SLcs:2656-2660 |
| Sperre wirkt auf die ganze Schule | ja (SZS) | nein | SZS:12-17 |
| Texte | « Direction », « Équipe de l'établissement » | « Coordination », « Enseignants de l'établissement » | SLcs:99-425 |
| Handbuch-Reiter | mit « finance » | ohne « finance » | T/HandbuchRoleTests.cs:16 |

Ansonsten gleich: Schüler, Klassen, Fächer, Import, Réattribution, Partages, Saisie pour un enseignant, Enseignement, Stundenplan, Anwesenheit, Eltern und Karten.

---

## 2. Daten

### 2.1 Entitäten (fachlich relevante Felder)

- **School**
  - `Name` (eindeutig, max. 200; DBC:112), `City`, `Country` (Default „Togo“), `DescriptionHtml` (sanitized), `IsActive`.
  - Dokumentstammdaten: `OffizielleBezeichnung`, `Slogan`, `DokumentAdresse`, `DokumentTelefon` (60), `DokumentEmail`, `Zulassungsnummer` (100), `Ministerium`, `RepublikZeile`, `StaatsMotto` (je 300), `DokumentDirection` (200), `DokumentStatut` (60).
  - Preis- und Provisionsfelder gehören zum Bereich Finanzen.
- **AppUser** (Identity)
  - `UserName` = **Identifiant**.
  - `Email`: optional, nur Kontakt.
  - Weitere Felder: `Vorname`, `Nachname`, `FullName`, `TelefonNummer`, `Geschlecht` (Divers=0, Maennlich=1, Weiblich=2), `GeburtsdatumUtc`, `ProfilBildAssetId`.
  - `IsBlocked`, `IsSelfRegistered`, `ActiveSessionId` (eine aktive Sitzung; Wechsel = Abmeldung anderswo), `CreatedAtUtc`.
- **UserSchool**
  - Schlüssel `(AppUserId, SchoolId)`.
  - Flags:
    - `IstPlattformZuordnung`: nur ProduktOwner.
    - `CanManageStudents`: true für Lehrer, SchulLeiter und Koordinator, false für Schüler und Eltern (DS:1764).
    - Finanzflags `CanAccessFinances`, `CanViewFinanceDashboard`, `CanCreateFinanceEntries`, `CanManageCyclePayments`, `CanAccessBilling`: nur für den Koordinator relevant.
- **StudentProfile** (genau eines je Konto; UMK:21)
  - Schule und Klasse: `SchoolId`, `CurrentKlasseId` (Klasse), `CurrentLevelCode` (Anzeige-Code, redundant), `IsArchived`, `AufnahmeBestaetigtAmUtc` (Annahme einer Selbstregistrierung).
  - Elternkontakt: `ElternTelefonNummer`, `ElternEmail`.
  - Matrikel: `Matrikelnummer` (max. 60, eindeutig je Schule, gefiltert auf not null; UMK:17-23).
  - Bulletin-Kopf: `Geburtsort` (150), `Nationalitaet` (80), `IstInterne`, `IstAffecte`.
  - Sonstiges: `CompletedLessonsCount`.
- **Klasse** (Klassen sind schulbezogen)
  - `Code` (max. 20, eindeutig je Schule; DBC:116) und `Title` (max. 200). In der Direktion gilt immer **Code = Titel** (DS:2219).
  - `Description`, `Price` (FCFA), `MaxPlaetze` (Richtkapazität, Überbelegung erlaubt), `KlassenStufeId`.
  - Altlasten: `RecommendedLessonCount`, `IstSystemNiveau` (geschützte Alt-Niveaus ohne Schule), `AutoAssignSkoulouAppAboOnCycleStart`.
- **KlassenStufe** (« Nom de référence » bzw. « Classe de référence », z. B. CP1 für CP1A/CP1B): `SchoolId`, `Name` (max. 100, eindeutig je Schule).
- **Fach** (« Matière »): `SchoolId`, `Name` (max. 120, eindeutig je Schule), `Gruppe` (`Lettres` | `Sciences` | `Autres` | leer; DS:4297), `Reihenfolge` (Bulletin).
- **LehrerFachZuordnung**: `(LehrerId, FachId)`, eindeutig (DBC:119). Steuert die Fächerauswahl des Lehrers.
- **LehrerNiveauZuordnung** (« Classes attribuées »): `(LehrerID, SchulID, KlasseId)`, eindeutig, mit `Wochenstunden` (mindestens 1, Provisionsbasis). **Bestimmt, welche Schüler und Inhalte ein Lehrer sieht.**
- **Schuljahr**: `SchulID`, `Bezeichnung` (max. 80, eindeutig je Schule), `Beginn`, `Ende` (DateOnly), `IstGeschlossen`.
- **BewertungsPeriode** (Details im Bereich Noten): `Titel` (eindeutig je Jahr), `Art` (Allgemein | Trimester | Test), `Reihenfolge`, `JahresGewicht`, `IstGeschlossen`.
- **SchuelerJahresZuordnung** (« inscription annuelle »): `(SchuljahrID, SchuelerID)` eindeutig, mit `KlasseID` und `IstWiederholer`. Bezugsgröße für Noten, Anwesenheit, Bulletin und Stundenplan des Schülers.
- **UnterrichtsZuordnung** (« affectation »/Lehrauftrag): `(SchuljahrID, KlasseID, FachID, LehrerID)` eindeutig, mit `Wochenstunden` (nur Planung).
- **Stundenplan**:
  - Felder: `SchuljahrID`, `KlasseID`, `Bezeichnung` (120), `GueltigAb`, `GueltigBis`, `IstVeroeffentlicht`, RowVersion.
  - **StundenplanEintrag**: `UnterrichtsZuordnungID`, `Wochentag`, `Beginn`, `Ende`, `Raum` (80, gespeichert in GROSSBUCHSTABEN).
- **AnwesenheitsEintrag**
  - Schlüssel: `(PeriodeID, JahresZuordnungID, Datum)` eindeutig (UMK:101).
  - `Status` = Anwesend | Abwesend | Verspaetet; dazu `IstEntschuldigt`, `VerspaetungsMinuten`, `Bemerkung` (300) und `BearbeitetVonID`.
- **ElternKind**: `(ElternID, SchuelerID)` eindeutig (UMK:87), mit `SchulID`, `Beziehung` (40: « Mère », « Père », « Tuteur », « Autre ») und `ErstelltVonID`.
- **AbschlussMention** (« Mentions de fin de cycle »): `(SchulID, Text)` eindeutig, max. 80 (DBC:80-81).
- **Lesson / TeacherLesson / Exam**: `ErfasstVonId`. Gesetzt = « Saisi par la direction ». Dazu `IsSharedWithSchool` (Partage mit der Direktion).

### 2.2 Abgeleitete Zustände eines Schülers

| Zustand | Bedingung | UI-Badge |
|---|---|---|
| Inscription en attente | `IsSelfRegistered && IsBlocked` | « Inscription à valider » |
| Gesperrt | `IsBlocked && !IsSelfRegistered` | « Accès bloqué » |
| Ohne Klasse | `CurrentKlasseId == null` (nicht gesperrt) | « Classe à attribuer » |
| Aktiv | nicht gesperrt, nicht archiviert | – |
| Archiviert | `StudentProfile.IsArchived` | wird in allen aktiven Listen ausgeblendet |

Die Klassenzugehörigkeit steht in der Alt-App **mehrfach**:
- `StudentProfile.CurrentKlasseId` plus `CurrentLevelCode`,
- `LevelEnrollment.IstAktuell` (Altabo bzw. Zyklus),
- `SchuelerJahresZuordnung` (pro Schuljahr),
- Zyklusteilnahme.

---

## 3. Geschäftsregeln

### 3.1 Ein Konto = eine Schule
- SchulLeiter, Koordinator, Lehrer, Schüler (und Eltern) haben **genau eine** Schule.
  - Prüfung: SZR:30-50; Meldung « Un compte appartient à un seul établissement. Choisissez un établissement. »
  - Datenbank: gefilterter eindeutiger Index `IX_UserSchools_EineSchuleProKonto` (DBC:216-220).
- Die Schule eines bestehenden Kontos ist fest: « Ce compte appartient déjà à un autre établissement. Pour cet établissement, créez un nouveau compte. » (SZR:45-49).
  - Eine Lehrkraft an zwei Schulen braucht zwei Konten.
  - Eltern mit Kindern an zwei Schulen ebenso.
- ProduktOwner sind mehrfach zuordenbar (`IstPlattformZuordnung = true`). Der SuperAdmin hat keine Zuordnung.
- Die Direktion legt Konten nur in der eigenen Schule an. Fehlen SchoolIds und hat die Direktion genau eine Schule, wird diese genommen (DS:4129-4141).
- Ein Direktor ohne Schule darf genau eine Schule anlegen; danach meldet das System « Chaque compte de direction gère un seul établissement… » (DS:1482-1486).

### 3.2 Identifiant, E-Mail, Passwort
- **Anmeldung** mit Identifiant (= `UserName`) und Passwort für alle Rollen.
- **Identifiant** (IDR:18-25, 88-100): 3–40 Zeichen, Regex `^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$`, eindeutig über alle Konten ohne Rücksicht auf Groß-/Kleinschreibung.
- **Normalisierung** der Eingabe, still beim Speichern (IDR:27-52): Kleinbuchstaben, Akzente entfernen, Leerzeichen/Apostroph → „.“, ß → „ss“, andere Zeichen fallen weg, mehrfache Punkte werden zusammengefasst.
- **Vorschlag** „vorname.nachname“ (IDR:55-62), unter 3 Zeichen mit Präfix „compte.“; ist er vergeben, wird hochgezählt: „ama.mensah2“, „…3“ (IDR:64-85). Ein leeres Identifiant beim Speichern erzeugt automatisch einen Vorschlag (DS:1697-1711).
- **E-Mail:** optional und laut Code mehrfach verwendbar (`RequireUniqueEmail=false`, PRG:96); **Pflicht** für SchulLeiter, ProduktOwner, SuperAdmin (DS:1682-1685, « Une adresse e-mail est nécessaire pour ce rôle… »); `EmailConfirmed = (Email != null)`.
- **Passwortregel** (PRG:97-101): mindestens 6 Zeichen, eine Ziffer, ein Kleinbuchstabe; keine Großbuchstaben oder Sonderzeichen nötig.
- **Startpasswort:** Bleibt das Feld leer, erzeugt das System ein Zufallspasswort (fester 4-Zeichen-Präfix + 24 Hex-Zeichen; `ErstzugangService`, DS:1727). Es wird **einmalig** im Dialog « Identifiants initiaux » angezeigt (SLr:824-834: « Transmettez ces identifiants individuellement et confidentiellement… »).
- **Neues Passwort durch die Direktion** (« Mot de passe », KontoZugangService.cs:14-30): nur für verwaltbare Konten, nicht für das eigene (« Changez votre propre mot de passe depuis votre profil. »). Zufallspasswort per Reset-Token; `ActiveSessionId` wechselt, alle Sitzungen enden. Einmalige Anzeige, **keine** Mail.

### 3.3 Konten anlegen und bearbeiten (Direktion)
- **Allgemein** (DS:4106-4260 → DS:1651-1773): Rolle und Schule werden geprüft (3.1). Beim Speichern werden **alle** Rollen ersetzt (DS:1754-1762); ein Rollenwechsel in « Modifier l'utilisateur » ist möglich. Danach werden UserSchool und Schülerprofil synchronisiert.
- **Koordinator:**
  - Finanzrechte gelten einheitlich für alle gewählten Schulen. Die Zugriffsliste muss genau diese Schulen abdecken und überall gleich sein, sonst « Les accès financiers ne peuvent être définis que pour un coordinateur de vos écoles. » (DS:4143-4156).
  - `CanAccessFinances` nur mit Menü **und** mindestens einem Reiter; jeder Reiter-Flag nur mit Menü (DS:4203-4208).
  - Defaults bei Neuanlage: Menu, « Nouvelle écriture », « Paiements des cycles », « Facturation SKOULOU » an, « Finances de l'école » **aus** (SLcs:953-960). Ohne « Paiements des cycles » warnt das Formular: kein Zyklus starten/schließen, keine Zahlungen (SLr:1130-1133).
- **Lehrer:**
  - « Classes attribuées »: Checkboxen über alle Klassen der Schule, je 1 Wochenstunde. « Matières attribuées »: nur Fächer der Schule, fremde werden still verworfen (DS:4401-4432).
  - Zuordnungen an Schulen außerhalb der Zuständigkeit bleiben erhalten (`managedSchoolIds`, DS:1775-1815). Ohne Klasse sieht der Lehrer **keine** Schüler (SLcs:1942-1944). Checkbox « Compte bloqué » im Formular (SLr:1158-1164).
- **Schüler:**
  - « Classe actuelle » ist optional (« À attribuer ultérieurement »), muss aber zur Schule gehören und darf kein System-Niveau sein (DS:1663-1670).
  - Folgen einer Klasse (DS:3125-3200): `CurrentKlasseId` und `CurrentLevelCode` (= Code); ohne neuen Zyklus mit Plattformpreis eine technische `LevelEnrollment` (`IsUnlocked=false`, `IsPaid=false`; T/DashboardServiceTests.cs:1849); Zyklusteilnahme erfasst (`ZyklusTeilnahmeService.AktiveKlasseErfassenAsync`); bei Altzyklus mit `AutoAssign…` ein Altabo. Ohne Klasse: Profil ja, LevelEnrollment nein (:1815).
  - **Von der Direktion angelegte Schüler sind sofort aktiv**, der Service setzt `IsBlocked = false` (DS:4180-4183).
- **Bearbeiten eines Schülers** (« Modifier les données personnelles »): nur Prénom, Nom, Identifiant, E-mail, Téléphone, Téléphone/E-mail d'un parent; Schule und Klasse bleiben (SLcs:1817-1857). Gegenprobe gegen fremde Schulen über `GetSchoolUserForEditAsync` (DS:4171-4178).
- **Nach dem Speichern:** Hat sich die E-Mail geändert, gehen Mails an alte und neue Adresse (KBS:38-48). Bei Neuanlage geht eine Willkommensmail raus (SLcs:1874-1879, siehe 3.17).

### 3.4 Freigabe, Sperre, Löschen, Archivieren
- **Valider** (Selbstregistrierung freigeben, `SetSchoolUserBlockedStateAsync(…, false)`, DS:3861-3931): nur mit nicht archiviertem Profil **und** gültiger Klasse der eigenen Schule. Setzt `AufnahmeBestaetigtAmUtc` einmalig, erfasst die Zyklusteilnahme. Mail « Votre compte SKOULOU est activé » nur beim Übergang „gesperrt + selbstregistriert“ → „frei“ (KBS:23-30).
- **Bloquer / Activer:** für jede verwaltbare Rolle der eigenen Schule (DS:3864-3872). Ein Koordinator sperrt den Direktor nicht (T/DashboardServiceTests.cs:1408); ein bezahltes Abo verhindert die Sperre **nicht** (:1433-1506).
- **Supprimer l'inscription** (DS:3060-3095): harte Löschung von Konto, Profil, UserSchool, LevelEnrollments; nur bei offener Selbstregistrierung (`IsSelfRegistered && IsBlocked`) der eigenen Schule und ohne Jahres- oder Unterrichtszuordnungen (« Ce compte possède des données scolaires… »). Danach Mail « Votre demande d'inscription à SKOULOU » (KBS:32-36).
- **Archiver la sélection:** nur Schüler **ohne aktive Klasse** sind auswählbar (SLcs:607-608); der Service setzt `IsArchived=true`, ohne das selbst zu prüfen (DS:3822-3859).
  - Archivierte Schüler verschwinden aus Listen, Zählern, Cockpit, Karten und der Elternverwaltung der Direktion und verlieren den Inhaltszugang (SchuelerZugangService).
  - Der **Login bleibt möglich**: Der UserSchool-Link besteht weiter (SZS:22-33).
  - **Keine Wiederherstellung:** Kein Code setzt `IsArchived=false`.
- Lehrer und Koordinatoren werden nicht archiviert, nur gesperrt. Klassen, Klassenstufen und Schulen werden **nie gelöscht**: « Les directeurs peuvent modifier les classes, mais pas les supprimer. » (DS:2136-2140, 2372-2376).

### 3.5 Klassen und Klassenstufen (DS:2177-2367, SLcs:2517-2554)

- **Pflichtangaben:** Schule und Nom de la classe (Titel). Description ist in der UI Pflicht (`[Required]` in CourseFormModel); der Service akzeptiert sie leer.
- **Code = Titel (getrimmt).** Gleicher Code in derselben Schule führt zu « Une classe avec ce nom existe déjà dans votre école. »
- **Nom de référence ist Pflicht:** bestehende Stufe wählen oder neue anlegen. Eine gleichnamige Stufe wird wiederverwendet (DS:2302-2323). Sonst: « Sélectionnez ou créez un nom de référence pour cette classe. »
- **Plausibilitätswarnung (nur Hinweis):** Stufe und Klassenname müssen nach Normalisierung (nur A–Z/0–9) mit der Stufe beginnen, z. B. CP1 ↔ CP1A. Sonst « Référence incohérente… » (SLcs:1992-2010, 2544-2549).
- **Grenzwerte (UI):**
  - Preis 0–1 000 000 FCFA (Default im Formular 10 000; Entitäts-Default 12 500).
  - MaxPlaetze 1–10 000 oder leer.
- **Belegung** = nicht archivierte Profile mit `CurrentKlasseId` (DS:2164).
  - Balken grün unter 85 %, amber ab 85 %, rot über 100 %; Breite gedeckelt bei 200 % (SLcs:2255-2290).
  - Bei Überbelegung: « Sur-occupation autorisée : +n élève(s)… ».
- **Schule nicht änderbar:** Beim Bearbeiten ist das Feld « École » gesperrt.
- **Rechteprüfung:** UserSchool mit `CanManageStudents` für diese Schule. Rolle und Freigabe werden **nicht** geprüft (DS:2183-2194).
- **Altfunktion Auto-Abo** (`AutoAssignSkoulouAppAboOnCycleStart`):
  - Aktivieren bei aktivem Zyklus braucht eine Bestätigung und vergibt sofort Abos.
  - Deaktivieren ist verboten, wenn Schüler ein Abo haben (DS:2260-2299, 2336-2363).
  - Die Checkbox fehlt in der UI (siehe 7).

### 3.6 Fächer (« Matières »; DS:4262-4466)
- **Anlegen:** Eine leere Eingabe führt zu « Veuillez saisir une matière. »
  - Existiert der Name schon (Groß-/Kleinschreibung egal), meldet das System **Erfolg** mit « La matière existe déjà. » (idempotent).
- **Gruppe:** nur `Lettres`, `Sciences`, `Autres` oder leer (« Sans groupe »); sonst « Choisissez le groupe Lettres, Sciences ou Autres. »
- **Löschen nur ohne Verwendung:** keine LehrerFachZuordnung, keine UnterrichtsZuordnung, keine FachBewertung. Sonst « Cette matière est déjà attribuée à un enseignant… ».
  - Die UI deaktiviert den Papierkorb, sobald ein Lehrer zugeordnet ist.
- Lehrer können eigene Fächer anlegen (`AuthService.AddTeacherFachAsync`; Bereich Inhalte).

### 3.7 Lehrer ↔ Klassen/Fächer
- **Dialog « Classes de … »** (SLr:1215-1263, DS:4691-4803):
  - Klassen pro Schule mit Wochenstunden (Eingabe min. 1, Schritt 0,5).
  - Der Service verwirft Einträge mit 0 Stunden und setzt mindestens 1.
  - Der Satz wird vollständig ersetzt.
- **Was die LehrerNiveauZuordnung steuert:** Sichtbarkeit von Schülern und Inhalten für den Lehrer und die Erlaubnis, dass die Direktion für ihn in dieser Klasse erfasst (DS:2588-2591).
- **Lehrauftrag im Jahr** (`UnterrichtSpeichernAsync`, UVS:167-193). Voraussetzungen, alle gleichzeitig:
  - offenes Schuljahr,
  - Klasse und Fach der Schule,
  - Lehrer aktiv, mit Rolle Lehrer und Link zur Schule,
  - **LehrerNiveauZuordnung zur Klasse und LehrerFachZuordnung zum Fach**,
  - Wochenstunden 0,01–168 mit max. 2 Dezimalen.

  Speichern ist ein Upsert über `(Jahr, Klasse, Fach, Lehrer)`. Die Stunden der LehrerNiveauZuordnung ändern sich dabei **nicht** (T/UnterrichtsVerwaltungTests.cs:92).
- **Lehrauftrag entfernen:** nur in einem offenen Jahr (UVS:195-204).

### 3.8 Réattribution (Schüler ohne aktive Klasse → Klasse)
- **Links Zielklasse, rechts Kandidaten:** Kandidaten sind Schüler ohne `CurrentKlasseId`. Filter:
  - « Situation précédente »: alle / « Nouveaux élèves (aucune classe précédente) » / « Ancienne classe : X ».
  - Freitextsuche.
- **Frühere Klasse** = letzte nicht aktuelle LevelEnrollment, sortiert nach Zyklusende/-start bzw. Erstellung (DS:3311-3335).
- **Zuweisen** (DS:3356-3458):
  - Läuft in einer Serializable-Transaktion mit globalem App-Lock.
  - Wirkt nur für Profile derselben Schule ohne aktuelle Klasse; eine leere Treffermenge führt zu « Aucun élève sans classe active… ».
  - Folgen wie in 3.3 (LevelEnrollment mit `PriceAtEnrollment = Klasse.Price`, Zyklusteilnahme, Altabo).
  - Meldung « n élève(s) attribué(s) à X ».
- **« Retirer »** (DS:3460-3509): setzt `CurrentKlasseId=null` und `LevelEnrollment.IstAktuell=false`.
  - Verboten bei Altabo `IsUnlocked`/`IsPaid` oder abgerechnet: « Cette affectation possède déjà un abonnement actif ou payé… ».
  - Bei neuen Zyklen (Plattformpreis) immer erlaubt.
- **Klassenwechsel** gibt es nur als Retirer plus Zuweisen. Eine direkte Funktion « Changer de classe » fehlt.

### 3.9 Import (« Import depuis Excel »; SLcs:1306-1439, IMP, XLS)
- **Voraussetzungen:** konkrete Schule gewählt; Datei `.csv` oder `.xlsx`; max. **2 MB** (SLcs:1332).
- **CSV-Lesen:** UTF-8 mit/ohne BOM, Fallback Windows-1252 (SLcs:1375-1394).
- **XLSX** wird zu „;“-CSV gewandelt (XLS:10-12):
  - nur das erste Blatt,
  - max. 10 000 Zeilen, 32 Spalten, 8 MiB entpackt, 100 ZIP-Einträge,
  - DTD verboten.
- **Trennzeichen** je Zeile: „;“, wenn die Zeile ein „;“ enthält, sonst „,“. Anführungszeichen-Escaping ist unterstützt (IMP:343-377).
- **Kopfzeilen-Aliasse** (IMP:16-39): Prénom/Prenom/Vorname; Nom/Nachname; Classe/Klasse; Identifiant/Login; E-mail/Email; Téléphone/Telephone/Telefon; E-mail parent; Téléphone parent. Akzent-tolerant, BOM-tolerant, tolerant auch für fehldekodiertes „PrÃ©nom“.
- **Pflicht pro Zeile:** Prénom, Nom, Classe.
  - Klassen werden über Code **oder** Titel gefunden, ohne Rücksicht auf Groß-/Kleinschreibung.
  - Lehrer dürfen mehrere Klassen haben, getrennt durch „,“ oder „|“.
  - Schüler genau eine Klasse: « Les élèves ne peuvent être affectés qu'à une seule classe… ».
- **E-Mail:** wird kleingeschrieben und auf Syntax geprüft.
- **Identifiant:** aus der Datei (Format und Eindeutigkeit, auch innerhalb der Datei) oder automatisch („(proposé)“).
- **Vorschau** (`PreviewAsync`):
  - Rechteprüfung: Schule freigegeben, Rolle SL oder Koord, Link mit `CanManageStudents`, nicht gesperrt (IMP:294-300).
  - Die Tabelle zeigt **nur die ersten 20 Zeilen**.
  - Der Import-Button ist nur aktiv, wenn **alle** Zeilen gültig sind.
- **Commit:**
  - Die Vorschau wird erneut gerechnet.
  - Pro Zeile entsteht ein Konto mit eigenem Zufallspasswort, `IsBlocked=false` und `IsSelfRegistered=false`.
  - Lehrer: Klassenzuordnungen mit 1 h/Woche, **keine Fächer**.
  - Schüler: Klasse, Eltern-E-Mail und Eltern-Telefon.
- **Atomar:** Der ganze Import läuft in einer Serializable-Transaktion mit App-Lock (BenutzerArbeitsvorgang). Scheitert eine Zeile, wird alles zurückgerollt: « L'import a été annulé intégralement… » (IMP:154-165).
- **Keine Mails** beim Import.

### 3.10 Schuljahr, Perioden, Jahreszuordnung, Matrikel, Dokumentdaten
- **Schuljahr anlegen** (UVS:152-165):
  - Bezeichnung 1–80 Zeichen; `Ende > Beginn`.
  - Name eindeutig, und **kein Überlappen** mit anderen Jahren der Schule.
  - UI-Default 1.9. bis 31.7. des Folgejahres (UnterrichtsVerwaltung.razor.cs).
  - Kein Bearbeiten, kein Löschen.
- **Laufendes Schuljahr** = nicht geschlossen und `Beginn ≤ heute ≤ Ende`, bei mehreren das mit dem spätesten Beginn (COC:68). Ein „aktiv“-Flag gibt es nicht.
- **Schuljahr schließen** (UVS:56-64):
  - Nur mit Bestätigungs-Checkbox.
  - **Irreversibel**: kein Wiederöffnen.
  - Danach sind Lehraufträge, Jahreszuordnungen, Pläne, Perioden und Anwesenheit gesperrt.
- **Perioden** (auf `/notes`, BWS:21-37):
  - Titel 1–120, eindeutig je Jahr; Reihenfolge ≥ 1.
  - Trimester: Reihenfolge 1–3, eindeutig je Jahr.
  - JahresGewicht 0–100 mit 2 Dezimalen. Für Art Test wird es auf 0 gesetzt, sonst muss es > 0 sein.
  - Schließen und Wiederöffnen ist Toggle, nur in offenem Jahr (BWS:10-19).
- **Jahreszuordnung** (« Inscriptions annuelles et redoublement », UVS:206-233):
  - Bedingungen: offenes Jahr; der Schüler ist **aktuell** in genau dieser Klasse (`CurrentKlasseId`); keine endgültige Bulletin-Veröffentlichung der Klasse in diesem Jahr.
  - Je Jahr nur eine Klasse: « L'élève possède déjà une classe pour cette année scolaire. »
  - Idempotent; setzt `IstWiederholer`.
- **Matrikel** (UVS:75-119):
  - Wird getrimmt und GROSS geschrieben; max. 60; eindeutig je Schule, inklusive archivierter Schüler.
  - Leer bedeutet: bestehende behalten, sonst automatisch `EL-` + Benutzer-GUID ohne Bindestriche.
  - Neue Profile bekommen diese Auto-Matrikel immer schon beim Insert (DBC:55-60).
  - « Attribuer les matricules manquants de l'école » füllt nur leere.
- **Coordonnées officielles** (UVS:24-45): alle Felder optional; DokumentEmail muss gültig sein; leere Felder werden zu null.

### 3.11 Stundenplan (« Emplois du temps »; SPS)
- **Anlegen** (Entwurf): offenes Jahr, Zeitraum innerhalb des Jahres, Titel 1–120 Zeichen, Klasse der Schule.
- **Kurs eintragen:**
  - Nur in Entwürfen; der Lehrauftrag muss zu Klasse und Jahr des Plans passen; `Beginn < Ende`.
  - **Konfliktprüfung** (SPS:166-176): überlappende Zeit am selben Wochentag mit überlappender Gültigkeit, gegen denselben Plan oder veröffentlichte Pläne. Konflikt bei
    - gleicher Lehrkraft (auch schulübergreifend),
    - gleicher Klasse,
    - gleichem Raum derselben Schule.

    Angrenzende Stunden sind erlaubt.
- **Veröffentlichen** (SPS:105-130):
  - Mindestens ein Eintrag.
  - Kein anderer veröffentlichter Plan derselben Klasse mit überlappendem Zeitraum.
  - Alle Konflikte werden erneut geprüft.
  - Bearbeiten erst nach « Retirer la publication pour modifier ».
- **Kopieren:** Ergebnis ist ein Entwurf in derselben Klasse und demselben Jahr; Titel und Daten kommen aus « Nouveau planning ».
- **Soll/Ist:** Ist = Summe der Minuten je Lehrauftrag ÷ 60; Soll = Lehrauftrag-Wochenstunden. Die Zeile ist amber bei einer Abweichung über 0,01 h.
- **Wer sieht was** (SPS:132-154):
  - Direktion und Koordination: alle Pläne und Entwürfe der Schule.
  - Lehrer: nur veröffentlichte, eigene Einträge.
  - Schüler: nur veröffentlichte Pläne seiner **Jahreszuordnungs**-Klasse.
  - Eltern: nichts.
- **PDF** `/emplois-du-temps/{schulID}/pdf` (PRG:419-430): dieselbe Sicht als Liste, nicht als Raster.
- **Erfassungsraster in der UI:** Tagesbeginn 07:00, Ende 17:00, Slot 60 min (5–180).

### 3.12 Anwesenheit (ANW)
- **Wer:** nur SchulLeiter und Koordinator, nicht Lehrer (ANW:43-50).
- **Erfassung:** pro Schüler-Jahreszuordnung, Periode und Datum; ein Eintrag je Tag (Upsert).
- **Bedingungen** (ANW:17-25):
  - Periode und Jahr offen.
  - Datum innerhalb des **Schuljahres** (nicht der Periode).
  - Verspätung 0–1440 Minuten; Bemerkung max. 300 Zeichen.
  - Gesperrt nach endgültiger Bulletin-Veröffentlichung der Klasse: « Les présences de cette classe sont verrouillées… ».
  - Gesperrt, solange ein veröffentlichtes Bulletin des Schülers für die Periode existiert: « Retirez le bulletin publié… ».
- **Normalisierung:** `IstEntschuldigt` gilt nur bei Abwesend oder Verspätet; Minuten werden nur bei Verspätet gespeichert, sonst 0.
- **UI:** auf `/bulletins` im Abschnitt « Présences et retards », Eingabe Schüler für Schüler. Optionen « Présent », « Absent », « En retard ».

### 3.13 Eltern (ELS)
- **Direktionsschule:** erster Link mit `CanManageStudents`; sonst « Aucun établissement n'est associé à votre compte. » (ELS:37-47).
- **Anlegen** (« Créer un accès »):
  - Pflicht: Prénom und Nom; Beziehung max. 40 Zeichen.
  - Das Konto (Rolle `Eltern`, eine Schule) entsteht über den generischen `SaveUserAsync`.
  - Die Verknüpfung erfolgt sofort; die Zugangsdaten werden einmalig angezeigt (ELS:90-121).
  - Vorbelegung: Nachname = letztes Wort des Schülernamens; Telefon und E-Mail aus dem Elternkontakt des Profils.
- **Verknüpfen:**
  - Kind und Elternkonto müssen zur Schule gehören.
  - Idempotent; **max. 12 Kinder** je Elternkonto (ELS:33, 123-141).
- **Trennen** löscht nur die Verknüpfung, nicht das Konto.
- **Neues Passwort:** wie in 3.2; der Identifiant bleibt (ELS:154-170).
- **Liste:** nur aktive Schüler (nicht archiviert, **nicht gesperrt**) mit ihren Eltern. Ein Elternteil ohne Kinder bleibt als « Lier un parent existant » wählbar.
- **Sicht der Eltern** (ELS:178-228):
  - Veröffentlichte Bulletins, auch ersetzte mit Badge « remplacé ».
  - Abwesenheiten und Verspätungen des laufenden Schuljahres mit den letzten 10 Einträgen.
  - Schulgebühren laufender Zyklen: Klasse, Schulgeld, bezahlt, Rest mit min. 0, Zahlungen.
  - Ein gesperrtes Elternkonto sieht nichts.

### 3.14 Schülerkarten (« Cartes d'élève »; KAR)
- **Endpunkt** `/direction/cartes/{klasseId}` (Rollen SL und Koord). Gibt 404, wenn die Klasse nicht zur eigenen Schule gehört (KAR:22-35).
- **Inhalt:**
  - Schüler der Klasse, nicht archiviert, nicht gesperrt; sortiert nach Nachname, Vorname.
  - Format A4 mit 2 Karten pro Reihe, 85,6 × 54 mm.
- **Kopf:** `OffizielleBezeichnung ?? Name`, « CARTE D'ÉLÈVE · <Schuljahr> »; Logo aus der zuletzt geänderten BulletinVorlage mit Logo.
- **Karte:**
  - Name als „NACHNAME Vorname“; Klasse (Code), Matricule, « Né(e) le » (falls bekannt).
  - Foto aus dem Profilbild (PNG/JPEG ≤ 3 MB), sonst Feld « Photo ».
  - QR mit dem Text `SKOULOU:<Matrikel>`.
  - Fuß: « Valable pour l'année scolaire … » und « Signature de la direction ».
- **Datei:** `cartes-<Code>.pdf`. Ohne Schüler: « Aucun élève actif dans cette classe. »

### 3.15 Cockpit-Kennzahlen (COC)
- **Schule:** erster Link mit `CanManageStudents`, nicht Plattform; null, wenn keiner existiert (Meldung « Aucun établissement n'est associé… »).
- **Kennzahlen:**
  - Élèves actifs: nicht archiviert und nicht gesperrt, auch ohne Klasse.
  - Inscriptions en attente: selbstregistriert und gesperrt.
  - bloqué(s): gesperrt, nicht selbstregistriert.
  - Classes: ohne System-Niveaus, dazu laufende Zyklen.
  - Enseignants: Links mit `CanManageStudents` und Rolle Lehrer.
  - Absences und Retards heute; « Aucune présence n'a encore été saisie aujourd'hui » ohne Eintrag.
  - Bulletins publiés im laufenden Jahr: nicht ersetzt.
  - Finanzen (Encaissés, Reste à encaisser, Solde, Part encaissée in %): nur mit Menu und Dashboard.
- **« À faire »-Liste:**
  - offene Anmeldungen → `schulleiter?onglet=eleves`,
  - kein laufendes Schuljahr → `direction/enseignement`,
  - 0 Klassen → `?onglet=classes`,
  - 0 Lehrer → `?onglet=equipe`.

### 3.16 « Saisi par la direction », Partages, „Réattribuer“
- **Wer darf für wen handeln** (`DarfFuerLehrerHandelnAsync`, DES:31-65): Der Lehrer selbst darf immer.
  - Andernfalls muss der Handelnde: nicht gesperrt sein, Rolle SL oder Koord haben, mit dem Lehrer eine **freigegebene** Schule teilen (nicht über eine Plattformzuordnung).
  - Das Ziel muss die Rolle Lehrer haben und darf nicht gesperrt sein.
  - Ein Lehrer darf nie für einen Kollegen handeln.
- **Jede Schreibaktion prüft neu** und setzt `ErfasstVonId = Handelnder` (DES:84-93). Arbeitet der Lehrer selbst, bleibt die Kennzeichnung erhalten.
  - Zusätzlich muss die Klasse dem Lehrer zugeordnet sein: « Cette classe n'est pas attribuée à cet enseignant. » (DS:2486-2489).
- **Auswahl « Saisir pour l'enseignant : »** auf `/teacher/lessons`, `/teacher/lesson-exercises` und `/teacher-exams`:
  - nur aktive Lehrer der eigenen freigegebenen Schule (DES:99-125);
  - der Text sagt « Aucune confirmation de l'enseignant n'est nécessaire. »
- **Markierung:** Badge « Saisi par la direction » für Lehrer und Direktion, nicht für Schüler.
  - Kopien und neue Runden übernehmen die Kennzeichnung (TLS:820, ExamService.cs:840-843, DS:2570-2575).
- **Partage:** Inhalte mit `IsSharedWithSchool` (TeacherLesson und Exam) erscheinen im Reiter « Contenus partagés » (TLS:17-68). Die Liste ist durchsuchbar und wird in Schritten zu 50 angezeigt.
- **„Réattribuer“** bedeutet im Code eine **neue Prüfungs- oder Lektionsrunde** für dieselbe Klasse (ExamService.cs:802-850), **nicht** die Übergabe an einen anderen Lehrer. Eine Übertragung von Inhalten zwischen Lehrern **gibt es nicht**.

### 3.17 Mails dieses Bereichs
| Anlass | Mail | Bedingung |
|---|---|---|
| Selbstregistrierung Direktor/Schüler | Willkommensmail (AUTH:169, 228) | E-Mail plausibel (Syntax) |
| Neues Konto durch die Direktion | Willkommensmail mit Identifiant (SLcs:1874-1879) | best effort; Textvariante nach Rollenpriorität SuperAdmin > SchulLeiter > Lehrer > Schüler (AUTH:785) |
| Elternkonto | Willkommensmail (ElternVerwaltung.razor.cs) | nur mit E-Mail |
| Valider | « Votre compte SKOULOU est activé » | nur beim Übergang von „selbstregistriert und gesperrt“ zu „frei“ |
| Supprimer l'inscription | « Votre demande d'inscription à SKOULOU » | nur bei offener Selbstregistrierung |
| E-Mail geändert | Sicherheitshinweis an alte und neue Adresse | Adresse geändert |
| Import, neues Passwort, Sperre, Archivierung | **keine** Mail | – |

Mails enthalten nie Passwörter oder Tokens (T/WillkommensMailTests.cs:182). Ein Versandfehler wird geloggt und bricht die Aktion nie ab.

---

## 4. Abläufe

**A – Schule starten**
1. Der Direktor registriert sich selbst mit Schule (Name, Stadt, Land, Default Togo). Es entstehen Schule, Konto (`IsBlocked`, `IsSelfRegistered`), Rollen **SchulLeiter + Lehrer** und ein Link mit `CanManageStudents` (AUTH:121-171); Willkommensmail « Votre école SKOULOU est enregistrée ».
2. Bis zur Freigabe durch den SuperAdmin scheitert der Login mit « Votre compte n'a pas encore été validé ou a été bloqué. »
3. Danach startet die Arbeit auf `/direction`; « À faire » zeigt die Startaufgaben.

**B – Schuljahr einrichten** (empfohlene Reihenfolge)
1. `/direction/enseignement`: « Coordonnées officielles » und « Matricules et état civil » pflegen, « Créer l'année scolaire ».
2. `/schulleiter` › Classes: Klassen mit « Nom de référence » anlegen; dann Matières anlegen und gruppieren.
3. Équipe: Koordinatoren (mit Finanzrechten) und Lehrer (mit Klassen und Fächern) anlegen, einzeln oder per Import.
4. Élèves: anlegen (mit Klasse) oder importieren.
5. Enseignement: « Affecter un enseignant » (Klasse × Fach × Lehrer × Stunden), dann « Inscriptions annuelles » je Schüler, einzeln.
6. `/notes`: Perioden und Bestandteile anlegen (Bereich Noten).
7. `/emplois-du-temps`: Entwurf anlegen, Kurse eintragen, Soll/Ist prüfen, « Publier le planning ».

**C – Konto anlegen (Équipe oder Élèves)**
1. « Nouvel utilisateur » bzw. « Ajouter un élève » öffnen (aus einer Klassenkarte mit vorbelegter Klasse); Rolle wählen.
2. Prénom und Nom erfassen, « Proposer » für den Identifiant; optional E-Mail, Telefon, bei Schülern Elternkontakt.
3. Je Rolle: Lehrer → Klassen und Matières; Koordinator → Finanzrechte; Schüler → Classe actuelle. Passwort leer lassen oder setzen.
4. Speichern: Dialog « Identifiants initiaux », Willkommensmail, alle Listen werden neu geladen.

**D – Selbstregistrierung eines Schülers bearbeiten**
1. Der Schüler wählt bei der Registrierung eine aktive Schule: Konto gesperrt und selbstregistriert, Profil ohne Klasse, Willkommensmail.
2. Die Direktion sieht « n inscription(s) attendent votre validation » und den Chip « À valider ».
3. Klasse über Réattribution zuweisen (« Ajouter à la classe »); « Valider » ohne Klasse scheitert.
4. « Valider »: setzt `AufnahmeBestaetigtAmUtc`, erfasst die Zyklusteilnahme, sendet die Aktivierungsmail. Alternativ « Supprimer l'inscription »: harte Löschung und Ablehnungsmail.

**E – Import:** Reiter Import → Schule wählen → Datei für Enseignants oder Élèves → Vorschau (gültige und fehlerhafte Zeilen) → Fehler in der Datei korrigieren und neu laden → « Importer les … (n) »: atomare Anlage, Meldung « Import terminé: n utilisateur(s) créé(s). »

**F – Klassenwechsel / Schulaustritt:** Réattribution mit Zielklasse = alte Klasse → « Retirer ». Für einen Wechsel danach der neuen Klasse zuweisen. Für einen Austritt: Élèves › Häkchen › « Archiver la sélection » (endgültig).

**G – Passwort vergessen (Konto ohne E-Mail):** « Mot de passe » → bestätigen → neues Passwort wird einmal angezeigt, alle Sitzungen enden. Konten mit E-Mail nutzen alternativ « Mot de passe oublié » (Bereich Auth).

**H – Elternzugang:** `/direction/parents` → Schüler suchen → « Créer un accès » (Daten, Lien, Identifiant) oder « Lier un parent existant » → Zugangsdaten einmal anzeigen. Der Elternteil landet nach dem Login direkt auf `/parents` (bei mehreren Kindern mit Tabs).

**I – Anwesenheit:** `/bulletins` › « Présences et retards » → Periode und Schüler wählen → Datum, Situation, « justifié », Minuten → « Enregistrer ». Das Cockpit zeigt die Tageswerte, Eltern sehen Abwesenheiten und Verspätungen.

**J – Schülerkarten:** Cockpit › « Cartes d'élève » → Klasse wählen → « Imprimer les cartes » öffnet das PDF in einem neuen Tab.

**K – Saisie pour un enseignant:** `/teacher/lessons` (bzw. Exercices, Examens) → Lehrer in « Saisir pour l'enseignant : » wählen → mit seinen Klassen und Fächern erstellen, bearbeiten, veröffentlichen, löschen. Der Inhalt gehört dem Lehrer und trägt den Badge.

---

## 5. Seiten, Routen, Endpunkte

| Route | Rollen | Inhalt |
|---|---|---|
| `/direction` | SL, Koord | Tableau de bord (3.15); Schnellzugriffe Élèves et enseignants, Notes et évaluations, Emplois du temps, Accès parents; « Gérer l'établissement » |
| `/schulleiter[?onglet=…]` | SL, Koord | Hauptverwaltung mit Reitern `eleves`, `classes`, `equipe`, `matieres`, `reattribution`, `import`, `partages` |
| `/direction/enseignement` | SL, Koord | « Organisation des enseignements » (3.10, 3.7) |
| `/direction/parents` | SL, Koord | « Accès parents » (3.13) |
| `/parents` | Eltern | « Espace parents » |
| `/emplois-du-temps` | SL, Koord, Lehrer, Schüler | Pläne verwalten oder lesen |
| `GET /emplois-du-temps/{schulID}/pdf` | SL, Koord, Lehrer, Schüler | PDF der eigenen Sicht |
| `GET /direction/cartes/{klasseId}` | SL, Koord | Kartenbogen (PDF) |
| `/bulletins` (Teil) | SL, Koord, Schüler | Anwesenheit erfassen |
| `/teacher/lessons`, `/teacher/lesson-exercises`, `/teacher-exams` | Lehrer, SL, Koord | Direktionsansicht und Erfassung für Lehrer |
| `/teacher-lessons/{id}/view` | SL, Koord | Leseansicht einer Lehrerlektion |

**`/schulleiter` im Detail** (SLr):
- **Kopf:** Titel « Élèves, classes et équipe »; « Modifier l'établissement » öffnet « Mes écoles » (Nom mit Dublettenhinweis, Ville, Pays durchsuchbar, Description). Bei mehreren Schulen (Altbestand) zusätzlich Schulauswahl « Toutes les écoles » mit Zählern.
- **Élèves** (SLr:51-269): Hinweisbox zu offenen Anmeldungen; « Importer », « Ajouter un élève »; Filter « Classe de référence » und « Classe » (ab 2 Klassen); 10 pro Seite, Suche über Name, E-Mail, Schule, Klasse; Sortierung Nom A-Z, Score global auf/ab, Progression, Leçons terminées; Chips « À valider », « Sans classe », « Scolarité incomplète ».
  - Je Eintrag: Badges, Klasse, E-Mail, Schule; Pille « Scolarité : x / y FCFA payé » bzw. « cycle finance à démarrer »; « Exercices et examens : n % (Anzahl) »; Knöpfe Valider, Supprimer l'inscription, Modifier, Mot de passe, Classes et abonnement (aufklappbar: Altabo, Zyklusstatus), Bloquer. Archivauswahl « Tout sélectionner pour l'archivage », « Archiver la sélection ».
- **Classes** (SLr:272-431): Filter « Statut des cycles » (ohne Wirkung, siehe 7) und « Nom de référence », Suche; Karten mit Titel, Code, Réf., Preis, Zyklus-Badge, Belegungsbalken; « Ajouter un élève », « Notes finales », « Clôturer le cycle », « Démarrer le cycle » (Zyklusknöpfe nur mit Zyklusrecht, Bereich Finanzen). Darunter « Mentions de fin de cycle » (max. 80 Zeichen, eindeutig je Schule; Ajouter, Modifier, Supprimer).
- **Équipe** (SLr:433-503): Liste ohne Schüler, Chips Coordinateurs/Enseignants, Suche nach Name, Identifiant, E-Mail, Rolle; Knöpfe Modifier, Mot de passe, Classes (nur Lehrer), Activer/Bloquer.
- **Matières** (SLr:505-573): Suche, « Nouvelle matière », Liste mit Gruppen-Dropdown und Papierkorb, Dialog « Supprimer la matière ».
- **Réattribution** (SLr:575-679): 1. Classe cible (Schüler der Klasse mit « Retirer »), 2. Élèves sans classe active (Filter, Suche, Häkchen), « Ajouter à la classe ».
- **Import** (SLr:681-785): 4-Schritte-Anleitung, Mustertabellen (Enseignants: Prénom*, Nom*, Classe*, E-mail, Téléphone; Élèves zusätzlich E-mail parent, Téléphone parent), Datei-Picker, Vorschau, Import-Button.
- **Contenus partagés** (SLr:787-821): Typ (« Support de cours »/« Examen »), Klassencode, Titel, Autor; Suche; « Afficher plus ».
- **Dialoge:** Identifiants initiaux, Nouveau mot de passe, Retirer l'élève de la classe, Mes écoles, Supprimer la matière, Gestion des utilisateurs, Gérer l'abonnement (Altabo, « Enregistrer comme payé »), Classes de …, Nouvelle/Modifier la classe, Bestätigung Auto-Abo, Zyklus starten/schließen/endgültig (Bereich Finanzen).

**`/direction/enseignement`:** Schulauswahl; aufklappbare Abschnitte « Coordonnées officielles de l'école », « Matricules et état civil des élèves », « Années scolaires » (anlegen und wählen), « Inscriptions annuelles et redoublement » (Liste mit Suche), « Archives et clôture de l'année », « Affecter un enseignant », « Enseignements de l'année sélectionnée » (Suche, « Retirer »).

**`/emplois-du-temps`:** Schulauswahl. Nur Direktion: « Nouveau planning » (Année, Classe, Intitulé, Valable à partir du/jusqu'au, « Créer le brouillon »), Plan-Auswahl, « Copier ce planning », Soll/Ist-Zeilen, « Grille de saisie des horaires », Kurs-Formular, « Publier le planning » bzw. « Retirer la publication pour modifier ». Für alle: Suche, « Télécharger le PDF », Kurse nach Wochentag (Montag zuerst), « Retirer » bei Entwürfen.

**`/direction/parents`:** Suche, Zähler « n élève(s) sur m avec un accès parent »; Karten je Schüler mit verknüpften Eltern (Nouveau mot de passe, Retirer), « Créer un accès », « Lier un parent existant »; Anzeige in Schritten zu 50.

---

## 6. Durch Tests festgelegte Regeln

Zeile = Testmethode unter `SKOULOUAPP.Tests/Services/`; ohne Dateiname = `DashboardServiceTests.cs`. SQL-Tests laufen nur mit `SKOULOU_SQL_TESTS=1`, sonst gelten sie leer als bestanden.

| Thema | Festgelegtes Verhalten |
|---|---|
| Rollen und Anlage | Direktor legt keinen SchulLeiter an (:1982); Koordinator nur in eigener Schule, mit `CanManageStudents=true` (:1767); Koordinator legt Lehrer/Schüler an, keinen SL/Koordinator (:1932, :167); ProduktOwner nur Direktoren seiner Schule, `SaveSchoolUserAsync` verweigert (:1888, :58) |
| Schülerprofil | ohne Klasse: Profil, keine LevelEnrollment (:1815); mit Klasse: genau eine technische LevelEnrollment, nicht freigeschaltet, unbezahlt (:1849); von der Direktion angelegte Schüler sind nicht gesperrt (GesamtAblaufTests.cs:57) |
| Finanzrechte Koordinator | Flags wie gewählt, erneutes Speichern ändert nichts, leer entzieht alles, Menu ohne Reiter = kein Zugriff, fremde Schule → Speichern scheitert, kein Konto (:2010, :2054, :2089, :2122) |
| Ein Konto = eine Schule | zwei Schulen und Schulwechsel abgelehnt mit exakter Meldung (:196); ProduktOwner mehrfach mit `IstPlattformZuordnung` (:238); ohne SchoolIds gilt die Direktionsschule (:416); Altbestand-Lehrer mit 2 Schulen nicht speicherbar (:382); gleiche Person an 2 Schulen = 2 Konten „ama.mensah“/„ama.mensah2“ (IdentifiantTests.cs:37) |
| Identifiant | „Ama Mensah“ → „ama.mensah“, „Éléonore D'Almeida“ → „eleonore.d.almeida“, „KOFFI-Agbéko“ → „koffi-agbeko“ (IdentifiantTests.cs:24); ungültig „a1“, „ama mensah“, „Ama.Mensah“, „.ama“ (:33); Dubletten ohne Rücksicht auf Groß-/Kleinschreibung, SL ohne E-Mail abgelehnt, Schüler ohne E-Mail ok (:70); Passwort-vergessen-Mail listet alle Konten einer Adresse (:104) |
| Konto und Sitzung | E-Mail-Wechsel lässt Identifiant, Passwort, Profil, Matrikel, Klasse, Fortschritt unverändert; Passwortwechsel rotiert die Sitzung (SchuelerIdentitaetTests.cs:24); neues Passwort durch Direktion nur für eigene Schüler/Lehrer, nicht für fremde oder sich selbst, Sitzung rotiert (KontoZugangServiceTests.cs:14) |
| Fächer, Zuordnungen | Fach nur in eigener Schule (:488), `IstLehrerZugewiesen` (:513), Löschen nur unbenutzt (:545), fremde Fächer verworfen (:297, :334, :453); Lehrer sehen nur Schüler zugeordneter Klassen, ohne Zuordnung keine (:958, :993, GesamtAblaufTests.cs:830) |
| Klassen | doppelter Name abgelehnt (:1641); CEP1A/CEP1B → eine KlassenStufe, Code = Titel (:1716); fremde Schule abgelehnt (:1669); System-Niveaus unsichtbar, nicht bearbeitbar, nicht löschbar (:1693, :1572, :1747); Klassen/Stufen nicht löschbar (:1603, :1622); Klassenerlaubnis nur eigene Schule (KlassenZuordnungsServiceTests.cs:12) |
| Liste, Réattribution | frühere Klasse „CP1A · CP1 A“ (:637, :674); Retirer ohne Abo ok, mit `IsUnlocked` verboten (:698, :725); Löschen nur bei offener Selbstregistrierung (:751, :786) |
| Sperre | bezahltes Abo verhindert Sperre nicht (:1433, :1479, :1506 – Testnamen widersprechen); Koordinator sperrt keinen fremden Schüler und nicht den Direktor (:1382, :1408); ProduktOwner sperrt nie (:132); gesperrter Direktor sperrt die Schule, Lehrer mit aktiver Zweitschule kommt weiter rein (SchulZugangServiceTests.cs:12, SchulSperreInhaltsTests.cs:39) |
| Saisi par la direction | erlaubt für SL/Koord derselben Schule und den Lehrer selbst; verboten für fremde Schule, gesperrte Partei, inaktive Schule, Kollegen, Nicht-Lehrer (DirektionsErfassungServiceTests.cs:131-205); Auswahl nur aktive Lehrer der Schule (:214); AuthorId = Lehrer, ErfasstVonId = Direktion, Markierung bleibt bei Lehrer-Edit (:235, :357); nur Klassen des Lehrers (:278); Prüfung im Namen des Lehrers ist veröffentlicht und **nicht** geteilt (:314) |
| Partage | sichtbar nur mit `includeSharedWithDirection`, Leseansicht, PDF nur mit `IsPdfExportEnabled` (TeacherLessonServiceTests.cs:244, :280, :313, :348) |
| Import | Lehrerkonto darf nicht importieren (SchoolUserImportServiceTests.cs:15); Identifiant-Vorschlag (:34); unbekannte Klasse = Zeilenfehler (:58); französische Header, BOM, „PrÃ©nom“ akzeptiert (:77-:141); Lehrer mit 2 Klassen → 2 Zuordnungen, Passwort ≥ 20 Zeichen (:164); XLSX erstes Blatt, „;“, Escaping, 8 MiB (ExcelImportConverterTests.cs:11-30); atomar, keine Duplikate bei verlorenem Commit (BenutzerArbeitsvorgangSqlTests.cs:25, SQL) |
| Unterricht | SL/Koord legen Jahr und Lehrauftrag an, Stunden-Upsert, LNZ-Stunden bleiben 1 (UnterrichtsVerwaltungTests.cs:92); abgelehnt: leerer Name, Ende vor Beginn, Überlappung, fehlende LehrerFachZuordnung, geschlossenes Jahr (:130, :142, :15); Matrikel GROSS, eindeutig, Auto-Wert, Ergänzen idempotent (:49, :191); Jahreszuordnung idempotent mit Redoublant (:169); Dokumentdaten getrimmt, ungültige E-Mail abgelehnt (:31) |
| Stundenplan | Überlappung abgelehnt, angrenzend ok, Lehrerkonflikt (StundenplanTests.cs:44, :78); ohne Eintrag kein Veröffentlichen, Lehrer/Schüler sehen erst danach etwas, veröffentlicht nicht editierbar (:57); Kopie = Entwurf, Soll/Ist minutengenau (:15); PDF wird erzeugt (:32) |
| Anwesenheit | Lehrer abgewiesen, Datum außerhalb des Jahres abgewiesen, gleicher Tag überschreibt, nach Veröffentlichung gesperrt, Werte im Bulletin-Snapshot (BewertungsServiceTests.cs:454); geschlossene Periode sperrt (:418) |
| Eltern | Anlage mit Identifiant und Passwort, Geschwister verknüpfbar, fremde Schule → InvalidOperation, Eltern sehen nur eigene Kinder (Bulletins, Abwesenheiten, Gebühren), Trennen und neues Passwort behalten den Identifiant (ElternServiceTests.cs:19) |
| Karten | fremde Direktion → null, eigene → `cartes-<Code>.pdf` (SchuelerkartenServiceTests.cs:15) |
| Mails | Direktor-Variante gewinnt bei Doppelrolle (WillkommensMailTests.cs:259, :369); ungültige Adresse oder Fehler: kein Versand, keine Exception (:102-:125); Aktivierungsmail einmalig, Ablehnungsmail nur bei offener Anmeldung, Adressänderung an alt und neu (KontoBenachrichtigungServiceTests.cs:18, :47, :59) |
| Nicht getestet | Cockpit, Archivierung, Willkommensmail bei Direktionsanlage, Rollenwechsel beim Bearbeiten, Stundenplan-Raumkonflikt, Anzeige der Import-Zugangsdaten |

---

## 7. Schwächen und Verbesserungsvorschläge

### 7.1 Fehler (Bugs)

1. **Importierte Konten bekommen keine Zugangsdaten zu sehen.**
   - `CommitAsync` liefert `Zugangsdaten`, aber `CommitImportAsync` zeigt sie nie an (SLcs:1400-1439).
   - Der UI-Text verspricht das Gegenteil (SLr:701).
   - Folge: Alle importierten Lehrer und Schüler sind ohne einzelnes « Mot de passe » nicht nutzbar.
   - → Neubau: Zugangsliste nach dem Import (Anzeige und druckbares PDF, einmalig).
2. **« Modifier » eines gesperrten Schülers hebt die Sperre still auf.**
   - `SaveSchoolUserAsync` setzt für Schüler immer `IsBlocked=false` (DS:4180-4183), auch beim Bearbeiten.
   - Eine offene Selbstregistrierung mit Klasse wird dabei ohne Aktivierungsmail angenommen.
   - Ohne Klasse ist sie gar nicht bearbeitbar (« Attribuez une classe… », DS:1672-1678).
   - → Sperrstatus nie implizit ändern; Freigabe nur über eine eigene Aktion.
3. **Falsche Fehlermeldung bei « Valider » ohne Klasse:** « Un élève ayant un abonnement payé ne peut pas être bloqué ici. » (DS:3874-3878). Der echte Grund ist fehlende Klasse oder fehlendes Profil (DS:3907-3912).
4. **Klassenname über 20 Zeichen** führt zu einem Datenbankfehler: `Code = Title`, Code max. 20, Title erlaubt 200 (DBC:108, DS:2219, CourseFormModel).
5. **Eindeutiger Index auf `AspNetUsers.Email`** (DBC:111, Snapshot mit Filter `IS NOT NULL`) widerspricht „eine E-Mail für mehrere Konten“ (PRG:96, SLr:1001, Geschwister, Lehrkraft an zwei Schulen).
   - Auf SQL Server scheitert die zweite Anlage vermutlich mit einer DB-Exception; InMemory-Tests merken das nicht.
6. **Rollenwechsel im Bearbeitungsdialog** (z. B. Lehrer → Élève/Coordinateur) ist erlaubt. Alte Lehrerzuordnungen und Lehraufträge bleiben stehen (DS:1754-1770).
   - Bei Mehrfachrollen (Direktor = SchulLeiter + Lehrer) nimmt die Anzeige `GetRolesAsync().FirstOrDefault()`, das Ergebnis ist zufällig.
7. **Koordinator schließt Zyklen ohne automatische Rechnung** (SLcs:2656-2660), obwohl der Dialog das Gegenteil verspricht (SLr:1460). Finanzrelevant.
8. **Archivieren ist eine Einbahnstraße**: keine Wiederherstellung, keine Archivansicht; der Service prüft „ohne Klasse“ nicht selbst (DS:3822-3859). Archivierte Schüler können sich trotzdem weiter anmelden (UserSchool-Link bleibt, SZS:22-33), sehen aber nichts.
9. **Jahreszuordnung blockiert Klassenwechsel im laufenden Jahr**: « L'élève possède déjà une classe pour cette année scolaire. » (UVS:221-222). Es gibt keine Korrekturfunktion.
10. **Willkommensmail-Texte falsch:**
    - Der Koordinator und Eltern bekommen « Merci pour votre inscription… sera activé après validation » (AUTH:785, Default-Zweig).
    - Von der Direktion angelegte Schüler lesen „wird nach Validierung aktiviert“, obwohl sie sofort aktiv sind.
    - Die Mails werben mit dem veralteten « SKOULOUAPPABO ».
11. **Filter « Statut des cycles »** und mehrere Methoden wirken nicht (siehe 7.3). Der Filter steuert nur ungenutzte Listen.

### 7.2 Inkonsistenzen / fachliche Unschärfen

- **Klassenzugehörigkeit an 4 Stellen:** Profil, LevelEnrollment, Jahreszuordnung, Zyklusteilnahme.
  - Der Stundenplan des Schülers folgt der Jahreszuordnung, Karten und Lehrersicht folgen der aktuellen Klasse.
  - → Neubau: **eine** Einschreibung `(Schüler, Schuljahr, Klasse, von/bis, Status)` als Wahrheit; „aktuelle Klasse“ daraus ableiten.
- **Zwei Lehrer-Klassen-Zuordnungen** (LehrerNiveauZuordnung für Zugriff und Provision, UnterrichtsZuordnung für Jahr und Fach) und **zwei Wochenstunden-Quellen**, die nicht synchron sind.
  - → Ein Lehrauftrag `(Jahr, Klasse, Fach, Lehrer, Stunden)` als einzige Quelle; Sicht- und Erfassungsrechte daraus ableiten.
- **„Partage“ ist für die Direktion faktisch bedeutungslos:**
  - Sie liest **alle** Lehrerlektionen der Schule (TLS:129-160, 422-450) und kann für jeden Lehrer erfassen.
  - Der Reitertext « Seuls les contenus que leurs auteurs ont explicitement partagés… » stimmt so nicht.
  - → Entscheiden: entweder volle Transparenz (Flag streichen) oder echte Freigabe.
- **Der selbst registrierte Direktor hat zusätzlich die Rolle Lehrer** (AUTH:159). Folgen:
  - Er zählt im Cockpit als Enseignant, sodass « Ajoutez vos enseignants » nie erscheint.
  - Er steht in den Lehrerlisten (Erfassung, Lehraufträge).
- **Die « À valider »-Chip zählt alle gesperrten Schüler**, nicht nur offene Anmeldungen (SLcs:2041).
- **Anwesenheitsdatum** wird gegen das Schuljahr geprüft, nicht gegen die Periode (ANW:20).
- **Die Konfliktprüfung „Lehrer schulübergreifend“** (SPS:168-174) greift nie, weil eine Lehrkraft an zwei Schulen zwei Konten hat.
- **Eltern sehen weiter archivierte oder gesperrte Kinder** (ELS:187: kein `IsArchived`-Filter). Gleichzeitig kann die Direktion für gesperrte Schüler keinen Elternzugang anlegen (ELS:64).
- **Eltern haben keinen Stundenplan**; ein Elternkonto kann nicht gesperrt, bearbeitet oder gelöscht werden.
- **Die Auto-Matrikel `EL-<GUID>` (35 Zeichen)** landet auf Karte und QR. Sie ist nicht menschenlesbar, und der QR ist nicht prüfbar (kein Verifikationsendpunkt).
  - → Lesbares Schema je Schule (z. B. Jahr + laufende Nummer) und signierter QR-Link.
- **Coordonnées und Stammdaten der Schule auf zwei Seiten verteilt**: `/schulleiter` (Name, Ort) und `/direction/enseignement` (Dokumentdaten).
- **`UpdateSchoolForSchoolLeaderAsync` übernimmt `IsActive` aus dem Formular** (DS:1567). Eine Direktion kann damit theoretisch ihre eigene Schule deaktivieren.

### 7.3 Toter Code / Altlasten

- **SchulLeiter.razor.cs, nie gerendert:**
  - Zahlungsdialog (`OpenZahlungModalAsync`, `ZahlungBuchenAsync`),
  - `ShowKlassenFinanzberichtAsync`, `VersetzungsZielKlasseOptions`,
  - `DurchlaufKlasseOptions` und `GefilterteDurchlaeufe`,
  - `OpenEditSelectedSchoolAsync`, `IsReferenceIncoherent`, `TotalBlockedCount`.
- **`isProduktOwner`-Zweige** auf `/schulleiter` sind unerreichbar: Policy nur SL und Koord; Service-Rollen für ProduktOwner leer.
- **Auto-Abo** (`AutoAssignSkoulouAppAboOnCycleStart`): keine Checkbox in der UI, der Bestätigungsdialog ist also unerreichbar. Dazu « Gérer l'abonnement » und Altabo-Logik `LevelEnrollment.IsUnlocked/IsPaid`.
- **`IstSystemNiveau`** und globale Niveaus ohne Schule; `RecommendedLessonCount`; `CurrentLevelCode`-Fallbacks in Filtern.
- **Leere Löschmethoden** `DeleteSchoolKlasseAsync` und `DeleteKlassenStufeAsync`.
- **`SchuelerProfilService`** (In-Memory, nur registriert); `allowBlockingPaidStudent` ohne Wirkung; `GetLehrerFachZuordnungenAsync` und `GetGemeinsameLehrerSchulenAsync` ohne UI-Nutzung.
- **Code für Mehrschul-Logik** (`managedSchoolIds`, Schulfilter, `SchoolIds`-Listen) seit „ein Konto = eine Schule“.
- → Neubau ohne diese Pfade; `schoolId` kommt aus dem Mandantenkontext, nicht aus Listen.

### 7.4 Umständliche Abläufe (UX)

- **Selbstregistrierung freigeben** braucht zwei Reiter (Réattribution, dann Élèves › Valider).
  - → Ein Dialog « Valider l'inscription » mit Klassenwahl.
- **Klassenwechsel** = Retirer plus Zuweisen; Schulaustritt = Retirer plus Archivieren.
  - → Aktionen « Changer de classe » und « Départ de l'élève » (mit Datum und Grund).
- **Jahreszuordnungen nur einzeln.**
  - → « Inscrire toute la classe » bzw. Jahreswechsel-Assistent (Promotion/Redoublement als Massenaktion).
- **Anwesenheit Schüler für Schüler auf der Bulletin-Seite**, nur durch die Direktion.
  - → Klassen-Appell (Liste mit Standard „présent“), Erfassung auch durch die Lehrkraft der Stunde, Bezug zum Stundenplan.
- **Kopieren eines Stundenplans** nimmt Titel und Daten aus dem Formular « Nouveau planning ». Das PDF ist eine Liste statt Wochenraster und für die Direktion inklusive aller Entwürfe.
- **Import-Vorschau zeigt nur 20 Zeilen**; Fehler dahinter sind nur als Zähler sichtbar. Eine einzige fehlerhafte Zeile blockiert alles.
  - → Fehlerliste vollständig, als Datei exportierbar.
- **Teacher-Import ohne Fächer**; Lehrer bekommen 1 h je Klasse pauschal.
- **Zufallspasswort mit 28 Zeichen und Hex-Großbuchstaben**: für Kinder und Eltern auf Papier kaum abtippbar.
  - → Kurze, gut lesbare Startpasswörter (z. B. 3 Wörter oder 8 Zeichen ohne verwechselbare Zeichen) mit Pflicht zum Ändern beim ersten Login.
- **Einige Listen haben Suche und Paging, andere nicht** (Lehraufträge, Jahreszuordnungen).

### 7.5 Performance-Fallen

- **`LoadAsync` lädt nach jeder Aktion alles neu:** alle Schüler mit Scores und Gebühren, Nutzer, geteilte Inhalte, Klassen, Fächer (SLcs:1105-1145).
- **`GetSchoolUsersAsync`** lädt alle Konten der Schule **inklusive Schüler** und ruft pro Konto `GetRolesAsync` auf (N+1, DS:3973-4007). Schüler werden erst in der UI verworfen.
- **`GetLehrerNiveauZuordnungenAsync` und `SetLehrerNiveauZuordnungenAsync`** laden **alle Klassen aller Schulen** (DS:4717-4721, 4775-4778).
- **`LoadUserFormTeacherClassOptionsAsync`** fragt pro Schule einzeln ab; `ElternService.KinderAsync` stellt 3–4 Abfragen pro Kind.
- **Globaler App-Lock `SKOULOUAPP:Zyklusverwaltung` mit Serializable-Transaktion** für jede Kontoanlage, jeden Import, jede Sperre und Klassenzuweisung aller Schulen (ZyklusTransaktionsSchutz.cs). Ein großer Import blockiert die ganze Plattform.
  - → Sperren pro Schule; Import in Batches mit Idempotenzschlüssel.
- **Clientseitiges Paging** (`DataListPanel`) über vollständig geladene Listen.

### 7.6 Sicherheitslücken / -risiken

- **Service-Methoden ohne Akteurprüfung**, Schutz nur durch die Seitenpolicy:
  - `SaveUserAsync`, `SaveSchoolKlasseAsync`, `SetStudentBlockedStateAsync`,
  - `GetSchoolForEditAsync`, `GetSchoolKlassenAsync`, `GetKlassenStufenAsync`, `GetStudentLevelEnrollmentsAsync`,
  - `TeacherLessonService.GetLessonDetailForDirectionAsync` und `GetSharedContentForSchoolLeaderAsync` (keine Rollenprüfung).
- **`SaveSchoolKlasseForSchoolLeaderAsync` und `DeleteSchoolFachForDirectionAsync`** prüfen nur `CanManageStudents`. Dieses Flag haben auch **Lehrer** (DS:1764): Ein Lehrer könnte über einen anderen Einstieg Klassen anlegen und Fächer löschen.
  - → Neubau: eine zentrale Autorisierung „Akteur hat Fähigkeit X in Schule S“ in jedem Command; kein Lehrerflag `CanManageStudents`.
- **Kein Lockout bei Fehlversuchen** (`lockoutOnFailure: false`, AUTH:271). Kritisch bei schwachen, von Schülern selbst gewählten 6-Zeichen-Passwörtern.
- **Direktion und Koordination können Passwörter aller verwalteten Konten zurücksetzen und sehen.** Es gibt kein Protokoll, wer wann zurückgesetzt, gesperrt, archiviert, Rollen geändert oder Finanzrechte vergeben hat.
  - → Audit-Log für alle Kontoaktionen.
- **Ein Koordinator darf die Schulstammdaten ändern**, inklusive Name (DS:1531). Das ist vermutlich unbeabsichtigt.
- **Die Erfassung im Namen eines Lehrers braucht keine Benachrichtigung** des Lehrers.
  - → Mindestens ein Hinweis bzw. Verlauf für den Lehrer.

### 7.7 Weitere Vorschläge für den Neubau

- **Mandantenkontext:** die Schule aus Login und Konto als Mandanten-ID; alle Abfragen über einen globalen Filter.
- **Status-Enums statt Bool-Kombinationen:** Konto-Status `EnAttente | Actif | Bloque | Archive` statt `IsBlocked + IsSelfRegistered + IsArchived`.
- **Schuljahr als Pflichtkontext** der Direktion, mit Assistent „Nouvelle année scolaire“ (Klassen übernehmen, Lehraufträge kopieren, Schüler versetzen).
- **Benennung:** Klasse mit eigenem kurzem Code und langem Namen, oder nur ein Name mit vernünftiger Länge; Code nicht still mit dem Titel gleichsetzen.
- **Importvorlagen** als herunterladbare XLSX-Datei; auch für Eltern-Verknüpfungen (Kind-Matrikel ↔ Elternteil).

---

## 8. Offene Fragen

1. Soll ein Direktor automatisch auch Lehrer sein (Rolle Lehrer bei Selbstregistrierung)? Falls ja: zählt er im Cockpit, in Lehraufträgen und Provisionen mit?
2. Sollen Koordinatoren Schulstammdaten (Name, Ort) ändern dürfen? Sollen sie Zyklen schließen dürfen, mit automatischer Rechnung wie der Direktor?
3. Ist eine E-Mail für mehrere Konten gewollt (UI-Text), oder soll die E-Mail eindeutig sein (DB-Index)?
4. Brauchen wir Archivierung auch für Lehrer und Koordinatoren und eine Wiederherstellung (« Réintégrer ») für Schüler?
5. Wie soll ein Klassenwechsel im laufenden Schuljahr fachlich wirken (Noten, Anwesenheit, Bulletin, Zyklus)? Mit Datum?
6. Sollen Lehrkräfte Anwesenheit erfassen dürfen (Appell je Stunde)? Muss das Anwesenheitsdatum in der Periode liegen?
7. Was bedeutet « Partage avec la direction » künftig, wenn die Direktion ohnehin alles lesen und für jeden Lehrer erfassen kann?
8. Ist eine Übertragung von Inhalten zwischen Lehrern gewünscht, z. B. bei Lehrerwechsel? In der Alt-App fehlt sie völlig.
9. Matrikel-Schema: frei, je Schule fortlaufend, oder nach nationaler Vorgabe (Togo)? Soll der QR auf der Karte prüfbar sein?
10. Sollen Eltern den Stundenplan sehen? Sollen Eltern mit Kindern an zwei Schulen ein einziges Konto bekommen (Ausnahme von „ein Konto = eine Schule“)?
11. Bleibt „ein Konto = eine Schule“ für Lehrkräfte, die an mehreren Schulen unterrichten (Konfliktprüfung im Stundenplan)?
12. Welche Altlasten (Altabo `LevelEnrollment.IsUnlocked/IsPaid`, Auto-Abo, System-Niveaus, Provisionen je Wochenstunde) entfallen ersatzlos? Abstimmung mit Bereich Finanzen nötig.
13. Sollen Klassen, Klassenstufen und Fächer löschbar bzw. deaktivierbar sein, solange sie unbenutzt sind? Die Alt-App verbietet das Löschen von Klassen generell.
14. Welche Mails sollen bei Kontoanlage, Import, Passwort-Neusetzung und Elternanlage verschickt werden (mit welchem Text)? Wie gehen wir mit Konten ohne E-Mail um (SMS/WhatsApp)?
