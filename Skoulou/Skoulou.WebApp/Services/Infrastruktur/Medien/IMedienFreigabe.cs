using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Data;

namespace Skoulou.WebApp.Services.Infrastruktur.Medien;

/// <summary>
/// Zusätzliche Freigabe für <c>/medien/{id}</c> über die Grundregel hinaus (z. B. Schüler sehen Bilder
/// und Videos der ihnen zugewiesenen Inhalte). Bereiche registrieren Implementierungen; eine Zusage genügt.
/// </summary>
public interface IMedienFreigabe
{
    Task<bool> DarfSehenAsync(SkoulouDbContext db, Guid kontoId, string rolle, Guid? schuleId, Medium medium, CancellationToken ct);
}
