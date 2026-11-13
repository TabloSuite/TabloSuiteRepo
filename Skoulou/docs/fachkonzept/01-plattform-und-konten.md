# Fachkonzept 01 – Plattform & Konten

Quelle ist die alte App: `SKOULOUAPP.Core`, `SKOULOUAPP.WebApp` und `SKOULOUAPP.Tests`, Stand 13.11.2026. Das Dokument beschreibt, **was** die alte App tut, einschließlich ihrer Fehler. Es ist keine Bauanleitung. Begriffe der französischen Oberfläche stehen in « … ».

Geheimnisse und echte Adressen stehen hier nie. Platzhalter sind `<Geheimnis>` und `<Kontaktadresse>`. Die gemeinsame TabloSuite-Kontaktadresse steht im alten Code als Konstante `SkoulouMarke.KontaktEmail`.

**Abkürzungen für Fundstellen** (Format `Kürzel:Zeile`):

| Kürzel | Datei |
|---|---|
| P | `SKOULOUAPP.WebApp/Program.cs` |
| AuS | `WebApp/Services/AuthService.cs` |
| DS | `Core/Services/DashboardService.cs` |
| Seed | `WebApp/Data/Seed/ApplicationDbInitializer.cs` |
| Ctx | `WebApp/Data/ApplicationDbContext.cs` |
| IR | `Core/Services/IdentifiantRegel.cs` |
| SZS | `Core/Services/SchulZugangService.cs` |
| SZR | `Core/Services/SchulZuordnungsRegel.cs` |
| KBS | `WebApp/Services/KontoBenachrichtigungService.cs` |
| KZS | `WebApp/Services/KontoZugangService.cs` |
| MBS | `WebApp/Services/MailboxService.cs` |
| MW | `WebApp/Services/MailWeiterleitung.cs` |
| PES | `Core/Services/Email/PostmarkEmailService.cs` |
| BES | `Core/Services/Email/BroadcastEmailService.cs` |
| ET | `WebApp/Services/EmailTemplates.cs` |
| MS | `WebApp/Services/AzureBlobMediaLibraryService.cs` |
| A / AC | `WebApp/Components/Pages/Admin.razor` / `Admin.razor.cs` |
| T/… | `SKOULOUAPP.Tests/Services/…` |

Die Konfigurationsschlüssel stehen gesammelt in **Abschnitt 5.5**. Sie müssen in der neuen App unverändert bleiben.

---

## 1. Zweck & beteiligte Rollen

**Zweck des Bereichs:**
- Konten und Anmeldung: Identifiant + Passwort, eine aktive Sitzung pro Konto.
- Selbstregistrierung von Direktion (mit neuer Schule) und Schülern.
- Plattformverwaltung durch den Administrator: Schulen, Plattformkonten, Medienübersicht, Preis.
- Betreuung von Schulen durch « Responsables produit ».
- Kommunikation:
  - Annonces in der App
  - E-Mails über Postmark
  - Posteingang der gemeinsamen Adresse mit Weiterleitung an eNOTE
  - Kontaktformular
- Technische Basis: Kultur, Blob-Speicher, Fehlerjournal, Transaktionsschutz und Hilfeinhalte (Videos, Handbuch).

### 1.1 Rollen

Die internen Namen sind DB-Werte und Claims. Sie müssen gleich bleiben, weil Policies, Seed und Tests daran hängen (`Core/DataModels/Identity/Berechtigungen.cs:55-65`).

| Intern | Oberfläche | Ebene | Kurzbeschreibung (Seed-Text, Seed:166-213) |
|---|---|---|---|
| `SuperAdmin` | « Administrateur » | Plattform | Vollzugriff; einziger, der sperren und Schulen bearbeiten darf |
| `ProduktOwner` | « Responsable produit » | Plattform | Betreut **zugewiesene** Schulen; keine Pädagogik; Finanzen nur lesend |
| `SchulLeiter` | « Directeur » / « Direction » | Schule | Leitet genau eine Schule |
| `Koordinator` | « Coordinateur » | Schule | Wie Direktion, aber Finanzen nur über Einzelrechte |
| `Lehrer` | « Enseignant » | Schule | Unterricht der zugewiesenen Klassen |
| `Schüler` (mit Umlaut!) | « Élève » | Schule | Eigene Klasse |
| `Eltern` | « Parent / tuteur » | Schule | Liest nur verknüpfte Kinder |

**Rollenkombinationen:**
- Wer sich selbst als Direktion registriert, erhält `SchulLeiter` **und** `Lehrer` (AuS:159).
- Wer im /admin als Directeur angelegt wird, erhält nur `SchulLeiter`. `SaveUserAsync` entfernt alle Rollen und setzt genau die gewählte (DS:1754-1762).
- Ein Konto mit `ProduktOwner` und `SuperAdmin` zugleich gilt im /admin als Admin (AC:605).

**Plattform- und Schulrollen:**
- Plattformrollen sind `SuperAdmin` und `ProduktOwner` (SZR:21).
- Nur `ProduktOwner` hat Plattformzuordnungen zu Schulen, also `UserSchool.IstPlattformZuordnung` (SZR:24).

### 1.2 Wie Rechte durchgesetzt werden

1. **Seitenattribute** (`[Authorize(Roles=…)]`) sowie Policies in P:143-149:
   - `RequireSuperAdmin` = SuperAdmin
   - `RequireSchulLeiter` = SchulLeiter|Koordinator
   - `RequireTeacher` = Lehrer|SchulLeiter|Koordinator
   - `RequireStudent` = Schüler
   - `RequireSuperAdmin` wird nirgends benutzt.
2. **Service-Prüfungen gegen die DB.** Rolle, Schulzugehörigkeit und Sperrstatus werden bei jeder Aktion neu gelesen, z. B. DS:128-153 und MBS:228-232.
3. **Abgeleitete Schulsperre** (`SchulZugangService`, siehe 3.4). Sie greift bei Login, bei jeder Cookie-Prüfung und bei der Circuit-Revalidierung.
4. **`Permission`/`RolePermission`** werden beim Start gesät (Seed:147-253), aber **nirgends ausgewertet**. Eine Suche nach `Berechtigungen.` findet nur Seed und Migrationen. Die Tabellen sind faktisch Dokumentation:
   - SuperAdmin: alle 9 Rechte.
   - SchulLeiter: teachers.manage, students.approve, students.subscription, billing.manage, courses.manage, reports.view.
   - Koordinator: wie SchulLeiter ohne billing.manage.
   - ProduktOwner: alles außer billing.manage. Das widerspricht der Praxis, dass er nicht sperren darf.
   - Lehrer: students.approve, courses.manage, reports.view.
   - Schüler: reports.view.
   - Eltern: keine.

### 1.3 Rechte in diesem Bereich

| Funktion | SA | PO | SL | Ko | Le | Sch | El | anonym |
|---|---|---|---|---|---|---|---|---|
| `/`, `/contact`, Registrierung, Passwort vergessen/zurücksetzen, Werbevideo | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| `/profil`, `/aide/videos`, `/handbuch` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | – |
| `/admin` | ✓ | ✓ (nur eigene Schulen) | – | – | – | – | – | – |
| `/admin/courrier` (Messagerie) | ✓ (alle) | ✓ (Mails seiner Schulen) | – | – | – | – | – | – |
| `/admin/communication`, `/admin/broadcast`, `/admin/exception-logs` | ✓ | – | – | – | – | – | – | – |
| Schule anlegen/bearbeiten, Preis | ✓ | – | – (eigene Schule über /schulleiter) | (dito) | – | – | – | – |
| Konto anlegen im /admin | SA, PO, SL | nur SL | – | – | – | – | – | – |
| Konto sperren / freischalten im /admin | ✓ / ✓ | – / ✓ | – | – | – | – | – | – |
| Neues Startpasswort (KZS) | – | – | ✓ eigene Schule | ✓ eigene Schule | – | – | – | – |
| Kontakt « Votre responsable » sehen | – | – | ✓ | ✓ | – | – | – | – |

Die Zeile « Konto anlegen » meint die Rollen, die angelegt werden dürfen, nicht die Rolle des Anlegenden.

**Weitere Regeln:**
- **Responsable (PO) im /admin:**
  - Er sieht nur seine Schulen und nur Konten dieser Schulen, ohne SA- und PO-Konten (DS:351-356).
  - Er darf nur freischalten, nicht sperren: « Le blocage d'un compte est réservé à l'administrateur. » (DS:137-139).
  - Er darf nur Directeurs anlegen, und nur in seinen Schulen (DS:109-119).
- **Eltern** haben keinen Zugriff auf Admin- oder Mailfunktionen. Sie sehen Hilfevideos der Gruppen « Pour tous » und « Parent ».

---

## 2. Daten

### 2.1 Entitäten (fachlich relevante Felder)

**AppUser** (Identity-Benutzer, Guid-Schlüssel; `Core/DataModels/Identity/AppUser.cs:11-46`, Längen Ctx:97-102)

| Feld | Bedeutung |
|---|---|
| `UserName` | **Identifiant**, der Anmeldeschlüssel. Eindeutig über `NormalizedUserName`. |
| `Email` (≤256) | Optionale Kontaktadresse, nie Anmeldeschlüssel. `EmailConfirmed` ist true, sobald eine Adresse gesetzt ist (AuS:247, DS:1741). |
| `Vorname`, `Nachname` (≤100), `FullName` (≤200) | Beim Profil-Speichern wird `FullName` = « Vorname Nachname » gesetzt (AuS:654). |
| `TelefonNummer` (≤50) | optional |
| `Geschlecht` | Enum, siehe 2.3 |
| `GeburtsdatumUtc` | optional |
| `IsBlocked` | Persönliche Sperre. Steht auch für « noch nicht validiert ». |
| `IsSelfRegistered` | Konto über /auth/register entstanden |
| `ProfilBildAssetId` | FK auf BlobMediaAsset, `SetNull` (Ctx:164-168) |
| `ActiveSessionId` | Einzig gültige Sitzung (siehe 3.3) |
| `AgbVersion` (≤32), `AgbAcceptedAtUtc` | Angenommene Nutzungsbedingungen |
| `CreatedAtUtc`, `UpdatedAtUtc` | |

**Role** hat zusätzlich `Description`, `CreatedAtUtc` und `UpdatedAtUtc`. `Name` ist eindeutig (Ctx:198). **AppUserRole** hat den Schlüssel (UserId, RoleId), FK `Restrict`.

**Permission** (`Name` eindeutig, ≤150) und **RolePermission** (RoleId, PermissionId) sind ungenutzt, siehe 1.2.

**School**, nur die Plattformfelder (`Core/DataModels/Schools/School.cs`). Die Dokument-Stammdaten wie OffizielleBezeichnung oder Ministerium gehören zum Bereich Bulletins.

| Feld | Bedeutung |
|---|---|
| `Name` | Eindeutig per DB-Index (Ctx:112) und Service-Prüfung |
| `City`, `Country` | Bei Selbstregistrierung ist das Land standardmäßig « Togo » (AuS:142). |
| `DescriptionHtml` | Wird beim Speichern bereinigt (DS:1448). |
| `IsActive` | Standard true. Inaktiv bedeutet: Schule gesperrt (3.4). |
| `SubscriptionPriceOverride` | Schulpreis des Abos. Leer bedeutet globaler Preis. |
| `…CommissionPercentOverride` (3 Felder) | Werden beim Speichern ignoriert und bleiben null (T/DashboardServiceTests.cs:1234/1261). Toter Bestand. |

**UserSchool** ordnet ein Konto einer Schule zu (`Core/DataModels/Schools/UserSchool.cs`).
- Schlüssel ist (AppUserId, SchoolId), beide FK `Restrict` (Ctx:214, 235-245).
- `IstPlattformZuordnung`: Betreuung durch einen PO, keine Mitgliedschaft.
- `CanManageStudents`: true für Lehrer, SL und Koordinator, sonst false (DS:1764).
- Finanz-Einzelrechte für Koordinatoren (gesetzt von der Direktion, Bereich Finanzen): `CanAccessFinances`, `CanViewFinanceDashboard`, `CanCreateFinanceEntries`, `CanManageCyclePayments`, `CanAccessBilling`.
- **Eindeutiger gefilterter Index `IX_UserSchools_EineSchuleProKonto`** auf AppUserId mit `[IstPlattformZuordnung] = 0` (Ctx:216-220; Migration `20261110202806_EineSchuleProKonto`).

**StudentProfile** (nur die Felder dieses Bereichs):
- `IX_StudentProfiles_EinProfilProKonto` ist eindeutig auf AppUserId.
- `IsArchived`
- `AufnahmeBestaetigtAmUtc`: wird beim Freischalten einer Selbstregistrierung gesetzt (DS:3917).
- `ElternTelefonNummer` (≤50) und `ElternEmail` (≤256): aus der Schülerregistrierung.
- `CurrentKlasseId`

