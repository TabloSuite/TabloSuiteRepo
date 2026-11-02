# Stundenplan und Bulletin scolaire

## Bestätigtes Ziel
Die App ist derzeit eine Testinstallation. Neue Fachmodelle dürfen umfassend eingeführt werden; bestehende Daten werden trotzdem nicht zurückgesetzt. Alle Schularten nutzen dieselben konfigurierbaren Funktionen.

- Direktor und Koordinator verwalten Stundenpläne sowie Noten aller Klassen/Fächer ihrer berechtigten Schulen.
- Lehrer erfassen Noten ausschließlich für eigene Unterrichtszuordnungen.
- Schüler lesen eigene veröffentlichte Stundenpläne und Bulletins.
- Bulletins: allgemeine Kurs-/Semesterbewertung; drei jahresnotenrelevante Trimester; frei betitelte Test-/Examen-blanc-Bulletins ohne Einfluss auf die Jahresnote.
- Gestaltung und Berechnung bleiben getrennt. Blockbasierte, versionierte Vorlagen mit serverseitigen PDFs; veröffentlichte Dokumente erhalten unveränderliche Snapshots.
- Wiederholerstatus gehört zur jährlichen Klassenzuordnung. Matrikelnummer gehört zum schulbezogenen Schülerprofil.

## Abhängigkeiten und Reihenfolge
1. Schuljahre, jährliche Klassenzuordnungen und Klasse–Fach–Lehrer-Unterrichtszuordnungen.
2. Schulprofil (Logo, Slogan, offizielle Angaben), Matrikelnummer und Stammdatenpflege.
3. Stundenplan mit Zeitraster, Konfliktprüfung, Veröffentlichung und Ansichten für Lehrkräfte/Schüler.
4. Bewertungsperioden, Komponenten, Koeffizienten, Notenerfassung und nachvollziehbare Berechnung.
5. Bulletin-Designer, Klassen-/Typzuweisung, Freigabe, Versionierung und PDF.
6. Anwesenheit, Exam-Import und Sammelausgabe.

## Erste Implementierungsphase
Schuljahr, jährliche Schülerklassenzuordnung und Unterrichtszuordnung werden als neue Tabellen eingeführt. Eine Verwaltungsseite ermöglicht Direktoren/Koordinatoren Schuljahre und Unterrichtszuordnungen zu verwalten. Bestehende Klassen-/Fach-/Lehrerzuordnungen werden als Validierungsgrundlage verwendet. Keine automatische Übernahme von Schülern ohne ausdrückliche Aktion.

Bestehende Kursdurchläufe, Abos und Finanzbuchungen bleiben unverändert. Insbesondere verändern geplante Wochenstunden nicht die bisherigen provisionsrelevanten Wochenstunden. Eine spätere Zusammenführung braucht eine eigene geprüfte Migration.

## Sicherheits- und Datenbankregeln
Serverseitige Prüfung von Rolle, aktiver Schule und konkreter Schulzuordnung bei jeder Operation; eigener Context je Operation. Keine Änderungen an Azure-Konfiguration, keine automatische Veröffentlichung, kein Zurücksetzen von Daten. Die neue Migration ist vor Anwendung auf die konkrete Datenbank zu prüfen.

## Status
Erste Grundlagenphase implementiert:
- Modelle `Schuljahr`, `UnterrichtsZuordnung`, `SchuelerJahresZuordnung` unter `SKOULOUAPP.Core/DataModels/Unterricht`.
- Factory-basierter `UnterrichtsVerwaltungService` unter `SKOULOUAPP.WebApp/Services` (verwendet den konkreten ApplicationDbContext; eine spätere Core-Abstraktion bleibt möglich).
- Französische Verwaltungsseite `/direction/enseignement` mit Schulwahl, Schuljahresanlage und Unterrichtspflege für Direktor/Koordinator.
- Jährliche Schülerzuordnung ist im Service verfügbar und getestet; ihre UI einschließlich Wiederholerpflege ist noch offen.
- Additive Migration `20261102113206_UnterrichtsGrundlagen` erzeugt; ausschließlich neue Tabellen, keine Datenänderungen an bestehenden Tabellen.
- 436/436 Tests erfolgreich, davon sieben neue Fälle. Modellabgleich meldet keine ausstehenden Änderungen. SQL-Migrationsskript erfolgreich erzeugt und strukturell geprüft; noch keine Ausführung gegen SQL Server.

