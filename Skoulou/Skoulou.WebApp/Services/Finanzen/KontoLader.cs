using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Finanzen;
using Skoulou.Core.Organisation;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Finanzen;

/// <summary>Schüler mit Klasse im gewählten Jahr (für Listen und Kontoköpfe).</summary>
public sealed record SchuelerKopf(Guid Id, string Name, string? Matrikel, Guid? KlasseId, string? Klasse);

/// <summary>
/// Lädt die Daten der Gebührenkonten und rechnet sie mit dem <see cref="GebuehrenRechner"/>. Der « Reste année
/// précédente » ist eine reine Abfrage: die Konten der Vorjahre werden der Reihe nach gerechnet und ihr Saldo
/// vorgetragen – beim Jahreswechsel muss nichts kopiert werden. Alles in wenigen Abfragen je Jahr (kein N+1).
/// </summary>
internal static class KontoLader
{
    private sealed record Posten(PostenDaten Daten, GeltungDaten Geltung);

    private sealed class JahresDaten
    {
        public required Dictionary<Guid, List<BelegungDaten>> Klassen { get; init; }
        public required List<Posten> Posten { get; init; }
        public required ILookup<Guid, ErmaessigungDaten> Ermaessigungen { get; init; }
        public required ILookup<Guid, ZahlungDaten> Zahlungen { get; init; }
        public required DateOnly Beginn { get; init; }

        public IReadOnlyList<PostenDaten> PostenFuer(Guid schuelerId)
        {
            if (!Klassen.TryGetValue(schuelerId, out var belegungen)) return [];
            return Posten.Where(p => PostenGeltung.Gilt(p.Geltung, belegungen)).Select(p => p.Daten).ToList();
        }
    }

    /// <summary>Schüler eines Jahres: gültige Einschreibung oder eine Zahlung in diesem Jahr.</summary>
    public static async Task<HashSet<Guid>> SchuelerDesJahresAsync(SkoulouDbContext db, Guid jahrId, Guid? klasseId, CancellationToken ct)
    {
        var eingeschrieben = SchulAbfragen.GueltigeEinschreibungen(db).Where(e => e.SchuljahrId == jahrId);
        if (klasseId is { } k) eingeschrieben = eingeschrieben.Where(e => e.KlasseId == k);
        var ids = await eingeschrieben.Select(e => e.SchuelerId).Distinct().ToListAsync(ct);
        if (klasseId is null)
            ids.AddRange(await db.Zahlungen.Where(z => z.SchuljahrId == jahrId).Select(z => z.SchuelerId).Distinct().ToListAsync(ct));
        return ids.ToHashSet();
    }

    /// <summary>Konten der Schüler im Jahr <paramref name="jahrId"/> (inkl. Vortrag aus allen Vorjahren der Schule).</summary>
    /// <param name="zusatz">Zusätzliche Zahlung im Zieljahr (z. B. eine stornierte für ihre Quittung).</param>
    public static async Task<Dictionary<Guid, GebuehrenKonto>> KontenAsync(SkoulouDbContext db, Guid schuleId, Guid jahrId,
        IReadOnlyCollection<Guid> schuelerIds, DateOnly stichtag, CancellationToken ct, (Guid SchuelerId, ZahlungDaten Zahlung)? zusatz = null)
    {
        var jahre = await db.Schuljahre.AsNoTracking().Where(j => j.SchuleId == schuleId)
            .OrderBy(j => j.Beginn).Select(j => new { j.Id, j.Beginn }).ToListAsync(ct);
        var ziel = jahre.FindIndex(j => j.Id == jahrId);
        if (ziel < 0) throw new FachFehler("Cette année scolaire est introuvable.");

        var kreis = schuelerIds.Distinct().ToList();
        var vortrag = new Dictionary<Guid, long>();
        var konten = new Dictionary<Guid, GebuehrenKonto>();
        if (kreis.Count == 0) return konten;

        for (var i = 0; i <= ziel; i++)
        {
            var istZiel = i == ziel;
            var daten = await JahresDatenAsync(db, jahre[i].Id, jahre[i].Beginn, kreis, ct);
            foreach (var s in kreis)
            {
                var v = vortrag.GetValueOrDefault(s);
                var posten = daten.PostenFuer(s);
                var zahlungen = daten.Zahlungen[s].ToList();
                if (istZiel && zusatz is { } z && z.SchuelerId == s && zahlungen.All(x => x.Id != z.Zahlung.Id)) zahlungen.Add(z.Zahlung);
                if (!istZiel && posten.Count == 0 && v == 0 && zahlungen.Count == 0) continue;
                var konto = GebuehrenRechner.Berechne(posten, daten.Ermaessigungen[s].ToList(), zahlungen, v,
                    istZiel ? stichtag : daten.Beginn, daten.Beginn);
                if (istZiel) konten[s] = konto;
                else vortrag[s] = konto.Saldo;
            }
        }
        return konten;
    }

    public static async Task<GebuehrenKonto> KontoAsync(SkoulouDbContext db, Guid schuleId, Guid jahrId, Guid schuelerId, DateOnly stichtag,
        CancellationToken ct, ZahlungDaten? zusatz = null)
        => (await KontenAsync(db, schuleId, jahrId, [schuelerId], stichtag, ct, zusatz is null ? null : (schuelerId, zusatz)))
           .GetValueOrDefault(schuelerId) ?? GebuehrenKonto.Leer(stichtag);

