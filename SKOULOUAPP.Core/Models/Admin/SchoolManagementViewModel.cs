namespace SkoulouApp.Core.Models.Admin;

public class SchoolManagementViewModel
{
    public Guid SchoolId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int TeacherCount { get; set; }
    public int StudentCount { get; set; }
    public decimal OutstandingDebt { get; set; }
    public decimal EffectiveSubscriptionPrice { get; set; }
    public decimal? SubscriptionPriceOverride { get; set; }
    public List<TeacherAssignmentOption> DirectorAssignments { get; set; } = [];
    public List<TeacherAssignmentOption> TeacherAssignments { get; set; } = [];
}
