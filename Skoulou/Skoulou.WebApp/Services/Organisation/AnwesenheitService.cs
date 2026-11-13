using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Organisation;

public sealed record AppellZeile(Guid EinschreibungId, Guid SchuelerId, string Name, string? Matrikel, AnwesenheitsStatus Status, int? Minuten,
    bool Entschuldigt, string? Bemerkung);

public sealed record Appell(Guid KlasseId, string Klasse, DateOnly Datum, string? Periode, string? Sperre, bool Erfasst, string? ErfasstVon,
    IReadOnlyList<AppellZeile> Zeilen);

public sealed record AppellEingabe(Guid EinschreibungId, AnwesenheitsStatus Status, int? Minuten, bool Entschuldigt, string? Bemerkung);

public sealed record AnwesenheitsSumme(int Absences, int AbsencesJustifiees, int Retards, int RetardMinuten)
{
    public static readonly AnwesenheitsSumme Leer = new(0, 0, 0, 0);
    public int NonJustifiees => Absences - AbsencesJustifiees;
}

public sealed record KlassenUebersichtZeile(Guid SchuelerId, string Name, AnwesenheitsSumme Summe);

public sealed record KlassenUebersicht(string Klasse, DateOnly Von, DateOnly Bis, int TageMitAppell, IReadOnlyList<KlassenUebersichtZeile> Zeilen);

public sealed record AnwesenheitsEintrag(DateOnly Datum, AnwesenheitsStatus Status, bool Entschuldigt, int? Minuten, string? Bemerkung, string Klasse);

public sealed record SchuelerAnwesenheit(Guid SchuelerId, string Name, string? Jahr, AnwesenheitsSumme Summe, IReadOnlyList<AnwesenheitsEintrag> Eintraege);

/// <summary>
/// Anwesenheit als Klassenappell (Bauplan §4.2, §11.7): Standard « présent », abwesend/verspätet mit Minuten,
/// entschuldigt, Bemerkung. Direktion für alle Klassen, Lehrkräfte für Klassen mit Lehrauftrag. Datum im Schuljahr,
/// in einer offenen Periode und nicht in der Zukunft; gesperrt, sobald die Bulletins der Periode veröffentlicht sind.
/// </summary>
public sealed class AnwesenheitService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, TimeProvider zeit)
{
    public const int MaxMinuten = 1440;

    /// <summary>Klassen, für die der Akteur den Appell machen darf (Direktion: alle aktiven; Lehrkraft: mit Lehrauftrag).</summary>
    public async Task<IReadOnlyList<AuswahlEintrag>> KlassenAsync(Guid akteurId, Guid jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.AnwesenheitErfassen, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        var q = db.Klassen.AsNoTracking().Where(k => k.SchuljahrId == jahrId && k.Aktiv);
        if (a.Art == Zugriffsart.Eigene)
        {
            var eigene = SchulAbfragen.KlassenDesLehrers(db, a.Id, jahrId);
            q = q.Where(k => eigene.Contains(k.Id));
        }
        var aktive = SchulAbfragen.AktiveEinschreibungen(db);
        var liste = await q.OrderBy(k => k.Name).Select(k => new { k.Id, k.Name, Anzahl = aktive.Count(e => e.KlasseId == k.Id) }).ToListAsync(ct);
        return liste.Select(k => new AuswahlEintrag(k.Id, k.Name, $"{k.Anzahl} élève{(k.Anzahl > 1 ? "s" : "")}")).ToList();
    }

