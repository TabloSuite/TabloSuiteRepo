using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
namespace HalloApp.Core.DataModels.Schools;

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






