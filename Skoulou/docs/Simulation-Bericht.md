# SKOULOU – Simulationsbericht (E7a)

Stand 14.11.2026. Simulation nach Bauplan §9.4: `Skoulou/Skoulou.Tests/Simulation/` (opt-in).

```
SKOULOU_SIM=1 dotnet test Skoulou/Skoulou.Tests --filter "FullyQualifiedName~SimulationTests.Drei"
  SKOULOU_SIM_FAKTOR=0.15            kleinere Klassen (Entwicklung, ca. 5 min)
  SKOULOU_SIM_BEHALTEN=SkoulouDB_SIM Datenbank behalten (für den Browser-Rundgang E7b)
SKOULOU_SIM=1 SKOULOU_SIM_MESSEN=SkoulouDB_SIM …~Antwortzeiten   nur Antwortzeiten auf vorhandener DB
```

Ergebnis jedes Laufs zusätzlich in `%TEMP%\skoulou-neu\E7\simulation-ergebnis.txt` (ohne Passwörter).

## Aufbau

- **Echte Dienste:** dieselben Registrierungen wie die App (`AddPlattform … AddFinanzen`) auf einer echten SQL-Server-DB
  (`SKOULOU_SIM_*`, danach gelöscht). Abweichungen nur in der Infrastruktur: steuerbare Uhr, Outbox ohne Versand,
  Medienspeicher im Arbeitsspeicher, Passwort-Hash mit 1.000 Iterationen.
- **Zeitachse:** globale Uhr; Stichtage 1. und 15. jedes Monats. Je Stichtag arbeiten die Schulen parallel (8),
  danach laufen Rechnungsdienst (`FaelligeAbrechnenAsync`) und Rechnungswege nacheinander. Zufall je Schule und
  Stichtag mit festem Startwert (20261114).
- **Plattform:** SuperAdmin (Start-Seed) + 2 Admins, 3 Responsables, Tarife (12.500, ab 2025-26 13.000; Sprachschulen
  8.000; Anteil 10 %, zwei Schulen 15 %). 24 Selbstregistrierungen: 22 validiert, 1 abgelehnt, 1 bleibt in Prüfung;
  eine Schule Jan.–März 2025 suspendiert.
- **Schulen:** 8 Primaires, 7 Collèges, 5 Lycées (Trimestres/Semestres), 2 Sprachschulen mit 4 Sessions (Klassen je
  Session). Personal (Koordination mit/ohne Finanzrechte, Lehrkräfte, Direktion unterrichtet), Fächer/Koeffizienten,
  Lehraufträge, Bewertungsvorlagen, Selbstregistrierungen von Schülern (validiert/abgelehnt), Eltern und Geschwister.
- **Je Schuljahr (2023-24, 2024-25, 2025-26, dazu 2026-27 bis heute):** Gebühren (Posten, Raten, Bourse/Fratrie),
  Zahlungen bar/Mobile Money/Virement/Chèque nach Zahlertyp (pünktlich, unregelmäßig, säumig, alles auf einmal, mit
  Avoir), Stornos mit Grund, manuelle Buchungen + Gegenbuchung, monatlicher Appell, Klassenwechsel, Austritte, späte,
  annullierte und rückwirkende Einschreibungen, Sessionwechsel; Leçons/Examens mit Versuchen und Korrektur
  (Stichprobe); Noten aller Klassen, Conseil (Klassenlehrer + Direktion), Veröffentlichung, Berichtigungen;
  Plattformrechnungen je Periode (melden, direkt bestätigen, anfechten → aufrechterhalten oder stornieren + neu
  abrechnen, Storno nach Zahlung, lange offen, Nachtragsrechnungen), Auszahlung der Responsable-Anteile;
  Jahreswechsel-Assistent (Admis, Redouble, Exclu, Partie), Gebühren kopieren/erhöhen, Jahr starten, Neuzugänge.

## Datenmenge und Laufzeit (Faktor 1)

Laufzeit 13 min (Ablauf 12:49, Invarianten + Messungen 0:22), Rechner mit 16 logischen Kernen, lokale SQL-Server-Instanz.

