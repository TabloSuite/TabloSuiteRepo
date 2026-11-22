# 04 – Noten, Bulletins & Schuljahreswechsel

Fachkonzept für den Neubau von SKOULOU, abgeleitet aus der alten App (Stand Quellcode 13.11.2026).
Maßgeblich ist der Code; die alten Dokumente (`docs/Stundenplan-und-Bulletin.md`, `SKOULOUAPP.WebApp/Projektuebersicht.md`) beschreiben teilweise Funktionen, die in der UI nicht mehr erreichbar sind (siehe Abschnitt 7).
UI-Texte stehen in « » exakt wie in der App.

**Pfadkürzel für Referenzen (`Kürzel:Zeile`)**

| Kürzel | Datei |
|---|---|
| BWS | `SKOULOUAPP.WebApp/Services/BewertungsService.cs` |
| NB | `SKOULOUAPP.Core/Services/NotenBerechnung.cs` |
| BUS | `SKOULOUAPP.WebApp/Services/BulletinService.cs` |
| KR | `SKOULOUAPP.WebApp/Services/BulletinService.Klassenrat.cs` |
| BAS | `SKOULOUAPP.WebApp/Services/BulletinAbschlussService.cs` |
| BSD | `SKOULOUAPP.WebApp/Services/BulletinStammdatenService.cs` |
| BPR | `SKOULOUAPP.WebApp/Services/BulletinPruefungService.cs` |
| BT | `SKOULOUAPP.WebApp/Services/BulletinTexte.cs` |
| SNAP | `SKOULOUAPP.WebApp/Services/BulletinSnapshot.cs` |
| SL | `SKOULOUAPP.WebApp/Services/BulletinStandardLayout.cs` |
| PDF | `SKOULOUAPP.WebApp/Services/BulletinPdfService.cs` |
| BB | `SKOULOUAPP.WebApp/Services/BulletinBeispiel.cs` |
| UVS | `SKOULOUAPP.WebApp/Services/UnterrichtsVerwaltungService.cs` |
| ANW | `SKOULOUAPP.WebApp/Services/AnwesenheitsService.cs` |
| UT | `SKOULOUAPP.WebApp/Services/UnterrichtTransaktion.cs` |
| COCK | `SKOULOUAPP.WebApp/Services/DirektionsCockpitService.cs` |
| ELT | `SKOULOUAPP.WebApp/Services/ElternService.cs` |
| EXS / FIN / DSH | `SKOULOUAPP.Core/Services/ExamService.cs` / `FinanzService.cs` / `DashboardService.cs` |
| CFG | `SKOULOUAPP.WebApp/Data/UnterrichtModellKonfiguration.cs` |
| PRG | `SKOULOUAPP.WebApp/Program.cs` |
| NV / BUL / BULA | `Components/Pages/NotenVerwaltung.razor(.cs)` / `Bulletins.razor(.cs)` / `Bulletins.Abschluss.cs` |
| KRE / AND | `Components/Shared/KlassenratErfassung.razor(.cs)` / `Components/Shared/AbschlussNotenDialog.razor` |
| UV / SLP | `Components/Pages/UnterrichtsVerwaltung.razor(.cs)` / `Components/Pages/SchulLeiter.razor(.cs)` |

---

## 1. Zweck & beteiligte Rollen

**Zweck.** Bewertungsperioden und Notenkomponenten je Schuljahr konfigurieren, Fachnoten erfassen (mit Historie), Mittelwerte/Ränge/Statistiken berechnen, den Conseil de classe erfassen, Bulletins als unveränderliche Snapshots klassenweise endgültig veröffentlichen, drucken (Einzel-/Sammel-PDF, QR-Prüfung), nachträglich berichtigen. Daneben existiert ein zweites, finanzgetriebenes „Jahres“-Modell: der **Klassenzyklus** (`KursNiveauDurchlauf`) mit „Notes finales“, Mentions und Abschluss. Schuljahr und Zyklus sind im alten Code **nicht verknüpft** (Kern-Schwäche, Abschnitt 7).

**Gemeinsame Zugangsprüfung (alle Services dieses Bereichs).** Rollen werden serverseitig aus der DB gelesen (nicht aus Claims). Zusätzlich:
- Schule nicht gesperrt (`SchulZugangService.IstSchuleFreigegebenAsync`), sonst Sperrhinweis – BWS:194, BUS:314, ANW (PruefenAsync).
- Konto hat `UserSchool` zu genau dieser Schule, Schule `IsActive`, Konto nicht `IsBlocked` – BWS:196, BUS:316.
- „Verwalter/Direktion“ = Rolle `SchulLeiter` **oder** `Koordinator` (BWS:190-191, BUS:310-311). Der Koordinator hat in Noten/Bulletins **identische Rechte** wie der Direktor; nur Zyklus starten/schließen und „Notes finales“ verlangen beim Koordinator das Schulrecht `CanManageCyclePayments` (« Paiements des cycles »; SLP:380-389, SLP Text 1132).
- Jede Operation öffnet einen eigenen DbContext; Schreibvorgänge laufen in **Serializable**-Transaktionen mit Retry (UT:18-19).

**Berechtigungsmatrix (wie im Code erzwungen)**

| Aktion | Direktor/Koord. | Lehrer | Schüler | Eltern | Anonym |
|---|---|---|---|---|---|
| Seite `/notes` | ja | ja (NV:2) | – | – | – |
| Perioden anlegen/schließen/öffnen, Komponenten, Fächer+Koeffizienten | ja (BWS:15, 25, 47, 70) | nein | – | – | – |
| Note erfassen/korrigieren | alle Fächer/Klassen der Schule | nur mit `UnterrichtsZuordnung` (Schule, Jahr, Klasse, Fach, Lehrer) und Rolle Lehrer (BWS:204-212) | – | – | – |
| Notenhistorie lesen | ja | wie Notenrecht (BWS:142) | – | – | – |
| Noten-Übersicht laden | alle `FachBewertung`en | nur zugeordnete Fächer (BWS:164-167) | – | – | – |
| `/bulletins`, Vorlagen, Vorschau, Conseil, Prüfung, Veröffentlichung, Berichtigung, Übergabe, Anwesenheit | ja | **kein Zugriff** (Seite BUL:2, Services DirektionAsync) | nur eigene Liste | – | – |
| Bulletin-PDF lesen | alle Ausgaben inkl. zurückgezogener | nein | eigene: veröffentlicht oder ersetzt (BUS:239-240, 262-265) | verknüpfte Kinder (`ElternKind`), gleiche Regel (BUS:262-264) | – |
| Sammel-PDF | ja (PRG:415) | nein | nein | nein | – |
| QR-Statusprüfung | – | – | – | – | ja, ohne Personendaten (PRG:376-388) |
| Schuljahr anlegen/schließen, Jahreszuordnung, Unterrichtszuordnung | ja (UVS:239-246) | nein | – | – | – |
| Zyklus starten/schließen, „Notes finales“ | Direktor; Koord. nur mit Zyklusrecht (SLP) | – | – | – | – |
| Mentions-Liste pflegen | ja (EXS:1643-1650, `verwaltung=true`) | nein | – | – | – |
| Mention einem Schüler zuweisen | ja | **ja** (Lehrer der Schule, EXS:1647) | – | – | – |

SuperAdmin/ProduktOwner haben ohne `UserSchool` zur Schule keinen Zugriff auf Noten/Bulletins.

---

## 2. Daten

### 2.1 Schuljahr und Zuordnungen
- **Schuljahr** (`Schuljahr`): `SchulID`, `Bezeichnung` (1–80 Zeichen, frei, z. B. « 2026–2027 », eindeutig je Schule – CFG:26), `Beginn`, `Ende` (DateOnly), `IstGeschlossen`. Identifiziert das Jahr; keine Jahreszahl-Logik.
- **SchuelerJahresZuordnung** („Inscription annuelle“): Schule, Schuljahr, Klasse, Schüler (AppUser), `IstWiederholer` („Redoublant“). Eindeutig (Schuljahr, Schüler) – CFG:40. Historischer Klassenbezug; alle Noten, Anwesenheiten, Conseil-Entscheidungen und Bulletins hängen an dieser Zuordnung, **nicht** am Schüler direkt.
- **UnterrichtsZuordnung** („Affectation“): Schule, Schuljahr, Klasse, Fach, Lehrer, `Wochenstunden` (0,01–168, nur Planung). Eindeutig (Jahr, Klasse, Fach, Lehrer) – CFG:31. Grundlage für Lehrer-Notenrecht, Lehrernamen auf dem Bulletin und Stundenplan.
- Nicht jahresgebunden, aber Voraussetzung: `LehrerNiveauZuordnung` (Lehrer↔Klasse), `LehrerFachZuordnung` (Lehrer↔Fach), `StudentProfile.CurrentKlasseId` (aktuelle Klasse), `Fach.Gruppe` (« Lettres »/« Sciences »/« Autres », DSH:4297), `Fach.Reihenfolge` (kein Editor vorhanden).

