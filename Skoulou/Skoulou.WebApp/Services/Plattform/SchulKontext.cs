using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Schuljahr für die Auswahl in der Kopfzeile.</summary>
public sealed record SchuljahrEintrag(Guid Id, string Bezeichnung, SchuljahrStatus Status);

/// <summary>
/// Kontext der Oberfläche je Verbindung: Name, Rolle, Schule, Koordinator-Rechte und das gewählte
/// Schuljahr (Standard = laufendes Jahr). Nur zur Anzeige – Rechte prüft immer <c>Zugriff</c>.
/// </summary>
public sealed class SchulKontext(IDbContextFactory<SkoulouDbContext> dbFactory)
{
    public bool Geladen { get; private set; }
    public Guid KontoId { get; private set; }
    public string Rolle { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Vorname { get; private set; } = string.Empty;
    public Guid? SchuleId { get; private set; }
    public string? SchulName { get; private set; }
    public SchulStatus? SchulStatus { get; private set; }
    public KoordinatorRechte Rechte { get; private set; }
    public IReadOnlyList<SchuljahrEintrag> Schuljahre { get; private set; } = [];
    public Guid? SchuljahrId { get; private set; }

    /// <summary>Wird ausgelöst, wenn sich Schuljahr oder Stammdaten ändern.</summary>
    public event Action? Geaendert;

    public string RollenBezeichnung => Rollen.Bezeichnung(Rolle);

    /// <summary>Zeigt die Kopfzeile eine Schuljahr-Auswahl (Direktion und Lehrkräfte)?</summary>
    public bool MitSchuljahrAuswahl => Rolle is Rollen.Direktor or Rollen.Koordinator or Rollen.Lehrer;

    public async Task LadeAsync(Guid kontoId, bool erzwingen = false, CancellationToken ct = default)
    {
        if (Geladen && KontoId == kontoId && !erzwingen) return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var k = await db.Users.AsNoTracking().Where(x => x.Id == kontoId)
            .Select(x => new
            {
                x.Rolle, x.Vorname, x.Nachname, x.SchuleId, x.KoordinatorRechte,
                SchulName = x.Schule != null ? x.Schule.Name : null,
                SchulStatus = x.Schule != null ? (SchulStatus?)x.Schule.Status : null
            })
            .FirstOrDefaultAsync(ct);
        if (k is null) return;

        KontoId = kontoId;
        Rolle = k.Rolle;
        Vorname = k.Vorname;
        Name = $"{k.Vorname} {k.Nachname}".Trim();
        SchuleId = k.SchuleId;
        SchulName = k.SchulName;
        SchulStatus = k.SchulStatus;
        Rechte = k.KoordinatorRechte;
        Schuljahre = k.SchuleId is { } schuleId
            ? await db.Schuljahre.AsNoTracking().Where(j => j.SchuleId == schuleId)
                .OrderByDescending(j => j.Beginn)
                .Select(j => new SchuljahrEintrag(j.Id, j.Bezeichnung, j.Status)).ToListAsync(ct)
            : [];
        if (SchuljahrId is null || Schuljahre.All(j => j.Id != SchuljahrId))
            SchuljahrId = (Schuljahre.FirstOrDefault(j => j.Status == SchuljahrStatus.Laufend) ?? Schuljahre.FirstOrDefault())?.Id;
        Geladen = true;
        Geaendert?.Invoke();
    }

    public void WaehleSchuljahr(Guid? id)
    {
        if (id == SchuljahrId || (id is not null && Schuljahre.All(j => j.Id != id))) return;
        SchuljahrId = id;
        Geaendert?.Invoke();
    }

    /// <summary>Darf der Koordinator (bzw. Direktor) Finanzen sehen? (Nur für die Navigation.)</summary>
    public bool HatRecht(KoordinatorRechte recht) => Rolle == Rollen.Direktor || (Rolle == Rollen.Koordinator && Rechte.HasFlag(recht));
}
