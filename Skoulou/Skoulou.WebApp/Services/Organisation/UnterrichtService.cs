using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Organisation;

public sealed record LehrauftragKurz(Guid Id, Guid LehrerId, string Lehrer, decimal Wochenstunden);

public sealed record KlassenfachZeile(Guid Id, Guid FachId, string Fach, string? Kuerzel, FachGruppe Gruppe, decimal Koeffizient,
    int Reihenfolge, IReadOnlyList<LehrauftragKurz> Lehrer);

public sealed record LehrauftragZeile(Guid Id, Guid KlassenfachId, Guid KlasseId, string Klasse, string Fach, decimal Wochenstunden);

/// <summary>
/// Klassenfächer (Koeffizienten) und Lehraufträge – der Lehrauftrag ist die einzige Quelle dafür, wer
/// welche Klasse sieht und benotet (Bauplan §4.2). Direktor und Koordinator dürfen selbst unterrichten.
/// </summary>
public sealed class UnterrichtService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff)
{
    public const decimal MaxKoeffizient = 100;
    public const decimal MaxWochenstunden = 40;

    public async Task<IReadOnlyList<KlassenfachZeile>> KlassenfaecherAsync(Guid akteurId, Guid klasseId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await OrgAbfragen.KlasseAsync(db, a, klasseId, ct);
        var faecher = await (from kf in db.Klassenfaecher.AsNoTracking()
                             join f in db.Faecher on kf.FachId equals f.Id
                             where kf.KlasseId == klasseId
                             select new { kf.Id, kf.FachId, f.Name, f.Kuerzel, f.Gruppe, kf.Koeffizient, Reihenfolge = kf.Reihenfolge ?? f.Reihenfolge })
            .ToListAsync(ct);
        var ids = faecher.Select(f => f.Id).ToList();
        var auftraege = await (from l in db.Lehrauftraege.AsNoTracking()
                               join u in db.Users on l.LehrerId equals u.Id
                               where ids.Contains(l.KlassenfachId)
                               orderby u.Nachname
                               select new { l.KlassenfachId, Kurz = new LehrauftragKurz(l.Id, l.LehrerId, u.Vorname + " " + u.Nachname, l.Wochenstunden) })
            .ToListAsync(ct);
        return faecher.OrderBy(f => f.Reihenfolge).ThenBy(f => f.Name)
            .Select(f => new KlassenfachZeile(f.Id, f.FachId, f.Name, f.Kuerzel, f.Gruppe, f.Koeffizient, f.Reihenfolge,
                auftraege.Where(x => x.KlassenfachId == f.Id).Select(x => x.Kurz).ToList()))
            .ToList();
    }

    /// <summary>Fächer einer Klasse hinzufügen (vorhandene bleiben unverändert). Anzahl neu.</summary>
    public async Task<int> FaecherHinzufuegenAsync(Guid akteurId, Guid klasseId, IReadOnlyCollection<Guid> fachIds, decimal koeffizient = 1, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        var koeff = PruefeKoeffizient(koeffizient);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var klasse = await OrgAbfragen.KlasseAsync(db, a, klasseId, ct);
        await PruefeJahrOffenAsync(db, klasse.SchuljahrId, ct);
        var gueltig = await db.Faecher.Where(f => f.SchuleId == a.SchuleId && f.Aktiv && fachIds.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct);
        if (gueltig.Count != fachIds.Distinct().Count()) throw new FachFehler("Une des matières choisies est introuvable ou désactivée.");
        var vorhanden = await db.Klassenfaecher.Where(kf => kf.KlasseId == klasse.Id).Select(kf => kf.FachId).ToListAsync(ct);
        var neu = gueltig.Except(vorhanden).ToList();
        foreach (var fachId in neu)
            db.Klassenfaecher.Add(new Klassenfach { SchuleId = a.SchuleId, KlasseId = klasse.Id, FachId = fachId, Koeffizient = koeff });
        await db.SaveChangesAsync(ct);
        return neu.Count;
    }

