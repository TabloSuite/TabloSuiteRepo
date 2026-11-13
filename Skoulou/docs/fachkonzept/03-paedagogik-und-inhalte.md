# 03 – Pädagogik & Lerninhalte (Fachkonzept aus der Alt-App)

Quelle: Alt-App `SKOULOUAPP.*`, Stand 13.11.2026. Das Dokument beschreibt, **was** die Alt-App fachlich tut, und markiert, was im Neubau anders werden sollte. Französische UI-Texte stehen wörtlich in « » bzw. "…".

**Kürzel für Fundstellen** (Pfade relativ zum Repo-Root):

| Kürzel | Datei |
|---|---|
| LS | `SKOULOUAPP.Core/Services/LearningService.cs` |
| AMS | `SKOULOUAPP.Core/Services/AnswerMatchingService.cs` |
| TLS | `SKOULOUAPP.Core/Services/TeacherLessonService.cs` |
| ES | `SKOULOUAPP.Core/Services/ExamService.cs` |
| DS | `SKOULOUAPP.Core/Services/DashboardService.cs` |
| DES | `SKOULOUAPP.Core/Services/DirektionsErfassungService.cs` |
| SZS | `SKOULOUAPP.Core/Services/SchuelerZugangService.cs` |
| HS | `SKOULOUAPP.Core/Services/Text/HtmlSanitizer.cs` |
| ABS | `SKOULOUAPP.WebApp/Services/AzureBlobMediaLibraryService.cs` |
| PDFS | `SKOULOUAPP.WebApp/Services/LessonExerciseA4PdfService.cs` |
| P | `SKOULOUAPP.WebApp/Program.cs` |
| DB | `SKOULOUAPP.WebApp/Data/ApplicationDbContext.cs` |
| TLE / TL / TE | `SKOULOUAPP.WebApp/Components/Pages/TeacherLessonExercises` / `TeacherLessons` / `TeacherExams` `.razor(.cs)` |
| SL / SE / SEs | `…/Pages/StudentLesson` / `StudentExam` / `StudentExams` `.razor(.cs)` |
| RTE.js | `SKOULOUAPP.WebApp/wwwroot/js/richTextEditor.js` |
| FB.js | `SKOULOUAPP.WebApp/wwwroot/js/studentExerciseFeedback.js` |

---

## 1. Zweck & beteiligte Rollen

**Zweck:** Lehrkräfte (oder die Direktion in ihrem Namen) erstellen für ihre Klassen drei Arten von Inhalten:
- **Leçon + exercices:** eine Übungslektion (`Lesson`) mit A4-Inhalt und automatisch bzw. manuell bewerteten Aufgaben.
- **Support de cours:** ein mehrseitiges Kursskript (`TeacherLesson`) mit optionalem Video.
- **Examen:** eine zeitlich begrenzte Prüfung mit einem einzigen Versuch.

Schüler bearbeiten diese Inhalte und bekommen sofort Feedback, auch gesprochenes. Lehrkraft, Direktion und Schüler sehen daraus Scores. Zielmarkt sind Schulen in Togo; viele Lehrkräfte haben kein eigenes Gerät, darum gibt es die Erfassung durch die Direktion (DES:8-17).

**Policies** (P:145-148):
- `RequireTeacher` = Lehrer | SchulLeiter | Koordinator
- `RequireSchulLeiter` = SchulLeiter | Koordinator
- `RequireStudent` = Schüler

### 1.1 Berechtigungen, wie sie im Code tatsächlich durchgesetzt werden

| Rolle | Darf | Prüfung (Fundstelle) |
|---|---|---|
| **Lehrer** – Übungslektion | Anlegen und bearbeiten (nur über `/teacher/create`). Bearbeitbar sind **alle** Lektionen seiner zugeordneten Klassen, egal wer sie angelegt hat. | Klassenliste aus `LehrerNiveauZuordnung` nur **in der Seite** (TLE.cs:1273-1307, TLE.cs:763-791). Der Service `DS.SaveLessonAsync` prüft im eigenen Lehrerpfad **nichts**, weder Klasse noch Eigentum noch Schulsperre (DS:2466-2586). |
| Lehrer – Frist ändern | Nur Autor, oder Lehrer der Klasse bei autorloser Lektion | DS:2605-2610 |
| Lehrer – manuell korrigieren | Antworten in seinen zugeordneten Klassen | LS:924-948 (keine Schul-/Sperrprüfung) |
| Lehrer – Übungslektion lesen (`/view`, PDF) | Nur **veröffentlichte** Lektionen seiner Klasse | LS:396, LS:415-417 |
| Lehrer – Support (`TeacherLesson`) | Laut Service nur Autor (TLS:561, 745, 783, 858). Die Seite `/teacher/lessons` leitet reine Lehrer aber auf `/teacher/create?mode=lesson` um → **reine Lehrer können keine Supports pflegen** (TL.cs:664-674). | Eine Klassenzuordnung prüft der Service nur beim Handeln für Dritte (TLS:535-539) |
| Lehrer – Examen | Nur eigene Prüfungen, nur in zugeordneten Klassen | ES:648-658, ES:669, ES:928 |
| **Direktion** (SchulLeiter/Koordinator) – im Namen eines Lehrers | Anlegen, bearbeiten, veröffentlichen, kopieren und löschen von Lektion, Support und Examen | `DarfFuerLehrerHandelnAsync` (DES:31-65): Handelnder ist nicht gesperrt und hat Rolle SchulLeiter/Koordinator; das Ziel hat Rolle Lehrer und ist nicht gesperrt; beide teilen eine **freigegebene** Schule über `UserSchools` ohne `IstPlattformZuordnung`. Zusätzlich muss die Klasse dem Lehrer zugeordnet sein (TLS:880-889, DS:2486-2489, ES:648-649). |
| Direktion – ohne Lehrerauswahl auf `/teacher/lesson-exercises` | Alle Klassen ihrer Schulen, Lektionen anlegen und bearbeiten – **ohne** Kennzeichnung « Saisi par la direction » | TLE.cs:663-684 |
| Direktion – Supports lesen | **Alle** Supports aller Lehrer ihrer Schulen, auch Entwürfe und nicht geteilte | Liste ohne Filter auf `IsSharedWithSchool`/`IsPublished` (TLS:147-154); Reader ohne Freigabeprüfung (TLS:422-447) |
| Direktion – Support-/Examen-PDF | Laut Absicht nur bei `IsSharedWithSchool && IsPdfExportEnabled` | TLS:394-400, ES:1023-1029. **Faktisch** greift zusätzlich `canActForAuthor` für jede Direktion derselben Schule (TLS:404-406, ES:1033-1035) → praktisch alles exportierbar |
| Direktion – eigene « Examens (PDF) » | Autor = Direktion; Teilen und PDF erzwungen, keine Schüler | Nur UI (TE.cs:412-418, 581-586) |
| Direktion – manuell korrigieren | **Nicht möglich**: keine `LehrerNiveauZuordnung`. Beim Handeln für einen Lehrer wird die Korrekturliste gar nicht geladen. | LS:924-948, TLE.cs:795-797 |
| **Schüler** | Lesen und bearbeiten von Inhalten seiner Klassen | Zugangsmenge `SZS.KlassenAsync` (SZS:9-36): Profil nicht archiviert, Nutzer nicht gesperrt, Schule freigegeben. Dazu die aktuelle Klasse, mit `mitHistorie` auch frühere Klassen. **Ohne** Prüfung von Abo/Zahlung (SZS:6) |
| Schüler – Examen starten | Nur in der **aktuellen** Klasse, veröffentlicht, nicht superseded, vor der Frist | ES:233-235, ES:347-352 |
| **SuperAdmin** | `/lektionen`: Titel aller Lektionen der Plattform; Medien aller Schulen. Die Lektionsverwaltung im Admin ist per Konstante ausgeblendet. | `Lektionen.razor:3`, `Admin.razor.cs:646` (`KlassenVerwaltungSichtbar = false`) |
| **ProduktOwner** | Medien der eigenen Schulen sehen; nur eigene Bilder löschen | ABS:577-629 |

Eine Schule gilt als **freigegeben**, wenn sie `IsActive` ist und keinen gesperrten SchulLeiter hat (`SchulZugangService.cs:12-17`). Fast jede Schreibaktion prüft das. Die Meldung lautet "L'accès à votre école est suspendu. Veuillez contacter la direction de votre établissement." (`SchulZugangService.cs:10`).

---

## 2. Daten

### 2.1 Entitäten

Alle Entitäten erben `Id`, `ErstelltAmUtc`, `GeaendertAmUtc` (`AuditableModel`).

