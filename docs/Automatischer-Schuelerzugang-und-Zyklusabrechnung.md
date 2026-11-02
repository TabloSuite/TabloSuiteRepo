# Automatischer Schülerzugang und Zyklusabrechnung

## Verbindlicher Auftrag
- Alle freigegebenen Schüler erhalten App-Zugang, unabhängig von aktivem Zyklus oder Zahlung der Schule.
- Selbstregistrierungen dürfen erst mit schulbezogener Klassenzuweisung angenommen werden; Ablehnung bleibt möglich.
- Alle Klassenteilnehmer werden pro Zyklus erfasst, einschließlich später hinzukommender Teilnehmer. Gerätebesitz ist kein Abrechnungskriterium.
- Preisverwaltung ausschließlich SuperAdmin: globaler Standard, expliziter Schulpreis vorrangig. Umsetzung: Preis bei Zyklusstart einfrieren; später hinzugefügte Teilnehmer desselben Zyklus verwenden denselben Preis.
- Schul-/Direktions- und Lehrerprovisionen entfallen. ProductOwner-Vergütung betrifft ausschließlich die Plattform, nicht Schulforderungen oder Schulbuchungen.
- Automatische Forderung bei Zyklusabschluss; keine manuelle Rechnungsanlage. Laufende Zyklen als vorläufige Verpflichtung, abgeschlossene unbezahlte Zyklen als offene Forderung anzeigen.
- Manuelle schulweite Sperre mit französischem Hinweis auf die Direktion. Andere Schulen eines mehrfach zugeordneten Nutzers bleiben unabhängig.
- Individuelle Erstanmeldedaten; keine Klartextspeicherung von Passwörtern. Passwort selbst setzen/ändern können.
- Erst nach Fertigstellung: ausschließlich localhost / SKOULOUAPPDB vollständig neu erstellen. Azure ist ausdrücklich nicht freigegeben.

## Historische Bestandsanalyse vor der Umstellung
Die folgenden Punkte beschreiben den Ausgangszustand und die damals erkannten Risiken, nicht den aktuellen Implementierungsstand. Der geprüfte Abschlussstand steht weiter unten.
### Zyklus und Teilnehmer
- FinanzService.DurchlaufStartenAsync erzeugt LevelEnrollments abhängig von Klasse.AutoAssignSkoulouAppAboOnCycleStart, setzt IsPaid und erstellt bereits beim Start Finanzbuchungen.
- DashboardService-Klassenpflege und Schülerzuweisung sowie SchoolUserImportService können weitere Vergaben auslösen. Diese müssen denselben zentralen Teilnehmerpfad verwenden.
- FinanzService.DurchlaufBeendenCoreAsync hat einen optionalen Abrechnungspfad. Der normale Abschluss kann diesen umgehen. Verpflichtung muss für alle Abschlusspfade gelten.
- Bisher löscht der Abschluss CurrentKlasseId und deaktiviert Lerninhalte. Das widerspricht der bestätigten fortbestehenden Klassenzugehörigkeit und dem Zugriff nach Abschluss; Promotion und Schüleransichten gemeinsam prüfen.
- Abrechnungsteilnehmer dürfen nicht erst beim Abschluss aus der aktuellen Klasse rekonstruiert werden. Dauerhafte Zykluszuordnung verwenden; ausgeschiedene Teilnehmer nicht stillschweigend entfernen.

### Geld und Forderungen
- FinanzService.SkoulouAppAboBuchungenErstellenAsync erzeugt bisher einen angenommenen Schülereingang und Plattform-/Lehrerausgänge. Automatische Teilnehmererfassung ist kein tatsächlicher Zahlungseingang.
- AbrechnungService.GetOffeneForderungenAusGeschlossenenKlassenAsync fordert ausdrücklich positive Schul-/Lehreranteile: nach deren Abschaffung würde diese Abfrage Forderungen verlieren.
- AboService behandelt fehlende Provisionen als noch nicht finales Abo und könnte neue Positionen erneut verändern. Alte Vergabe-/Storno-APIs müssen abgeschaltet oder vereinheitlicht werden.
- FinanzService und AbrechnungService ziehen Provisionen vom Schulbetrag ab; zukünftig schuldet die Schule den vollständigen Plattformpreis.
- ProvisionsService, Schul-/Klassenberichte, PDFs und Benachrichtigungen verwenden alte Zähl-/Verteilungslogik.
- Zahlungsankündigung und bestätigter Geldeingang bleiben getrennt. Wiederholte Klicks, Retries und paralleler Abschluss dürfen keine zweite Rechnung oder Buchung erzeugen.
- Schulgebühren bleiben unabhängig; bestehende Verlustbuchung bei bestätigtem Abschluss mit offenen Schulgebühren fachlich beibehalten.