### Fortsetzung: Migration und Dokumentstammdaten
- `UnterrichtsGrundlagen` wurde auf dem verifizierten lokalen SQL Server `localhost`, Datenbank `SKOULOUAPPDB`, erfolgreich angewendet und in `__EFMigrationsHistory` bestätigt.
- Optionale Schulangaben ergänzt: offizielle Bezeichnung, Slogan, Adresse, Telefon, E-Mail, Zulassungsnummer, Ministerium, Republikzeile und Staatsmotto.
- Schülerprofile um Matrikelnummer und Geburtsort erweitert. Matrikelnummern werden getrimmt, großgeschrieben und sind je Schule eindeutig, auch gegenüber archivierten Schülern. Leere Nummern sind erlaubt. SQL-Duplikatfehler werden verständlich behandelt.
- Französische Pflege unter `/direction/enseignement` in standardmäßig geschlossenen Bereichen; Lehrer dürfen diese Verwaltungsdaten nicht bearbeiten.
- Migration `20261102113912_SchulDokumentStammdaten` enthält elf optionale Spalten und einen gefilterten eindeutigen Index. Der bestehende ungefilterte Schulindex bleibt erhalten. Auch diese Migration wurde lokal erfolgreich angewendet; die History bestätigt beide Migrationen ohne ausstehende Einträge.
- Vollständige Testsuite: 440/440 erfolgreich, davon elf Fälle für Unterrichtsverwaltung und Dokumentstammdaten. Keine Veröffentlichung und keine Browserprüfung.

Noch offen: Logo-/Siegelverwaltung, automatische Matrikelnummernvergabe, weitere optionale Schülerangaben, Schülerjahreszuordnungs-UI, Stundenplan, Notenverwaltung und Bulletin-Designer samt Freigabe und PDF. Azure-Datenbank und veröffentlichte App wurden nicht verändert.

### Fortsetzung: Stundenplan und Bewertungsmodul
- Jährliche Schülerklassenzuordnung und Wiederholerpflege sind in der Verwaltungsseite ergänzt. Schuljahresschließung ist mit ausdrücklicher Bestätigung möglich; Zuordnungen sind danach gesperrt.
- Stundenplanverwaltung unter `/emplois-du-temps`: Entwürfe, Veröffentlichung/Rücknahme, Tageskarten, Suche, Lehrer-/Schüler-Lesefilter und geschützter serverseitiger PDF-Download. Konfliktprüfung für Lehrer, Klassen und optionale Räume; veröffentlichte Pläne werden vor Änderung zurückgezogen.
- Migration `20261102114756_Stundenplan` auf `localhost / SKOULOUAPPDB` erfolgreich angewendet, History nach Anwendung bestätigt. Azure unverändert.
- Vollständige Testsuite vor Erweiterung der Bewertungsmodelle: 444/444 erfolgreich. Zusätzlicher Stundenplan-PDF-Test erfolgreich; vier Stundenplantests bestanden.
- Bewertungsmodelle, Factory-Service und französische Seite `/notes` implementiert: allgemeine/Trimester-/Testperioden, Komponenten/Barèmes, Fachkoeffizienten, Notenerfassung durch zugeordnete Lehrer sowie Direktor/Koordinator. Gewichtete Berechnung auf /20 behandelt fehlende Noten nicht als Null; Testperioden sind von der Jahresnote ausgeschlossen.
- Bewertungsmodul anschließend validiert: 455/455 Tests erfolgreich. Die additive Migration `20261102115656_Bewertungen` wurde geprüft und lokal angewendet.
- Periodenwechsel in der Notenoberfläche setzt die Komponentenauswahl zurück; damit bleibt keine versehentliche Auswahl aus einer anderen Periode erhalten.