| Bereich | Menge |
|---|---|
| Schulen / Konten | 24 / 14.767 (9.897 Schüler, 4.552 Eltern, 244 Lehrkräfte, 44 Koordinatoren, 3 Admins, 3 Responsables) |
| Einschreibungen / Klassen | 23.765 / 712 (252 Klassenwechsel, 383 Austritte, 689 Sessionwechsel, 331 späte, 39 annullierte, 12 rückwirkende) |
| Noten / Bulletins | 352.697 / 47.795 (1.335 Veröffentlichungen, 16 Berichtigungen mit 425 Bulletins, 523 « Non classé ») |
| Jahresentscheidungen | 14.224 Admis, 2.261 Redouble, 63 Exclu, 143 Partie; 66 Passagen |
| Zahlungen / Buchungen | 64.698 (662 storniert, ≈ 1,3 Mrd. FCFA) / 67.567 (1.950 manuell, 66 Gegenbuchungen) |
| Plattformrechnungen | 222 (9 Nachträge, 29 angefochten, 14 storniert, davon 7 nach Zahlung), 53.254 Positionen, 195 Responsable-Anteile |
| Pädagogik | 35 Leçons, 35 Examens, 306 Zuweisungen, 6.795 Versuche, 3.015 manuelle Korrekturen, 4.912 Appelle |
| Outbox / Audit | 52.580 Mails (nicht versendet) / 90.162 Einträge |

## Geprüfte Invarianten (alle erfüllt)

- Jahrespreis P je Schüler und Jahr exakt in Periodenanteile zerlegt (88 Schuljahre, 47.738 gültige Positionen;
  jede Position = Anteil(P, i, n), Summe über alle Perioden = P).
- Jeder Schüler höchstens einmal je Periode abgerechnet, auch über Klassenwechsel; Positionen stornierter Rechnungen
  storniert; Anzahl/Betrag jeder Rechnung = Positionen; jede beendete Periode vollständig (192 Perioden: genau die
  Schüler mit nicht annullierter Einschreibung im Zeitraum).
- Nummern ohne Lücken/Dubletten: Quittungen 64.698 (88 Kreise), Écritures 67.567, Bulletins 47.795, Factures 222.
- Kassensaldo (`KasseAsync`) = Summe des Journals; Zahlungen im Journal = gültige Zahlungen; « Facture SKOULOU » =
  gemeldete Zahlungen; manuelle Buchungen netto = erfasst (22 Schulen).
- Familiensalden: 648 Schülerkonten (210 zusätzlich aus Elternsicht) = Soll − bezahlt + Vortrag (unabhängiges
  Gebührenbuch der Simulation, über alle Jahre).
- Ränge: 66 Klassen-Perioden, Rang/« ex »/« Non classé » passend zu den gerundeten Mitteln; 2.339 Mittel unabhängig
  aus den Noten nachgerechnet (Barème, Gewicht, Koeffizient, abs = 0).
- Rechte: 19 Zugriffe auf Daten anderer Schulen bzw. ohne Recht (Direction, Coordination mit/ohne Recht, Enseignant,
  Élève, Parent, Responsable, Administrateur) → « refusé ».
- Responsable-Anteile nur für bestätigte Rechnungen (Betrag = abgerundeter Prozentsatz), nach Storno einer bezahlten
  Rechnung negativer Ausgleich (6), Summe je stornierter Rechnung = 0.

Zusätzlich während des Laufs: « Reste à payer » jeder Quittung = unabhängiges Gebührenbuch; Jahreswechsel je
Entscheidung (Admis → Folgestufe, Redouble → gleiche Stufe als Wiederholer, Exclu/Partie → keine Einschreibung);
höchstens ein gültiges Bulletin je Schüler und Periode; erwartete Ablehnungen (Koordinator ohne Recht bucht,
Responsable validiert, Aktion bei suspendierter Schule).

## Gefundene und behobene Fehler

| Befund | Ursache | Behebung |
|---|---|---|
| Sprachschulen: nach dem Jahreswechsel hatten alle Klassen keine Session mehr (Appell, Stundenplan, Zuordnung falsch) | `JahreswechselService` kopierte `Klasse.PeriodeId` nicht | Kopie zeigt auf die Periode mit derselben Nr; Test `Session_Klassen_behalten_ihre_Session_im_neuen_Jahr` (Commit 256da0a) |

Schemaänderungen: keine.

## Antwortzeiten (größte Schule, Endstand; Median von 3)