### 2.2 Bewertung
- **BewertungsPeriode**: Schule, Schuljahr, `Titel` (≤120, eindeutig je Jahr – CFG:59), `Art`, `Reihenfolge` (≥1), `JahresGewicht` (decimal 7,2), `IstGeschlossen`.
- **BewertungsArt**: `Allgemein` (UI « Bulletin général »; auch für Semester verwendet), `Trimester` (« Trimestre », Reihenfolge 1–3), `Test` (« Examen blanc / test », zählt nie zur Jahresnote).
- **BewertungsBestandteil** („Composante“): Periode, `Bezeichnung` (≤80, eindeutig je Periode – CFG:63), `Gewicht` (>0–100), `MaximalPunkte` („Barème“, >0–10000). **Gilt für alle Klassen und alle Fächer der Periode** (schulweit).
- **FachBewertung** („Matière“ einer Klasse im Jahr): Schuljahr, Klasse, Fach, `Koeffizient` (>0–100). Eindeutig (Jahr, Klasse, Fach) – CFG:66.
- **FachNote**: Bestandteil, FachBewertung, JahresZuordnung, `Punkte` (decimal 7,2), `Bemerkung` (≤300, „Appréciation“), `BearbeitetVonID`. Eindeutig (Bestandteil, FachBewertung, JahresZuordnung) – CFG:74.
- **NotenAenderung** (nur anhängende Historie): FachNote, Bearbeiter-ID, eingefrorener `BearbeiterName`, `AltePunkte` (null = Ersterfassung), `NeuePunkte`, alte/neue Bemerkung, Zeitstempel. FK Restrict schützt Noten mit Historie vor Löschung.
- **AnwesenheitsEintrag**: Periode, JahresZuordnung, `Datum`, `Status` (`Anwesend`/`Abwesend`/`Verspaetet` = « Présent »/« Absent »/« En retard »), `IstEntschuldigt`, `VerspaetungsMinuten` (0–1440), `Bemerkung` (≤300). Eindeutig (Periode, Zuordnung, Datum) – CFG:101.

### 2.3 Conseil de classe
- **KlassenratEntscheidung** je (Periode, JahresZuordnung) eindeutig (CFG:95): `Distinktion?` (null = Vorschlag aus Moyenne), `Sanktionen` (Flags), `Bemerkung` (« Appréciation du conseil », ≤300), `Entscheidung` (Jahresentscheidung), Bearbeiter.
- `KlassenratDistinktion`: Keine, TableauHonneur, Encouragements, Felicitations.
- `KlassenratSanktion` [Flags]: AvertissementTravail, AvertissementConduite, BlameTravail, BlameConduite.
- `Jahresentscheidung`: Offen (« Non concernée / à décider »), Admis, Redouble, Exclu. **Rein dokumentarisch** – steuert keine Versetzung.