### Fortsetzung: Bulletin-Grundfunktionen
- `/bulletins` bietet der Direktion schul-/klassen-/typbezogene Vorlagenoptionen: Akzentfarbe, Koeffizienten, Rang, Vorperioden, Signaturbereiche und Fußtext. Dies ist noch kein frei anordnungsfähiger Blockdesigner; die HTML-Vorschau ist vereinfacht und keine maßstabsgetreue PDF-Vorschau.
- Gespeicherte Ausgaben enthalten einen JSON-Snapshot von Schul-/Schülerangaben, Noten, Lehrernamen, Berechnung und Layoutoptionen. Der geschützte serverseitige PDF-Download rendert diesen Snapshot, nicht nachträglich veränderte Stammdaten.
- Veröffentlichung erfordert vollständige Noten des Schülers. Korrektur: Veröffentlichung mit Begründung zurückziehen, Noten korrigieren, neue nummerierte Ausgabe veröffentlichen. Frühere Snapshots bleiben unverändert. Notenkorrekturen bei aktiv veröffentlichtem Schülerbulletin werden gesperrt.
- Schüler sehen nur eigene veröffentlichte Ausgaben; die Direktion sieht auch zurückgezogene Versionen. Lehrer haben keinen Zugriff auf Bulletin-Dokumente.
- Die Jahresnote wird nur beim dritten Trimester und mit allen drei Trimesterpositionen berechnet; Tests zählen nie dazu. Rang wird bei unvollständiger Klassenkohorte nicht ausgegeben.
- Migration `20261102120042_BulletinAusgaben`: ausschließlich zwei neue Tabellen mit restriktiven Fremdschlüsseln, Versionsindex und gefiltertem Index für genau eine aktive Veröffentlichung pro Schüler/Periode. Auf dem erneut geprüften Ziel `localhost / SKOULOUAPPDB` lokal angewendet. Keine Azure-Änderung.
- Lösungsbuild und vollständige Testsuite erfolgreich: 457/457 Tests, einschließlich Snapshot-/Versionierungs-/Berechtigungsfällen und echter Bulletin-PDF-Erzeugung. Keine Browserprüfung oder Veröffentlichung.
- Noch offen: freier Blockdesigner und maßstabsgetreue Vorschau, Logo/Siegel, Anwesenheit, Zeitraster/Komfortfunktionen, Development-Hard-Delete-Folgeflüsse, detaillierte Notenrevisionen und transaktionale Absicherung paralleler Notenkorrektur/Veröffentlichung. Ein vollständiger Abschluss der Gesamtfunktion ist damit noch nicht erreicht.

### Bekannte Bestandswarnungen (weiterhin offen)
Der Lösungsbuild kompiliert, ist aber nicht warnungsfrei. Unter anderem werden NU1902/NU1903 für die bereits referenzierte Version `System.Security.Cryptography.Pkcs 9.0.4` sowie bestehende Razor-/xUnit-Warnungen gemeldet. Diese wurden nicht unterdrückt und sind vor der nächsten Veröffentlichung zu bereinigen. Keine Paketaktualisierung wurde in dieser Grundlagenphase durchgeführt.

### Folgearbeiten an bestehenden Flows
Die neuen restriktiven Fremdschlüssel schützen historische Schuljahresdaten. Bestehende Development-Hard-Delete-Abläufe für Schulen/Benutzer müssen vor Verwendung mit Unterrichts-/Schuljahresdaten entsprechend erweitert werden. Automatische Provisionen bleiben bewusst unverändert. Mehrfachzugriffe auf dieselbe neue Zuordnung werden durch eindeutige Indizes abgesichert; weitergehende parallele Schuljahres-/Zeitraumbearbeitung benötigt zusätzliche Transaktionsprüfung.