| Liste | ms | Liste | ms |
|---|---|---|---|
| Élèves (1re page / recherche) | 11 / 11 | Factures SKOULOU (admin / école) | 1 / 2 |
| Parents | 12 | Écoles (admin) | 147 |
| Paiements (année en cours / précédente) | 6 / 11 | Comptes (admin / recherche) | 24 / 61 |
| Comptes élèves (frais, « en retard », compteurs) | 142 / 144 / 136 | Parts des responsables | 1 |
| Journal de caisse | 42 | Tableau de bord admin | 22 |
| Grille de notes / Conseil de classe | 4 / 19 | Audit (admin) | 2 |
| Bulletins (année précédente) / Cockpit | 17 / 12 | PDF : reçu / facture / rapport financier | 17 / 100 / 164 |

Alle Listen deutlich unter 500 ms – keine Änderung nötig (vorher = nachher). Das PDF aller Bulletins einer Klasse
(≈ 45 Seiten) braucht ≈ 0,75 s; das ist Dokumenterzeugung, keine Liste.

## Offene Punkte (fachliche Entscheidung bzw. Beobachtung)

1. **Gleichzeitige Kontoanlage:** legen zwei Schulen im selben Moment Personen mit gleichem Namen an, bekommt die zweite
   « Cette donnée existe déjà (enregistrement simultané) » und muss erneut speichern (2–4× je Lauf bei ≈ 14.800 Anlagen). Möglich:
   automatisch mit dem nächsten Identifiant-Vorschlag wiederholen.
2. **Session-Einschreibungen** enden nicht automatisch am Session-Ende: bleibt ein Teilnehmer ohne Wechsel/Austritt in
   der Klasse, wird er auch für die Folgesession abgerechnet. Die Simulation wechselt bzw. trägt aus wie eine
   sorgfältige Direktion.
3. **Gebühren der Sprachschulen:** Posten gelten für das ganze Jahr (Stufe/Klasse der letzten Einschreibung); eine
   Gebühr « je Session » ist nicht abbildbar – wer nach einer Session geht, schuldet das Jahr (Abhilfe: Ermäßigung).
   Gleiches gilt für Austritte im Jahr allgemein (keine anteilige Gebühr, Abschreibung nur über Ermäßigung).
4. **Storno einer bezahlten Plattformrechnung:** die Ausgabe « Facture SKOULOU » bleibt im Journal der Schule; es gibt
   keine Verrechnung mit der Ersatzrechnung. Der Admin muss die Ersatzrechnung direkt als bezahlt bestätigen (oder
   erstatten) – sonst zahlt die Schule doppelt.
5. **« Comptes élèves »** rechnet bei jeder Seite alle Konten des Jahres (O(n)): 0,13 s bei ≈ 550 Schülern, bei sehr
   großen Schulen (> 3.000) ≈ 1 s zu erwarten. Beobachten.
6. Seite **/direction/journal** (Audit der Schule) ist noch ein Platzhalter (« en préparation ») → E7b.

Stand E7b: Punkte 1, 2, 3 und 6 umgesetzt (f67552d, 8e809da; Migration `SessionGebuehrUndHauptAdmin`), Punkt 4
bleibt auf Wunsch des Nutzers so, Punkt 5 weiter beobachten (im Rundgang 0,4 s bei ≈ 570 Konten).

## Datenbank für den Rundgang (E7b)

`SkoulouDB_SIM` (lokal) enthält den Endstand des letzten vollständigen Laufs: Jahre 2023-24 bis 2025-26
abgeschlossen, 2026-27 läuft (Noten der 1. Periode teilweise erfasst, nicht veröffentlicht), Uhrzeit der Daten
14/11/2026. Alle Passwörter sind zufällig (nirgends gespeichert); E7b setzt bekannte Passwörter per Hilfsskript.

## Rundgang (E7b, Bauplan §9.5)

