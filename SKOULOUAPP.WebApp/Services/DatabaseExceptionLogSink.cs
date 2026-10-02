using System.Security.Claims;
using SkoulouApp.WebApp.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Serilog.Core;
using Serilog.Events;
using System.Threading;

namespace SkoulouApp.WebApp.Services;

/// <summary>
/// Speichert Serilog-Ereignisse mit Exception als administrierbare ExceptionLogs.
/// Fehler beim Protokollieren werden absichtlich geschluckt, damit das Logging keinen Folgefehler auslöst.
/// </summary>
public sealed class DatabaseExceptionLogSink(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor) : ILogEventSink
{
    private static readonly AsyncLocal<bool> IsWritingLog = new();

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Exception is null || logEvent.Level < LogEventLevel.Error || IsWritingLog.Value)
        {
            return;
        }

        IsWritingLog.Value = true;

        try
        {
            var httpContext = httpContextAccessor.HttpContext;
            var userId = Guid.TryParse(httpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId)
                ? parsedUserId
                : (Guid?)null;

            using var dbContext = dbContextFactory.CreateDbContext();
            var schoolId = userId is null
                ? null
                : dbContext.UserSchools
                    .Where(x => x.AppUserId == userId.Value)
                    .Select(x => (Guid?)x.SchoolId)
                    .FirstOrDefault();

            dbContext.ExceptionLogs.Add(new ExceptionLog
            {
                OccurredAtUtc = DateTime.UtcNow,
                Message = logEvent.RenderMessage(),
                ExceptionType = logEvent.Exception.GetType().FullName,
                StackTrace = logEvent.Exception.ToString(),
                RequestPath = httpContext?.Request.Path.Value,
                UserId = userId,
                SchoolId = schoolId
            });
            dbContext.SaveChanges();
        }
        catch
        {
            // Logging darf den ursprünglichen Fehler nie verdecken.
        }
        finally
        {
            IsWritingLog.Value = false;
        }
    }
}
