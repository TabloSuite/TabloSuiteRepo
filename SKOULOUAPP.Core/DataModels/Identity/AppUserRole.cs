using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace HalloApp.Core.DataModels.Identity;

public class AppUserRole : IdentityUserRole<Guid>
{
    [NotMapped]
    public Guid AppUserId
    {
        get => UserId;
        set => UserId = value;
    }

    public AppUser AppUser { get; set; } = null!;
    public Role Role { get; set; } = null!;
}






