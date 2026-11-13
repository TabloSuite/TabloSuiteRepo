# 05 – Finanzen & Abrechnung (Fachkonzept für den Neubau)

Stand: Analyse der Alt-App vom 13.11.2026. Quelle der Wahrheit für das *Was*; das *Wie* wird neu entworfen.
Verweise `Datei:Zeile` beziehen sich auf die Alt-Projekte: `Core/…` = `SKOULOUAPP.Core`, `Web/…` = `SKOULOUAPP.WebApp`, `Tests/…` = `SKOULOUAPP.Tests/Services`.
Alle Beträge in FCFA. Französische UI-Begriffe stehen in « … » exakt wie in der App.

---

## 1. Zweck & beteiligte Rollen

### 1.1 Vier getrennte Geldkreisläufe

| Kreislauf | Wer zahlt an wen | Erfassung in der App | Status in der Alt-App |
|---|---|---|---|
| **A. Schulbuchhaltung** « Recettes & dépenses de l'école » | beliebige Einnahmen/Ausgaben der Schule | manuelle Buchungen mit optionalem Beleg + automatische Systembuchungen | aktiv |
| **B. Schulgebühren** « Scolarité » | Eltern/Schüler → Schule (bar/offline) | Raten- oder Gesamtzahlung je Schüler und Klassenzyklus, erzeugt Einnahme + « Reçu de paiement » | aktiv |
| **C. Plattformabrechnung** « Facturation SKOULOU » | Schule → Plattform (Überweisung, offline) | Festpreis je Zyklusteilnehmer, Rechnung entsteht automatisch beim Zyklusabschluss; Schule meldet Überweisung, SuperAdmin bestätigt | aktiv (neue Zyklen) + Altpfad |
| **D. Provisionen** « Commissions » | Plattform → Direktion / Lehrerpool / Responsable | nur noch **historisch** (Alt-Abos mit Provisions-Snapshot); neue Zyklen erzeugen **keine** Schul-/Lehrer-/PO-Anteile | faktisch abgeschafft |

Es gibt **keinen Zahlungsanbieter**, kein Mobile Money, keinen Zahlungs-Webhook (einziger Webhook ist Postmark-Inbound-Mail, `Web/Program.cs:332`). Jeder Geldeingang wird von Menschen manuell erfasst bzw. bestätigt. Es gibt **keine Hintergrundjobs** (kein `AddHostedService`), keine automatischen Mahnungen.

Verbindliche Fachentscheidung (Doc `docs/Automatischer-Schuelerzugang-und-Zyklusabrechnung.md`): Schülerzugang hängt **nicht** von Zahlungen ab; die Schule schuldet pro Zyklusteilnehmer den vollen, beim Start eingefrorenen Plattformpreis; Schul-/Lehrerprovisionen entfallen; die ProductOwner-Vergütung betrifft nur die Plattform intern und mindert nie die Schulrechnung.

### 1.2 Rechteprüfung (wie im Code erzwungen)

**`FinanzService.GetFinanceAccessAsync` → (Menu, Dashboard, Entries, Cycles)** (`Core/Services/FinanzService.cs:24-56`):
1. Benutzer gesperrt (`IsBlocked`) → keine Rechte.
2. `SuperAdmin` → (ja, ja, nein, nein) für **jede** Schule (nur lesen).
3. `ProduktOwner` → (ja, ja, nein, nein) nur wenn ein `UserSchool`-Link zur Schule existiert (ohne weitere Flags).
4. Sonst muss die Schule „freigegeben“ sein: `IsActive` **und** kein gesperrter SchulLeiter mit dieser Schule verknüpft (`Core/Services/SchulZugangService.cs:12-17` – die schulweite Sperre ist also „Direktor gesperrt“), und es muss ein `UserSchool`-Link mit `CanManageStudents` bestehen.
5. `SchulLeiter` → (ja, ja, ja, ja).
6. `Koordinator` mit `CanAccessFinances` → (ja, `CanViewFinanceDashboard`, `CanCreateFinanceEntries`, `CanManageCyclePayments`); sonst nichts. Lehrer/Schüler: nichts.

**`CanAccessBillingAsync`** (`FinanzService.cs:58-75`): SuperAdmin immer; PO bei Schul-Link; sonst freigegebene Schule + `CanManageStudents` + (SchulLeiter **oder** Koordinator mit `CanAccessFinances && CanAccessBilling`).

**Koordinator-Finanzrechte** setzt die Direktion im Benutzerformular pro Schule (`Core/Services/DashboardService.cs:4199-4209`): `Menu = gewähltMenu && (Dashboard||Entries||Cycles||Billing)`; jedes Einzelrecht = `Menu && gewählt`. (Gleiche Regel in `FinanzService.SetCoordinatorFinanceAccessAsync:77-109`, die von keiner Seite aufgerufen wird.)

**UI-Zusatzregel** (`Web/Components/Pages/Finanzen.razor.cs:469-504`): Plattformrollen (SuperAdmin, ProduktOwner) bekommen in « Finances » nie die Reiter Erfassen/Zyklen/Facturation; jede Mutation prüft vorher erneut `GetFinanceAccessAsync` serverseitig.

**Wichtig:** Service-Methoden prüfen Rechte nur, wenn der Aufrufer `autorisierungErzwingen: true` übergibt; `userId == Guid.Empty` gilt als erlaubt (`FinanzService.cs:111-121`, `127-141`). `AboService` und `ProvisionsService` prüfen Aufrufer gar nicht (UI-Flag `istSuperAdmin`).

### 1.3 Matrix „wer darf was / sieht welche Zahlen“

