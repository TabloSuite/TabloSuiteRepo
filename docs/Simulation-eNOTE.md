# eNOTE – Simulationsbericht

Stand 14.11.2026. Auftrag: [Simulationen-Auftrag.md](Simulationen-Auftrag.md).

## S1 – Daten-Simulation

```
ENOTE_SIM=1 dotnet test eNOTE/eNOTE.Tests --filter "FullyQualifiedName~SimulationTests.Achtzehn"
  ENOTE_SIM_FAKTOR=0.1              weniger Verkäufe je Tag (Entwicklung, ca. 3 min)
  ENOTE_SIM_BEHALTEN=ENOTEDB_SIM    Datenbank behalten (für die Klick-Simulation S2)
ENOTE_SIM=1 ENOTE_SIM_MESSEN=ENOTEDB_SIM …~Antwortzeiten   nur Antwortzeiten auf vorhandener DB
```

Code: `eNOTE/eNOTE.Tests/Simulation/` (opt-in, sonst übersprungen). Ergebnis jedes Laufs zusätzlich in
`%TEMP%\sim\enote\simulation-ergebnis.txt` (ohne Passwörter).

### Aufbau

- **Echte Dienste** wie in `Program.cs` (Identity, alle Anwendungsdienste) auf einer echten SQL-Server-DB (localhost,
  `ENOTE_SIM_*` bzw. `ENOTEDB_SIM`; die Entwicklungs-DB `ENOTEDB` wird nie berührt). Keine Roh-Inserts: jede Buchung,
  jede Rechnung, jede Freigabe läuft über den Dienst, den auch die Seite benutzt.
- **Nichts nach außen:** kein Mailweg konfiguriert (Anbieter « Aucun » wird vor dem Start geprüft, sonst Abbruch) –
  alle Mails nur in `MailNachrichten`; Medien im Arbeitsspeicher; keine SMS/Zahlungsanbieter; nie Azure.
- **Globale Uhr:** `TimeProvider` der Simulation; je Laden eine eigene Ortszeit des Tages (AsyncLocal), damit die Läden
  parallel (8) arbeiten. Dafür nutzen jetzt alle Dienste mit Zeitlogik den `TimeProvider` (Kasse, Storno, Abo, Admin,
  Auth/Registrierung, Responsable, Salon, Team, Finanzen, Besitzerbericht; optionaler Parameter, Standard
  `TimeProvider.System` – in der App unverändert). Standardwerte der Modelle (`CreatedAtUtc = DateTime.UtcNow` …)
  setzt ein SaveChanges-Interceptor der Simulation auf die Simulationszeit.
- **Nächtliche Dienste** wie im Hintergrund: fällige Tarife, Perioden aller Läden, Löschungen nach Bedenkzeit, Abo-Mails.
- Fester Zufalls-Startwert (20261114), Passwort-Hash mit 1.000 Iterationen (einzige Abweichung von der App).

### Inhalt (03.03.2025 – 13.11.2026, gut 20 Monate)

- **Plattform:** Hauptadmin (Seed) legt 2 Admins an (einer später gesperrt), 4 Responsables mit Provision % bzw.
  % + Festbetrag.