## Aktueller Ausbaustand (ersetzt frühere Offen-Listen)
- Kontrollierter Blockdesigner: Identität, Notentabelle, Ergebnisse, Bemerkung und Signaturen lassen sich umordnen; Inhalte und Berechnungen bleiben fachlich geschützt. Die eingebettete A4-Vorschau verwendet das tatsächlich erzeugte PDF. Kein frei positionierbarer WYSIWYG-Designer.
- Optionale Logos und Siegel aus der schuleigenen Medienbibliothek (PNG/JPEG, jeweils höchstens 2 MiB). Bildbytes werden im Snapshot gespeichert, sodass spätere Bildänderungen das veröffentlichte Dokument nicht ändern. Ein eigener Logo-/Siegel-Uploaddialog ist noch nicht ergänzt.
- Tageweise Anwesenheit/Abwesenheit/Verspätung pro Schüler und Bewertungsperiode, ausschließlich Direktion/Koordinator. Nicht erfasste Tage gelten nicht automatisch als anwesend. Veröffentlichte Ausgaben schützen die zugrunde liegenden Einträge vor Korrektur; Summen werden im Bulletin eingefroren.
- `/notes`: Perioden können geschlossen und in einem offenen Schuljahr von der Direktion wieder geöffnet werden. Veröffentlichte Schülerbulletins bleiben auch nach Wiederöffnung geschützt. Konfigurationswerte akzeptieren höchstens zwei Dezimalstellen.
- Serialisierbare Transaktionen mit frischem Context pro Wiederholungsversuch für Notenkorrektur, Komponenten-/Fachkonfiguration, Periodensperre, Veröffentlichung und Rücknahme. Snapshot und Versionsanlage verwenden denselben Context. Ein echter SQL-Parallelitätstest ist noch offen; InMemory-Tests prüfen keine Sperrsemantik.
- Rücknahmemotiv, Bearbeiter und Zeitpunkt werden getrennt vom ursprünglichen Veröffentlichungsgrund gespeichert. Wiederholte Rücknahmen werden abgewiesen.
- Stundenpläne: Kopie als Entwurf, variables Eingabezeitraster und Soll-/Ist-Wochenstunden in 60-Minuten-Einheiten. Keine Änderung der provisionsrelevanten Stunden. Publikation einer überlappenden Kopie bleibt gesperrt.
- Development-Schulreset entfernt auch Bulletin-, Anwesenheits-, Noten- und Stundenplandaten in Fremdschlüsselreihenfolge. Der Regressionstest enthält jetzt entsprechende Daten und schützt geteilte Benutzer. Historisch verwendete Fächer werden nicht gelöscht.
- Schüler-/Periodenwechsel verwirft veraltete Vorschauen; Schülerwechsel entfernt vorherige Bemerkung und Editionsbegründung.
- Migration `20261102121422_BulletinGestaltungUndAnwesenheit` nach SQL-Prüfung auf `localhost / SKOULOUAPPDB` angewendet. Migrationsliste bestätigt den Eintrag unabhängig; Modellabgleich meldet keine ausstehenden Änderungen. Bestehende Vorlagen erhalten die vollständige Standardblockfolge statt einer leeren Zeichenfolge.
- Vollständige Testsuite: **463/463 erfolgreich**, einschließlich Periodensperren, Dreitrimester-Jahresnote, Testausschluss und Rücknahmeprotokoll. Kein Deployment und keine eigenständige Browserprüfung.

### Verbleibende Arbeiten
- Eigener Logo-/Siegel-Upload, zusätzliche frei konfigurierbare Layout-/Vorlagenblöcke und komfortable Sammelausgabe.
- Detaillierte Notenänderungshistorie, echte relationale Parallelitäts-/Löschtests und weitere Prüfung individueller Benutzerlöschungen.
- Automatische Matrikelnummernvergabe und bewusste Übernahme bestehender Exam-Ergebnisse in das neue Bewertungsmodell.
- Bestehende Paket-/Buildwarnungen beheben; vor Veröffentlichung insbesondere die gemeldeten PKCS-Sicherheitswarnungen prüfen.

## Bestätigte Logo-/Stempelkorrektur
- Direktion und Koordinatoren können unter `/bulletins` ein optionales Schullogo direkt hochladen, auswählen, als Vorschau ansehen und aus der Vorlage entfernen. Das Entfernen aus einer Vorlage löscht keine Bibliotheksdatei und keine historischen Dokumentdaten.
- Upload verwendet einen eigenen Factory-Context und die bestehende private Medienablage. Persistierte Rollen, aktive Schulzuordnung und Kontostatus werden vor Lesen/Speichern geprüft. MIME-Typ und Dateiendung werden aus dem Inhalt bestimmt, nicht vom Browser übernommen.
- PNG/JPEG, maximal 2 MiB, höchstens 4096 Pixel pro Seite und 8 Millionen Pixel insgesamt; Headerprüfung vor dem vorhandenen QuestPDF-Bilddecoder. SVG, leere und unlesbare Inhalte werden abgewiesen.
- Kein Siegel-/Cachet-Upload und keine Stempelauswahl mehr. Neue Snapshots enthalten kein digitales Stempelbild. Der PDF-Bereich „Signature et cachet de la direction“ reserviert Platz für echte Unterschrift und Schulstempel.
- Legacy-Spalte und historische Snapshotdaten bleiben erhalten; bereits vorhandene Dokumentversionen werden nicht nachträglich umgeschrieben. Keine neue Schemaänderung erforderlich.
- Logo-Upload ist damit nicht mehr Bestandteil der offenen Liste oben. Weitere Gesamtarbeiten (Sammelausgabe, Notenhistorie, relationale Tests) bleiben offen.

