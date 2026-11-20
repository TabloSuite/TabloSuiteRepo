# Claude in Visual Studio einrichten (eigenes Claude-Konto)

Stand: 20.11.2026 · Für jeden Entwickler im Team.

Wir nutzen die Erweiterung **« Claude Code Extension for Visual Studio »** von Daniel Carvalho Liedke. Sie ist kein Produkt von Anthropic. Sie bindet **Claude Code** in Visual Studio ein.

---

## Voraussetzungen

- **Windows 11**
- **Visual Studio 2022 oder 2026**
- Ein **eigenes Claude-Konto mit Pro oder höher**. Der kostenlose Plan enthält Claude Code nicht.
- Der Code aus GitHub ist schon geklont (`TabloSuite/TabloSuiteRepo`).

---

## Schritt für Schritt

### 1. Claude-Konto anlegen
Auf https://claude.ai mit deiner eigenen E-Mail-Adresse registrieren und **Pro** abschließen.

### 2. Git für Windows installieren (empfohlen)
Damit Claude Bash-Befehle nutzen kann. Ohne Git nimmt Claude stattdessen PowerShell. In PowerShell:
```
winget install --id Git.Git -e
```

### 3. Claude Code installieren
In PowerShell, **ohne** Administratorrechte:
```
irm https://claude.ai/install.ps1 | iex
```
Danach ein **neues** PowerShell-Fenster öffnen und prüfen:
```
claude --version
```
Es muss eine Versionsnummer erscheinen, z. B. `2.1.322 (Claude Code)`.

### 4. Einmal anmelden
Im selben Fenster:
```
claude
```
Der Browser öffnet sich. Dort mit **deinem** Claude-Konto anmelden und zurück ins Terminal wechseln. Danach `/exit` eingeben.

### 5. Erweiterung in Visual Studio installieren
**Erweiterungen → Erweiterungen verwalten…** → nach **« Claude Code Extension »** suchen (von Daniel Carvalho Liedke) → installieren → **Visual Studio neu starten**.

### 6. Claude-Fenster öffnen
**Ansicht → Weitere Fenster → Claude Code Extension**. Das Fenster am besten rechts andocken. Ab dann öffnet es sich automatisch.

### 7. Projekt öffnen
`TabloSuiteMappe.slnx` laden. Claude liest dann automatisch die Team-Regeln aus **`CLAUDE.md`**: Branches, `master` nur mit Zustimmung, keine Geheimnisse im Repo, Azure nur lesen ohne OK.

### 8. Modell wählen
Über den **🤖-Knopf** in der Erweiterung. Mit Pro reicht das Nutzungslimit mit **Sonnet** deutlich länger als mit Opus. Opus nur für schwierige Aufgaben nehmen.

### 9. Test
Claude fragen:
> Welche Regeln gelten in diesem Projekt für Branches und Azure?

Die Antwort sollte die Regeln aus `CLAUDE.md` enthalten. Dann ist alles richtig eingerichtet.

---

## Einmalig für die Teamarbeit

**Git-Name und E-Mail setzen,** damit deine Commits dir zugeordnet werden. In PowerShell, mit deinen Angaben:
```
git config --global user.name "Vorname Nachname"
git config --global user.email "deine@adresse.de"
```

**Eigenen Branch anlegen,** bevor Claude etwas committet. In Visual Studio unten rechts auf den Branch-Namen klicken → **« Neuer Branch »** → z. B. `Vorname.Nachname` (von `master` aus).

---

## Wenn etwas nicht klappt

| Problem | Lösung |
|---|---|
| Das Terminal geht in einem **eigenen Fenster** auf statt in der Erweiterung | Windows-Einstellungen → nach « Terminal » suchen → **Terminal** auf **Windows-Konsolenhost** stellen |
| `claude` wird nicht gefunden | Ein **neues** PowerShell-Fenster öffnen oder Visual Studio neu starten. Danach `claude --version` |
| Claude meldet « nicht angemeldet » | Im Terminal `claude` starten und mit `/login` neu anmelden |
| Etwas anderes stimmt nicht | Im Terminal `claude doctor` ausführen. Das zeigt eine Diagnose, ohne etwas zu ändern |

---

Quellen: [Claude Code Extension for Visual Studio (GitHub)](https://github.com/dliedke/ClaudeCodeExtension) · [Claude Code – Installation (Anthropic)](https://code.claude.com/docs/en/setup)