| Entität | Bedeutung | Wichtige Felder |
|---|---|---|
| `Lesson` | Übungslektion einer Klasse („Leçon + exercices“) | `KlasseId`, `Title` (max 200 lt. Form), `Summary`, `Content`, `FachId`, `Order`, `CreatedByTeacherId`, `ErfasstVonId` (s. u.) und die Freigabefelder in der nächsten Tabelle |
| `Exercise` | Eine Aufgabe (= eine Frage) einer Lektion | `Title` (= Blocktitel, max 200), `Instructions` („Consigne“), `Question` (HTML, DB **max 1000**, DB:298), `ExerciseType` (**String**), `Order`, `Points` (Default 10), `AudioText`, `TtsLanguage` (Default de-DE), `AcceptedAnswersJson` (`["v1","v2"]`), `GapAnswersJson` (`[["a","b"],["c"]]`) |
| `ExerciseOption` | Antwortoption | `Text` (max 500), `IsCorrect`, `Order` |
| `ExerciseAttempt` | **Jede** Abgabe eines Schülers als neue Zeile (Historie) | Antwortfelder `SelectedOptionId`, `SelectedOptionIdsJson`, `TextAnswer`, `GapAnswersJson`; Bewertung `IsCorrect`, `AwardedPoints`; Snapshot `ExerciseSnapshotJson` (max 4000); Korrektur `RequiresManualGrading`, `ManuallyGradedIsCorrect`, `ManualFeedback` (max 2000), `ManuallyGradedByUserId`, `ManuallyGradedAtUtc`; dazu `AssignmentVersion`, `AnsweredAtUtc` |
| `LessonProgress` | Fortschritt Schüler × Lektion (unique, DB:344-346) | `CorrectExerciseCount`, `TotalExerciseCount`, `IsCompleted`, `CompletedAtUtc`, `LastActivityUtc` |
| `LessonMedia` | Altmodell für Medien einer Übungslektion; heute nur noch für **das Video** genutzt | `MediaType`="video", `RelativePath`=`/media-library/videos/{assetId}?lessonId={lessonId}`, `StorageProvider`="AzureBlob" (DS:2693-2737). Kein Fremdschlüssel auf das Asset |
| `TeacherLesson` | Support de cours (mehrseitiges Skript) | `KlasseId`, `AuthorId`, `ErfasstVonId`, `Title` (200), `Summary` (1000), `CourseTheme` (200, Freitext), `Order`, `IsPublished`, `IsSharedWithSchool` (« Visible par la direction »), `IsPdfExportEnabled`, `FachId`, `VideoAssetId` (FK, SetNull) |
| `TeacherLessonPage` | Eine A4-Seite eines Supports | `PageNumber` (ab 1), `ContentHtml` (Cascade beim Löschen) |
| `BlobMediaAsset` | Privates Bild oder Video in der Schul-Mediathek | `SchoolId`, `OwnerId`, `KlasseId?`, `BlobName`, `DisplayName` (200), `AltText` (250), `ContentType`, `Length`, `MediaType` (Image=0/Video=1), `VideoDuration` |
| `FormelFavorit` | Persönliche Formel bzw. Favorit | `BenutzerID`, `LaTeX` (1000), `Bezeichnung` (200), `Vorschau` (400). Unique (BenutzerID, LaTeX) |
| `Exam` | Prüfung einer Klasse | `Title`, `Description`, `DurationInMinutes`, `SubmissionDueAtUtc` (« date limite »), `PassingScore`, `ExamType`, `IsSuperseded`, `AuthorId`, `ErfasstVonId`, `IsPublishedToStudents`, `IsSharedWithSchool`, `IsPdfExportEnabled` |
| `ExamExercise` | Aufgabenblock („Exercice n“) | `Title`, `Consigne` (1000), `Order` |
| `ExamQuestion` | Frage | `ExamExerciseId?`, `Prompt` (1000), `QuestionType`, `Points` (Default 1), `AudioText`, `TtsLanguage`, `AcceptedAnswersJson`, `GapAnswersJson` |
| `ExamQuestionOption` | Option | `Text` (500), `IsCorrect`, `Order` |
| `ExamAttempt` | Versuch | `StartedAtUtc`, `DeadlineUtc`, `SubmittedAtUtc`, `CorrectAnswers`, `TotalQuestions`, `ScorePercent`, `IsPassed`, `IsCompleted`, `RequiresManualGrading`; Nachtrag `ManuelleNote` (+ `…ErfasstAmUtc`, `…ErfasstVonUserId`) |
| `ExamAnswer` | Antwort je Frage (Entwurf **und** Endstand) | `SelectedOptionId`, `SelectedOptionIdsJson`, `FreeTextAnswer` (4000), `GapAnswersJson`, `IsCorrect`, `IsManuallyGraded`, `ManualPoints` (**wird nie geschrieben**) |
| `StudentProfile.AntwortTonAus` | Gesprochenes Feedback aus | bool, Default false = Stimme an (`StudentProfile.cs:54`) |
| `StudentProfile.CompletedLessonsCount` | Denormalisierter Zähler der abgeschlossenen Lektionen | wird bei Abschluss neu gezählt (LS:1148-1175) |
| `LevelEnrollment` | Klassenzugehörigkeit pro Zyklus inkl. Abo | Für diesen Bereich relevant: `KlasseId`, `DurchlaufId`, `IstAktuell`, `IsUnlocked`, `IsPaid`, `StorniertAmUtc` |

**Freigabe- und Abgabefelder von `Lesson`:**

| Feld | Bedeutung |
|---|---|
| `IsPublished` | Gemeinsame Freigabe von Lektion und Aufgaben. DB-Default `true` (DB:300) |
| `ExerciseSubmissionDueAtUtc` | « date limite » der Aufgaben |
| `AssignmentVersion` | Abgaberunde, Default 1 |

**`ErfasstVonId`** steht an `Lesson`, `TeacherLesson` und `Exam`. Es ist das Konto der Direktion, die den Inhalt für den Lehrer erfasst hat (UI: « Saisi par la direction »). Bearbeitet der Lehrer den Inhalt später selbst, bleibt das Feld erhalten.

**Totes Datenmodell:** `LektionsFortschrittModell` + `LektionsFortschrittService` dupliziert `LessonProgress` als **In-Memory-Liste**. Es gibt kein DbSet; der Service ist nur registriert (`LektionsFortschrittService.cs:9`, P:155).

### 2.2 Aufzählungen und Status

- **Aufgabentyp (Übung, String, normalisiert LS:980-1000 / DS:2930-2949):**

  | Wert | Label |
  |---|---|
  | `SingleChoice` | « Choix unique » |
  | `MultipleChoice` | « Choix multiple » |
  | `Text` | « Texte » |
  | `SentenceBuilding` | « Construction de phrase » |
  | `GapFill` | « Texte à trous » |
  | `AudioText` | « Audio → réponse texte » |
  | `AudioSingleChoice` | « Audio → choix unique » |
  | `AudioMultipleChoice` | « Audio → choix multiple » |

  Labels aus TLE.cs:564-574. Aliase: choice, freetext, gap, lücken, satzbau, audio, audiochoice. **Unbekannt oder leer → SingleChoice.**
- **`ExamQuestionType`** (Enum `ExamQuestionType.cs`): dieselben 8 Typen; heißt dort `FreeText` statt `Text`.
- **`ExamType`:** `TestExam`=0, `AbschlussRelevant`=1. Historisch; neue Prüfungen sind **immer** TestExam (ES:700). Die Abschlussnote wird unabhängig davon manuell vergeben (`ExamType.cs:3-10`).
- **`ExamReleaseTarget`:** `PrivateAuthor`/`SchoolLeader`/`ClassStudents`. Wird **nicht gespeichert**, sondern aus den Flags abgeleitet (ES:889-893).
- **`BlobMediaType`:** Image=0, Video=1.
- **TTS-Sprachen:** de-DE (Default), fr-FR, en-US (DS:2951-2957; ES:1500-1514 über das Präfix).
- **Zustände einer Übungslektion:** Entwurf (`!IsPublished`) → veröffentlicht. Aus Lehrersicht dazu:
  - « Remise en cours » (Frist läuft)
  - « Date limite passée »
  - « Aucune date limite »
  - « Leçon verrouillée » (Frist läuft und Abgaben in der aktuellen Version vorhanden)
- **Zustände eines Examens:**
  - « non attribué » (`!IsPublishedToStudents`)
  - offen bis …
  - Frist vorbei
  - « Session précédente · résultats conservés » (`IsSuperseded`) (TE.razor:73-94)

---

## 3. Geschäftsregeln

### 3.1 Toleranter Antwortvergleich (gilt für Übungen **und** Examen)

`AMS.Normalize` (AMS:19-51) bereitet Antwort und Variante gleich auf:
1. HTML entfernen; Blockenden `</p|div|li|h1-6|br>` werden zu Leerzeichen; Entities werden dekodiert (AMS:69-74).
2. Trimmen.
3. Nur Buchstaben und Ziffern behalten; Satzzeichen fallen weg; Leerraum wird zu genau einem Leerzeichen (AMS:31-47).
4. Diakritika entfernen (NFD ohne NonSpacingMark) – **auch Umlaute** (ä→a). Der Kommentar in AMS:28 behauptet etwas anderes. `ß` bleibt.
5. **Groß-/Kleinschreibung bleibt erhalten.** Der Klassenkommentar (AMS:11) sagt das Gegenteil; ein Test legt die Groß-/Kleinschreibung aber fest (§6).

Weitere Regeln:
- `Matches` ist **true**, wenn die normalisierte Antwort nicht leer ist und exakt einer normalisierten Variante gleicht (AMS:79-96).
- **Varianten-Eingabe:** eine Variante pro Zeile. `<br>`, `</p>`, `</div>`, `</li>` gelten als Zeilentrenner; leere Zeilen fallen weg (AMS:195-221). Gespeichert wird ein JSON-Array oder `null` (AMS:226-230).
- **Lücken:** `GradeGaps` vergleicht Lücke *i* mit dem Schülerwert *i* (Reihenfolge, kein Marker-Parsing). Ergebnis ist `(correct, total)` mit `total` = Anzahl definierter Lücken (AMS:173-190).

### 3.2 Übungsabgabe und Bewertung (`LS.SubmitExerciseAsync`, LS:692-860)

**Vorbedingungen** (in dieser Reihenfolge, jeweils mit Meldung):
1. Aufgabe existiert – sonst "L'exercice est introuvable."
2. Schulprofil passt – sonst "Cette leçon n'appartient pas à votre école."
3. Klasse liegt in der Zugangsmenge **inkl. Historie** – sonst "Ce niveau n'est pas encore débloqué pour votre compte."
4. Lektion ist veröffentlicht – sonst "Cette leçon n'est pas encore publiée."
5. Frist nicht überschritten – sonst "La date limite est dépassée pour cet exercice." (LS:704-733)

**Bewertung je Typ – überall alles oder nichts, keine Teilpunkte:**

| Typ | Regel | Punkte | Meldung |
|---|---|---|---|
| GapFill | richtig ⇔ `total > 0 && correct == total` | `Points` oder 0 | "Bravo, toutes les réponses sont correctes !" bzw. "{c}/{t} correct(s). Réessayez." (LS:747-756) |
| MultipleChoice / AudioMultipleChoice | Gewählte Menge (nur gültige IDs, distinct) **gleich** der Menge der korrekten Optionen, und es gibt mindestens eine korrekte Option. Leere Auswahl → Fehler "Aucune réponse sélectionnée." | `Points` oder 0 | LS:757-774 |
| SingleChoice / AudioSingleChoice | gewählte Option `IsCorrect` (ungültige Option → "L'option choisie est invalide.") | `Points` oder 0 | LS:775-793 |
| Text / SentenceBuilding / AudioText **mit** Varianten | `Matches(answer.Trim(), varianten)` | `Points` oder 0 | "Bravo, c'est correct !" / "Ce n'est pas la bonne réponse. Réessayez." |
| Text-Typen **ohne** Varianten | `IsCorrect=false`, `RequiresManualGrading=true`, 0 Punkte. Nach Fristablauf wird abgelehnt: "La date limite est dépassée pour cet exercice ouvert." | 0 bis zur Korrektur | "Votre réponse a été enregistrée. Elle sera corrigée par votre enseignant." (LS:794-823) |

- **Wiederholen:** Serverseitig **unbegrenzt**; jede Abgabe ist eine neue Zeile mit aktueller `AssignmentVersion` und Snapshot (LS:825-839).
  - Die UI sperrt eine Aufgabe, sobald sie richtig ist oder auf manuelle Korrektur wartet (SL.razor:296-301, SL.cs:191, 203).
  - Bei falscher Antwort darf der Schüler beliebig oft neu antworten, ohne Abzug. Es zählt nur der **letzte** Versuch.
