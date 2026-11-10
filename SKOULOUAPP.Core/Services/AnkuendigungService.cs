using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Interfaces;
using SkoulouApp.Core.Services.Text;
using Microsoft.EntityFrameworkCore;

namespace SkoulouApp.Core.Services;

public sealed class AnkuendigungService(ISkoulouAppDbContext dbContext)
{
    public Task<List<Ankuendigung>> GetAktiveAsync(DateTime jetztUtc, CancellationToken cancellationToken = default)
        => dbContext.Ankuendigungen.AsNoTracking()
            .Where(x => x.AblaufAmUtc > jetztUtc)
            .OrderByDescending(x => x.ErstelltAmUtc)
            .ToListAsync(cancellationToken);

    public Task<List<Ankuendigung>> GetAlleAsync(CancellationToken cancellationToken = default)
        => dbContext.Ankuendigungen.AsNoTracking().OrderByDescending(x => x.ErstelltAmUtc).ToListAsync(cancellationToken);

    public async Task<(bool Succeeded, string Message)> ErstellenAsync(string titel, string inhaltHtml, DateTime ablaufAmUtc, DateTime jetztUtc, CancellationToken cancellationToken = default)
    {
        titel = titel.Trim();
        inhaltHtml = HtmlSanitizer.Sanitize(inhaltHtml);
        if (titel.Length is < 1 or > 200 || HtmlSanitizer.ToPlainText(inhaltHtml).Length < 5 || ablaufAmUtc <= jetztUtc)
        {
            return (false, "Indiquez un titre, un texte d'au moins cinq caractères et une date d'expiration future.");
        }

        dbContext.Ankuendigungen.Add(new Ankuendigung { Titel = titel, InhaltHtml = inhaltHtml, ErstelltAmUtc = jetztUtc, AblaufAmUtc = ablaufAmUtc });
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, "Annonce publiée.");
    }

    public async Task<bool> LoeschenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ankuendigung = await dbContext.Ankuendigungen.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (ankuendigung is null) return false;
        dbContext.Ankuendigungen.Remove(ankuendigung);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
