namespace SkoulouApp.Core.Models.Dashboard;

public class SchoolUserItem
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public bool IsBlocked { get; set; }
    public bool IsSelfRegistered { get; set; }
    public List<string> Schools { get; set; } = [];
}