- **Snapshot:** enthält Id, Titel, Frage, Typ, Punkte und die Optionen mit `IsCorrect`; **nicht** die Varianten- und Lücken-Lösungen (LS:1177-1193).
- **„Hat automatische Bewertung“:** Choice-Typ, GapFill oder mindestens eine Variante (LS:331-333). Davon hängt das Button-Label ab: « Vérifier la réponse » bzw. « Envoyer la réponse » (SL.cs:450-451).

### 3.3 Manuelle Korrektur (Übungen)

**Liste « Corrections à faire »** (LS:862-915):
- Enthält nur Versuche mit `RequiresManualGrading`, deren Lektion veröffentlicht ist **und eine Frist hat, die abgelaufen ist** (LS:885-889). Es gilt also: **ohne Frist keine Korrektur**.
- Nur Klassen des Lehrers.
- Sortierung: Lektion, Aufgabe, Abgabezeit.

**Speichern** (`GradeManualExerciseAttemptAsync`, LS:917-971):

| Prüfung | Meldung |
|---|---|
| Versuch existiert | "Réponse introuvable." |
| noch offen | "Cette réponse a déjà été corrigée." – keine Nachkorrektur |
| Klasse des Lehrers | "Vous n'êtes pas autorisé à corriger cette réponse." |
| `0 ≤ Punkte ≤ max` (max = `Points > 0 ? Points : 10`) | "Les points doivent être compris entre 0 et {max}." |

- `IsCorrect = awardedPoints >= max` (LS:957). Teilpunkte gibt es nur hier.
- Danach werden Feedback (getrimmt) und Korrektor/Zeit gesetzt und der Fortschritt neu berechnet.
- Eine **Fristprüfung fehlt serverseitig** (nur die Liste filtert).
- In der UI ist das Punktefeld mit der **vollen Punktzahl vorbelegt** (TLE.cs:268-287, 799).

### 3.4 Lektionsfortschritt und Abschluss (`UpdateLessonProgressAsync`, LS:1008-1134)

- Betrachtet werden nur Versuche der **aktuellen** `AssignmentVersion`; pro Aufgabe zählt der **neueste** Versuch.
- **Gelöst** = `IsCorrect` **oder** (Aufgabe ohne automatische Bewertung **und** manuell korrigiert, egal mit wie vielen Punkten) (LS:1078-1099).
  - Eine manuell mit 0 Punkten korrigierte Textaufgabe zählt also als „gelöst“.
- `IsCompleted = total > 0 && gelöst ≥ total` (LS:1101). `CompletedAtUtc` wird beim ersten Abschluss gesetzt und nie zurückgesetzt (LS:1123-1126).
- Beim Wechsel auf abgeschlossen wird `StudentProfile.CompletedLessonsCount` neu gezählt (LS:1128-1131, LS:1148-1175). Wird eine Lektion wieder offen (neue Version), passt sich der Zähler **nicht** nach unten an.
- Die Lektionsliste rechnet dasselbe live (LS:144-211), dazu Punkte: `Earned = Σ clamp(letzter.AwardedPoints, 0, max)`, `Total = Σ (Points > 0 ? Points : 10)` (LS:160-183).
- Fortschrittsring in der Lektion: `round(gelöst*100 / Anzahl Aufgaben)` (SL.cs:59-61).

### 3.5 Frist, Abgaberunden (`AssignmentVersion`), Sperre, Korrekturmodus

- **Sperre:** Hat die aktuelle Version Abgaben **und** läuft die Frist noch, wird Speichern abgelehnt mit "Cette leçon a déjà des réponses d'élèves pour la session en cours. Les modifications seront possibles après la date limite ou via une nouvelle session." (DS:2522-2525). Die UI deaktiviert dann alle Felder (TLE.razor:246-264).
- **Neue Runde beim Speichern:** Hat die aktuelle Version Abgaben und gibt es keine Frist oder ist sie vorbei, gilt `AssignmentVersion += 1` (DS:2527-2530). Alte Versuche bleiben stehen, zählen aber nicht mehr für Fortschritt und Score.
- **Frist ändern** (`ChangeLessonSubmissionDueAsync`, DS:2593-2632):
  - Nur ein zukünftiges Datum, sonst "Choisissez une date limite future."; leer = Frist entfernen.
  - Eine **Verlängerung** (neues Datum später als das alte, oder vorher keine Frist) bei vorhandenen Abgaben erhöht die Version. Dann dürfen **alle** Schüler neu abgeben.
  - Meldung: "La date limite a été mise à jour. Tous les élèves peuvent envoyer de nouvelles réponses jusqu'à cette date."
- **Korrekturmodus (Schüler):** gilt, wenn eine Frist gesetzt und `UtcNow > Frist` ist (LS:283-284).
  - Erst dann liefert der Server Varianten- und Lücken-Lösungen aus (LS:359-362).
  - Die UI zeigt richtig grün, falsch rot, « Bonne réponse : … » bzw. « Blanc n : … » (SL.razor:240-266).
  - Ohne Frist gibt es **nie** einen Korrekturmodus.
- **Neu zuweisen** (« Modifier et réattribuer », TLE.cs:991-1018):
  - Die UI lädt die Lektion als Vorlage: `LessonId=null`, `QuellLessonId` gesetzt, Version 1, veröffentlicht, Frist leer, **Titel leer**. Die Klasse darf wechseln.
  - Gespeichert wird eine **neue** Lektion, die « Saisi »-Kennzeichnung der Quelle wird übernommen (DS:2568-2576).
  - Die Quelle bleibt veröffentlicht; ein Superseded-Konzept wie bei Examen gibt es hier nicht.

### 3.6 Speichern einer Übungslektion (DS:2466-2586, TLE.cs:1250-1389)

**UI-Vorbelegungen:**
- Leerer Inhalt → "Contenu de leçon (template enseignant)."
- Leere Zusammenfassung → Titel
- Titel ist Pflicht: "Veuillez saisir un titre de leçon."
- Die Klasse ist nach dem Anlegen nicht änderbar: "La classe de cette leçon ne peut pas être modifiée ici. Utilisez « Modifier et réattribuer »…"
- Fach ist **Pflicht**, nur aus den dem Lehrer zugewiesenen Fächern: "Aucune matière ne vous est attribuée pour cette classe. Demandez à la direction de configurer vos matières." (TLE.cs:1342-1347). Der Service selbst würde neue Fächer anlegen (DS:2634-2691).

**Inhalt:** Der Editor teilt den Text an A4-Grenzen in Seiten. Diese werden mit `<!--SKOULOUAPP_PAGE_BREAK-->` zu **einem** String verbunden (TLE.cs:1260-1271) und beim Lesen wieder geteilt (LS:17-44).

**Aufgaben-Sync** (DS:2806-2873):
- `Order` = Position.
- `Points ≤ 0` wird still zu 10; es gibt keine Obergrenze (DS:2840).
- `AudioText` nur bei Audio-Typen.
- Varianten nur bei Nicht-GapFill, Lücken nur bei GapFill.
- Optionen nur bei Choice-Typen; Optionen ohne Text werden verworfen (DS:2878).
- SingleChoice ohne markierte korrekte Option → die erste Option wird korrekt (DS:2905-2906).
- Entfernte Aufgaben werden **gelöscht**, und per Cascade **alle ihre Versuche** (DB:314-318).

**Reihenfolge und Autor:**
- `Order` einer neuen Lektion = max(Order der Klasse) + 1 (DS:2543-2550).
- `CreatedByTeacherId` = aktueller Lehrer beim Anlegen oder bei autorlosen Altdaten (DS:2554-2562).

**Video:** über `LessonMedia`. Geprüft wird nur, dass das Asset existiert, nicht Schule oder Klasse (DS:2709-2716).

### 3.7 Supports de cours (`TeacherLesson`)

- **Speichern** (TLS:529-601):
  - Klasse existiert ("Le niveau sélectionné est introuvable.") und ist kein System-Niveau ("Les niveaux standard font partie du matériel pédagogique et ne peuvent pas être gérés par les enseignants.").
  - Schule ist freigegeben.
  - Bearbeiten darf nur der Autor ("Le cours introuvable.").
  - `Order` neu = max + 1.
  - Seiten werden per Id abgeglichen: entfernte Seiten löschen, Nummern neu ab 1 vergeben (TLS:891-929).
  - Inhalt wird **unbereinigt** gespeichert.
- **Fach** (TLS:643-712):
  - Ist ein Freitext gesetzt, wird das Fach der Schule ohne Rücksicht auf Groß-/Kleinschreibung gesucht oder neu angelegt.
  - Danach wird es dem Lehrer automatisch zugeordnet (`LehrerFachZuordnung`).
  - Fach ist optional (anders als bei Übungslektionen).
- **Veröffentlichen/Zurückziehen:** jederzeit, nur Autor, Schule freigegeben (TLS:734-757).
- **Kopieren in eine Klasse** (TLS:764-843):
  - Titel wird „{Titel} (copie)“.
  - Entwurf, außer « Publier directement la copie ».
  - Flags werden übernommen, das Video **nicht**.
  - Das Fach nur bei gleicher Schule.
  - « Saisi » wird übernommen.
  - Die Zielklasse ist bei eigenem Handeln **nicht** auf die Klassen des Lehrers beschränkt.
- **Löschen:** Autor, sofort und hart, inkl. Seiten (TLS:846-874). In der UI ohne Bestätigung (TL.razor:247).
- **Sichtbarkeit für Schüler:** veröffentlicht, Klasse in der Zugangsmenge **inkl. Historie**, gleiche Schule (TLS:291-324).
  - Wo sie gelistet werden:
    - auf `/student` unter « Ma classe » (nur wenn für die aktuelle Klasse ein Zyklus existiert, DS:1090-1121)
    - auf `/student/history` (frühere Klassen, TLS:221-267)
  - **PDF für Schüler** nur mit `IsPdfExportEnabled` (TLS:358-363).
- `IsSharedWithSchool` steht im Formular auf Default **true**, `IsPdfExportEnabled` ebenfalls (`TeacherLessonFormModel.cs:33,36`).

### 3.8 Examen

**Anlegen und Bearbeiten** (ES:685-764, ES:663-681):
- Server-Validierung:
  - Titel und Klasse sind Pflicht.
  - Mindestens 1 Block und 1 Frage.
  - Jede Frage braucht einen Prompt und `Points ≥ 1`.
  - Choice-Typen brauchen mindestens 2 Optionen (ES:1516-1556).
