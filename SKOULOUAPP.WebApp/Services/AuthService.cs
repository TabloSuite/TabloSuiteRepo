using HalloApp.Core.Options;
using HalloApp.Core.Models.Profile;
using HalloApp.Core.Models.TeacherLessons;
using HalloApp.Core.DataModels.Schools;
using HalloApp.Core.Services;
using HalloApp.Core.Services.Email;
using HalloApp.Core.Services.Text;
using HalloApp.WebApp.Data;
using HalloApp.WebApp.Models.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace HalloApp.WebApp.Services;

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

        if (string.IsNullOrWhiteSpace(input.SchulName))
        {
            return (false, "Veuillez indiquer le nom de ton école.");
        }

        // Eigener, kurzlebiger DbContext: verhindert Kollisionen mit dem geteilten
        // Circuit-DbContext (Blazor Server) während paralleler Abfragen.
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var schuleVorhanden = await db.Schools.AnyAsync(x => x.Name == input.SchulName, cancellationToken);
        if (schuleVorhanden)
        {
            return (false, "Une école portant ce nom existe déjà.");
        }

        var schule = new School
        {
            Name = input.SchulName.Trim(),
            City = input.SchulStadt.Trim(),
            Country = string.IsNullOrWhiteSpace(input.SchulLand) ? "Togo" : input.SchulLand.Trim(),
            IsActive = true
        };
        db.Schools.Add(schule);
        await db.SaveChangesAsync(cancellationToken);

        var user = new AppUser
        {
            UserName = input.Email,
            Email = input.Email,
            Vorname = input.Vorname,
            Nachname = input.Nachname,
            FullName = input.FullName,
            TelefonNummer = NormalizeOptional(input.TelefonNummer),
            Geschlecht = input.Geschlecht,
            GeburtsdatumUtc = input.Geburtsdatum.HasValue ? DateTime.SpecifyKind(input.Geburtsdatum.Value, DateTimeKind.Utc) : null,
            AgbVersion = AktuelleAgbVersion,
            AgbAcceptedAtUtc = DateTime.UtcNow,
            EmailConfirmed = true,
            IsSelfRegistered = true,
            IsBlocked = true
        };

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
        await SendWelcomeEmailAsync(user.Email!, user.Vorname, cancellationToken);
        return (true, "Ton école et ton compte de directeur ont été créés et attendent la validation par l'exploitant.");
    }

    public async Task<(bool Succeeded, string ErrorMessage)> RegisterStudentAsync(RegisterInputModel input, CancellationToken cancellationToken = default)
    {
        if (!input.AgbAccepted)
        {
            return (false, "Veuillez accepter les conditions d'utilisation avant de t'inscrire.");
        }

        if (input.SelectedSchoolId is null)
        {
            return (false, "Veuillez choisir une école.");
        }

        // Eigener, kurzlebiger DbContext: verhindert Kollisionen mit dem geteilten
        // Circuit-DbContext (Blazor Server) während paralleler Abfragen.
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var school = await db.Schools.FirstOrDefaultAsync(x => x.Id == input.SelectedSchoolId.Value && x.IsActive, cancellationToken);
        if (school is null)
        {
            return (false, "L'école sélectionnée est introuvable.");
        }

        var user = new AppUser
        {
            UserName = input.Email,
            Email = input.Email,
            Vorname = input.Vorname,
            Nachname = input.Nachname,
            FullName = input.FullName,
            TelefonNummer = NormalizeOptional(input.TelefonNummer),
            Geschlecht = input.Geschlecht,
            GeburtsdatumUtc = input.Geburtsdatum.HasValue ? DateTime.SpecifyKind(input.Geburtsdatum.Value, DateTimeKind.Utc) : null,
            AgbVersion = AktuelleAgbVersion,
            AgbAcceptedAtUtc = DateTime.UtcNow,
            EmailConfirmed = true,
            IsSelfRegistered = true,
            IsBlocked = true
        };

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
        await SendWelcomeEmailAsync(user.Email!, user.Vorname, cancellationToken);
        return (true, "Ton compte a été créé et attend la validation par l'école.");
    }

    public async Task<(bool Succeeded, string ErrorMessage)> PasswordSignInAsync(string email, string password, bool rememberMe)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return (false, "L'adresse e-mail ou le mot de passe est invalide.");
        }

        if (user.IsBlocked)
        {
            return (false, "Ton compte n'a pas encore été validé ou a été bloqué.");
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false);
        if (!result.Succeeded)
        {
            return (false, "L'adresse e-mail ou le mot de passe est invalide.");
        }

        user.ActiveSessionId = Guid.NewGuid();
        user.UpdatedAtUtc = DateTime.UtcNow;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return (false, "La connexion n'a pas pu être initialisée.");
        }

        var claims = new[] { new System.Security.Claims.Claim("halloapp_session", user.ActiveSessionId.Value.ToString()) };
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
    public async Task<(bool Succeeded, string Message)> SendPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        const string neutral = "Si un compte existe pour cette adresse, un e-mail de réinitialisation a été envoyé.";

        var normalizedEmail = email.Trim();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return (true, neutral);
        }

        var user = await userManager.FindByEmailAsync(normalizedEmail);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return (true, neutral);
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var encoded = Uri.EscapeDataString(token);
        var resetUrl = $"{BaseUrl}/auth/reset-password?email={Uri.EscapeDataString(user.Email)}&token={encoded}";

        var body = EmailTemplates.Wrap(
            "Réinitialisation du mot de passe",
            $"""
            <p>Bonjour {System.Net.WebUtility.HtmlEncode(user.Vorname)},</p>
            <p>Tu as demandé la réinitialisation de ton mot de passe. Clique sur le bouton ci-dessous pour en choisir un nouveau :</p>
            <p style="text-align:center;margin:32px 0;">
                <a href="{resetUrl}" style="background:#7c3aed;color:#fff;padding:12px 28px;border-radius:9999px;text-decoration:none;font-weight:600;">Réinitialiser le mot de passe</a>
            </p>
            <p>Si tu n'es pas à l'origine de cette demande, tu peux ignorer cet e-mail.</p>
            """);

        try
        {
            var result = await emailService.SendAsync(
                new EmailRecipient(user.Email, user.FullName),
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
    public async Task<(bool Succeeded, string Message)> ResetPasswordAsync(string email, string token, string newPassword)
    {
        var normalizedEmail = email.Trim();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return (false, "Le lien de réinitialisation est invalide ou a expiré.");
        }

        var user = await userManager.FindByEmailAsync(normalizedEmail);
        if (user is null)
        {
            return (false, "Le lien de réinitialisation est invalide ou a expiré.");
        }

        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        return result.Succeeded
            ? (true, "Ton mot de passe a été réinitialisé. Tu peux maintenant te connecter.")
            : (false, string.Join(" ", result.Errors.Select(x => x.Description)));
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
        return result.Succeeded
            ? (true, "Ton mot de passe a été mis à jour.")
            : (false, string.Join(" ", result.Errors.Select(x => x.Description)));
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
                && asset.MediaType == HalloApp.Core.DataModels.Lessons.BlobMediaType.Image
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
    public async Task SendWelcomeEmailAsync(string email, string vorname, CancellationToken cancellationToken = default)
    {
        if (!IstPlausibleEmail(email))
        {
            logger.LogWarning("Willkommensmail übersprungen: '{Email}' ist keine plausible E-Mail-Adresse.", email);
            return;
        }

        var (rolle, schulName) = await LadeEmpfaengerKontextAsync(email, cancellationToken);

        var (betreff, titel, inhalt) = BaueWillkommensInhalt(vorname, rolle, schulName);
        var body = EmailTemplates.Wrap(titel, inhalt, $"Ton compte SKOULOUAPP est prêt, {vorname}.");

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
    private async Task<(string? Rolle, string? SchulName)> LadeEmpfaengerKontextAsync(
        string email, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var user = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Email == email, cancellationToken);

            if (user is null)
            {
                return (null, null);
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

            return (rolle, schule?.Name);
        }
        catch (Exception ex)
        {
            // Ohne Kontext wird lediglich die neutrale Variante der Mail versendet.
            logger.LogWarning(ex, "Empfängerkontext für die Willkommensmail an {Email} konnte nicht geladen werden.", email);
            return (null, null);
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

        var loginUrl = $"{BaseUrl}/login";
        var gruss = $"<p>Bonjour {name},</p>";
        var schulZeile = schule is null
            ? string.Empty
            : $"<p>Ton compte est rattaché à l'école <strong>{schule}</strong>.</p>";
        var abschluss = "<p>À bientôt,<br/>L'équipe SKOULOUAPP</p>";
        var button = EmailTemplates.Button(loginUrl, "Accéder à SKOULOUAPP");

        var adminHinweis = EmailTemplates.InfoBox(
            "Tu peux gérer les écoles, valider les comptes des directeurs, suivre les abonnements "
            + "<strong>SKOULOUAPPABO</strong> ainsi que la facturation de toute la plateforme.");
        var aboHinweisTeam = EmailTemplates.InfoBox(BaueAboHinweis(istSchueler: false));
        var aboHinweisSchueler = EmailTemplates.InfoBox(BaueAboHinweis(istSchueler: true));

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
                {aboHinweisTeam}
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
                {aboHinweisSchueler}
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

    private string BaseUrl => (configuration["App:BaseUrl"] ?? "https://localhost:7064").TrimEnd('/');
}

