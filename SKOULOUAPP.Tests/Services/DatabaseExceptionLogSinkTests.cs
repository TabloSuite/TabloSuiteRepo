using HalloApp.Tests.Infrastructure;
using HalloApp.WebApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Xunit;

namespace HalloApp.Tests.Services;

public class DatabaseExceptionLogSinkTests
{
    [Fact]
    public async Task Emit_ProtokolliertNurFehlerMitException()
    {
        var factory = new TestApplicationDbContextFactory();
        using var logger = new LoggerConfiguration()
            .WriteTo.Sink(new DatabaseExceptionLogSink(factory, new HttpContextAccessor()))
            .CreateLogger();
        logger.Information("Information ohne Exception");
        logger.Error("Fehler ohne Exception");
        logger.Error(new InvalidOperationException("Testfehler"), "Fehler beim Rendern");

        await using var db = await factory.CreateDbContextAsync();
        var entry = await db.ExceptionLogs.SingleAsync();
        Assert.Contains("Fehler beim Rendern", entry.Message);
        Assert.Equal(typeof(InvalidOperationException).FullName, entry.ExceptionType);
    }
}
