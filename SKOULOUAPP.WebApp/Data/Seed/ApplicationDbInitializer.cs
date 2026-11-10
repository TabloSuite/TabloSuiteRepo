using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace SkoulouApp.WebApp.Data.Seed;

public static class ApplicationDbInitializer
{
    public static async Task SeedAsync(
        ApplicationDbContext dbContext,
        RoleManager<Role> roleManager,
        UserManager<AppUser> userManager,
        bool applyMigrations = false)
    {
        if (applyMigrations)
        {
            await dbContext.Database.MigrateAsync();
        }

        await SeedPermissionsAndRolesAsync(dbContext, roleManager);
        await SeedSuperAdminAsync(userManager);
    }

    private static async Task SeedPermissionsAndRolesAsync(ApplicationDbContext dbContext, RoleManager<Role> roleManager)
    {
        var permissions = CreatePermissions();

        foreach (var permission in permissions)
        {
            if (!await dbContext.Permissions.AnyAsync(x => x.Name == permission.Name))
            {
                dbContext.Permissions.Add(permission);
            }
        }

        await dbContext.SaveChangesAsync();

        var storedPermissions = await dbContext.Permissions.ToListAsync();
        var roles = CreateRoles(storedPermissions);

        foreach (var role in roles)
        {
            var desiredPermissionIds = role.RolePermissions
                .Select(x => x.PermissionId)
                .ToList();

            var existingRole = await roleManager.FindByNameAsync(role.Name!);
            if (existingRole is null)
            {
                var roleToCreate = new Role
                {
                    Name = role.Name,
                    NormalizedName = role.NormalizedName,
                    Description = role.Description
                };

                await roleManager.CreateAsync(roleToCreate);
                existingRole = await roleManager.FindByNameAsync(role.Name!);
            }

            if (existingRole is null)
            {
                continue;
            }

            existingRole.Description = role.Description;
            existingRole.UpdatedAtUtc = DateTime.UtcNow;

            var existingPermissionLinks = await dbContext.RolePermissions
                .Where(x => x.RoleId == existingRole.Id)
                .ToListAsync();

            dbContext.RolePermissions.RemoveRange(existingPermissionLinks);

            foreach (var permissionId in desiredPermissionIds)
            {
                dbContext.RolePermissions.Add(new RolePermission
                {
                    RoleId = existingRole.Id,
                    PermissionId = permissionId
                });
            }
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedSuperAdminAsync(UserManager<AppUser> userManager)
    {
        // Es werden bewusst keine Schulen und keine weiteren Benutzer angelegt.
        // Der SuperAdmin ist der einzige Seed-Benutzer; Schulen, Schulleiter,
        // Lehrer und Schueler werden ausschliesslich ueber die Anwendung erfasst.
        await EnsureUserAsync(
            userManager,
            email: "dodji_etse@web.de",
            fullName: "Dodji Etse",
            password: "Admin123!",
            isSelfRegistered: false,
            isBlocked: false,
            roles: [Rollen.SuperAdmin],
            geburtsdatumUtc: new DateTime(1985, 5, 12, 0, 0, 0, DateTimeKind.Utc));
    }

    private static async Task<AppUser> EnsureUserAsync(
        UserManager<AppUser> userManager,
        string email,
        string fullName,
        string password,
        bool isSelfRegistered,
        bool isBlocked,
        IReadOnlyCollection<string> roles,
        DateTime? geburtsdatumUtc = null)
    {
        // Identifiant des Start-Admins ist die konfigurierte Adresse (E-Mails sind nicht mehr eindeutig).
        var user = await userManager.FindByNameAsync(email);
        if (user is null)
        {
            user = new AppUser
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                EmailConfirmed = true,
                IsSelfRegistered = isSelfRegistered,
                IsBlocked = isBlocked,
                GeburtsdatumUtc = geburtsdatumUtc
            };

            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException($"L'utilisateur d'initialisation '{email}' n'a pas pu être créé : {string.Join(", ", createResult.Errors.Select(x => x.Description))}");
            }
        }
        else
        {
            user.FullName = fullName;
            user.UserName = email;
            user.Email = email;
            user.EmailConfirmed = true;
            user.IsSelfRegistered = isSelfRegistered;
            user.GeburtsdatumUtc ??= geburtsdatumUtc;
            // IsBlocked wird bei bereits existierenden Nutzern bewusst NICHT überschrieben,
            // damit manuelle Freigaben/Blockierungen durch Admins beim erneuten Seeding
            // (z. B. bei jedem App-Start) nicht wieder zurückgesetzt werden.
            user.UpdatedAtUtc = DateTime.UtcNow;
            await userManager.UpdateAsync(user);
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        var rolesToRemove = currentRoles.Where(x => !roles.Contains(x)).ToArray();
        if (rolesToRemove.Length > 0)
        {
            await userManager.RemoveFromRolesAsync(user, rolesToRemove);
        }

        var rolesToAdd = roles.Where(x => !currentRoles.Contains(x)).ToArray();
        if (rolesToAdd.Length > 0)
        {
            await userManager.AddToRolesAsync(user, rolesToAdd);
        }

        return user;
    }

    private static List<Permission> CreatePermissions()
    {
        return
        [
            // Nur der SuperAdmin darf Benutzer blockieren/freigeben/löschen.
            new() { Name = Berechtigungen.UsersBlock, Description = "Bloquer, débloquer et supprimer des utilisateurs (Administrateur uniquement)" },
            new() { Name = Berechtigungen.SchoolsManage, Description = "Créer et gérer les écoles et leurs tarifs" },
            new() { Name = Berechtigungen.TeachersManage, Description = "Gérer les enseignants et les affecter à son école" },
            new() { Name = Berechtigungen.StudentsApprove, Description = "Valider les inscriptions des élèves" },
            new() { Name = Berechtigungen.StudentsSubscription, Description = "Attribuer un abonnement et marquer un niveau comme payé" },
            new() { Name = Berechtigungen.PaymentsReverse, Description = "Annuler un paiement déjà validé (Administrateur uniquement)" },
            new() { Name = Berechtigungen.BillingManage, Description = "Consulter le chiffre d'affaires et clôturer les périodes de facturation" },
            new() { Name = Berechtigungen.CoursesManage, Description = "Gérer les cours, les leçons et les exercices" },
            new() { Name = Berechtigungen.ReportsView, Description = "Consulter la progression et les résultats" }
        ];
    }

    private static List<Role> CreateRoles(List<Permission> permissions)
    {
        var superAdmin = new Role
        {
            Name = Rollen.SuperAdmin,
            NormalizedName = "SUPERADMIN",
            Description = "Accès complet aux écoles, utilisateurs, contenus, paiements et rapports"
        };

        var schulLeiter = new Role
        {
            Name = Rollen.SchulLeiter,
            NormalizedName = "SCHULLEITER",
            Description = "Dirige son école : gère enseignants et élèves, abonnements, paiements et facturation"
        };

        var koordinator = new Role
        {
            Name = Rollen.Koordinator,
            NormalizedName = "KOORDINATOR",
            Description = "Gère les écoles qui lui sont attribuées sans accès aux finances ni à la facturation"
        };

        var produktOwner = new Role
        {
            Name = Rollen.ProduktOwner,
            NormalizedName = "PRODUKTOWNER",
            Description = "Gère la plateforme sans accès aux finances ni à la facturation"
        };

        var teacher = new Role
        {
            Name = Rollen.Lehrer,
            NormalizedName = "LEHRER",
            Description = "Gère les élèves et les contenus dans les écoles attribuées"
        };

        var student = new Role
        {
            Name = Rollen.Schueler,
            NormalizedName = "SCHÜLER",
            Description = "Accède aux cours, supports et activités de son établissement"
        };

        var eltern = new Role
        {
            Name = Rollen.Eltern,
            NormalizedName = "ELTERN",
            Description = "Parent ou tuteur : consulte les bulletins, présences et frais de ses enfants"
        };

        // SuperAdmin erhält alle Berechtigungen.
        foreach (var permission in permissions)
        {
            superAdmin.RolePermissions.Add(new RolePermission { Role = superAdmin, Permission = permission, PermissionId = permission.Id });
        }

        // SchulLeiter darf seine Schule verwalten, Lehrer anlegen, Schüler validieren,
        // Abos zuweisen/bezahlt markieren und die Abrechnung einsehen/abschließen –
        // aber KEINE Benutzer blockieren/löschen und KEINE Zahlungen rückgängig machen.
        foreach (var permission in permissions.Where(x => x.Name is
            Berechtigungen.TeachersManage or Berechtigungen.StudentsApprove or Berechtigungen.StudentsSubscription
            or Berechtigungen.BillingManage or Berechtigungen.CoursesManage or Berechtigungen.ReportsView))
        {
            schulLeiter.RolePermissions.Add(new RolePermission { Role = schulLeiter, Permission = permission, PermissionId = permission.Id });
        }

        foreach (var permission in permissions.Where(x => x.Name is
            Berechtigungen.TeachersManage or Berechtigungen.StudentsApprove or Berechtigungen.StudentsSubscription
            or Berechtigungen.CoursesManage or Berechtigungen.ReportsView))
        {
            koordinator.RolePermissions.Add(new RolePermission { Role = koordinator, Permission = permission, PermissionId = permission.Id });
        }

        foreach (var permission in permissions.Where(x => x.Name is not Berechtigungen.BillingManage))
        {
            produktOwner.RolePermissions.Add(new RolePermission { Role = produktOwner, Permission = permission, PermissionId = permission.Id });
        }

        // Lehrer sehen Inhalte/Rapports und dürfen Schüler betreuen.
        foreach (var permission in permissions.Where(x => x.Name is Berechtigungen.StudentsApprove or Berechtigungen.CoursesManage or Berechtigungen.ReportsView))
        {
            teacher.RolePermissions.Add(new RolePermission { Role = teacher, Permission = permission, PermissionId = permission.Id });
        }

        var reportPermission = permissions.Single(x => x.Name == Berechtigungen.ReportsView);
        student.RolePermissions.Add(new RolePermission { Role = student, Permission = reportPermission, PermissionId = reportPermission.Id });

        return [superAdmin, schulLeiter, koordinator, produktOwner, teacher, student, eltern];
    }
}

