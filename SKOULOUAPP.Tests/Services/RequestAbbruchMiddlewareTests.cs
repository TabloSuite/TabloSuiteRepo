using Microsoft.AspNetCore.Http;
using SkoulouApp.WebApp.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

public sealed class RequestAbbruchMiddlewareTests
{
    [Theory]
    [InlineData("/favicon.svg")]
    [InlineData("/_blazor/disconnect")]
    public async Task ClientabbruchWirdAls499Behandelt(string path)
    {
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.RequestAborted = cancellation.Token;
        var middleware = new RequestAbbruchMiddleware(_ =>
        {
            cancellation.Cancel();
            throw new TaskCanceledException();
        });
        await middleware.InvokeAsync(context);
        Assert.Equal(499, context.Response.StatusCode);
    }

    [Fact]
    public async Task InterneCancellationOhneClientabbruchBleibtSichtbar()
    {
        var context = new DefaultHttpContext();
        var middleware = new RequestAbbruchMiddleware(_ => throw new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => middleware.InvokeAsync(context));
    }

    [Fact]
    public async Task EchterFehlerBleibtAuchBeiClientabbruchSichtbar()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        var middleware = new RequestAbbruchMiddleware(_ => throw new InvalidOperationException("Database failure"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
    }
}
