using Microsoft.AspNetCore.Identity;

namespace ENote.Core.DataModels.Identity;

public class Role : IdentityRole<Guid>
{
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = ENote.Core.Services.AppUhr.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<AppUserRole> UserRoles { get; set; } = new List<AppUserRole>();
}