**Ankuendigung** (« Annonce »): `Titel` (≤200), `InhaltHtml` (bereinigt), `ErstelltAmUtc`, `AblaufAmUtc` (Index; Ctx:84-85). Sie gilt global für alle Schulen und Rollen und hat keinen Autor.

**MailNachricht** (Messagerie, Ctx:86-91):

| Feld | Bedeutung |
|---|---|
| `SchulID?` | Zuordnung zu einer Schule. Steuert, welcher PO die Mail sieht. |
| `GesendetVonID?` | Absender bei ausgehenden Mails |
| `Absender`, `Empfaenger` (≤256) | |
| `Betreff` (≤300) | |
| `Inhalt` | Bereinigtes HTML |
| `PostmarkMessageID` (≤100) | Eindeutig, gefiltert auf NOT NULL. Dient der Deduplizierung. |
| `IstEingehend` | Eingang oder Ausgang |

Index auf (SchulID, ErstelltAmUtc).

**ExceptionLog** (`WebApp/Data/ExceptionLog.cs`, Ctx:200-211):
- Inhalt: `OccurredAtUtc`, `Message` (≤4000), `ExceptionType` (≤500), `StackTrace`, `RequestPath` (≤2000), `UserId?`, `SchoolId?` (FK `SetNull`).
- Bearbeitungsstatus: `IsHandled`, `HandledAtUtc`, `HandledByUserId`.

**BlobMediaAsset**:
- Herkunft: `SchoolId?`, `OwnerId`, `KlasseId?` (alle FK `Restrict`).
- Datei: `BlobName` (≤600), `OriginalFileName` (≤260), `DisplayName` (≤200), `ContentType` (≤100), `Length`.
- Inhalt: `AltText` (≤250), `MediaType` (Image|Video), `VideoDuration?`.
- Indizes auf (SchoolId, ErstelltAmUtc) und (SchoolId, KlasseId) (Ctx:169-192).

**BenutzerVorgangsBestaetigung**: `Id` und `ErstelltAmUtc`. Ein technischer Commit-Beleg ohne Inhalte (`WebApp/Data/BenutzerVorgangsBestaetigung.cs`), siehe 3.20.

**CommissionSettings** ist eine einzelne Zeile mit globalem Abopreis und PO-Anteil (Standardpreis 12.500 FCFA, `Core/DataModels/Schools/AboTyp.cs:20`). Gepflegt wird sie auf /admin. Fachlich gehört sie zum Bereich Abrechnung.

### 2.2 Beziehungen
- AppUser n:m Role über AppUserRole.
- AppUser 1:n UserSchool n:1 School.
  - Für Schulrollen ist das höchstens **eine** Nicht-Plattform-Zeile.
  - Für PO sind es beliebig viele Plattformzeilen.
- AppUser 1:0..1 StudentProfile, und das Profil hat genau eine Schule.
- ElternKind verknüpft Eltern- und Schülerkonto derselben Schule (Bereich Eltern).
- MailNachricht.SchulID → School wird ohne Navigation genutzt.

### 2.3 Enums und Konstanten

| Enum / Konstante | Werte und Bedeutung |
|---|---|
| `Geschlecht` | `Divers=0` « Divers » (Standard), `Maennlich=1` « Masculin », `Weiblich=2` « Féminin » (`Core/DataModels/Identity/Geschlecht.cs`, `Register.razor.cs:48-53`) |
| `RegistrierungsArt` | `SchulLeiter=0` (neue Schule), `Schueler=1` (bestehende Schule) (`WebApp/Models/Auth/RegisterInputModel.cs:8-15`) |
| `BroadcastAudience` | `SchoolLeaders=0` « Directeurs », `Teachers=1` « Enseignants », `Students=2` « Élèves », `Everyone=3` « Tout le monde », `ManualAddresses=4` « Adresses saisies uniquement » (BES:10-29, `AdminBroadcast.razor.cs:82-89`) |
| `BlobMediaType` | `Image=0`, `Video=1` |
| AGB-Version | `"2026-08-05"` (AuS:27) |
| Sitzungs-Claim | `"skoulouapp_session"` mit dem Wert `ActiveSessionId` (AuS:294) |
| App-Kennung im Betreff | `"[SKOULOU]"` (`WebApp/Services/SkoulouEmailService.cs:8`) |
| Marke | Domain `skoulou.tablosuite.com`, Website `https://skoulou.tablosuite.com`, Firmenseite `https://tablosuite.com`, Kontakt `<Kontaktadresse>` (`WebApp/Services/SkoulouMarke.cs`) |

---

## 3. Geschäftsregeln

### 3.1 Identifiant (IR)
- **Normalisierung** (IR:27-49):
  - getrimmt, Unicode-zerlegt, Akzente entfernt, Kleinbuchstaben
  - Leerzeichen und Apostroph (`'`, `’`) werden zu `.`; `ß` wird zu `ss`
  - erlaubt sind nur `a-z 0-9 . - _`, alles andere entfällt
  - mehrere Punkte werden zu einem; `.-_` am Rand werden entfernt
  - Kürzung auf 40 Zeichen
  - Beispiele: « Ama Mensah » → `ama.mensah`, « Éléonore D'Almeida » → `eleonore.d.almeida` (T/IdentifiantTests.cs:24).
- **Gültig** sind 3–40 Zeichen mit Regex `^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$` (IR:18-24, 51-52). Sonst: « L'identifiant doit comporter 3 à 40 caractères : lettres minuscules sans accent, chiffres, point, tiret ou trait de soulignement. »
- **Eindeutig** über `NormalizedUserName`, also ohne Groß-/Kleinschreibung (IR:88-100). Sonst: « Cet identifiant est déjà utilisé. Choisissez-en un autre. »
- **Vorschlag** (IR:55-85):
  - Grundform `vorname.nachname`. Ist sie kürzer als 3 Zeichen, wird `compte.` vorangestellt.
  - Die Grundform ist höchstens 37 Zeichen lang, damit eine Zählnummer Platz hat.
  - Bei Kollision folgen `…2`, `…3` usw.
  - Ein leeres Identifiant bei der Kontoanlage erzeugt automatisch einen eindeutigen Vorschlag (DS:1697-1711).
- Ältere Konten behalten ihren alten UserName, früher die E-Mail (IR:14).

### 3.2 Passwort
- Die Identity-Regeln (P:97-101): mindestens 6 Zeichen, mindestens eine Ziffer, mindestens ein Kleinbuchstabe. Kein Großbuchstabe und kein Sonderzeichen nötig. Hinweistext: « Au moins 6 caractères, dont 1 chiffre et 1 lettre minuscule. »
- Es gibt **kein Lockout**, denn die Anmeldung prüft mit `lockoutOnFailure: false` (AuS:271).
- **Startpasswort**, wenn bei der Anlage keines angegeben wird: `"aA7!" + Hex(12 Zufallsbytes)`, also 28 Zeichen (`Core/Services/ErstzugangService.cs:8`). Der Import verlangt mindestens 20 Zeichen (T/SchoolUserImportServiceTests.cs:164).
- Einmalige Zugangsdaten erscheinen nur im Speicher, im Dialog « Identifiants initiaux » (A:1009-1019). Sie werden nicht gespeichert und nicht gemailt.
- **Jede Passwortänderung erzeugt eine neue `ActiveSessionId`.** Das gilt für ChangePassword, Reset und das neue Startpasswort. Andere Geräte werden dadurch abgemeldet (AuS:394, 417; KZS:28).

### 3.3 Anmeldung und Sitzung
- Die Anmeldung erfolgt mit Identifiant (UserName, getrimmt) und Passwort (AuS:263-297).
  - Konto fehlt oder Passwort falsch: « L'identifiant ou le mot de passe est incorrect. »
  - **Den Sperrhinweis gibt es erst nach richtigem Passwort** (T/GesamtAblaufTests.cs:35): « Votre compte n'a pas encore été validé ou a été bloqué. » (AuS:26)
  - Ist die Schule gesperrt: `SchulZugangService.GesperrtHinweis` = « L'accès à votre école est suspendu. Veuillez contacter la direction de votre établissement. » Der Login-Endpunkt macht daraus aber `?error=invalid-credentials` (P:729-731). Der Hinweis kommt also nie an.
- Bei Erfolg wird `ActiveSessionId = Guid.NewGuid()` gespeichert und der Claim `skoulouapp_session` gesetzt. « Rester connecté » erzeugt ein persistentes Cookie (AuS:286-296).
- **Eine Sitzung pro Konto.** Die Cookie-Prüfung `OnValidatePrincipal` (P:118-140) läuft **bei jeder HTTP-Anfrage**. Abgemeldet wird, wenn eine dieser Bedingungen gilt:
  - Benutzer fehlt
  - `IsBlocked`
  - `DarfAnmeldenAsync` ist false
  - der Claim fehlt
  - der Claim ≠ `ActiveSessionId`
- Laufende Blazor-Circuits prüft `SchulAuthenticationStateProvider` alle **30 s** gleichwertig nach (`WebApp/Services/SchulAuthenticationStateProvider.cs:15-24`).
- **Ziel nach dem Login** (P:734-755):
  - Schüler ohne `returnUrl` (bzw. mit `/`) → `/student`
  - Eltern ohne `returnUrl` (bzw. mit `/`) → `/parents`
  - sonst `returnUrl`, aber nur wenn es lokal ist (beginnt mit `/`, nicht mit `//`)
  - sonst `/`
- Pfade: `LoginPath`/`AccessDeniedPath` = `/auth/login`, `LogoutPath` = `/auth/logout` (P:114-116). Logout ist ein POST und leitet nach `/` (P:758-762).

### 3.4 Abgeleitete Schulsperre (SZS)
- Eine Schule ist **freigegeben**, wenn `IsActive` gilt **und kein Konto mit Rolle SchulLeiter dieser Schule gesperrt ist** (SZS:12-17). Ein einziger gesperrter Directeur sperrt also die ganze Schule. Die Sperre wird abgeleitet und nie in Benutzerflags kopiert (SZS:7).
- `DarfAnmeldenAsync` (SZS:22-32):
  - Das Konto darf nicht gesperrt sein.
  - SuperAdmin und PO dürfen immer.
  - Konten ohne Schulbezug (keine UserSchool, kein aktives StudentProfile) dürfen auch.
  - Alle anderen brauchen **mindestens eine** freigegebene zugeordnete Schule. Ein Altkonto mit zwei Schulen bleibt also angemeldet (T/SchulZugangServiceTests.cs:12).
- Wirkung auf andere Bereiche:
  - Schülerzugang zu Klassen und Videos: leer bei gesperrter Schule
  - Direktionsrechte entfallen (DS:3933-3944)
  - Medien sind für das Personal unsichtbar (MS:595-599)
  - Logo-Upload ist verboten
- Es gibt **keine** E-Mail und keine Rückfrage beim Deaktivieren.

### 3.5 Ein Konto = eine Schule (seit 10.11.2026)
- Die Regel gilt für SchulLeiter, Koordinator, Lehrer und Schüler (SZR:7-13). Wer an zwei Schulen arbeitet, bekommt zwei Konten. Die beiden Identifiants unterscheiden sich, z. B. `ama.mensah` und `ama.mensah2` (T/IdentifiantTests.cs:37).
- `SchulZuordnungsRegel.PruefenAsync` (SZR:30-50):
  - Plattformrollen sind frei.
  - Sonst braucht es **genau 1** Schule: « Un compte appartient à un seul établissement. Choisissez un établissement. »
  - Hat das bestehende Konto schon eine andere Nicht-Plattform-Schule oder ein StudentProfile einer anderen Schule: « Ce compte appartient déjà à un autre établissement. Pour cet établissement, créez un nouveau compte. »
  - Ein Altkonto mit zwei Schulen kann nicht mehr gespeichert werden. Beide Links bleiben bestehen (T/DashboardServiceTests.cs:382).
- Die DB sichert die Regel zusätzlich mit dem gefilterten Unique-Index ab (2.1).
- `SyncUserSchoolsAsync` entfernt zuerst und fügt danach hinzu, damit der Index nicht kurz verletzt wird (DS:3026-3028).
- Mehrere Directeurs pro Schule werden **nicht** verhindert.

### 3.6 Selbstregistrierung (/auth/register)

**Vorprüfung `ValidateRegistrationAsync`** (AuS:70-114). Sie läuft vor dem AGB-Dialog und legt nichts an (T/AuthServiceRegistrationTests.cs:101/130).
- Identifiant normalisieren und prüfen (3.1).
- Direktion:
  - E-Mail ist Pflicht: « Une adresse e-mail est nécessaire pour la direction (validation, factures, mot de passe oublié). »
  - Schulname ist Pflicht: « Veuillez indiquer le nom de votre établissement. »
  - Der Schulname muss neu sein, exakter Vergleich nach Trim: « Une école portant ce nom existe déjà. »
