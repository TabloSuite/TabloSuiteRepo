using SkoulouApp.Core.DataModels.Schools;

namespace SkoulouApp.WebApp.Data;

public class ExceptionLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public string Message { get; set; } = string.Empty;
    public string? ExceptionType { get; set; }
    public string? StackTrace { get; set; }
    public string? RequestPath { get; set; }
    public Guid? UserId { get; set; }
    public Guid? SchoolId { get; set; }
    public School? School { get; set; }
    public bool IsHandled { get; set; }
    public DateTime? HandledAtUtc { get; set; }
    public Guid? HandledByUserId { get; set; }
}
