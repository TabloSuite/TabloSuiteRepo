using System.Security.Claims;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;
using Serilog.Core;
using Serilog.Events;

namespace ENote.WebApp.Services;

/// <summary>
/// Speichert Serilog-Ereignisse mit Exception als administrierbare ExceptionLogs.
/// Fehler beim Protokollieren werden absichtlich geschluckt, damit das Logging
/// keinen Folgefehler auslöst.
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
            var benutzerId = Guid.TryParse(httpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
                ? id
                : (Guid?)null;

            using var db = dbContextFactory.CreateDbContext();
            var salonId = benutzerId is null
                ? null
                : db.UserSalons
                    .Where(x => x.AppUserId == benutzerId.Value)
                    .Select(x => (Guid?)x.SalonId)
                    .FirstOrDefault();

            db.ExceptionLogs.Add(new Core.DataModels.Infrastruktur.ExceptionLog
            {
                ZeitUtc = ENote.Core.Services.AppUhr.UtcNow,
                Nachricht = logEvent.RenderMessage(),
                Ausnahme = logEvent.Exception.ToString(),
                Pfad = httpContext?.Request.Path.Value,
                BenutzerId = benutzerId,
                SalonId = salonId
            });
            db.SaveChanges();
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
