using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace SkoulouApp.Core.Services;

public sealed class FormelFavoritService(ISkoulouAppDbContext dbContext)
{
    public async Task<IReadOnlyList<FormelFavoritDto>> LadeAsync(Guid benutzerId, CancellationToken cancellationToken = default)
    {
        if (benutzerId == Guid.Empty)
        {
            return [];
        }

        var favoriten = await dbContext.FormelFavoriten
            .AsNoTracking()
            .Where(x => x.BenutzerID == benutzerId)
            .OrderBy(x => x.Bezeichnung)
            .ThenBy(x => x.ErstelltAmUtc)
            .Select(x => new FormelFavoritDto(x.Id, x.LaTeX, x.Bezeichnung, x.Vorschau))
            .ToListAsync(cancellationToken);

        return favoriten;
    }

    public async Task<FormelFavoritDto?> HinzufuegenAsync(Guid benutzerId, string? latex, string? bezeichnung, string? vorschau, CancellationToken cancellationToken = default)
    {
        if (benutzerId == Guid.Empty)
        {
            return null;
        }

        var latexNorm = NormalizeToken(latex, 1000);
        if (string.IsNullOrWhiteSpace(latexNorm))
        {
            return null;
        }

        var nameNorm = NormalizeToken(bezeichnung, 200);
        var previewNorm = NormalizeToken(vorschau, 400);

        var bestehend = await dbContext.FormelFavoriten
            .FirstOrDefaultAsync(x => x.BenutzerID == benutzerId && x.LaTeX == latexNorm, cancellationToken);

        if (bestehend is not null)
        {
            if (!string.IsNullOrWhiteSpace(nameNorm))
            {
                bestehend.Bezeichnung = nameNorm;
            }

            if (!string.IsNullOrWhiteSpace(previewNorm))
            {
                bestehend.Vorschau = previewNorm;
            }

            bestehend.GeaendertAmUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return new FormelFavoritDto(bestehend.Id, bestehend.LaTeX, bestehend.Bezeichnung, bestehend.Vorschau);
        }

        var favorit = new FormelFavorit
        {
            Id = Guid.NewGuid(),
            BenutzerID = benutzerId,
            LaTeX = latexNorm,
            Bezeichnung = string.IsNullOrWhiteSpace(nameNorm) ? latexNorm : nameNorm,
            Vorschau = string.IsNullOrWhiteSpace(previewNorm) ? latexNorm : previewNorm,
            ErstelltAmUtc = DateTime.UtcNow
        };

        dbContext.FormelFavoriten.Add(favorit);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new FormelFavoritDto(favorit.Id, favorit.LaTeX, favorit.Bezeichnung, favorit.Vorschau);
    }

    public async Task<bool> EntfernenAsync(Guid benutzerId, string? latex, CancellationToken cancellationToken = default)
    {
        if (benutzerId == Guid.Empty)
        {
            return false;
        }

        var latexNorm = NormalizeToken(latex, 1000);
        if (string.IsNullOrWhiteSpace(latexNorm))
        {
            return false;
        }

        var favorit = await dbContext.FormelFavoriten
            .FirstOrDefaultAsync(x => x.BenutzerID == benutzerId && x.LaTeX == latexNorm, cancellationToken);

        if (favorit is null)
        {
            return false;
        }

        dbContext.FormelFavoriten.Remove(favorit);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string NormalizeToken(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

public sealed record FormelFavoritDto(Guid Id, string Latex, string Bezeichnung, string Vorschau);
