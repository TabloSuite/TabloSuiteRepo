# Upgrade Options — SKOULOUAPPMAPPE

Assessment: 3 SDK-style Projekte auf net9.0; Ziel net10.0; 7 von 9 NuGet-Paketen benötigen ein Upgrade; 1 Paket ist inkompatibel/veraltet markiert; 10 API-Kompatibilitätshinweise in der Blazor-WebApp.

## Strategy

### Upgrade Strategy
Die Solution ist klein, alle Projekte sind bereits moderne SDK-style Projekte, und der Wechsel von net9.0 auf net10.0 ist ein moderner TFM-Wechsel.

| Value | Description |
|-------|-------------|
| **All-at-Once** (selected) | Aktualisiert alle Projekte in einem atomaren Durchlauf; passend für kleine moderne Solutions. |
| Top-Down | Aktualisiert Einstiegspunkt-Anwendungen zuerst und konsolidiert Bibliotheken danach; sicherer bei großen oder stark risikobehafteten Solutions. |

## Project Structure

### Package Management
Die Solution hat mehrere Projekte mit projektweisen PackageReference-Versionen und noch kein zentrales `Directory.Packages.props`.

| Value | Description |
|-------|-------------|
| **Per-Project (defer CPM to post-migration)** (selected) | Behält Paketversionen zunächst in den Projektdateien und reduziert Churn während des Framework-Upgrades. |
| Central Package Management (CPM) | Erstellt `Directory.Packages.props` und verschiebt Paketversionen zentral; sinnvoll als spätere Aufräum-/Wartungsmaßnahme. |

## Compatibility

### Unsupported Packages
Die Assessment-Daten markieren ein Paket als inkompatibel bzw. veraltet und mehrere Microsoft-Pakete benötigen .NET-10-Versionen.

| Value | Description |
|-------|-------------|
| **Resolve Inline** (selected) | Recherchiert und behebt kleine Paketinkompatibilitäten direkt im jeweiligen Upgrade-Task. |
| Defer Resolution | Erzeugt bei umfangreichen Paketproblemen zunächst compilierbare Übergangslösungen und plant Folgeaufgaben. |
| Compatibility Mode | Nutzt Kompatibilitätsreferenzen nur für Sonderfälle mit nicht direkt verwendetem transitive dependency code. |

### Unsupported API Handling
Die Blazor-WebApp enthält 8 binär/source-inkompatible API-Hinweise und 2 Verhaltensänderungen für .NET 10.

| Value | Description |
|-------|-------------|
| **Fix Inline** (selected) | Behebt API-Änderungen direkt im Upgrade-Task, sodass keine Stub-Aufgaben offen bleiben. |
| Defer Complex Changes | Behebt einfache Änderungen direkt und verschiebt komplexe API-Änderungen in Folgeaufgaben. |
