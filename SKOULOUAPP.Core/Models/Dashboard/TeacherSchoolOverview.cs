namespace HalloApp.Core.Models.Dashboard;

public class TeacherSchoolOverview
{
    public Guid SchoolId { get; set; }
    public string SchoolName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public int StudentCount { get; set; }
    public int PendingApprovalCount { get; set; }
    public int BlockedStudentCount { get; set; }
    public int TeacherCount { get; set; }
    public decimal EffectiveSubscriptionPrice { get; set; }

}