| Aktion / Sicht | SchulLeiter | Koordinator | Lehrer | Schüler / Eltern | ProduktOwner | SuperAdmin |
|---|---|---|---|---|---|---|
| Journal + Saldo der Schule (« Finances de l'école ») | eigene Schule | wenn Dashboard-Recht | – | – | zugeordnete Schulen, lesend | alle Schulen, lesend |
| Buchung/Kategorie anlegen, eigene Buchung löschen | ja | wenn Entries-Recht | – | – | – | – (UI); Service erlaubt Löschen von Systembuchungen |
| Zyklus starten/schließen, Rate erfassen | ja | wenn Cycles-Recht | – | – | – | – |
| « Reçu de paiement » | ja | wenn Dashboard oder Cycles | – | eigener Schüler / verknüpfte Eltern | zugeordnet | ja |
| Beleg « justificatif » öffnen | ja | Dashboard oder Entries | – | – | zugeordnet | ja |
| Rapport / PDF | ja | wenn Dashboard | – | – | zugeordnet | ja |
| Facturation ansehen | ja | wenn Billing-Recht | – | – | zugeordnet | alle |
| Überweisung melden / Rechnung ablehnen | ja | wenn Billing-Recht | – | – | – | – |
| Manuelle Rechnung erstellen / Mail erneut senden | – | – | – | – | zugeordnete Schulen | ja |
| Sammelabrechnung, Zahlungseingang bestätigen | – | – | – | – | – | ja |
| Preise (global, je Schule), PO-Prozentsatz | – | – | – | – | sieht nur | ja |
| Teilnahme stornieren (/cycle-participants) | – | – | – | – | – | ja |
| /provisionen | Schulanteil + Lehrerpool (jede verknüpfte Schule, **ohne** Finanzrechtprüfung) | wie SchulLeiter | Lehrerpool abgeschlossener Perioden | – | eigener PO-Anteil | alle Anteile inkl. « Part opérateur » |
| Elternbereich: Gebührenstand | – | – | – | Eltern: laufende Zyklen des Kindes | – | – |

---

## 2. Daten

### 2.1 Schulbuchhaltung

**`FinanzKategorie`** (`Core/DataModels/Finanzen/FinanzKategorie.cs`): `SchulID` (null = global, SuperAdmin), `Bezeichnung` (max 150), `Richtung`, `SystemCode`, `IstSystemTyp`, `IstAktiv` (nur aktive werden gelistet; Deaktivieren gibt es in der UI nicht).

**`FinanzBuchung`** (`…/FinanzBuchung.cs`): `SchulID`, `KategorieID`, `Richtung` (eingefroren aus Kategorie), `Betrag` (>0, immer positiv; decimal 18,2), `Wirkungsdatum` (frei, rückwirkend), `BeschreibungHtml`, `Belegnummer` (max 100, Pflicht nur fachlich gedacht, technisch optional), `Buchungsnummer` (berechnet `B-`+GUID ohne Bindestriche, unique, `Web/Data/ApplicationDbContext.cs:616-620`), `ErfasstVonUserID`, `HerkunftReferenzID` (Id der Quelle: RatenZahlung, LevelEnrollment oder Durchlauf).

**`FinanzBeleg`** (`…/FinanzBeleg.cs`): 1:1 zu Buchung (unique `BuchungID`, Cascade), `SchulID`, `Dateiname` (255), `Inhaltstyp`, `BlobName` (privater Blob `schools/{schulId:N}/receipts/{belegId:N}`), Legacy-Spalte `Inhalt` (Bytes in DB; Migrationsmethode `BestehendeFinanzBelegeMigrierenAsync`, nirgends aufgerufen).

**`FinanzRichtung`**: `Einnahme`=0, `Ausgabe`=1.

**`FinanzSystemCode`** (`…/FinanzSystemCode.cs`) – Systemkategorien sind global, nicht löschbar, nicht manuell bebuchbar:

| Code | Richtung | Anzeigename | Verwendung |
|---|---|---|---|
| 0 `Keiner` | – | frei | manuelle Kategorien |
| 1 `KursNiveauZahlung` | Einnahme | « Paiement de classe », in Listen « Scolarité » | jede Ratenzahlung (aktiv) |
| 2 `VerkaufLehrmaterial`, 5 `SonstigeEinnahme`, 100 `Miete`, 101 `Steuer`, 102 `LehrerLohnKosten`, 103 `LehrerLohnSkoulouAppAbo`, 105 `SonstigeAusgabe` | | | definiert, **nie erzeugt** (tot) |
| 3 `SkoulouAppAboZahlung` | Einnahme | « SKOULOUAPP-Abo · Paiement élève » | nur Alt-Abos |
| 4 `SkoulouAppAboAnteil` | Ausgabe | « SKOULOUAPP-Abo · Part plateforme » bzw. « SKOULOUAPP · Frais de plateforme par cycle » | Alt-Abos **und** neue Zyklus-Plattformkosten (dieselbe Kategorie!) |
| 6 `…SchulleiterAnteil`, 8 `…ProductOwnerAnteil` | Ausgabe | « Part direction », « Part du responsable produit » | nie erzeugt, nur ausgewertet |
| 7 `SkoulouAppAboLehrerAnteil` | Ausgabe | « SKOULOUAPP-Abo · Fonds enseignants » | nur Alt-Abos |
| 104 `SchulgebuehrVerlust` | Ausgabe | « Verlust Schulgebühr » (deutsch in der UI!) | offene Scolarité beim Zyklusabschluss |

### 2.2 Zyklus, Gebühren, Teilnahme

**`KursNiveauDurchlauf`** = Klassenzyklus « cycle » (`…/KursNiveauDurchlauf.cs`): `SchulID`, `KlasseId`, `NiveauPreis` (= Schulgebühr « Scolarité » je Schüler, beim Start eingegeben), `PlattformPreisProSchueler` (eingefrorener Plattformpreis; **null = historischer Zyklus** vor Einführung der Automatik), `StartDatum`, `EndDatum` (null = aktiv), `ErstelltVonUserID`, `RatenZahlungen`. Pro Schule+Klasse höchstens **ein** aktiver Zyklus (nur per Code/Applock, Index `(SchulID, KlasseId, EndDatum)` ist **nicht** unique).

**`RatenZahlung`** (`…/RatenZahlung.cs`): `DurchlaufID` (Cascade), `StudentId`, `Betrag`, `ZahlungsArt` (`Cash`=0 « Cash (total) » / `Rate`=1 « Échelonné »), `Wirkungsdatum`, `BeschreibungHtml`, `FinanzBuchungID` (SetNull), `ErfasstVonUserID`. Keine Fälligkeiten, kein Ratenplan.

**`LevelEnrollment`** = Zyklusteilnahme / Alt-„Abo“ (`Core/DataModels/Courses/LevelEnrollment.cs`): `StudentId`, `KlasseId`, `DurchlaufId`, `IstAktuell`, `StorniertAmUtc/StorniertVonID/StornoGrund (≤300)`, `AboTyp` (nur noch `SkoulouApp`=0; SKOULOU+ existiert nicht mehr, `SkoulouPlusAnzahl => 0`), `IsUnlocked`, `IsPaid`, `PaidAtUtc`, `UnlockedAtUtc`, `PriceAtEnrollment` (Preis-Snapshot), `SchoolLeader/Teacher/ProductOwnerCommissionAtEnrollment` (Snapshots, bei neuen Zyklen 0), `AbgerechnetInPeriodeId` (FK SetNull). Unique `(StudentId, KlasseId)` gefiltert `IstAktuell=1`; unique `(StudentId, DurchlaufId)` wenn `DurchlaufId` gesetzt (`ApplicationDbContext.cs:442-450`). FK Student und Klasse **Cascade** (Löschen eines Schülers löscht Abrechnungspositionen).

**`KlassenAbschlussEntscheidung`** (Bereich Prüfungen) bestimmt nach Abschluss die „Kohorte“ eines geschlossenen Zyklus in Finanzübersichten (siehe 3.4).

**`Klasse`**: `Price` (Default 12 500 im Entity, 10 000 im Formular `CourseFormModel.cs:27`, Range 0–1 000 000) = **Vorschlag** für die Scolarité; `AutoAssignSkoulouAppAboOnCycleStart` (wirkungslos, siehe 7).

### 2.3 Preise & Provisionsparameter

- **`SkoulouAppKonstanten`** (`Core/DataModels/Schools/AboTyp.cs:17-47`): `StandardPreisAbo = 12 500`; `StandardProvisionSchulleiterPlus = 500`, `…LehrerPlus = 1 000` → Prozent 4 % bzw. 8 %; PO 0 %. `BerechneProvision(preis, prozent) = Round(preis × prozent / 100, 2, AwayFromZero)`.
- **`CommissionSettings`** (eine Zeile, älteste nach `ErstelltAmUtc` gilt): `GlobalSubscriptionPrice` (Default 12 500), `SchoolLeaderPercent` 4, `TeacherPercent` 8, `ProductOwnerPercent` 0.
- **`School`**: `SubscriptionPriceOverride` (Schulpreis, Vorrang), `SchoolLeader/Teacher/ProductOwnerCommissionPercentOverride` (nur Alt-Pfad). `EffektiverPreis(global) = Override ?? global` (`School.cs:61-62`).

### 2.4 Plattformabrechnung

**`Abrechnungsperiode`** = Rechnung « facturation » (`Core/DataModels/Schools/Abrechnungsperiode.cs`): `SchulID`, `VonUtc` (inkl.), `BisUtc` (exkl. laut Kommentar, faktisch Tagesende inkl.), `Umsatz` (Rechnungsbetrag, decimal 12,2), `AnzahlAbonnements`, `Status`, `IstAbgeschlossen`, `AbgeschlossenAmUtc`, `ErstelltVonUserId` (null = Alt/Automatik), `AutomatischDurchKlassenabschlussErstellt`, `AusgeloestDurchDurchlaufId`, `AutomatischAusgeloestVonUserId`, `AutomatischErstelltAmUtc`, `IstSammelabrechnungAusGeschlossenenKlassen`, `AblehnungsGrund`, `SchulLeiterReaktionAmUtc`, `ZahlungGemeldetVonUserId`, `BestaetigtAmUtc`, `BestaetigtVonSuperAdminId`, Positionen = `LevelEnrollment.AbgerechnetInPeriodeId`.

**`AbrechnungStatus`**: `Erstellt`=0 « En attente du directeur » · `Akzeptiert`=1 « Acceptée et virée » (Schule meldet Überweisung) · `Abgelehnt`=2 « Refusée » · `Bestaetigt`=3 « Confirmée et terminée » (SuperAdmin hat Bankeingang geprüft) (`Web/Components/Pages/Abrechnung.razor.cs:441-448`).

**`UserSchool`-Finanzflags**: `CanAccessFinances`, `CanViewFinanceDashboard`, `CanCreateFinanceEntries`, `CanManageCyclePayments`, `CanAccessBilling`.

**Tote Tabellen**: `LehrerProvisionsAuszahlung` (Wochenstunden-Snapshot, Betrag, `AlsErhaltenBestaetigtAmUtc`) und `ProduktOwnerProvisionsAuszahlung` werden nie geschrieben; Tests prüfen sogar, dass sie leer bleiben. `LehrerNiveauZuordnung.Wochenstunden` („für die Provisionsverteilung“) hat keine Geldwirkung mehr.

---

## 3. Geschäftsregeln

### 3.1 Plattformpreis und Preis-Snapshot
- **R1 Preisermittlung** (`Core/Services/ZyklusTeilnahmeService.cs:116-127`): `Preis = School.SubscriptionPriceOverride ?? CommissionSettings.GlobalSubscriptionPrice ?? 12 500`. Muss > 0 sein und höchstens 2 Dezimalstellen haben, sonst Fehler « Le tarif SKOULOU fixé par l'administrateur doit être positif et comporter au maximum deux décimales. »
- **R2 Snapshot**: beim Zyklusstart in `KursNiveauDurchlauf.PlattformPreisProSchueler` eingefroren (`FinanzService.cs:796-806`); **jede** Teilnahme dieses Zyklus – auch spätere – erhält genau diesen Preis als `PriceAtEnrollment` (`ZyklusTeilnahmeService.cs:133, 160`). Spätere Preisänderungen wirken nur auf neue Zyklen.
- **R3 Preis pflegen** nur SuperAdmin: globaler Preis `ProvisionsService.SaveCommissionSettingsAsync` (`Core/Services/ProvisionsService.cs:43-71`; Prüfung Preis > 0, ≤ 2 Dezimalen, Prozente ≥ 0, Summe ≤ 100 %; gespeichert werden nur `GlobalSubscriptionPrice` und `ProductOwnerPercent`); Schulpreis `DashboardService.SaveSchoolAsync` (`DashboardService.cs:1411-1450`, leer = globaler Preis). Ungültig: 0, −1, 3000,001.
- **R4 Kein Pro-rata**: wer am letzten Zyklustag dazukommt, kostet den vollen Preis (Plattform) bzw. schuldet die volle Scolarité.

### 3.2 Klassenzyklus starten / schließen
- **R5 Start** (`FinanzService.DurchlaufStartenAsync:744-818`), Reihenfolge der Prüfungen: Recht `Cycles`; `niveauPreis > 0` (« Le prix de la classe doit être supérieur à 0. »); Klasse gehört der Schule und ist kein Systemniveau; kein aktiver Zyklus derselben Klasse (« Un cycle est déjà actif pour cette classe. Veuillez d'abord le clôturer. »); Startsperre (R6); dann Zyklus anlegen, Plattformpreis einfrieren, Teilnehmer erfassen (R8) – alles in einer Serializable-Transaktion mit App-Lock.
- **R6 Startsperre** (`FinanzService.cs:728-742`): Nur wenn der **zuletzt geschlossene** Zyklus der Klasse **historisch** ist (`PlattformPreisProSchueler == null`) und seine automatische Rechnung weder `Akzeptiert` noch `Bestaetigt` ist → « Avant de démarrer un nouveau cycle, déclarez le virement de la facturation du cycle précédent dans Facturation SKOULOU. ». Für neue Zyklen gibt es **keine** Startsperre wegen offener Plattformrechnungen (Test `DurchlaufStartenAsync_NeueZyklenTrennenOffeneForderungVonStartfreigabe`: alle Status → Start erlaubt).
- **R7 Abschluss** (`FinanzService.DurchlaufBeendenCoreAsync:864-1051`): Recht `Cycles`; Zyklus nicht schon geschlossen (« Le cycle est déjà clôturé. »); `endDatum ≥ StartDatum` (Zukunft erlaubt). Fehlende Abschlussnoten blockieren nicht. Ablauf neuer Zyklus: (a) offene Scolarité berechnen (R13), (b) Teilnehmer letztmalig ergänzen, (c) Rechnung + Plattformaufwand erzeugen (R18), (d) `EndDatum` setzen, (e) aktuelle `LevelEnrollment` der Klasse auf `IstAktuell=false`, (f) Verlustbuchung (R14). Schüler **bleiben** in der Klasse, Lektionen/Prüfungen bleiben veröffentlicht. Historischer Zyklus: Schüler verlieren `CurrentKlasseId`, Lektionen/Prüfungen der Klasse werden **entveröffentlicht** (`:988-1020`), Rechnung nur, wenn der Abschluss über die Direktor-Variante läuft (R27).
- Danach sind keine Raten mehr möglich (« Le cycle est clôturé. Aucun paiement n'est possible avant le démarrage d'un nouveau cycle pour la classe. »).

### 3.3 Wer ist abrechnungspflichtiger Zyklusteilnehmer (Plattform)
- **R8** `ZyklusTeilnahmeService.TeilnehmerErgaenzenAsync` (`:129-165`): alle `StudentProfile` mit `SchoolId = Zyklus.Schule`, `CurrentKlasseId = Zyklus.Klasse`, `!IsArchived` und `(!IsSelfRegistered || !IsBlocked || AufnahmeBestaetigtAmUtc != null)` – d. h. nicht angenommene Selbstregistrierungen zählen nicht, angenommene zählen auch bei späterer persönlicher Sperre.
- Bereits erfasste Schüler (auch stornierte) werden nicht erneut angelegt → **einmal je Schüler und Zyklus**. Ältere `IstAktuell`-Teilnahmen derselben Klasse werden deaktiviert. Neue Teilnahme: `IsUnlocked=true`, `IsPaid=false`, `PaidAtUtc=null`, alle Provisionen 0.
- **R9 Auslöser** (gemeinsamer Pfad `AktiveKlasseErfassenAsync`, `:24-43`): Zyklusstart, Zyklusabschluss, Klassenzuweisung/Annahme/Entsperren eines Schülers (`DashboardService.cs:3187, 3446, 3928`), Versetzung (`ExamService.cs:1382`). Nur aktive Zyklen mit Plattformpreis.
- **R10 Austritt, Sperre, Archivierung, Klassenwechsel löschen die Teilnahme nicht**. Wechselt ein Schüler mitten im Zyklus in eine andere Klasse mit aktivem Zyklus, entsteht dort eine zweite Teilnahme → **doppelte Plattformkosten** für denselben Schüler (so gewollt? → Offene Frage).
- **R11 Stornierung einer Fehlzuordnung** (`ZyklusTeilnahmeService.StornierenAsync:90-114`): nur aktiver, nicht gesperrter SuperAdmin; Grund Pflicht, ≤ 300 Zeichen; nur neue Zyklen, nur solange Zyklus offen und Position nicht abgerechnet; nicht doppelt. Setzt nur `StorniertAm/Von/Grund` – keine Löschung, keine Klassen-/Zugangsänderung. Stornierte Positionen werden nicht abgerechnet und nicht neu angelegt.

### 3.4 Schulgebühren « Scolarité »
- **R12 Soll/Bezahlt/Offen je Schüler** (`Core/Services/FinanzUebersicht.cs:170-178`): `Soll = NiveauPreis` des Zyklus (für alle gleich), `Bezahlt = Σ RatenZahlung des Schülers im Zyklus`, `Offen = max(0, Soll − Bezahlt)`, Status « Payé » wenn `Bezahlt ≥ Soll`, sonst « En cours ». „Überfällig“ gibt es nicht (keine Fälligkeitsdaten); Kennzahl « Paiements en attente » / « Aucun retard » = Summe offen in aktiven Zyklen > 0.
- Teilnehmer der Gebührensicht: aktiver Zyklus → aktuelle Klassenmitglieder (`CurrentKlasseId`); geschlossener Zyklus → Schüler mit `KlassenAbschlussEntscheidung` für diesen Zyklus, sonst **alle** Schüler, die je eine Teilnahme an dieser Klasse hatten (`FinanzService.cs:1755-1787`) – nicht identisch mit der Plattform-Teilnehmerliste.
- **R13 Zahlung buchen** (`FinanzService.ZahlungBuchenAsync:1060-1149`): `Betrag > 0`; Zyklus aktiv; Schüler muss **aktuell** in der Klasse sein (« L'élève n'est pas inscrit dans cette classe. »); bei `Cash` muss `Betrag == max(0, NiveauPreis − bisher bezahlt)` sein (« Le paiement cash doit correspondre au montant restant à payer. »). Bei `Rate` **keine Obergrenze** (Überzahlung möglich), kein Datumsbereich. Erzeugt `RatenZahlung` + Einnahmebuchung Kategorie `KursNiveauZahlung` mit `HerkunftReferenzID = Zahlung.Id`. Danach Mail an den Schüler (R40) und Link « Imprimer le reçu ».
- **R14 Offene Schuld beim Abschluss → Verlust** (`FinanzService.cs:1155-1168`, `1022-1041`): `offen = max(0, Anzahl aktuelle Klassenmitglieder × NiveauPreis − Σ aller Zahlungen des Zyklus)` (Klassen-Netto, nicht je Schüler!). Ist `offen > 0`, Ausgabebuchung `SchulgebuehrVerlust` über `offen`, Wirkungsdatum = Enddatum, Text « Frais scolaires impayés lors de la clôture du cycle de classe {Code} : {Betrag} FCFA. ». UI: « Le solde impayé (…) est enregistré comme perte. »
- Eine Zahlung kann **nicht storniert/korrigiert** werden (keine Funktion; Journal zeigt « Corriger depuis le paiement »).

### 3.5 Schulbuchhaltung
- **R15 Manuelle Buchung** (`FinanzService.BuchungErfassenAsync:342-448`): Recht `Entries`; `Betrag > 0`; Belegnummer ≤ 100; Datei optional, aber wenn vorhanden: Name ≤ 255, 5 B–25 MB, Endung + Magic Bytes: `.pdf` (`%PDF-`), `.png`, `.jpg/.jpeg` (`FF D8 FF`) (`:450-465`); Kategorie muss global oder schuleigen sein; Systemkategorien verboten (« Les catégories automatiques SKOULOU ne peuvent pas être saisies manuellement. »). Blob wird vor dem DB-Commit hochgeladen und bei DB-Fehler gelöscht.
- **R16 Kategorien** (`:240-334`): Name Pflicht (getrimmt); Duplikat = gleiche Richtung + gleicher Name in Schule **oder** global; SuperAdmin legt globale, alle anderen schuleigene an. Löschen nur freie, unbenutzte Kategorien; globale nur SuperAdmin.
- **R17 Löschen einer Buchung** (`:519-564`): nur Ersteller (oder SuperAdmin); Systembuchungen nur SuperAdmin (« Les écritures automatiques doivent être corrigées depuis leur paiement d'origine. »); Plattformrollen sonst nur lesend. Beleg-Blob wird nach Commit gelöscht. Hartes Löschen, kein Storno-Gegenbuchungssatz.

### 3.6 Plattformrechnung neuer Zyklen
- **R18 Automatische Rechnung beim Abschluss** (`FinanzService.cs:927-983`): Positionen = Teilnahmen des Zyklus mit `StorniertAmUtc == null && AbgerechnetInPeriodeId == null`. Gibt es ≥ 1 Position: neue `Abrechnungsperiode` mit `VonUtc = StartDatum`, `BisUtc = Tagesende(endDatum)` (lokal → UTC, `:143-149`), `Umsatz = Σ PriceAtEnrollment` (voller Preis, keine Abzüge), `Status = Erstellt`, `AutomatischDurchKlassenabschlussErstellt = true`. Jede Position erhält `AbgerechnetInPeriodeId`, und **je Position** eine Ausgabebuchung `SkoulouAppAboAnteil` (Betrag = Positionspreis, Datum = Enddatum, Text « Frais SKOULOUAPP du cycle clôturé. Charge de l'école ; le règlement reste suivi séparément dans la facturation. »). Meldung: « Facturation SKOULOU automatique créée pour {n} participant(s) du cycle, montant {x} FCFA. Le paiement de l'école reste à confirmer. ». Gilt unabhängig davon, ob Direktor oder Koordinator schließt. Keine Mail an die Schule.
- **R19** Keine fiktiven Schülereinnahmen, `IsPaid` bleibt `false`; der tatsächliche Zahlungseingang ist nur der Rechnungsstatus.
- **R20 Statusmaschine** (`Core/Services/AbrechnungService.cs:447-572`), jede Statusänderung in Serializable-Transaktion mit Lock `SKOULOUAPP:Facturation:{id}`:
  - `Erstellt` → `Akzeptiert` (« Accepter & virement effectué ») oder → `Abgelehnt` (« Refuser », Grund Pflicht: « Veuillez indiquer le motif du refus. »). Berechtigt: `DarfZahlungMeldenAsync` (`:514-528`) = SchulLeiter bzw. Koordinator mit Billing-Recht der **Rechnungsschule**.
  - Service erlaubt jeden Wechsel zwischen `Erstellt/Akzeptiert/Abgelehnt`, solange nicht `Bestaetigt`; UI zeigt « Accepter… » bei Status ≠ `Akzeptiert` (also auch nach Ablehnung) und « Refuser » nur bei `Erstellt`.
  - `Akzeptiert` → `Bestaetigt` nur durch aktiven SuperAdmin (« Seule une facturation acceptée par le directeur peut être confirmée. »); setzt `IstAbgeschlossen`, Zeitstempel, `BestaetigtVonSuperAdminId`. Danach unveränderlich (« Cette facturation a déjà été confirmée et ne peut plus être modifiée. »).
  - Keine Fälligkeit, keine Mahnung, keine Teilzahlung, kein Storno/Gutschrift einer Rechnung; abgelehnte Rechnungen behalten ihre Positionen dauerhaft.
- **R21 Verpflichtungsübersicht** « Engagements de l'école par cycle » (`AbrechnungService.GetZyklusVerpflichtungenAsync:25-50`, Recht Billing): je laufendem neuen Zyklus `Teilnehmer = Anzahl nicht stornierter Teilnahmen`, `Betrag = Σ PriceAtEnrollment` → « Engagement provisoire »; `OffeneRechnungen = alle Rechnungen ≠ Bestaetigt` (Anzahl, Summe); `ZahlungGemeldet = Σ Umsatz mit Status Akzeptiert`.

### 3.7 Saldo der Direktion (« Finances de l'école »)
`GetDirektorKontostandUebersichtAsync` (`FinanzService.cs:1577-1712`) aggregiert **alle Buchungen der Schule ohne Zeitfilter** (der übergebene Filter wird ignoriert) und `DirektorKontostandUebersicht` (`FinanzUebersicht.cs:138-160`):

```
IstKontostand        = Σ Einnahmen − Σ Ausgaben   (ohne Kategorie ProductOwnerAnteil)
SchulgebuehrenEingenommen = Σ Einnahmen KursNiveauZahlung      « Scolarité encaissée »
SonstigeEinnahmen    = Σ Einnahmen außer KursNiveauZahlung/AboZahlung   « Autres recettes »
BetrieblicheAusgaben = Σ Ausgaben außer Abo-Anteilen (4,6,7,8) – inkl. Verlust!   « Dépenses de fonctionnement »
SkoulouAppAboAnteil  = Σ Ausgaben Code 4 (Alt-Plattformteil + neue Zykluskosten)  « Part SKOULOU comptabilisée »
LehrerAboAnteil      = Σ Ausgaben Code 7
SchulleiterAboAnteil = max(0, AboEinnahmen − Lehrer − Plattform)
KontostandMitNichtUeberwiesenen = Ist + Lehrer + Plattform     « Solde avec abonnement SKOULOU »
WirklicherKontostand = KontostandMitNichtUeberwiesenen − Plattform = Ist + Lehrer
                                                    « Le solde qui revient à l'école » / « Solde disponible après règlement »
OffeneSchulgebuehrenAktiveKlassen = Σ je aktuellem Klassenmitglied aktiver Zyklen max(0, NiveauPreis − bezahlt)
OffeneAboGebuehren   = Σ PriceAtEnrollment alter unbezahlter Abos mit Provision (für neue Zyklen immer 0)
Saldo-Status: < 0 « Perte », = 0 « Aucun bénéfice », > 0 « Bénéfice »
```
Test-Zahlen siehe 6.1. Das Cockpit (`Web/Services/DirektionsCockpitService.cs:85-95`) zeigt `IstKontostand`, « Scolarité encaissée » (alle Jahre) und « Reste à encaisser (cycles en cours) ».

### 3.8 Rapport (Bildschirm und PDF)
`FinanzBerichtDarstellung.Erstellen` (`FinanzUebersicht.cs:72-120`): Zeilen = Buchungen **ohne** Abo-Codes (3,4,6,7,8). Nur mit « withSubscriptions » werden Abo-Einnahmen (Code 3) und Plattformausgaben (Code 4) aufsummiert hinzugenommen; `Lehreranteil` (Code 7) nur informativ; `Direktionsanteil = Σ Code 6` oder sonst `max(0, AboEinnahmen − Ausgaben 4,7,8)`, nur wenn kein Richtungsfilter. `Saldo = Einnahmen − Ausgaben`. Folge: **neue Zyklus-Plattformkosten (Code 4) fehlen im Rapport, wenn der Haken nicht gesetzt ist**, obwohl das PDF « Charges de l'école, frais SKOULOU compris » titelt. Datum: `Von` = Tagesbeginn, `Bis` = Tagesende; Bericht nur für aktive Schule und mit Dashboard-Recht (`FinanzService.cs:1325-1359`).

### 3.9 Altpfad: Abo je Schüler (LevelEnrollment mit Provisionen)
Nur noch für historische Zyklen (`PlattformPreisProSchueler == null`); für neue Zyklen sind alle Aktionen gesperrt (Test `AlteAboAktionenDuerfenNeueTeilnahmeNichtVeraendern`).
- **R22 Abo zuweisen = sofort bezahlt** (`Core/Services/AboService.cs:64-230`): aktiver Zyklus der Klasse Pflicht; Preis = Schulpreis/global; Provisionen = `BerechneProvision(preis, Schul-Override ?? Settings ?? 4 % / 8 % / 0 %)`; Summe der Provisionen > Preis → Fehler. Setzt `IsUnlocked=IsPaid=true`, Zeitstempel; hebt `CurrentLevelCode` nur bei ordinal höherem Klassencode an. Danach Buchungen R23. Erneutes Zuweisen nur, wenn noch kein Provisions-Snapshot oder unbezahlt/deaktiviert.
- **R23 Buchungen eines Alt-Abos** (`FinanzService.SkoulouAppAboBuchungenErstellenAsync:571-686`), idempotent über `HerkunftReferenzID`: Einnahme voller Preis (Code 3); Ausgabe `Plattform = Preis − SL − Lehrer` (Code 4, PO-Anteil bleibt darin); Ausgabe Lehreranteil (Code 7). Der Direktionsanteil bleibt als Netto in der Schule. Bsp. 20 000 / SL 800 / L 1 600 → +20 000, −17 600, −1 600.
- **R24** « Bezahlt zurücknehmen » und « Abo stornieren » nur SuperAdmin (`/admin`), nur solange nicht abgerechnet und (Rücknahme) Zyklus offen; löscht die Abo-Buchungen hart (`AboService.cs:291-488`).
- **R25 Alt-Rechnungsbetrag je Position** = `max(0, Preis − SL − Lehrer)` (`AbrechnungService.cs:52-55`); Kandidaten nur `IsPaid`, `Preis > 0`, mindestens eine Provision > 0, nicht abgerechnet.
- **R26 Manuelle Rechnung** « Facturer les anciennes inscriptions uniquement » (`AbrechnungService.cs:371-440`, SuperAdmin oder zugeordneter PO): Zeitraum `Von` = `BisUtc` der letzten Nicht-Sammel-Rechnung (auch automatischer!) oder Schulerstellung; `Bis` = gewähltes Datum (nur wenn > Von) sonst jetzt; Positionen nach `PaidAtUtc` in [Von, Bis]. Ohne Positionen: « Aucun paiement d'abonnement SKOULOU ouvert n'est à facturer pour cette période. ». Danach Mail an alle aktiven SchulLeiter (R41).
- **R27 Historischer Zyklusabschluss mit Rechnung** nur in der Direktor-Variante (Direktor-Recht via `DarfSchulleiterFinanzenVerwaltenAsync`); Koordinator-Abschluss erzeugt keine Rechnung. Positionen: bezahlte Alt-Abos der Klasse mit `DurchlaufId = Zyklus` oder ohne Zyklus mit `PaidAtUtc` im Zyklusfenster.
- **R28 Sammelabrechnung** « Créer les facturations » (`AbrechnungService.cs:263-300`, nur SuperAdmin in der UI): je Schule eine Rechnung für offene Alt-Abos aus geschlossenen Zyklen; `Von = min(PaidAtUtc)`, `Bis = jetzt`.
- **R29 Überfällig (nur Alt-Abos)** « Cycles actifs et abonnements payés depuis au moins 10 mois » (`AbrechnungService.cs:192-257`): offene Alt-Abos mit `PaidAtUtc ≤ jetzt − 10 Monate` in einem aktiven Zyklus, der seit ≥ 10 Monaten läuft; `TageOffen = (jetzt − älteste Zahlung).TotalDays` (abgeschnitten). Für neue Zyklen existiert keinerlei Überfälligkeitslogik.

### 3.10 Provisionen (Formeln)
`ProvisionsService.BerechneAnteileAsync` (`ProvisionsService.cs:275-358`):
- **Neue Zyklen**: ganze Position → `Admin` (« Part opérateur ») = `PriceAtEnrollment`; keine Schul-, Lehrer- oder PO-Anteile.
- **Alt-Positionen**: `Schule = Snapshot SL > 0 ? Snapshot : Round(Preis × 4 %, 2)`, `Lehrerpool = Snapshot L > 0 ? Snapshot : Round(Preis × 8 %, 2)`, `PO = Snapshot PO > 0 ? Snapshot : Round(Preis × 0 %, 2)`, `Admin = Preis − Schule − Lehrer − PO`. Gesamtumsatz = Summe aller Anteile = Preis.
- Lehrerpool ist schulweit; individuelle Verteilung « gérée par la direction », keine Wochenstunden-Rechnung, keine Auszahlungsbestätigung (`LehrerErhaltBestaetigenAsync`/`ProduktOwnerErhaltBestaetigenAsync` liefern immer « … n'est plus nécessaire … », `:249-269`).
- `ProductOwnerPercent` wird zwar in `/admin` gepflegt (« Responsable produit », Betrag ↔ Prozent umrechenbar), fließt aber in **keine** neue Position ein → der Responsable sieht für neue Zyklen nie etwas (Perioden mit PO-Anteil 0 werden ausgeblendet, `:231`).
- Sichtbarkeit: Lehrer = `ProvisionLehrer` abgeschlossener Perioden aller verknüpften Schulen; PO = `ProvisionProductOwner` abgeschlossener Perioden zugeordneter Schulen; Direktion = Schule + Lehrerpool; SuperAdmin = alle vier.

### 3.11 Abgleich « Rapprochement des justificatifs et des écritures »
`GetFinanzAbgleichAsync` (`FinanzService.cs:1263-1323`): Scolarité-Abweichungen = Zahlungen ohne passende Buchung (Code 1, Einnahme, gleiche Referenz, gleicher Betrag) + Code-1-Buchungen ohne Zahlung. Plattform: je Rechnung `erwartet = Σ max(0, Preis − SL − L)` der Positionen; Abweichung, wenn `Umsatz ≠ erwartet` oder `≠ Σ Code-4-Buchungen der Positionen` oder eine Position ≠ genau eine Code-4-Buchung hat. Anzeige: « factures à vérifier », « virements confirmés » = Σ Umsatz `Bestaetigt`.

### 3.12 Rundung, Datum, Nebenläufigkeit
- Speicherung: Buchung/Rate/NiveauPreis decimal(18,2); Preise/Snapshots/Umsatz decimal(12,2); Prozente (5,2). Anzeige immer `N0` (ganze FCFA). Provisionsrundung 2 Stellen, MidpointRounding.AwayFromZero; Direktionsformular zeigt Anteile auf 0 Stellen gerundet.
- Datumsfelder sind lokale Kalenderdaten (`DateTime.Today`), Abrechnungsgrenzen werden über Serverzeitzone nach UTC umgerechnet (Togo = UTC+0, Azure = UTC → unauffällig).
- Standard-Filter Journal: ab 1. September des laufenden Schuljahres (`Finanzen.razor.cs:105-106`).
- Zyklusstart/-abschluss, Teilnehmerergänzung, Storno, Schülersperre: Serializable-Transaktion + `sp_getapplock 'SKOULOUAPP:Zyklusverwaltung'` exklusiv, 30 s Timeout (`Core/Services/ZyklusTransaktionsSchutz.cs:13-21`), Retry mit geleertem ChangeTracker. Ratenbuchung und manuelle Rechnung laufen **ohne** Lock; Doppelklickschutz nur über UI-`DatabaseActionGuard`.

---

## 4. Abläufe

### 4.1 Geldfluss im Überblick
```
Eltern/Schüler ──(bar, offline)──► Schule: RatenZahlung → Einnahme « Scolarité » + Reçu + Mail
Schule ── manuelle Einnahmen/Ausgaben (+ Beleg) ──► eigenes Journal
Zyklusende: Schule schuldet Plattform  n_Teilnehmer × Preis-Snapshot
           → Rechnung (Erstellt) + Ausgabe « Frais de plateforme par cycle » im Schuljournal
Schule ──(Überweisung, offline)──► Plattform: « Accepter & virement effectué » (Akzeptiert)
SuperAdmin prüft Bank ──► « Marquer comme payé (compte reçu) » (Bestaetigt)
Plattform ──► Lehrer/Direktion/Responsable: neu nichts; alt nur Anzeige von Anteilen, keine Auszahlung
```

### 4.2 Schuljahr einer Klasse (Normalfall, neuer Zyklus)
1. SuperAdmin pflegt globalen Preis und ggf. Schulpreis (`/admin`).
2. Direktion (oder Koordinator mit « Paiements des cycles ») öffnet in « Espace directeur › Classes de mon école » (`/schulleiter`) « Démarrer le cycle »: Scolarité-Vorschlag = `Klasse.Price` (wenn > 0, sonst Schul-/globaler Abo-Preis, `FinanzService.cs:1414-1439`), Startdatum. → Zyklus, Plattformpreis eingefroren, alle aktuellen Klassenmitglieder als Teilnehmer erfasst.
3. Schüler werden später aufgenommen/zugewiesen/entsperrt → automatisch nacherfasst (R9).
4. Eltern zahlen bar; Personal erfasst « Enregistrer un paiement » (Typ « Échelonné » oder « Cash (total) »). Reçu drucken, Mail an Schüler.
5. Laufend sichtbar: Dû/Payé/Ouvert je Schüler; in « Facturation SKOULOU » « Engagement provisoire » (Teilnehmer × Preis).
6. SuperAdmin kann Fehlzuordnungen in `/cycle-participants` mit Grund stornieren (nur vor Abschluss).
7. « Clôturer le cycle »: Dialog zeigt « Frais de scolarité impayés », « Notes finales » (fehlend erlaubt), Teilnehmerliste mit Preisen; zweite Bestätigung « Clôturer définitivement ». → Rechnung + Plattformaufwand + ggf. « Verlust Schulgebühr ».
8. Direktion überweist und klickt « Accepter & virement effectué » (oder « Refuser » mit Grund).
9. SuperAdmin sieht « {n} paiement(s) à confirmer », prüft Bank, « Marquer comme payé (compte reçu) » → « Confirmée et terminée ».
10. Neuer Zyklus derselben Klasse: neue Teilnahmen (neue `LevelEnrollment`-Zeilen), alte bleiben mit Rechnungsbezug erhalten. Kein Zwang, die alte Rechnung zuvor zu begleichen.

### 4.3 Manuelle Schulbuchung
« Nouvelle écriture »: Kategorie wählen oder anlegen (Richtung « Recette »/« Dépense »), « Montant (FCFA) », Datum, Referenz, Datei, Notiz → Buchung + privater Beleg. Löschen « Annuler » nur eigene manuelle Buchungen.

### 4.4 Altpfad (nur Bestandsdaten)
Abo zuweisen (= bezahlt) → Abo-Buchungen → manuelle Rechnung durch SuperAdmin/PO (Mail an Direktion) oder Rechnung beim Direktor-Abschluss oder Sammelabrechnung → gleiche Statusmaschine → Provisionsanzeige. Startsperre R6 bis zur Zahlungsmeldung.

---

## 5. Seiten, Routen, Dokumente, Mails

| Route | Rollen (Attribut) | Inhalt |
|---|---|---|
| `/finanzen` « Recettes & dépenses de l'école » | SchulLeiter, Koordinator, SuperAdmin, ProduktOwner | Schulwahl (nur berechtigte aktive Schulen, `FinanzService.GetSchulenAsync:1364-1388`); Reiter « Finances de l'école », « Nouvelle écriture », « Paiements des cycles », « Facturation SKOULOU » (eingebettete `/abrechnung`) je nach Recht |
| `/finanzen/rapport` | wie oben | Rapport mit Query `schoolId, from, to, direction, sort, withSubscriptions`; ungültige Filter → « Les filtres du rapport ne sont pas valides. » |
| `GET /finanzen/rapport/pdf-file` | SuperAdmin, PO, SchulLeiter, Koordinator | QuestPDF A4 « Rapport financier · {Schule} », Datei `rapport-financier-{id}-{yyyyMMdd}.pdf` (`Web/Program.cs:432-458`) |
| `GET /finanzen/recu/{zahlungId}` | jeder Angemeldete, Prüfung im Service | « REÇU DE PAIEMENT » A5 quer (`Web/Services/QuittungService.cs`) |
| `GET /finanzen/justificatif/{buchungId}` | SuperAdmin, PO, SchulLeiter, Koordinator | Beleg inline, Content-Type aus DB (`Program.cs:481-490`) |
| `/abrechnung` « Facturation SKOULOU » | SuperAdmin, SchulLeiter, Koordinator, ProduktOwner | Rechnungen je Schule |
| `/provisionen` « Commissions » / « Mes parts d'abonnement » | SuperAdmin, SchulLeiter, Koordinator, Lehrer, ProduktOwner | Anteile (Menüpunkt nur für PO « Parts de mes écoles ») |
| `/superadmin/finances` « Centre financier » | SuperAdmin | Kacheln + Reiter « Finances des écoles » (= eingebettete `/finanzen`, lesend) |
| `/cycle-participants` « Participants facturables des cycles » | SuperAdmin | Zyklus-Suche (Schule/Klasse, « Cycles en cours uniquement », 50 je Seite, « Afficher plus »), Teilnehmer mit Preis, « Signaler une affectation erronée » + « Motif obligatoire » + « Confirmer l'annulation financière » |
| `/admin` | SuperAdmin, PO (lesend) | « Prix de l'abonnement (FCFA) » je Schule; Abschnitt « Prix général et rémunération interne » (globaler Preis, Responsable produit FCFA/%, « Plateforme » berechnet); Alt-Aktionen Bezahlt zurücknehmen / Abo stornieren |
| `/schulleiter` | Direktion | Zyklusstart/-abschluss, Zahlung, Klassenfinanzbericht je Zyklus |
| Elternbereich | Eltern | je laufendem Zyklus des Kindes Schulgeld, « Déjà payé », Rest, Zahlungsliste mit « Reçu » (`Web/Services/ElternService.cs:206-218`) |

**Menü** (`Web/Components/Layout/NavMenu.razor:76-117`): SuperAdmin « Centre financier » + « Facturation SKOULOU »; PO « Parts de mes écoles », « Facturations de mes écoles », « Finances des écoles »; SchulLeiter/Koordinator (mit irgendeinem Finanzrecht) « Comptabilité et paiements ». Lehrer haben keinen Menüpunkt für `/provisionen`.

**`/finanzen` – Reiter « Finances de l'école »**: Kopfkarte « Le solde qui revient à l'école » (+ Badge Bénéfice/Perte), « Solde avec abonnement SKOULOU », « Part SKOULOU comptabilisée », Aufschlüsselung (« Scolarité encaissée », « Autres recettes », « Abonnements SKOULOU encaissés », « Dépenses de fonctionnement »), « Paiements en attente », offene Rechnungen mit Link « Voir et déclarer le virement », Abgleichskarte; Filter « Filtrer les écritures de l'école » (Von/Bis, « Type » Tous/Recettes/Dépenses, « Tri » Date récent/ancien, Montant haut/bas), « Voir le rapport », « ↓ Exporter PDF »; Journaltabelle (Date, Catégorie, Justificatif, Type, Montant, Saisi par) **ohne** Abo-/Plattformbuchungen, Links « Voir le justificatif », « Reçu de paiement », Aktionen « Annuler » bzw. « Corriger depuis le paiement ». Eine Namensmaskierung für PDFs existiert, wird aber nirgends benutzt (`Finanzen.razor.cs:225-254`, tot); das Rapport-PDF enthält keine Erfassernamen.
**Reiter « Paiements des cycles »**: Hinweis, dass Start/Abschluss in « Espace directeur » liegen; Filter « Statut » (En cours/Clôturés), « Classe », « Rechercher un élève »; je Zyklus Code, Badge Actif/Clôturé, « Prix », Daten, Summen « Dû/Payé/Ouvert », Schülertabelle; Buttons « Enregistrer un paiement », « Clôturer le cycle »; Zahlungsdialog « Paiement — {Code}» mit « Élève » (Suche, Anzeige « Ouvert : x FCFA »), « Type », « Montant » bzw. « Montant restant à payer » (bei Cash gesperrt, automatisch), « Date d'effet ».

**`/abrechnung`**: SuperAdmin-Blöcke « Attention requise » (10-Monats-Liste), « Abonnements SKOULOU issus de classes clôturées » + « Créer les facturations », « Contrôle bancaire requis · {n} paiement(s) à confirmer » (mit Ersteller/Melder), Info zu automatischen Rechnungen; Direktion: « Action requise · Direction – {n} facturation(s) à valider »; Bereich « Période ouverte » mit « Engagements de l'école par cycle », für SA/PO Filter « Afficher jusqu'au (date et heure) » + « Filtrer »/« Jusqu'à maintenant », Kacheln « Abonnements SKOULOU » / « Chiffre d'affaires ouvert », Button « Facturer les anciennes inscriptions uniquement »; Liste « Facturations de l'école » mit « En cours (n) » / « Terminées (n) », Datumsfilter, je Rechnung Zeitraum, Anzahl, Betrag, Status-Badge, Quelle « Auto: clôture de classe » / « Manuel: Administrateur », Aktionen « Renvoyer l'e-mail » (nur manuelle, Status Erstellt), « Marquer comme payé (compte reçu) » (Dialog « Confirmer la facturation »), « Accepter & virement effectué », « Refuser » (Dialog « Refuser la facturation », « Motif du refus… »).

**`/provisionen`**: Direktion/Koordination/SuperAdmin: Reiter « Commissions ouvertes » / « Historique », Karten « Part école », « Fonds enseignants », für SuperAdmin zusätzlich « Part du responsable produit », « Part opérateur »; « Total affiché » = SuperAdmin Gesamtumsatz, sonst Schule + Lehrerpool (`Provisionen.razor.cs:118-121`). Lehrer: je Periode « Fonds enseignants ». PO: Summe « Mes parts d'abonnement ».

**Reçu** (`QuittungService.cs:27-123`): Nummer `REC-{Jahr des Zahlungsdatums}-{erste 8 Hex der Zahlungs-GUID}`; Schule (offizielle Bezeichnung, Adresse, Telefon · Mail), « Reçu de : » Schüler + « Matricule », « Classe », « Objet : frais de scolarité — Paiement intégral | Versement (paiement échelonné) », « Montant reçu », Betrag in Worten « Arrêté le présent reçu à la somme de … francs CFA. » (bis 999 999 999, französische Zahlwörter), « Frais de scolarité » (= NiveauPreis), « Total versé » (Summe bis einschließlich dieser Zahlung, sortiert nach Wirkungsdatum, dann Erfassungszeit), « Reste à payer », « Observation » (HTML entfernt), « Enregistré par », « Fait à {Ort}, le … », « Signature et cachet ». Zugriff: Schüler selbst, verknüpfte Eltern, sonst `Menu && (Cycles || Dashboard)`.

**PDF Rapport** (`Web/Services/FinanzBerichtPdfService.cs`): Kopf, Filterbeschreibung « Du … au … · Recettes et dépenses · Date décroissante », « Recettes », « Charges de l'école, frais SKOULOU compris », « Solde comptable de l'école », optional Abo-Block (« Anciennes commissions … (historique uniquement) »), Tabelle Date/Catégorie/N° écriture / justificatif/Type/Montant, Seitenzahlen.

**Mails** (alle über `IEmailService`/Postmark, Fehler werden nur geloggt):
- **R40** « Paiement scolaire enregistré – SKOULOU » an die Mail des **Schülers** nach jeder Rate (`Web/Services/KontoBenachrichtigungService.cs:49-60`); Alt: « Abonnement SKOULOU enregistré comme payé » (`:69-84`).
- **R41** « Facturation SKOULOU à régler » an alle aktiven SchulLeiter der Schule mit gültiger Adresse (dedupliziert), **nur** für manuelle Rechnungen im Status `Erstellt` (nicht automatisch, nicht Sammel): Schule, Zeitraum, Anzahl, « Montant à virer » (`Web/Services/AbrechnungBenachrichtigungService.cs:37-105`). Erneut senden: SuperAdmin oder zugeordneter PO (`:17-35`).
- Keine Mails bei automatischer Rechnung, Ablehnung, Zahlungsmeldung, Bestätigung; keine Erinnerungen.

**Belegspeicher** (`Web/Services/AzureFinanzBelegStorage.cs`): Azure-Blob privater Container; wenn Blob deaktiviert, lokal unter `App_Data/media-library/finance-private/{schule}/{beleg}` (muss außerhalb `wwwroot` liegen, Pfad strikt validiert); aktiviert aber unkonfiguriert → Fehler, kein lokaler Fallback.

---

## 6. Durch Tests festgelegte Regeln

### 6.1 Schulbuchhaltung / Saldo (`Tests/FinanzServiceTests.cs`)
- Alt-Abo 20 000 mit SL 800 / L 1 600 / PO 400 → genau 3 Buchungen: +20 000, −17 600 (Plattform), −1 600 (Lehrer); **keine** PO-Buchung; alle mit Referenz = Enrollment, eindeutige `B-…`-Nummern (`:715-750`).
- Dazu manuell +5 000 (« Don »), −2 000 (« Matériel ») → Journal Einnahmen 25 000, Ausgaben 21 200, Saldo 3 800; Kontostand: AboEingenommen 20 000, SL-Anteil 800, Lehrer 1 600, Plattform 17 600, Sonstige 5 000, Betrieb 2 000, Ist 3 800, « mit nicht überwiesenen » 23 000, « wirklich » 5 400 (`:781-842`).
- Bezahlte Schüler werden je Schule nur einmal gezählt (`:752-779`); `BestimmeSaldoStatus` nur nach Vorzeichen.
- Manuelle Buchung: Betrag ≤ 0 abgelehnt; Systemkategorie abgelehnt; ungültige Datei abgelehnt; ohne Referenz/Datei erlaubt; Löschen nur durch Ersteller; Abo-Systembuchung nur durch SuperAdmin löschbar; Belegzugriff schulisoliert; Kategorie leer/Duplikat abgelehnt; verwendete Kategorie nicht löschbar.
- Rechte: Plattformrollen ohne Schreibrechte; Koordinator sieht nur freigegebene Reiter, braucht eigenes Billing-Recht und Cycle-Recht für Raten; Entziehen aller Reiter entzieht alle Finanzrechte; Bericht bei manipulierter URL (fremde Schule, `from > to`) → null.
- Zyklus: zweiter aktiver Zyklus verboten; Systemniveau verboten; Abschluss ohne Noten erlaubt, Schüler bleibt in Klasse, danach keine Noten-/Entscheidungsänderung; 1 Schüler × 12 500 ohne Zahlung → offene Schuld 12 500; veröffentlichte Lektionen/Prüfungen bleiben bei neuen Zyklen veröffentlicht.
- Raten: 5 000 auf 12 500 → bezahlt 5 000, offen 7 500, eine Einnahmebuchung; Cash 5 000 bei Rest 12 500 → abgelehnt; Cash 12 500 → ok; Rate 5 000 + Cash 7 500 → ok; nach Abschluss keine Zahlung; fremder Schüler abgelehnt.
- Gesamtszenario (`:964-1047`): Zyklus 12 500, Rate 8 000 → Restschuld 4 500; Rechnung = eingefrorener Preis; fremdes Alt-Abo bleibt unberechnet; Abgleich ohne Abweichung; keine Startsperre; Neustart erzeugt neue Teilnahme (unbezahlt, freigeschaltet), alte behält Rechnungsbezug; `AboZuweisenAsync` scheitert im neuen Zyklus; genau eine Plattformbuchung.
- Wiederholung (`:1049-1100`): zwei Zyklen → zwei getrennte Rechnungen mit je 1 Position.
- Abgleich: fehlende Zahlungsbuchung → 1 Abweichung; Rechnung 17 600 offen → bestätigt 0, danach 17 600; manipulierte Rechnung → 1 Abweichung.

### 6.2 Plattformabrechnung (`Tests/AbrechnungServiceTests.cs`, `ZyklusVerpflichtungsTests.cs`)
- Alt-Rechnungsbetrag Standard: 12 500 − 500 − 1 000 = **11 000**; unbezahlte Positionen zählen nicht; zweite Erstellung zählt nichts doppelt; ohne Positionen Fehler mit « abonnement SKOULOU »; frei gewähltes Enddatum in der Zukunft wird übernommen; Alt-Kurszahlungen ohne Provisionen werden ignoriert.
- Geschlossene Zyklen: nur offene, im Zyklusfenster bezahlte Abos → 1 Position, 11 000; Sammelabrechnung genau einmal, zweite schlägt fehl.
- 10-Monats-Regel: Zahlung genau vor 10 Monaten in aktivem Zyklus ≥ 10 Monate → 1 Eintrag, `TageOffen` = Tagesdifferenz; jüngerer Zyklus oder Zahlung aus anderem Zyklus → keine Warnung.
- Workflow: akzeptieren → bestätigen → `Bestaetigt`, `IstAbgeschlossen`, Bestätiger gesetzt; fremder Benutzer/ohne Akzeptanz kann nicht bestätigen; Ablehnung ohne Grund scheitert (« motif »); fremde Schule « autorisé à modifier »; Koordinator braucht Billing-Recht der Rechnungsschule, darf ablehnen und danach akzeptieren; PO darf nur zugeordnete Schule abrechnen und keine Zahlung melden; Bestätigungsliste zeigt Ersteller (« Product Owner ») und Melder (« Directeur »).
- Verpflichtungen: Teilnahmen 3 000 (2 aktiv inkl. `IstAktuell=false`, 1 storniert) → 2 Teilnehmer, 6 000 vorläufig; Rechnungen 6 000 Erstellt + 3 000 Akzeptiert + 2 000 Bestaetigt → offen 9 000 (2 Rechnungen), gemeldet 3 000; fremde Schule ignoriert; Unbekannte/gesperrte Admins → `UnauthorizedAccessException`.

### 6.3 Zyklusteilnahme (`Tests/ZyklusTeilnahmeServiceTests.cs`, `ZyklusteilnahmeSqlTests.cs`)
- Schulpreis 3 000 vor globalem 5 000; nach Entfernen des Schulpreises 5 000. Teilnehmer einmalig; späterer Teilnehmer nach Preisänderung auf 8 000 erhält trotzdem 3 000; alle unbezahlt, freigeschaltet, Provisionen 0, keine Buchungen; Ergänzen nach Abschluss → Fehler.
- Angenommene, gesperrte Selbstregistrierung zählt; offene nicht; fremde Schule nicht; Fremdnutzer darf Liste nicht sehen.
- 4 Schüler (Départ archiviert, Bloqué, Attente nicht angenommen, Arrivée kommt und geht) → Rechnung 3 Positionen, **9 000**; zweiter Abschluss scheitert, keine zweite Rechnung; alle `IsPaid=false`.
- Storno: Schüler darf nicht, leerer Grund nicht; nach Storno keine Neuanlage; Abschluss erzeugt **keine** Rechnung (0 Positionen); zweites Storno scheitert.
- Alte Abo-Aktionen (zuweisen, bezahlt, stornieren, Klasse+Abo, Abo-Buchungen, Rücknahme selbst bei manipuliertem `IsPaid`) scheitern bei neuen Zyklen.
- SQL-Parallelität (nur mit `SKOULOU_SQL_TESTS=1`): parallele Starts → genau einer erfolgreich; vorläufig 6 000/2 Teilnehmer; parallele Abschlüsse → genau einer; Rechnung 6 000/2; danach vorläufig 0, offen 6 000; 3 Buchungen (2 Plattform + 1 Verlust).

### 6.4 Provisionen / Abo (`Tests/ProvisionsServiceTests.cs`, `AboServiceTests.cs`)
- Neue Zyklusrechnung 3 000 → Lehrer 0, Admin 3 000, keine anderen Empfänger.
- Schulpreis 20 000 mit 4/8/2 % → Snapshots 800 / 1 600 / 400; PO sieht genau seinen Snapshot; Lehrerpool = 1 000 (Standard), keine Auszahlungszeilen, Empfangsbestätigung « n'est plus nécessaire »; Lehrer sieht nur eigene Schulen und nur abgeschlossene Perioden.
- Prozentsumme > 100 (60 + 50) abgelehnt; ungültige Preise 0/−1/3000,001 abgelehnt; nur aktiver SuperAdmin darf Preise speichern.
- Gesamtablauf (`Tests/GesamtAblaufTests.cs`): Schulpreis 8 000 → SL 320 (4 %), Lehrer 640 (8 %); Raten 8 000 auf 12 500 und Cash 13 000 auf 13 000 → offen 4 500 bzw. 0, Verlustbuchung genau einmal; Preisvorschlag beim Neustart 12 500 / 13 000.

### 6.5 Reçu, Bericht, Mail, Speicher
- Betrag in Worten: 25 000 « vingt-cinq mille », 1 500 « mille cinq cents », 80 000 « quatre-vingt mille », 200 000 « deux cent mille », 1 250 000 « un million deux cent cinquante mille », 71 « soixante et onze », 1 001 « mille un » (`QuittungServiceTests.cs:14-24`).
- Reçu Scolarité 75 000, Zahlungen 25 000 und 30 000 → erstes Reçu Total 25 000 / Rest 50 000, zweites 55 000 / 20 000, Matricule angezeigt, Nummer `REC-2026-…`, Fremder → null, echtes PDF.
- Rapport: ohne Abos nur Scolarité 500; mit Abos (2 × 1 000 Einnahmen, 1 700 Plattform, 200 Lehrer) → AboEinnahmen 2 000, AboAusgaben 1 700, Lehrer 200, Direktion 100, Einnahmen 2 500, Saldo 800; Richtungsfilter → kein Direktionsanteil. Szenario 50 000 + 20 000 Scolarité, 3 × 12 500 Abo, 3 × 11 000 Plattform, 3 × 1 000 Lehrer → ohne Abos 70 000, mit Abos Einnahmen 107 500, Ausgaben 33 000, Saldo 74 500, Direktion 1 500 (`FinanzBerichtPdfServiceTests.cs`).
- Rechnungsmail nur an aktive Direktoren der Rechnungsschule, enthält Schule, Betrag, Anzahl, « virement »; automatische Rechnung → kein Versand (0,0); fehlende Empfänger → (0,1); erneut senden nur SuperAdmin/zugeordneter PO und nur manuelle `Erstellt`-Rechnungen.
- Eltern: Schulgeld 60 000, bezahlt 20 000 → Rest 40 000 (`ElternServiceTests.cs:75-77`).
- Belege lokal privat außerhalb `wwwroot`; Produktion ohne Blob-Konfiguration → Fehler, kein lokaler Ordner.

---

## 7. Schwächen & Vorschläge für den Neubau

### 7.1 Fachliche / Geld-Fehler
1. **Verlust wird doppelt wirksam**: offene Scolarité wurde nie eingenommen, wird aber als Ausgabe « Verlust Schulgebühr » gebucht und zählt in « Dépenses de fonctionnement » → Kassensaldo sinkt um Geld, das nie da war (`FinanzService.cs:1022-1041`, `1624-1630`). → Neubau: Forderungen (Soll) und Kasse (Ist) trennen; Abschreibung nur in einer Forderungs-/Ergebnissicht, nie im Kassenjournal.
2. **Offene Schuld klassennetto**: `Σ Soll − Σ aller Zahlungen` – Überzahlung eines Schülers oder Zahlungen ausgetretener Schüler verringern den Verlust anderer (`:1155-1168`), während das Dashboard je Schüler rechnet. → Immer je Schüler `max(0, Soll − Ist)`, Überzahlung als Guthaben führen.
3. **Rate ohne Obergrenze, kein Datumscheck**, keine Stornierung/Korrektur von Zahlungen, kein Reçu-Storno. → Rate ≤ Rest (oder explizites Guthaben), Wirkungsdatum im Zyklus, Stornobuchung mit Grund statt Löschen.
4. **Zwei Teilnehmerdefinitionen**: Plattform (R8, mit Annahme-/Archivfilter, historisiert) vs. Scolarité (aktuelle Klassenmitglieder ohne Filter; nach Abschluss Entscheidungs-Kohorte oder **alle jemals** eingeschriebenen der Klasse über alle Zyklen, `:1780-1786`). Schüler ohne Abschlussentscheidung verschwinden nach Abschluss aus der Gebührensicht. → Eine Teilnahme-Entität je Zyklus als Basis für Soll, Plattform und Bericht.
5. **Klassenwechsel im Zyklus = doppelte Plattformkosten** (R10), kein Pro-rata.
6. **Rechnungen ohne Lebenszyklus**: keine Fälligkeit, keine Mahnung, kein Überfällig-Status für neue Zyklen, keine Startsperre mehr, keine Teilzahlung, kein Zahlungsnachweis (Referenz/Beleg) bei « virement effectué », kein Storno/Gutschrift; abgelehnte Rechnung ist Sackgasse (Positionen bleiben gebunden). → Statusmodell `Offen → Zahlung gemeldet (mit Referenz, Betrag, Beleg) → Bezahlt / Teilbezahlt`, `Strittig`, `Storniert` mit Gutschrift; Fälligkeit (z. B. Abschluss + 30 Tage) und Erinnerungsjob.
7. **Plattformkosten im Schuljournal nach Periodenabgrenzung (Abschluss), Zahlung nur als Status** → Saldo-Begriffe verwirrend (« Solde avec abonnement SKOULOU » vs. « Le solde qui revient à l'école »; Formel `Ist + Lehrer + Plattform − Plattform`). → Klare Kennzahlen: Kasse, offene Forderungen Eltern, offene Verbindlichkeit Plattform.
8. **Rapport ohne Haken unterschlägt neue Plattformkosten** (Code 4 als „Abo-Buchung“ gefiltert, Titel behauptet das Gegenteil). Dashboard-Saldo ignoriert jeden Zeitfilter.
9. **Kategorie-Code 4 doppelt belegt** (Alt-Plattformanteil und Zykluskosten) mit Bezeichnungs-Pingpong: `GetOderErstelleSystemKategorieAsync` benennt global um, `GetKategorienAsync` benennt beim Lesen zurück (`:202-230`, `971-972`, `1801-1806`). Lese-Methode schreibt in die DB.
10. **ProductOwner-Vergütung konfigurierbar, aber nie berechnet** (Snapshot immer 0) → « Responsable » sieht für neue Zyklen nichts; Admin-UI suggeriert Wirkung. Lehrer sehen « 0 FCFA »-Perioden.
11. **Direktionsformular zeigt noch « Part direction » / « Part enseignants » (4 %/8 %)** (`Web/Components/Pages/SchulLeiter.razor:1324-1326`) – widerspricht der Abschaffung.
12. Belegnummer als „Rechnungsnummer manueller Buchungen“ ist nicht Pflicht; Reçu-Nummer nicht fortlaufend (GUID-Präfix) – für OHADA-konforme Belege ungeeignet. → Fortlaufende Nummernkreise je Schule und Jahr (Reçu, Rechnung).
13. Manuelle Alt-Rechnung: Periodenbeginn = `BisUtc` der letzten **beliebigen** Nicht-Sammel-Rechnung (auch automatisch, ggf. in der Zukunft) → Alt-Abos können aus jeder Periode fallen (`AbrechnungService.cs:62-70`).
14. Cash-Betrag im Direktionsdialog wird auf den vollen `NiveauPreis` statt auf den Rest gesetzt → Fehler bei Teilzahlern (`SchulLeiter.razor.cs:2347-2353`).
15. Gemischte Preisbedeutung: `Klasse.Price` („vom SuperAdmin festgelegt“) dient als Scolarité-Vorschlag, Default 12 500 vs. 10 000; Fallback auf Abo-Preis vermischt Schulgebühr und Plattformpreis.

### 7.2 Sicherheit / Rechte
16. Services standardmäßig ungeschützt (`autorisierungErzwingen=false`, `Guid.Empty` = erlaubt); `AboService`, `ProvisionsService`, `SammelabrechnungenAusGeschlossenenKlassenErstellenAsync`, `AbrechnungErstellenAsync(erstellerId:null)` ohne Rechteprüfung. → Neubau: Autorisierung immer im Anwendungsdienst über den angemeldeten Benutzer, nie über Parameter.
17. `/provisionen`: SchulLeiter/Koordinator sehen Schulanteile jeder verknüpften Schule ohne Finanzrecht; Lehrer sehen Pool jeder verknüpften Schule.
18. Schulweite Sperre = „ein gesperrter SchulLeiter“ (`SchulZugangService.cs:12-17`) – implizit und fragil.
19. FK-Cascade von Schüler/Klasse auf Abrechnungspositionen und von Zyklus auf Raten → Löschen vernichtet Finanzhistorie. → Finanzdaten nie kaskadierend löschen, nur archivieren.
20. Ratenbuchung ohne Transaktion/Idempotenz; „ein aktiver Zyklus“ nur per Applock statt Unique-Index.

### 7.3 Toter Code / Altlasten
21. `LehrerProvisionsAuszahlung`, `ProduktOwnerProvisionsAuszahlung`, Erhalt-Bestätigungen, `SetCoordinatorFinanceAccessAsync`, `BestehendeFinanzBelegeMigrierenAsync`, `FinanzBeleg.Inhalt`, SystemCodes 2/5/6/8/100–103/105, `AboTyp`, SKOULOU+-Reste, `Klasse.AutoAssignSkoulouAppAboOnCycleStart` (Start erfasst immer alle), `allowBlockingPaidStudent`, `IsUnlocked`/`IsPaid`/`PaidAtUtc` für neue Zyklen, Alt-Abopfad komplett, `School.*CommissionPercentOverride`, `CommissionSettings.SchoolLeader/TeacherPercent` (werden als 0 gelesen, aber DB-Default 4/8). → Im Neubau ersatzlos streichen (keine Bestandsdaten).
22. Startdialog in `/finanzen` liegt im `foreach` der Zyklen (`Finanzen.razor:507/525`) und wird von dort nie geöffnet – Start/Abschluss doppelt implementiert (`/finanzen` und `/schulleiter`).

### 7.4 Performance
23. Journal lädt alle Buchungen des Filters ohne Paging; `LadeAllesAsync` lädt Kontostand und Abgleich doppelt; Abgleich lädt alle Zahlungen/Buchungen der Schule aller Jahre mit quadratischer Prüfung (`FinanzService.cs:1285-1287`); Provisionen N+1 je Periode; SuperAdmin-`/abrechnung` lädt globale Joins über alle Schulen beim Öffnen; `studentIds.Contains(...)`-Listen; Systemkategorie je Position in Schleife nachgeladen; Beleg-Upload liest bis 25 MB in den Blazor-Circuit-Speicher.

### 7.5 Empfehlungen für das neue Modell (kompakt)
- Entitäten: `Zyklus` (Klasse, Zeitraum, Scolarité-Betrag, Plattformpreis-Snapshot), `ZyklusTeilnahme` (Eintritt/Austritt, Storno mit Grund), `Gebuehrenforderung` je Teilnahme (+ optional Ratenplan mit Fälligkeiten, Rabatt/Bourse), `Zahlung` (Betrag, Art, Kanal bar/Mobile Money, Referenz, Storno), `Quittung` (fortlaufend), `Buchung` (Kassenjournal, nur echte Geldbewegungen), `Plattformrechnung` + `Rechnungsposition` + `Zahlungsmeldung` + `Gutschrift`.
- Beträge als ganze FCFA (`long`/decimal(18,0)); Validierung „positiv, ganzzahlig“.
- Rechnungsnummern, Fälligkeit, Erinnerungen als Hintergrundjob; Mail auch bei automatischer Rechnung und bei Ablehnung/Bestätigung.
- Mobile-Money-Anbindung (T-Money/Flooz bzw. Aggregator) als optionale Zahlungsquelle mit idempotentem Webhook vorsehen.

---

## 8. Offene Fragen
1. Soll ein Klassenwechsel innerhalb eines Zyklus doppelt (je Klasse) berechnet werden, oder gilt „ein Schüler = eine Plattformgebühr je Zeitraum/Schuljahr“?
2. Pro-rata für spät eingetretene oder früh ausgetretene Schüler (Plattform und Scolarité)?
3. Ist ein Zyklus = ein Schuljahr? Wie hängt er mit `Schuljahr` (Bulletins) zusammen?
4. Fälligkeit und Konsequenz unbezahlter Plattformrechnungen (Mahnstufen, Startsperre für neue Zyklen, Schulsperre)?
5. Bleibt die ProductOwner-/« Responsable »-Vergütung? Wenn ja: Basis (Rechnungsbetrag oder bestätigter Zahlungseingang), Satz je Schule, Auszahlung/Bestätigung?
6. Sind Direktions-/Lehrerprovisionen endgültig abgeschafft (dann Seite `/provisionen` für Schule/Lehrer streichen)?
7. Scolarité: ein Betrag je Klasse genügt, oder braucht es Gebührenarten (Inscription, APE, Tenue, Cantine), Ratenpläne mit Fälligkeiten, Ermäßigungen/Bourses, Geschwisterrabatt?
8. Darf eine erfasste Zahlung storniert/korrigiert werden, von wem, mit welchem Beleg (Storno-Reçu)?
9. Soll unbezahlte Scolarité beim Abschluss abgeschrieben werden oder als Forderung in den nächsten Zyklus übergehen?
10. Gesetzliche Anforderungen an Reçu/Rechnung in Togo (fortlaufende Nummer, NIF, TVA, Pflichtangaben, Aufbewahrung)?
11. Gewünschte Zahlungskanäle: nur bar/Überweisung oder Mobile Money (Eltern → Schule und/oder Schule → Plattform) mit automatischem Abgleich?
12. Wer bekommt Zahlungsbestätigungen: Schüler-Mail, Eltern-Mail, SMS/WhatsApp?
13. Sollen Koordinatoren weiterhin feingranulare Finanzrechte (5 Flags) bekommen, oder reichen Rollen „Comptable“/„Caissier“?
14. Soll die Plattform Rechnungen zusätzlich als PDF erzeugen (heute gibt es keine Plattformrechnungs-PDF, nur Mail-Text)?