- Annotation-Grenzen (nur clientseitig, verschachtelt vermutlich ungeprüft):

  | Feld | Grenze |
  |---|---|
  | Dauer | 1–600 min, Default 60 |
  | `PassingScore` | 0–100, Default 50 |
  | Frage-`Points` | 1–100 |
  | Titel | max 200 |

  Quelle: `ExamFormModel.cs:14-112`.
- Beschreibung: bereinigt **ohne** Bilder. Consigne und Prompt: **mit** Bildern.
- Blocktitel-Default „Exercice n“. `Order` der Fragen läuft global über alle Blöcke.
- Bearbeiten heißt: alle Blöcke und Fragen löschen und neu anlegen. Erlaubt nur **ohne Versuche** und nicht superseded.
- **Freigabe:**
  - `IsPublishedToStudents` = « Élèves »
  - `IsSharedWithSchool` = « Direction »
  - Beides ist kombinierbar (ES:701-705).
  - Ist bei der Zuweisung eine Frist gesetzt, muss sie in der Zukunft liegen (ES:615-619).
- **Löschen:** nur der Autor, ohne Versuche (auch offene), Schule freigegeben (ES:961-995). In der UI ohne Bestätigung.

**Sitzung:**
- **Ein Versuch pro Schüler und Prüfung.** Doppelstart verhindert eine Serializable-Transaktion mit App-Lock `SKOULOUAPP:Exam:{exam}:{student}` (ES:198-211).
- **Deadline** des Versuchs (ES:285-287):

  ```
  DeadlineUtc = SubmissionDueAtUtc is d ? min(d, now + Dauer) : now + Dauer
  ```

  Wer spät startet, hat weniger Zeit. Die UI zeigt « commencez au plus tard le … » = Frist − Dauer (SEs.razor:72-86).
- **Fortsetzen:** gleicher Versuch, gleiche Deadline, gespeicherte Antworten.
  - Entwurf: Autosave alle 5 s und beim Verlassen (SE.cs:169-173, 333-337).
  - Der Server lehnt den Entwurf ab, wenn die Deadline oder die Frist erreicht ist (ES:359-365).
- **Timeout:** `timedOut = now ≥ DeadlineUtc || SubmissionDueAtUtc ≤ now` (ES:463).
  - Dann werden die Client-Antworten **verworfen**; bewertet wird der gespeicherte Entwurf.
  - `IsPassed` ist dann **immer false** (ES:465-467, 558).
  - Der Client sendet bei Restzeit 0 automatisch (SE.cs:158-167). Serverseitig wird nur „lazy“ abgeschlossen: beim Listen, Starten und Neu-Zuweisen (ES:68-80, 252-275, 827-834). Es gibt keinen Hintergrundjob.
- **Sichtbar für Schüler** (ES:44-54): gleiche Schule **und** (eigener Versuch abgeschlossen **oder** aktuelle Klasse + veröffentlicht + nicht superseded).
  - **Startbar** = Fragen vorhanden, nicht superseded, kein abgeschlossener Versuch, Deadline und Frist in der Zukunft (`ExamListItem.cs:19-21`).
- **Lösungen** sieht der Schüler **sofort nach der eigenen Abgabe** (ES:108-191). Sie bleiben sichtbar nach Klassenwechsel oder Entveröffentlichung, aber nicht bei Konto-/Schulsperre oder Archivierung.

**Bewertung** (ES:496-558):
- Choice: wie bei Übungen.
- Multiple: exakte Menge.
- GapFill: alle Lücken richtig.
- Text-Typen: mit Varianten über `Matches`; ohne Varianten `IsManuallyGraded` + `RequiresManualGrading`, zählt als falsch.
- **Punkte werden ignoriert:**

  ```
  ScorePercent = total == 0 ? 0 : (int)Math.Round(correct * 100.0 / total)   // Banker's Rounding
  IsPassed     = !timedOut && !requiresManualGrading && ScorePercent >= PassingScore
  ```

- **Eine manuelle Bewertung von Examen gibt es nicht** (`ManualPoints` wird nie geschrieben). Eine Prüfung mit einer Textfrage ohne Varianten ist deshalb **nie bestehbar**.
- **`ManuelleNote`:** 0–100, nur für AbschlussRelevant-Versuche im aktiven Durchlauf, hat Vorrang (`WirksameNote = ManuelleNote ?? ScorePercent`, `KlassenAbschlussUebersicht.cs:34`). Ohne Aufrufer, also **toter Code** (ES:1179-1251).

**Frist ändern** (`ChangeSubmissionDueAsync`, ES:770-797; **nur Service, keine UI**):
- Bedingungen: aktiv, veröffentlicht, Frist in der Zukunft, kein abgelaufener offener Versuch.
- Laufende Versuche: `Deadline = min(neueFrist, Start + Dauer)`.

**Neu zuweisen « Réattribuer »** (ES:803-850):
1. Bedingungen: nur der Autor; Quelle veröffentlicht und nicht superseded; kein laufender Versuch; Frist leer oder vorbei; neue Frist ist Pflicht, liegt in der Zukunft und gilt für **dieselbe Klasse**.
2. Abgelaufene offene Versuche werden bewertet.
3. Aus dem (in der UI editierbaren, kopierten) Formular entsteht eine **neue, veröffentlichte** Prüfung.
4. Die Quelle wird `IsSuperseded=true`; alte Versuche bleiben an der Quelle.
5. Eine Neuzuweisung pro Schüler gibt es nicht. Der Button ist deaktiviert bei laufendem Versuch oder bei einer Frist in der Zukunft (TE.razor:116).

### 3.9 Scores

**Score-Farbe** (`ScoreColorHelper.cs`):

| Bereich | Farbe |
|---|---|
| < 50 % | Rot `#dc2626` |
| = 50 % | Hellrot |
| 51–75 % | Hellgrün |
| > 75 % | Grün `#16a34a` |

**CombinedScore** (`DS.GetCombinedProgressScoresAsync`, DS:538-747) wird genutzt im Lehrer-Dashboard, in den Direktions- und Admin-Listen sowie für den Schüler (dort auf die aktuelle Klasse beschränkt, DS:1041).
1. **Übungen:**
   - Nur nicht offene Versuche (`!RequiresManualGrading`) veröffentlichter Lektionen.
   - Pro (Schüler, Lektion) nur die **höchste** vorhandene AssignmentVersion; pro Aufgabe der neueste Versuch (DS:577-657).
   - `earned = AwardedPoints > 0 ? clamp(AwardedPoints, 0, max) : (IsCorrect ? max : 0)`; `max = Points > 0 ? Points : 10`.
   - Prozentwert je Aufgabe: `round(earned*100/max)`.
2. **Überfällig und nicht versucht:** Aufgaben veröffentlichter Lektionen der **aktuellen** Klasse mit abgelaufener Frist, für die der Schüler nie (in irgendeiner Version) abgegeben hat, zählen mit **0 %** und voller Maximalpunktzahl (DS:678-730).
3. **Examen:**
   - Nur Prüfungen, die *aktuell* `IsPublishedToStudents` sind; abgeschlossene Versuche ohne `RequiresManualGrading`; pro Prüfung der neueste Versuch.
   - Prozent = `ScorePercent`; Punkte = `CorrectAnswers / TotalQuestions` (DS:611-676).
4. **Ergebnis** = **ungewichteter Mittelwert der Prozentwerte** aller Einträge (Übungen und Examen gleich gewichtet): `round((ΣÜbung% + ΣExamen%) / Anzahl)` (DS:732-746).

**Klassenhistorie** (`/student`, `/student/history`, DS:1084-1245):
- Pro Zyklus (Durchlauf) gilt ein anderes Verfahren: **punktgewichtet** `round(Σearned*100/Σmax)` über Übungen + überfällige 0er + Examen (`CorrectAnswers/TotalQuestions`).
- Gezählt wird nur, was zwischen Zyklusstart und Zyklusende liegt (DS:1179-1212).
- Angezeigt mit Abschlussnote, Mention und « Réussi/… ».

**Lehrer-Kacheln** (DS:3706-3820):
- `SolvedExerciseCount` = distinct Aufgaben mit irgendeinem richtigen Versuch (alle Versionen, alle Klassen).
- `ExamsTaken/Passed/BestScore` über alle abgeschlossenen Versuche.
- Letzte Aktivität = max(Examen, LessonProgress).

**Streak** (DS:1254-1312): aufeinanderfolgende **UTC**-Tage mit Aktivität (Übung, Lektionsabschluss, Examen) in der aktuellen Klasse, endend heute oder gestern. Anzeige ab 2: « n jours de suite ».

**Skoulou+-Kennzeichen:** inkonsistent definiert.

| Fundstelle | Definition |
|---|---|
| DS:3578 | `IstAktuell && IsUnlocked` |
| DS:3796 | `IstAktuell && IsPaid` |
| Schüler-Dashboard, DS:1066 | immer `false` |

### 3.10 LevelEnrollment / Skoulou+ – Lernzugang

- **Grundregel:** „Materialzugang ist eine Schul-/Klassenberechtigung, keine Zahlungsbestätigung“ (SZS:6). Lektionen, Aufgaben, Supports, Videos und Examen erfordern **weder** `IsUnlocked` **noch** `IsPaid` (Tests §6).
- **Enrollments erweitern nur die Historie** (SZS:19-32): Zyklus-Enrollments, Abschlussentscheidungen und Alt-Enrollments ohne Zyklus mit `IsUnlocked || !IstAktuell` geben Lesezugriff auf **frühere** Klassen.
  - Stornierte Enrollments (`StorniertAmUtc`) werden dabei **nicht** ausgeschlossen.
- `LearningService` setzt `IsUnlocked=true` hart (LS:234); die Schlossanzeige im Lernpfad ist damit tot (StudentLessons.cs:173-191).
- Der Parameter `hasActiveSubscription` beim Video-Endpunkt wird berechnet, aber nie genutzt (P:705-706).

### 3.11 Medien (Kurzfassung der Regeln)

**Formate und Grenzen:**

| | Bilder | Videos |
|---|---|---|
| Formate | PNG/JPG/GIF/WebP | MP4/M4V/MOV |
| Größe | ≤ 25 MB, **nur clientseitig** | ≤ 8 GB |
| Pflicht | Alt-Text, ≤ 250 Zeichen; Server prüft ihn (ABS:684-687) | Dauer 1 s–1 h, **vom Client gemessen** (ABS:564-575) |

- Anzeigename ≤ 200, bereinigt.
- Keine Magic-Byte-Prüfung.
- Upload in 4-MB-Blöcken.

