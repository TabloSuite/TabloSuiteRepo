using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.Models.Media;
using SkoulouApp.Core.Options;
using SkoulouApp.WebApp.Data;

namespace SkoulouApp.WebApp.Services;

public sealed class SchulLogoService(IDbContextFactory<ApplicationDbContext> factory,
    IWebHostEnvironment environment, IOptions<AzureBlobStorageOptions> options)
{
    public async Task<BlobMediaAssetItem> HochladenAsync(Guid user, Guid schule, Stream inhalt, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        if (!await new SkoulouApp.Core.Services.SchulZugangService(db).IstSchuleFreigegebenAsync(schule, token))
            throw new UnauthorizedAccessException(SkoulouApp.Core.Services.SchulZugangService.GesperrtHinweis);
        var rollen = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
            where ur.UserId == user select r.Name!).ToListAsync(token);
        if ((!rollen.Contains(Rollen.SchulLeiter) && !rollen.Contains(Rollen.Koordinator))
            || !await db.UserSchools.AnyAsync(x => x.AppUserId == user && x.SchoolId == schule && x.School.IsActive && !x.AppUser.IsBlocked, token))
            throw new UnauthorizedAccessException("Seuls la direction et les coordinateurs de cette école peuvent importer son logo.");
        using var buffer = new MemoryStream();
        var bloc = new byte[8192];
        int lu;
        while ((lu = await inhalt.ReadAsync(bloc, token)) > 0)
        {
            if (buffer.Length + lu > SchulLogoPruefung.MaxBytes) throw new InvalidOperationException("Le logo ne doit pas dépasser 2 Mo.");
            await buffer.WriteAsync(bloc.AsMemory(0, lu), token);
        }
        var typ = SchulLogoPruefung.Pruefen(buffer.ToArray());
        buffer.Position = 0;
        // Der bestehende Storage-Service erhält ausschließlich diesen operationsbezogenen Context.
        var storage = new AzureBlobMediaLibraryService(db, environment, options);
        return await storage.UploadImageAsync(user, rollen, schule, null, buffer,
            typ == "image/png" ? "logo.png" : "logo.jpg", "Logo de l'école", typ, "Logo de l'école", token);
    }
}
