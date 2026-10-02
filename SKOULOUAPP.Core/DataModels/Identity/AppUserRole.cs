using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.DataModels.Exercises;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace SkoulouApp.Core.DataModels.Identity;

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