- **39 Läden** in 7 Ländern (Togo, Bénin, Sénégal, Côte d'Ivoire, Burkina Faso, Mali, Cameroun; Ortszeit UTC+0/+1),
  10 Arten: Salon de coiffure, Institut, Barbershop, Boutique (Pagne mit Sammel-Artikeln), Maquis, Dépôt pharmaceutique,
  Cabinet médical (Aufenthalte), Atelier de couture, Superette, Cyber. Kanäle: Selbstregistrierung (Freigabe durch Admin),
  Admin-Anlage, Responsable-Anlage (Freigabe durch den Responsable), 2 zweite Läden nach der Probezeit; 2 Anmeldungen
  abgelehnt, 1 bleibt wartend. Alle Läden zahlen in FCFA (eNOTE kennt keine Währungseinstellung).
- **Einrichtung je Laden:** Besitzer-PIN, Kontaktdaten, 4–7 Employés (187) mit Kassen-PIN und Rechten (Finances,
  Prestations, Remises, « rembourser »), Kategorien mit Farbe und Unterkategorie, Favoriten, Barcodes, Produkte mit Lager
  und Mindestbestand, Remises (Prozent mit Bedingung, Festbetrag, auf eine Leistung begrenzt), Mobile-Money-Auswahl,
  Salles/Postes, Ausgabenkategorien.
- **Täglicher Betrieb:** PIN-Anmeldung (gelegentlich falsche PIN), Kasse öffnen, Verkäufe bar / Mobile Money / Karte /
  gemischt, Remises, « Divers », Kunden (mit/ohne Telefon-Einwilligung), Ankünfte « Clients présents » mit Notizen und
  Départ, Tickets « en attente » parken/fortsetzen/löschen, Aufenthalte im Cabinet (« Enregistrer sans encaisser »,
  Zurücknehmen, nach Tagen « À la caisse »), Erstattungen (Teil/ganz, alle Zahlwege, mit/ohne Lager, mit/ohne PIN),
  Retraits, Apports, Warenentnahmen, Schichtwechsel, Abschluss mit gelegentlichem Écart; Ausgaben (mit Beleg),
  Nachbestellungen, Abschreibungen, Preisänderung eines Produkts während des Kassenbetriebs.
- **Abo und Plattform:** Probezeit → Perioden, Zahler pünktlich / spät (Kassensperre) / einmal abgelehnt, Bestätigung
  durch Admin oder Eingang beim Responsable, Formelwechsel (Trimestriel, Semestriel, Annuel), Tariferhöhung zum
  Monatsanfang (3.600 → 4.000 F), geplante und wieder aufgehobene Erhöhung, Senkung sofort (3.800 F), individuelle Tarife,
  Provisionen (Anforderung, Reversement, Auszahlung, altes Modell), Kündigung in der Probezeit, Kündigung zahlend,
  zurückgenommene Kündigung, Löschung mit 30 Tagen Bedenkzeit, Laden vorübergehend deaktiviert, Probezeit verlängert,
  Annonces (auch Wartung), manuelle Mails, Kontaktformular, Nachricht an TabloSuite.
- **Rechte-Stichproben** (monatlich, 287× « refusé »): Employé ↔ fremder Laden (Kunden, Présents, Tickets,
  Aufenthalte), Employé ↔ Kundenliste, Besitzer ↔ fremder Laden (Kundenfiche, Vertrag, Kündigung, Salle, Zahlung),
  Responsable ↔ fremder Laden (Freigabe, Probezeit), Responsable entzieht Freigabe, gesperrter Admin.

### Ergebnis (voller Lauf, Faktor 1)

- **Laufzeit** 13 min 09 s (Ablauf 12 min 42 s, 8 Läden parallel), kleiner Lauf (Faktor 0,1) knapp 3 min.
- **Datenmenge** (`ENOTEDB_SIM`, behalten für S2): 39 Läden (33 am Ende im Betrieb), 233 Konten, 187 Employés,
  343 Leistungen, 7.136 Kunden, 15.120 Kassensitzungen, **110.982 Verkäufe** (bar 54.686, Mobile Money 33.992, Karte
  10.149, gemischt 14.704; 10.586 mit Remise, 32.966 mit Kunde) mit 126.733 Zahlungsanteilen, 1.366 Erstattungen,
  8.129 Besuche « Clients présents », 615 Notizen, 2.788 Aufenthaltszeilen (280 Aufenthalte), 4.467 Tickets
  « en attente » (3.491 fortgesetzt), 2.671 Ausgaben, 544 Geldbewegungen, 494 Lagerabgänge, 299 Abo-Rechnungen
  (341 gemeldet, 44 abgelehnt, 297 bestätigt – 137 davon beim Responsable), 137 Provisions-Eingänge, 1.045 Mails nur in
  der Warteschlange, 8 Annonces. Größter Laden: Maquis Médina mit 9.123 Belegen.
- **Befunde 0, Invarianten verletzt 0, Fehler im Log 0**; 456 Einzelprüfungen und 15.421 Kassenabschlüsse live
  bestanden; 199 Kassenstarts wegen Zahlungsverzug gesperrt (nur säumige Zahler), 599 nach Kündigung/Löschung abgelehnt.
- Erwartete Regeln gezählt, nicht als Fehler: Barerstattung bei zu wenig Bargeld (62), Erstattung auf den Rest eines
  Zahlwegs begrenzt (75), Ankunft eines bereits anwesenden Kunden (75).

### Invarianten (alle erfüllt)

- Verkäufe, Umsatz und Erstattungen je Laden = eigene Buchführung der Simulation.
- Belege: Summe der Zahlungsanteile = Summe der Zeilen = Gesamt; Beleg in einer Sitzung seines Ladens und in deren
  Zeitfenster; Ids eindeutig. eNOTE vergibt keine fortlaufenden Bon-Nummern (Guid je Beleg) – « lückenlos » entfällt.
- Erstattungen negativ, nie mehr als das Original, im selben Laden.
- Kassenstand: Soll = Startbestand + Baranteile + Bewegungen in der Kasse (bei jedem Abschluss live und am Ende für
  alle Abschlüsse aus der DB); Écart = gezählt − Soll; keine offene Kasse.
- Finances je Monat (519 Monatsberichte), gesamt und « Par jour » = Verkäufe − Erstattungen.
- Bestände aller Produkte = Anfang + Lieferungen − Verkäufe − Aufenthalte − Abschreibungen − Entnahmen + Rückgaben.
- Kundenumsätze der Liste « Clients » = Summe ihrer Verkäufe (alle Kunden).
- Abo-Rechnungen je Laden und Beginn einmal, lückenlos, erst nach der Probezeit, keine nach der Kündigung, nie 0 F,
  Preis/Basis = Tarif zum Periodenbeginn (eigene Tarifgeschichte der Simulation inkl. individueller Tarife).
- Provisionen nur aus bestätigten Zahlungen, Betrag = Preis × % + Festbetrag (Festbetrag höchstens einmal je Laden und
  Monat), Anforderungen = Summe ihrer Positionen, Auszahlungen (altes Modell) nachgerechnet.
- Gekündigte/deaktivierte Läden können nicht kassieren (599× Kassenstart abgelehnt); Kassensperre nur bei säumigen
  Zahlern; gelöschter Laden ohne Personendaten, Konten anonymisiert.
- Keine Mail versendet (nur Warteschlange); keine Fehler im Log der Dienste.

### Gefundene und behobene Fehler

| Fehler | Ursache / Behebung | Commit |
|---|---|---|
| Speichern eines Produkts (z. B. nur neuer Preis) setzte den Bestand auf den Wert beim Öffnen des Formulars zurück – inzwischen verkaufte Ware « kam zurück » (32 von 135 Produkten falsch) | `KatalogService.SpeichereLeistungAsync` übernimmt beim Bearbeiten nur noch die Änderung aus dem Formular (`stueckzahlBeimOeffnen`); `Leistungen.razor.cs` übergibt den Bestand beim Öffnen | 5706474 |
| Ticket « offert » (Remise ≥ Preis, 0 F) ließ sich nicht kassieren (« Chaque paiement doit avoir un montant positif », Knopf gesperrt) | `KassenRechner.PruefeZahlungen` erlaubt bei 0 F genau einen Anteil über 0 F; Kasse lässt « Espèces » bei 0 F zu | 5706474 |
| Selbst registrierte Läden (ohne Land) hatten keinen einzigen Mobile-Money-Anbieter (0 von 23.043 Zahlungen) | `MobileMoneyService`: ohne Land gilt Togo (wie die Ortszeit Lomé) | e573224 |
| Validierungsmail duzte (« Ton inscription a été validée ») | Betreff « Votre inscription a été validée » | e573224 |

Regressionstests: `KassenAblaufEndToEndTests` (2), `MobileMoneyTests` (1), `LadenRechteTests` (1).
Keine Schemaänderung, keine Migration.

### Antwortzeiten (volle Daten, Median von 3)

Alle Hauptlisten und Berichte liegen auf vollen Daten deutlich unter 500 ms (größter Laden 9.123 Belege, 39 Läden) –
keine Optimierung nötig, vorher = nachher.

| Liste / Aktion | ms |
|---|---|
| Finances: Übersicht 12 Monate / Monat | 75 / 28 |
| Finances: Par jour (12 Monate) | 67 |
| Finances: Dernières ventes Seite 1 / Seite 40 / mit Suche | 38 / 39 / 131 |
| Finances: Clôtures de caisse / Mouvements 12 Monate | 7 / 1 |
| Accueil: Dashboard / Tagesumsätze 14 Tage | 78 / 23 |
| Caisse: Encaisser (Verkauf buchen) / Soll für den Abschluss / Kundensuche | 49 / 1 / 4 |
| Clients: Liste Seite 1 / Kennzahlen / Clients présents | 22 / 68 / 4 |
| Équipe / Abonnement | 28 / 3 |
| Admin: Établissements Seite 1 / alle / Kennzahlen | 75 / 72 / 3 |
| Facturation: Liste / Kennzahlen / Zahlungen 12 Monate | 3 / 1 / 39 |
| Responsable: Mes établissements / Admin: Abrechnung Responsables | 27 / 14 |
| Admin: Bericht Besitzer (12 Monate) | 76 |

### Offene Punkte

- **Aufgehobene Tariferhöhung:** Die Ankündigungsmail « Nouveau tarif à partir du 1er mai 2026 » ging (in der Simulation
  an 35 Besitzer) hinaus; `PreisAnpassungService.AufhebenAsync` blendet nur das Banner aus, eine Entwarnung per Mail gibt
  es nicht. Text/Wunsch bitte entscheiden.
- **Land bei der Selbstregistrierung:** Das Formular fragt kein Land ab; ein Laden aus Bénin/Cameroun bekommt Togo
  (Ortszeit UTC+0, Togo-Anbieter); ändern kann das Land derzeit niemand. Vorschlag: Länderauswahl in
  der Registrierung bzw. in Administration › Établissements.
- **EF-Hinweise im Log:** « Savepoints are disabled because MARS is enabled » (Serializable-Transaktionen von
  Abrechnung/PO mit `MultipleActiveResultSets=True` der lokalen Verbindung) und 6 Abfragen mit mehreren
  Collection-Includes (kartesisches Produkt möglich). Ohne messbare Folgen.
- **Nicht simuliert:** zwei Tablets desselben Ladens gleichzeitig (Bestandsabzug ohne Nebenläufigkeits-Token, Verkauf
  während des Abschlusses) – Kandidat für die Klick-Simulation S2 mit parallelen Nutzern.
- Erstattungsdialog listet 0-F-Zeilen (offert) als erstattbar; der Server antwortet korrekt « Rien à rembourser ».

## S2 – Klick-Simulation (App im Hintergrund)

Stand 15.11.2026. App aus eigenem Build-Ordner (`%TEMP%\sim\enote\build`, Development, http://localhost:5291) auf
`ENOTEDB_SIM`, Server-Log in Datei. **Nichts nach außen:** `Mail__Provider=Aucun` (Token/SMTP leer, Seite E-mails zeigt
inaktiven Versand) – 199 Mails nur in der Warteschlange, 0 versendet; Medien nur im lokalen Temp-Speicher (Development
nutzt nie Azure Blob, die Azure-Schlüssel der User-Secrets werden nicht gelesen); Seed-Admin aus. Konten der SIM-DB mit
bekannten Passwörtern/PINs per Helfer (`pw\konten.mjs`, Zugangsdaten nur in `%TEMP%\sim\enote\konten.json`).
Playwright (Edge headless) mit `pw\lib.mjs` (Bereitschaft über SignalR-Ruhe, Erfassung von Konsolen-/Seitenfehlern,
HTTP ≥ 400, Reconnect, Handy-Überlauf, Texte, Zeiten), Abläufe `kasse.mjs`, `praesenz.mjs`, `rollen.mjs`, Läufe
`lauf.mjs`, `zwei-geraete.mjs`, `monkey.mjs`, DB-Stichprobe `pruefung.mjs`.

### Läufe und Umfang

- **Hauptlauf** (3 Runden, 12:48 min) und **Abschlusslauf** (2 Runden, 7:56 min, neue Zufallsdaten): je **13 gleichzeitige
  Nutzer**, Desktop 1440×900 und Handy 390×844 gemischt – 5 Employés (Maquis, Superette, Cabinet médical, Boutique,
  Barbershop: Kasse öffnen, Verkäufe bar/Mobile Money/Karte/gemischt, Remise, Kunde suchen/anlegen, Montant libre,
  Ticket parken/fortsetzen, Ankunft « Clients présents » mit Fiche/Notiz, « Enregistrer sans encaisser » → « À la caisse »
  → Encaissement, Départ, Erstattung, Retrait bar/Ware, Ventes, Écran client, Réduire, Abschluss mit Écart), 2 Propriétaires
  (Kategorie/Farbe, Leistung + Produkt mit Lager anlegen/ändern/archivieren/löschen, Perte, Etiketten-PDF, Inventaire,
  Remises, Équipe anlegen/PIN/Droits/entfernen, Clients/Fiche/Note/anlegen/löschen, Finances mit Zeiträumen/Reitern/PDF/
  Apport, Dépenses, Paramètres/Salles/Mobile Money, Abonnement: Formel, Kündigung und Löschung bis zum letzten Schritt
  und abbrechen, « J'ai payé », Profil), 2 Responsables (Établissements, Nouvel établissement, Valider, Essai, Demande,
  Facturation), 2 Admins (Établissements, Tarif personnalisé, Tarifs mit geplanter und wieder aufgehobener Erhöhung,
  Mobile Money, Administrateurs anlegen/sperren/entsperren/löschen, Facturation Confirmer/Refuser, E-mails, Annonces,
  Journaux), 2 Visiteurs (Startseite/FAQ/Video, Kontaktformular, Registrierung mit CGU, Hilfe).
- **Zwei Geräte desselben Ladens** (Superette, Tablet + Handy, 3 Läufe): gemeinsame Kasse, 24 Verkäufe desselben
  Lagerartikels (Suche/Kachel und Barcode-Scanner) plus 8× exakt gleichzeitiges « Encaisser », gleichzeitiges
  « Reprendre » desselben geparkten Tickets, Verkauf auf B während A schließt (gleichzeitig / B zuerst / A zuerst),
  doppelter Abschluss.
- **Monkey** je Rolle (8 Nutzer parallel, 9:56 min): auf jeder erreichbaren Seite alle sichtbaren Knöpfe/Links/Reiter,
  Dialoge mit leeren Feldern und Unsinn (negativ, 14 Ziffern, 600 Zeichen, Skript-Tags, Akzente/Anführungszeichen),
  Doppelklick auf Speichern, Zurück-Taste, Neuladen; endgültige Kündigung und Kontolöschung nur auf einem
  Wegwerf-Konto (Kündigung zum Probe-Ende, Löschung ab +30 Tage, Kasse gesperrt – korrekt).
- **Langsame Verbindung:** Tippen mit 250/400 ms WebSocket-Latenz (Playwright `routeWebSocket`).
- **Klicks gesamt ≈ 7.500:** Employé 5.181, Propriétaire 1.466, Admin 480, Responsable 237, Visiteur 128.
  Über die UI gebucht: 461 Verkäufe, 27 Erstattungen, 53 Kassenabschlüsse, 46 Ankünfte, 72 neue Kunden.

### DB-Stichprobe (UI ↔ Datenbank)

Je Laden: Anzahl Verkäufe, Umsatz, Anteile bar/Karte/Mobile Money, Erstattungen, Anzahl Abschlüsse, je Abschluss
angezeigtes Soll = gespeichertes Soll = Start + Baranteile + Bewegungen in der Kasse, Écart = gezählt − Soll; dazu für
alle Läden: höchstens eine offene Kasse, kein Verkauf nach dem Abschluss seiner Sitzung, Anteile = Zeilen = Gesamt, keine
Mail versendet. Hauptlauf 85 Prüfungen, 2 Abweichungen (Festbetrag-Remise, s. u.); **Abschlusslauf 70 Prüfungen,
0 Abweichungen**; Server-Log ohne Fehler.

### Gefundene und behobene Fehler

| Fehler | Ursache / Behebung | Commit |
|---|---|---|
| Bei langsamer Verbindung (250–400 ms) verlor das Tippen Zeichen: Anmeldepasswort « Clic2026sim-test » → « Clc06i-et », Kassen-PIN « 200159 » → « 2019 », Fond « 1525000 » → « 1.200 », Kunden wurden als « KodjvAgbodji 865 » angelegt, Motif/Notiz verstümmelt | 8 Felder mit `value="@x" @oninput` (PasswordInput, PinEingabe, BetragEingabe, KundenAuswahl, Clients-Suche, AnkunftFelder, KundenVerlauf, TarifAnpassung) auf `@bind` bzw. `@bind:get/@bind:set` umgestellt (Blazor gleicht dann mit dem Browser ab); Betragsfeld formatiert beim Verlassen. Wächter-Test gegen das Muster | 8f253df |
| « Mot de passe oublié » / « Réinitialiser » lieferten 500 | `KontoWiederherstellung` nie in Program.cs registriert; mit ihm drei weitere (noch nicht eingebundene) Dienste. Test prüft alle in Komponenten injizierten Dienste | 8f253df |
| `/ausgaben` lieferte 500 in `ENOTEDB_SIM` | Simulationsartefakt: S1-Medienspeicher vergab Namen `sim/<guid>`; jetzt nach App-Regeln, SIM-DB per SQL repariert | 8f253df |
| Zwei Kassen: gleichzeitige Verkäufe desselben Artikels verloren Bestandsabzüge (−15 erwartet, −14 gebucht; SQL-Test 40 Verkäufe → 99 statt 60); Verkauf während des Abschlusses konnte im Soll fehlen | `KassenService`: Verkauf und Abschluss einer Sitzung in einer Transaktion mit `UPDLOCK` auf der Sitzungszeile (über die Retry-Strategie) | f0604e8 |
| Zweites Gerät blieb nach dem Abschluss auf dem anderen Gerät auf der alten Sitzung (« Aucune caisse ouverte. ») | Abschluss meldet sich über `AnwesenheitsSignal`, die Kasse zieht nach (Kassenstart bzw. neue Sitzung, Hinweis, Ticket bleibt) | f0604e8 |
| Remise « 0 % » mit Unsinn-Namen gespeichert | Wert muss > 0 sein (`Rabatte.Pruefe`) | f787bb6 |
| `/remises` am Handy 1111 px breit (lange « Condition ») | `overflow-wrap: anywhere` auf `.rm-karte` | f787bb6 |
| **Festbetrag-Remise je Zeile statt je Bon:** Kasse zeigte 30.300 F, gebucht 29.900 F (3 Zeilen × 200 F) – Bon, Umsatz, Zahlungsanteil und Rückgeld wichen vom Kassierten ab | `KassenService.VerteileFestbetraege`: einmal je Remise, anteilig auf die Zeilen. In der S1-DB 3 Belege (1.200 F) betroffen | 4ac4069 |
| Warnung « Savepoints are disabled because MARS is enabled » je Verkauf (Folge der neuen Transaktion) | Savepoints aus, Ereignis in der DbContext-Konfiguration ignoriert | f787bb6, Abschluss-Commit |

Regressionstests: `EingabefelderTests` (Wächter + Betrag + Remise), `DiensteRegistrierungTests`,
`KassenAblaufEndToEndTests` (Festbetrag, Verkauf/Abschluss auf geschlossener Sitzung), `ZweiKassenSqlTests` (opt-in
`ENOTE_SIM=1`, echte SQL-DB, Gegenprobe ohne Korrektur rot), Medienspeicher der Simulation. 427 Tests grün,
Build ohne neue Warnungen. **Keine Schemaänderung, keine Migration.**

### Antwortzeiten

Ruhig gemessen (ein Nutzer, Median von 3, bis zum sichtbaren Ergebnis): Kasse laden 108 ms, Kachel 49 ms, Encaisser
344 ms, Ventes 127 ms, Clôturer öffnen 51 ms; Besitzer Finances 45 ms (30 jours 762 ms), Clients 240 ms, Prestations
78 ms, Équipe 61 ms; Admin Établissements 252 ms, Facturation 245 ms – keine Aktion > 2 s, keine Seite > 1 s. Unter
Volllast (13 Browser + Server auf einem Rechner) maßen die Browser 3–4 s je Aktion; der Server antwortete dabei in
~30 ms – Engpass war der Testrechner, nicht die App.

### Offene Punkte

- **« En caisse » im Kassenkopf** zeigt je Gerät nur die eigenen Verkäufe (zweites Gerät desselben Ladens); der Abschluss
  rechnet korrekt aus der DB. Ebenso zeigt ein offener Abschlussdialog « Espèces attendues » vom Öffnen. Vorschlag:
  nach jedem Verkauf Ladensignal und Stand aus der DB.
- Perte, Retrait Ware, « Enregistrer sans encaisser », Erstattung ins Lager sowie Bar-Erstattung/Retrait laufen noch ohne
  die Sitzungssperre (gleichzeitig mit einem Verkauf theoretisch verlorener Abzug; nicht beobachtet).
- Remise « Nom »/« Condition » nur im Browser begrenzt (`maxlength`); der Server speichert auch 600 Zeichen.
- Erstattung: « Rembourser » bleibt aktiv, wenn der Betrag den Rest des gewählten Zahlwegs übersteigt (Server lehnt mit
  klarer Meldung ab).
- EF-Hinweise: Kundenabfrage mit `TOP(@p)` ohne `ORDER BY`; Mehrfach-Include (aus S1 bekannt).
- Die S1-Daten-Simulation wurde mit dem korrigierten Medienspeicher nicht neu gerechnet (Regressionstest grün).
