using Microsoft.AspNetCore.Identity;

namespace ENote.Core.DataModels.Identity;

public class AppUserRole : IdentityUserRole<Guid>
{
    public AppUser User { get; set; } = null!;
    public Role Role { get; set; } = null!;
}
