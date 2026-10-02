using SkoulouApp.Core.DataModels.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace SkoulouApp.Tests.Infrastructure;

/// <summary>
/// Test-Double für <see cref="UserManager{AppUser}"/>. Verwaltet Benutzer und
/// Rollenzuordnungen im Speicher und überschreibt nur die Methoden, die von den
/// Core-Services verwendet werden.
/// </summary>
public class TestUserManager : UserManager<AppUser>
{
    private readonly List<AppUser> _users;
    private readonly Dictionary<Guid, HashSet<string>> _roles = new();

    public Action<AppUser>? OnUserCreated { get; set; }
    public Action<AppUser>? OnUserUpdated { get; set; }
    public Action<AppUser>? OnUserDeleted { get; set; }
    public IdentityResult ChangePasswordResult { get; set; } = IdentityResult.Success;
    public IdentityResult ResetPasswordResult { get; set; } = IdentityResult.Success;

    private TestUserManager(List<AppUser> users, IUserStore<AppUser> store)
        : base(
            store,
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<AppUser>(),
            Array.Empty<IUserValidator<AppUser>>(),
            Array.Empty<IPasswordValidator<AppUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services: null!,
            logger: null!)
    {
        _users = users;
    }

    public static TestUserManager Create(IEnumerable<AppUser>? users = null)
    {
        var list = users?.ToList() ?? new List<AppUser>();
        var store = new TestUserStore(list);
        return new TestUserManager(list, store);
    }

    /// <summary>Weist einem vorhandenen Benutzer eine Rolle zu (Test-Setup).</summary>
    public void SeedRole(AppUser user, string role)
    {
        if (!_roles.TryGetValue(user.Id, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _roles[user.Id] = set;
        }

        set.Add(role);
    }

    public override Task<IList<AppUser>> GetUsersInRoleAsync(string roleName)
    {
        IList<AppUser> result = _users
            .Where(u => _roles.TryGetValue(u.Id, out var set) && set.Contains(roleName))
            .ToList();

        return Task.FromResult(result);
    }

    public override Task<IList<string>> GetRolesAsync(AppUser user)
    {
        IList<string> result = _roles.TryGetValue(user.Id, out var set)
            ? set.ToList()
            : new List<string>();

        return Task.FromResult(result);
    }

    public override Task<AppUser?> FindByIdAsync(string userId)
        => Task.FromResult(_users.FirstOrDefault(x => x.Id.ToString() == userId));

    public override Task<AppUser?> FindByEmailAsync(string email)
        => Task.FromResult(_users.FirstOrDefault(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase)));

    public override Task<IdentityResult> ChangePasswordAsync(AppUser user, string currentPassword, string newPassword)
        => Task.FromResult(ChangePasswordResult);

    public override Task<IdentityResult> ResetPasswordAsync(AppUser user, string token, string newPassword)
        => Task.FromResult(ResetPasswordResult);

    public override Task<IdentityResult> CreateAsync(AppUser user, string password)
    {
        if (user.Id == Guid.Empty)
        {
            user.Id = Guid.NewGuid();
        }

        if (!_users.Contains(user))
        {
            _users.Add(user);
        }

        OnUserCreated?.Invoke(user);

        return Task.FromResult(IdentityResult.Success);
    }

    public override Task<IdentityResult> UpdateAsync(AppUser user)
    {
        OnUserUpdated?.Invoke(user);
        return Task.FromResult(IdentityResult.Success);
    }

    public override Task<IdentityResult> AddToRoleAsync(AppUser user, string role)
    {
        SeedRole(user, role);
        return Task.FromResult(IdentityResult.Success);
    }

    public override Task<IdentityResult> AddToRolesAsync(AppUser user, IEnumerable<string> roles)
    {
        foreach (var role in roles)
        {
            SeedRole(user, role);
        }

        return Task.FromResult(IdentityResult.Success);
    }

    public override Task<IdentityResult> RemoveFromRolesAsync(AppUser user, IEnumerable<string> roles)
    {
        if (_roles.TryGetValue(user.Id, out var set))
        {
            foreach (var role in roles)
            {
                set.Remove(role);
            }
        }

        return Task.FromResult(IdentityResult.Success);
    }

    public override Task<bool> IsInRoleAsync(AppUser user, string role)
        => Task.FromResult(_roles.TryGetValue(user.Id, out var roles) && roles.Contains(role));

    public override Task<IdentityResult> DeleteAsync(AppUser user)
    {
        OnUserDeleted?.Invoke(user);
        _roles.Remove(user.Id);
        _users.Remove(user);
        return Task.FromResult(IdentityResult.Success);
    }
}
