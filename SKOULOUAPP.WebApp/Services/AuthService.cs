using SkoulouApp.Core.Options;
using SkoulouApp.Core.Models.Profile;
using SkoulouApp.Core.Models.TeacherLessons;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Services;
using SkoulouApp.Core.Services.Email;
using SkoulouApp.Core.Services.Text;
using SkoulouApp.WebApp.Data;
using SkoulouApp.WebApp.Models.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace SkoulouApp.WebApp.Services;

public class AuthService(
    ApplicationDbContext dbContext,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IEmailService emailService,
    IOptions<PostmarkOptions> postmarkOptions,
    IConfiguration configuration,
    ILogger<AuthService> logger)
{
    public const string BlockedAccountMessage = "Ton compte n'a pas encore été validé ou a été bloqué.";
    private const string AktuelleAgbVersion = "2026-08-05";
    public static string CurrentTermsVersion => AktuelleAgbVersion;

    public async Task<bool> RequiresTermsAcceptanceAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var terms = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.AgbVersion, user.AgbAcceptedAtUtc })
            .SingleOrDefaultAsync(cancellationToken);

        return terms is not null && (terms.AgbVersion != AktuelleAgbVersion || terms.AgbAcceptedAtUtc is null);
    }

    public async Task<bool> AcceptTermsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        user.AgbVersion = AktuelleAgbVersion;
        user.AgbAcceptedAtUtc = DateTime.UtcNow;
        user.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<List<School>> GetActiveSchoolsAsync(CancellationToken cancellationToken = default)
        => dbContext.Schools
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Prüft fachliche Voraussetzungen der öffentlichen Registrierung, bevor
    /// die Nutzungsbedingungen angezeigt werden. Es werden keine Daten angelegt.
    /// </summary>
    public async Task<(bool Succeeded, string ErrorMessage)> ValidateRegistrationAsync(
        RegisterInputModel input,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        // Anmeldung per Identifiant; die E-Mail ist Kontaktadresse und darf mehrfach vorkommen.
        input.Identifiant = IdentifiantRegel.Normalisieren(input.Identifiant);
        var identifiantFehler = await IdentifiantRegel.PruefenAsync(db, input.Identifiant, null, cancellationToken);
        if (identifiantFehler is not null)
        {
            return (false, identifiantFehler);
        }

        if (input.Art == RegistrierungsArt.SchulLeiter && string.IsNullOrWhiteSpace(input.Email))
        {
            return (false, "Une adresse e-mail est nécessaire pour la direction (validation, factures, mot de passe oublié).");
        }
        if (input.Art == RegistrierungsArt.SchulLeiter)
        {
            var schoolName = input.SchulName.Trim();
            if (string.IsNullOrWhiteSpace(schoolName))
            {
                return (false, "Veuillez indiquer le nom de ton école.");
            }

            if (await db.Schools.AnyAsync(school => school.Name == schoolName, cancellationToken))
            {
                return (false, "Une école portant ce nom existe déjà.");
            }

            return (true, string.Empty);
        }

        if (input.SelectedSchoolId is null)
        {
            return (false, "Veuillez choisir une école.");
        }

        var schoolExists = await db.Schools.AnyAsync(
            school => school.Id == input.SelectedSchoolId.Value && school.IsActive,
            cancellationToken);
        return schoolExists
            ? (true, string.Empty)
            : (false, "L'école sélectionnée est introuvable.");
    }

    /// <summary>
    /// Registriert einen SchulLeiter zusammen mit seiner neuen Schule.
    /// Der SchulLeiter erhält zusätzlich die Lehrer-Rolle für seine Schule.
    /// Das Konto ist zunächst blockiert und muss vom SuperAdmin freigegeben werden.
    /// </summary>
    public async Task<(bool Succeeded, string ErrorMessage)> RegisterSchulLeiterAsync(RegisterInputModel input, CancellationToken cancellationToken = default)
    {
        if (!input.AgbAccepted)
        {
            return (false, "Veuillez accepter les conditions d'utilisation avant de t'inscrire.");
        }

        var validation = await ValidateRegistrationAsync(input, cancellationToken);
        if (!validation.Succeeded)
        {
            return validation;
        }

        // Eigener, kurzlebiger DbContext: verhindert Kollisionen mit dem geteilten
        // Circuit-DbContext (Blazor Server) während paralleler Abfragen.
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var schule = new School
        {
            Name = input.SchulName.Trim(),
            City = input.SchulStadt.Trim(),
            Country = string.IsNullOrWhiteSpace(input.SchulLand) ? "Togo" : input.SchulLand.Trim(),
            IsActive = true
        };
        db.Schools.Add(schule);
        await db.SaveChangesAsync(cancellationToken);

        var user = NeuesSelbstregistriertesKonto(input);

        var createResult = await userManager.CreateAsync(user, input.Password);
        if (!createResult.Succeeded)
        {
            // Schule wieder entfernen, damit keine verwaiste Schule entsteht.
            db.Schools.Remove(schule);
            await db.SaveChangesAsync(cancellationToken);
            return (false, string.Join(" ", createResult.Errors.Select(x => x.Description)));
        }

        await userManager.AddToRolesAsync(user, [Rollen.SchulLeiter, Rollen.Lehrer]);

        db.UserSchools.Add(new UserSchool
        {
            AppUserId = user.Id,
            SchoolId = schule.Id,
            CanManageStudents = true
        });

        await db.SaveChangesAsync(cancellationToken);
        await SendWelcomeEmailAsync(user.UserName!, cancellationToken);
        return (true, "Votre établissement et votre compte de direction ont été créés. Ils attendent la validation par l'exploitant.");
    }

    public async Task<(bool Succeeded, string ErrorMessage)> RegisterStudentAsync(RegisterInputModel input, CancellationToken cancellationToken = default)
    {
        if (!input.AgbAccepted)
        {
            return (false, "Veuillez accepter les conditions d'utilisation avant de t'inscrire.");
        }

        var validation = await ValidateRegistrationAsync(input, cancellationToken);
        if (!validation.Succeeded)
        {
            return validation;
        }

        // Eigener, kurzlebiger DbContext: verhindert Kollisionen mit dem geteilten
        // Circuit-DbContext (Blazor Server) während paralleler Abfragen.
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (input.SelectedSchoolId is not Guid schoolId)
        {
            return (false, "Veuillez sélectionner une école.");
        }

        var school = await db.Schools.FirstOrDefaultAsync(x => x.Id == schoolId && x.IsActive, cancellationToken);
        if (school is null)
        {
            return (false, "L'école sélectionnée est introuvable.");
        }

        var user = NeuesSelbstregistriertesKonto(input);

        var createResult = await userManager.CreateAsync(user, input.Password);
        if (!createResult.Succeeded)
        {
            return (false, string.Join(" ", createResult.Errors.Select(x => x.Description)));
        }

        await userManager.AddToRoleAsync(user, Rollen.Schueler);

        db.UserSchools.Add(new UserSchool
        {
            AppUserId = user.Id,
            SchoolId = school.Id,
            CanManageStudents = false
        });

        db.StudentProfiles.Add(new StudentProfile
        {
            AppUserId = user.Id,
            SchoolId = school.Id,
            CompletedLessonsCount = 0,
            ElternTelefonNummer = NormalizeOptional(input.ElternTelefonNummer),
            ElternEmail = NormalizeOptional(input.ElternEmail)
        });

        await db.SaveChangesAsync(cancellationToken);
        await SendWelcomeEmailAsync(user.UserName!, cancellationToken);
        return (true, "Votre compte a été créé. Il attend la validation par l'établissement.");
    }

    private static AppUser NeuesSelbstregistriertesKonto(RegisterInputModel input)
    {
        var email = NormalizeOptional(input.Email);
        return new AppUser
        {
            UserName = IdentifiantRegel.Normalisieren(input.Identifiant),
            Email = email,
            Vorname = input.Vorname,
            Nachname = input.Nachname,
            FullName = input.FullName,
            TelefonNummer = NormalizeOptional(input.TelefonNummer),
            Geschlecht = input.Geschlecht,
            GeburtsdatumUtc = input.Geburtsdatum.HasValue ? DateTime.SpecifyKind(input.Geburtsdatum.Value, DateTimeKind.Utc) : null,
            AgbVersion = AktuelleAgbVersion,
            AgbAcceptedAtUtc = DateTime.UtcNow,
            EmailConfirmed = email is not null,
            IsSelfRegistered = true,
            IsBlocked = true
        };
    }

    /// <summary>Vorschlag für das Identifiant aus Vor- und Nachname (eindeutig).</summary>
    public async Task<string> IdentifiantVorschlagAsync(string? vorname, string? nachname, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await IdentifiantRegel.EindeutigerVorschlagAsync(db, vorname, nachname, cancellationToken: cancellationToken);
    }

    public const string UngueltigeAnmeldung = "L'identifiant ou le mot de passe est incorrect.";

    /// <summary>Anmeldung mit Identifiant (Identity-UserName) + Passwort. Ältere Konten nutzen ihren bisherigen Benutzernamen.</summary>
    public async Task<(bool Succeeded, string ErrorMessage)> PasswordSignInAsync(string identifiant, string password, bool rememberMe)
    {
        var user = string.IsNullOrWhiteSpace(identifiant) ? null : await userManager.FindByNameAsync(identifiant.Trim());
        if (user is null)
        {
            return (false, UngueltigeAnmeldung);
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false);
        if (!result.Succeeded)
        {
            return (false, UngueltigeAnmeldung);
        }

        if (user.IsBlocked)
        {
            return (false, BlockedAccountMessage);
        }

        await using var zugangsDb = await dbContextFactory.CreateDbContextAsync();
        if (!await new SchulZugangService(zugangsDb).DarfAnmeldenAsync(user.Id))
            return (false, SchulZugangService.GesperrtHinweis);

        user.ActiveSessionId = Guid.NewGuid();
        user.UpdatedAtUtc = DateTime.UtcNow;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return (false, "La connexion n'a pas pu être initialisée.");
        }

        var claims = new[] { new System.Security.Claims.Claim("skoulouapp_session", user.ActiveSessionId.Value.ToString()) };
        await signInManager.SignInWithClaimsAsync(user, new AuthenticationProperties { IsPersistent = rememberMe }, claims);
        return (true, string.Empty);
    }

    public Task SignOutAsync()
        => signInManager.SignOutAsync();

    public Task<IAuthenticationSchemeProvider> GetSchemeProviderAsync(IServiceProvider serviceProvider)
        => Task.FromResult(serviceProvider.GetRequiredService<IAuthenticationSchemeProvider>());

    // ----------------------------------------------------------------------
    // Passwort-Reset (öffentlich)
    // ----------------------------------------------------------------------

    /// <summary>
    /// Erzeugt (falls das Konto existiert) einen Passwort-Reset-Token und versendet den Reset-Link per E-Mail.
    /// Aus Sicherheitsgründen wird immer eine neutrale Erfolgsmeldung zurückgegeben.
    /// </summary>
    public async Task<(bool Succeeded, string Message)> SendPasswordResetAsync(string emailOderIdentifiant, CancellationToken cancellationToken = default)
    {
        const string neutral = "Si un compte correspond, un e-mail de réinitialisation a été envoyé à l'adresse enregistrée.";

        var eingabe = emailOderIdentifiant.Trim();
        if (string.IsNullOrWhiteSpace(eingabe))
        {
            return (true, neutral);
        }

        // Eine E-Mail kann zu mehreren Konten gehören (z. B. Lehrkraft an zwei Schulen):
        // Die Mail nennt dann jedes Identifiant mit eigenem Link.
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var normalisiert = eingabe.ToUpperInvariant();
        var konten = eingabe.Contains('@')
            ? await db.Users.AsNoTracking().Where(u => u.NormalizedEmail == normalisiert).OrderBy(u => u.UserName).Take(10).ToListAsync(cancellationToken)
            : await db.Users.AsNoTracking().Where(u => u.NormalizedUserName == normalisiert && u.Email != null).ToListAsync(cancellationToken);
        if (konten.Count == 0 || string.IsNullOrWhiteSpace(konten[0].Email))
        {
            return (true, neutral);
        }

        var zeilen = new System.Text.StringBuilder();
        foreach (var konto in konten)
        {
            var kontoUser = await userManager.FindByIdAsync(konto.Id.ToString());
            if (kontoUser is null) continue;
            var token = await userManager.GeneratePasswordResetTokenAsync(kontoUser);
            var resetUrl = BuildAppUrl($"/auth/reset-password?id={kontoUser.Id}&token={Uri.EscapeDataString(token)}");
            zeilen.Append($"<p>Identifiant : <strong>{System.Net.WebUtility.HtmlEncode(kontoUser.UserName)}</strong></p>");
            zeilen.Append(EmailTemplates.Button(resetUrl, "Choisir un nouveau mot de passe"));
        }

        var user = konten[0];
        var body = EmailTemplates.Wrap(
            "Réinitialisation du mot de passe",
            $"""
            <p>Bonjour {System.Net.WebUtility.HtmlEncode(user.Vorname)},</p>
            <p>Vous avez demandé la réinitialisation de votre mot de passe SKOULOU. {(konten.Count > 1 ? "Plusieurs comptes utilisent cette adresse ; choisissez le compte concerné :" : "Cliquez sur le bouton ci-dessous pour choisir un nouveau mot de passe :")}</p>
            {zeilen}
            <p>Si vous n'êtes pas à l'origine de cette demande, ignorez simplement cet e-mail.</p>
            """);

        try
        {
            var result = await emailService.SendAsync(
                new EmailRecipient(user.Email!, user.FullName),
                new EmailMessage
                {
                    Subject = "SKOULOUAPP – Réinitialisation du mot de passe",
                    HtmlBody = body,
                    TextBody = EmailTemplates.ToPlainText(body)
                },
                cancellationToken);

            if (!result.Succeeded)
            {
                logger.LogWarning("Password-Reset-Mail an {Email} konnte nicht gesendet werden: {Error}", user.Email, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Password-Reset-Mail an {Email} konnte nicht gesendet werden.", user.Email);
        }

        return (true, neutral);
    }

    /// <summary>Setzt das Passwort anhand des zugesandten Tokens zurück.</summary>
    public async Task<(bool Succeeded, string Message)> ResetPasswordAsync(Guid kontoID, string token, string newPassword)
    {
        var user = kontoID == Guid.Empty ? null : await userManager.FindByIdAsync(kontoID.ToString());
        if (user is null)
        {
            return (false, "Le lien de réinitialisation est invalide ou a expiré.");
        }

        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
            return (false, string.Join(" ", result.Errors.Select(x => x.Description)));

        user.ActiveSessionId = Guid.NewGuid();
        var sessionResult = await userManager.UpdateAsync(user);
        if (!sessionResult.Succeeded) return (false, "Le mot de passe a changé. Veuillez vous reconnecter.");
        await SendPasswordChangedEmailAsync(user, true);
        return (true, $"Votre mot de passe a été réinitialisé. Connectez-vous avec l'identifiant « {user.UserName} ».");
    }

    // ----------------------------------------------------------------------
    // Passwort ändern (eingeloggt)
    // ----------------------------------------------------------------------

    public async Task<(bool Succeeded, string Message)> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return (false, "Utilisateur introuvable.");
        }

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
            return (false, string.Join(" ", result.Errors.Select(x => x.Description)));

        user.ActiveSessionId = Guid.NewGuid();
        var sessionResult = await userManager.UpdateAsync(user);
        if (!sessionResult.Succeeded) return (false, "Le mot de passe a changé. Veuillez vous reconnecter.");
        await SendPasswordChangedEmailAsync(user, false);
        return (true, "Ton mot de passe a été mis à jour.");
    }

    private async Task SendPasswordChangedEmailAsync(AppUser user, bool wasReset)
    {
        if (!IstPlausibleEmail(user.Email ?? string.Empty)) return;

        var subject = wasReset ? "SKOULOUAPP – Mot de passe réinitialisé" : "SKOULOUAPP – Mot de passe modifié";
        var loginUrl = BuildAppUrl("/auth/login");
        var forgotPasswordUrl = BuildAppUrl("/auth/forgot-password");
        var body = EmailTemplates.Wrap(
            "Confirmation de sécurité",
            $"""
            <p>Bonjour {System.Net.WebUtility.HtmlEncode(user.Vorname)},</p>
            <p>Le mot de passe de ton compte SKOULOUAPP a été {(wasReset ? "réinitialisé" : "modifié")}.</p>
            <p>Tu peux maintenant te connecter avec ton nouveau mot de passe.</p>
            {EmailTemplates.Button(loginUrl, "Se connecter à SKOULOUAPP")}
            {EmailTemplates.InfoBox($"<strong>Tu n'es pas à l'origine de cette action&nbsp;?</strong><br/>Protège immédiatement ton compte en <a href=\"{forgotPasswordUrl}\" style=\"color:#1d4ed8;font-weight:700;\">demandant un nouveau mot de passe</a>, puis contacte l'assistance.")}
            """);

        try
        {
            var result = await emailService.SendAsync(
                new EmailRecipient(user.Email!, user.FullName),
                new EmailMessage { Subject = subject, HtmlBody = body, TextBody = EmailTemplates.ToPlainText(body) });
            if (!result.Succeeded)
                logger.LogWarning("Bestätigung der Passwortänderung an {Email} konnte nicht gesendet werden: {Error}", user.Email, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bestätigung der Passwortänderung an {Email} fehlgeschlagen.", user.Email);
        }
    }

    // ----------------------------------------------------------------------
    // Profil
    // ----------------------------------------------------------------------

    public async Task<UserProfileModel?> GetUserProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var user = await db.Users
            .AsNoTracking()
            .Include(item => item.ProfilBildAsset)
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var rollen = await (
            from userRole in db.UserRoles.AsNoTracking()
            join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == userId
            select role.Name!).ToListAsync(cancellationToken);

        var schulen = await (
            from userSchool in db.UserSchools.AsNoTracking()
            join school in db.Schools.AsNoTracking() on userSchool.SchoolId equals school.Id
            where userSchool.AppUserId == userId
            select new UserProfileSchoolItem
            {
                SchoolId = school.Id,
                SchoolName = school.Name
            }).ToListAsync(cancellationToken);

        var studentMeta = await db.StudentProfiles
            .AsNoTracking()
            .Where(profile => profile.AppUserId == userId)
            .Select(profile => new
            {
                profile.ElternTelefonNummer,
                profile.ElternEmail,
                profile.CurrentKlasseId
            })
            .FirstOrDefaultAsync(cancellationToken);

        var klassenAnzeige = studentMeta?.CurrentKlasseId is Guid klasseId
            ? await db.Klassen
                .AsNoTracking()
                .Where(klasse => klasse.Id == klasseId)
                .Select(klasse => klasse.Code + " · " + klasse.Title)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var faecher = await db.LehrerFachZuordnungen
            .AsNoTracking()
            .Where(assignment => assignment.LehrerId == userId)
            .Select(assignment => new UserProfileFachItem
            {
                FachId = assignment.FachId,
                Name = assignment.Fach.Name,
                SchoolId = assignment.Fach.SchoolId,
                SchoolName = assignment.Fach.School.Name
            })
            .ToListAsync(cancellationToken);

        return new UserProfileModel
        {
            UserId = user.Id,
            Identifiant = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            Vorname = user.Vorname,
            Nachname = user.Nachname,
            FullName = user.FullName,
            TelefonNummer = user.TelefonNummer,
            Geschlecht = user.Geschlecht,
            IsBlocked = user.IsBlocked,
            IsSelfRegistered = user.IsSelfRegistered,
            CreatedAtUtc = user.CreatedAtUtc,
            UpdatedAtUtc = user.UpdatedAtUtc,
            ProfilBildAssetId = user.ProfilBildAssetId,
            ProfilBildUrl = user.ProfilBildAssetId.HasValue ? $"/media-library/{user.ProfilBildAssetId.Value}" : null,
            ElternTelefonNummer = studentMeta?.ElternTelefonNummer,
            ElternEmail = studentMeta?.ElternEmail,
            CurrentKlasseId = studentMeta?.CurrentKlasseId,
            CurrentKlasseAnzeige = klassenAnzeige,
            Rollen = rollen,
            Schulen = schulen,
            Faecher = faecher
        };
    }

    public async Task<IReadOnlyList<FachOptionItem>> GetTeacherFachOptionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var schoolIds = await db.UserSchools
            .AsNoTracking()
            .Where(link => link.AppUserId == userId)
            .Select(link => link.SchoolId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (schoolIds.Count == 0)
        {
            return [];
        }

        var faecher = await db.Faecher
            .AsNoTracking()
            .Where(fach => schoolIds.Contains(fach.SchoolId))
            .OrderBy(fach => fach.School.Name)
            .ThenBy(fach => fach.Name)
            .Select(fach => new FachOptionItem
            {
                FachId = fach.Id,
                SchoolId = fach.SchoolId,
                SchoolName = fach.School.Name,
                Name = fach.Name
            })
            .ToListAsync(cancellationToken);

        return faecher;
    }

    public async Task<(bool Succeeded, string Message, Guid? FachId)> AddTeacherFachAsync(Guid userId, string fachName, CancellationToken cancellationToken = default)
    {
        var normalized = fachName.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return (false, "Veuillez saisir une matière.", null);
        }

        var normalizedKey = normalized.ToUpperInvariant();

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var schoolIds = await db.UserSchools
            .AsNoTracking()
            .Where(link => link.AppUserId == userId)
            .Select(link => link.SchoolId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (schoolIds.Count == 0)
        {
            return (false, "Aucune école associée à ce profil.", null);
        }

        var zielSchoolId = schoolIds[0];
        var fach = await db.Faecher
            .FirstOrDefaultAsync(
                item => item.SchoolId == zielSchoolId && item.Name.ToUpper() == normalizedKey,
                cancellationToken);
        if (fach is null)
        {
            fach = new Fach { SchoolId = zielSchoolId, Name = normalized };
            await db.Faecher.AddAsync(fach, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        var alreadyAssigned = await db.LehrerFachZuordnungen
            .AnyAsync(assignment => assignment.LehrerId == userId && assignment.FachId == fach.Id, cancellationToken);
        if (!alreadyAssigned)
        {
            await db.LehrerFachZuordnungen.AddAsync(new LehrerFachZuordnung
            {
                LehrerId = userId,
                FachId = fach.Id
            }, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return (true, "La matière a été ajoutée.", fach.Id);
    }

    public async Task<(bool Succeeded, string Message)> UpdateProfileAsync(
        Guid userId,
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return (false, "Utilisateur introuvable.");
        }

        if (request.ProfilBildAssetId.HasValue)
        {
            await using var validationDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var ownsImage = await validationDb.BlobMediaAssets.AsNoTracking().AnyAsync(asset =>
                asset.Id == request.ProfilBildAssetId.Value && asset.OwnerId == userId
                && asset.MediaType == SkoulouApp.Core.DataModels.Lessons.BlobMediaType.Image
                && (asset.SchoolId.HasValue
                    ? validationDb.UserSchools.Any(link => link.AppUserId == userId && link.SchoolId == asset.SchoolId)
                    : !validationDb.UserSchools.Any(link => link.AppUserId == userId)), cancellationToken);
            if (!ownsImage)
            {
                return (false, "Cette photo de profil ne vous appartient pas ou ne correspond pas à votre école.");
            }
        }

        user.Vorname = request.Vorname.Trim();
        user.Nachname = request.Nachname.Trim();
        user.FullName = $"{request.Vorname.Trim()} {request.Nachname.Trim()}".Trim();
        user.TelefonNummer = NormalizeOptional(request.TelefonNummer);
        user.Geschlecht = request.Geschlecht;
        user.ProfilBildAssetId = request.ProfilBildAssetId;
        user.UpdatedAtUtc = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return (false, string.Join(" ", result.Errors.Select(x => x.Description)));
        }

        var isTeacher = await userManager.IsInRoleAsync(user, Rollen.Lehrer);
        if (isTeacher)
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var assignedSchoolIds = await db.UserSchools
                .AsNoTracking()
                .Where(link => link.AppUserId == userId)
                .Select(link => link.SchoolId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var validFachIds = await db.Faecher
                .AsNoTracking()
                .Where(fach => request.FachIds.Contains(fach.Id) && assignedSchoolIds.Contains(fach.SchoolId))
                .Select(fach => fach.Id)
                .Distinct()
                .ToListAsync(cancellationToken);

            var currentAssignments = await db.LehrerFachZuordnungen
                .Where(assignment => assignment.LehrerId == userId)
                .ToListAsync(cancellationToken);

            db.LehrerFachZuordnungen.RemoveRange(currentAssignments.Where(assignment => !validFachIds.Contains(assignment.FachId)));

            var existingIds = currentAssignments.Select(assignment => assignment.FachId).ToHashSet();
            foreach (var fachId in validFachIds.Where(fachId => !existingIds.Contains(fachId)))
            {
                db.LehrerFachZuordnungen.Add(new LehrerFachZuordnung
                {
                    LehrerId = userId,
                    FachId = fachId
                });
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return (true, "Ton profil a été mis à jour.");
    }

    // ----------------------------------------------------------------------
    // Willkommensmail
    // ----------------------------------------------------------------------

    /// <summary>
    /// Sendet nach der Registrierung eine Willkommens-/Bestätigungsmail.
    /// Der Inhalt richtet sich nach der Rolle des Kontos (SuperAdmin, SchulLeiter,
    /// Lehrer und Schüler).
    /// Best-Effort: Schlägt der Versand fehl (z. B. weil die Adresse nicht existiert,
    /// von Postmark als "Inactive recipient" markiert ist oder das Kontingent erschöpft ist),
    /// wird der Fehler nur protokolliert. Die Registrierung bleibt davon unberührt.
    /// </summary>
    /// <summary>Willkommensmail an die (optionale) Kontaktadresse des Kontos, mit Identifiant.</summary>
    public async Task SendWelcomeEmailAsync(string identifiant, CancellationToken cancellationToken = default)
    {
        var (email, vorname, rolle, schulName) = await LadeEmpfaengerKontextAsync(identifiant, cancellationToken);
        if (email is null || !IstPlausibleEmail(email))
        {
            return;
        }

        var (betreff, titel, inhalt) = BaueWillkommensInhalt(vorname, rolle, schulName);
        inhalt = $"<p>Votre identifiant de connexion : <strong>{System.Net.WebUtility.HtmlEncode(identifiant)}</strong></p>" + inhalt;
        var body = EmailTemplates.Wrap(titel, inhalt, $"Votre compte SKOULOU est prêt, {vorname}.");

        try
        {
            var result = await emailService.SendAsync(
                new EmailRecipient(email, vorname),
                new EmailMessage
                {
                    Subject = betreff,
                    HtmlBody = body,
                    TextBody = EmailTemplates.ToPlainText(body)
                },
                cancellationToken);

            if (!result.Succeeded)
            {
                logger.LogWarning("Willkommensmail an {Email} konnte nicht gesendet werden: {Error}", email, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            // Der E-Mail-Versand darf die Registrierung niemals scheitern lassen.
            logger.LogWarning(ex, "Willkommensmail an {Email} fehlgeschlagen und wird ignoriert.", email);
        }
    }

    /// <summary>
    /// Ermittelt Rolle und Schulname des Empfängers, damit die
    /// Willkommensmail passende Abo-Hinweise enthalten kann.
    /// </summary>
    private async Task<(string? Email, string Vorname, string? Rolle, string? SchulName)> LadeEmpfaengerKontextAsync(
        string identifiant, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var normalisiert = identifiant.Trim().ToUpperInvariant();
            var user = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.NormalizedUserName == normalisiert, cancellationToken);

            if (user is null)
            {
                return (null, string.Empty, null, null);
            }

            var rollen = await (
                from ur in db.UserRoles
                join r in db.Roles on ur.RoleId equals r.Id
                where ur.UserId == user.Id
                select r.Name!).ToListAsync(cancellationToken);

            // Höchste Rolle gewinnt, damit z. B. ein SchulLeiter mit Lehrer-Rolle
            // die Schulleiter-Variante der Mail erhält.
            var rolle = new[] { Rollen.SuperAdmin, Rollen.SchulLeiter, Rollen.Lehrer, Rollen.Schueler }
                .FirstOrDefault(rollen.Contains);

            var schule = await (
                from us in db.UserSchools
                join s in db.Schools on us.SchoolId equals s.Id
                where us.AppUserId == user.Id
                select new { s.Name }).FirstOrDefaultAsync(cancellationToken);

            return (user.Email, user.Vorname, rolle, schule?.Name);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Empfängerkontext für die Willkommensmail an {Identifiant} konnte nicht geladen werden.", identifiant);
            return (null, string.Empty, null, null);
        }
    }

    /// <summary>
    /// Baut Betreff, Überschrift und HTML-Inhalt der Willkommensmail
    /// abhängig von der Rolle.
    /// </summary>
    private (string Subject, string Heading, string Html) BaueWillkommensInhalt(
        string vorname, string? rolle, string? schulName)
    {
        var name = System.Net.WebUtility.HtmlEncode(vorname);
        var schule = string.IsNullOrWhiteSpace(schulName)
            ? null
            : System.Net.WebUtility.HtmlEncode(schulName);

        var loginUrl = BuildAppUrl("/auth/login");
        var gruss = $"<p>Bonjour {name},</p>";
        var schulZeile = schule is null
            ? string.Empty
            : $"<p>Ton compte est rattaché à l'école <strong>{schule}</strong>.</p>";
        var abschluss = "<p>À bientôt,<br/>L'équipe SKOULOUAPP</p>";
        var button = EmailTemplates.Button(loginUrl, "Accéder à SKOULOUAPP");

        var adminHinweis = EmailTemplates.InfoBox(
            "Tu peux gérer les écoles, valider les comptes des directeurs, suivre les abonnements "
            + "<strong>SKOULOUAPPABO</strong> ainsi que la facturation de toute la plateforme.");
        var directionHinweis = EmailTemplates.InfoBox("""
            <strong>Tout ton établissement, dans un seul espace</strong><br/>
            Une fois ton compte validé, tu pourras organiser simplement le fonctionnement quotidien de ton école&nbsp;:
            <ul style="margin:12px 0 4px;padding-left:20px;">
                <li style="margin:6px 0;"><strong>Organisation scolaire&nbsp;:</strong> gérer les classes, les cycles, les enseignants, les élèves et leurs affectations.</li>
                <li style="margin:6px 0;"><strong>Frais scolaires&nbsp;:</strong> enregistrer les montants dus, les paiements comptants ou échelonnés et suivre immédiatement les soldes restant à payer.</li>
                <li style="margin:6px 0;"><strong>Finances de l'école&nbsp;:</strong> centraliser les recettes, les dépenses et les justificatifs, puis consulter des synthèses et rapports filtrés par période.</li>
                <li style="margin:6px 0;"><strong>SKOULOUAPPABO&nbsp;:</strong> attribuer l'abonnement aux élèves après le démarrage du cycle, enregistrer les paiements et suivre les commissions ainsi que les montants facturés.</li>
                <li style="margin:6px 0;"><strong>Pilotage clair&nbsp;:</strong> retrouver au même endroit les situations de paiement, les activités pédagogiques et les informations utiles à la direction.</li>
            </ul>
            """);
        var aboHinweisTeam = EmailTemplates.InfoBox(BaueAboHinweis(istSchueler: false));
        var schuelerHinweis = EmailTemplates.InfoBox("""
            <strong>Ton espace scolaire, toujours à portée de main</strong><br/>
            Dès que ton compte et ton accès de classe seront validés par ton école, tu pourras profiter d'un espace personnel pour apprendre et suivre ton parcours&nbsp;:
            <ul style="margin:12px 0 4px;padding-left:20px;">
                <li style="margin:6px 0;"><strong>Cours et supports&nbsp;:</strong> retrouver les leçons et documents publiés pour ta classe par tes enseignants.</li>
                <li style="margin:6px 0;"><strong>Exercices et examens&nbsp;:</strong> réaliser les activités qui te sont proposées et consulter les résultats disponibles.</li>
                <li style="margin:6px 0;"><strong>Progression personnelle&nbsp;:</strong> suivre tes leçons terminées, tes scores et tes dernières activités au même endroit.</li>
                <li style="margin:6px 0;"><strong>SKOULOUAPPABO&nbsp;:</strong> avec l'abonnement actif de ta classe, tu bénéficies des leçons, supports, exercices et examens autorisés. La direction attribue et suit cet abonnement pour chaque classe.</li>
                <li style="margin:6px 0;"><strong>Un accompagnement clair&nbsp;:</strong> pour toute question sur ton compte, ta classe ou un paiement, adresse-toi directement à la direction de ton école.</li>
            </ul>
            """);

        return rolle switch
        {
            Rollen.SuperAdmin => (
                "Ton accès administrateur SKOULOUAPP est prêt",
                "Bienvenue dans l'espace administrateur",
                $"""
                {gruss}
                <p>Ton compte <strong>administrateur</strong> SKOULOUAPP a été créé.</p>
                {adminHinweis}
                {button}
                {abschluss}
                """),

            Rollen.SchulLeiter => (
                "Ton école SKOULOUAPP est enregistrée",
                "Bienvenue, direction d'école",
                $"""
                {gruss}
                <p>Ton compte de <strong>directeur d'école</strong> a bien été créé.</p>
                {schulZeile}
                <p>Il sera activé après validation par l'exploitant. Tu recevras une notification dès que tu pourras te connecter.</p>
                 <p>Ton futur espace de direction t'aidera à suivre l'administration, les paiements et les activités de ton établissement avec une vue claire et centralisée.</p>
                 {directionHinweis}
                {button}
                {abschluss}
                """),

            Rollen.Lehrer => (
                "Ton compte enseignant SKOULOUAPP est prêt",
                "Bienvenue dans ton espace enseignant",
                $"""
                {gruss}
                <p>Ton compte <strong>enseignant</strong> SKOULOUAPP a été créé.</p>
                {schulZeile}
                {aboHinweisTeam}
                {button}
                {abschluss}
                """),

            Rollen.Schueler => (
                "Bienvenue chez SKOULOUAPP",
                "Bienvenue dans ton espace d'apprentissage",
                $"""
                {gruss}
                <p>Merci pour ton inscription à <strong>SKOULOUAPP</strong> ! Ton compte d'élève a bien été créé.</p>
                {schulZeile}
                <p>Il sera activé après validation. Tu pourras te connecter dès que ton compte sera activé.</p>
                 <p>Une fois ton accès validé, ton espace personnel réunira les contenus et informations utiles à ton apprentissage.</p>
                 {schuelerHinweis}
                {button}
                {abschluss}
                """),

            _ => (
                "Bienvenue chez SKOULOUAPP",
                "Bienvenue chez SKOULOUAPP",
                $"""
                {gruss}
                <p>Merci pour ton inscription à <strong>SKOULOUAPP</strong> ! Ton compte a bien été créé.</p>
                {schulZeile}
                <p>Il sera activé après validation. Tu recevras une notification dès que tu pourras te connecter.</p>
                {button}
                {abschluss}
                """)
        };
    }

    /// <summary>
    /// Erklärt das einheitliche Klassenabo für Lehrer und Schüler.
    /// </summary>
    private static string BaueAboHinweis(bool istSchueler)
    {
        var anrede = istSchueler ? "tu bénéficies" : "tes élèves bénéficient";
        var besitz = istSchueler ? "ton enseignant" : "l'enseignant";

        return $"""
            <strong>SKOULOUAPPABO</strong><br/>
            Avec l'abonnement actif de la classe, {anrede} des leçons, supports, exercices et examens publiés par {besitz}. La direction attribue et suit cet abonnement pour chaque classe.
            """;
    }

    /// <summary>Grobe Syntaxprüfung, damit offensichtlich ungültige Adressen gar nicht erst versendet werden.</summary>
    private static bool IstPlausibleEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var at = email.IndexOf('@');
        return at > 0 && at < email.Length - 1 && email.LastIndexOf('.') > at;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    // ----------------------------------------------------------------------
    // Kontaktformular (öffentlich)
    // ----------------------------------------------------------------------

    public async Task<(bool Succeeded, string Message)> SendContactMessageAsync(
        string senderName, string senderEmail, string subject, string messageBody, CancellationToken cancellationToken = default)
    {
        var normalizedSenderName = (senderName ?? string.Empty).Trim();
        var normalizedSenderEmail = (senderEmail ?? string.Empty).Trim();
        var normalizedSubject = (subject ?? string.Empty).Trim();

        var supportEmail = postmarkOptions.Value.SupportEmail;
        if (string.IsNullOrWhiteSpace(supportEmail))
        {
            supportEmail = postmarkOptions.Value.FromEmail;
        }

        if (string.IsNullOrWhiteSpace(supportEmail))
        {
            logger.LogWarning("Kontaktformular: Keine Support- oder From-E-Mail konfiguriert.");
            return (false, "Le formulaire de contact n'est pas encore configuré.");
        }

        var sanitizedMessage = HtmlSanitizer.Sanitize(messageBody);
        var plainMessage = HtmlSanitizer.ToPlainText(sanitizedMessage);
        if (plainMessage.Length < 10)
        {
            logger.LogInformation("Kontaktformular abgelehnt: Nachricht zu kurz (Length={Length}).", plainMessage.Length);
            return (false, "Le message doit contenir au moins 10 caractères.");
        }

        var html = EmailTemplates.Wrap(
            "Nouveau message de contact",
            $"""
            <p><strong>Nom :</strong> {System.Net.WebUtility.HtmlEncode(normalizedSenderName)}</p>
            <p><strong>E-mail :</strong> {System.Net.WebUtility.HtmlEncode(normalizedSenderEmail)}</p>
            <p><strong>Sujet :</strong> {System.Net.WebUtility.HtmlEncode(normalizedSubject)}</p>
            <hr/>
            <div>{sanitizedMessage}</div>
            """);

        logger.LogInformation("Kontaktformular: Versand an Support wird gestartet (To={SupportEmail}).", supportEmail);
        var result = await emailService.SendAsync(
            new EmailRecipient(supportEmail),
            new EmailMessage
            {
                Subject = $"[Contact] {normalizedSubject}",
                HtmlBody = html,
                TextBody = $"Nom: {normalizedSenderName}\nE-mail: {normalizedSenderEmail}\nSujet: {normalizedSubject}\n\n{plainMessage}",
                ReplyTo = string.IsNullOrWhiteSpace(normalizedSenderEmail) ? null : normalizedSenderEmail
            },
            cancellationToken);

        if (!result.Succeeded)
        {
            logger.LogWarning("Kontaktformular: Versand fehlgeschlagen: {Error}", result.ErrorMessage);
        }

        return result.Succeeded
            ? (true, "Merci ! Ton message a bien été envoyé.")
            : (false, "L'envoi a échoué. Merci de réessayer plus tard.");
    }

    private string BuildAppUrl(string relativePath)
        => $"{BaseUrl}/{relativePath.TrimStart('/')}";

    private string BaseUrl => (configuration["App:BaseUrl"] ?? "https://localhost:7064").TrimEnd('/');
}

