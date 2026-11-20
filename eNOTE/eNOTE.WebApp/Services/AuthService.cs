using System.Security.Claims;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>
/// Authentifizierung und öffentliche Registrierung (Besitzer legt sich mit
/// seinem Salon an; der SuperAdmin gibt den Salon anschließend frei).
/// </summary>
public class AuthService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    ILogger<AuthService> logger,
    Mails.EmailService emailService,
    Mails.MailBenachrichtigungen? mails = null, TimeProvider? zeit = null, Sicherheit.MissbrauchsBremse? bremse = null,
    AnmeldeSitzungen? anmeldeSitzungen = null)
{
    private readonly TimeProvider uhr = zeit ?? TimeProvider.System;
    private AnmeldeSitzungen Sitzungen => anmeldeSitzungen ?? new AnmeldeSitzungen(dbContextFactory, uhr);

    private const string AppName = "eNOTE";
    private const string AppUrl = "https://enote.tablosuite.com";

    /// <summary>Höchstlängen der Registrierung (Spalten UserName/Email: 256, Salons.Name: 450; Namen wie in « Mon profil »).</summary>
    public const int PseudonymMax = 64, EmailMax = 256, LadenNameMax = 120;

    /// <summary>Zeichen, die ASP.NET Identity in Pseudonymen zulässt (Standard von UserOptions.AllowedUserNameCharacters).</summary>
    private const string PseudonymZeichen = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";

    /// <summary>
    /// Prüft Länge und Zeichen der Registrierungsfelder, bevor ein Konto entsteht. Null = in Ordnung, sonst eine französische
    /// Meldung (vorher: Datenbankfehler « HTTP 500 » bzw. die englische Meldung von Identity).
    /// </summary>
    public static string? PruefeRegistrierungsFelder(string? vorname, string? nachname, string pseudonym, string email, string salonName)
    {
        if (string.IsNullOrWhiteSpace(vorname) || string.IsNullOrWhiteSpace(nachname))
            return "Veuillez saisir votre prénom et votre nom.";
        if (vorname.Trim().Length > KontoProfilService.MaxNamenLaenge || nachname.Trim().Length > KontoProfilService.MaxNamenLaenge)
            return $"Le prénom et le nom sont limités à {KontoProfilService.MaxNamenLaenge} caractères.";
        if (pseudonym.Length > PseudonymMax)
            return $"Pseudonyme : {PseudonymMax} caractères au maximum.";
        if (pseudonym.Any(c => !PseudonymZeichen.Contains(c)))
            return "Le pseudonyme ne peut contenir que des lettres sans accent, des chiffres et les signes . - _ @ + (sans espace). Exemple : kofi.mensah";
        if (email.Length > EmailMax)
            return $"Adresse e-mail : {EmailMax} caractères au maximum.";
        if (salonName.Length > LadenNameMax)
            return $"Nom de l'établissement : {LadenNameMax} caractères au maximum.";
        return null;
    }
    /// <summary>
    /// Ende der kostenlosen Probezeit: 3 Kalendermonate, wobei das aktuelle
    /// Monat als erstes zählt. Start am 3. November → Probe bis 31. Januar
    /// (November, Dezember, Januar).
    /// </summary>
    public static DateTime BerechneProbeEnde(DateTime startUtc)
    {
        var monatsAnfang = new DateTime(startUtc.Year, startUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return monatsAnfang.AddMonths(3).AddTicks(-1); // Ende des 3. Monats
    }

    /// <summary>
    /// Probezeit nur für den ersten Laden eines Besitzers. Hat er bereits einen
    /// anderen Laden, gibt es für weitere Läden keine Probezeit mehr.
    /// </summary>
    public async Task<DateTime?> BerechneProbeEndeFuerBesitzerAsync(Guid besitzerId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var hatBereitsLaden = await db.UserSalons
            .AnyAsync(us => us.AppUserId == besitzerId, cancellationToken);

        return hatBereitsLaden ? null : BerechneProbeEnde(uhr.GetUtcNow().UtcDateTime);
    }

    public async Task<(bool Succeeded, string? Error)> LoginAsync(
        string pseudonym,
        string passwort,
        bool dauerhaft,
        CancellationToken cancellationToken = default)
    {
        // Login erfolgt über das eindeutige Pseudonym (UserName), nicht über die
        // mehrfach verwendbare Kontakt-E-Mail.
        var user = await userManager.FindByNameAsync(pseudonym.Trim());
        if (user is null || user.IsBlocked)
        {
            return (false, "invalid-credentials");
        }

        var ergebnis = await signInManager.CheckPasswordSignInAsync(user, passwort, lockoutOnFailure: true);
        if (!ergebnis.Succeeded)
        {
            return (false, "invalid-credentials");
        }

        // « Connexion personnelle »: Mitarbeiter-Konten nur mit diesem Recht (sonst kassieren sie am Gerät des Besitzers).
        // Erst nach dem richtigen Passwort geprüft – Fremde erfahren so nicht, dass es ein Mitarbeiter-Konto ist.
        await using (var db = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            if (!await LadenZugriff.DarfSichAnmeldenAsync(db, user.Id, cancellationToken))
            {
                return (false, "employe-sans-connexion");
            }
        }

        // « Appareils connectés » (17.11.2026): Besitzer bleiben auf ihren anderen Geräten angemeldet (zwei Kassen im Laden);
        // für alle anderen Konten meldet eine neue Anmeldung die frühere Sitzung ab (neuer Konto-Stempel).
        var (stempel, geraetSitzung) = await Sitzungen.AnmeldenAsync(user.Id,
            signInManager.Context.Request.Headers.UserAgent.ToString(), cancellationToken);

        await signInManager.SignInAsync(user, dauerhaft);

        // Session-Claim setzen, damit OnValidatePrincipal die aktive Sitzung prüfen kann.
        if (signInManager.Context.User.Identity is ClaimsIdentity)
        {
            await signInManager.SignOutAsync();
            var principal = await signInManager.CreateUserPrincipalAsync(user);
            var identity = (ClaimsIdentity)principal.Identity!;
            identity.AddClaim(new Claim("enote_session", stempel.ToString()));
            identity.AddClaim(new Claim(AnmeldeSitzungen.GeraetClaim, geraetSitzung.ToString()));
            await signInManager.Context.SignInAsync(
                IdentityConstants.ApplicationScheme,
                principal,
                new Microsoft.AspNetCore.Authentication.AuthenticationProperties { IsPersistent = dauerhaft });
        }

        return (true, null);
    }

    public async Task LogoutAsync() => await signInManager.SignOutAsync();

    /// <summary>Ändert das Passwort des angemeldeten Nutzers (aktuelles Passwort erforderlich).</summary>
    public async Task<(bool Erfolg, string? Fehler)> AenderePasswortAsync(
        Guid userId, string aktuellesPasswort, string neuesPasswort, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return (false, "Utilisateur introuvable.");
        }

        var ergebnis = await userManager.ChangePasswordAsync(user, aktuellesPasswort, neuesPasswort);
        if (!ergebnis.Succeeded)
        {
            var fehler = string.Join(" ", ergebnis.Errors.Select(e => e.Description));
            return (false, string.IsNullOrWhiteSpace(fehler) ? "Le changement de mot de passe a échoué." : fehler);
        }

        if (mails is not null)
            await mails.PasswortGeaendertAsync(user.Id, cancellationToken);

        // Sitzung rotieren, damit andere Geräte abgemeldet werden.
        user.ActiveSessionId = Guid.NewGuid();
        user.UpdatedAtUtc = uhr.GetUtcNow().UtcDateTime;
        await userManager.UpdateAsync(user);
        return (true, null);
    }

    /// <summary>Länder für das Registrierungsformular (siehe <see cref="LaenderAuswahl"/>).</summary>
    public async Task<List<string>> GetLaenderAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await LaenderAuswahl.GetAsync(db, cancellationToken);
    }

    public async Task<(bool Succeeded, string ErrorMessage)> RegisterBesitzerAsync(
        string vorname,
        string nachname,
        string pseudonym,
        string email,
        string passwort,
        string salonName,
        string? salonStandort,
        string? salonSlogan = null,
        CancellationToken cancellationToken = default,
        string? land = null,
        string? anschluss = null,
        string? telefon = null)
    {
        var bereinigtPseudonym = pseudonym.Trim();
        var bereinigtEmail = email.Trim();
        var bereinigtSalon = salonName.Trim();

        // Bremse gegen Massenregistrierungen: je Anschluss und je E-Mail-Adresse nur wenige erfolgreiche Registrierungen am Tag.
        if (bremse is not null
            && (!bremse.IstErlaubt("inscription-anschluss", anschluss, Sicherheit.MissbrauchsBremse.InscriptionJeAnschluss)
                || !bremse.IstErlaubt("inscription-adresse", bereinigtEmail, Sicherheit.MissbrauchsBremse.InscriptionJeAdresse)))
        {
            return (false, Sicherheit.MissbrauchsBremse.MeldungInscription);
        }

        if (string.IsNullOrWhiteSpace(bereinigtPseudonym))
        {
            return (false, "Veuillez choisir un pseudonyme.");
        }

        // Klicktest 20.11.2026: zu lange Eingaben endeten in « HTTP 500 » (Spaltenlänge der Datenbank) und ließen ein Konto
        // ohne Laden zurück (Pseudonym danach blockiert); Leerzeichen/Akzente im Pseudonym ergaben eine englische Meldung.
        if (PruefeRegistrierungsFelder(vorname, nachname, bereinigtPseudonym, bereinigtEmail, bereinigtSalon) is { } feldFehler)
        {
            return (false, feldFehler);
        }

        // Telefon ist Pflicht: wer einen Laden registriert, muss leicht erreichbar sein (Validierung, Rückfragen).
        var (bereinigtTelefon, telefonFehler) = TelefonRegel.Pruefe(telefon);
        if (telefonFehler is not null)
        {
            return (false, telefonFehler);
        }

        // Login = Pseudonym (eindeutig); E-Mail ist nur Kontaktadresse und darf mehrfach sein.
        if (await userManager.FindByNameAsync(bereinigtPseudonym) is not null)
        {
            return (false, "Ce pseudonyme est déjà utilisé.");
        }

        if (string.IsNullOrWhiteSpace(bereinigtSalon))
        {
            return (false, "Veuillez indiquer le nom de votre salon.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Salons.AnyAsync(s => s.Name == bereinigtSalon, cancellationToken))
        {
            return (false, "Un salon portant ce nom existe déjà.");
        }

        // Land (Ortszeit, Mobile Money) aus dem Formular prüfen, bevor das Konto entsteht; ohne Angabe gilt Togo.
        string? ladenLand = null;
        if (land is not null)
        {
            try { ladenLand = await LaenderAuswahl.PruefeAsync(db, land, cancellationToken); }
            catch (ArgumentException ex) { return (false, ex.Message); }
        }

        var besitzer = new AppUser
        {
            UserName = bereinigtPseudonym,
            Email = bereinigtEmail,
            Vorname = vorname.Trim(),
            Nachname = nachname.Trim(),
            FullName = $"{vorname.Trim()} {nachname.Trim()}",
            TelefonNummer = bereinigtTelefon,
            EmailConfirmed = true,
            IsSelfRegistered = true,
            // Konto ist nutzbar, aber der Salon bleibt bis zur Freigabe gesperrt.
            IsBlocked = false
        };

        IdentityResult erstellen;
        try
        {
            erstellen = await userManager.CreateAsync(besitzer, passwort);
        }
        catch (DbUpdateException ex)
        {
            // Gleichzeitige Registrierung mit demselben Pseudonym (eindeutiger Index) – keine Fehlerseite.
            logger.LogWarning(ex, "Besitzer-Registrierung: Konto {Pseudonym} konnte nicht gespeichert werden.", bereinigtPseudonym);
            return (false, "Ce pseudonyme est déjà utilisé.");
        }
        if (!erstellen.Succeeded)
        {
            var fehler = string.Join(" ", erstellen.Errors.Select(e => e.Description));
            logger.LogWarning("Besitzer-Registrierung fehlgeschlagen für {Email}: {Fehler}", bereinigtEmail, fehler);
            return (false, string.IsNullOrWhiteSpace(fehler) ? "L'inscription a échoué." : fehler);
        }

        var salon = new Salon
        {
            Name = bereinigtSalon,
            Standort = string.IsNullOrWhiteSpace(salonStandort) ? null : salonStandort.Trim(),
            Slogan = string.IsNullOrWhiteSpace(salonSlogan) ? null : salonSlogan.Trim(),
            Land = ladenLand,
            IstAktiv = true,
            IstFreigegeben = false,
            ProbeBisUtc = BerechneProbeEnde(uhr.GetUtcNow().UtcDateTime)
        };

        try
        {
            await userManager.AddToRoleAsync(besitzer, AppRollen.Besitzer);

            db.Salons.Add(salon);
            await db.SaveChangesAsync(cancellationToken);

            db.UserSalons.Add(new UserSalon
            {
                AppUserId = besitzer.Id,
                SalonId = salon.Id,
                // Besitzer hat alle salonbezogenen Rechte.
                KannFinanzenSehen = true,
                KannLeistungenVerwalten = true,
                KannRabatteVerwalten = true
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Laden ließ sich nicht speichern (z. B. gleichzeitig derselbe Name): Konto wieder entfernen, sonst bliebe ein
            // Besitzer ohne Laden zurück und das Pseudonym wäre für einen neuen Versuch blockiert.
            logger.LogWarning(ex, "Besitzer-Registrierung: Laden {Salon} konnte nicht gespeichert werden – Konto wird entfernt.", bereinigtSalon);
            if (db.Entry(salon).State == EntityState.Unchanged)
            {
                // Laden gespeichert, Zuordnung nicht: Laden über einen frischen Context wieder entfernen.
                await using var aufraeumen = await dbContextFactory.CreateDbContextAsync(CancellationToken.None);
                if (await aufraeumen.Salons.FindAsync([salon.Id], CancellationToken.None) is { } gespeichert)
                {
                    aufraeumen.Salons.Remove(gespeichert);
                    try { await aufraeumen.SaveChangesAsync(CancellationToken.None); } catch (DbUpdateException) { }
                }
            }
            await userManager.DeleteAsync(besitzer);
            return (false, "L'inscription n'a pas pu être enregistrée. Vérifiez le nom de l'établissement (il existe peut-être déjà) et réessayez.");
        }

        logger.LogInformation("Neuer Salon registriert (freizugeben): {Salon} durch {Email}", bereinigtSalon, bereinigtEmail);

        // Willkommensmail (TabloSuite-Branding); Fehler blockieren die Registrierung nicht.
        try
        {
            await emailService.SendeAsync(
                bereinigtEmail,
                "Bienvenue sur TabloSuite",
                Mails.TabloSuiteMails.Bienvenue(besitzer.Vorname, AppName, AppUrl),
                "bienvenue",
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Willkommensmail an {Email} fehlgeschlagen.", bereinigtEmail);
        }

        // Info an das TabloSuite-Team: neues Établissement wartet auf Validierung.
        if (mails is not null)
            await mails.NeueRegistrierungAsync(bereinigtSalon, $"{vorname.Trim()} {nachname.Trim()}", bereinigtEmail, bereinigtTelefon,
                string.Join(" · ", new[] { ladenLand, salonStandort?.Trim() }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } ort ? ort : null,
                cancellationToken);

        bremse?.Merken("inscription-anschluss", anschluss);
        bremse?.Merken("inscription-adresse", bereinigtEmail);
        return (true, string.Empty);
    }
}