### 2.4 Bulletin
- **BulletinVorlage** je (Schule, Klasse, Art) eindeutig (CFG:104): Name (≤120), `AkzentFarbe` (#RRGGBB), Schalter `ZeigeKoeffizienten`, `ZeigeRang`, `ZeigeVorperioden`, `ZeigeUnterschriften` (Visa), `ZeigeLehrkraefte`, `ZeigeAnwesenheit`, `ZeigeTeilnoten`, `ZeigePunkte` (Moy×Coef + Totaux), `ZeigeFachRang`, `ZeigeAppreciation`, `ZeigeSignaturSpalte`, `ZeigeFachGruppen`, `ZeigeKlassenStatistik`, `ZeigeMentionen`, `ZeigeJahresBilanz`, `ZeigeFoto`, `ZeigeNoteInWorten`; `SchriftGroesse` 8–12, `SeitenRand` 15–50, `FussText` ≤300, `LogoAssetID`, `BlockReihenfolge` (nur Altlayout), `SiegelAssetID` (Legacy, wird immer null gesetzt – BUS:77).
- **BulletinAusgabe** („Édition“): Schule, Periode, JahresZuordnung, `Version` (fortlaufend je Periode+Zuordnung, eindeutig – CFG:117), `IstVeroeffentlicht` (max. eine aktive je Periode+Zuordnung, gefilterter Unique-Index – CFG:118), `IstEndgueltig`, `PruefToken` (64 Hex, eindeutig – CFG:108), `ErsetztAusgabeID`/`ErsetztDurchID` (Kette Original↔Rectificatif), `SnapshotJson`, `AenderungsGrund`, `RuecknahmeGrund`, `ZurueckgezogenVonID/AmUtc`, `ErstelltVonID`.
- Abgeleitete Status (BUL:238): « Publié », « Publié · Définitif », « Retiré », « Remplacé — original archivé ».
- **BulletinAbschluss**: (Periode, Klasse) eindeutig (CFG:110) = „Klasse für diese Periode endgültig veröffentlicht“. Zentrale Sperrmarke.
- **BulletinBerichtigung** („Rectification“): Schule, Jahr, Klasse, `Grund` (≤300), `EntwurfJson` (Liste NoteID/Punkte/Bemerkung), `StammdatenEntwurfJson` (Name, Matricule, Geburtsort, Geburtsdatum je Zuordnung), `VorschauHash`, `FreigegebenAmUtc/VonID`, `AbgebrochenAmUtc`. Zustände: Entwurf → geprüft (Hash) → freigegeben | abgebrochen.
- **BulletinUebergabe**: Ausgabe, Bearbeiter, `IstAbholung` (false = « Notification », true = « Remise »), `Vermerk` (1–300).
- **BulletinSnapshot** (SNAP:19, JSON in `SnapshotJson`, eingefroren): Schulkopf (offizielle Bezeichnung/Name, Slogan, Adresse, Republik, Ministerium, Motto, Zulassung, Kontakt Tel·Mail, Direction régionale, Statut, Ort), Schuljahr-Bezeichnung, Periodentitel, Klassencode, Schüler (Name, Matricule, Geburtsort, Geburtsdatum dd/MM/yyyy, Sexe M/F, Nationalität, Interne, Affecté, Redoublant, Foto-Bytes), Fächer (Name, Lehrer, Coef, Moyenne, Gruppe, Teilnoten/20, Rang, Appréciation, Jahres-Moyenne, Jahresrang), Moyenne, Rang, Effectif, Vorperioden, Jahresnote/-rang, Klassenmittel/Min/Max, Distinction-Text, Sanktions-Texte, Rats-Bemerkung, Entscheidungstext, Observation, Anwesenheitssummen (erfasste Tage, Fehltage, entschuldigt, Verspätungen, Minuten), alle Layoutoptionen inkl. Logo-Bytes, Dokumentnummer/-datum/PruefToken/ersetzte Nummer. Snapshots ohne `Layout` werden im Altlayout gedruckt (PDF:19-22).

### 2.5 Zyklus-Modell (Finanz-„Jahr“ je Klasse)
- **KursNiveauDurchlauf** („Cycle“): Schule, Klasse, `NiveauPreis` (Schulgebühr FCFA, >0), `PlattformPreisProSchueler` (beim Start eingefroren; null = historischer Zyklus), `StartDatum`, `EndDatum` (null = aktiv). Max. ein aktiver Zyklus je Schule+Klasse. **Kein Bezug zu `Schuljahr`.**
- **KlassenAbschlussEntscheidung** je (Zyklus, Schüler): `AbschlussNoteText` (frei, ≤64, z. B. « 12/20 »), Legacy `AbschlussNote`, `MentionText` (Text-Snapshot), `IsBestanden` (null = offen, « Réussi »/« Non réussi »), Entscheider/Zeit.
- **AbschlussMention**: Schule, `Text` (1–80, eindeutig je Schule). Katalog « Mentions de fin de cycle ».
- `LevelEnrollment` (Zyklusteilnahme, `IstAktuell`) – Details im Finanzkonzept.

---

## 3. Geschäftsregeln

### 3.1 Schuljahr
1. Anlegen: Bezeichnung getrimmt 1–80 Zeichen, `Ende > Beginn`, kein gleichnamiges und **kein zeitlich überlappendes** Jahr der Schule (auch geschlossene) – UVS:158-162. UI-Vorgabe 1.9. lfd. Jahr bis 31.7. Folgejahr (UV.cs:71-72).
2. Schließen: nur Flag `IstGeschlossen = true`, mit Bestätigungs-Checkbox (UV:92-93); **irreversibel** (keine Öffnen-Funktion), keine Vorbedingungen (offene Perioden, fehlende Bulletins egal), keine Versetzung, keine Archivierung – UVS:56-64.
3. Geschlossenes Jahr sperrt: Periode anlegen, Fach/Koeffizient, Komponenten, Noten, Perioden schließen/öffnen (BWS:16, 181, 187), Unterrichtszuordnung anlegen/entfernen, Jahreszuordnung (UVS:172, 201, 212), Anwesenheit (ANW:20). **Nicht gesperrt:** Conseil de classe, Prüfung/endgültige Veröffentlichung, Berichtigungen.
4. „Aktuelles Jahr“ ist uneinheitlich definiert: `/notes` und `/bulletins` wählen das Jahr mit spätestem Beginn (auch geschlossen; NV.cs:83-85, BUL.cs:146); `/direction/enseignement` das erste offene (UV.cs:147-148); Cockpit das offene Jahr mit `Beginn ≤ heute ≤ Ende` (COCK:68-72), die Elternanwesenheit jedes Jahr mit `Beginn ≤ heute ≤ Ende`, auch geschlossen (ELT:200).

### 3.2 Jahreszuordnung (Inscription annuelle)
1. Nur offenes Jahr; Schüler muss aktives (nicht archiviertes) Profil der Schule haben **und `CurrentKlasseId` = gewählte Klasse** (UVS:215-218).
2. Pro Jahr genau eine Klasse; ein Klassenwechsel innerhalb des Jahres wird abgewiesen (« L'élève possède déjà une classe pour cette année scolaire. », UVS:221-222). Erneutes Speichern ändert nur `IstWiederholer`.
3. Nach `BulletinAbschluss` irgendeiner Periode dieses Jahres für die Klasse gesperrt (UVS:213-214).
4. Keine Lösch-, keine Sammel-, keine Übernahmefunktion; jeder Schüler wird einzeln eingeschrieben.

### 3.3 Perioden
1. Anlegen (Verwalter, offenes Jahr): Titel 1–120, Reihenfolge ≥1, Trimester nur 1–3, `JahresGewicht` 0–100 mit max. 2 Dezimalen, für Nicht-Test >0; Test erhält **immer 0** (BWS:27-30, 35).
2. Eindeutig: Titel je Jahr; bei Trimester zusätzlich die Reihenfolge (BWS:31-33). Für `Allgemein` sind doppelte Reihenfolgen erlaubt.
3. Schließen/Öffnen per Umschalter (« Clôturer et verrouiller la période » / « Rouvrir la période pour correction »), nur Verwalter, nur in offenem Jahr (BWS:10-20). Veröffentlichte Bulletins bleiben auch nach Wiederöffnung geschützt.
4. Keine Bearbeitung/Löschung von Perioden.

### 3.4 Komponenten (Bestandteile)
1. Nur in offener Periode eines offenen Jahres; Bezeichnung 1–80, eindeutig je Periode; Gewicht >0–100, Barème >0–10000, je max. 2 Dezimalen (BWS:48-52).
2. **Strukturverriegelung:** sobald irgendeine Note der Periode existiert (egal welche Klasse/Fach) – « La structure de la période ne peut plus changer après la saisie des notes. » (BWS:53-54) – oder irgendeine Klasse der Periode endgültig veröffentlicht ist (BWS:55-56).
3. Keine Bearbeitung/Löschung.

### 3.5 Fächer und Koeffizienten
1. Klasse und Fach müssen zur Schule gehören; Koeffizient >0–100, max. 2 Dezimalen (BWS:72-75). Anlegen oder Ändern (Upsert).
2. Koeffizient **gesperrt ab der ersten Note** dieses Fachs (ganzes Jahr, alle Perioden) – BWS:82-83.
3. Weder Anlegen noch Ändern, solange irgendein Bulletin des Jahres dieser Klasse veröffentlicht ist (BWS:85-87).
4. Ein Fach ohne `FachBewertung` ist für Lehrer unsichtbar und erscheint nicht auf dem Bulletin – auch wenn eine Unterrichtszuordnung existiert.

### 3.6 Notenerfassung
1. Periode offen, Jahr offen (BWS:100, 178-184).
2. Komponente, Fach und Zuordnung müssen zu derselben Schule, demselben Jahr und derselben Klasse gehören (BWS:103-105).
3. Recht: Verwalter oder Lehrer mit passender Unterrichtszuordnung (BWS:204-212).
4. Gesperrt, wenn Klasse/Periode endgültig veröffentlicht (« … Préparez une rectification dans les bulletins. », BWS:107-108) oder für diesen Schüler/Periode eine Ausgabe veröffentlicht ist (BWS:109-111).
5. `0 ≤ Punkte ≤ Barème`, max. 2 Dezimalen; Bemerkung ≤300, leer → null, getrimmt (BWS:112-118).
6. Identisches Speichern (gleiche Punkte und Bemerkung) ist ein No-op ohne Historie (BWS:119); sonst Upsert + `NotenAenderung` in derselben Transaktion (BWS:129-131).
7. Erfassung ist **eine Note pro Klick** (Klasse·Fach → Komponente → Élève → Note → « Enregistrer la note »), kein Raster (NV:63-73).
8. Es gibt keine Kennzeichnung „absent“, „dispensé“ oder „non noté“; eine fehlende Note ist schlicht nicht vorhanden.

### 3.7 Berechnungsformeln (NB)
- **Fachmittel einer Periode** (NB:6-13): über **alle** Komponenten der Periode; fehlt eine einzige Note → `null` („Notes incomplètes“); keine Komponenten → `null`.
  `Moy_Fach = Σ(Punkte/Barème · 20 · Gewicht) / Σ Gewicht`
- **Gesamtmittel (Moyenne générale)** (NB:15-21): über alle Fächer (`FachBewertung`) der Klasse; ein `null`-Fach → `null`.
  `Moy = Σ(Moy_Fach · Coef) / Σ Coef`
- **Jahresmittel** (NB:23-29): Perioden mit `IstTest` oder Gewicht 0 werden ignoriert; fehlt eine Periodenmoyenne → `null`.
  **Neubau (Nutzer 22.11.2026):** Perioden, in denen der Schüler nicht in der Schule eingeschrieben war (Eintritt im Lauf des
  Jahres), zählen für ihn nicht; Jahresmittel und Jahresrang aus seinen übrigen Perioden (Bauplan §5 Regel 3).
  `Moy_an = Σ(Moy_Periode · JahresGewicht) / Σ JahresGewicht`; analog je Fach (Fach-Jahresmittel, KR:66-67).
- **Teilnote** auf /20 umgerechnet: `Punkte · 20 / Barème` (KR:30-32).
- Fehlende Noten zählen **nie** als 0 (Test `NotenBerechnungTests`).
- Ungültige Eingaben (Barème ≤0, Gewicht ≤0, Punkte außerhalb, Note >20, Coef ≤0) werfen `ArgumentOutOfRange`.

### 3.7a « Matières facultatives » (Neubau, 19.11.2026 – gab es in der Alt-App nicht)
Vom Nutzer freigegeben für Togo (Culture, Ewe …); Schulen ohne Wahlfächer (z. B. Sprachschulen) merken nichts davon. Formeln: Bauplan §5 Regel 2a.
1. **Einstellung je Klassenfach** (Direktion, Classes › « Matières et enseignants »): Schalter « Matière facultative » und Pflichtauswahl « Compte dans la moyenne »: « Seuls les points au-dessus de 10 comptent » (Standard), « Compte comme une matière normale », « Ne compte pas (information seulement) ». Wird mit Koeffizienten kopiert, auf das Niveau übertragen und beim Jahreswechsel übernommen.
2. **Wahl je Schüler fürs ganze Schuljahr** (Reiter « Matières facultatives »): Häkchen Schüler × Wahlfach, « Tous » / « Aucun », « Enregistrer ». Nur Schüler der Klasse und nur Wahlfächer der Klasse; im abgeschlossenen Jahr nur lesen. Klassenwechsel überträgt die Wahl auf dasselbe Wahlfach der neuen Klasse.
3. **Notenerfassung:** im Wahlfach erscheinen nur die Wählenden (Hinweis « Matière facultative »); für andere wird weder Note noch Appréciation angenommen, und sie fehlen nie in « Notes incomplètes ». Cockpit-Vollständigkeit zählt im Wahlfach nur die Wählenden.
4. **Rechnen:** Bonus-Regel: (Fachmittel − 10) × Koeff., nur positiv, Koeffizient nicht in der Summe; Moyenne höchstens 20. Beispiel: 64 Punkte / 5 Koeff. = 12,80; Ewe 16 (coef 2) → (64 + 12) / 5 = 15,20; Ewe 8 → 12,80. Fachrang und Fachstatistik nur über die Wählenden.
5. **Bulletin:** nicht gewählte Wahlfächer erscheinen beim Schüler gar nicht; gewählte stehen im Bereich « MATIÈRES FACULTATIVES » mit kurzer Regel (« points au-dessus de 10 ajoutés » …; bei verschiedenen Regeln unter jedem Fach), Bonus in « MOY × COEF » als « +12,00 », « TOTAUX » mit Bonus ohne dessen Koeffizienten. Gruppenköpfe (« MATIÈRES LITTÉRAIRES », « MATIÈRES SCIENTIFIQUES », « AUTRES MATIÈRES ») und Gruppen-Bilan nur mit der Vorlage « Groupes de matières » (`ZeigeFachGruppen`, Standard aus), nur über Pflichtfächer. Ohne Wahlfächer bleiben Snapshot-JSON und Hash unverändert (kein neues Feld).
6. **Eltern (Live-Noten):** nur gewählte Wahlfächer, mit Hinweis auf die Regel; Moyenne nach derselben Regel.
7. **Ergänzt 19.11.2026 (Nutzer « ja für alle »):**
   - *Vorhandene Noten:* wird ein Fach erst nachträglich facultative, bleiben alle Noten gespeichert; sie zählen nur für Wählende
     und erscheinen wieder, sobald der Schüler gewählt wird. Der Dialog nennt die Zahl (`KlassenfachZeile.NotenOhneWahl`).
   - *Sperre nach der Veröffentlichung:* sobald für die Klasse ein Bulletin veröffentlicht ist, sind Schalter und Regel fest
     (Struktur – wie Bewertungen, auch in einer Rectification). Die Wahl eines Schülers mit gültigem Bulletin in dieser Klasse
     (nicht zurückgezogen) ist fest, außer während einer offenen Rectification der Klasse (wie Noten). Neu angekommene Schüler
     ohne Bulletin wählen immer. Gleicher Stand speichert ohne Fehler.
   - *Jahreswechsel:* die Wahl des Vorjahres wird übernommen, wenn dasselbe Fach in der neuen Klasse auch Wahlfach ist
     (`UnterrichtService.FachwahlenVorjahrUebernehmenAsync`, ein Durchgang); sonst entfällt sie. Die Direktion ändert nur die Ausnahmen.

### 3.8 Rundung und Anzeige
- Intern wird **ungerundet** gerechnet und gespeichert (Snapshot enthält volle decimal-Werte).
- Anzeige: `Round(x, 2, AwayFromZero)`, Format `0.00` fr-FR (Komma) – NB:31, BT:64-65.
- Moy×Coef = ungerundete Moyenne × Coef, dann gerundet angezeigt (SL:152).
- „En toutes lettres“: gerundeter Wert, z. B. 11,02 → « onze virgule zéro deux » (BT:68-78).
- Schwellen (Appréciation, Distinction) vergleichen den **ungerundeten** Wert.

### 3.9 Ränge
- Wettbewerbsrang auf den **gerundeten** Werten: `Rang = 1 + Anzahl Mitschüler mit strikt höherer gerundeter Moyenne` → ex-aequo erhalten denselben Rang, der nächste wird übersprungen (1, 1, 3). Anzeige « 1er », « 2e », … bzw. « — » (BT:62).
- **Rang der Moyenne générale (Bulletin):** nur wenn **alle** Schüler der Klasse eine vollständige Moyenne haben, sonst `null` (BUS:111-112; Altlayout zeigt « Non calculable (notes manquantes) »).
- **Fachrang, Jahresrang, Fach-Jahresrang und Rang im Conseil:** Schüler ohne Wert werden einfach ignoriert (KR:70-71) – abweichende Regel.
- Kohorte = alle Jahreszuordnungen der Klasse im Jahr; « Effectif » = deren Anzahl (BUS:109, 132).

### 3.10 Klassenstatistik, Appréciation, Mentions
- « Résultats de la classe »: Mittel, Max, Min der vollständigen Schüler-Moyennes (KR:160, 191-193).
- **Appréciation je Fach** (KR:156): neueste nicht-leere Lehrer-Bemerkung irgendeiner Komponente des Fachs in der Periode; sonst automatisch (BT:12-23): ≥18 « Excellent », ≥16 « Très bien », ≥14 « Bien », ≥12 « Assez bien », ≥10 « Passable », ≥8 « Insuffisant », ≥5 « Faible », sonst « Très faible ».
- **Distinction** (BT:26-40): eigener Eintrag des Conseils, sonst Vorschlag aus der Periodenmoyenne: ≥16 « Tableau d'honneur + Félicitations », ≥14 « Tableau d'honneur + Encouragements », ≥12 « Tableau d'honneur », sonst keine (KR:170).
- **Sanctions**: Kombination aus « Avertissement travail », « Avertissement conduite », « Blâme travail », « Blâme conduite » (BT:42-51).
- **Décision**: « Admis(e) en classe supérieure », « Redouble la classe », « Exclu(e) »; nur auf dem Jahresabschluss-Bulletin gedruckt (KR:197).
- Fachgruppen-Bilan: je Gruppe `Σ(Moy·Coef)/Σ Coef` über Fächer mit Note; Reihenfolge Lettres, Sciences, sonstige, Autres, ohne Gruppe (SL:194-222). « TOTAUX » = Σ Coef und Σ Moy×Coef, nur bei angezeigten Koeffizienten (SL:188-189).
- Fachreihenfolge im Bulletin: `Fach.Reihenfolge`, dann Name (KR:144-145); Teilnoten-Spalten nur bei >1 Komponente, in Anlagereihenfolge (KR:43, 190).

### 3.11 Jahresbilanz („Bilan de fin d'année“)
- Jahresperioden = alle Perioden **derselben Art** wie die aktuelle, ohne Tests, nach Reihenfolge (KR:49-50).
- Jahresabschluss liegt vor, wenn die aktuelle Periode die letzte ist und: bei Trimester alle drei Reihenfolgen 1–3 existieren und es die 3. ist; bei anderen Arten mindestens zwei Perioden existieren (KR:53-61).
- Dann: Jahresmittel, Jahresrang, Fach-Jahresmittel/-rang, Décision. Die „Vorperioden“ (gleiche Art, kleinere Reihenfolge) erscheinen im Bilan statt im Moyenne-Kasten (SL:262-263, 309-328).
- Titelzeile/Periodenwort werden aus dem **Titeltext** abgeleitet („semestre“ → « BULLETIN SEMESTRIEL DE NOTES », „trimestre“ → « … TRIMESTRIEL … », sonst « BULLETIN DE NOTES ») – SL:92-98, 330-334.

### 3.12 Anwesenheit (Bulletin-relevant)
- Nur Direktion/Koordination; offene Periode, offenes Jahr, Datum innerhalb des Schuljahres; ein Eintrag je Tag/Periode/Schüler, neue Erfassung ersetzt (ANW:10-33).
- `IstEntschuldigt` nur bei Status ≠ Anwesend; Minuten nur bei Verspätung (ANW:28-29).
- Gesperrt nach Veröffentlichung für den Schüler (ANW:24-25) und nach `BulletinAbschluss` (ANW:17-19).
- Nicht erfasste Tage gelten nicht als anwesend; Bulletin zeigt Summen der Periode (Absences, davon justifiées/non justifiées, Retards mit Minuten) oder « Présences non renseignées ».

### 3.13 Vorlagen
- Je Schule+Klasse+Art eine Vorlage; Validierung: Name 1–120, Farbe `^#[0-9a-fA-F]{6}$`, Schrift 8–12, Rand 15–50, Fußtext ≤300, Blockreihenfolge = Permutation der 5 Blöcke `Schueler,Noten,Ergebnisse,Bemerkung,Unterschriften` (BUS:48-52, 303-308).
- Logo nur aus der Bildbibliothek der eigenen Schule (BUS:73-76); Upload PNG/JPEG ≤2 MiB (BUL.cs:53-63).
- Mehrfachzuweisung: dieselben Einstellungen als **eigene Kopie** je Zielklasse und Art; Zielklassen der Schule, keine Systemniveaus; bestehende Vorlagen nur mit ausdrücklichem `ueberschreiben`, sonst wird gar nichts geändert (BUS:27-44).
- Vorlagenänderungen wirken nur auf künftige Snapshots.
- Vorschau mit fiktivem Schüler (Togo-Beispiel) und echtem Schulkopf (BB:8, 47); fehlendes Logo wird dort toleriert.

### 3.14 Snapshot-Erzeugung (Vorschau/Veröffentlichung)
- Nur Direktion; Observation ≤1000 Zeichen; Periode der Schule; Zuordnung zu Schule und Jahr der Periode; **Vorlage für Klasse+Art Pflicht** (« Créez d'abord un modèle … ») – BUS:90-100.
- Logo wird als Bytes eingebettet (PNG/JPEG, ≤2 MB); fehlt das referenzierte Logo, **scheitert** der Snapshot (BUS:143-162). Foto-Fehler werden toleriert (KR:203-207).
- Lehrer je Fach = alle Lehrer der Unterrichtszuordnungen (Klasse, Fach, Jahr), kommagetrennt (BUS:113-119).
- Klassenrat-Daten werden zum Erstellungszeitpunkt übernommen; spätere Änderungen wirken nicht auf bestehende Ausgaben.

### 3.15 Veröffentlichung
**Endgültige Klassenveröffentlichung (einziger UI-Weg):**
1. « Vérifier toute la classe avant publication »: Kohorte 1–200 Schüler, noch kein `BulletinAbschluss`; für jeden Schüler Snapshot; jeder muss vollständig sein (« Notes incomplètes : {Schüler}. Complétez toute la classe avant de publier. »); max. 32 MB JSON; Ergebnis = SHA-256 über alle Snapshots (BAS:20-51).
2. Bestätigungs-Checkbox Pflicht; beim Veröffentlichen wird neu gerechnet und der Hash muss identisch sein, sonst « Les données ont changé. Vérifiez à nouveau toute la classe. » (BAS:59-61).
3. Je Schüler neue Ausgabe: `IstEndgueltig`, `Version = max+1`, Grund « Publication définitive », Nummer `B-{GUID ohne Bindestriche}`, Datum, 64-Hex-Prüftoken; eine eventuell vorher aktive (vorläufige) Ausgabe wird entveröffentlicht und als ersetzt verknüpft (BAS:63-87). Observation wird von der vorher aktiven Ausgabe übernommen, sonst leer (BAS:42-43).
4. `BulletinAbschluss(Periode, Klasse)` wird angelegt (BAS:73). Danach: kein Rückzug (BUS:194), keine Notenänderung, keine Anwesenheit, keine neuen Komponenten der Periode, keine Jahreszuordnung der Klasse; nur noch Berichtigung.

**Vorläufige Einzelveröffentlichung** (Service vorhanden und getestet, **in der UI nicht verdrahtet**): Grund 1–300 Pflicht; kein `BulletinAbschluss`; vollständige Noten; keine andere aktive Ausgabe; Version +1 (BUS:164-185). Rückzug mit Grund (1–300), nur nicht-endgültige, nur einmal; speichert Grund/Bearbeiter/Zeit getrennt vom Veröffentlichungsgrund (BUS:187-204).

### 3.16 Berichtigung (Rectification)
1. Start: Direktion, Grund 1–300, Klasse+Periode müssen endgültig veröffentlicht sein; je Klasse+Jahr nur ein offener Entwurf (BAS:89-101).
2. Notenkorrektur in den Entwurf: beliebige bestehende Note der Klasse/des Jahres (auch anderer Perioden), Barème- und 2-Dezimalen-Regel; ändert Originalnoten **nicht** (BAS:117-129). Neue Noten können nicht ergänzt werden.
3. Identitätskorrektur: Name 1–200, Matricule ≤50, Geburtsort ≤150, keine Steuerzeichen, Geburtsdatum nicht in der Zukunft; Schüler der Klasse/des Jahres mit mindestens einer veröffentlichten Ausgabe; vorbefüllt aus Entwurf oder neuester veröffentlichter Ausgabe; ändert **nicht** das Profil (BSD:13-59).
4. Jede Entwurfsänderung löscht den `VorschauHash`.
5. « Calculer tous les bulletins concernés »: für jede veröffentlichte Ausgabe der Klasse/des Jahres Neuberechnung mit Entwurfsnoten; übernommen werden **nur** Fächer, Moyenne, Rang, Vorperioden, Jahresnote (BAS:153-154) plus Identität; betroffen ist eine Ausgabe, wenn ihre Note direkt geändert wurde oder sich das JSON unterscheidet (Ränge von Mitschülern, Folgeperioden). Leer → Fehler. Max. 200 Ausgaben/32 MB. Hash = Originalnoten + Entwürfe + Wirkungen (BAS:131-168).
6. Freigabe: Checkbox + Hash muss sowohl dem neu berechneten als auch dem gespeicherten Prüf-Hash entsprechen (BAS:182); schreibt Noten + `NotenAenderung`; Originale entveröffentlicht und mit `ErsetztDurchID` verknüpft; neue endgültige Ausgaben mit neuer Nummer, neuem QR und Vermerk « BULLETIN RECTIFICATIF — remplace le n° … » (BAS:184-205, SL:85-86).
7. Abbrechen jederzeit; Originale und Noten unverändert.

### 3.17 Übergabe-/Kontaktprotokoll
« Consigner une notification effectuée » / « Consigner la remise du document signé » mit Pflichtvermerk (Empfänger und Kontaktart), nur Protokoll, **kein Versand** (BAS:211-226, BUL:246-255).

### 3.18 Öffentliche Prüfung (QR)
Token exakt 64 Hex, sonst „nicht gefunden“ (BPR:13). Ausgabe: offizielle Schulbezeichnung, Nummer, Datum (UTC), Status « Valide » / « Non valide » / « Remplacé par un bulletin rectificatif », « Remplace » / « Remplacé par » (BPR:11-23). Keine Namen/Noten. `no-store`, `noindex`, CSP `default-src 'none'`, 30 Anfragen/min/IP (PRG:237-240, 376-388). QR nur bei endgültigen Ausgaben; ohne gültiges `App:BaseUrl` schlägt die PDF-Erzeugung fehl (PDF:106-119).

### 3.19 PDF
- QuestPDF, A4. Allgemeines Layout (SL): dreispaltiger Kopf (Ministère/Direction/Logo · Titel+Schule · République/Motto/Année/Code/Statut), Schülerblock, Notentabelle (DISCIPLINES, Teilnoten, MOY./20, COEF, MOY×COEF, RANG, MOY. ANN., RANG ANN., APPRÉCIATION, PROFESSEUR, SIGNATURE), Kästen ASSIDUITÉ / MOYENNE … / RÉSULTATS DE LA CLASSE, MENTIONS DU CONSEIL DE CLASSE (Ankreuzfelder), APPRÉCIATION DU CONSEIL DE CLASSE (Rats-Bemerkung + Observation), VISA DU CHEF D'ÉTABLISSEMENT (« Fait à {Ort}, le {Datum} », Platz für echte Unterschrift + Stempel), ggf. BILAN DE FIN D'ANNÉE / DÉCISION DE FIN D'ANNÉE / LE PROFESSEUR PRINCIPAL; Fußtext, QR, Seitenzahl.
- Layout klemmt Rand auf 15–40 und Schrift auf (Größe−1) 7–10 (SL:24-25). Blockreihenfolge wird im allgemeinen Layout **ignoriert**.
- Sammel-PDF: nur aktuell veröffentlichte Ausgaben der Klasse+Periode, höchstens eine je Schüler, 1–200 Bulletins, ≤32 MB Snapshotdaten, Sortierung Name (fr-FR, case-insensitive) dann Matricule; jedes Bulletin neue Seite, eigene Seitenzählung; keine Neuberechnung (BUS:269-301, PDF:12-17).
- Vorschau in der UI als Data-URL-PDF mit Nummer « BROUILLON — NON VALIDE » und ohne QR (BULA:102).

### 3.20 Sperrmatrix (was blockiert was)

| Ereignis ↓ / gesperrt → | Note | Koeff./Fach | Komponente | Anwesenheit | Jahreszuordnung | Rückzug | Conseil |
|---|---|---|---|---|---|---|---|
| Periode geschlossen | ja | – | ja | ja | – | – | nein |
| Schuljahr geschlossen | ja | ja | ja | ja | ja | – | nein |
| Erste Note im Fach | – | Koeff. ja | – | – | – | – | – |
| Erste Note in Periode (beliebige Klasse) | – | – | ja | – | – | – | – |
| Ausgabe des Schülers veröffentlicht | ja (Schüler/Periode) | ja (ganze Klasse, ganzes Jahr) | – | ja | – | – | nein |
| `BulletinAbschluss` (Periode, Klasse) | ja | (über veröff.) | ja (ganze Periode) | ja | ja (Klasse/Jahr) | ja | nein |

### 3.21 Zyklusabschluss, Notes finales, Versetzung
1. **Zyklusstart**: Schulgebühr >0, nur Schulklassen, max. ein aktiver Zyklus; Plattformpreis eingefroren; bei historischem Vorzyklus Sperre bis Überweisungsmeldung (FIN:744-818, 728-742).
2. **Notes finales** (Dialog « Notes finales et décisions », Eyebrow « Clôture du cycle »): je Teilnehmer Freitext-Note (≤64), Décision « À renseigner »/« Réussi »/« Non réussi », Mention (« Aucune mention » oder Katalog; archivierte Texte werden als « … (archivée) » angezeigt). Gespeichert werden nur vollständige Zeilen (Note + Entscheidung); nur bei aktivem Zyklus (AND:149-187, EXS:1652-1697, 1254-1292, 1616-1641).
3. Teilnehmer aktiver Zyklus = Profile mit aktueller Klasse; geschlossener Zyklus = Schüler mit Entscheidung, sonst historische Einschreibungen (EXS:1387-1419).
4. Mentions-Katalog: Umbenennen/Löschen ändert bereits vergebene `MentionText` nicht (Snapshot).
5. **Zyklusabschluss** (« Clôturer le cycle »): Enddatum ≥ Startdatum; fehlende Notes finales blockieren nicht (nur Hinweis « … non définies »); neue Zyklen: Teilnehmer ergänzen, automatische Abrechnungsperiode (volle Plattformforderung) + Ausgabebuchung je Teilnehmer; alle aktuellen Einschreibungen `IstAktuell=false`; **neue Zyklen: Schüler behalten Klasse, Lektionen/Exams bleiben veröffentlicht**; historische Zyklen: Klasse wird geleert, Lektionen/Exams entveröffentlicht; offene Schulgebühren werden als Verlust « Verlust Schulgebühr » gebucht (FIN:864-1051). Danach keine Zahlungen, Notes finales oder Mentions mehr.
6. **Versetzung**: Es gibt **keine automatische Promotion**. `ExamService.VersetzeSchuelerAsync` (nur nach Zyklusende, nur `IsBestanden=true`, Zielklasse derselben Schule; setzt aktuelle Klasse, ggf. Einschreibung) ist nur in Tests aufgerufen (EXS:1298-1385). Praktischer Weg: « Retirer de la classe » (aktuelle Klasse leeren, DSH:3460-3509) → Reiter « Réattribution » (Schüler ohne aktive Klasse, filterbar nach früherer Klasse, Mehrfachauswahl) → Zielklasse (DSH:3376-3458); alternativ Klassenfeld im Benutzerformular.

---

## 4. Abläufe

### 4.1 Jahresvorbereitung (Direktion/Koordination)
1. `/direction/enseignement`: « Coordonnées officielles de l'école » (Kopf des Bulletins), « Matricules et état civil des élèves » (Matricule, Geburtsort, Nationalité, Interne, Affecté), « Années scolaires » → « Créer l'année scolaire ».
2. Voraussetzung im Personalbereich: Lehrer ↔ Klasse und Lehrer ↔ Fach. Dann « Affecter un enseignant » je Klasse/Fach/Lehrer/Wochenstunden für das gewählte Jahr.
3. « Inscriptions annuelles et redoublement »: jeden Schüler einzeln in seiner aktuellen Klasse einschreiben, ggf. « Redoublant pour cette année scolaire ».
4. `/schulleiter` (Klassen): Zyklus je Klasse starten (Schulgebühr, Startdatum); Fächergruppe je Fach setzen.
5. `/notes`: « Nouvelle période » (z. B. 3 Trimester mit Gewicht, optional « Examen blanc / test »), je Periode « Composantes de la moyenne » **vor der ersten Note** vollständig anlegen, « Matières et coefficients » je Klasse+Fach.
6. `/bulletins`: « Modèles par classe » je Klasse und Typ anlegen (ggf. « Appliquer ce modèle à plusieurs classes »), Logo hochladen, Vorschau « Aperçu avec des données d'exemple ».

### 4.2 Noten erfassen (laufend)
Lehrer/Direktion: `/notes` → Jahr → Periode → « Classe et matière » → « Composante » → « Élève » → Note (+ « Appréciation (facultatif) ») → « Enregistrer la note ». Unten je Schüler « Moyenne : xx,xx / 20 » oder « Notes incomplètes » für Fach+Periode. « Historique de la note sélectionnée » zeigt Änderungen (UTC, Bearbeiter, alt → neu). Direktion erfasst Anwesenheit unter `/bulletins` › « Présences et retards ».

### 4.3 Conseil de classe
`/bulletins` › « Conseil de classe » → Periode + Klasse → « Afficher la classe »: je Schüler Moyenne, Rang, Distinction (« Selon la moyenne (…) » oder manuell), « Décision de fin d'année », Sanctions-Checkboxen, « Appréciation du conseil » → « Enregistrer les décisions du conseil ». Muss **vor** der Veröffentlichung erfolgen, da Snapshots einfrieren.

### 4.4 Endgültige Veröffentlichung
1. Optional Periode schließen (« Clôturer et verrouiller la période »).
2. `/bulletins` › oben Jahr + Periode wählen › « Téléchargement groupé des bulletins » › « Classe à vérifier / exporter » › « Vérifier toute la classe avant publication ».
3. Je Schüler « Aperçu » prüfen (Entwurf-PDF), Checkbox « Je confirme avoir vérifié tous les bulletins … » › « Publier définitivement les bulletins de la classe ».
4. Schüler/Eltern sehen die Ausgabe sofort; Cockpit zählt sie.

### 4.5 Drucken und Verteilen
« Télécharger les bulletins publiés (PDF) » (Klasse+Periode) oder Einzel-PDF je Ausgabe; Unterschrift/Stempel auf Papier; « Contacts et remise » protokolliert Benachrichtigung bzw. Übergabe.

### 4.6 Berichtigung
« Rectification après publication » › Motiv in « Motif de l'édition / du retrait » › « Ouvrir une rectification motivée » › Identität (« Rectifier l'identité d'un élève » › « Enregistrer l'identité dans le brouillon ») und/oder « Note à rectifier » › « Enregistrer dans le brouillon » › « Calculer tous les bulletins concernés » (Liste alt→neu Moyenne/Rang, Vorschau) › Checkbox › « Publier définitivement les rectifications » › Familien informieren, Übergaben protokollieren. « Abandonner le brouillon » verwirft.

### 4.7 Jahresende (wie die alte App es tatsächlich erlaubt)
1. Letzte Periode (z. B. 3e trimestre): Conseil inkl. Décision, endgültige Veröffentlichung → Jahresbilanz erscheint automatisch.
2. Je Klasse « Notes finales » im Zyklus (optional) und « Clôturer le cycle » (Rechnung, Verlustbuchung offener Gebühren).
3. « Archives et clôture de l'année » › Bestätigung › « Clôturer l'année sélectionnée ».
4. Versetzen: Schüler aus der Klasse nehmen und über « Réattribution » neuen Klassen zuordnen; Redoublants bleiben; Exclu(e)s separat archivieren/sperren (eigene Funktion, nicht mit der Décision verknüpft).

### 4.8 Neues Schuljahr – was übernommen wird

| Übernommen (nicht jahresgebunden) | Neu anzulegen |
|---|---|
| Konten, Schülerprofile (Matricule, Geburtsort, Nationalité, Interne/Affecté), aktuelle Klasse | Schuljahr (nicht überlappend) |
| Eltern-Verknüpfungen (`ElternKind`) | Jahreszuordnung jedes Schülers (einzeln, Redoublant manuell) |
| Klassen, Fächer, Fächergruppen, Lehrer↔Klasse/Fach | Unterrichtszuordnungen (Lehrer je Klasse/Fach) |
| Bulletin-Vorlagen (je Klasse+Typ), Logo, Mentions-Katalog | Perioden, Komponenten, Fächer+Koeffizienten |
| Schuldaten für Dokumente | Zyklen je Klasse (neue Schulgebühr, neuer eingefrorener Plattformpreis) |
| Alte Noten, Bulletins, Conseil, Anwesenheit (historisch, an alter Jahreszuordnung) | Stundenpläne (Kopierfunktion existiert im Stundenplanmodul) |

---

## 5. Seiten, Routen, Endpunkte

| Route | Rollen | Inhalt |
|---|---|---|
| `/notes` « Notes et évaluations » (NV) | SchulLeiter, Koordinator, Lehrer | Dropdowns « École », « Année scolaire »; Hinweis ohne Jahr mit Link « Créer l'année scolaire » (nur Direktion); Direktion: « Nouvelle période » (Titre, Type, Ordre, Poids; Hinweis « Un examen blanc / test ne compte jamais dans la moyenne annuelle. »), « Matières et coefficients » (Classe, Matière, Coefficient), Periodenumschalter, « Composantes de la moyenne » (Intitulé, Poids, Barème); alle: « Saisie et correction », « Historique de la note sélectionnée », Ergebnisliste je Schüler. |
| `/bulletins` « Bulletins scolaires » (BUL) | SchulLeiter, Koordinator, Schüler | Direktion: « Modèles par classe » (Klasse, Typ, Name, Couleur, Logo-Auswahl/-Import/« Retirer du modèle », Gruppen « Tableau des notes » und « Résultats et décisions » mit 17 Schaltern, Taille du texte, Marges A4, Texte de bas de page, « Enregistrer le modèle », Beispielvorschau, Mehrfachzuweisung mit Suche/« Sélectionner les résultats »/« Tout désélectionner »/Ersetzbestätigung, Liste « Modèles enregistrés » › « Modifier / copier »); « Conseil de classe »; « Préparer et publier un bulletin » (Année, Période, Élève, Observation, Motif, nur « Prévisualiser »); « Téléchargement groupé des bulletins » (Klassenprüfung, endgültige Veröffentlichung, Sammel-PDF); « Rectification après publication »; « Présences et retards »; Vorschau (iframe + HTML-Kurzfassung). Alle: « Éditions enregistrées » mit Suche (Élève/Période), Filter Jahr/Periode/Klasse (nur Direktion), 50 je Seite « Afficher plus », PDF, « Retirer la publication » (nur nicht-endgültig), « Contacts et remise ». |
| `/direction/enseignement` « Organisation des enseignements » (UV) | SchulLeiter, Koordinator | Coordonnées officielles, Matricules (« Attribuer les matricules manquants de l'école »), Années scolaires (nur offene im Dropdown), Inscriptions annuelles (Liste mit Suche), « Archives et clôture de l'année », « Affecter un enseignant », « Enseignements de l'année sélectionnée » (« Retirer »). |
| `/schulleiter` (SLP) | Policy RequireSchulLeiter | Klassenkarten: « Notes finales », « Clôturer le cycle », « Démarrer le cycle »; « Mentions de fin de cycle »; Reiter « Réattribution »; Fächergruppe je Fach. |
| `/finanzen` | SchulLeiter, Koordinator, SuperAdmin, ProduktOwner | Gleicher Dialog « Notes finales » und Zyklus starten/schließen. |
| `/direction` Cockpit (COCK) | SchulLeiter, Koordinator | Untertitel « Année scolaire … » oder « Aucune année scolaire en cours »; « n bulletin(s) publié(s) cette année » (veröffentlicht, nicht ersetzt, Perioden des laufenden Jahres – COCK:80-82); « n cycle(s) en cours »; Anwesenheit heute. |
| `/parents` « Espace parents » | Eltern | Je Kind veröffentlichte Bulletins (« Aucun bulletin publié pour le moment. »), Abwesenheiten des laufenden Jahres, Schulgebühren (ELT:177-228). |
| `GET /bulletins/{ausgabeID}/pdf` | SchulLeiter, Koordinator, Schüler, Eltern | Einzel-PDF aus Snapshot; 403 bei fehlendem Recht (PRG:390-400). |
| `GET /bulletins/sammelausgabe/{schulID}/{periodeID}/{klasseID}/pdf` | SchulLeiter, Koordinator | Sammel-PDF; `no-store, private`; Fachfehler als 400 Text (PRG:402-415). |
| `GET /verification-bulletin/{token}` | anonym | HTML-Statusseite (3.18). |

---

## 6. Durch Tests festgelegte Regeln

**`NotenBerechnungTests`**: Barème-Normalisierung und Gewichtung (5/10 Gew. 1 + 35/40 Gew. 2 = 15); fehlende Note → null, echte 0 → 0; Gesamtmittel mit null-Fach → null; Jahresnote ignoriert Tests auch bei Gewicht 100; unvollständige Jahresperioden → null; Punkte > Barème und Koeffizient 0 werfen.

**`BulletinStandardLayoutTests`**: Beispielbulletin (1er und 3e trimestre) ergibt genau **eine A4-Seite**; Zahl in Worten (11,02/14,5/17,75/8); Distinction-Vorschlag 16,5 → Félicitations, 14 → Encouragements, 12,4 → Tableau d'honneur, 11,99 → keine.

**`BewertungsServiceTests`**:
- Rollen Direktor, Koordinator, zugeordneter Lehrer dürfen Noten erfassen; `BearbeitetVonID` = letzter Bearbeiter. Lehrer ohne Unterrichtszuordnung: Speichern verweigert, Übersicht ohne Fächer; fremde Schule → Unauthorized.
- Note > Barème und geschlossene Periode werden abgewiesen; Periode schließen nur Verwalter, Wiederöffnen erlaubt, im geschlossenen Jahr nicht; geschlossene Periode sperrt Anwesenheit.
- Mehr als 2 Dezimalen bei Periodengewicht, Komponentengewicht, Koeffizient abgewiesen; Test-Periode erhält Gewicht 0; Trimester-Reihenfolge 4 abgewiesen.
- Notenhistorie: Ersterfassung mit `AltePunkte=null`, identisches Speichern (auch mit Leerzeichen) erzeugt keinen Eintrag; Lehrer verliert Historienzugriff ohne Zuordnung.
- Jahresnote erst mit drei Trimestern und vollständigen Noten; Tests (auch Note 0, Gewicht 100) ignoriert; Test-Bulletin ohne Jahresnote.
- Allgemeines Bulletin: Teilnoten je Komponente, Fachgruppe, Fachrang, automatische Appréciation (16 → « Très bien »), Conseil-Werte (Distinction, Sanction, Bemerkung) im Snapshot, Klassenmittel; Lehrer darf Conseil nicht speichern; Vorschlag 16 → Félicitations ohne Eintrag.
- Snapshot bleibt nach Notenänderung unverändert; Rückzug speichert Grund/Bearbeiter/Zeit, zweiter Rückzug abgewiesen; zurückgezogene Ausgabe für Schüler unsichtbar; Neuauflage Version 2; Lehrer kann Bulletin nicht lesen; Notenänderung bei veröffentlichtem Bulletin gesperrt.
- Unvollständige Noten verhindern Veröffentlichung; Vorlage nur für Klassen der Schule, Lehrer darf keine Vorlage speichern; ungültige Blockreihenfolge, fremdes Logo, Schriftgröße 7/99 abgewiesen; Layoutwerte im Snapshot, alte Snapshots erhalten Standardwerte 10/25/true/true.
- Mehrfachzuweisung: Lehrer verboten, leere Auswahl und fremde Klasse abgewiesen (atomar, nichts geändert), Duplikate entfernt, getrennte Kopien, Überschreiben nur explizit.
- Klassenprüfung verlangt Vollständigkeit; nach Notenänderung ist der alte Hash ungültig; ohne Bestätigung keine Veröffentlichung.
- Endgültig: Lehrer darf nicht prüfen; QR-Status « Valide », ungültiger Token → null; Rückzug und Notenänderung verboten; Berichtigung durch Koordinator; Original bleibt (Moyenne 10), Rectificatif 15 mit Verweis auf Originalnummer; Original-QR « Remplacé par un bulletin rectificatif »; Übergaben protokolliert, Schüler darf Übergaben nicht lesen; PDF mit `App:BaseUrl` erzeugbar.
- Berichtigung erfasst Rangänderung des Mitschülers (1 → 2) und Folgeperiode (Vorperiodenwert 18); geänderter Entwurf nach Prüfung macht Hash ungültig; Abbruch lässt Noten unverändert.
- Identitätsberichtigung: Lehrer verboten, fremde Zuordnung/leerer Name/Zukunftsdatum abgewiesen; unveränderter Entwurf → „keine Wirkung“-Fehler; nur Ausgaben des betroffenen Schülers (2 Perioden) ersetzt, Mitschüler unverändert, Originale unverändert, Profilname unverändert, keine neue Notenhistorie.
- Sammelausgabe: nur veröffentlichte Snapshots der Klasse+Periode (andere Klasse/Periode ausgeschlossen); Lehrer/Schüler/fremde Schule verboten; nach Rückzug leer → Fehler; gesperrter Verwalter verboten; Sammel-PDF: je Schüler eigene Seite, 0 oder 201 Bulletins abgewiesen.
- Anwesenheit: Lehrer verboten, Datum außerhalb des Jahres abgewiesen, Entschuldigung wird gespeichert, Minuten nur bei Verspätung, ein Eintrag je Tag; Summen im Snapshot; nach Veröffentlichung gesperrt.

**`UnterrichtsVerwaltungTests`**: Jahr schließen nur Verwalter, danach keine Unterrichtszuordnung; leere Bezeichnung, Ende < Beginn, Überlappung abgewiesen; Unterrichtszuordnung verlangt Lehrer↔Fach-Zuordnung, positive Stunden, offenes Jahr; Jahreszuordnung idempotent mit Wiederholerstatus; Matricule-Regeln (Großschreibung, eindeutig auch gegenüber archivierten, automatische `EL-<GUID>`-Nummer stabil).

**`UnterrichtSqlTests`** (nur mit `SKOULOU_SQL_TESTS=1`): parallele Notenänderungen ergeben konsistente Historienkette; parallele Veröffentlichung → genau eine aktive Ausgabe; Berichtigungen (Note, Identität) mit SQL; Restrict-FKs verhindern Löschung von Schülern/Noten mit Historie.

**`ElternServiceTests`**: Eltern sehen nur verknüpfte Kinder ihrer Schule; Bulletin-PDF für eigenes Kind ja, fremder Elternteil nein.

**`ExamServiceTests` / `FinanzServiceTests` / `PromotionTeilnahmeTests` / `DashboardServiceTests`** (Zyklus): Mentions schulgebunden, Text-Snapshot bleibt nach Umbenennen/Löschen, nach Zyklusende gesperrt; Zyklusende erlaubt fehlende Notes finales und **bewahrt die Klasse** (neue Zyklen), danach Note/Entscheidung abgewiesen; offene Schulgebühr wird gemeldet; veröffentlichte Lektionen/Exams bleiben; neuer Zyklus ignoriert versetzte Vorjahresschüler; geschlossener Zyklus behält Entscheidungskohorte nach Promotion; Versetzung erfasst laufenden Zielzyklus zum eingefrorenen Preis, idempotent, ohne Zahlungsfiktion; Schülerdashboard zeigt Abschlussnoten wiederholter Zyklen; Entfernen aus Klasse nur ohne aktives/bezahltes Altabo.

---

## 7. Schwächen und Verbesserungsvorschläge

### 7.1 Fachliche Fehler / Inkonsistenzen
1. **Zwei unverbundene „Jahres“-Modelle:** Schuljahr (Noten/Bulletins) und Zyklus (Gebühren, Notes finales, Mentions, Réussi) ohne Fremdschlüssel; doppelte Entscheidungslogik (`Jahresentscheidung` Admis/Redouble/Exclu vs. `IsBestanden` + Freitextnote + Mention). → Im Neubau **ein** Schuljahr je Schule als Klammer; Zyklus/Gebührenperiode referenziert das Schuljahr; eine einzige Jahresentscheidung je Schüler.
2. **Décision ohne Wirkung:** « Admis » / « Redouble » / « Exclu » setzt weder nächste Klasse noch `IstWiederholer` noch Archivierung. → Assistent „Passage en classe supérieure“: Vorschlag aus Décision, Zielklasse aus Klassenstufen-Folge, Redoublant automatisch, Exclus archivieren, alles in einer Transaktion mit Vorschau.
   **Neubau (Nutzer 22.11.2026):** die Vorschau warnt mit Zahlen, wenn eine Zielklasse über ihre Kapazität käme (Bestand im neuen
   Jahr + Zugänge), sperrt aber nicht.
3. **Uneinheitliche Rangregeln:** Moyenne-Rang `null`, sobald ein Mitschüler unvollständig ist (BUS:111), Fach-/Jahres-/Conseil-Rang ignorieren Unvollständige (KR:70). → Eine Regel (z. B. „Non classé“ für Unvollständige, Rang über Klassifizierte), ex-aequo explizit („ex“).
4. **Berichtigung lässt Werte veralten:** nur Fächer, Moyenne, Rang, Vorperioden, Jahresnote werden übernommen (BAS:153-154); Klassenmittel/Min/Max, Jahresrang, Fach-Jahresränge, vorgeschlagene Distinction bleiben alt. → Snapshot vollständig neu berechnen, nur Layout/Identität beibehalten.
5. **Berichtigung umgeht Sperren:** Entwurfsnoten dürfen aus beliebigen Perioden kommen (auch offene/nicht veröffentlichte), ignorieren geschlossene Perioden/Jahre; betroffene vorläufige Ausgaben anderer Perioden werden als `IstEndgueltig` ersetzt, ohne `BulletinAbschluss`. → Berichtigung auf endgültig veröffentlichte Perioden beschränken.
6. **Komponenten schulweit je Periode:** jedes Fach jeder Klasse braucht eine Note in **jeder** Komponente, sonst ist der Schüler unvollständig und die Klasse nicht veröffentlichbar; Struktur friert nach der ersten Note irgendeiner Klasse ein. → Komponenten je Periode als Vorlage, überschreibbar je Klasse/Fach; Status „absent/dispensé/non évalué“ mit definierter Behandlung.
7. **Keine Korrektur von Konfigurationsfehlern:** Perioden, Komponenten, Fächer, Jahreszuordnungen nicht bearbeit-/löschbar; Klassenwechsel im Jahr unmöglich; Jahresabschluss irreversibel. → Bearbeiten solange ungenutzt, Versionierung danach; Klassenwechsel mit Datum (Zuordnung mit Gültigkeit); Wiedereröffnung mit Begründung.
8. **Conseil-Erfassung ungeschützt:** keine Prüfung auf Klassenzugehörigkeit, geschlossene Periode, endgültige Veröffentlichung (KR:98-128); Änderungen nach Veröffentlichung divergieren still vom Dokument. → Conseil mit der Veröffentlichung sperren, Teil des Prüf-Hashs.
9. **Observation praktisch tot:** Feld « Observation » wird nur in der Vorschau verwendet; bei endgültiger Veröffentlichung wird die Observation einer früheren Ausgabe übernommen, sonst leer (BAS:42-44).
10. **Semester nur über Titeltext/Typ „Allgemein“:** Jahresabschluss für `Allgemein` bei ≥2 Perioden, doppelte Reihenfolgen möglich, Titelzeile per String-Suche. → Explizite Periodenstruktur (Trimestre/Semestre/Période libre) und explizites Kennzeichen „letzte Periode“.
11. Distinction/Appréciation-Schwellen auf ungerundetem Wert (11,996 zeigt 12,00, aber keine Distinction).
12. Logo gelöscht → jeder Snapshot der Vorlage scheitert (BUS:146-147), Foto dagegen tolerant.
13. Visa-Datum vorläufiger Ausgaben = Druckzeitpunkt (SL:300).
14. Zyklus-Dialoge widersprechen sich: « Pour les nouveaux cycles, les élèves restent dans leur classe » vs. Endbestätigung « tous les élèves quitteront la classe active » (SLP:1459, 1481). `GetAbschlussNotenStatusAsync` zählt nur fehlende Notentexte, Text spricht von « note finale ou décision » (FIN:704-722).
15. Eltern-Liste zeigt nur aktive Ausgaben, `LesenAsync` erlaubt Eltern/Schülern aber auch ersetzte Originale (ELT:191-195 vs. BUS:264).
16. Standardlayout ignoriert `BlockReihenfolge` und klemmt Rand auf 40 statt 50.
17. `Fach.Reihenfolge` ohne Editor (immer 0 → alphabetisch).

### 7.2 Toter / irreführender Code
- `PublierAsync` (BUL.cs:209) ohne Button; Einzelveröffentlichung/Rückzug nur aus Altdaten erreichbar, UI-Feld « Motif de l'édition / du retrait » dient faktisch nur der Berichtigung.
- `VerschiebeBlock`, `BlockTitel` (BUL.cs:97-105); `SiegelAssetID`, `Siegel` im Snapshot; Altlayout `BisherigesDokument`.
- `ExamService.VersetzeSchuelerAsync` ohne UI; `AbschlussNote` (Legacy decimal); Abschluss-Exam-Funktionen (`GetAbschlussExamErgebnisseAsync`, `SetManuelleAbschlussExamNoteAsync`) ohne Bezug zu Bulletins.
- Alte Doku beschreibt Einzelveröffentlichung als Normalweg – veraltet.

### 7.3 UX
- Noteneingabe Feld für Feld statt Klassenraster (Schüler × Komponenten) mit Tastaturnavigation, Import (Excel) und Fortschrittsanzeige.
- Jahreszuordnung, Unterrichtszuordnung, Perioden/Koeffizienten jedes Jahr einzeln von Hand; keine „Année suivante préparer“-Kopie.
- Viele Bereiche auf einer Seite (`/bulletins` mit 7 Sektionen), Filter oben gelten implizit für unten (Klasse aus Sammelbereich für Liste und Berichtigung).
- Kein Lehrerzugang zu Klassenübersicht/Bulletins (Professeur principal), keine Notenfreigabe-Workflow (Lehrer meldet „fertig“).
- Keine Benachrichtigung an Familien (nur Protokoll).

### 7.4 Performance-Fallen
- `BewertungsService.LadenAsync` lädt **alle** Noten der Schule für das Jahr; `SnapshotAsync` ruft es **je Schüler** erneut auf; Klassenprüfung = N Komplettladungen (BAS:40-49).
- `BulletinRechner` sucht Noten per `SingleOrDefault` linear in der Gesamtliste; Ränge rechnen für jeden Schüler alle Mitschüler neu (O(N²·Fächer·Perioden·Komponenten·Noten)).
- Klassenprüfung (rein lesend) läuft in einer Serializable-Transaktion (BAS:53-54) → Sperren auf großen Tabellen; Hash über JSON inkl. Logo-Bytes; Berichtigungsprüfung berechnet jeden betroffenen Snapshot neu.
- Snapshots duplizieren Logo/Foto-Bytes je Ausgabe (DB-Wachstum).
→ Klassenweise Berechnung einmal laden (Dictionary-Lookups), Ergebnisse je Klasse/Periode materialisieren, Bilder per Referenz auf unveränderliche, versionierte Assets.

### 7.5 Sicherheitslücken
- `ExamService.SetManuelleAbschlussNoteAsync`, `SetKlassenBestandenEntscheidungAsync`, `VersetzeSchuelerAsync`, `GetKlassenAbschlussUebersichtAsync` prüfen **weder Benutzer noch Rolle noch Schule** (EXS:1127-1385, 1652-1697) – Schutz nur über die aufrufende Seite.
- Rollenprüfungen in BWS/BUS sind schulunabhängig (`UserRoles`) und koppeln nur über `UserSchool`; korrekt nur unter „ein Konto = eine Schule“.
- Einzel-PDF-Endpunkt fängt `InvalidOperationException` (fehlende `App:BaseUrl`, kaputter Snapshot) nicht → 500 (PRG:390-400).
- `KlassenratErfassung` fängt nur `InvalidOperationException`; `UnauthorizedAccessException` bricht den Circuit (KRE.cs:76).
- Dokumentnummer = GUID: nicht erratbar, aber für Papierverwaltung unbrauchbar; keine fortlaufende Registernummer.
→ Im Neubau: jede Service-Methode mit (Benutzer, Schule, Rolle, Ressource gehört zur Schule) absichern; zentrale Autorisierungsschicht; Endpunkte mit einheitlicher Fehlerabbildung.

### 7.6 Empfehlungen für das Datenmodell im Neubau
1. `Schuljahr` als zentrale Klammer: Perioden, Klassenbelegung (mit Gültigkeitsdatum), Lehrerzuordnungen, Gebührenperiode/Zyklus, Bulletins, Entscheidungen.
2. `Klassenstufe` mit Reihenfolge/Nachfolger für Versetzungsvorschläge.
3. Bewertungsschema als versionierte Vorlage (Periodenstruktur, Komponenten, Koeffizienten je Stufe) mit „aus Vorjahr übernehmen“.
4. Noten mit Status (`noté`, `absent justifié`, `absent`, `dispensé`) und definierter Wirkung auf Mittel/Rang.
5. Einheitliche Rang-/Rundungsregel als konfigurierbare Schulregel (Dezimalen, Rundungsart, ex-aequo-Anzeige, „Non classé“).
6. Veröffentlichung klassenweise mit Freigabestatus je Fach (Lehrer) → Conseil → Veröffentlichung; Snapshot inkl. Conseil im Hash.
7. Eine Jahresentscheidung (Passage/Redoublement/Exclusion/Orientation) mit Ausführung im Jahreswechsel-Assistenten.

---

## 8. Offene Fragen
1. Soll der Klassenzyklus (Gebühren) im Neubau mit dem Schuljahr zusammenfallen, oder braucht es unterjährige Zyklen (z. B. Nachhilfe, Kurse)?
2. Welche Periodenstrukturen sind Pflicht: 3 Trimestres, 2 Semestres, frei? Unterschiedliche Struktur je Klassenstufe (Primaire vs. Secondaire)?
3. Jahresmittel: einfache/gewichtete Mittel der Periodenmoyennes (aktuell) oder Mittel der Fach-Jahresmittel? Gewichte je Land (z. B. T3 doppelt)?
4. Rundung: 2 Dezimalen „au plus proche“ oder Abschneiden? Rang auf gerundeter Moyenne beibehalten? Anzeige „ex“ bei Gleichstand?
5. Wie sollen fehlende Noten, Abwesenheit bei Compo, Dispense (EPS) und „Non classé“ behandelt werden?
6. Soll eine Mindestmoyenne (z. B. 10/20) einen Versetzungsvorschlag erzeugen; wer entscheidet endgültig (Conseil vs. Direktion)?
7. Sollen Lehrer (Professeur principal) Bulletins ihrer Klasse sehen bzw. den Conseil vorbereiten dürfen?
8. Brauchen Schulen eine fortlaufende, lesbare Bulletin-Nummer (Register) zusätzlich zum QR-Token?
9. Sollen Komponenten (Interrogation, Devoir, Composition) je Fach/Klasse unterschiedlich sein dürfen?
10. Soll die Notenkonduite (« Conduite ») als Fach oder als eigener Wert geführt werden (Muster enthält sie als Fach)?
11. Wie werden Klassenwechsel und Neuzugänge während des Jahres in Mittel, Rang und Effectif behandelt?
12. Sollen « Examen blanc / test »-Bulletins weiterhin eigene Vorlagen und Veröffentlichungen haben, und sollen Exams der Lernplattform je in Noten einfließen dürfen (bisher bewusst getrennt)?
13. Benachrichtigung der Familien bei Veröffentlichung/Berichtigung: automatisch (SMS/WhatsApp/Mail) oder weiterhin nur Protokoll?
14. Soll ein geschlossenes Schuljahr wieder geöffnet werden können (mit Begründung, nur Direktor)?