App lokal auf `SkoulouDB_SIM` (eigener Build-Ordner, Port 5181, Postmark aus), je Rolle ein Konto mit bekanntem
Passwort (nur in `%TEMP%\skoulou-neu\E7\konten.json`). Playwright mit Edge headless, Desktop 1440×900 und Handy
390×844: alle Ziele der Navigation + Profil, dazu Reiter, je Seite ein Detail-Link und der Hauptdialog
(« Nouveau… », « Ajouter… », « Encaisser… », « Importer… »; nur öffnen und schließen). Geprüft: Konsolenfehler,
HTTP ≥ 400/abgebrochene Anfragen, Ladezeit, horizontaler Überlauf am Handy (auch abgeschnittene Inhalte und zu schmale
Felder), englische/deutsche Wörter, Texte ohne Akzente, Platzhalter, leere Listen ohne Erklärung, Fehlerhinweise,
Dialog « Avant de commencer ». E-Mails in den Bildern maskiert. Bilder: `Pictures\SKOULOU-Neubau-E7-Rundgang\<rolle>\`
(erster Durchgang vor den Korrekturen in `<rolle>\vorher\`).

| Rolle (Konto der größten Schule) | Seiten (Desktop/Handy) | Dialoge | langsamste Seite |
|---|---|---|---|
| Visiteur (öffentliche Seiten, Inscription Direction/Élève) | 11 / 11 | Bedingungen 2 | / 0,45 s |
| Administrateur principal | 18 / 18 | 4 | /admin/ecoles 0,37 s |
| Administrateur | 18 / 18 | 3 | /admin/ecoles 0,31 s |
| Responsable | 8 / 8 | – | /responsable/facturation 0,16 s |
| Direction (Collège, ≈ 1.000 Schüler) | 35 / 35 | 12 | /finances 0,74 s (kalt), sonst 0,45 s |
| Direction école de langues (Sessions) | 35 / 35 | 12 | /finances/journal 0,23 s |
| Coordinateur avec droits financiers | 33 / 33 | 12 | /finances 0,44 s |
| Coordinateur sans droits | 26 / 26 | 8 | /direction/eleves 0,16 s |
| Enseignant (professeur principal) | 15 / 15 | – | /bulletins 0,14 s |
| Élève / Parent | 11 / 11, 10 / 10 | – | 0,16 s / 0,14 s |

Ergebnis des letzten Durchgangs: keine Konsolenfehler, keine fehlgeschlagenen Anfragen, kein Überlauf am Handy,
keine Platzhalter, kein Fehler im Server-Protokoll. Rechte wie gewollt: « Administrateurs » nur beim
Hauptadministrator (ein anderer Admin sieht den Eintrag nicht, direkter Aufruf → « Seul l'administrateur principal… »),
Koordinator ohne Recht ohne Finanzen/Établissement/Journal. Inscription: Kästchen « J'accepte les conditions
d'utilisation » + Fenster der Bedingungen für Direction und Élève; nach der Anmeldung erscheint bei keiner Rolle mehr
« Avant de commencer ». Dialog « Nouveau frais » zeigt « Période » (Gebühr je Session).

Gefunden und behoben:

| Befund | Behebung (Commit) |
|---|---|
| Guide utilisateur unvollständig (Admin ohne Facturation/Tarifs/Administrateurs, Responsable ohne Mes parts, Direction ohne Parents, Contenus, Passage, Finanzseiten, Établissement, Journal) und Links ins Leere (Koordinator ohne Recht → /finances) | Texte ergänzt und an die Seiten angeglichen, Schritte nur zu erreichbaren Seiten; Tests: jeder Schritt in der Navigation, jede Seite der Navigation im Guide (98a13eb) |
| Beträge in den Finanzen « F CFA », sonst « FCFA » | einheitlich « 150.000 FCFA » (4a5c59b) |
| Handy: Schuljahr-Auswahl der Kopfzeile lag 13 px unter dem Avatar | Rasterspalte begrenzt, Kalender-Icon am Handy aus, « 2026-2027 » ganz lesbar (4a5c59b) |
| Admin Médias: leere Liste ohne Erklärung | Erklärungstext (4a5c59b) |

Ladezeiten: alle Seiten unter 1 s. Einzige Seite über 1 s war /finances beim ersten Aufruf nach dem Start (1,27 s,
Kaltstart der Abfragen); warm 0,45 s, im letzten Durchgang kalt 0,74 s – keine Änderung nötig.

Hinweise ohne Änderung: Die Liste « Journal » der Direction zeigt in `SkoulouDB_SIM` viele « Conditions acceptées »
aus dem E7a-Lauf (Altbestand, der heutige Code schreibt diese Einträge nicht mehr). Beim Start mit den lokalen
Einstellungen legt der Start-Seed das konfigurierte Admin-Konto zusätzlich in `SkoulouDB_SIM` an (nur lokal).
