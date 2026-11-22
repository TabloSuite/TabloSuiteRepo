# Copilot Instructions – TabloSuite

Diese Datei ersetzt seit 22.11.2026 die frühere Anleitung der SKOULOUAPP (nicht mehr im Repository; alte Fassung in der Git-Historie).
Verbindlich für alle, die im Repository arbeiten – auch für GitHub Copilot:

- **Team-Regeln, Git, Geheimnisse, Azure:** [`CLAUDE.md`](../CLAUDE.md). Nur im eigenen Branch arbeiten; `master` nur nach ausdrücklicher
  Zustimmung; keine Passwörter, Schlüssel oder Verbindungszeichenfolgen mit Geheimnissen in Dateien; Azure ist Produktion.
- **Überblick über alle Apps:** [`docs/Projektuebersicht.md`](../docs/Projektuebersicht.md).
- **eNOTE:** Fachregeln und Konventionen in [`eNOTE/copilot-instructions.md`](../eNOTE/copilot-instructions.md), Architektur in
  [`eNOTE/Projektuebersicht.md`](../eNOTE/Projektuebersicht.md).
- **SKOULOU:** verbindlich [`Skoulou/docs/Bauplan.md`](../Skoulou/docs/Bauplan.md) (§11 Entscheidungen) und `Skoulou/docs/fachkonzept/01…05`.

Kurz: Sichtbare Texte der Apps nur auf Französisch (« vous »), Code und Kommentare auf Deutsch. In Blazor je Datenbankoperation ein
eigener Context über `IDbContextFactory`. Rechte immer serverseitig im Service prüfen. Fachregeln vor Änderungen mit dem Code abgleichen.
