# eNOTE – Copilot Instructions

> Gilt für alles unter `eNOTE/` (eNOTE.Core, eNOTE.WebApp, eNOTE.Tests). Vorrang vor den allgemeinen SKOULOUAPP-Regeln bei eNOTE-spezifischen Themen.

## Einstieg
- Architektur, Fachregeln, lokale Startschritte und Flows stehen in [`eNOTE/Projektuebersicht.md`](../eNOTE/Projektuebersicht.md). Vor Änderungen an Geschäftsregeln den beschriebenen Laden-/Rollen-/Kassen-Datenfluss mit dem tatsächlichen Code abgleichen.
- Projekte: `eNOTE.Core` (Modelle + domänenreine Services, keine EF/Web-Abhängigkeit), `eNOTE.WebApp` (Blazor Interactive Server, Identity, EF Core/SQL Server, Migrationen, Services, Pages), `eNOTE.Tests` (xUnit). Zielplattform .NET 10.
- DB lokal: `localhost / ENOTEDB` (siehe `appsettings.Development.json`). Start: `dotnet run --project eNOTE/eNOTE.WebApp` → https://localhost:62148.

## Verbindliche Fachregeln
- **Mandant = Salon (Laden).** `IstFreigegeben` (Admin-Freigabe), `IstAktiv`, `ProbeBisUtc`. Erste Registrierung legt Besitzer + ersten Laden an (3-Monats-Probe, nicht freigegeben → Admin prüft). Weitere Läden darf der Besitzer erst nach abgelaufener Probezeit eines freigegebenen Ladens anlegen (`SalonService.FuegeSalonHinzuAsync`); neue Läden sind aktiv, aber nicht freigegeben und haben keine neue Probezeit.
- **Daten pro Laden verwalten:** globaler `AktiverSalonState` (scoped, LocalStorage-persistiert) + `SalonSwitcher` in der Topbar. Verwaltungsseiten (Équipe, Prestations, Remises, Finances, Dépenses, Abonnement, Paramètres) laden ihre Daten anhand des aktiven Ladens und reagieren auf `OnChange`. Nicht mehr pro Seite eigene Salon-Auswahl einführen.
- **Caisse:** Kassenstart per Kassen-PIN löst den Laden über `UserSalon` auf; höchstens eine offene Sitzung pro Laden. Soll-Barbestand = Startbestand + nur Bar-Einnahmen (`KassenRechner.BerechneBarSoll`); Karte erhöht die Kasse nicht. Beim erneuten Öffnen mit laufender Sitzung modalen Hinweis (Kassierer, Start, Soll) zeigen.
- **Rabatte** sind aktivierbar (optional mit Bedingung) und können auf eine Leistung begrenzt sein. Standard-Artikel „Divers" nutzt einen freien Betrag.
- **Kein QZ Tray / keine lokale Drucker-Zusatzsoftware.** Bon via `/bon/{id}` (PDF) und `/bon/{id}/druck` (HTML, ruft `window.print()`); dialogloser Windows-Druck über Browser-Kiosk-Modus (`eNOTE/Bon-Drucken.md`, `eNOTE/Start-Kasse.ps1`).
- **Schulden/Abrechnung:** Plattform-Abopreis global als Standard oder individuell pro Salon; ProduktOwner-Provision (Prozent/Festbetrag) ist plattformintern und darf die Laden-Finanzen nicht verfälschen. Finanzberichte strikt pro Laden.

## Technische Regeln
- Medien: Development lokal außerhalb von wwwroot; Production ausschließlich privater Azure Blob Storage per Managed Identity, ohne lokalen/Shared-Key-Fallback. Deployment-Einstellungen und getrennte, zielbestätigte DB-Administration: [Deployment-Azure.md](Deployment-Azure.md). Keine Veröffentlichung, DB-/Cloudänderungen, Secret-Lesezugriffe oder Browserprüfung für den laufenden Vorbereitungsauftrag; vorhandene Änderungen erhalten.
- Pro DB-Operation `IDbContextFactory` verwenden; Rechte serverseitig im Service prüfen (nicht nur in der UI).
- Code/Core-Modelle **deutsch**, alle sichtbaren Nutzertexte **französisch**. Kommunikation/Entwicklerdoku deutsch.
- IDs: `Id` für Basis, zusammengesetzt mit `…ID`/`…Id` (z. B. `SalonId`, `VerkaufId`).
- Bei Modelländerungen EF-Migration erstellen (`dotnet ef migrations add … --project eNOTE/eNOTE.WebApp`).
- Scoped UI-States (`KassenAnzeigeState`, `AktiverSalonState`, `BesitzerPinState`): Komponenten abonnieren `OnChange` und implementieren `IDisposable`/`IAsyncDisposable` zum sauberen Abmelden.
- Design: Soft-UI/Neumorphismus in `wwwroot/app.css`; horizontale Scrollbars vermeiden; Touch-Targets ≥ 44px; native Selects/DatePicker durch `EnoteSelect`/`EnoteDatePicker`; Modals über `ModalDialog`; `AsyncActionButton` für async Aktionen.
- Nach jeder Änderung relevante Tests und den Lösungsbuild ausführen.