**Ablage:**
- Blob-Name: `SkoulouAppStorage/schools/{schoolId:N}/[classes/{klasseId:N}/]{images|videos}/{yyyy/MM}/{name}-{guid:N}{ext}` (ABS:661-667).
- In Development liegt alles lokal unter `wwwroot/media-library` (P:172-179).
- Zugriff über Managed Identity; **keine SAS-URLs**, alles läuft als Proxy über die App.

**Sichtbarkeit und Löschen** (ABS:577-612):
- Lehrer und Direktion sehen **alle** Assets ihrer freigegebenen Schulen, nicht nach Eigentümer oder Klasse gefiltert.
- Löschen nur für **Bilder**; Lehrer nur eigene, Direktion alle.
- Gesperrt, wenn das Bild Profilbild ist oder in einer `TeacherLessonPage` vorkommt (ABS:436-447). Übungen und Examen werden nicht geprüft.

**Einbettung:**
- Bilder kommen als `<img src="/media-library/{id}">` ins HTML; Einfügen nur über die Mediathek.
- Paste wird blockiert: "Les images doivent être choisies dans la bibliothèque Azure Storage." (RTE.js:563-577).

**Schülerzugriff auf ein Bild** (ABS:467-495):
- Die URL muss in einem veröffentlichten Inhalt seiner **aktuellen** Klasse vorkommen (LIKE-Suche) und das Asset zur Schule gehören.
- Video (ABS:497-562): veröffentlichter Support mit `VideoAssetId` oder veröffentlichte Lektion mit passendem `LessonMedia`; Klassen inkl. Historie.

### 3.12 Formeln & FormelFavorit

- **Editor:** Quill 2 + KaTeX (CDN).
  - Palette « Insérer une formule » mit 46 Vorlagen in 6 Gruppen, dazu « Favoris » und « Mes formules » (RTE.js:104-151, 930-1139).
  - Bearbeitungsdialog « Modifier la formule (LaTeX) » mit Live-Vorschau und Umschalter « Ajouter à mes formules » (RTE.js:1141-1287).
- **Speicherform:** `<span class="ql-formula" data-value="LaTeX">`. Der Sanitizer erlaubt `data-value` nur auf `span`, ≤ 1000 Zeichen, ohne `<`, `>` und Steuerzeichen (HS:292-301).
- **API** (P:764-815, jede angemeldete Rolle):
  - `GET /api/formules/favoris`
  - `POST /api/formules/favoris` {latex, bezeichnung, vorschau} (400 bei leerem LaTeX)
  - `DELETE /api/formules/favoris?latex=` (204/404)
- **Regeln** (`FormelFavoritService.cs`):
  - Pro Benutzer; gleiches LaTeX **aktualisiert** statt zu duplizieren (:43-61).
  - Zu lange Werte werden gekürzt statt abgelehnt (LaTeX 1000 / Bezeichnung 200 / Vorschau 400); leere Bezeichnung oder Vorschau = LaTeX (:105-114).
  - Sortierung: Bezeichnung, dann Erstellzeit. Kein Mengenlimit.
  - Eigene Formeln heißen immer « Formule personnelle » (RTE.js:1223-1227).

### 3.13 Gesprochenes Feedback & TTS

- **Feedback nach jeder Übungsabgabe** (FB.js:144-177, Aufruf SL.cs:309-326):

  | Situation | Ton | Satzgruppe |
  |---|---|---|
  | Manuelle Korrektur nötig | zwei Dreiecktöne | `gesendet` |
  | Richtig | aufsteigender Dreiklang | `richtig-1..6`, danach `weiter`, wenn eine nächste Aufgabe folgt |
  | Falsch | zwei Sägezahntöne | `falsch-1..6` |

- **Audiodateien:** 14 MP3s in `wwwroot/audio/antworten/` (Stimme „Vivienne“, einmalig mit Azure erzeugt).
  - Zufällige Auswahl ohne direkte Wiederholung (FB.js:73-82), Wiedergabe über einen vorgeladenen AudioContext (iPhone-tauglich).
  - Fehlt eine Datei, spricht die Browserstimme fr-FR: "Bravo, très bien !", "Oups, essaie encore !", "Exercice suivant !", "Réponse envoyée !" (FB.js:12-17, 122-138). Der Vorname wird nicht gesprochen.
- **Schalter « Voix : oui/non »** pro Schülerkonto (`AntwortTonService.cs:12-31`). Gesetzt wird er für **alle** Profile des Kontos; false = kein Schülerprofil vorhanden.
- **Nach richtiger Antwort** springt die UI nach 700 ms zur nächsten Aufgabe (SL.cs:263-271).
- **Vorlesen von Audio-Aufgaben:** Button mit `data-skoulouapp-tts-text/lang`, delegierter Handler in `tts.js:186-198` (Web Speech API). Die bevorzugte Sprache steht in localStorage `skoulouapp_tts_lang`.
  - `ITextToSpeechProvider` / `BrowserSpeechProvider` sind registriert (P:242), werden aber praktisch nicht genutzt (`PlayAudioAsync` tot, SL.cs:364-367).

---

## 4. Abläufe

**A. Lehrer erstellt eine Übungslektion**
1. `/teacher/create?mode=lesson` → « + Nouvelle leçon ».
2. Formular: Titre, École, Niveau, Matière (Pflicht), Résumé, Contenu (A4-Editor), Video aus der Mediathek, « Publier la leçon et ses exercices », « Date limite des réponses ouvertes (optionnel) ».
3. Aufgabenblöcke « Exercice n »: Blocktitel + Fragen.
   - Jede Frage hat Typ, Points, Consigne, Question und typabhängige Felder.
   - Neue Frage = SingleChoice, 10 Punkte, 2 Optionen, die erste korrekt (TLE.cs:1511-1522).
4. Speichern → Seiten werden mit Marker verbunden → `DS.SaveLessonAsync` → "Les exercices de la leçon ont été enregistrés."

**B. Schüler bearbeitet die Lektion**
1. `/student/lessons` → Parcours/Liste → « Commencer / Poursuivre / Revoir la leçon ».
2. `/student/lessons/{id}`: A4-Seiten lesen, Video ansehen, optional PDF « Télécharger la leçon et les exercices (sans solutions) ».
3. Aufgaben einzeln mit Punkte-Navigation; Blöcke = aufeinanderfolgende Aufgaben mit gleichem Titel (SL.cs:521-559).
4. « Vérifier la réponse » → Bewertung, Feedbacktext, Ton/Stimme, bei richtig automatisch weiter. Falsch → erneut versuchen.
5. Textaufgabe ohne Varianten → « Envoyer la réponse » → gesperrt mit « Réponse enregistrée. ».

**C. Frist läuft ab → Korrektur**
1. Der Lehrer sieht unter « Corrections à faire » die offenen Antworten und vergibt Points + « Commentaire (facultatif) ».
2. Der Schüler sieht im Korrekturmodus « Points obtenus » und « Feedback », dazu die Lösungen.
3. Danach bearbeiten:
   - Speichern erhöht die Version, oder
   - « Date limite » verlängern (neue Runde für alle), oder
   - « Modifier et réattribuer » (neue Lektion).

**D. Support de cours (Direktion für einen Lehrer)**
1. `/teacher/lessons` → « Saisir pour l'enseignant : » wählen.
2. « + Nouveau cours » → Titre, École, Niveau, Résumé, Matière/« Nouvelle matière », « Thème du cours (optionnel) », Video, A4-Editor.
3. Checkboxen « Publier pour les élèves » / « Visible par la direction » / « Autoriser l'export PDF ».
4. Speichern → die Seiten werden an A4-Grenzen geteilt → `TeacherLessonPage`-Zeilen.
5. Weitere Aktionen: « Copier vers une classe », « Publier/Dépublier », « Supprimer ».

**E. Examen**
1. Der Lehrer legt die Prüfung in `/teacher/create?mode=exam` an: Blöcke, Fragen, Dauer, Bestehensgrenze, Empfänger « Direction »/« Élèves », Frist.
2. Der Schüler startet sie in `/student/exams` (« Commencer »). Timer läuft, Autosave, « Envoyer ».
3. Ergebnis: Score, « Réussi/Échoué »; danach sofort « Consulter la correction ».
4. Nach Fristende « Réattribuer » → neue Runde; die alte Prüfung wird « Session précédente ».

**F. Direktion liest und exportiert**
- `/teacher/lessons` (Reiter « Leçons + exercices » / « Examens ») → « Voir le contenu » → `/…/view` → « Exporter PDF ».

---

## 5. Seiten, Routen, PDFs, Endpunkte, JS

### 5.1 Seiten

| Route | Policy | Inhalt |
|---|---|---|
| `/teacher` « Tableau pédagogique » | RequireTeacher | Schülerübersicht für den Lehrer (Nav nur für reine Lehrer, `NavMenu.razor:89-97`). Filter École + Classe (beide Pflicht), « Inscrit depuis le/jusqu'au ». Liste à 10, Suche Name/E-Mail/Schule/Niveau, 4 Sortierungen. Pro Schüler: Badge « Bloqué / en attente », Abo-Status, Kennzahlen Leçons / Exercices / Examens (bestanden/geschrieben) / Score (Teacher.razor:39-115). Schüler = aktuelle Klasse ∈ Zuordnungen und `UserSchools.CanManageStudents` (DS:3706-3746) |
| `/teacher/create` | RequireTeacher | Reiter « Leçons et exercices » (TLE eingebettet) / « Examens » (TE eingebettet), `?mode=lesson\|exam` |
| `/teacher/lesson-exercises` | RequireTeacher | (siehe Detailliste unten) |
| `/teacher/lessons` « Contenus des enseignants » | RequireTeacher (reine Lehrer → Redirect) | Direktionsansicht: Reiter, Filter École/Classe/Enseignant, Suche, Badges Fach / « Publié »/« Brouillon » / « Visible direction » / « Partagé par X » / « Saisi par la direction ». Mit Lehrerauswahl: Support-Editor (§4 D) |
| `/teacher-exams` « Examens (PDF) » | RequireTeacher (reine Lehrer → `/teacher/create?mode=exam`) | Liste mit Filter Schule/Klasse/Suche, Badges (§2.2); Buttons « Modifier », « Corrigé », « Sans solutions », « Réattribuer », Löschen. Editor-Modal mit 8 Fragetypen, Bildern aus der Mediathek, TTS-Feldern |
| `/teacher-lessons/{id}/view` | RequireSchulLeiter | A4-Reader mit « ← Page précédente / Page suivante → », Buttons « Exporter PDF avec solutions » / « Exporter sans solutions », kein Video |
| `/teacher/lesson-exercises/{id}/view` | RequireTeacher | Nur veröffentlichte Lektionen: Video, A4-Seiten, Aufgaben read-only mit leeren Kreisen/Quadraten; zeigt den **rohen Typ-String**; « Exporter PDF » |
| `/teacher-exams/{id}/view` | RequireTeacher | A4-Leseansicht, `?solutions=` (Lösungszuordnung über Optionstext), « Exporter PDF » |
| `/student` « Espace élève » | RequireStudent | Hinweis-Chips (Zyklus, offene Lektionen/Examen, Streak), Kacheln Score / « Leçons terminées » / Exercices (Punkte) / Examens (Punkte), « Ma classe » mit Zähler, Résultats, Note finale, « Supports de cours de vos enseignants », frühere Klassen |
| `/student/lessons` | RequireStudent | Ansicht « Parcours » / « Liste » (10 pro Seite), Suche « Titre, classe, résumé, matière… », Filter Matière/Enseignant/Statut (« Tous », « En cours », « Terminé »), « Aller à la leçon n° » |
| `/student/lessons/{id}` | RequireStudent | A4-Inhalt, Video, PDF-Link, Aufgaben-Wizard, « Voix : oui/non », Korrekturmodus-Banner "La date limite est dépassée. La correction est disponible en lecture seule…" |
| `/student/lessons/support/{id}` | RequireStudent | Support-Reader seitenweise, Video, « Retour aux leçons » |
| `/student/history` | RequireStudent | Zykluskarten (Score, Note finale, Réussi, Mention) + « Supports des classes précédentes » mit PDF |
| `/student/exams`, `/student/exams/{id}` | RequireStudent | Liste (10 pro Seite, Countdown, Gründe, warum nicht startbar) und Sitzung/Ergebnis/Korrektur |
| `/lektionen` | Role SuperAdmin | Katalog aller Lektionen (Titel, Zusammenfassung, Zahl der Aufgaben/Medien, DS:254-270) |