    public async Task KoeffizientSetzenAsync(Guid akteurId, Guid klassenfachId, decimal koeffizient, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        var koeff = PruefeKoeffizient(koeffizient);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var kf = await KlassenfachAsync(db, a, klassenfachId, ct);
        await PruefeJahrOffenAsync(db, await db.Klassen.Where(k => k.Id == kf.KlasseId).Select(k => k.SchuljahrId).FirstAsync(ct), ct);
        kf.Koeffizient = koeff;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Fach aus der Klasse entfernen, solange keine Noten/Appréciations und kein Stundenplan daran hängen.</summary>
    public async Task KlassenfachEntfernenAsync(Guid akteurId, Guid klassenfachId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        await dbFactory.TransaktionAsync(async db =>
        {
            var kf = await KlassenfachAsync(db, a, klassenfachId, ct);
            if (await db.Bewertungen.AnyAsync(b => b.KlassenfachId == kf.Id, ct) || await db.FachBemerkungen.AnyAsync(b => b.KlassenfachId == kf.Id, ct))
                throw new FachFehler("Cette matière a déjà des évaluations dans cette classe : elle ne peut plus être retirée.");
            var auftraege = await db.Lehrauftraege.Where(l => l.KlassenfachId == kf.Id).ToListAsync(ct);
            var auftragIds = auftraege.Select(l => l.Id).ToList();
            if (await db.StundenplanEintraege.AnyAsync(e => auftragIds.Contains(e.LehrauftragId), ct))
                throw new FachFehler("Cette matière figure dans l'emploi du temps de la classe : retirez d'abord ces cours.");
            db.Lehrauftraege.RemoveRange(auftraege);
            await db.SaveChangesAsync(ct);
            db.Klassenfaecher.Remove(kf);
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    /// <summary>Fächer und Koeffizienten einer Klasse in eine andere übernehmen (fehlende Fächer werden ergänzt).</summary>
    public async Task<int> KoeffizientenKopierenAsync(Guid akteurId, Guid quelleKlasseId, Guid zielKlasseId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        if (quelleKlasseId == zielKlasseId) throw new FachFehler("Choisissez une autre classe comme modèle.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await OrgAbfragen.KlasseAsync(db, a, quelleKlasseId, ct);
        var ziel = await OrgAbfragen.KlasseAsync(db, a, zielKlasseId, ct);
        await PruefeJahrOffenAsync(db, ziel.SchuljahrId, ct);
        var anzahl = await KopiereInAsync(db, a.SchuleId, quelleKlasseId, ziel.Id, ct);
        await db.SaveChangesAsync(ct);
        return anzahl;
    }

    /// <summary>Koeffizienten der Klasse auf alle anderen Klassen derselben Stufe im selben Jahr übertragen. Anzahl Klassen.</summary>
    public async Task<int> KoeffizientenAufStufeAsync(Guid akteurId, Guid quelleKlasseId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var quelle = await OrgAbfragen.KlasseAsync(db, a, quelleKlasseId, ct);
        if (quelle.StufeId is null) throw new FachFehler("Cette classe n'a pas de niveau : choisissez d'abord son niveau.");
        await PruefeJahrOffenAsync(db, quelle.SchuljahrId, ct);
        var ziele = await db.Klassen.Where(k => k.SchuljahrId == quelle.SchuljahrId && k.StufeId == quelle.StufeId && k.Id != quelle.Id && k.Aktiv)
            .Select(k => k.Id).ToListAsync(ct);
        foreach (var ziel in ziele) await KopiereInAsync(db, a.SchuleId, quelle.Id, ziel, ct);
        await db.SaveChangesAsync(ct);
        return ziele.Count;
    }

    private static async Task<int> KopiereInAsync(SkoulouDbContext db, Guid schuleId, Guid quelle, Guid ziel, CancellationToken ct)
    {
        var quellFaecher = await db.Klassenfaecher.AsNoTracking().Where(kf => kf.KlasseId == quelle).ToListAsync(ct);
        var zielFaecher = await db.Klassenfaecher.Where(kf => kf.KlasseId == ziel).ToListAsync(ct);
        foreach (var q in quellFaecher)
        {
            if (zielFaecher.FirstOrDefault(z => z.FachId == q.FachId) is { } z)
            {
                z.Koeffizient = q.Koeffizient;
                z.Reihenfolge = q.Reihenfolge;
            }
            else
            {
                db.Klassenfaecher.Add(new Klassenfach { SchuleId = schuleId, KlasseId = ziel, FachId = q.FachId, Koeffizient = q.Koeffizient, Reihenfolge = q.Reihenfolge });
            }
        }
        return quellFaecher.Count;
    }

    // ---------------- Lehraufträge ----------------

    /// <summary>Lehrauftrag setzen oder Stunden ändern (Upsert über Klassenfach + Lehrkraft).</summary>
    public async Task<Guid> LehrauftragSetzenAsync(Guid akteurId, Guid klassenfachId, Guid lehrerId, decimal wochenstunden, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        var stunden = Math.Round(wochenstunden * 2, MidpointRounding.AwayFromZero) / 2;
        if (stunden < 0.5m || stunden > MaxWochenstunden) throw new FachFehler("Les heures par semaine doivent être comprises entre 0,5 et 40 (par demi-heure).", "stunden");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var kf = await KlassenfachAsync(db, a, klassenfachId, ct);
        await PruefeJahrOffenAsync(db, await db.Klassen.Where(k => k.Id == kf.KlasseId).Select(k => k.SchuljahrId).FirstAsync(ct), ct);
        var lehrer = await db.Users.AsNoTracking().Where(k => k.Id == lehrerId)
            .Select(k => new { k.SchuleId, k.Rolle, k.Status }).FirstOrDefaultAsync(ct) ?? throw new FachFehler("Cet enseignant est introuvable.");
        if (lehrer.SchuleId != a.SchuleId) throw new KeinZugriffFehler(Zugriff.MeldungAndereSchule);
        if (!OrgRollen.Lehrkraefte.Contains(lehrer.Rolle)) throw new FachFehler("Seuls les enseignants, la direction et la coordination peuvent enseigner.");
        if (lehrer.Status == KontoStatus.Archive) throw new FachFehler("Ce compte est archivé : réintégrez-le d'abord.");

        var auftrag = await db.Lehrauftraege.FirstOrDefaultAsync(l => l.KlassenfachId == kf.Id && l.LehrerId == lehrerId, ct);
        if (auftrag is null)
        {
            auftrag = new Lehrauftrag { SchuleId = a.SchuleId, KlassenfachId = kf.Id, LehrerId = lehrerId };
            db.Lehrauftraege.Add(auftrag);
        }
        auftrag.Wochenstunden = stunden;
        await db.SaveChangesAsync(ct);
        return auftrag.Id;
    }

    public async Task LehrauftragEntfernenAsync(Guid akteurId, Guid lehrauftragId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var auftrag = await db.Lehrauftraege.FirstOrDefaultAsync(l => l.Id == lehrauftragId, ct) ?? throw new FachFehler("Cette affectation est introuvable.");
        a.PruefeSchule(auftrag.SchuleId);
        if (await db.StundenplanEintraege.AnyAsync(e => e.LehrauftragId == auftrag.Id, ct))
            throw new FachFehler("Cette affectation figure dans un emploi du temps : retirez d'abord ces cours.");
        db.Lehrauftraege.Remove(auftrag);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Lehraufträge einer Lehrkraft in einem Schuljahr.</summary>
    public async Task<IReadOnlyList<LehrauftragZeile>> AuftraegeDesLehrersAsync(Guid akteurId, Guid lehrerId, Guid jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await OrgAbfragen.KontoAsync(db, a, lehrerId, ct, OrgRollen.Lehrkraefte);
        return await (from l in db.Lehrauftraege.AsNoTracking()
                      join kf in db.Klassenfaecher on l.KlassenfachId equals kf.Id
                      join k in db.Klassen on kf.KlasseId equals k.Id
                      join f in db.Faecher on kf.FachId equals f.Id
                      where l.LehrerId == lehrerId && k.SchuljahrId == jahrId
                      orderby k.Name, f.Reihenfolge
                      select new LehrauftragZeile(l.Id, kf.Id, k.Id, k.Name, f.Name, l.Wochenstunden))
            .ToListAsync(ct);
    }

    /// <summary>Alle Klassenfächer eines Jahres (für die Auswahl beim Zuordnen), Zusatz = bisherige Lehrkräfte.</summary>
    public async Task<IReadOnlyList<AuswahlEintrag>> KlassenfaecherDesJahresAsync(Guid akteurId, Guid jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        var liste = await (from kf in db.Klassenfaecher.AsNoTracking()
                           join k in db.Klassen on kf.KlasseId equals k.Id
                           join f in db.Faecher on kf.FachId equals f.Id
                           where k.SchuljahrId == jahrId && k.Aktiv
                           orderby k.Name, f.Reihenfolge
                           select new
                           {
                               kf.Id, Klasse = k.Name, Fach = f.Name,
                               Lehrer = db.Lehrauftraege.Where(l => l.KlassenfachId == kf.Id)
                                   .Join(db.Users, l => l.LehrerId, u => u.Id, (l, u) => u.Nachname).ToList()
                           }).ToListAsync(ct);
        return liste.Select(x => new AuswahlEintrag(x.Id, $"{x.Klasse} · {x.Fach}",
            x.Lehrer.Count == 0 ? "sans enseignant" : string.Join(", ", x.Lehrer))).ToList();
    }

    private static decimal PruefeKoeffizient(decimal k)
    {
        var r = Math.Round(k, 2, MidpointRounding.AwayFromZero);
        if (r <= 0 || r > MaxKoeffizient) throw new FachFehler("Le coefficient doit être compris entre 0,01 et 100.", "koeffizient");
        return r;
    }

    private static async Task<Klassenfach> KlassenfachAsync(SkoulouDbContext db, SchulAkteur a, Guid id, CancellationToken ct)
    {
        var kf = await db.Klassenfaecher.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new FachFehler("Cette matière de classe est introuvable.");
        a.PruefeSchule(kf.SchuleId);
        return kf;
    }

    private static async Task PruefeJahrOffenAsync(SkoulouDbContext db, Guid jahrId, CancellationToken ct)
        => OrgAbfragen.PruefeOffen(await db.Schuljahre.AsNoTracking().FirstAsync(j => j.Id == jahrId, ct));
}
