namespace HalloApp.Core.Models.Admin;

public class TeacherAssignmentOption
{
    public Guid TeacherId { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsAssigned { get; set; }
}