### Zugriff und Anmeldung
- LearningService, TeacherLessonService, DashboardService sowie Quiz-/Exam-Flows verwenden IsUnlocked/IsPaid und IstAktuell. Zugriff darf zukünftig nicht von diesen Zahlungsflags abhängen.
- AuthService.PasswordSignInAsync und Program.OnValidatePrincipal prüfen nur persönliche Sperre/Session-ID. IsActive einer Schule reicht nicht als zentrale Schulzugriffssperre.
- Schulbezogene Sperre nicht durch dauerhaftes Setzen von IsBlocked aller Schüler/Lehrer umsetzen: das würde persönliche Sperren überschreiben und Mehrfachschulen beschädigen.
- Blazor-Circuits, authentifizierte HTTP-Endpunkte, PDFs, Medien und Services müssen die Schule prüfen; allein Login oder Navigation reichen nicht.
- DashboardService.SaveUserAsync verwendet ein gemeinsames Fallbackpasswort; individuelle temporäre Zugangsdaten/Erstwechsel sind sicherer.

## Umsetzungsliste
1. Bestandsanalyse und verbindliche Regeln dokumentieren.
2. Preis-Snapshot und automatische Zyklusteilnahme ohne Schul-/Lehreranteile implementieren.
3. Automatische Abschlussforderungen, Zahlungen und Finanzübersichten umstellen.
4. Schülerzugang und Klassenzuweisung vor Annahme umstellen.
5. Schulbezogene Sperre einschließlich laufender Zugriffe umsetzen.
6. Erstanmeldedaten und Passwortwechsel absichern.
7. Rollenübergreifende UI, Berichte, Exporte und Benachrichtigungen bereinigen.
8. Finanz-, Autorisierungs-, Import-, Wiederholungs- und echte SQL-Tests ergänzen/aktualisieren.
9. Migrations-/Baseline-Strategie mit bestehender Azure-Historie absichern; danach nur lokale DB zurücksetzen.
10. Gesamtsuite und Lösungsbuild ausführen, Dokumentation und manuelle Prüfliste aktualisieren.

## Sicherheitsgrenzen
- Keine destruktiven Datenbankkommandos während der Fachumstellung.
- Bestehende Azure-Datenbank nicht mit einer unpassenden Initialmigration starten. Historische Migrationen nicht unbesehen löschen; Deploymentpfad vor Baseline-Wechsel festlegen.
- Aktuelle Arbeitskopie enthält umfangreiche vorherige Bulletin-Änderungen. Kein Reset oder Verwerfen dieser Änderungen.

## Geprüfter Abschlussstand
Die beauftragte Bereinigung, Identitätsprüfung und lokale technische Vorbereitung sind umgesetzt und automatisiert validiert. Die manuelle fachliche Browserabnahme führt weiterhin der Nutzer durch; der technische App-Start ersetzt diese Abnahme nicht.

