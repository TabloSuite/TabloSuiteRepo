using SkoulouApp.Core.DataModels.Identity;
using Microsoft.AspNetCore.Identity;

namespace SkoulouApp.Tests.Infrastructure;

/// <summary>
/// Minimaler In-Memory-<see cref="IUserStore{TUser}"/>, damit ein
/// <see cref="UserManager{AppUser}"/> in Tests instanziiert werden kann.
/// </summary>
public class TestUserStore : IUserStore<AppUser>, IUserPasswordStore<AppUser>
{
    private readonly List<AppUser> _users;

    public TestUserStore(List<AppUser> users) => _users = users;

    public Task<string> GetUserIdAsync(AppUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.Id.ToString());

    public Task<string?> GetUserNameAsync(AppUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.UserName);

    public Task SetUserNameAsync(AppUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(AppUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(AppUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    public Task<IdentityResult> CreateAsync(AppUser user, CancellationToken cancellationToken)
    {
        if (user.Id == Guid.Empty)
        {
            user.Id = Guid.NewGuid();
        }

        _users.Add(user);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> UpdateAsync(AppUser user, CancellationToken cancellationToken)
        => Task.FromResult(IdentityResult.Success);

    public Task<IdentityResult> DeleteAsync(AppUser user, CancellationToken cancellationToken)
    {
        _users.Remove(user);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<AppUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
        => Task.FromResult(_users.FirstOrDefault(x => x.Id.ToString() == userId));

    public Task<AppUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
        => Task.FromResult(_users.FirstOrDefault(x => x.NormalizedUserName == normalizedUserName));

    public Task SetPasswordHashAsync(AppUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(AppUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(AppUser user, CancellationToken cancellationToken)
        => Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));

    public void Dispose()
    {
    }
}