- Schüler:
  - Eine Schule muss gewählt sein: « Veuillez choisir votre établissement. »
  - Sie muss existieren und `IsActive` sein: « L'école sélectionnée est introuvable. »

**Formularregeln** (`RegisterInputModel.cs`):

| Feld | Regel |
|---|---|
| Prénom, Nom | Pflicht, je ≤100 |
| Identifiant | 3–40 Zeichen |
| E-Mail | optional, ≤256, Format |
| Téléphone, Téléphone d'un parent | optional, ≤50, Telefonformat |
| E-mail d'un parent | optional, ≤256 |
| Date de naissance | optional |
| Schulname | ≤200 |
| Ville, Land | je ≤120; Land standardmäßig « Togo » |
| Passwort | 6–100 Zeichen |
| Bestätigung | « Les mots de passe ne correspondent pas. » |

**Weitere Regeln:**
- Die AGB-Zustimmung ist Pflicht und wird vor jeder Anlage geprüft: « Veuillez accepter les conditions d'utilisation pour finaliser l'inscription. » (AuS:123-126, 175-178).
- **Neues Konto** (AuS:232-251):
  - `IsBlocked = true` und `IsSelfRegistered = true`
  - AGB-Version und -Zeitpunkt werden sofort gesetzt
  - `EmailConfirmed`, sobald eine E-Mail angegeben ist
- **Direktion** (AuS:121-171):
  - Zuerst entsteht die Schule mit `IsActive = true`, dann das Konto.
  - Scheitert die Kontoanlage, wird die Schule wieder gelöscht.
  - Danach folgen die Rollen SL+Lehrer, die UserSchool mit `CanManageStudents = true` und die Willkommensmail.
  - Meldung: « Votre établissement et votre compte de direction ont été créés. Ils attendent la validation par l'exploitant. »
  - Es gibt **keine Transaktion**.
- **Schüler** (AuS:173-230):
  - Rolle Schüler, UserSchool mit `CanManageStudents = false`, StudentProfile mit Eltern-Telefon/-E-Mail, Willkommensmail.
  - Meldung: « Votre compte a été créé. Il attend la validation par l'établissement. »
- Der Query-Parameter `?type=` stellt die Art vor:
  - `director`, `school`, `directeur` → Direktion
  - `student`, `schueler`, `schüler`, `eleve`, `élève` → Schüler
  - Standard ist Direktion (`Register.razor.cs:90-95`).

### 3.7 Nutzungsbedingungen (AGB)
- Zustimmung ist nötig, wenn `AgbVersion ≠ "2026-08-05"` oder `AgbAcceptedAtUtc` leer ist (AuS:30-40). Die Annahme speichert die Version und den Zeitpunkt (AuS:42-57).
- Betroffen sind alle Konten, die nicht selbst registriert wurden: von der Direktion oder im /admin angelegt, importiert, das Seed-Konto. Sie sehen nach dem ersten Login das Modal « Validez les règles de votre espace ».
  - Das Modal steht im Layout (`TermsAcceptanceGuard`, `MainLayout.razor:56`).
  - Buttons: « J'accepte et je continue » oder « Se déconnecter ».
- Die Textsektion richtet sich nach der Hauptrolle. Reihenfolge der Prüfung: PO > Koordinator > SL > Lehrer > Eltern > sonst Élève (`TermsAcceptanceGuard.razor.cs:85-93`).
  - Für SuperAdmin gibt es **keinen Zweig**. Er sieht « Élève ».
- Durchgesetzt wird das **nur in der Oberfläche**. Endpunkte wie PDF-Downloads funktionieren auch ohne Zustimmung.
- Eine Versionsänderung erzwingt die erneute Zustimmung aller Konten. Den Versionsstand gibt es nur als Code-Konstante.

### 3.8 Passwort vergessen, zurücksetzen, ändern, neu setzen
- **Vergessen** (AuS:313-379):
  - Eingabe ist E-Mail **oder** Identifiant. Antwort ist immer neutral: « Si un compte correspond, un e-mail de réinitialisation a été envoyé à l'adresse enregistrée. »
  - Mit `@`: alle Konten mit dieser `NormalizedEmail`, sortiert nach UserName, **höchstens 10**.
  - Ohne `@`: das Konto mit diesem UserName, falls es eine E-Mail hat.
  - Es geht **eine** Mail an die Adresse. Sie enthält je Konto das Identifiant (fett) und einen Button « Choisir un nouveau mot de passe » mit Link `{App:BaseUrl}/auth/reset-password?id={KontoId}&token={urlencodiert}` (T/IdentifiantTests.cs:104, T/WillkommensMailTests.cs:285).
  - Die Anrede nimmt den Vornamen des ersten Kontos.
  - Versandfehler werden nur protokolliert.
- **Zurücksetzen** (AuS:382-399):
  - Konto per `id` suchen, dann Identity-Reset mit Token (Standard-Tokenlebensdauer, nicht konfiguriert).
  - Danach neue Session und Bestätigungsmail.
  - Meldung: « Votre mot de passe a été réinitialisé. Connectez-vous avec l'identifiant « {UserName} ». »
  - Bei ungültiger ID: « Le lien de réinitialisation est invalide ou a expiré. »
  - Fehlt `id` oder `token` in der URL: « Le lien de réinitialisation est invalide ou incomplet. » (`ResetPassword.razor.cs:38`)
- **Ändern im Profil** (AuS:405-422):
  - Das aktuelle Passwort ist Pflicht.
  - Danach neue Session, Bestätigungsmail, Abmeldung und Weiterleitung auf `/auth/login?info=password-updated`. Dort steht « Votre mot de passe a été modifié. Veuillez vous reconnecter. » (`Profile.razor.cs:244-248`, `Login.razor.cs:38`).
- **Bestätigungsmail** (AuS:424-453):
  - nur an eine plausible Adresse
  - Betreff « Mot de passe réinitialisé » bzw. « Mot de passe modifié »
  - Links auf `/auth/login` und `/auth/forgot-password`
  - nie Passwort oder Token
  - ein Mailfehler macht die Änderung nicht rückgängig (T/WillkommensMailTests.cs:182-230)
- **Neues Startpasswort durch die Direktion** (KZS:14-31). Gedacht für Konten ohne E-Mail.
  - Nicht für das eigene Konto: « Changez votre propre mot de passe depuis votre profil. »
  - Das Zielkonto muss von der Direktion verwaltbar sein (`GetSchoolUserForEditAsync`), sonst: « Ce compte n'appartient pas à votre établissement ou ne peut pas être géré ici. »
  - Ablauf: Passwort erzeugen (3.2), Reset per Token, neue Session.
  - Rückgabe sind Identifiant und Passwort zur einmaligen Anzeige (T/KontoZugangServiceTests.cs:14).

### 3.9 Profil (/profil)
- **Nur lesend:** Identifiant, Kontaktadresse (« Aucune », wenn leer), Rollen, Schule(n), aktuelle Klasse (Schüler), Eltern-Telefon/-E-Mail. Die eigene E-Mail kann der Nutzer nicht ändern.
- **Bearbeitbar** (AuS:626-705): Prénom, Nom (Pflicht), Téléphone (≤50), Genre, Profilfoto.
- **Profilfoto:**
  - höchstens 25 Mo, vom Client begrenzt (`Profile.razor.cs:176`)
  - wird als Medienobjekt der ersten Schule des Nutzers gespeichert
  - das Asset muss dem Nutzer gehören, ein Bild sein und zu seiner Schule passen; ohne Schule darf es keine Schule haben. Sonst: « Cette photo de profil ne vous appartient pas ou ne correspond pas à votre école. »
  - « Retirer » setzt nur die Referenz auf null. Das alte Asset bleibt liegen.
- **Lehrer:** « Matières enseignées » ist eine Mehrfachauswahl der Fächer seiner Schule(n). Fremde Fach-IDs werden verworfen.
  - Ein neues Fach kann per Name angelegt werden (Vergleich ohne Groß-/Kleinschreibung).
  - Es wird in der **ersten** Schule angelegt und sofort zugeordnet (AuS:577-624).
- **Farbthema** (Marine, Océan, Corail, Ardoise, Sauge) liegt nur im Browser (`SKOULOUAPPTheme`).

### 3.10 Konten im /admin
- Nur **Neuanlage**, Bearbeiten wird abgelehnt (DS:103-126).
  - Ablehnungsmeldung bei Bearbeiten oder unerlaubter Rolle: « Ce rôle ne peut pas être créé ici. »
  - Erlaubte Rollen: SA → SuperAdmin, ProduktOwner, SchulLeiter; PO → nur SchulLeiter.
- Schulen:
  - SL und PO brauchen mindestens 1 Schule, SA genau 0: « Veuillez attribuer au moins une école au directeur ou au responsable produit. »
  - Nur erlaubte Schulen (SA alle, PO seine): « Vous ne pouvez attribuer que vos écoles. »
- E-Mail ist Pflicht für SL, PO und SA: « Une adresse e-mail est nécessaire pour ce rôle (factures, messages de la plateforme). » Für Koordinator, Lehrer und Schüler ist sie optional (T/IdentifiantTests.cs:70).
- Der Service erzwingt `IsBlocked = false` und `IsSelfRegistered = false`. Die Checkboxen « Utilisateur bloqué » und « Auto-inscription » im Formular wirken nicht (DS:121-122).
- Die Ausführung läuft atomar über `BenutzerArbeitsvorgang` (3.20).
- Nach Erfolg: Willkommensmail (3.13) und Dialog « Identifiants initiaux ». Der Dialog erscheint **immer**, auch wenn der Admin selbst ein Passwort eingegeben hat (AC:806-813).
- **Sperren / Freischalten** (DS:128-153, 3883-3931):
  - Nie das eigene Konto; nur SA oder PO.
  - Sperren darf nur der SA.
  - Ein PO darf nur Konten seiner Schulen freischalten, nicht SA/PO-Konten: « Cet utilisateur n'appartient pas à vos écoles. » bzw. « Ce compte ne peut pas être bloqué ici. »
  - Wird ein **selbstregistriertes** Konto freigeschaltet, müssen alle nicht archivierten StudentProfiles existieren und eine schuleigene Klasse haben (kein System-Niveau). Dann wird `AufnahmeBestaetigtAmUtc` gesetzt und die Zyklusteilnahme erfasst (abrechnungsrelevant).
  - Jeder Fehlschlag meldet « Un élève ayant un abonnement payé ne peut pas être bloqué ici. » Diese Meldung ist irreführend, siehe 7.
- Bei Freischaltung einer Selbstregistrierung geht die Mail « Votre compte SKOULOU est activé » raus (KBS:23-30). Beim Sperren gibt es keine Mail.
- **Löschen:** im /admin nur der Development-Hard-Delete eines Directeurs mit allen Schuldaten (DS:1822-2041; in Production verweigert, T/DashboardServiceTests.cs:815/882). Offene Schüler-Selbstregistrierungen löscht die Direktion (Bereich Direktion), mit Ablehnungsmail (KBS:32-36).

### 3.11 Schulen
- Anlegen und Bearbeiten darf nur ein nicht gesperrter SA (DS:1411-1468): « Seul l'administrateur peut modifier l'école et son tarif. »
- Pflichtfelder: Name (≤200), Ville (≤120), Pays (≤120).
- Preis:
  - Formular erlaubt 0–10.000.000.
  - Der Service verlangt **> 0 und höchstens 2 Dezimalen**: « Le tarif doit être positif et comporter au maximum deux décimales. »
  - Ein leerer Preis bedeutet globaler Preis. Ein neuer Tarif ändert bereits gestartete Zyklen nie (Hinweistext A:75-136).
- Der Name ist eindeutig: « Une école avec ce nom existe déjà. »
- Eine neue Schule hat keinen Directeur. Er entsteht erst durch ein Directeur-Konto mit dieser Schule.
- **Aktiv/Inaktiv** über die Checkbox « L'école est active ». Die Wirkung steht in 3.4. Die Direktion kann `IsActive` ihrer eigenen Schule ebenfalls setzen (DS:1567).
- Schulen werden nie gelöscht, außer durch den Development-Hard-Delete.
- **Schulden** in der Liste = Summe `Abrechnungsperioden.Umsatz` mit Status ≠ Bestaetigt (DS:1335).
- **Angezeigter Preis** = Override ?? globaler Preis (DS:1337).