### Implementierte Regeln
- Der Plattformpreis wird pro Zyklus eingefroren; spätere Teilnehmer verwenden denselben Preis. Schul-/Lehreranteile sind bei neuen Positionen null.
- Dauerhafte Teilnehmerpositionen bleiben bei Austritt, persönlicher Sperre und Klassenwechsel erhalten. Nicht angenommene Selbstregistrierungen zählen nicht; eine dokumentierte Annahme bleibt trotz späterer Sperre wirksam.
- Neue Zyklen erzeugen beim Abschluss automatisch die volle Forderung und einen Plattformaufwand der Schule. Es entstehen weder fiktive Schülereinnahmen noch behauptete Zahlungseingänge. Der tatsächliche Zahlungseingang bleibt ein eigener Rechnungsvorgang.
- Alte Abo-Vergabe-, Bezahl-, Rücknahme- und Stornoaktionen sind für neue Zykluspositionen gesperrt. Fehlzuordnungen sind ausschließlich über die begründete SuperAdmin-Stornierung korrigierbar.
- Die Teilnehmerübersicht ist im SuperAdmin-Bereich verlinkt; die Abschlussdialoge unterscheiden historische Klassentrennung von neuer Klassenbeibehaltung.
- Zyklusstart/-abschluss, Teilnehmerergänzung und begründete Stornierung verwenden relationale Serializable-Transaktionen und gemeinsame Sperren. Aufnahme und Import laufen atomar über `BenutzerArbeitsvorgang` mit eigenem Context und UserManager pro Versuch. Ein in derselben Transaktion geschriebener Commit-Beleg verhindert Doppelanlage bei verlorener Commit-Antwort.
- Additive Migrationen erhalten die bestehende Historie. `20261102151047_AutomatischeZyklusteilnahme`, `20261102162907_BenutzerVorgangsBestaetigung` und `20261102170932_SchulbezogeneKlassenOhneSprachgruppen` wurden auf isolierten SQL-Testdatenbanken geprüft und beim bestätigten lokalen Neuaufbau ebenfalls angewendet. Die letzte Migration entfernt ausschließlich die überflüssige `CEFRGroup`-Spalte; historische Migrationen wurden nicht ersetzt.
- `SchuelerZugangService` trennt Materialberechtigungen von Zahlungsflags: aktuelle Klasse und schulbezogene Historie sind bei aktivem Konto, nicht archiviertem Profil und aktiver Schule zugänglich. LearningService, TeacherLessonService und Schülerdashboard verwenden diese Prüfung; Veröffentlichung und PDF-Freigaben bleiben verpflichtend.
- Neue Zyklusabschlüsse ziehen bestehende Lektions-/Prüfungsveröffentlichungen nicht mehr zurück. Schüler-Leerseiten und Dashboard verlangen keine Abozahlung oder manuelle Aktivierung. Prüfungslisten und Prüfungsstart sind ebenfalls unabhängig von Abo und aktivem Zyklus; aktuelle berechtigte Klasse, Veröffentlichung, nicht ersetzte Zuweisung und Fristen bleiben verbindlich.
- Entwurf und Abgabe prüfen erneut Konto-, Schul-, Profil- und Klassenrechte; bekannte Versuchs-IDs umgehen Sperren nicht. Abgeschlossene eigene Ergebnisse bleiben innerhalb der aktiven Schule auch ohne aktuelle Klassenzuweisung lesbar. Schüler-Prüfungs-PDF prüft zusätzlich aktive Schulberechtigung und weiterhin Veröffentlichung, aktuelle Klasse und PDF-Freigabe; der HTTP-Export bleibt ohne Lösungen.
- Bei abgelaufenen Abgabefristen werden keine neuen Antworten angenommen. Der private Fristabschluss bei Lehrerneuzuweisung ist vom öffentlichen Schüler-Submit getrennt und verarbeitet ausschließlich abgelaufene Versuche ohne neue Eingaben.
- Videozugang verwendet dieselbe Materialberechtigung, einschließlich veröffentlichter Lektionszuordnung. Ein übergebener positiver Abo-Schalter kann gesperrte/archivierte Konten oder inaktive Schulen nicht übersteuern. Tests verwenden ausschließlich temporäre lokale Dateien, keine Azure-Zugriffe.

### Identität und Bereinigung
- Fachbeziehungen bleiben an unveränderlichen Benutzer-GUIDs. E-Mail und Passwort sind änderbare Zugangsdaten, keine Beziehungsschlüssel. Die berechtigte Verwaltung ändert E-Mail und Benutzername gemeinsam am bestehenden Konto; eine selbstbedienbare E-Mail-Änderung im Schülerprofil ist derzeit nicht implementiert. Passwortwechsel im Profil und Passwort-Reset sind vorhanden; beim erfolgreichen Wechsel wird die aktive Sitzung rotiert.
- Der echte Identity-UserManager ist im Test `SchuelerIdentitaetTests.EmailUndPasswortwechsel_BewahrenIdentitaetMatrikelUndLerndaten` beteiligt: Normalisierung der neuen Adresse, Suche über die neue statt der alten Adresse, Ablehnung eines falschen Passworts, erfolgreicher Passwortwechsel und unveränderte GUID-/Profil-/Matrikel-/Fortschritts-/Teilnahmebezüge.
- Matrikelnummern sind schulbezogene Kennungen. Automatisch wird `EL-` plus GUID ohne Bindestriche erzeugt (35 Zeichen); keine vierstellige Pflicht und kein Ersatz der GUID-Fremdschlüssel. Veröffentlichte Bulletin-Dokumente bleiben unveränderliche Snapshots mit geregelter Berichtigung.
- Aktive CEFR-Felder, Gruppenfreischaltung, unreferenziertes `KlasseModell`, ungenutzte manuelle Lehrer-Zahlungsaktionen und die leere `Teacher.razor.new` wurden entfernt. Testklassen sind schulbezogen; alte Sprachlevel- und widersprüchliche Seed-/Abrechnungsanweisungen wurden bereinigt. Notwendige historische Provisions-/Abrechnungstests bleiben ausdrücklich als Bestandskompatibilität erhalten. Vorhandene Uploads werden nicht allein anhand ihres Dateinamens gelöscht.

