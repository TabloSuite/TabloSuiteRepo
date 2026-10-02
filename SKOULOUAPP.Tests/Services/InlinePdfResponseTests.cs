using SkoulouApp.WebApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class InlinePdfResponseTests
{
    [Theory]
    [InlineData("epreuve-sans-solutions.pdf")]
    [InlineData("Leçon-avec-solutions.pdf")]
    public async Task Create_ZeigtPdfInlineMitDownloadnamen(string fileName)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        context.Response.Body = new MemoryStream();
        var bytes = "%PDF-test"u8.ToArray();

        var result = InlinePdfResponse.Create(context.Response, bytes, fileName);
        await result.ExecuteAsync(context);

        Assert.Equal("application/pdf", context.Response.ContentType);
        Assert.StartsWith("inline", context.Response.Headers.ContentDisposition.ToString());
        Assert.Contains("filename*=utf-8''", context.Response.Headers.ContentDisposition.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Uri.EscapeDataString(fileName), context.Response.Headers.ContentDisposition.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(bytes, ((MemoryStream)context.Response.Body).ToArray());
    }
}