## PDF-Sammelausgabe umgesetzt
- Unter `/bulletins` können Direktion und Koordinatoren eine Klasse auswählen und deren veröffentlichte Bulletins für die oben ausgewählte Schule, Schuljahr und Periode als ein PDF herunterladen.
- Keine automatische Veröffentlichung, keine Neuberechnung: Grundlage sind ausschließlich gespeicherte, aktuell veröffentlichte Snapshots. Zurückgezogene Versionen und andere Klassen/Perioden werden ausgeschlossen. Pro Schüler wird höchstens eine Ausgabe exportiert, sortiert nach gespeichertem Namen (französische Sortierung) und Matrikelnummer.
- Jedes Bulletin startet auf einer neuen Seite und behält seine eigene Seitennummerierung. Einzel- und Sammeldownload verwenden denselben QuestPDF-Dokumentaufbau.
- Der Download-Endpunkt prüft Rollen und aktive Schulmitgliedschaft serverseitig sowie die Zugehörigkeit von Klasse, Periode und Jahreszuordnung. Schüler und Lehrer erhalten keinen Sammelzugriff. Antwort ist nicht cachebar.
- Schutzgrenzen: maximal 200 Bulletins und 32 Mi Zeichen kumulierte Snapshotdaten pro Download. Bei leerer oder zu großer Auswahl erscheint eine französische Meldung statt eines unvollständigen PDFs.
- Regressionen: 470/470 Tests erfolgreich, einschließlich Rollen-/Schul-/Klassen-/Periodengrenzen, Kontosperre, Rücknahme/Neuauflage und echtem zweiseitigem Sammel-PDF. Keine Migration und keine Azure-Änderung erforderlich.
- Die frühere offene Sammelausgabe ist damit erledigt. Notenhistorie, weitergehende Layoutoptionen und reale relationale Parallelitäts-/Löschtests bleiben separate Restarbeiten. Ein Download bildet den Veröffentlichungsstand beim Lesen ab; eine nachträgliche Rücknahme kann bereits heruntergeladene Dokumente nicht widerrufen.

## Abschluss der beauftragten Restfunktionen
Dieser Abschnitt ersetzt die älteren Offen-Listen für die nachfolgend genannten Funktionen.

- **Notenhistorie:** `NotenAenderung` erfasst die erstmalige Erfassung und echte Änderungen an Punkten oder Bemerkungen in derselben Transaktion wie die Note. Identisches erneutes Speichern erzeugt keinen weiteren Eintrag. Alte/neue Werte, Bearbeiter-ID, eingefrorener Bearbeitername und UTC-Zeitpunkt werden gespeichert. Unter `/notes` ist die Historie der ausgewählten Note abrufbar; Lehrkräfte benötigen weiterhin die konkrete Fach-/Klassenberechtigung. Es gibt keine Bearbeitungs-/Löschfunktion für Historieneinträge im normalen Notenworkflow. Alte Änderungen vor Einführung sind nicht rekonstruierbar; kein Anspruch auf revisionssichere Speicherung gegenüber direkten Datenbankadministratoren.
- **Matrikelnummern:** Neue Profile erhalten bei fehlender Nummer `EL-<Schüler-GUID ohne Trennzeichen>`. Das Format ist bewusst stabil und nicht an Klasse oder Schuljahr gebunden. Bestehende eigene Nummern bleiben erhalten; leere Stammdateneingabe löscht sie nicht. In der Unterrichtsverwaltung ergänzt „Attribuer les matricules manquants de l'école“ fehlende Nummern aktiver Profile der ausgewählten Schule. Archivierte Schülernummern bleiben durch den eindeutigen Schulindex geschützt. Veröffentlichte Bulletin-Snapshots werden nicht nachträglich aktualisiert.
- **Kontrollierte Layoutfreiheit:** Schriftgröße 8–12 Punkte, Seitenrand 15–50 Punkte, optionale Lehrkräftespalte und optionale Anwesenheitsangaben ergänzen Blockreihenfolge, Farbe, Logo und bisherige Sichtbarkeitsschalter. Optionen werden validiert und mit jeder Ausgabe eingefroren. Ältere JSON-Snapshots behalten über Standardwerte das bisherige Layout. Es wird kein beliebiger HTML-Vorlageneditor eingeführt.
- **Löschkonsistenz:** Die produktiven Benutzerlöschstellen wurden geprüft. Noch offene Selbstregistrierungen mit akademischen Schüler-/Lehrerzuordnungen werden nicht hart gelöscht; die UI erhält einen Hinweis auf Archivierung/Blockierung. Der bestehende Development-Schulreset entfernt Notenhistorie vor Noten. SQL-Fremdschlüssel verhindern die isolierte Löschung verwendeter Schüler und Noten mit Historie.
- **Exam-Verknüpfung ausdrücklich ausgeschlossen:** Bestehende Exams und die manuelle Bulletin-Notenerfassung bleiben getrennt. Dies ist keine offene Voraussetzung für den Bulletinbetrieb.