**Detail `/teacher/lesson-exercises`** (embedded):
- Block « Corrections à faire »: Filter, Suche, 50er-Schritte, Points/Commentaire, « Enregistrer la note ».
- Block « Mes leçons »: Filter École/Classe, Suche, Badges Klasse/Fach/« n exercice(s) »/« Leçon verrouillée »/« Soumissions existantes (vN) »/Frist.
- Buttons: « Modifier », « Voir », « PDF », « Date limite » (Modal « Modifier la date limite »), « Réattribuer ».
- Vollbild-Editor (§4 A).

### 5.2 PDF / A4

**Engine:** headless Chrome/Edge `--print-to-pdf`, 60 s Timeout, ein Prozess pro Request (PDFS:136-212). Ohne Browser: "Le moteur PDF n'est pas disponible sur ce serveur."

**Inhalt:**
- Jede gespeicherte Seite wird ein `article.lesson-a4-page` mit „Page N“; **keine Kopfzeile**.
- Übungslektion: dazu ein Abschnitt « Exercices » mit „Exercice {Order} : {Title}“, Consigne als Klartext, Optionen ○/□, Lücken „______“, sonst zwei Linien.
- **Nie Lösungen.**
- Nur private Bilder werden als base64 eingebettet; externe und `data:`-Bilder werden entfernt (PDFS:31-134).
- **Examen** (`ExamA4HtmlDocument.cs:10-61`):
  - Kopf „Niveau · Minuten · Punkte“, Zeile Nom/Date/Note ___ / Gesamtpunkte.
  - Blöcke „Exercice n — Titel“.
  - ✓ und Musterlösungen nur mit `solutions`.
  - Consigne und Beschreibung **ohne Bilder**.

**Tote QuestPDF-Klassen:** `LessonPdfDocument`, `DirectionLessonExercisePdfDocument`, `ExamPdfDocument`.

| Endpunkt | Policy | Regel | Datei |
|---|---|---|---|
| `GET /teacher/lesson-exercises/{id}/pdf-file` (P:533-564) | RequireTeacher | wie `/view`: nur veröffentlichte Lektionen | `…-sans-solutions.pdf` |
| `GET /teacher-lessons/{id}/pdf-file?solutions=` (P:566-606) | RequireTeacher | §1.1; `solutions` ändert **nur den Dateinamen** | `-avec-solutions` / `-sans-solutions` |
| `GET /teacher-exams/{id}/pdf-file?solutions=` (P:492-531) | RequireTeacher | Autor/Direktion (§1.1), Default ohne Lösungen | `{Titel}-corrige.pdf` / `-sans-solutions.pdf` |
| `GET /student/lessons/{id}/pdf-file` (P:666-688) | RequireStudent | wie Lektionsansicht | `lecon-et-exercices-sans-solutions.pdf` |
| `GET /student/lessons/support/{id}/pdf-file` (P:608-640) | RequireStudent | + `IsPdfExportEnabled` | `-eleve.pdf` |
| `GET /student/exams/{id}/pdf-file` (P:642-664) | RequireStudent | veröffentlicht + PDF-Flag + aktuelle Klasse + **abgeschlossener** Versuch; immer ohne Lösungen | `epreuve-sans-solutions.pdf` |

Antwort jeweils `inline` (`InlinePdfResponse.cs:8-16`).

### 5.3 Weitere Endpunkte und JS-Interop

**Endpunkte:**
- `GET /media-library/{assetId}` (Bild, P:357-374)
- `GET /media-library/videos/{assetId}?lessonId=` (P:690-711)
- Formel-API (§3.12)

**JS:**

| Funktion | Zweck |
|---|---|
| `skoulouStudentExerciseFeedback.vorladen/play` | gesprochenes Feedback |
| `SKOULOUAPPAudio.activate/testTone/getStatus`, `SKOULOUAPPTts.getStatus` | Audio-Diagnose in SL.cs:369-405 |
| `SKOULOUAPPTts.speak/stop` | Vorlesen |
| RichTextEditor-Interop | A4-Paginierung (`SplitIntoPagesAsync`) und Bildbibliothek |
| `SKOULOUAPPVideoMetadata` | Videodauer messen |
| `SKOULOUAPPScroll.scrollElementToTop` | Scrollen im Editor |

Ein globaler MutationObserver rendert KaTeX (`App.razor:88-99`).

---

## 6. Durch Tests festgelegte Regeln

**AnswerMatchingServiceTests**
- `SplitVariants` trennt an `<p>` und `<br>`.
- `SplitVariants` dekodiert Entities (`a < b`, `c & d`) und entfernt leere Zeilen.

**LearningServiceTests**

| Test | Geprüfte Regel |
|---|---|
| `GetLessonsForStudentAsync_AutorloseLektionenFilternNachKlasseUndFach` | Autorlose Lektion: Lehrerfilter = Lehrer der Klasse mit passendem Fach |
| `…_ZeigtNurLektionenFreigeschalteterNiveaus`, `…_SchaltetKeineParallelklassenFrei`, `…_OhneFreischaltung_LiefertKeineLektionen` | nur Zugangsklassen, keine Parallelklassen |
| `…_BehältFrüherFreigeschalteteLektionenNachNiveauwechsel` | Historie bleibt sichtbar |
| `SubmitAnswerAsync_*` | richtig → Lektion abgeschlossen (1/1); falsch → nicht abgeschlossen; fremdes Niveau oder unbekannte Aufgabe → `Succeeded=false` |
| `SubmitExerciseAsync_MultipleChoice_*` | exakte Menge richtig; unvollständig falsch; zusätzliche falsche Option falsch; leere Auswahl schlägt fehl |
| `SubmitExerciseAsync_SpeichertAssignmentVersionUndSnapshot` | Versuch trägt die Lektionsversion (3) und einen Snapshot mit dem Titel |
| `GetLessonDetailAsync_BeruecksichtigtNurAbgabenDerAktuellenVersion` | alte Version zählt nicht |
| `GetLessonDetailAsync_NachFristverlaengerung_ErlaubtAllenSchuelernErneuteAbgabe` | nach Frist keine Abgabe; Verlängerung → Version 2, alle dürfen neu abgeben, alte Versuche bleiben (1×v1, 2×v2) |
| `SubmitExerciseAsync_SentenceBuilding_AkzeptiertRichTextHtmlAntwort` | HTML-Antworten werden akzeptiert |
| `SubmitExerciseAsync_SentenceBuilding_BewertetGrossUndKleinschreibung` | „ich bin Paul“ ≠ „Ich bin Paul“ → **case-sensitiv** |
| `GetLessonDetailAsync_NachFrist_LiefertReviewMitLoesungen` | Korrekturmodus mit korrekten Options-IDs und eigener Auswahl |

**DashboardServiceTests (Bereich 03)**

| Test | Geprüfte Regel |
|---|---|
| `GetTeacherStudentsAsync_BerechnetKombiniertenScoreAusUebungenUndPruefungen` | 100 % + 0 % + Examen 80 % = **60 %**, Anzahl 3. Offene manuelle Aufgabe und unveröffentlichtes/offenes Examen zählen nicht |
| `GetStudentDashboardAsync_ZaehltNurVeroeffentlichteAufgabenUndLetztenVersuchJeAufgabe` | 100 %, 20/20 Punkte |
| `GetStudentDashboardAsync_ZeigtLetzteAktivitaetUndNurAktuelleSerieDerKlasse` | Streak 0 ohne bzw. bei alter Aktivität; Streak 2 bei heute + gestern |
| `GetLessonForEditAsync_BehaeltFristBeimBearbeiten` | Frist bleibt beim Bearbeiten erhalten |
| `SaveLessonAsync_MitAbgabenVorFrist_SperrtBearbeitungInAktuellerRunde` | Speichern scheitert, Version bleibt 1 |
| `SaveLessonAsync_MitAbgabenNachFrist_ErhohtVersionUndSpeichertAenderung` | Version 2, Titel geändert |
| `ChangeLessonSubmissionDueAsync_VerlaengerungOeffnetNeueAbgabeversionFuerAlle` | Verlängerung → Version 2, Altversuch bleibt v1 |
| `ChangeLessonSubmissionDueAsync_FremderLehrerDarfNichtVerlaengern` | fremder Lehrer scheitert |
| `GetTeacherStudentProgressAsync_*` | nur zugeordnete Schüler; ohne Zuordnung leer |

**TeacherLessonServiceTests**

