using System.Security.Claims;
using SkoulouApp.WebApp.Components.Shared;
using SkoulouApp.WebApp.Data;
using SkoulouApp.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace SkoulouApp.WebApp.Components.Pages;

public partial class ExceptionLogs
{
    [Inject] private IDbContextFactory<ApplicationDbContext> DbContextFactory { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] private DatabaseActionGuard DatabaseActionGuard { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private readonly List<ExceptionLog> logs = [];
    private readonly List<SchoolFilterItem> schools = [];
    private DateTime? fromDate = DateTime.Today.AddDays(-7);
    private DateTime? toDate = DateTime.Today;
    private Guid? schoolId;
    private bool onlyUnhandled;
    private bool isLoading;
    private Guid? busyLogId;
    private Guid? copyingLogId;
    private Guid? copiedLogId;

    private IReadOnlyList<ModernDropdownOption<Guid?>> SchoolOptions =>
    [
        new(null, "Toutes les écoles"),
        .. schools.Select(school => new ModernDropdownOption<Guid?>((Guid?)school.Id, school.Name))
    ];

    private Task SetFromDateAsync(DateTime? value)
    {
        fromDate = value?.Date;
        return Task.CompletedTask;
    }

    private Task SetToDateAsync(DateTime? value)
    {
        toDate = value?.Date;
        return Task.CompletedTask;
    }

    private Task SetSchoolAsync(Guid? value)
    {
        schoolId = value;
        return Task.CompletedTask;
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadSchoolsAsync();
        await LoadLogsAsync();
    }

    private async Task LoadSchoolsAsync()
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync();
        schools.AddRange(await dbContext.Schools
            .AsNoTracking()
            .OrderBy(school => school.Name)
            .Select(school => new SchoolFilterItem(school.Id, school.Name))
            .ToListAsync());
    }

    private async Task ApplyFiltersAsync()
    {
        await DatabaseActionGuard.RunAsync(LoadLogsAsync);
    }

    private async Task ResetFiltersAsync()
    {
        fromDate = DateTime.Today.AddDays(-7);
        toDate = DateTime.Today;
        schoolId = null;
        onlyUnhandled = false;
        await ApplyFiltersAsync();
    }

    private async Task LoadLogsAsync()
    {
        isLoading = true;
        try
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync();
            var query = dbContext.ExceptionLogs
                .Include(log => log.School)
                .AsNoTracking()
                .AsQueryable();

            if (fromDate.HasValue)
            {
                query = query.Where(log => log.OccurredAtUtc >= fromDate.Value.Date);
            }

            if (toDate.HasValue)
            {
                query = query.Where(log => log.OccurredAtUtc < toDate.Value.Date.AddDays(1));
            }

            if (schoolId.HasValue)
            {
                query = query.Where(log => log.SchoolId == schoolId);
            }

            if (onlyUnhandled)
            {
                query = query.Where(log => !log.IsHandled);
            }

            logs.Clear();
            logs.AddRange(await query.OrderByDescending(log => log.OccurredAtUtc).Take(500).ToListAsync());
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task MarkHandledAsync(Guid logId)
    {
        await RunLogActionAsync(logId, async dbContext =>
        {
            var log = await dbContext.ExceptionLogs.FindAsync(logId);
            if (log is null)
            {
                return;
            }

            var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            log.IsHandled = true;
            log.HandledAtUtc = DateTime.UtcNow;
            log.HandledByUserId = Guid.TryParse(authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                ? userId
                : null;
            await dbContext.SaveChangesAsync();
        });
    }

    private async Task CopyLogAsync(ExceptionLog log)
    {
        if (copyingLogId.HasValue)
        {
            return;
        }

        copyingLogId = log.Id;
        try
        {
            var payload = BuildCopyPayload(log);
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", payload);
            copiedLogId = log.Id;
            _ = Task.Run(async () =>
            {
                await Task.Delay(1800);
                if (copiedLogId == log.Id)
                {
                    copiedLogId = null;
                    await InvokeAsync(StateHasChanged);
                }
            });
        }
        finally
        {
            copyingLogId = null;
        }
    }

    private static string GetHeaderTitle(ExceptionLog log)
    {
        if (string.IsNullOrWhiteSpace(log.Message))
        {
            return "Exception sans message";
        }

        var firstLine = log.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return "Exception sans message";
        }

        return firstLine.Length <= 120
            ? firstLine
            : $"{firstLine[..117]}...";
    }

    private static string BuildCopyPayload(ExceptionLog log)
    {
        return string.Join(Environment.NewLine, [
            $"Date: {log.OccurredAtUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}",
            $"École: {log.School?.Name ?? "École non attribuée"}",
            $"État: {(log.IsHandled ? "Traitée" : "À traiter")}",
            string.Empty,
            "Message:",
            log.Message ?? string.Empty,
            string.Empty,
            "Stack trace:",
            log.StackTrace ?? string.Empty
        ]);
    }

    private async Task DeleteAsync(Guid logId)
    {
        await RunLogActionAsync(logId, async dbContext =>
        {
            var log = await dbContext.ExceptionLogs.FindAsync(logId);
            if (log is null)
            {
                return;
            }

            dbContext.ExceptionLogs.Remove(log);
            await dbContext.SaveChangesAsync();
        });
    }

    private async Task RunLogActionAsync(Guid logId, Func<ApplicationDbContext, Task> action)
    {
        if (busyLogId.HasValue)
        {
            return;
        }

        busyLogId = logId;
        try
        {
            await DatabaseActionGuard.RunAsync(async () =>
            {
                await using var dbContext = await DbContextFactory.CreateDbContextAsync();
                await action(dbContext);
                await LoadLogsAsync();
            });
        }
        finally
        {
            busyLogId = null;
        }
    }

    private sealed record SchoolFilterItem(Guid Id, string Name);
}