### Nachgewiesene Validierung
- Finanz- und Gesamtflusstests wurden auf die bestätigten neuen Regeln aktualisiert; historische Zahlungserklärungsblockaden werden ausdrücklich mit historischen Zyklen geprüft.
- Vollständige Testsuite mit `SKOULOU_SQL_TESTS=1`: **508 erfolgreich, 0 fehlgeschlagen, 0 übersprungen**. Ergebnisdatei: `SKOULOUAPP.Tests/TestResults/abschluss-gesamt.trx`. Der Lösungsbuild ist erfolgreich; EF meldet keine Modelländerungen ohne Migration.
- Drei tatsächlich ausgeführte SQL-Integrationstests verwenden ausschließlich zufällige lokale `SKOULOUAPP_TEST_*`-Datenbanken: paralleler Zyklusstart/Teilnehmerergänzung/Abschluss und Prüfungsstart; gemeinsamer Identity-/Profil-/Import-Rollback sowie verlorene Commit-Antwort; parallele Notenänderungen, Fremdschlüssel und Bulletin-Abläufe. Diese Testdatenbanken werden nach dem Test entfernt.
- Eine separate Quiz-Service-Implementierung ist im Workspace nicht vorhanden. Vorhandene Prüfungs-/Lektionsaufgabenpfade wurden geprüft; daraus wird kein zusätzlicher implementierter Quiz-Workflow behauptet.
- Compiler-/Razor-/Analyzerwarnungen sind nicht vollständig bereinigt und wurden nicht unterdrückt. `System.Security.Cryptography.Pkcs` ist inzwischen zentral auf `9.0.21` festgelegt; die frühere Warnung zu `9.0.4` beschreibt nicht mehr die aktuelle Paketversion. Der Build ist kein Nachweis vollständiger Warnungsfreiheit oder einer manuellen Browserabnahme.

### Lokaler Neuaufbau und Startprüfung
- Ausschließlich `localhost / SKOULOUAPPDB` wurde nach erfolgreicher Testsuite und erfolgreichem Build gelöscht und neu erstellt. Zuvor wurden lokaler Rechner/SQL-Edition geprüft und eine COPY_ONLY-Sicherung mit CHECKSUM erstellt sowie mit RESTORE VERIFYONLY validiert.
- Sicherung: `C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\SKOULOUAPPDB_vor_Neuaufbau_20261102_182114.bak`.
- Neuaufbau über die vorhandene `InitialMigration` und sämtliche Folgemigrationen: **16 angewendete Migrationen**, zuletzt `20261102170932_SchulbezogeneKlassenOhneSprachgruppen`. Keine neue globale Baseline; bestehende Installationen behalten ihren Updatepfad. Azure wurde weder verändert noch neu initialisiert.
- App mit `--no-launch-profile`, `Development`, ausdrücklich lokaler Datenbankverbindung und deaktiviertem externem Blob-Zugriff auf `http://127.0.0.1:5099` gestartet. Startseite und `/auth/login` liefern HTTP 200, ohne Error-/Fatal-Meldungen. Dies war eine technische HTTP-Prüfung ohne Browser und keine angemeldete Rollen-/UI-Abnahme. Der temporäre App-Prozess wurde anschließend beendet.
- Seed geprüft: sechs Rollen (`SuperAdmin`, `ProduktOwner`, `SchulLeiter`, `Koordinator`, `Lehrer`, `Schüler`), neun Berechtigungen, 32 Rollenberechtigungszuordnungen und genau ein aktiver, bestätigter SuperAdmin mit Passwort-Hash und Rollenzuordnung. Schulen und Klassen sind leer (je 0); es wurde kein fester Kurskatalog angelegt. Zugangsdaten werden hier nicht wiederholt; vor produktiver Nutzung das initiale Passwort ändern.

### Manuelle Abnahme durch den Nutzer
1. Mit dem lokalen Development-Profil starten, als SuperAdmin anmelden und das initiale Passwort ändern.
2. Schule, Klassenstufe/Klasse sowie Direktion, Lehrer und Schüler anlegen; individuelle Zugangsdaten und Selbstregistrierung mit Annahme/Ablehnung prüfen.
3. Zyklus starten, späten Teilnehmer hinzufügen, Klasse wechseln und Zyklus schließen; Preis-Snapshot, historische Teilnahme, volle Schulrechnung und Zahlungsbestätigung kontrollieren.
4. Schülerzugang vor/nach Zyklus, schulweite Sperre, Mehrfachschulrechte, Materialveröffentlichung sowie PDFs ohne Lösungen prüfen.
5. Schulfinanzen, Raten/Verlustbuchung, Import, Stundenplan und Bulletin-Veröffentlichung/Berichtigung in den tatsächlich verwendeten Rollen durchspielen.