### 3.12 Responsables (ProduktOwner) und ihre Schulen
- Der SA ordnet Schulen **nur bei der Neuanlage** zu, über die Mehrfachauswahl « Établissements suivis ».
  - Gespeichert wird `UserSchool` mit `IstPlattformZuordnung = true` und `CanManageStudents = false` (DS:3038-3044, T/DashboardServiceTests.cs:87/238).
  - Eine spätere Änderung ist im UI nicht möglich.
- Ein PO legt nur Directeurs seiner Schulen an. Er sieht dann:
  - im /admin nur seine Schulen und Konten
  - in der Messagerie nur Mails, die einer seiner Schulen zugeordnet sind
  - Finanzen nur lesend; Abrechnungen und Provisionen seiner Schulen
  - Medien seiner Schulen; löschen darf er nur eigene
- **ResponsableKontakt** (`WebApp/Services/ResponsableKontaktService.cs:24-41`):
  - Ein nicht gesperrter SL oder Koordinator sieht auf /contact den PO seiner Schule. Bei mehreren gilt der alphabetisch erste nach FullName.
  - Angezeigt werden Name (Ersatztext « Votre responsable »), E-Mail und Telefon.
  - `tel:`-Link: nur Ziffern und `+`.
  - **WhatsApp-Link** `https://wa.me/<Ziffern>` nur, wenn die Nummer mit `+` oder `00` beginnt und mindestens 8 Ziffern hat. Führende Nullen werden entfernt (Zeile 16-18; T/ResponsableKontaktServiceTests.cs:13).

### 3.13 Ausgehende E-Mails
- **Versandkette:** `IEmailService` = `SkoulouEmailService` → `PostmarkEmailService` (P:187-188).
  - `SkoulouEmailService` legt das Layout an, falls es fehlt (`ET.EnsureLayout`). Dabei wird der Betreff als Überschrift genommen und Klartext erzeugt.
  - Es setzt **« [SKOULOU] »** vor den Betreff, aber nicht doppelt, auch nicht bei « Re: [SKOULOU] … » (`SkoulouEmailService.cs:10-18`).
- **Postmark** (PES):
  - Absender: `"{FromName} <{FromEmail}>"`. Ohne `MessageStream` gilt `outbound`.
  - Nicht konfiguriert (`Enabled=false`, kein Token, kein FromEmail): kein Versand, Fehler wird zurückgegeben und protokolliert (PES:195-217).
  - **Kein Open- und Link-Tracking** (`TrackOpens=false`, `TrackLinks="None"`), wegen Spamfiltern (PES:159-163).
  - Fehlt der Textteil, wird er aus HTML erzeugt (PES:152-156).
  - Batch über `email/batch` mit höchstens 500 Empfängern pro Aufruf. Doppelte Adressen werden ohne Groß-/Kleinschreibung entfernt. ErrorCode ≠ 0 zählt als Fehler (PES:20, 53-140).
- **Layout** (ET:45-142):
  - tabellenbasiert, nur Inline-Styles, Marker `<!-- SKOULOUAPP-EMAIL-LAYOUT -->`
  - Header Navy→Blau mit Logo-Badge und « SKOULOU »
  - Fußzeile mit « APPRENDRE. PROGRESSER. RÉUSSIR. », Kontaktblock (Website, `<Kontaktadresse>`, « Lomé, Togo »), © Jahr
  - Hinweis « Cet e-mail a été envoyé automatiquement… »
  - Optionaler Preheader; dazu Hilfen `Button(url,label)`, `InfoBox(html)`, `ToPlainText(html)`
  - Farben (Test-fixiert): #0b1b3a, #38bdf8, #f7faff
- **Alle automatischen Mails.** Sie sind best-effort: Fehler werden nur geloggt und brechen den Vorgang nie ab. Gesendet wird nur an eine plausible Adresse.

| Auslöser | Bedingung | Betreff (ohne Präfix) | Fundstelle |
|---|---|---|---|
| Selbstregistrierung; Kontoanlage im /admin, in /schulleiter und für Eltern | Konto hat E-Mail | rollenabhängig, siehe unten | AuS:720-754 |
| Freischaltung einer Selbstregistrierung | vorher selbstregistriert **und** gesperrt, jetzt frei, hat E-Mail | « Votre compte SKOULOU est activé » | KBS:23-30 |
| Ablehnung einer offenen Selbstregistrierung (Direktion) | vorher selbstregistriert und gesperrt | « Votre demande d'inscription à SKOULOU » | KBS:32-36 |
| E-Mail-Adresse eines Kontos geändert (Direktion) | Adresse wirklich geändert | « Adresse e-mail de votre compte SKOULOU modifiée », an **alte und neue** Adresse | KBS:38-47 |
| Schulgebühr bezahlt (Finanzen) | Zahlung hat `FinanzBuchungID`, Betrag > 0 | « Paiement scolaire enregistré – SKOULOU », Betrag im Format `N0` + « FCFA », Datum `dd/MM/yyyy` | KBS:49-60 |
| Abo erstmals bezahlt | vorher unbezahlt, Buchung mit SystemCode `SkoulouAppAboZahlung` existiert | « Abonnement SKOULOU enregistré comme payé » | KBS:69-84 |
| Passwort vergessen / geändert / zurückgesetzt | siehe 3.8 | « SKOULOU – Réinitialisation du mot de passe » / « … Mot de passe modifié » / « … réinitialisé » | AuS:313-453 |
| Kontaktformular | immer | « [Contact] {Sujet} » an `SupportEmail` (sonst `FromEmail`), ReplyTo = Absender | AuS:957-1014 |

- **Willkommensmail** (AuS:760-915):
  - Die Rolle wird nach Rang gewählt: SA > SL > Lehrer > Schüler > sonst generisch.
  - Vorangestellt ist « Votre identifiant de connexion : **…** ». Das Passwort steht nie darin.
  - Button « Accéder à SKOULOU » auf `{BaseUrl}/auth/login`.
  - Texte nach Rolle:
    - SA: « Votre accès administrateur SKOULOU est prêt »
    - SL: « Votre école SKOULOU est enregistrée », mit Schulname, Hinweis auf Validierung durch « l'exploitant » und der Liste « Tout votre établissement, dans un seul espace »
    - Lehrer: « Votre compte enseignant SKOULOU est prêt », mit SKOULOUAPPABO-Hinweis
    - Schüler: « Bienvenue chez SKOULOU », mit « Votre espace scolaire, toujours à portée de main »
    - sonst, also PO, Koordinator und Eltern: generisch « Merci pour votre inscription … activé après validation ». Für diese Rollen ist der Text fachlich falsch.

### 3.14 Eingehende Mails und Weiterleitung an eNOTE (P:332-355, MW)
- Postmark Inbound ruft `POST /api/postmark/inbound` auf.
  - **Basic-Auth** `skoulou:<Postmark:InboundWebhookSecret>`, Vergleich in konstanter Zeit.
  - Kein Secret konfiguriert oder Auth falsch → 401. Antiforgery ist abgeschaltet.
- Pflicht sind `MessageID` und `From`. `To` muss die gemeinsame `<Kontaktadresse>` enthalten, sonst 400.
- **Eigene Absender** (`Postmark:FromEmail` oder `<Kontaktadresse>`) werden mit 200 verworfen. So kommt die Kopie des Kontaktformulars nicht doppelt zurück (MW:28-34).
- **eNOTE-Weiterleitung** (MW:18-21, 37-70):
  - Bedingung: Der Betreff enthält « enote » (ohne Groß-/Kleinschreibung) und **nicht** « [SKOULOU] ».
  - Das unveränderte Postmark-JSON geht per POST an `Postmark:WeiterleitungEnoteUrl` mit Basic-Auth `enote:<Postmark:WeiterleitungEnoteGeheimnis>`. Timeout 15 s (P:192).
  - Nur eine 2xx-Antwort gilt als übernommen. Fehlt die Konfiguration, kommt eine 401 oder ein Netzfehler, bleibt die Mail im SKOULOU-Posteingang. Es geht nichts verloren.
- **Speichern** (MBS:202-215):
  - Deduplizierung über `PostmarkMessageID`
  - Inhalt = `HtmlBody`, sonst HTML-kodierter `TextBody`, bereinigt
  - Kürzung auf 256/256/300/100 Zeichen
  - `SchulID` immer **null**; der Parameter wird überschrieben

### 3.15 Messagerie (MBS)
- **Sichtbarkeit** (MBS:97-118):
  - SA: alle Mails, optional gefiltert nach einem PO (Mails mit SchulID in dessen Schulen).
  - PO: nur Mails mit SchulID einer seiner Schulen. Eingehende Mails sieht er also erst, wenn der SA sie mit « Attribuer à une école » zugeordnet hat (MBS:217-226; T/MailboxServiceTests.cs:14).
  - Alle anderen: kein Zugriff (`null`).
- **Liste:**
  - Eingang und Gesendet getrennt, neueste zuerst
  - Suche in Betreff, Absender, Empfänger und Inhalt
  - 50 pro Seite, höchstens 200 pro Abruf
  - der Inhalt wird erst beim Öffnen geladen (MBS:64-94)
- **Einzelversand** (MBS:157-200):
  - Empfänger muss eine gültige Adresse sein; Betreff 1–300 Zeichen; Text nicht leer. Sonst: « Vérifiez l'adresse, l'objet et le message. »
  - PO: Eine Schule ist Pflicht, sie muss ihm gehören, **und der Empfänger muss ein Konto dieser Schule sein**. Meldungen: « Choisissez une école pour envoyer ce message. », « Cette école ne vous est pas attribuée. », « Le destinataire ne fait pas partie de cette école. »
  - SA: freie Adresse, Schule optional.
  - Gesendet wird mit Layout. Nur bei Erfolg entsteht ein Ausgangsdatensatz mit Absender `<Kontaktadresse>`, Meldung « Message envoyé. »
- **Empfängerliste je Schule** (MBS:137-155): nicht gesperrte Konten mit E-Mail und Rolle SL, Ko, Le oder Sch. Flags `IstDirektion` (SL oder Ko), `IstLehrer`, `IstSchueler`.
- « Répondre » setzt `Re: ` davor, aber nicht doppelt (`Mailbox.razor.cs:199-207`).

### 3.16 Gruppen-E-Mail (« Envoi ciblé ou groupé », nur SA)
- **Empfänger** (BES:57-109):
  - nur nicht gesperrte Konten mit E-Mail
  - Zielgruppe nach Rolle: « Directeurs » = **nur SchulLeiter**; « Tout le monde » = SL + Lehrer + Schüler, **ohne Koordinator, PO und Eltern**
  - optional eine Schule (über UserSchools)
  - zusätzliche Adressen werden immer angehängt; Trennzeichen sind `, ; Leerzeichen Tab Zeilenumbruch`, implausible Adressen fallen weg
  - « Adresses saisies uniquement » fragt die DB nicht ab
  - doppelte Adressen werden ohne Groß-/Kleinschreibung entfernt
