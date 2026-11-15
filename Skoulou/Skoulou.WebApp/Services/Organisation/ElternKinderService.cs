using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Organisation;

/// <summary>Kind eines Elternkontos für die Auswahl auf den Elternseiten.</summary>
public sealed record KindEintrag(Guid SchuelerId, string Vorname, string Name, string? Klasse, string Beziehung);

/// <summary>
/// Kinder eines Elternkontos (gemeinsam für alle Elternseiten: Notes, Bulletins, Présences, Frais …).
/// Andere Bereiche prüfen pro Abfrage zusätzlich <see cref="SchulAbfragen.IstKindAsync"/>.
/// </summary>
public sealed class ElternKinderService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, TimeProvider zeit)
{
    public async Task<IReadOnlyList<KindEintrag>> KinderAsync(Guid akteurId, CancellationToken ct = default)
    {
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        if (akteur.Rolle != Rollen.Eltern) throw new KeinZugriffFehler();
        Zugriff.Pruefe(akteur, Faehigkeit.NotenErfassen);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Aktuelle Klasse = laufende Einschreibung (eine Session-Klasse endet mit ihrer Session).
        var aktive = SchulAbfragen.LaufendeEinschreibungen(db, new SchulZeit(zeit).Heute);
        return await (from ek in db.ElternKinder.AsNoTracking()
                      join k in db.Users on ek.SchuelerId equals k.Id
                      where ek.ElternId == akteurId && k.Status != KontoStatus.Archive
                      orderby k.Vorname
                      select new KindEintrag(
                          k.Id, k.Vorname, (k.Vorname + " " + k.Nachname).Trim(),
                          aktive.Where(e => e.SchuelerId == k.Id)
                              .OrderByDescending(e => e.Von)
                              .Join(db.Klassen, e => e.KlasseId, kl => kl.Id, (e, kl) => kl.Name)
                              .FirstOrDefault(),
                          ek.Beziehung))
            .ToListAsync(ct);
    }
}
