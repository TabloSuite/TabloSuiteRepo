using Microsoft.Net.Http.Headers;

namespace HalloApp.WebApp.Services;

internal static class InlinePdfResponse
{
    public static IResult Create(HttpResponse response, byte[] bytes, string fileName)
    {
        response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline")
        {
            FileName = "document.pdf",
            FileNameStar = fileName
        }.ToString();

        return Results.File(bytes, "application/pdf");
    }
}