| Test | Geprüfte Regel |
|---|---|
| `SaveLessonAsync_ErstelltLektionMitSeiten` | Seiten 1..n |
| `SaveLessonAsync_Systemniveau_WirdAbgewiesen` | System-Niveau → „niveaux standard“ |
| `SaveLessonAsync_SpeichertEingebettetesBild` | eingebettetes Bild bleibt erhalten |
| `SaveLessonAsync_AktualisiertUndEntferntSeiten` | Seiten werden entfernt und neu nummeriert |
| `GetLessonsForStudentAsync_*` | nur veröffentlicht; auch ohne Direktionsfreigabe; frühere Klassen; Markierung aktuelle Klasse/Historie |
| `GetHistoryLessonsForStudentAsync_ZeigtBeendeteKlasseOhneAktuelleKlasse` | Historie auch ohne aktuelle Klasse |
| `GetLessonDetailForStudentAsync_VerweigertAnderesNiveau`, `…_VerweigertAktiveGesperrteKlasse` | anderes Niveau bzw. gesperrte Klasse → null |
| `GetLessonDetailForStudentPdfExportAsync_ErfordertSchulFreigabe` | PDF nur mit `IsPdfExportEnabled` |
| `GetLessonsForTeacherOrSchoolDirectionAsync_*` | Direktion sieht Geteiltes derselben Schule; ohne Direktionsrecht keine fremden |
| `GetLessonDetailForDirectionAsync_LiefertGeteilteLektionSchreibgeschuetzt` | Leseansicht für die Direktion |
| `GetLessonDetailForPdfExportAsync_ErlaubtDirektionBeiFreigabe` | PDF für die Direktion bei Freigabe |
| `CopyLessonToKlasseAsync_*` | „(copie)“, Entwurf, Flags übernommen, Fach null bei fremder Schule, Seiten kopiert; fremder Autor → Exception; `publishDirectly` → veröffentlicht |

**DirektionsErfassungServiceTests / DirektionsErfassungSqlTests**
- **Erlaubt:** Schulleitung und Koordination derselben Schule; gleicher Nutzer.
- **Verweigert:**
  - Direktion bzw. Lehrer anderer Schule
  - gesperrte Direktion, gesperrter Lehrer, gesperrte Schule
  - Lehrer für einen Kollegen
  - Ziel ohne Lehrerrolle
- Lehrerliste: nur aktive Lehrer der eigenen Schule.
- **Support:** Autor = Lehrer, `ErfasstVon` = Leiter; nach Lehrer-Edit bleibt die Kennzeichnung. Fremde Schule → Unauthorized, nichts gespeichert. Nur Klassen des Lehrers. Veröffentlichen, Laden und Löschen durch fremde Direktion abgewiesen.
- **Examen:** analog. PDF für den Leiter ja, für einen fremden Leiter nein. Ein Koordinator der Schule darf löschen.
- **Übungslektion:** `CreatedByTeacherId` = Lehrer, `ErfasstVon` = Leiter; die Kennzeichnung bleibt nach Lehrer-Edit.

**ExamServiceTests / PruefungsZugangTests / ExamA4HtmlDocumentTests**
- Start erzeugt genau 1 Versuch; Wiederaufnahme mit gleicher Deadline und Entwurf.
- Nach der Deadline bewertet Start den Entwurf und startet nicht neu.
- Nach Abgabe kein Neustart, auch nicht bei Fristverschiebung; nur Neuzuweisung.
- Fremder Versuch → null.
- Abgeschlossenes Examen bleibt nach Klassenverlust gelistet.
- Ohne LevelEnrollment/Zyklus: Liste, Start, Entwurf, Abgabe und Korrektur funktionieren.
- Rechteverlust (gesperrt / archiviert / Schule inaktiv / Klasse entfernt / entveröffentlicht / superseded) sperrt auch einen laufenden Versuch.
- Frist vorbei → neue Antwort ignoriert, Entwurf bewertet.
- MultipleChoice exakt; Timeout → nicht bestanden; zweite Abgabe liefert das bestehende Ergebnis.
- `ChangeSubmissionDue` passt laufende Deadlines an; Nicht-Autor → Exception.
- Reassign: neue ID, Quelle superseded, alte Versuche bleiben; zweite Neuzuweisung → Exception.
- Löschen mit Versuchen bzw. System-Niveau abgelehnt; fremde Klasse mit gleichem Code abgelehnt.
- Schüler-PDF erst nach Abschluss und mit Flag.
- A4-HTML: Titel encodiert, `<script>` entfernt, ✓ nur mit Lösungen, private Bilder bleiben.
- SQL: 3 parallele Starts → 1 Versuch.

**SchulSperreInhaltsTests**
- Leseansicht Übungslektion: Direktion ja, Schüler mit `UserSchools`-Link nein, gesperrte Direktion nein, inaktive Schule nein.
- Gesperrte Schule: Prüfungs- und Lektionsaktionen leer/abgelehnt, nach Entsperren wieder da.

**SchuelerZugangServiceTests / SchuelerVideoZugangTests**
- Material ohne Abo vor und nach Zyklusabschluss zugänglich; das Dashboard zeigt « unlocked » und « nicht bezahlt ».
- Sperre, Archivierung oder inaktive Schule → kein Zugang.
- Fremde Klassen auch bei inkonsistenter Zuordnung nicht.
- Video ohne Abo nur mit aktiver Schul-/Klassenberechtigung und passender `lessonId`.

**AntwortTonServiceTests**
- Standard Stimme an; der Schalter wird gespeichert; ohne Schülerprofil → false.

**Weitere Tests**

| Test | Geprüfte Regel |
|---|---|
| FormelFavoritServiceTests | pro Benutzer; Duplikat aktualisiert; Entfernen |
| AzureBlobImageLibraryValidationTests | Anzeigename bereinigt; Alt-Text Pflicht, ≤ 250 |
| ResponsableMedienRechteTests | PO sieht nur eigene Schulen und löscht keine fremden Bilder |
| LessonExerciseA4PdfServiceTests | 3 gespeicherte Seiten = 3 PDF-Seiten; Übungen +1 Seite; Remote-Bild entfernt; Bildbreite und Ausrichtung bleiben. Browser-Tests **überspringen still** ohne Chrome |
| `richTextEditor.test.cjs` (19 Tests) | Bild verschieben, Größe ändern (80–670 px), Ausrichtung, Schrift-Picker. **Nicht getestet:** Paginierung, Formeln, Paste-Block |
| FinanzServiceTests | Lektionen und Examen bleiben nach Zyklusende veröffentlicht |

---

## 7. Schwächen & Verbesserungsvorschläge

### 7.1 Fachliche Fehler und Lücken

1. **Raten bis 100 %:** Unbegrenzte Wiederholungen ohne Abzug, und es zählt der letzte Versuch (LS:692-860). Wer Choice-Aufgaben durchprobiert, bekommt die volle Punktzahl, und der Score ist damit wertlos.
   → Versuchsrichtlinie pro Lektion (max. Versuche, Punkteabzug, „erster Versuch zählt“) und den Score auf dem ersten bzw. besten Versuch je Richtlinie aufbauen.
2. **Manuelle Korrektur nur mit Frist:** Ohne Frist erscheinen offene Antworten nie in der Liste und werden nie korrigiert (LS:885-889). Bei Examen fehlt die manuelle Korrektur ganz: `ManualPoints` wird nie geschrieben, und eine Textfrage ohne Musterlösung macht eine Prüfung **unbestehbar** (ES:541-558).
   → Ein gemeinsamer Korrektur-Workflow für Übungen und Examen, unabhängig von der Frist, mit Nachkorrektur.
3. **Punkte vs. Fragen:** Examen werten Fragen gleich und ignorieren `Points`, das PDF druckt aber „Note ___ / Gesamtpunkte“ (ES:555-557). GapFill hat nie Teilpunkte, obwohl `GradeGaps` die richtigen Lücken zählt. Drei verschiedene Score-Formeln (§3.9: Mittel der Prozente, punktgewichtete Historie, Examen als Fragenanzahl).
   → **Eine** punktbasierte Bewertung mit optionalen Teilpunkten (Lücken, Multiple Choice) und einer einzigen dokumentierten Score-Formel.
4. **Lösungen zu früh:**
   - Examen zeigen die Korrektur sofort nach der eigenen Abgabe, während Mitschüler noch schreiben (ES:108-191).
   - `AudioText` steht bei Diktaten im DOM (SE.razor:166).
   - Das Lückenfeedback „{c}/{t} correct(s)“ hilft beim Raten.

   → Sichtbarkeit der Lösungen als Regel: nach Frist für alle bzw. nach Sitzungsende. Audio serverseitig erzeugen (MP3 per URL), den Text nicht ausliefern.
5. **Timeout-Bewertung:** Die Client-Antworten werden verworfen und die Prüfung ist nie bestanden, auch bei 100 %. Der Client feuert bei gerundet 0 s, ob als normale Abgabe oder als Timeout ist daher zufällig. Die letzten bis zu 5 s Antworten gehen verloren (ES:463-467; SE.cs:158-173).
   → Karenzzeit (z. B. 30 s), Antworten bis Deadline + Karenz übernehmen, Hintergrundjob für das Auto-Submit.
6. **Datenverlust beim Bearbeiten:**
   - Das Entfernen einer Aufgabe löscht per Cascade alle historischen Versuche (DS:2865-2872, DB:314-318).
   - Exam-Edit erzeugt neue IDs (ES:663-681).
   - Varianten werden mit `\n` in Quill geladen und verschmelzen beim Speichern vermutlich (DS:2437).

   → Versionierte, unveränderliche Aufgaben (Revision pro Runde), Soft-Delete und Varianten als Liste im UI statt Textblock.
7. **Blockstruktur geht verloren:** Der Editor speichert jede Frage als eigene `Exercise` mit dem Blocktitel und lädt jede Frage als eigenen Block (TLE.cs:1572-1628). Der Schüler gruppiert nach gleichem Titel (SL.cs:521-559), das PDF druckt „Exercice 2 : Exercice 1“.
   → Echte Entität `ExerciseBlock` wie `ExamExercise`.
8. **Zwei Lektionsmodelle:** `Lesson` (Seiten als Marker-String) und `TeacherLesson` (Seitenzeilen) mit verschiedenen Rechten, Fach-Regeln, Video-Ablagen (`LessonMedia` vs. FK) und Readern. Der Support-Editor ist für reine Lehrer unerreichbar (TL.cs:664-674); CourseTheme wird nirgends gruppiert.
   → **Ein** Modell „Leçon“: Seiten + optionale Aufgabenblöcke + Video-FK. Lehrer und Direktion nutzen denselben Editor.
9. **Zwei Prüfmodelle:** Übungen haben `ExerciseType` als String mit Fallback SingleChoice, Examen ein Enum; Normalisierung dreifach dupliziert.
   → Gemeinsames Frage-Modell und **eine** Bewertungs-Engine für Übung und Examen.
10. **SentenceBuilding** ist nur ein Freitext (keine Wortbank). Die Lücken-Marker `[[n]]` werden nie geparst und im Schülertext roh angezeigt.
    → Wortbank-UI; Lücken inline aus Markern rendern und beim Speichern validieren (Anzahl Marker = Anzahl Lücken).