    /// <summary>Kopfdaten (Name, Matrikel, Klasse im Jahr) der Schüler.</summary>
    public static async Task<Dictionary<Guid, SchuelerKopf>> KoepfeAsync(SkoulouDbContext db, Guid jahrId, IReadOnlyCollection<Guid> schuelerIds, CancellationToken ct)
    {
        var ids = schuelerIds.Distinct().ToList();
        var konten = await (from k in db.Users
                            where ids.Contains(k.Id)
                            join p in db.SchuelerProfile on k.Id equals p.KontoId into pj
                            from p in pj.DefaultIfEmpty()
                            select new { k.Id, k.Vorname, k.Nachname, Matrikel = p == null ? null : p.Matrikel }).ToListAsync(ct);
        var klassen = await KlassenAsync(db, jahrId, ids, ct);
        var namen = await db.Klassen.Where(k => k.SchuljahrId == jahrId).Select(k => new { k.Id, k.Name }).ToDictionaryAsync(k => k.Id, k => k.Name, ct);
        return konten.ToDictionary(k => k.Id, k =>
        {
            var klasse = klassen.TryGetValue(k.Id, out var b) ? PostenGeltung.Massgeblich(b)?.KlasseId : null;
            return new SchuelerKopf(k.Id, $"{k.Nachname} {k.Vorname}".Trim(), k.Matrikel, klasse, klasse is { } kid ? namen.GetValueOrDefault(kid) : null);
        });
    }

    /// <summary>
    /// Gültige Einschreibungen des Jahres je Schüler mit effektivem Zeitraum (Session-Klassen enden mit ihrer Session).
    /// Maßgeblich für Jahresposten und Kopf ist die aktive bzw. zuletzt begonnene (<see cref="PostenGeltung.Massgeblich"/>).
    /// </summary>
    private static async Task<Dictionary<Guid, List<BelegungDaten>>> KlassenAsync(SkoulouDbContext db, Guid jahrId, List<Guid> ids, CancellationToken ct)
    {
        var einschreibungen = await (from e in SchulAbfragen.GueltigeEinschreibungen(db)
                                     where e.SchuljahrId == jahrId && ids.Contains(e.SchuelerId)
                                     join k in db.Klassen on e.KlasseId equals k.Id
                                     join p in db.Perioden on k.PeriodeId equals (Guid?)p.Id into pj
                                     from p in pj.DefaultIfEmpty()
                                     select new
                                     {
                                         e.SchuelerId, e.KlasseId, k.StufeId, e.Von, e.Bis, Aktiv = e.Status == EinschreibungsStatus.Active,
                                         SessionBeginn = p == null ? (DateOnly?)null : p.Beginn, SessionEnde = p == null ? (DateOnly?)null : p.Ende
                                     }).ToListAsync(ct);
        return einschreibungen.GroupBy(e => e.SchuelerId)
            .ToDictionary(g => g.Key, g => g.Select(e => new BelegungDaten(e.KlasseId, e.StufeId,
                SessionRegel.Beginn(e.Von, e.SessionBeginn), SessionRegel.Ende(e.Bis, e.SessionEnde), e.Aktiv)).ToList());
    }

    private static async Task<JahresDaten> JahresDatenAsync(SkoulouDbContext db, Guid jahrId, DateOnly beginn, List<Guid> ids, CancellationToken ct)
    {
        var posten = await db.GebuehrenPosten.AsNoTracking().Where(p => p.SchuljahrId == jahrId).Include(p => p.Raten).ToListAsync(ct);
        var perioden = await db.Perioden.AsNoTracking().Where(p => p.SchuljahrId == jahrId)
            .Select(p => new { p.Id, p.Beginn, p.Ende }).ToDictionaryAsync(p => p.Id, ct);
        var ermaessigungen = await db.Ermaessigungen.AsNoTracking()
            .Where(e => e.SchuljahrId == jahrId && ids.Contains(e.SchuelerId))
            .Select(e => new { e.SchuelerId, Daten = new ErmaessigungDaten(e.PostenId, e.Art, e.Wert) }).ToListAsync(ct);
        var zahlungen = await db.Zahlungen.AsNoTracking()
            .Where(z => z.SchuljahrId == jahrId && z.StorniertAmUtc == null && ids.Contains(z.SchuelerId))
            .Select(z => new { z.SchuelerId, Daten = new ZahlungDaten(z.Id, z.Betrag, z.Datum, z.ErstelltAmUtc) }).ToListAsync(ct);
        return new JahresDaten
        {
            Klassen = await KlassenAsync(db, jahrId, ids, ct),
            Posten = posten.Select(p => new Posten(Daten(p), p.PeriodeId is { } pid && perioden.TryGetValue(pid, out var per)
                ? new GeltungDaten(p.StufeId, p.KlasseId, per.Beginn, per.Ende)
                : new GeltungDaten(p.StufeId, p.KlasseId))).ToList(),
            Ermaessigungen = ermaessigungen.ToLookup(e => e.SchuelerId, e => e.Daten),
            Zahlungen = zahlungen.ToLookup(z => z.SchuelerId, z => z.Daten),
            Beginn = beginn
        };
    }

    public static PostenDaten Daten(GebuehrenPosten p)
        => new(p.Id, p.Bezeichnung, p.Betrag, p.Pflicht, p.Reihenfolge, p.Raten.Select(r => new RatenDaten(r.Nr, r.Betrag, r.Faellig)).ToList());
}