    private async Task<(SchulAkteur A, Klasse Klasse, Schuljahr Jahr)> KlasseFuerAppellAsync(SkoulouDbContext db, Guid akteurId, Guid klasseId, CancellationToken ct)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.AnwesenheitErfassen, ct: ct);
        var klasse = await OrgAbfragen.KlasseAsync(db, a, klasseId, ct);
        if (a.Art == Zugriffsart.Eigene && !await SchulAbfragen.UnterrichtetKlasseAsync(db, a.Id, klasse.Id, ct))
            throw new KeinZugriffFehler("Vous n'enseignez pas dans cette classe.");
        var jahr = await db.Schuljahre.AsNoTracking().FirstAsync(j => j.Id == klasse.SchuljahrId, ct);
        return (a, klasse, jahr);
    }

    /// <summary>Grund, warum an diesem Tag kein Appell möglich ist (null = möglich), und die Periode.</summary>
    private async Task<(string? Sperre, Periode? Periode)> PruefeTagAsync(SkoulouDbContext db, Klasse klasse, Schuljahr jahr, DateOnly datum, CancellationToken ct)
    {
        if (datum > new SchulZeit(zeit).Heute) return ("L'appel ne peut pas être fait pour une date future.", null);
        if (jahr.Status == SchuljahrStatus.Abgeschlossen) return ($"L'année scolaire {jahr.Bezeichnung} est clôturée.", null);
        if (datum < jahr.Beginn || datum > jahr.Ende) return ($"Cette date est en dehors de l'année scolaire {jahr.Bezeichnung}.", null);
        var perioden = await db.Perioden.AsNoTracking()
            .Where(p => p.SchuljahrId == jahr.Id && p.Beginn <= datum && p.Ende >= datum && (klasse.PeriodeId == null || p.Id == klasse.PeriodeId))
            .OrderByDescending(p => p.ZaehltFuerJahr).ToListAsync(ct);
        if (perioden.Count == 0) return ("Cette date ne fait partie d'aucune période de l'année (vacances ?).", null);
        var periode = perioden[0];
        if (periode.Status == PeriodenStatus.Geschlossen) return ($"La période « {periode.Titel} » est fermée.", periode);
        if (await db.KlassenVeroeffentlichungen.AnyAsync(v => v.KlasseId == klasse.Id && v.PeriodeId == periode.Id && v.Status == VeroeffentlichungsStatus.Publiee, ct))
            return ($"Les bulletins de « {periode.Titel} » sont publiés : les présences sont verrouillées.", periode);
        return (null, periode);
    }

    private static IQueryable<Einschreibung> AmTag(SkoulouDbContext db, Guid klasseId, DateOnly datum)
        => SchulAbfragen.GueltigeEinschreibungen(db).Where(e => e.KlasseId == klasseId && e.Von <= datum && (e.Bis == null || e.Bis >= datum));

    public async Task<Appell> AppellAsync(Guid akteurId, Guid klasseId, DateOnly datum, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (_, klasse, jahr) = await KlasseFuerAppellAsync(db, akteurId, klasseId, ct);
        var (sperre, periode) = await PruefeTagAsync(db, klasse, jahr, datum, ct);
        var zeilen = await (from e in AmTag(db, klasse.Id, datum).AsNoTracking()
                            join k in db.Users on e.SchuelerId equals k.Id
                            orderby k.Nachname, k.Vorname
                            select new
                            {
                                e.Id, k.Vorname, k.Nachname, SchuelerId = k.Id,
                                Matrikel = db.SchuelerProfile.Where(p => p.KontoId == k.Id).Select(p => p.Matrikel).FirstOrDefault(),
                                A = db.Anwesenheiten.Where(x => x.EinschreibungId == e.Id && x.Datum == datum)
                                    .Select(x => new { x.Status, x.Minuten, x.Entschuldigt, x.Bemerkung, x.ErfasstVonId }).FirstOrDefault()
                            }).ToListAsync(ct);
        var erfasstVon = zeilen.Select(z => z.A?.ErfasstVonId).FirstOrDefault(id => id is not null);
        var name = erfasstVon is { } id ? await db.Users.Where(u => u.Id == id).Select(u => u.Vorname + " " + u.Nachname).FirstOrDefaultAsync(ct) : null;
        return new Appell(klasse.Id, klasse.Name, datum, periode?.Titel, sperre, erfasstVon is not null, name,
            zeilen.Select(z => new AppellZeile(z.Id, z.SchuelerId, $"{z.Nachname} {z.Vorname}".Trim(), z.Matrikel,
                z.A?.Status ?? AnwesenheitsStatus.Present, z.A?.Minuten, z.A?.Entschuldigt ?? false, z.A?.Bemerkung)).ToList());
    }

    /// <summary>Appell speichern (Upsert je Schüler und Tag); fehlende Zeilen gelten als « présent ».</summary>
    public async Task<int> AppellSpeichernAsync(Guid akteurId, Guid klasseId, DateOnly datum, IReadOnlyList<AppellEingabe> eingaben, CancellationToken ct = default)
    {
        return await dbFactory.TransaktionAsync(async db =>
        {
            var (a, klasse, jahr) = await KlasseFuerAppellAsync(db, akteurId, klasseId, ct);
            var (sperre, _) = await PruefeTagAsync(db, klasse, jahr, datum, ct);
            if (sperre is not null) throw new FachFehler(sperre);
            var einschreibungen = await AmTag(db, klasse.Id, datum).Select(e => e.Id).ToListAsync(ct);
            var fremd = eingaben.Select(e => e.EinschreibungId).Except(einschreibungen).Any();
            if (fremd) throw new FachFehler("La liste de la classe a changé : rechargez l'appel.");
            var vorhanden = await db.Anwesenheiten.Where(x => einschreibungen.Contains(x.EinschreibungId) && x.Datum == datum).ToListAsync(ct);
            foreach (var id in einschreibungen)
            {
                var e = eingaben.FirstOrDefault(x => x.EinschreibungId == id) ?? new AppellEingabe(id, AnwesenheitsStatus.Present, null, false, null);
                var (status, minuten, entschuldigt, bemerkung) = Normalisiere(e);
                var eintrag = vorhanden.FirstOrDefault(x => x.EinschreibungId == id);
                if (eintrag is null)
                {
                    eintrag = new Anwesenheit { SchuleId = a.SchuleId, EinschreibungId = id, Datum = datum };
                    db.Anwesenheiten.Add(eintrag);
                }
                eintrag.Status = status;
                eintrag.Minuten = minuten;
                eintrag.Entschuldigt = entschuldigt;
                eintrag.Bemerkung = bemerkung;
                eintrag.ErfasstVonId = a.Id;
            }
            await db.SaveChangesAsync(ct);
            return einschreibungen.Count;
        }, ct);
    }

    /// <summary>Minuten nur bei Verspätung (1–1440), « justifié » nur bei Abwesenheit/Verspätung, Bemerkung ≤ 300.</summary>
    public static (AnwesenheitsStatus, int?, bool, string?) Normalisiere(AppellEingabe e)
    {
        if (!Enum.IsDefined(e.Status)) throw new FachFehler("Statut de présence inconnu.");
        var bemerkung = Eingabe.Text(e.Bemerkung, 300, "la remarque");
        return e.Status switch
        {
            AnwesenheitsStatus.Present => (e.Status, null, false, bemerkung),
            AnwesenheitsStatus.Absent => (e.Status, null, e.Entschuldigt, bemerkung),
            _ => e.Minuten is >= 1 and <= MaxMinuten
                ? (e.Status, e.Minuten, e.Entschuldigt, bemerkung)
                : throw new FachFehler("Indiquez la durée du retard en minutes (1 à 1440).")
        };
    }

    /// <summary>Übersicht einer Klasse im Zeitraum: Abwesenheiten, davon entschuldigt, Verspätungen je Schüler.</summary>
    public async Task<KlassenUebersicht> KlassenUebersichtAsync(Guid akteurId, Guid klasseId, DateOnly von, DateOnly bis, CancellationToken ct = default)
    {
        if (bis < von) (von, bis) = (bis, von);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (_, klasse, _) = await KlasseFuerAppellAsync(db, akteurId, klasseId, ct);
        var einschreibungen = SchulAbfragen.GueltigeEinschreibungen(db).Where(e => e.KlasseId == klasse.Id && e.Von <= bis && (e.Bis == null || e.Bis >= von));
        var schueler = await (from e in einschreibungen.AsNoTracking()
                              join k in db.Users on e.SchuelerId equals k.Id
                              select new { EinschreibungId = e.Id, SchuelerId = k.Id, k.Vorname, k.Nachname })
            .ToListAsync(ct);
        var ids = schueler.Select(s => s.EinschreibungId).ToList();
        var eintraege = await db.Anwesenheiten.AsNoTracking().Where(x => ids.Contains(x.EinschreibungId) && x.Datum >= von && x.Datum <= bis)
            .Select(x => new { x.EinschreibungId, x.Datum, x.Status, x.Entschuldigt, x.Minuten }).ToListAsync(ct);
        var zeilen = schueler.GroupBy(s => s.SchuelerId)
            .Select(g =>
            {
                var meine = eintraege.Where(x => g.Any(s => s.EinschreibungId == x.EinschreibungId)).ToList();
                var erster = g.First();
                return new KlassenUebersichtZeile(g.Key, $"{erster.Nachname} {erster.Vorname}".Trim(),
                    Summe(meine.Select(m => (m.Status, m.Entschuldigt, m.Minuten))));
            })
            .OrderBy(z => z.Name).ToList();
        return new KlassenUebersicht(klasse.Name, von, bis, eintraege.Select(x => x.Datum).Distinct().Count(), zeilen);
    }

    /// <summary>Anwesenheit eines Schülers im Schuljahr (Direktion, Lehrkraft der Klasse, Elternteil, der Schüler selbst).</summary>
    public async Task<SchuelerAnwesenheit> SchuelerAsync(Guid akteurId, Guid schuelerId, Guid? jahrId = null, CancellationToken ct = default)
    {
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        Zugriff.Pruefe(akteur, Faehigkeit.NotenErfassen);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var schueler = await db.Users.AsNoTracking().Where(k => k.Id == schuelerId && k.Rolle == Rollen.Schueler)
            .Select(k => new { k.Id, k.SchuleId, k.Vorname, k.Nachname }).FirstOrDefaultAsync(ct) ?? throw new FachFehler("Cet élève est introuvable.");
        var erlaubt = akteur.Rolle switch
        {
            Rollen.Schueler => akteur.Id == schuelerId,
            Rollen.Eltern => await SchulAbfragen.IstKindAsync(db, akteur.Id, schuelerId, ct),
            Rollen.Lehrer => await SchulAbfragen.UnterrichtetSchuelerAsync(db, akteur.Id, schuelerId, ct),
            Rollen.Direktor or Rollen.Koordinator => akteur.SchuleId == schueler.SchuleId,
            _ => false
        };
        if (!erlaubt || akteur.SchuleId != schueler.SchuleId) throw new KeinZugriffFehler();

        var jahr = jahrId is { } jid
            ? await db.Schuljahre.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jid && j.SchuleId == schueler.SchuleId, ct)
            : await SchulAbfragen.LaufendesSchuljahrAsync(db, schueler.SchuleId!.Value, ct);
        if (jahr is null) return new SchuelerAnwesenheit(schueler.Id, $"{schueler.Vorname} {schueler.Nachname}", null, AnwesenheitsSumme.Leer, []);
        var alle = await (from x in db.Anwesenheiten.AsNoTracking()
                          join e in SchulAbfragen.GueltigeEinschreibungen(db) on x.EinschreibungId equals e.Id
                          join k in db.Klassen on e.KlasseId equals k.Id
                          where e.SchuelerId == schuelerId && e.SchuljahrId == jahr.Id && x.Status != AnwesenheitsStatus.Present
                          orderby x.Datum descending
                          select new AnwesenheitsEintrag(x.Datum, x.Status, x.Entschuldigt, x.Minuten, x.Bemerkung, k.Name)).ToListAsync(ct);
        return new SchuelerAnwesenheit(schueler.Id, $"{schueler.Vorname} {schueler.Nachname}".Trim(), jahr.Bezeichnung,
            Summe(alle.Select(x => (x.Status, x.Entschuldigt, x.Minuten))), alle.Take(100).ToList());
    }

    public static AnwesenheitsSumme Summe(IEnumerable<(AnwesenheitsStatus Status, bool Entschuldigt, int? Minuten)> eintraege)
    {
        int abs = 0, absJ = 0, ret = 0, min = 0;
        foreach (var (status, entschuldigt, minuten) in eintraege)
        {
            if (status == AnwesenheitsStatus.Absent) { abs++; if (entschuldigt) absJ++; }
            else if (status == AnwesenheitsStatus.Retard) { ret++; min += minuten ?? 0; }
        }
        return new AnwesenheitsSumme(abs, absJ, ret, min);
    }
}
