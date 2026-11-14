using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Finanzen;

public sealed record AnteilZeile(Guid Id, Guid ResponsableId, string Responsable, string Schule, string Rechnung, string Periode, decimal Prozent,
    long Betrag, bool IstAusgleich, DateTime ErstelltAmUtc, DateTime? AusgezahltAmUtc, string? Referenz);
public sealed record AnteilSummen(long Offen, int OffenAnzahl, long Ausgezahlt);
public sealed record ResponsableEintrag(Guid Id, string Name, long Offen);

/// <summary>
/// Anteile der Responsables (Bauplan §11): der Responsable sieht nur seine Anteile (« Mes parts », nur lesen);
/// der Admin sieht alle und markiert Auszahlungen einzeln oder gesammelt mit Datum und Referenz (Audit).
/// </summary>
public sealed class AnteilService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit, TimeProvider zeit)
{
    public const string FilterOffen = "offen";
    public const string FilterAusgezahlt = "verses";

    /// <summary>Responsable: nur eigene; Admin: alle (optional ein Responsable).</summary>
    private async Task<Guid?> SichtAsync(Guid akteurId, Guid? responsableId, CancellationToken ct)
    {
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        Zugriff.Pruefe(akteur, Faehigkeit.SchulenBetreuen);
        return akteur.IstAdmin ? responsableId : akteur.Rolle == Rollen.Responsable ? akteur.Id : throw new KeinZugriffFehler();
    }

    public async Task<Seite<AnteilZeile>> ListeAsync(Guid akteurId, Guid? responsableId, string? filter, int ueberspringen, int anzahl, CancellationToken ct = default)
    {
        var sicht = await SichtAsync(akteurId, responsableId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = Gefiltert(db, sicht, filter);
        var projektion = from a in q.OrderBy(a => a.AusgezahltAmUtc != null).ThenByDescending(a => a.ErstelltAmUtc)
                         join k in db.Users on a.ResponsableId equals k.Id
                         join s in db.Schulen on a.SchuleId equals s.Id
                         join r in db.Plattformrechnungen on a.RechnungId equals r.Id
                         join p in db.Perioden on r.PeriodeId equals p.Id
                         join j in db.Schuljahre on r.SchuljahrId equals j.Id
                         select new AnteilZeile(a.Id, a.ResponsableId, k.Vorname + " " + k.Nachname, s.Name, r.Nummer, p.Titel + " " + j.Bezeichnung,
                             a.Prozent, a.Betrag, a.AusgleichFuerId != null, a.ErstelltAmUtc, a.AusgezahltAmUtc, a.AuszahlungsReferenz);
        return await Seite<AnteilZeile>.LadeAsync(projektion, ueberspringen, anzahl, ct);
    }

    public async Task<AnteilSummen> SummenAsync(Guid akteurId, Guid? responsableId, CancellationToken ct = default)
    {
        var sicht = await SichtAsync(akteurId, responsableId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = Gefiltert(db, sicht, null);
        var offen = q.Where(a => a.AusgezahltAmUtc == null);
        return new AnteilSummen(await offen.SumAsync(a => (long?)a.Betrag, ct) ?? 0, await offen.CountAsync(ct),
            await q.Where(a => a.AusgezahltAmUtc != null).SumAsync(a => (long?)a.Betrag, ct) ?? 0);
    }

    /// <summary>Responsables mit offenem Betrag (Admin, Filter der Auszahlungsliste).</summary>
    public async Task<IReadOnlyList<ResponsableEintrag>> ResponsablesAsync(Guid akteurId, CancellationToken ct = default)
    {
        await zugriff.PruefeAsync(akteurId, Faehigkeit.PlattformVerwalten, null, false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.Where(k => k.Rolle == Rollen.Responsable).OrderBy(k => k.Nachname).ThenBy(k => k.Vorname)
            .Select(k => new ResponsableEintrag(k.Id, k.Vorname + " " + k.Nachname,
                db.ResponsableAnteile.Where(a => a.ResponsableId == k.Id && a.AusgezahltAmUtc == null).Sum(a => (long?)a.Betrag) ?? 0))
            .ToListAsync(ct);
    }

    /// <summary>Markiert offene Anteile als ausgezahlt (einzeln oder gesammelt); bereits ausgezahlte werden übersprungen.</summary>
    public async Task<int> AuszahlenAsync(Guid akteurId, IReadOnlyCollection<Guid> anteilIds, DateOnly? datum, string? referenz, CancellationToken ct = default)
    {
        var (admin, _) = await zugriff.PruefeAsync(akteurId, Faehigkeit.PlattformVerwalten, null, true, ct);
        if (anteilIds.Count == 0) throw new FachFehler("Sélectionnez au moins une part à verser.");
        var tag = FinanzEingabe.Datum(datum, new SchulZeit(zeit).Heute);
        var text = Eingabe.Pflicht(referenz, 100, "la référence du versement", "referenz");
        var ids = anteilIds.Distinct().ToList();
        return await dbFactory.TransaktionAsync(async db =>
        {
            var anteile = await db.ResponsableAnteile.Where(a => ids.Contains(a.Id) && a.AusgezahltAmUtc == null).ToListAsync(ct);
            if (anteile.Count == 0) throw new FachFehler("Ces parts sont déjà versées.");
            var zeitpunkt = DateTime.SpecifyKind(tag.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Utc);
            foreach (var a in anteile) (a.AusgezahltAmUtc, a.AuszahlungsReferenz, a.AusgezahltVonId) = (zeitpunkt, text, admin.Id);
            foreach (var g in anteile.GroupBy(a => a.ResponsableId))
                audit.Erfasse(db, admin.Id, null, FinanzAudit.Auszahlung, nameof(ResponsableAnteil), g.Key,
                    $"Versement au responsable : {g.Count()} part(s), {SkoulouKultur.Fcfa(g.Sum(a => a.Betrag))}, le {SkoulouKultur.Datum(tag)} (réf. {text}).");
            await db.SaveChangesAsync(ct);
            return anteile.Count;
        }, ct);
    }

    private static IQueryable<ResponsableAnteil> Gefiltert(SkoulouDbContext db, Guid? responsableId, string? filter)
    {
        var q = db.ResponsableAnteile.AsQueryable();
        if (responsableId is { } r) q = q.Where(a => a.ResponsableId == r);
        return filter switch
        {
            FilterOffen => q.Where(a => a.AusgezahltAmUtc == null),
            FilterAusgezahlt => q.Where(a => a.AusgezahltAmUtc != null),
            _ => q
        };
    }
}