- **Versand** (MBS:17-50):
  - Prüfungen: nur SA (« Accès refusé. »), Schule muss existieren (« École introuvable. »), Empfänger vorhanden (« Aucun destinataire correspondant n'a été trouvé. »)
  - Text mindestens 5 Zeichen Klartext: « Le message est trop court. »
  - Es wird **einzeln** pro Empfänger gesendet. Jede erfolgreiche Mail wird als Ausgang gespeichert.
  - Ergebnis: « N e-mail(s) envoyé(s). M échec(s). »

### 3.17 Annonces (`Core/Services/AnkuendigungService.cs`)
- **Anlegen** (Zeile 42-54):
  - Titel 1–200 Zeichen nach Trim
  - Klartext des bereinigten HTML mindestens 5 Zeichen
  - Ablauf muss in der Zukunft liegen
  - Fehlermeldung: « Indiquez un titre, un texte d'au moins cinq caractères et une date d'expiration future. »
  - Erfolg: « Annonce publiée. »
  - Die Ablaufzeit kommt als « Expiration (heure locale) » und wird mit der **Serverzeitzone** nach UTC umgerechnet (`AdminBroadcast.razor.cs:141-143`). Standard ist jetzt + 7 Tage.
- **Aktiv** ist eine Annonce, solange `AblaufAmUtc > jetzt` (Zeile 10-14).
  - Angezeigt wird sie **allen Besuchern, auch anonymen**, oben in jedem Layout. Unter dem Text steht « Visible jusqu'au … ».
  - Jeder offene Circuit fragt alle **30 s** neu ab (`Shared/AnkuendigungAnzeige.razor:35-46`).
- **Löschen** ist jederzeit ohne Rückfrage möglich (« Annonce supprimée. »). Ein zweites Löschen liefert false.
- **Verwaltungsliste:** Titelsuche, 50 pro Seite, ohne Inhalt.

### 3.18 Kontaktformular (/contact, öffentlich)
- **Felder:** Votre nom, Votre e-mail (Format), Sujet, Message (RichText ohne Bilder). Alle sind Pflicht; die Nachricht braucht mindestens 10 Zeichen.
  - Die Seite prüft die Rohlänge (HTML), der Service den Klartext ≥ 10: « Le message doit contenir au moins 10 caractères. »
- **Ablauf** (`Contact.razor.cs:61-72`):
  1. Die Nachricht wird **zuerst** als eingehende Mail gespeichert. Betreff « [Contact] … », Inhalt mit Name, `SchulID` null.
  2. Danach geht die Mail an den Support (3.13).
  3. Ergebnis: « Merci ! Votre message a bien été envoyé. » bzw. « L'envoi a échoué. Merci de réessayer plus tard. »
- Angemeldete Nutzer bekommen ihre E-Mail aus dem Claim vorbefüllt.
- Hinweis auf der Seite: « ce message est envoyé à l'équipe de support de SKOULOU, et non à votre école ».

### 3.19 Medien und Blob-Speicher
- **Containerzugriff** (`WebApp/Services/SkoulouBlobSpeicher.cs:14-41`):
  - Bevorzugt `ServiceUri` mit **System-Managed-Identity**. Sie muss eine reine `https://<konto>.blob.core.windows.net/` sein: kein SAS, kein Pfad, kein Port, kein Userinfo.
  - Sonst gilt `BlobConnectionString`.
  - Konfiguriert ist der Speicher, wenn eins von beiden gesetzt ist.
- **`Enabled`:**
  - In Development **immer false**, dann gilt der lokale Ordner `LocalDataRootPath`.
  - Sonst immer true; die Konfiguration wird ignoriert (P:172-179).
  - Ohne Blob-Konfiguration in Production gibt es eine klare Exception und keinen lokalen Rückfall (MS:669-675).
- **Medienbibliothek** (Container `ContainerName`):
  - Blobname `SkoulouAppStorage/schools/{schoolId:N}/[classes/{klasseId:N}/]{images|videos}/{yyyy/MM}/{name}-{guid:N}{ext}`; Profilbild ohne Schule unter `SkoulouAppStorage/profiles/{userId:N}/…` (MS:154-155, 661-667)
  - Bildtypen: PNG, JPEG, GIF, WebP
  - Videotypen: MP4, MOV, M4V; Dauer > 0 und ≤ 1 h; Größe ≤ 8 GB
  - Upload in 4-MB-Blöcken
  - Pflicht-Alt-Text ≤ 250; Name ≤ 200 (Details im Bereich Unterricht)
- **Sichtbarkeit** (MS:577-612):
  - SA: alle Assets mit Schule
  - PO: Assets seiner Schulen
  - SL, Ko, Le: Assets der eigenen **freigegebenen** Schule
  - Schüler und Eltern: kein direkter Zugriff, nur über veröffentlichte Inhalte
- **Löschen** (MS:407-465):
  - nur Bilder
  - SA, SL und Ko dürfen alle sichtbaren löschen; Lehrer und PO nur eigene
  - gesperrt, wenn das Bild als Profilbild oder in TeacherLesson-Seiten benutzt wird
- **Finanzbelege** (Container `FinanzBelegContainerName`, `WebApp/Services/AzureFinanzBelegStorage.cs`):
  - Blobname `schools/{schulId:N}/receipts/{belegId:N}`
  - Der lokale Rückfall liegt unter `<LocalDataRootPath>/finance-private/<Schule>/<Beleg>`, ersatzweise `ContentRoot/App_Data/media-library`. Er darf **nicht** unter wwwroot liegen, und der Pfad wird streng geprüft (Zeile 18, 69-85).
- **Schullogo:** nur SL oder Ko der eigenen, freigegebenen und aktiven Schule. Höchstens 2 Mo, echtes PNG/JPEG mit Dimensionsprüfung (`SchulLogoService.cs:22-28`, `SchulLogoPruefung.cs:7`).
- **Alter lokaler Medienspeicher** (`MediaStorage:*`): nur in Development aktiv. In Production wirft er « Utilisez la bibliothèque de médias… » (`ProductionMediaStorageService.cs:8`).

### 3.20 Transaktionen und Doppelklickschutz
- **`DatabaseActionGuard`** (scoped pro Circuit):
  - `RunAsync` führt eine Aktion nur aus, wenn gerade keine läuft (`Interlocked`), sonst passiert nichts.
  - Rückgabe true/false bzw. `default` (`WebApp/Services/DatabaseActionGuard.cs:12-45`).
  - Benutzt auf allen Formularseiten dieses Bereichs. Es gibt keine Rückmeldung an den Nutzer, wenn ein Klick verworfen wird.
- **`BenutzerArbeitsvorgang`** (`WebApp/Services/BenutzerArbeitsvorgang.cs:16-54`) für Kontoanlage und Import:
  - eigener DbContext, UserManager und DashboardService pro Versuch
  - Serializable-Transaktion
  - globaler App-Lock `sp_getapplock 'SKOULOUAPP:Zyklusverwaltung'` mit 30 s Timeout (`Core/Services/ZyklusTransaktionsSchutz.cs:13-21`)
  - bei Erfolg wird ein `BenutzerVorgangsBestaetigung` mit fester Vorgangs-ID geschrieben
  - Geht nach dem Commit die Antwort verloren, prüft der Retry diesen Beleg und gibt das gemerkte Ergebnis zurück, samt einmaligen Passwörtern (T/BenutzerArbeitsvorgangSqlTests.cs:25)
- **EF-Resilienz:** 6 Wiederholungen, höchstens 30 s Abstand, Command-Timeout 60 s (P:78-84).

### 3.21 Fehlerjournal
- `DatabaseExceptionLogSink` (Serilog) speichert **nur Ereignisse ≥ Error mit Exception** (`WebApp/Services/DatabaseExceptionLogSink.cs:21-65`):
  - Nachricht, Typ, kompletter `ToString()`, Request-Pfad, UserId und **erste** Schule des Nutzers
  - Rekursionsschutz über AsyncLocal
  - Fehler beim Schreiben werden verschluckt
  - synchrones `SaveChanges`
- Blazor-Renderfehler meldet `ProtokollierendeFehlergrenze` als Error. Die Seite zeigt dann « Une erreur inattendue s'est produite. » (`Routes.razor`).
- Clientabbrüche werden zu **499** statt 500 und landen nicht im Journal (`RequestAbbruchMiddleware.cs:8-21`).
- **Suche** (`ExceptionLogSuche.cs:15-55`):
  - Von/Bis jeweils inklusive Tag
  - Schule; « nur offene »
  - Text in Message, ExceptionType oder RequestPath
  - neueste zuerst, 50 pro Seite

### 3.22 Kultur
- Feste Kultur für alle Threads und Requests (P:25-30, 295-301): `fr-FR` mit **Punkt als Tausendertrennzeichen** und Komma als Dezimalzeichen (`Core/Services/SkoulouKultur.cs:15-26`).
  - Beispiele: « 150.000 FCFA », « 13,67 », « 12/11/2026 », « jeudi 12 novembre 2026 » (T/SkoulouKulturTests.cs).
- Der Browser oder die Serversprache hat keinen Einfluss, weil die Culture-Provider geleert sind.

---

## 4. Abläufe

**A1 – Schule registrieren** (Direktion, öffentlich)
1. Die Direktion klickt auf / « Inscrire mon établissement ». Auf /auth/register wählt sie « Direction » und füllt die Felder aus. « Proposer » schlägt ein Identifiant vor.
2. Die Vorprüfung läuft (3.6). Danach öffnet sich das AGB-Modal mit den Sektionen Élève, Direction & coordination, Enseignant, Responsable produit und Règles communes.
3. Mit « J'accepte et je poursuis » werden Schule und gesperrtes Konto angelegt (SL+Lehrer). Die Willkommensmail SL geht raus.
4. Die Schule gilt als gesperrt, solange der Directeur gesperrt ist (3.4). Schüler können sie trotzdem schon in der Registrierung wählen, weil nur `IsActive` geprüft wird.
5. Der SA soll das Konto im /admin mit « Activer » freischalten. **Das scheitert im alten Code**, siehe 7, Nr. 2. Danach wären Login und die Mail « Votre compte SKOULOU est activé » möglich.

**A2 – Schüler registriert sich**
1. Auf /auth/register wählt der Schüler « Élève » und eine aktive Schule. E-Mail und Elternkontakt sind optional. Nach den AGB entsteht das gesperrte Konto mit StudentProfile ohne Klasse und der Willkommensmail Schüler.
2. Die Direktion sieht die offene Anmeldung (Bereich Direktion). Sie hat zwei Möglichkeiten:
   - Klasse zuweisen und freischalten. Dabei werden `AufnahmeBestaetigtAmUtc` und die Zyklusteilnahme gesetzt, und die Aktivierungsmail geht raus.
   - Ablehnen. Das Konto, das Profil und die UserSchool werden gelöscht, und die Ablehnungsmail geht raus.

**A3 – Responsable oder Directeur anlegen** (/admin)
1. Der SA klickt « Nouvel utilisateur » und wählt die Rolle: Directeur, Responsable produit oder Administrateur. Ein PO kann nur Directeur wählen.
2. Er gibt Name, Identifiant (oder « Proposer »), E-Mail (Pflicht) und Schule(n) an. Das Passwort ist optional.
3. Beim Speichern entsteht das Konto atomar und sofort aktiv. Die Willkommensmail geht raus, und der Dialog « Identifiants initiaux » zeigt die Zugangsdaten zur vertraulichen Weitergabe (« Identifiants transmis — fermer »).

**A4 – Anmelden**
1. Der Nutzer gibt auf /auth/login Identifiant und Passwort ein, optional « Rester connecté ». Das Formular wird per Antiforgery als POST an `/auth/login/submit` gesendet.
2. Bei Erfolg wird die neue Session gesetzt, und eine Sitzung auf einem anderen Gerät endet spätestens bei deren nächster Anfrage bzw. nach 30 s. Danach folgt die Weiterleitung (3.3).
3. Fehlen die AGB, erscheint das Modal.

**A5 – Passwort vergessen**
1. Der Nutzer gibt auf /auth/forgot-password E-Mail oder Identifiant ein und bekommt immer die neutrale Meldung.
2. Er erhält eine Mail mit je einem Link pro Konto.
3. Auf /auth/reset-password setzt er das neue Passwort, Bestätigung inklusive. Danach kommen eine Bestätigungsmail und die Meldung mit Identifiant. Andere Sitzungen enden.

**A6 – Konto ohne E-Mail hat das Passwort vergessen**
1. Die Direktion klickt in /schulleiter bzw. /direction/parents auf « Nouveau mot de passe ».
2. Das neue Passwort erscheint einmalig. Die Direktion gibt es persönlich weiter.

**A7 – Eingehende Mail**
1. Eine externe Mail an `<Kontaktadresse>` wird in die IONOS-Weiterleitung kopiert und kommt dann über Postmark Inbound an den Webhook.
2. Der Webhook verwirft sie bei eigenem Absender, leitet sie bei eNOTE-Betreff an eNOTE weiter und speichert sie sonst als Eingang ohne Schule.
3. Der SA ordnet sie einer Schule zu. Erst dann sieht sie der PO dieser Schule.
4. « Répondre » schickt die Antwort mit Präfix [SKOULOU]. Eine Antwort darauf bleibt wegen des Präfixes bei SKOULOU.

**A8 – Annonce veröffentlichen**
1. Der SA legt sie unter /admin/communication › « Annonces » an: Titre, Message, Expiration.
2. Sie erscheint sofort und bei allen offenen Seiten innerhalb von 30 s, bis zum Ablauf oder zu « Supprimer ».

**A9 – Schule deaktivieren**
1. Der SA entfernt im /admin › « Modifier » den Haken « L'école est active ».
2. Alle Schulkonten werden bei der nächsten Anfrage bzw. nach 30 s abgemeldet und können sich nicht mehr anmelden. SA und PO sind nicht betroffen.

---

## 5. Seiten, Routen, Endpunkte, Jobs, Konfiguration

### 5.1 Seiten

| Route | Zugriff | Inhalt |
|---|---|---|
| `/` | öffentlich | Siehe Startseite unten. |
| `/auth/login` | öffentlich | Linke Werbespalte, Formular Identifiant/Mot de passe, « Rester connecté », Link « Mot de passe oublié ? » und « Inscrire mon établissement ou m'inscrire comme élève ». Fehlerparameter: `?error=account-blocked` « Votre compte n'est pas encore validé ou a été bloqué. Contactez votre établissement… », `?error=invalid-credentials` « Identifiant ou mot de passe incorrect. ». Info: `?info=password-updated`. |
| `/auth/register` | öffentlich | Kacheln Direction/Élève. Felder siehe 3.6. AGB-Modal mit Sektionsnavigation, Buttons « Annuler l'inscription » / « J'accepte et je poursuis ». Hinweis « Chaque compte appartient à un seul établissement… ». |
| `/auth/forgot-password` | öffentlich | Feld « Adresse e-mail ou identifiant ». Hinweis « Sans adresse e-mail, demandez à la direction de réinitialiser votre mot de passe. » |
| `/auth/reset-password?id&token` | öffentlich | Neues Passwort + Bestätigung, danach Link « Aller à la connexion ». |
| `/profil` | angemeldet | Siehe 3.9. Bereiche « Identité », « Thème de couleur », Formular, Infoblock (Rollen, « Établissement(s) suivis »), Bereich « Changer le mot de passe » (Anker `#change-password`). `?returnUrl=` nur lokal und nicht `/profil` selbst. |
| `/admin` | SA, PO | Siehe 5.2. |
| `/admin/communication` | SA | Reiter « Annonces » (AdminBroadcast ohne Mail-Editor) und « E-mails » (Messagerie, lädt erst beim ersten Öffnen). |
| `/admin/broadcast` | SA | Eigenständig: Annonces + « Rédiger un e-mail ». Zielgruppe, Schule (« Toutes les écoles »), Zusatzadressen, Sujet, Message. Wird auch im Mail-Dialog eingebettet. |
| `/admin/courrier` | SA, PO | « Messagerie »: Ordner « Réception »/« Envoyés », Suche, « Afficher plus », pro Mail « Afficher le message », « Répondre », für SA « Attribuer à une école ». SA zusätzlich mit PO-Filter. Dialog « Nouvel e-mail » mit Modus « Message individuel » / « Envoi ciblé ou groupé » (nur SA). Pflichtschule für PO. Empfänger-Filter nach Rolle mit Suche. Im Menü nur für PO; der SA erreicht die Seite über /admin/communication. |
| `/admin/exception-logs` | SA | « Journal des exceptions »: Filter Du/Au, Schule, nur offene, Suche, « Réinitialiser ». Karten mit Status « À traiter »/« Traitée ». Buttons « Copier » (Zwischenablage), « Traiter » (setzt IsHandled/HandledAt/HandledBy), « Supprimer » (**ohne Rückfrage**). Stack trace ausklappbar. |
| `/contact` | öffentlich | Bei Direktion zuerst « Votre responsable SKOULOU » mit Anrufen/WhatsApp/E-Mail-Buttons, dann das Formular (3.18). |
| `/aide/videos` | angemeldet | Gruppen « Pour bien commencer », Direction, Enseignant, Élève, Parent. Die eigene Gruppe steht nach « Pour tous » zuerst. Karten mit Poster, Dauer, Gesamtdauer; Abspielen im Overlay. Leertext « Aucune vidéo pour votre rôle pour le moment. » |
| `/handbuch` | angemeldet | « Guide utilisateur ». Reiter nach Hauptrolle (Rang SA > PO > Ko > SL > Le > sonst Élève): start « Démarrer », learning « Apprendre », teaching « Pédagogie », school « École », commissions « Commissions », finance « Finances », admin « Administration». Sichtbarkeit siehe 6. |
| `/access-denied` | – | Seite « Accès refusé » existiert, wird aber **nie angesteuert**. Fehlende Rechte führen über `RedirectToLogin` zum Login (`Routes.razor:7`). |
| `/Error` | – | Produktions-Fehlerseite mit Request-ID. |
| `/audio-test` | **öffentlich** | Testseite ohne Autorisierung (Altlast). |

**Startseite `/`** (`Home.razor(.cs)`):
- Angemeldete Schüler werden auf `/student` umgeleitet.
- **Anonyme Besucher** sehen:
  - Hero « Votre établissement, simplement organisé. » mit Buttons « Inscrire mon établissement » / « Se connecter »
  - Zähler « N établissement(s) actif(s) » (nur `IsActive`) und « N élève(s) inscrit(s) » (nicht archiviert, Schule aktiv, Konto nicht gesperrt). Sie werden 10 min statisch im Prozess zwischengespeichert, bei einem Fehler ausgeblendet.
  - Ein festes Demo-Dashboard: « Collège Les Palmiers »
  - Das Werbevideo « SKOULOU en action », falls die Datei existiert
  - Sechs Funktionskacheln (Abschnitt « À propos »), drei Rollenkarten
  - « Votre établissement en ligne en trois étapes » mit Inscription / Validation / Équipe et élèves
  - Kontaktzeile
- **Angemeldete** sehen « Bonjour, bienvenue dans votre espace. » und Kacheln je Rolle:
  - Direction/Ko: Tableau de bord, Direction, Bulletins, Comptabilité
  - Lehrer/Direktion: Notes, Emplois du temps
  - Eltern: Mes enfants
  - SA/PO: Administration, Facturation
  - alle: Guide utilisateur

**Menü** (`Layout/NavMenu.razor`):
- SA: « Administration », « Communication », « Journal des erreurs »
- PO: « Responsable produit », « Messagerie », « Parts de mes écoles », « Facturations de mes écoles »
- alle Angemeldeten: « Vidéos d'aide », « Guide utilisateur », « Contact »
- Benutzermenü: Profil, « Changer le mot de passe », Abmelden (POST `auth/logout`); anonym « Se connecter » / « S'inscrire »
- Fußzeile: Domain, `<Kontaktadresse>`, « Lomé, Togo », « © Jahr SKOULOU, une application TabloSuite »

### 5.2 /admin im Detail (A, AC)
- **Kopf und Kennzahlen:**
  - Titel « Tableau administrateur » bzw. « Espace responsable produit »; SA hat den Link « Participants facturables des cycles ».
  - Kacheln « Écoles », « Enseignants », « Élèves », « En attente / bloqués » (mit « dont X élève(s) »).
  - Schulfilter-Band « Toutes les écoles » filtert **nur** die Nutzerliste.
- **Karte « Vue d'ensemble des écoles »** (alle Karten sind einklappbar und starten zugeklappt):
  - SA: « Nouvelle école » und Filter « Filtrer les écoles par directeur »
  - Liste clientseitig, 12 pro Seite, Suche in Name/Ville/Pays
  - Karte: Name, Ort, « Ecole active/inactive », Enseignants/Eleves, « Dette de l'ecole envers SKOULOU », « Prix de l'abonnement affiche » (« Tarif specifique »/« Prix general »), Directeur, bis zu 4 Lehrer + « +n »
  - SA: « Modifier » öffnet den Schuldialog (3.11)
- **Unterabschnitt « Gestion des utilisateurs »:**
  - Liste mit Server-Paging, 50 pro Abruf, « Afficher plus ».
  - Suche nach Name, E-Mail, Identifiant oder Rolle.
  - Rollen-Chips mit Zählern: Directeurs, Responsables produit, Coordinateurs, Enseignants, Élèves; Zwischenüberschriften je Hauptrolle.
  - Karte: Name, E-Mail, Rolle, « Compte bloqué/actif », « Écoles : … », « Auto-inscription : Oui/Non ».
  - Nur SA, nur bei Schülern: Abo- und Zahlungsbadges, « Score exercices & examens » und das Panel « Abonnements & paiements » mit « Annuler l'abonnement » / « Annuler et retirer de la classe ». Das Panel ist nur bei freigeschaltetem und noch nicht abgerechnetem Abo aktiv (Bereich Abrechnung).
  - Buttons:
    - « Voir les données » (lesend)
    - « Activer » (gesperrte Konten, SA und PO)
    - « Bloquer » (nur SA)
    - « Supprimer définitivement (test) » (nur SA, nur Development, nur Directeur)
- **Karte « Bibliothèque de médias »** (SA alles, PO seine Schulen):
  - Kacheln Total/Filtre/Écoles
  - Filter nach Schule mit Größe und Anzahl
  - Suche, 48 pro Abruf
  - rein lesend, ohne Vorschau, Upload oder Löschen; zeigt den internen Blob-Pfad
- **« Prix général et rémunération interne »** (nur SA):
  - globaler Abopreis (FCFA) und PO-Anteil in FCFA oder %
  - Prozent = round(Betrag / Preis × 100, 2); Betrag = round(Preis × % / 100, 2, AwayFromZero)
  - Plattformanteil = Rest
  - Speichern nur bei Preis > 0 mit höchstens 2 Dezimalen und Summe ≤ 100 %. Jeder dieser Fehler meldet « La somme des commissions ne peut pas dépasser 100 %. »
  - Erfolg: « Les commissions seront appliquées aux nouveaux abonnements. » (`Core/Services/ProvisionsService.cs:25-71`)
- Unsichtbarer toter Block `div.hidden` (A:649-981, `KlassenVerwaltungSichtbar=false`) mit altem Klassen-, Lektions- und Medien-Upload. **Nicht übernehmen.**

### 5.3 HTTP-Endpunkte dieses Bereichs

| Methode und Pfad | Schutz | Verhalten |
|---|---|---|
| `POST /auth/login/submit` | Antiforgery | 3.3; Redirect mit `error`-Parameter (P:713-756) |
| `POST /auth/logout` | – | Abmelden, Redirect `/` (P:758-762). **Ohne Antiforgery-Prüfung.** |
| `POST /api/postmark/inbound` | Basic-Auth `skoulou:<Geheimnis>`, ohne Antiforgery | 3.14 |
| `GET /medien/videos/{datei}` | Dateiname `^[a-z0-9-]+\.mp4$`; `VideoKatalog.DarfAbrufen` | Werbevideo öffentlich mit `Cache-Control: public, max-age=3600`. Lernvideos nach Rolle mit `private, max-age=3600`. ETag aus Länge und Änderungszeit, Range-fähig. Antworten: 401 anonym, 403 falsche Rolle, 404 unbekannt (P:313-327). Dateien liegen in `ContentRoot/Medien/videos`, Poster in `wwwroot/videos/*.jpg`; Cache-Busting `?v=<Ticks hex>` (`VideoKatalog.cs:97-98`). |
| `GET /media-library/{assetId}` | angemeldet | Bild über `OpenImageAsync`, Range-fähig (P:357-374) |
| `GET /media-library/videos/{assetId}?lessonId=` | angemeldet | Video über `OpenVideoAsync`, Range-fähig. Der berechnete Abo-Status wird ignoriert (P:690-711). |

**Videokatalog** (`WebApp/Services/VideoKatalog.cs:37-64`): 10 Lernvideos plus Werbevideo `skoulou-presentation.mp4`.
- Pour tous (alle Rollen): visite-guidee, pourquoi
- Direction (Leitung = SA, PO, SL, Ko): direction-preparer, direction-equipe, direction-notes, direction-frais
- Enseignant (Leitung + Lehrer): enseignant-cours, enseignant-notes
- Élève (Leitung + Lehrer + Schüler): eleve-apprendre
- Parent (Leitung + Eltern): parent-suivre
- Unbekannte oder abgelöste Dateien werden allen verweigert, auch dem SA.

### 5.4 Pipeline, Start und Hintergrundaktivität
- **Reihenfolge** (P:282-330):
  - nur außerhalb von Development: `UseExceptionHandler("/Error")` und HSTS
  - `UseSerilogRequestLogging`, `RequestAbbruchMiddleware`, HTTPS-Redirect
  - RequestLocalization mit fester Kultur
  - Authentication, Authorization, RateLimiter, Antiforgery, StaticAssets, Endpunkte
- SignalR-Nachrichten bis 8 MB, für Base64-Bilder im Editor (P:51-52).
- **Start** (P:245-279):
  1. Nur in Development: Warnung bei Modelländerungen ohne Migration.
  2. Migrationen laufen nur, wenn `Database:ApplyMigrationsOnStartup` gesetzt ist.
  3. Permissions abgleichen. Rollen anlegen; deren Beschreibung und RolePermissions werden **bei jedem Start neu geschrieben** (Seed:25-87).
  4. Seed-Admin anlegen (Seed:96-136):
     - nur wenn `Seed:SuperAdminEmail` **und** `Seed:SuperAdminPasswort` gesetzt sind
     - Identifiant = `Seed:SuperAdminIdentifiant`, sonst die E-Mail
     - Name = `Seed:SuperAdminName`, sonst « Administrateur SKOULOU »
     - `EmailConfirmed`, nicht gesperrt
     - existiert der Name schon **mit** SuperAdmin-Rolle: keine Änderung, kein Passwort-Reset
     - existiert er **ohne** die Rolle: Startabbruch, keine Rechteerweiterung
  5. Gibt es keinen SuperAdmin, wird gewarnt: « Aucun compte SuperAdmin : définissez Seed__SuperAdminEmail et Seed__SuperAdminPasswort… »
- **Hintergrundjobs:** Es gibt keine `IHostedService`. Periodisch laufen nur:
  - Annonce-Abfrage alle 30 s pro Circuit
  - Auth-Revalidierung alle 30 s pro Circuit
  - Cookie-Prüfung bei jeder Anfrage
- **Rate-Limit** gibt es nur für `/verification-bulletin` (30 pro Minute und IP, P:237-240, Bereich Bulletins). Login, Registrierung, Passwort vergessen und Kontakt sind **ungebremst**.

### 5.5 Konfigurationsschlüssel (alle; Namen beibehalten!)
Umgebungsvariablen nutzen `__` statt `:`, z. B. `Postmark__ServerToken`.

| Schlüssel | Bedeutung | Standard (Code / appsettings / Development) | Fundstelle |
|---|---|---|---|
| `Database:ConnectionStringName` | Name der zu nutzenden Verbindung | Code `DefaultConnection`; appsettings `AzureDefaultConnection`; Dev `DefaultConnection`. Leere Verbindung → Startabbruch | P:65-71 |
| `ConnectionStrings:DefaultConnection` | lokaler SQL Server | nur appsettings.Development (lokale DB `SKOULOUAPPDB`, Windows-Auth) | – |
| `ConnectionStrings:AzureDefaultConnection` | Azure SQL (`<Geheimnis>`) | leer; in Azure als App-Setting/Connection String | P:58-66 |
| `Database:ApplyMigrationsOnStartup` | Migrationen beim Start | `false` | P:33 |
| `Seed:SuperAdminEmail` / `Seed:SuperAdminPasswort` | Start-Admin (beide nötig) | – (`<Geheimnis>`) | Seed:98-103 |
| `Seed:SuperAdminIdentifiant` | Anmeldename des Start-Admins | = E-Mail | Seed:106-110 |
| `Seed:SuperAdminName` | Anzeigename | « Administrateur SKOULOU » | Seed:123-128 |
| `App:BaseUrl` | Basis für Mail-Links und Bulletin-QR | Code `https://localhost:7064` (AuS); appsettings `https://skoulou.tablosuite.com`; Dev `https://localhost:7064` | AuS:1019, `BulletinPdfService.cs:9` |
| `Postmark:Enabled` | Versand an/aus | Klasse `true`; appsettings `false` (in Azure auf true setzen) | `Core/Options/PostmarkOptions.cs:27` |
| `Postmark:ServerToken` | API-Token (`<Geheimnis>`) | leer | PES:191 |
| `Postmark:FromEmail` | Absenderadresse; gilt zugleich als „eigene Adresse“ beim Eingang | appsettings `<Kontaktadresse>` | PES:144-146, P:348 |
| `Postmark:FromName` | Absendername | Klasse « SkoulouApp »; appsettings « SKOULOU » | – |
| `Postmark:SupportEmail` | Ziel des Kontaktformulars | appsettings `<Kontaktadresse>`; leer → FromEmail | AuS:964-968 |
| `Postmark:MessageStream` | Postmark-Stream | `outbound` | PES:158 |
| `Postmark:InboundWebhookSecret` | Basic-Auth-Passwort des Webhooks (Benutzer `skoulou`) | leer → Webhook lehnt alles ab (401) | P:334-338 |
| `Postmark:WeiterleitungEnoteUrl` | eNOTE-Inbound-URL | leer → keine Weiterleitung | MW:39 |
| `Postmark:WeiterleitungEnoteGeheimnis` | Basic-Auth-Passwort an eNOTE (Benutzer `enote`) | leer → keine Weiterleitung | MW:40 |
| `AzureBlobStorage:*` **und** `Storage:*` | gleiche Felder; `Storage` wird zuletzt gebunden und gewinnt. In App Service nur `Storage__…` möglich. | – | P:160-162 |
| `…:ServiceUri` | Speicherkonto-URI, Managed Identity, hat Vorrang | leer | `AzureBlobStorageOptions.cs:17` |
| `…:BlobConnectionString` | Alternative mit Schlüssel (`<Geheimnis>`). Leer → `ConnectionStrings:BlobConnection` → `BlobConnection` | leer; Dev `UseDevelopmentStorage=true` | P:163-171 |
| `…:ContainerName` | Container der Medien | `etsetech-blobuploads`; Dev `skoulou-media-dev` | Options:19 |
| `…:FinanzBelegContainerName` | Container der Belege | `etsetech-blobuploads` | Options:20 |
| `…:LocalDataRootPath` | lokaler Ordner, wenn Blob aus ist (nur Dev) | `wwwroot/media-library`; Dev `<lokaler Pfad>` | Options:21 |
| `…:Enabled` | wird **ignoriert** (Dev → false, sonst true) | appsettings true/false | P:172-179 |
| `MediaStorage:RootFolder` / `MediaStorage:PublicBasePath` | alter lokaler Speicher (nur Dev) | `wwwroot/media` / `media` | `MediaStorageOptions.cs` |
| `LessonPdf:BrowserPath` | Chrome/Edge für HTML→PDF (Bereich Unterricht) | leer → bekannte Installationspfade | `LessonExerciseA4PdfService.cs:198` |
| `OpenAI:ApiKey` | steht in appsettings, wird **nirgends gelesen** | leer | – |
| `Logging:LogLevel:*` | in appsettings, aber wirkungslos (Serilog mit festem Level Information, Konsole + DB-Sink) | Information / Warning | P:36-41 |
| `AllowedHosts` | Host-Filter | `*` | – |
| `ASPNETCORE_ENVIRONMENT` | Development schaltet Blob ab und lokalen Speicher ein, Hard-Delete frei, keine HSTS/Fehlerseite | – | P:172-204, 254, 282 |
| `SKOULOU_SQL_TESTS` | nur Tests: SQL-Server-Tests aktivieren | – | Tests |

**Fest im Code** (bei Bedarf als Konstanten übernehmen):
- Postmark-Basis-URL `https://api.postmarkapp.com/`
- HttpClient-Name `mail-weiterleitung` mit 15 s Timeout
- `web.config`: in-process, stdout-Log nach `%home%\LogFiles\stdout`
- QuestPDF Community-Lizenz (P:34)

---

## 6. Durch Tests festgelegte Regeln

Die meisten Tests laufen mit EF InMemory. **Unique-Indizes werden dort nicht geprüft.** SQL-Tests laufen nur mit `SKOULOU_SQL_TESTS=1`. `FakeEmailService` sammelt die Mails und kann Fehler und Exceptions simulieren.

- **Registrierung und Login**
  - Ohne AGB keine Registrierung, auch keine Schule (T/AuthServiceRegistrationTests.cs:43/90).
  - Nach Erfolg genau 1 Willkommensmail mit Textteil (:54).
  - Die Vorprüfung meldet einen doppelten Schulnamen vor den AGB und legt nichts an (:101/130).
  - AGB-Pflicht und Annahme (:152/163).
  - Sperrhinweis nur bei richtigem Passwort (T/GesamtAblaufTests.cs:35).
  - Selbstregistrierte Direktion hat `IsBlocked` und `IsSelfRegistered`, Rollen SL+Lehrer, `CanManageStudents`. Nach Freigabe gibt es den Login per Identifiant mit `ActiveSessionId` (:57, Kontoteil Z.114-253).
  - Von der Direktion angelegte Schüler sind sofort aktiv.
- **Identifiant**
  - Normalisierung und Format (T/IdentifiantTests.cs:24/33).
  - Zwei Konten mit gleicher E-Mail bekommen `ama.mensah` / `ama.mensah2` (:37).
  - Vergeben ohne Groß-/Kleinschreibung; Direktion ohne E-Mail abgelehnt, Schüler ohne E-Mail erlaubt (:70).
  - Der Reset nennt alle Konten einer E-Mail (:104).
  - Der Import erzeugt ein Identifiant ohne Platzhalter-E-Mail (T/SchoolUserImportServiceTests.cs:34) und ein Passwort mit mindestens 20 Zeichen (:164).
- **Seed**
  - Ohne Konfiguration kein Konto, aber Rollen (T/SuperAdminSeedTests.cs:47).
  - Mit Konfiguration genau 1 Admin, beim zweiten Start keine Dublette (:59).
  - Ohne Identifiant ist die E-Mail der Name (:77).
  - Vorhandener Name ohne Rolle ergibt eine Exception (:88).
- **Passwort und Session**
  - Ein E-Mail-Wechsel ändert UserName und Hash nicht. Ein falsches altes Passwort lässt Session und Hash unverändert. Ein richtiger Wechsel setzt eine neue Session (T/SchuelerIdentitaetTests.cs:24).
  - Das Startpasswort der Direktion gilt nur für eigene Konten und nie für das eigene (T/KontoZugangServiceTests.cs:14).
  - Bestätigungsmails ohne Passwort und Token, Mailfehler ohne Rollback, Reset-Link-Format (T/WillkommensMailTests.cs:182-285).
- **Konten und Schulen** (T/DashboardServiceTests.cs)
  - PO legt nur Directeurs seiner Schulen an (:58, :1888).
  - PO-Link hat `CanManageStudents=false` (:87); PO mit 2 Schulen hat nur Plattformlinks (:238).
  - PO sieht nur eigene Schulnamen (:110).
  - Sperren nur durch SA; PO entsperrt nur in eigenen Schulen (:132).
  - Genau eine Schule / `MeldungSchuleFest` (:196, :382, :416, :2010).
  - Anlagerechte zwischen Ko, SL und Lehrer (:167, :1767, :1932, :1982).
  - Koordinator-Finanzflags (:2010-2122).
  - Sperren und Freischalten (:1342-1533). Eine Kontosperre ist unabhängig von Zahlungen; der Name des Tests :1433 sagt das Gegenteil.
  - Offene Selbstregistrierung löschen (:751/786).
  - Development-Hard-Delete und Verbot in Production (:815/882).
  - Schulpreis-Override und Kommissionsfelder bleiben null (:1178-1287).
  - Admin-Kennzahlen (:903); Schulen alphabetisch (:937).
  - Ein Test für Aktivieren/Deaktivieren von Schulen **fehlt**.
- **Schulsperre**
  - Ein gesperrter Directeur sperrt die Schule. Ein Lehrer mit aktiver Zweitschule darf weiter (T/SchulZugangServiceTests.cs:12).
  - Inhalte und Aktionen in einer gesperrten Schule sind gesperrt (T/SchulSperreInhaltsTests.cs:15/39).
- **Mails**
  - Aktivierungsmail nur beim Übergang, Ablehnung nur bei offener Registrierung, Adressänderung an alt und neu, Zahlungsmail erst mit Buchung, Abo-Mail nur bei der ersten Zahlung, keine Exceptions (T/KontoBenachrichtigungServiceTests.cs:18-120).
  - Willkommenstexte je Rolle; bei Direktor + Lehrer gewinnt die höhere Rolle (T/WillkommensMailTests.cs:102-369).
  - Layout, Farben, Preheader, kein `<style>`, ToPlainText (T/EmailTemplatesTests.cs).
  - Präfix [SKOULOU] einmalig, Layout nicht doppelt (T/SkoulouEmailServiceTests.cs).
  - eNOTE-Erkennung, eigene Absender, Basic-Auth der Weiterleitung, Verbleib bei Fehler (T/MailWeiterleitungTests.cs).
  - Deduplizierung, PO-Sicht erst nach Zuordnung, PO-Pflichtschule und nur Schulkontakte, SA frei, Kontaktliste, Gruppenversand nur SA (T/MailboxServiceTests.cs).
  - Zusatzadressen und Duplikate (T/BroadcastEmailServiceTests.cs).
  - WhatsApp-Link (T/ResponsableKontaktServiceTests.cs:13).
- **Listen** (T/ListenSucheTests.cs)
  - Nutzer seitenweise, sortiert nach Hauptrolle, Filter und Zähler (:48/75); PO ohne Admins (:112).
  - Medienliste (:147), Mailbox (:259), Fehlerjournal (:312), Annonces (:383).
- **Annonces:** Ablauf in der Zukunft, Bereinigung, aktiv bis zum Ablauf, Löschen (T/AnkuendigungServiceTests.cs).
- **Hilfe**
  - Handbuch-Reiter je Rolle (T/HandbuchRoleTests.cs:16): SA alle 6; SL ohne admin; Ko learning/teaching/school/commissions; PO school/commissions; Lehrer learning/teaching/commissions; Schüler learning.
  - Videogruppen je Rolle und Abrufrecht; anonym nur das Werbevideo; Dateinamenregex (T/VideoKatalogTests.cs).
- **Infrastruktur**
  - Kultur (T/SkoulouKulturTests.cs).
  - ServiceUri-Prüfung (T/SkoulouBlobSpeicherTests.cs).
  - Beleg-Pfade, kein wwwroot, kein Rückfall in Production (T/AzureFinanzBelegStorageTests.cs).
  - PO-Medienrechte (T/ResponsableMedienRechteTests.cs:21).
  - Logo-Prüfung (T/SchulLogoTests.cs).
  - Sink nur mit Exception (T/DatabaseExceptionLogSinkTests.cs:13).
  - 499 bei Abbruch (T/RequestAbbruchMiddlewareTests.cs).
  - Atomare Kontoanlage und Commit-Beleg (T/BenutzerArbeitsvorgangSqlTests.cs:25).

---

## 7. Schwächen und Verbesserungen für den Neubau

1. **Mehrere Konten pro E-Mail gegen den Unique-Index.**
   - Code, Kommentare und Tests wollen eine E-Mail für mehrere Konten erlauben (`RequireUniqueEmail=false` P:96, IR:11-13, AuS:323).
   - Die DB hat aber `IX_AspNetUsers_Email` **UNIQUE** (gefiltert auf NOT NULL; Ctx:111, InitialMigration). Auf SQL Server scheitert die zweite Anlage. Die InMemory-Tests verdecken das.
   - → Fachlich entscheiden (siehe 8). Wenn mehrfach erlaubt, nur einen nicht eindeutigen Index anlegen und einen SQL-Test dazu schreiben.
2. **Selbstregistrierte Direktion lässt sich nicht freischalten.**
   - `SetStudentBlockedStateCoreAsync` verlangt für jedes selbstregistrierte Konto StudentProfiles mit Klasse (DS:3907-3910). Ein Directeur hat keine und bekommt die falsche Meldung « Un élève ayant un abonnement payé… ».
   - Folge: Die neue Schule bleibt dauerhaft gesperrt.
   - → Eigener Ablauf « Valider l'établissement » für SA: Schule und Direktion zusammen freigeben oder ablehnen. Ablehnung mit Mail und Löschung der leeren Schule.
3. **Schüler können sich bei einer noch nicht validierten Schule registrieren.** Die Schulliste prüft nur `IsActive` (AuS:59-64), und neue Schulen sind sofort `IsActive=true`.
   - → Status `EnAttente` / `Active` / `Suspendue` explizit an der Schule führen. Registrierung nur bei `Active`.
4. **Schulsperre ist implizit.** „Irgendein gesperrter Directeur“ sperrt die Schule (SZS:14-16), ohne Hinweis im UI und ohne Mail.
   - → Expliziter Schulstatus (Nr. 3), Kontosperre davon trennen, Bestätigungsdialog, Info-Mail an die Direktion.
5. **Der Login verschluckt den Hinweis « accès suspendu ».** Er wird zu „falsche Zugangsdaten“ (P:729-731).
   - → Eigener Fehlercode `school-suspended`.
6. **Permissions sind toter Code.** Seed, Tabellen und Texte bleiben ohne Wirkung. Die Rechte stecken verstreut in Services.
   - → Entweder echte Policy- und Permission-Prüfung zentral, oder Tabellen weglassen. Rechte als eine Matrix wie 1.3 im Code definieren und testen.
7. **Keine Brute-Force- und Spam-Bremse.** Kein Lockout (AuS:271). Kein Rate-Limit auf Login, Registrierung, Passwort vergessen, Kontakt und Webhook.
   - → Identity-Lockout (z. B. 5 Versuche / 15 min), Rate-Limiter je IP. Für die öffentlichen Formulare ein Honeypot oder Captcha.
8. **AGB nur im UI.**
   - Das Modal ist überspringbar, Endpunkte funktionieren ohne Zustimmung.
   - SA erscheint als « Élève ».
   - Die AGB-Texte stehen doppelt und **weichen voneinander ab** (Register und Guard: Lehrer-Kommission, PO-Quittierung).
   - Die Version ist eine Code-Konstante.
   - → Middleware oder Policy „AGB akzeptiert“, ein einziger Textbaustein je Rolle, Version als Konfiguration oder Tabelle.
9. **Falsche Willkommenstexte.**
   - Im /admin angelegte Directeurs lesen « activé après validation », obwohl sie aktiv sind.
   - PO, Koordinator und Eltern bekommen den generischen Registrierungstext (AuS:785-913).
   - → Texte nach Rolle **und** Entstehung (selbst registriert / angelegt) wählen.
10. **Registrierung ohne Transaktion.**
    - Erst die Schule, dann das Konto, dann Rollen ohne Ergebnisprüfung (AuS:138-168).
    - Doppelter Schulname nur per exaktem Vergleich, Groß-/Kleinschreibung hängt von der DB-Collation ab.
    - → Eine Transaktion; Name normalisiert vergleichen.
11. **Irreführende Meldungen und ignorierte Parameter.**
    - « …abonnement payé… » bei jedem Fehlschlag; `allowBlockingPaidStudent` wird nicht benutzt (DS:149-152).
    - Formular-Checkboxen « bloqué » / « Auto-inscription » werden überschrieben.
    - Der Dialog mit Zugangsdaten erscheint auch bei selbst gesetztem Passwort.
12. **/admin kann wenig.**
    - Kein Bearbeiten von Konten (auch keine E-Mail-Korrektur für Directeurs und PO).
    - Keine spätere Änderung der PO-Schulen; kein Ablehnen und kein Löschen in Production.
    - Die Rolle « Eltern » erscheint unübersetzt als „Eltern“.
    - Texte ohne Akzente (« ecole », « Eleves ») und kaputte Emojis.
    - → Kontodetail mit Bearbeiten, Rollen- und Schulzuordnung, Deaktivieren statt Löschen.
13. **Gruppen-Mails erreichen nicht alle.** « Directeurs » sind nur SchulLeiter, ohne Koordinatoren. « Tout le monde » schließt Koordinatoren, PO und Eltern aus (BES:141-147).
    - → Zielgruppen frei kombinierbar; „Direction“ = SL + Ko.
14. **Messagerie-Lücken.**
    - Ein PO kann externen Absendern nicht antworten, weil der Empfänger ein Schulkonto sein muss (MBS:174-178).
    - Eingehende Mails werden nie automatisch zugeordnet; man könnte über den Absender eine Schule finden.
    - Der Inhalt wird nur als Klartext angezeigt.
    - Die Gruppenmail läuft im Einzelversand statt per Batch und speichert das ganze Layout-HTML.
    - Webhook mit falschem `To` liefert 400, Postmark wiederholt dann.
    - → Antwort auf eingehende Mails immer erlauben; Auto-Zuordnung per Absenderkonto; 200 + Ignorieren für fremde `To`.
15. **Kontaktformular.** Es speichert vor dem Senden, Wiederholungen erzeugen Dubletten. Die Länge wird auf rohem HTML geprüft.
    - → Speichern und Senden entkoppeln (Outbox), eine Prüfung im Service.
16. **Annonces.**
    - Abfrage alle 30 s pro Circuit, auch für anonyme Besucher.
    - Keine Zielgruppe (Schule, Rolle), keine Startzeit.
    - Ablauf über die Serverzeitzone (Azure = UTC, Togo = UTC, also zufällig korrekt).
    - → Cache (MemoryCache, Invalidierung beim Speichern) oder Push über den Circuit; optional Zielgruppen; Zeitzone fest `Africa/Lome`.
17. **Leistung.**
    - `OnValidatePrincipal` mit 2–4 DB-Abfragen **pro Request**, dazu Revalidierung alle 30 s.
    - Globaler `sp_getapplock` serialisiert **jede** Kontoanlage und Sperre plattformweit.
    - /admin lädt nach jeder Aktion alles neu; `GetAdminSummaryAsync` lädt alle Lehrer und Schüler in den Speicher.
    - Die Bildbibliothek lädt alles ohne Paging.
    - → Session-Prüfung mit kurzem Cache (z. B. 30–60 s) oder Security-Stamp; Lock je Schule; Kennzahlen per COUNT; gezieltes Nachladen.
18. **Fehlerjournal.**
    - Synchrone DB-Schreibvorgänge im Logging-Pfad.
    - Keine Aufbewahrungsfrist, Löschen ohne Rückfrage.
    - Die Schule ist die „erste“ des Nutzers.
    - `Logging:LogLevel` ist wirkungslos.
    - → Asynchroner Sink mit Puffer, Retention (z. B. 90 Tage), Serilog-Level aus der Konfiguration lesen, Rückfrage beim Löschen.
19. **Konfigurationsaltlasten.**
    - `AzureBlobStorage:Enabled` wird ignoriert; zwei Abschnitte für dieselben Optionen.
    - `OpenAI:ApiKey` und `MediaStorage:*` sind ungenutzt bzw. Altlast.
    - `App:BaseUrl` fällt auf localhost zurück.
    - → Schlüssel aus 5.5 beibehalten, aber beim Start validieren (`ValidateOnStart`). Fehlt `App:BaseUrl` in Production, Start abbrechen.
20. **Sicherheitsdetails.**
    - `/auth/logout` ohne Antiforgery.
    - `/audio-test` öffentlich.
    - `/access-denied` tot: Ein angemeldeter Nutzer mit falscher Rolle landet auf der Login-Seite.
    - Der Reset ist auch für gesperrte Konten möglich.
    - Interne Blob-Pfade sind im UI sichtbar.
    - Bilder werden serverseitig nicht größenbegrenzt; Content-Type und Videodauer stammen vom Client.
    - Einige DashboardService-Methoden prüfen den Aufrufer nicht (z. B. `GetSchoolForEditAsync` DS:1574).
    - → Antiforgery für Logout, Access-Denied-Seite für Angemeldete, Prüfung in **jedem** Service, Signaturprüfung für Uploads.
21. **Handbuch und Hauptrolle.** Die Rangfolge der Rollen steht an vier Stellen und ist verschieden (Terms, Handbuch, Welcome-Mail, VideoKatalog). Eltern bekommen das Schüler-Handbuch.
    - → Eine zentrale Funktion `Hauptrolle(user)`, eine Eltern-Lasche.
22. **Medien.**
    - `Length` ist bei Browser-Uploads vermutlich 0, sodass die 8-GB-Prüfung und die Summen nicht wirken.
    - Ersetzte Profilbilder bleiben liegen.
    - Der Development-Hard-Delete scheitert an FK `Restrict` der Medien.
    - Lokale und Azure-Pfade haben verschiedene Segmentreihenfolge.
    - → Länge serverseitig zählen, verwaiste Assets aufräumen, ein Pfadschema.

---

## 8. Offene Fragen

1. Darf eine E-Mail-Adresse zu mehreren Konten gehören (Lehrkraft an zwei Schulen, Eltern mehrerer Kinder an verschiedenen Schulen)? Code und Tests sagen ja, die Datenbank sagt nein (7, Nr. 1).
2. Wie werden selbstregistrierte Schulen heute tatsächlich validiert, etwa per SQL? Soll der SA eine Schule auch **ablehnen** können, mit Mail und Löschung?
3. Soll das Sperren **eines** Directeurs wirklich die ganze Schule sperren, auch wenn es einen zweiten aktiven Directeur gibt? Oder soll das nur der Schulstatus tun?
4. Dürfen Schüler sich schon vor der Validierung einer Schule bei ihr registrieren?
5. Soll ein Responsable Konten nur freischalten und nie sperren dürfen? Soll er Directeurs auch bearbeiten können oder nur anlegen?
6. Wer ändert die Schulzuordnung eines Responsables, und wie? Darf eine Schule mehrere Responsables haben? Heute gewinnt im Kontakt der alphabetisch erste.
7. Sollen Annonces für anonyme Besucher sichtbar sein? Braucht es Zielgruppen (Schule, Rolle) und eine Startzeit?
8. Sollen Gruppenmails auch Koordinatoren, Eltern und Responsables erreichen? Sollen Eltern die Schülerbenachrichtigungen bekommen, etwa die Zahlungsmail, die heute nur an die Schüleradresse geht?
9. AGB: Wer erhöht die Version, und muss jede Änderung von allen neu akzeptiert werden? Welcher Text gilt für Lehrer- und PO-Kommissionen (zwei abweichende Fassungen)? Braucht der SuperAdmin überhaupt AGB?
10. Wie lange sollen Mails (MailNachricht) und Fehlerprotokolle aufbewahrt werden (Datenschutz)?
11. Soll « Rester connecté » trotz „eine Sitzung pro Konto“ bleiben? Mobil und Desktop gleichzeitig ist heute unmöglich.
12. Der PO-Anteil ist einstellbar, SL- und Lehrer-Anteile sind fest 0, die Felder pro Schule sind tot. Ist das das Zielmodell? Abstimmen mit dem Bereich Abrechnung.
13. Soll der Kontakt für Lehrer, Schüler und Eltern auf die Schule umgeleitet werden, statt an den SKOULOU-Support zu gehen? Die Seite rät es heute nur.
14. Sollen eingehende Mails automatisch einer Schule zugeordnet werden, wenn der Absender ein bekanntes Konto ist?
