# E-Mail-Versand & DNS-Einrichtung (Postmark)

Diese Dokumentation beschreibt die vollständige Einrichtung des transaktionalen
E-Mail-Versands über **Postmark** für die Domain `meinehalloapp.com`, inklusive
der notwendigen DNS-Einträge (DKIM / Return-Path) bei **IONOS**.

---

## 1. Überblick

| Komponente        | Wert / Ort                                             |
|-------------------|--------------------------------------------------------|
| E-Mail-Provider   | Postmark (https://postmarkapp.com)                     |
| Absenderdomain    | `meinehalloapp.com`                                    |
| Absenderadresse   | `kontakt.etse@meinehalloapp.com`                       |
| DNS-Anbieter      | **IONOS (1&1)** – Nameserver `ns…ui-dns.de/.com/.biz/.org` |
| Message Stream    | `outbound` (Transactional) für Auth-/Kontakt-Mails     |
| Konfiguration     | User Secrets (lokal) / App-Konfiguration (Produktion)  |

> **Wichtig:** Die Domain `meinehalloapp.com` wird **nicht** über Azure DNS,
> sondern über **IONOS** verwaltet. Alle DNS-Einträge müssen daher im
> IONOS-DNS-Portal gepflegt werden.

---

## 2. DNS-Einträge

Postmark verlangt für eine verifizierte Absenderdomain **zwei** DNS-Einträge.
SPF ist laut Postmark nicht mehr erforderlich (wird automatisch gehandhabt).

### 2.1 DKIM (TXT-Record)

| Feld     | Wert                                                                 |
|----------|----------------------------------------------------------------------|
| Typ      | `TXT`                                                                 |
| Hostname | `20260724070215pm._domainkey`                                        |
| Wert     | `k=rsa;p=MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQCuyS8A6HJluBAGErsZJK5vZty1w9WhH2xCnxDs6uvAjWFOs3Clly1yDx8+jkyEMqXRXXZhCooflIhXCOt8RtfrDbiURHQhkBYmQszdT+snUtbg+X9GC40J6rh58D1bPGA2p7f0ZWE+/gkSb2tJZLNE2NlEeWHHzXZCI880045QSQIDAQAB` |
| TTL      | 1 Stunde                                                             |

**Zweck:** DKIM signiert ausgehende E-Mails kryptografisch, damit
Empfänger-Mailserver die Echtheit prüfen können.

### 2.2 Return-Path (CNAME-Record)

| Feld     | Wert            |
|----------|-----------------|
| Typ      | `CNAME`         |
| Hostname | `pm-bounces`    |
| Zeigt auf| `pm.mtasv.net`  |
| TTL      | 1 Stunde        |

**Zweck:** Der Return-Path (Bounce-Adresse) liegt so unter der eigenen Domain
und verbessert Zustellbarkeit und Bounce-Verarbeitung.

### 2.3 Eintragen bei IONOS

1. Bei [ionos.de](https://www.ionos.de) anmelden → **Domains & SSL**.
2. Neben `meinehalloapp.com` auf **DNS** / Zahnrad klicken.
3. **Eintrag hinzufügen** und beide Records wie oben anlegen.

> **Kritisch:** Im Feld *Hostname* nur den **relativen** Namen eintragen
> (z. B. `pm-bounces`), **niemals** `.meinehalloapp.com` anhängen – IONOS
> ergänzt die Domain automatisch. Der DKIM-Wert muss als **eine** durchgehende
> Zeichenkette eingetragen werden (keine Zeilenumbrüche, keine Leerzeichen).

---

## 3. Verifizierung prüfen (PowerShell)

Nach dem Speichern kann die Propagation 5 Minuten bis (selten) 48 Stunden dauern.
Prüfung über einen öffentlichen Resolver (Google 8.8.8.8):

```powershell
# DKIM (TXT)
Resolve-DnsName "20260724070215pm._domainkey.meinehalloapp.com" -Type TXT -Server 8.8.8.8

# Return-Path (CNAME)
Resolve-DnsName "pm-bounces.meinehalloapp.com" -Type CNAME -Server 8.8.8.8
```

Direkt beim autoritativen IONOS-Nameserver gegenprüfen:

```powershell
Resolve-DnsName "pm-bounces.meinehalloapp.com" -Type CNAME -Server ns1065.ui-dns.de
```

Sobald beide Records auflösen, in Postmark unter
**Sender Signatures → DNS Settings** auf **Verify** klicken. Beide Einträge
wechseln dann auf **Verified** (grün).

**Status (Stand der Einrichtung):**
- ✅ DKIM – Verified
- ✅ Return-Path – Verified
- ✅ Reale Zustellung getestet – Test-Mail an `kontakt.etse@meinehalloapp.com`
  ist im IONOS-Postfach angekommen (End-to-End bestätigt).

---

## 4. Account-Freigabe (Approval)

Neue Postmark-Accounts starten im **Test mode**. Zum realen Versand an fremde
Domains ist eine manuelle Freigabe nötig (**Request approval**):

- Schritt 1: **Verify a Sender Signature or Domain** – erledigt (DKIM/Return-Path).
- Schritt 2: **Send or receive an email** – durch echten API-Traffic erfüllt.
- Formular mit Angaben zu Sendevolumen, Nachrichtentyp und Empfängerquelle
  ausfüllen (nur transaktionale Mails an selbst registrierte Nutzer, keine
  gekauften Listen).

**Verhalten im Test/Pending-Modus:**
- Versand an die **eigene Domain** (`@meinehalloapp.com`) wird real zugestellt.
- Versand an **fremde Domains** (z. B. `web.de`, `gmail.com`) wird von der API
  mit `OK` bestätigt, aber erst **nach Freigabe** real zugestellt.

---

## 5. Anwendungskonfiguration

### 5.1 User Secrets (lokale Entwicklung)

Die Postmark-Zugangsdaten liegen in den **User Secrets** des Projekts
`HalloApp.WebApp` (nicht in `appsettings.json`, da geheim):

```json
{
  "Postmark": {
	"Enabled": true,
	"ServerToken": "<geheimer-server-token>",
	"FromEmail": "kontakt.etse@meinehalloapp.com",
	"FromName": "HALLO-APP",
	"SupportEmail": "kontakt.etse@meinehalloapp.com",
	"MessageStream": "outbound"
  }
}
```

Bearbeiten über die Kommandozeile:

```powershell
cd HalloApp.WebApp
dotnet user-secrets set "Postmark:ServerToken" "<token>"
```

> Der `ServerToken` ist der **Server-API-Token** aus Postmark
> (Servers → \<dein Server\> → API Tokens), **nicht** der Account-Token.

### 5.2 Produktion

In der Produktionsumgebung werden dieselben Schlüssel über die
Umgebungs-/App-Konfiguration gesetzt (z. B. Azure App Settings), niemals im
Quellcode oder in eingecheckten Dateien.

---

## 6. Sendeweg im Code

| Baustein                  | Datei                                                       |
|---------------------------|------------------------------------------------------------|
| Optionen                  | `HalloApp.Core/Options/PostmarkOptions.cs`                 |
| Abstraktion               | `HalloApp.Core/Services/Email/IEmailService.cs`            |
| Postmark-Implementierung  | `HalloApp.Core/Services/Email/PostmarkEmailService.cs`     |
| Broadcast-Versand         | `HalloApp.Core/Services/Email/BroadcastEmailService.cs`    |
| E-Mail-Vorlagen           | `HalloApp.WebApp/Services/EmailTemplates.cs`               |
| Auth-/Kontakt-Mails       | `HalloApp.WebApp/Services/AuthService.cs`                  |
| DI-Registrierung          | `HalloApp.WebApp/Program.cs`                               |

**Streams:**
- **Transaktional** (`outbound`): Registrierung, Passwort-Reset, Passwort-
  Änderung, Kontakt-Formular, Willkommens-Mail.
- **Broadcast**: Sammel-E-Mails an ganze Schulen/Lehrer/Schüler
  (eigener Broadcast-Stream in Postmark verwenden).

---

## 7. Testversand (manuell)

Ein direkter API-Test (bildet den Weg des `PostmarkEmailService` nach):

```powershell
$secretsPath = "$env:APPDATA\Microsoft\UserSecrets\43aa6e7c-6ec6-4dc0-8010-6b3660d72e6a\secrets.json"
$secrets = Get-Content $secretsPath -Raw | ConvertFrom-Json
$token  = $secrets.Postmark.ServerToken
$from   = $secrets.Postmark.FromEmail
$stream = $secrets.Postmark.MessageStream

$body = @{
	From          = $from
	To            = "kontakt.etse@meinehalloapp.com"
	Subject       = "HalloApp Postmark Test"
	HtmlBody      = "<h2>Test erfolgreich</h2>"
	TextBody      = "Test erfolgreich."
	MessageStream = $stream
} | ConvertTo-Json

Invoke-RestMethod -Method Post -Uri "https://api.postmarkapp.com/email" `
	-Headers @{ "Accept"="application/json"; "X-Postmark-Server-Token"=$token } `
	-ContentType "application/json" -Body $body
```

**Erfolgreiche Antwort:**

```json
{
	"ErrorCode": 0,
	"Message": "OK",
	"MessageID": "8e428a32-309c-4dd6-9d95-2c843dc3c021",
	"SubmittedAt": "2026-07-29T07:36:56Z",
	"To": "kontakt.etse@meinehalloapp.com"
}
```

`ErrorCode: 0` / `Message: "OK"` bedeutet, dass Postmark die Nachricht
angenommen hat. Die Aktivität ist zusätzlich im Postmark-**Activity**-Log
sichtbar.

---

## 8. Fehlersuche

| Symptom | Ursache / Lösung |
|---------|------------------|
| Postmark „We couldn't find your DKIM record" | Propagation noch nicht abgeschlossen (bis 48 h) oder Hostname doppelt qualifiziert. Mit `Resolve-DnsName` gegen 8.8.8.8 und IONOS-NS prüfen. |
| 401 Unauthorized | Falscher/fehlender `ServerToken`. User Secret prüfen. |
| 412 Pending approval | Account noch nicht freigegeben – Versand nur an eigene Domain möglich. |
| 300 / Inactive recipient | Empfänger von Postmark unterdrückt (früherer Hard Bounce). Im Portal reaktivieren. |
| Mail kommt nicht an, API sagt „OK" | Test/Pending-Modus: fremde Domains werden erst nach Freigabe zugestellt. |

---

## 9. Spam-Vermeidung: DNS-Status und weitere Ursachen

Wenn Mails zwar zugestellt werden, aber bei web.de, GMX, Outlook oder Gmail
im **Spam-Ordner** landen, prüft man zuerst die Authentifizierung der Domain
(DKIM, Return-Path, SPF, DMARC). Für `meinehalloapp.com` ist dieser Teil
**bereits vollständig erledigt** — die eigentliche Ursache liegt woanders.

### 9.1 Aktueller Ist-Stand

Geprüft über den öffentlichen Resolver 8.8.8.8:

| Baustein     | Eintrag                                        | Status |
|--------------|------------------------------------------------|--------|
| DKIM         | `20260724070215pm._domainkey` → `k=rsa;p=MIGf…` | ✅ Verified |
| Return-Path  | `pm-bounces` → `pm.mtasv.net`                   | ✅ Verified |
| SPF          | `v=spf1 include:_spf-eu.ionos.com ~all`         | ✅ ausreichend |
| DMARC        | `v=DMARC1; p=none;`                             | ✅ aktiv, ohne Reporting |

Das Postmark-Portal bestätigt denselben Stand: **Send from any address**,
**Ensure effective delivery** und **Monitor email authentication** stehen
alle auf *Active*.

**Bewertung:** Die DNS-Authentifizierung ist vollständig und korrekt.
Die Domain ist damit **nicht** die Ursache für Zustellung in den Spam-Ordner.

### 9.2 Warum SPF hier nicht angefasst werden muss

Postmark weist im DNS-Settings-Dialog ausdrücklich darauf hin:
*„What about SPF? We no longer require SPF DNS records since it's automatically
handled for you."*

Der Grund liegt im Return-Path. Die SPF-Prüfung bewertet **nicht** die sichtbare
`From`-Adresse, sondern den Envelope-Absender (Return-Path). Dieser lautet durch
den verifizierten CNAME `pm-bounces.meinehalloapp.com` und löst auf
`pm.mtasv.net` auf, wo Postmark einen eigenen SPF-Record
(`v=spf1 include:spf.mtasv.net -all`) pflegt. Die SPF-Prüfung läuft damit
bereits heute auf **PASS**, und weil der Return-Path unter der eigenen Domain
liegt, ist auch die **DMARC-Ausrichtung über SPF** erfüllt.

> **Deshalb den SPF-Record der Domain unverändert lassen.** Ein zusätzliches
> `include:spf.mtasv.net` im IONOS-Record wäre wirkungslos (die Domain steht
> nicht im Envelope) und erhöht nur unnötig die Zahl der DNS-Lookups. Das
> 10-Lookup-Limit darf nie überschritten werden, sonst schlägt SPF komplett fehl.

### 9.3 Optional — DMARC-Berichte aktivieren

Der DMARC-Record ist gültig und aktiv. Er enthält allerdings kein `rua`,
daher bekommst du keine Rückmeldung darüber, wie Empfängerserver deine Mails
bewerten. Für die Fehlersuche bei Spam-Problemen ist das der nützlichste
Hebel — es ändert die Zustellbarkeit selbst aber nicht.

Den bestehenden DMARC-Record bearbeiten:

| Feld     | Wert |
|----------|------|
| Typ      | `TXT` |
| Hostname | `_dmarc` |
| Wert     | `v=DMARC1; p=none; rua=mailto:kontakt.etse@meinehalloapp.com; fo=1; adkim=r; aspf=r` |
| TTL      | 1 Stunde |

Bedeutung der Parameter:

| Parameter | Wirkung |
|-----------|---------|
| `p=none`  | Beobachtungsmodus — Mails werden nicht abgewiesen. Startpunkt. |
| `rua=`    | Zieladresse für die täglichen Aggregat-Berichte. |
| `fo=1`    | Bericht auch dann, wenn nur *eine* Prüfung (SPF oder DKIM) fehlschlägt. |
| `adkim=r` / `aspf=r` | Entspannte Ausrichtung — Subdomains gelten als passend. |

### 9.4 Postmark-Seite prüfen

Im Postmark-Portal unter **Sender Signatures → meinehalloapp.com → DNS Settings**
sind bereits alle Bereiche grün:

| Kachel | Bedeutung |
|--------|-----------|
| **Send from any address** – Active | DKIM verifiziert, jede Adresse der Domain darf senden. |
| **Ensure effective delivery** – Active | DKIM **und** Return-Path gesetzt — optimale Zustellbarkeit. |
| **Monitor email authentication** – Active | Ein gültiger DMARC-Record wurde erkannt. |

Zusätzlich unter **Servers → \<Server\> → Message Streams** prüfen, dass für
transaktionale Mails der Stream `outbound` verwendet wird und **kein**
Link-/Open-Tracking aktiv ist (Tracking schreibt Links auf eine fremde Domain
um — ein klassisches Spam-Signal). Anwendungsseitig ist das bereits abgeschaltet.

### 9.5 Verifizieren

```powershell
# DKIM
(Resolve-DnsName "20260724070215pm._domainkey.meinehalloapp.com" -Type TXT -Server 8.8.8.8).Strings

# Return-Path — muss pm.mtasv.net liefern
(Resolve-DnsName "pm-bounces.meinehalloapp.com" -Type CNAME -Server 8.8.8.8).NameHost

# DMARC
(Resolve-DnsName "_dmarc.meinehalloapp.com" -Type TXT -Server 8.8.8.8).Strings

# SPF der Domain (bleibt unverändert, nur zur Kontrolle)
(Resolve-DnsName "meinehalloapp.com" -Type TXT -Server 8.8.8.8).Strings |
	Where-Object { $_ -like "v=spf1*" }
```

### 9.6 Zustellung real testen

1. Test-Mail über die App an ein **web.de-** und ein **gmail.com-**Postfach senden.
2. In Gmail die Mail öffnen → **⋮ → Original anzeigen**. Dort muss stehen:

   ```
   SPF:   PASS
   DKIM:  PASS
   DMARC: PASS
   ```

3. Alternativ eine Mail an `check-auth@verifier.port25.com` senden — der
   Dienst antwortet mit einem vollständigen Authentifizierungsbericht.

> Wenn hier überall **PASS** steht und die Mail trotzdem im Spam landet,
> ist die Ursache **nicht** die Authentifizierung, sondern die
> Absender-Reputation (siehe 9.7) oder der Inhalt der Mail (siehe 9.8).

### 9.7 Die wahrscheinlichste verbleibende Ursache: Reputation

Da DNS und Authentifizierung sauber sind, bleibt als Hauptursache die junge
Absenderdomain. Laut Portal wurden erst **5 von 100 Mails** in der Periode
versendet — die Domain hat schlicht noch keine Sendehistorie, und Freemail-Filter
(web.de und GMX sind besonders streng) stufen unbekannte Absender vorsorglich ein.

Was hilft:

- **Warm-up:** Volumen über Wochen langsam steigern statt sofort viele Mails.
- **Als „Kein Spam" markieren:** Jede manuelle Verschiebung aus dem Spam-Ordner
  in den Posteingang trainiert den Filter. Absender zusätzlich ins Adressbuch
  aufnehmen.
- **Broadcast vom transaktionalen Stream trennen:** In Postmark einen eigenen
  **Broadcast-Stream** anlegen. Sonst beschädigt eine schlechte Sammelmail-Quote
  die Zustellbarkeit von Login- und Passwort-Mails.
- **Bounces beobachten:** Im Postmark-**Activity**-Log unterdrückte Empfänger
  nicht erneut anschreiben.
- **Nach 2–4 Wochen** stabiler Zustellung DMARC von `p=none` auf
  `p=quarantine` anheben — das signalisiert Empfängern eine gepflegte Domain.

### 9.8 Inhaltliche Spam-Faktoren (bereits im Code behoben)

| Maßnahme | Ort |
|----------|-----|
| Klartext-Teil für jede Mail (reine HTML-Mails werden abgewertet) | `PostmarkEmailService.FallbackText` |
| Open-/Link-Tracking deaktiviert | `PostmarkEmailService.BuildPayload` |
| Absendername `HALLO-APP` statt einer URL | `appsettings.json` → `Postmark:FromName` |
| Empfänger als `Name <adresse>` statt nackter Adresse | `PostmarkEmailService.BuildPayload` |
| Tabellen-Layout mit Inline-Styles und `bgcolor`-Attributen | `EmailTemplates.Wrap` |

Weitere inhaltliche Punkte, die beim Formulieren zu beachten sind:

- Betreffzeilen ohne Großbuchstaben-Ketten, Ausrufezeichen-Häufungen oder
  Wörter wie „gratis", „Gewinn", „dringend".
- Ein ausgewogenes Verhältnis von Text zu Links — Mails, die fast nur aus
  einem Button bestehen, werden schlechter bewertet.
- Keine URL-Verkürzer und keine Links auf fremde Domains.
