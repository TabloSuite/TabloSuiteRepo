using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Paedagogik;

/// <summary>
/// Persönliche Formeln (« Mes formules ») im Formeldialog: je Konto höchstens 200; gleiches LaTeX
/// aktualisiert die Bezeichnung statt zu duplizieren. Nur Personal, das Inhalte erstellt.
/// </summary>
public sealed class FormelService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff)
{
    public const int Limit = 200;
    public const int MaxLaTeX = InhaltBereiniger.MaxFormelLaenge;

    public async Task<IReadOnlyList<FormelZeile>> ListeAsync(Guid akteurId, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.LeserAsync(zugriff, akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.FormelFavoriten.AsNoTracking().Where(f => f.KontoId == akteur.Id)
            .OrderBy(f => f.Bezeichnung).ThenBy(f => f.ErstelltAmUtc)
            .Select(f => new FormelZeile(f.Id, f.LaTeX, f.Bezeichnung)).ToListAsync(ct);
    }

    public async Task<FormelZeile> SpeichernAsync(Guid akteurId, string? latex, string? bezeichnung, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.LeserAsync(zugriff, akteurId, ct);
        latex = latex?.Trim() ?? string.Empty;
        if (latex.Length == 0) throw new FachFehler("Saisissez une formule.", "LaTeX");
        if (latex.Length > MaxLaTeX) throw new FachFehler($"La formule est trop longue ({MaxLaTeX} caractères au plus).", "LaTeX");
        if (latex.Any(c => c is '<' or '>' || (char.IsControl(c) && c is not ('\n' or '\t'))))
            throw new FachFehler("La formule contient des caractères non autorisés (< ou >). Utilisez \\lt et \\gt.", "LaTeX");
        bezeichnung = string.IsNullOrWhiteSpace(bezeichnung) ? "Formule personnelle" : bezeichnung.Trim();
        if (bezeichnung.Length > 200) bezeichnung = bezeichnung[..200];

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var vorhanden = await db.FormelFavoriten.FirstOrDefaultAsync(f => f.KontoId == akteur.Id && f.LaTeX == latex, ct);
        if (vorhanden is null)
        {
            if (await db.FormelFavoriten.CountAsync(f => f.KontoId == akteur.Id, ct) >= Limit)
                throw new FachFehler($"Vous avez déjà {Limit} formules enregistrées. Supprimez-en avant d'en ajouter.");
            vorhanden = new FormelFavorit { KontoId = akteur.Id, LaTeX = latex, Bezeichnung = bezeichnung };
            db.FormelFavoriten.Add(vorhanden);
        }
        else
        {
            vorhanden.Bezeichnung = bezeichnung;
        }
        await db.SaveChangesAsync(ct);
        return new FormelZeile(vorhanden.Id, vorhanden.LaTeX, vorhanden.Bezeichnung);
    }

    public async Task LoeschenAsync(Guid akteurId, Guid id, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.LeserAsync(zugriff, akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var f = await db.FormelFavoriten.FirstOrDefaultAsync(x => x.Id == id && x.KontoId == akteur.Id, ct)
                ?? throw new FachFehler("Cette formule est introuvable.");
        db.FormelFavoriten.Remove(f);
        await db.SaveChangesAsync(ct);
    }
}