11. **Ohne Frist kein Korrekturmodus**, und die Neu-Zuweisung einer Übungslektion lässt die Quelle veröffentlicht (Duplikate beim Schüler).
    → Superseded-Logik wie bei Examen.
12. **Fortschritt und Zähler:**
    - Eine manuell mit 0 Punkten bewertete Textaufgabe gilt als „gelöst“ (LS:1096-1098).
    - `CompletedLessonsCount` sinkt nie.
    - Die Klassenkarte zählt auch unveröffentlichte Lektionen und Examen (DS:967-986).
    - Supports der aktuellen Klasse fehlen auf `/student`, solange kein Zyklus existiert (DS:1090-1121).
13. **Skoulou+** dreifach unterschiedlich definiert (§3.9); dazu `HasActiveSubscription` = „hat irgendeine Zugangsklasse“ (DS:1043).
14. **Historienzugang über stornierte Enrollments** (SZS:21-31 filtert `StorniertAmUtc` nicht).
15. **Zeitzone:** Eingabe und Anzeige erfolgen per `ToLocalTime()` auf dem **Server**, Streak nach UTC-Datum.
    → Explizite Schul-Zeitzone (Togo: UTC+0, keine Sommerzeit), Umrechnung im Client.

### 7.2 Sicherheit und Berechtigungen

- **Autorisierung in der Seite statt im Service:**
  - `DS.SaveLessonAsync` und `DS.GetLessonForEditAsync` prüfen im Lehrerpfad nichts (DS:2378, 2466-2510).
  - `TLS.SaveLessonAsync`/`CopyLessonToKlasseAsync` prüfen für den eigenen Lehrer keine Klassenzuordnung.
  - Video-Asset-IDs werden nicht gegen Schule und Klasse geprüft (DS:2709-2716).

  → Jede Schreib- und Leseoperation im Service mit Policy (`CanEditContent(user, content)`) und Tests je Rolle.
- **`IsSharedWithSchool` ist wirkungslos:** Die Direktion sieht und exportiert über die ungefilterte Liste und `canActForAuthor` alle Inhalte der Lehrer ihrer Schule (TLS:147-154, 404-406, 422-447; ES:1033-1035). Die UI behauptet das Gegenteil (`DirectionExamReader.razor:14`).
  → Fachlich entscheiden (§8), dann konsistent durchsetzen.
- **Inkonsistente Flag-Logik:**
  - Der Autor exportiert per URL trotz `IsPdfExportEnabled=false`.
  - `solutions` beim Support-PDF wirkt nur auf den Dateinamen.
  - Entwürfe sind für den Lehrer weder ansehbar noch druckbar (404, LS:396).
- **HTML wird unbereinigt gespeichert** und erst beim Rendern regex-basiert bereinigt (TLS:919-925, DS:2542). `https://`-Bilder sind erlaubt (Tracking).
  → Beim Speichern mit einer bewährten Bibliothek (z. B. Ganss.HtmlSanitizer) bereinigen; nur interne Medien zulassen.
- **Uploads:**
  - Bildgröße nur clientseitig, keine Magic-Bytes, Videodauer vom Client.
  - Löschschutz nur für Supports (ABS:436-447), Videos nie löschbar (verwaiste Blobs).
  - Die TL-Bildbibliothek ohne `RequiredSchoolId` erlaubt fremde Schulbilder.

  → Server-Limits, Typprüfung, eine Tabelle `MediaUsage` (Inhalt ↔ Asset) für den Löschschutz.
- Formel-API ohne Antiforgery und ohne Mengenlimit; CDN-Skripte (Quill/KaTeX) ohne SRI.
- Die Direktion kann nicht korrigieren; beim Handeln für einen Lehrer fehlt die Korrekturliste (TLE.cs:795-797). Das widerspricht dem Zweck „Lehrer ohne Gerät“.

### 7.3 Performance und Technik

- **Riesige Dateien:**

  | Datei | Zeilen |
  |---|---|
  | `DashboardService.cs` | 4804 |
  | `TeacherLessonExercises.razor.cs` | 1681 |
  | `RTE.js` | 1691 |
  | `TeacherLessons.razor.cs` | 1407 |

  Dazu Logik doppelt: Typ-Normalisierung 3×, Dateinamen-Logik 4×, ProgressReadStream 3×.
- **Laden und Rechnen:**
  - Listen laden alles und paginieren im Client.
  - Examenlisten laden alle Versuche nur zum Zählen (ES:856-866).
  - `/student/lessons` ruft das komplette Schüler-Dashboard nur für einen ungenutzten `levelCode` (StudentLessons.cs:233-234; LS:47 unbenutzt).
  - CombinedScore lädt alle Versuche aller Schüler in den Speicher.
- **Jeder Bildabruf eines Schülers** macht LIKE-Scans über HTML-Spalten (ABS:478-494) und liefert kein Cache-Control.
  → Medienrechte über `MediaUsage`; Ergebnis cachen.
- **PDF:** ein Chrome-Prozess pro Request, ohne Limit, ohne Kopfzeile, KaTeX wird im PDF vermutlich nicht gerendert.
  → Eine PDF-Engine mit Warteschlange, Kopf (Schule, Klasse, Lehrkraft, Titel) und gerenderten Formeln.
- **Lazy Auto-Submit im Lesepfad** (ES:68-72); Submit ohne Lock, kein Unique-Index (Attempt, Question).
- **Toter Code:**
  - `LektionsFortschritt*`, die QuestPDF-Dokumente, `GetHomeCatalogAsync`
  - `TLS.GetLessonsForStudentAsync` (nur Tests), `LS.GetLessonDetailForDirection*`
  - `ExamType`/`ManuelleNote`-Methoden, `ITextToSpeechProvider.SpeakAsync`-Pfad
  - Admin-Lektionsverwaltung (`KlassenVerwaltungSichtbar=false`), Video-Upload-Code in TL/TLE
  - `IsSchoolLeaderAsync` (TLS:980)
- **UI-Texte:**
  - Gemischtsprachig: „Richtig!“ (SL.cs:169), „Audio-Diagnose konnte nicht gelesen werden.“
  - Deutsche Fehlermeldung bei Examen (ES:440-449)
  - Rohe HTML-Ausgabe von Schülerantworten in der Exam-Korrektur (SE.razor:54, 63)
  - Timer `mm:ss` ohne Stunden bei bis zu 600 min
  - Löschen ohne Bestätigung (TL, TE)

### 7.4 Empfehlungen für den Neubau (Zusammenfassung)

1. **Inhaltsmodell:** `Lecon` (Seiten, Video-FK, Fach, Thema) → `AufgabenBlock` → `Frage` (gemeinsam für Übung und Examen) → `Option`/`Lösung`. `Examen` referenziert eigene Blöcke.
2. **Runden:** `Zuweisung` (Klasse, Frist, Versuchsrichtlinie, Lösungs-Sichtbarkeit, Version). Versuche hängen an der Zuweisung; Inhalte sind pro Runde eingefroren.
3. **Bewertungs-Engine:** eine Klasse für alle Typen, punktbasiert, Teilpunkte optional, Toleranz konfigurierbar (Groß-/Kleinschreibung, Akzente), umfassend unit-getestet.
4. **Korrektur:** gemeinsame Warteschlange für Übung und Examen; Direktion darf für den Lehrer korrigieren; Nachkorrektur mit Protokoll.
5. **Rechte:** zentrale Policy-Schicht; das Flag „Visible par la direction“ fachlich klären; ErfasstVon beibehalten.
6. **Medien:** `MediaUsage`-Tabelle, Server-Validierung, Video-Löschen, signierte Kurzzeit-URLs oder ein gecachter Proxy.
7. **Scores:** eine Formel (Punkte-gewichtet), ein Service, materialisierte Werte statt Live-Aggregation.

---

## 8. Offene Fragen

1. Sollen Übungen beliebig oft wiederholbar bleiben? Wenn ja: Zählt der erste, der beste oder der letzte Versuch, und gibt es Abzug?
2. Score: Mittel der Prozentwerte (heute CombinedScore) oder punktgewichtet (heute Historie)? Sollen überfällige, nicht bearbeitete Examen ebenfalls als 0 zählen (heute nur Übungen)?
3. Sollen Examenspunkte (`Points`) wirken, und soll es Teilpunkte (Lücken, Multiple Choice) geben?
4. Wann sehen Schüler Examenslösungen: sofort, nach Frist für alle, oder erst nach Freigabe durch die Lehrkraft?
5. Bleibt die Antwortprüfung **case-sensitiv** (per Test festgelegt), oder wird sie pro Aufgabe einstellbar? Sollen Umlaute weiter als ä = a gelten?
6. Darf die Direktion private, nicht geteilte Inhalte der Lehrer sehen und exportieren (heute faktisch ja)? Was bedeutet « Visible par la direction » dann?
7. Sollen reine Lehrer Supports (mehrseitige Skripte) selbst erstellen können? Sollen Support und Übungslektion zu einem Inhaltstyp verschmelzen?
8. Ist das Fach Pflicht (heute bei Übungen ja, bei Supports nein)? Dürfen Lehrkräfte Fächer selbst anlegen (heute nur über den Support-Editor)?
9. Darf die Direktion manuell korrigieren, und darf korrigiert werden, bevor die Frist abläuft?
10. Soll ein Examen pro Schüler neu zugewiesen werden können (Krankheit, Nachschreiber), statt nur klassenweise?
11. Soll « Réattribuer » einer Übungslektion die Quelle für Schüler ausblenden (wie bei Examen)?
12. Soll LevelEnrollment/Skoulou+ künftig irgendeinen Lerninhalt freischalten (z. B. Videos, PDF), oder bleibt der Zugang rein klassenbasiert? Wie wird „Skoulou+“ einheitlich definiert?
13. Sollen stornierte Zyklus-Enrollments weiterhin Lesezugriff auf die frühere Klasse geben?
14. Soll das gesprochene Feedback mehrsprachig werden (heute fest Französisch), und soll der Vorname wieder genannt werden?
15. Soll das « Tableau pédagogique » Scores auf das Fach bzw. die Klasse der Lehrkraft beschränken (heute alle Fächer des Schülers)?
16. Sollen PDFs eine Kopfzeile (Schule, Klasse, Lehrkraft) und für Lehrkräfte eine Lösungsversion der Übungslektion bekommen?
17. Welche Medien-Grenzen gelten wirklich (Video 8 GB / 1 h)? Bandbreite und Kosten in Westafrika sprechen für deutlich kleinere Grenzen bzw. Transkodierung.
