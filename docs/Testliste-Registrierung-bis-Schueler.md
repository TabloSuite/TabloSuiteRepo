# Testliste: Von Schulleiter-Registrierung bis Schüler

Diese Liste führt den kompletten End-to-End-Ablauf durch – vom Anlegen einer Schule
durch den Schulleiter bis zur aktiven Nutzung durch einen Schüler. Jeder Punkt kann
manuell in der laufenden App abgehakt werden.

Legende: ⬜ offen · ✅ bestanden · ❌ fehlgeschlagen

---

## 1. Schulleiter-Registrierung

- ⬜ **1.1** Startseite (`/`) öffnet sich, Bereiche (Über die App, Registrierung, Abos, Kurse) sichtbar.
- ⬜ **1.2** Über „Anmelden/Registrieren“ (oben rechts) gelangt man zur Registrierung (`/register`).
- ⬜ **1.3** Registrierungsart **Schulleiter** ist auswählbar.
- ⬜ **1.4** Pflichtfelder für neue Schule (Name, Stadt, Land) werden angezeigt.
- ⬜ **1.5** Registrierung ohne Pflichtfelder wird abgelehnt (Validierungsmeldung).
- ⬜ **1.6** Registrierung mit gültigen Daten legt Schule + Schulleiter-Konto an.
- ⬜ **1.7** Nach Registrierung erfolgt automatischer Login / Weiterleitung.
- ⬜ **1.8** Doppelte Registrierung mit gleicher E-Mail wird abgelehnt.

## 2. Schulleiter-Login & Dashboard

- ⬜ **2.1** Login mit den Schulleiter-Zugangsdaten funktioniert.
- ⬜ **2.2** Weiterleitung auf `/schulleiter` (Espace directeur).
- ⬜ **2.3** Hero-Bereich und Schulkarte(n) werden korrekt angezeigt.
- ⬜ **2.4** Kennzahlen (Anzahl Schüler, „en attente“) stimmen mit Datenlage überein.
- ⬜ **2.5** Zugriff auf `/admin` oder `/abrechnung` ist als Schulleiter NICHT erlaubt.

## 3. Schüler-Selbstregistrierung

- ⬜ **3.1** Registrierungsart **Schüler** auf `/register` auswählbar.
- ⬜ **3.2** Auswahl einer bestehenden Schule (aus Schulleiter-Schritt) möglich.
- ⬜ **3.3** Geburtsdatum-Feld wird angezeigt und ist Pflicht.
- ✅ **3.4** Alterprüfung (`AltersPruefung`) lehnt zu junge Schüler ab bzw. markiert korrekt. *(automatisiert: `AltersPruefungTests`)*
- ⬜ **3.5** Nach Registrierung ist das Schülerkonto zunächst **blockiert / in attente**.
- ⬜ **3.6** Der neue Schüler taucht beim Schulleiter unter „en attente“ auf.

## 4. Schüler-Freischaltung durch Schulleiter

- ⬜ **4.1** Blockierter Schüler wird in der Schülerliste mit Status „Bloqué / en attente“ angezeigt.
- ✅ **4.2** Button **Valider** schaltet den Schüler frei (Status → „Validé“). *(automatisiert: `SetStudentBlockedStateAsync_ValidiertGesperrtenSchueler`)*
- ✅ **4.3** Button **Bloquer** blockiert einen validierten Schüler wieder. *(automatisiert: `SetStudentBlockedStateAsync_BlockiertValidiertenSchueler`)*
- ⬜ **4.4** Statuswechsel wird sofort in der UI reflektiert (ohne manuelles Neuladen).

## 5. Abo-Zuweisung (Schulleiter)

- ⬜ **5.1** Button **Abonnements** öffnet das Level-Panel für den Schüler.
- ⬜ **5.2** Verfügbare Kurs-Level werden aufgelistet.
- ⬜ **5.3** Zuweisung **SKOULOUBASIS** funktioniert (Level wird freigeschaltet).
- ⬜ **5.4** Zuweisung **SKOULOUPLUS** funktioniert (Level wird freigeschaltet).
- ⬜ **5.5** Preis wird zum Zeitpunkt der Zuweisung eingefroren (`PriceAtEnrollment`).
- ⬜ **5.6** Als **Payé** markieren funktioniert und zeigt das Badge korrekt an.
- ⬜ **5.7** Bezahlung zurücknehmen ist NUR erlaubt, solange nicht abgerechnet.

## 6. Abrechnung (SuperAdmin)

- ⬜ **6.1** Login als SuperAdmin und Aufruf `/abrechnung`.
- ⬜ **6.2** Schule aus der Liste auswählbar (Auswahl-Ring sichtbar).
- ⬜ **6.3** Offene Periode zeigt korrekte Anzahl SKOULOUBASIS / SKOULOUPLUS.
- ⬜ **6.4** Offener Umsatz (`OffenerUmsatz`) wird korrekt berechnet.
- ⬜ **6.5** „Clôturer la période“ ist deaktiviert, wenn keine Positionen vorhanden.
- ⬜ **6.6** Periodenabschluss friert bezahlte Positionen ein (`AbgerechnetInPeriodeId` gesetzt).
- ⬜ **6.7** Nach Abschluss erscheint die Periode in der Historie mit korrektem Umsatz.
- ⬜ **6.8** Bezahlung einer abgerechneten Position kann nicht mehr zurückgenommen werden.

## 7. Schüler-Nutzung

- ⬜ **7.1** Login als validierter Schüler → Weiterleitung `/student`.
- ⬜ **7.2** Hero zeigt Name, Schule und aktuelles Niveau korrekt.
- ⬜ **7.3** Kennzahlen (Niveau, abgeschlossene Lektionen, Kontostatus) stimmen.
- ⬜ **7.4** Nur freigeschaltete (bezahlte/zugewiesene) Level sind zugänglich.
- ⬜ **7.5** „Commencer une leçon“ öffnet `/student/lessons`.
- ⬜ **7.6** Quiz (`/student/quizzes`) und Prüfungen (`/student/exams`) sind erreichbar.
- ⬜ **7.7** Blockierter Schüler kann sich nicht einloggen bzw. hat keinen Zugriff.

## 8. Berechtigungen & Sicherheit (Querschnitt)

- ⬜ **8.1** `RequireSuperAdmin` schützt `/admin` und `/abrechnung`.
- ⬜ **8.2** `RequireSchulLeiter` schützt `/schulleiter`.
- ⬜ **8.3** `RequireStudent` schützt `/student` und Unterseiten.
- ⬜ **8.4** Nicht angemeldeter Zugriff auf geschützte Seiten leitet zum Login um.
- ⬜ **8.5** Permission-Konstanten (`Berechtigungen`) werden statt Magic-Strings verwendet.

---

## Automatisierte Tests (Referenz)

- ✅ **Gesamtablauf-Test**: `SkoulouApp.Tests\Services\GesamtAblaufTests.cs` →
  `VollstaendigerAblauf_VonSchulleiterBisSchueler_FunktioniertDurchgehend`
  deckt den kompletten Fluss (Schulleiter → Lehrer → Schüler → Abo → Abrechnung → Schüler-Dashboard) in einem Test ab.
- ⬜ Alle xUnit-Tests in `SkoulouApp.Tests` laufen grün (`dotnet test`).
- ⬜ `AboService`-Tests (Zuweisung, Bezahlung, Rücknahme) bestanden.
- ⬜ `AbrechnungService`-Tests (offene Übersicht, Abschluss, Historie) bestanden.
