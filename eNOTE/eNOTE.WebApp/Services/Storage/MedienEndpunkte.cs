using ENote.Core.Services.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ENote.WebApp.Services.Storage;

public static class MedienEndpunkte
{
    public static IEndpointConventionBuilder MapEnoteMedien(this IEndpointRouteBuilder endpoints)
        => endpoints.MapGet("/media/{**blobName}", LadeMediumAsync).RequireAuthorization();

    private static async Task<IResult> LadeMediumAsync(string blobName, HttpContext httpContext,
        MedienZugriff zugriff, IMediaStorageService mediaStorage, CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        httpContext.Response.Headers.XContentTypeOptions = "nosniff";
        httpContext.Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
        var asset = await zugriff.LadeAssetAsync(httpContext.User, blobName, cancellationToken);
        if (asset is null) return Results.NotFound();
        var bytes = await mediaStorage.LadeBytesAsync(asset.BlobName, cancellationToken);
        if (bytes is null) return Results.NotFound();
        var contentType = Path.GetExtension(asset.BlobName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
        // Aktive oder unbekannte Dateiformate nie als HTML/SVG im App-Ursprung ausführen.
        return Results.File(bytes, contentType,
            fileDownloadName: contentType.StartsWith("image/", StringComparison.Ordinal) ? null : Path.GetFileName(asset.BlobName));
    }
}
