namespace SkoulouApp.Core.Models.Dashboard;

public class AdminSchoolOverview
{
    public Guid SchoolId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int TeacherCount { get; set; }
    public int StudentCount { get; set; }
}
