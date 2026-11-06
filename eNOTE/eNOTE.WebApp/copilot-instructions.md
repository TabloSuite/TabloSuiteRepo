# eNOTE – Copilot Instructions

> Gilt für alles unter `eNOTE/` (eNOTE.Core, eNOTE.WebApp, eNOTE.Tests). Vorrang vor den allgemeinen SKOULOUAPP-Regeln bei eNOTE-spezifischen Themen.

## Einstieg
- Architektur, Fachregeln, lokale Startschritte und Flows stehen in [`eNOTE/Projektuebersicht.md`](../eNOTE/Projektuebersicht.md). Vor Änderungen an Geschäftsregeln den beschriebenen Laden-/Rollen-/Kassen-Datenfluss mit dem tatsächlichen Code abgleichen.
- Projekte: `eNOTE.Core` (Modelle + domänenreine Services, keine EF/Web-Abhängigkeit), `eNOTE.WebApp` (Blazor Interactive Server, Identity, EF Core/SQL Server, Migrationen, Services, Pages), `eNOTE.Tests` (xUnit). Zielplattform .NET 10.
- DB lokal: `localhost / ENOTEDB` (siehe `appsettings.Development.json`). Start: `dotnet run --project eNOTE/eNOTE.WebApp` → https://localhost:62148.

## Verbindliche Fachregeln
- **Mandant = Salon (Laden), ein Geschäft pro Besitzerkonto.** Jedes weitere Geschäft benötigt ein neues Konto mit eindeutigem Pseudonym und Passwort. E-Mail ist wiederverwendbare Kontakt-/Wiederherstellungsadresse, kein Anmeldeschlüssel. Admin/PO verwalten weiterhin mehrere Geschäfte über separate Verwaltungsrechte. Kein Ladenumschalter oder weitere Ladenanlage im bestehenden Konto.
- **Kassierer:** interne Mitarbeiter mit Vorname/Nachname und gehashter PIN, ohne E-Mail/Passwort oder eigene Anmeldung. Das Ladenkonto muss bereits angemeldet sein. Historische Verkäufe bleiben mit Mitarbeiter-GUIDs verknüpft.
- **Daten pro Laden verwalten:** `AktiverSalonState` liefert automatisch die einzige Zuordnung. Services prüfen die tatsächliche Konto-/Ladenzugehörigkeit. Gleiche E-Mail gewährt niemals Zugriff auf andere Konten.
- **Caisse:** Kassenstart per Kassen-PIN löst den Laden über `UserSalon` auf; höchstens eine offene Sitzung pro Laden. Soll-Barbestand = Startbestand + nur Bar-Einnahmen (`KassenRechner.BerechneBarSoll`); Karte erhöht die Kasse nicht. Beim erneuten Öffnen mit laufender Sitzung modalen Hinweis (Kassierer, Start, Soll) zeigen.
- **Rabatte** sind aktivierbar (optional mit Bedingung) und können auf eine Leistung begrenzt sein. Standard-Artikel „Divers" nutzt einen freien Betrag.
- **Kein QZ Tray / keine lokale Drucker-Zusatzsoftware.** Bon via `/bon/{id}` (PDF) und `/bon/{id}/druck` (HTML, ruft `window.print()`); dialogloser Windows-Druck über Browser-Kiosk-Modus (`eNOTE/Bon-Drucken.md`, `eNOTE/Start-Kasse.ps1`).
- **Schulden/Abrechnung:** Plattform-Abopreis global als Standard oder individuell pro Salon; ProduktOwner-Provision (Prozent/Festbetrag) ist plattformintern und darf die Laden-Finanzen nicht verfälschen. Finanzberichte strikt pro Laden.

## Technische Regeln
- Medien: Development ausschließlich lokal außerhalb von wwwroot; Production ausschließlich privater Azure Blob Storage per Managed Identity, ohne lokalen oder Shared-Key-Fallback. Deployment nur vorbereiten, nicht veröffentlichen; keine Secrets lesen, keine DB-/Cloudänderungen oder Browserprüfung ohne gesonderten Auftrag. Vorhandene Änderungen erhalten.
- Pro DB-Operation `IDbContextFactory` verwenden; Rechte serverseitig im Service prüfen (nicht nur in der UI).
- Code/Core-Modelle **deutsch**, alle sichtbaren Nutzertexte **französisch**. Kommunikation/Entwicklerdoku deutsch.
- IDs: `Id` für Basis, zusammengesetzt mit `…ID`/`…Id` (z. B. `SalonId`, `VerkaufId`).
- Bei Modelländerungen EF-Migration erstellen (`dotnet ef migrations add … --project eNOTE/eNOTE.WebApp`).
- Scoped UI-States (`KassenAnzeigeState`, `AktiverSalonState`, `BesitzerPinState`): Komponenten abonnieren `OnChange` und implementieren `IDisposable`/`IAsyncDisposable` zum sauberen Abmelden.
- Design: Soft-UI/Neumorphismus in `wwwroot/app.css`; horizontale Scrollbars vermeiden; Touch-Targets ≥ 44px; native Selects/DatePicker durch `EnoteSelect`/`EnoteDatePicker`; Modals über `ModalDialog`; `AsyncActionButton` für async Aktionen.
- Nach jeder Änderung relevante Tests und den Lösungsbuild ausführen.
