using HalloApp.Core.DataModels.BaseModel;
using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
namespace HalloApp.Core.DataModels.Identity;

public class Permission : AuditableModel
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}