### Validierung und Datenbank
- Gesamtsuite: **474/474 erfolgreich**. Der darin enthaltene SQL-Test ist opt-in und führt ohne `SKOULOU_SQL_TESTS=1` keine SQL-Prüfung aus; deshalb wurde er zusätzlich explizit aktiviert und separat erfolgreich ausgeführt.
- Echter SQL-Test migriert eine neue zufällige Datenbank `localhost / SKOULOUAPP_TEST_<GUID>` und entfernt ausschließlich diese anschließend wieder. Geprüft: parallele Notenänderungen samt konsistenter Historienkette, konkurrierende Veröffentlichung mit genau einer aktiven Ausgabe, Schüler-/Noten-Löschschutz und geordnete Historien-/Notenlöschung. Keine vollständige Abdeckung aller möglichen SQL-Rennen oder sämtlicher Löschgraphen behauptet.
- Wiederholung: In PowerShell `$env:SKOULOU_SQL_TESTS='1'` setzen und `dotnet test SKOULOUAPP.Tests --filter FullyQualifiedName~UnterrichtSqlTests` ausführen; anschließend die Umgebungsvariable entfernen. Lokaler SQL Server und Rechte zum Erstellen/Löschen einer Testdatenbank erforderlich.
- Additive Migration `20261102124740_NotenHistorieUndBulletinLayout` auf `localhost / SKOULOUAPPDB` angewendet und unabhängig per Migrationsliste bestätigt. Bestehende Vorlagen erhalten kompatible Standardwerte 10/25/true/true. EF-Modellabgleich: keine ausstehenden Änderungen. Keine Entwicklungsdaten zurückgesetzt.
- Azure, Veröffentlichung und eigenständige Browserprüfungen bleiben unangetastet. Der manuelle rollenbasierte Praxistest steht beim Nutzer noch aus.
- Bestehende Build-/Paketwarnungen sind nicht erledigt, insbesondere `NU1902`/`NU1903` zu `System.Security.Cryptography.Pkcs 9.0.4`. Ein erfolgreicher Build bedeutet nicht Warnungsfreiheit oder Produktionsfreigabe.

### Empfohlener manueller Abnahmelauf
1. Als Direktion und Koordinator jeweils eigene Schule wählen; fremde Schule darf nicht erreichbar sein.
2. Fehlende Matrikelnummern ergänzen und prüfen, dass bestehende Nummern sowie archivierte Profile unverändert bleiben.
3. Als berechtigte Lehrkraft Note erfassen und korrigieren; Historie mit Ausgangs-/Folgewerten ansehen. Fremde Fächer dürfen nicht zugänglich sein.
4. Vorlagenoptionen ändern, Vorschau prüfen und neue Ausgabe veröffentlichen. Bestehende Ausgabe muss ihren gespeicherten Stand behalten.
5. Einzel-/Sammel-PDF drucken, Unterschrifts-/Stempelplatz prüfen; zurückgezogene Ausgaben dürfen im Schülerzugriff und Sammel-PDF nicht erscheinen.
6. Stundenplan-Konflikte, Veröffentlichung und Lehrer-/Schüleransicht mit realistischen Klassen prüfen.
