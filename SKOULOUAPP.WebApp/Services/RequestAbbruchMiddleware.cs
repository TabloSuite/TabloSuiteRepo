using Microsoft.AspNetCore.Http;

namespace SkoulouApp.WebApp.Services;

/// <summary>Ein vom Client abgebrochener Request ist kein interner Serverfehler.</summary>
public sealed class RequestAbbruchMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Keine Cookieänderung oder Fehlerseite an einen getrennten Client senden.
            // Andere Cancellation-/Timeoutfehler werden ausdrücklich weitergereicht.
            if (!context.Response.HasStarted)
                context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
        }
    }
}
