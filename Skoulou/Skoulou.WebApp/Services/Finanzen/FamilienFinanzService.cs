using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Finanzen;

/// <summary>Gebühren aus Sicht des Schülers (eigene) und der Eltern (nur verknüpfte Kinder) – nur lesen, laufendes Schuljahr.</summary>
public sealed class FamilienFinanzService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, TimeProvider zeit)
{
    /// <summary>Konto des Schülers bzw. des gewählten Kindes; null, wenn die Schule noch kein Schuljahr hat.</summary>
    public async Task<SchuelerKontoDetail?> KontoAsync(Guid akteurId, Guid? kindId, CancellationToken ct = default)
    {
        var (akteur, _) = await zugriff.PruefeAsync(akteurId, Faehigkeit.FinanzenEinsehen, null, false, ct);
        if (akteur.SchuleId is not { } schuleId) throw new KeinZugriffFehler();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var schuelerId = akteur.Rolle switch
        {
            Rollen.Schueler => akteur.Id,
            Rollen.Eltern when kindId is { } k && await SchulAbfragen.IstKindAsync(db, akteur.Id, k, ct) => k,
            _ => throw new KeinZugriffFehler()
        };
        var jahr = await SchulAbfragen.LaufendesSchuljahrAsync(db, schuleId, ct);
        if (jahr is null) return null;
        return await GebuehrenService.KontoDetailAsync(db, schuleId, jahr, schuelerId, new SchulZeit(zeit).Heute, ct);
    }
}
