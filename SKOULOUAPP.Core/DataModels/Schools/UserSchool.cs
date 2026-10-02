using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.DataModels.Exercises;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
namespace SkoulouApp.Core.DataModels.Schools;

public class UserSchool
{
    public Guid AppUserId { get; set; }
    public AppUser AppUser { get; set; } = null!;

    public Guid SchoolId { get; set; }
    public School School { get; set; } = null!;

    public bool CanManageStudents { get; set; }
    public bool CanAccessFinances { get; set; }
    public bool CanViewFinanceDashboard { get; set; }
    public bool CanCreateFinanceEntries { get; set; }
    public bool CanManageCyclePayments { get; set; }
}






