using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Noten;

public sealed record BewertungEingabe(string? Titel, BewertungsArt Art, DateOnly? Datum, decimal Bareme, decimal Gewicht, Guid? KomponenteId = null);

public sealed record KomponenteZeile(Guid Id, Guid PeriodeId, string Titel, BewertungsArt Art, decimal Gewicht, decimal Bareme, int Reihenfolge, int Nutzung);

public sealed record KomponenteEingabe(string? Titel, BewertungsArt Art, decimal Gewicht, decimal Bareme);

public sealed record PeriodeMitKomponenten(Guid PeriodeId, string Titel, bool ZaehltFuerJahr, PeriodenStatus Status, IReadOnlyList<KomponenteZeile> Komponenten);

/// <summary>
/// Bewertungen (Évaluations) je Klassenfach und Periode sowie die Komponenten je Periode (Vorlage, Bauplan §11.14).
/// Barème und Gewicht einer Bewertung ändern sich nur, solange keine Note existiert; löschen ebenso.
/// </summary>
public sealed class BewertungService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff)
{
    public const decimal MaxBareme = 1000;
    public const decimal MaxGewicht = 100;

    public async Task<Guid> AnlegenAsync(Guid akteurId, Guid klassenfachId, Guid periodeId, BewertungEingabe e, CancellationToken ct = default)
    {
        var (titel, bareme, gewicht) = Pruefe(e.Titel, e.Bareme, e.Gewicht);
        return await dbFactory.TransaktionAsync(async db =>
        {
            var (a, k) = await zugriff.KlassenfachAsync(db, akteurId, klassenfachId, schreiben: true, ct);
            var periode = await StrukturOffenAsync(db, k, periodeId, ct);
            PruefeDatum(e.Datum, periode);
            var komponente = await KomponenteAsync(db, e.KomponenteId, periode.Id, ct);
            var b = new Bewertung
            {
                SchuleId = a.SchuleId, KlassenfachId = k.Kf.Id, PeriodeId = periode.Id, KomponenteId = komponente, Titel = titel, Art = e.Art,
                Datum = e.Datum, Bareme = bareme, Gewicht = gewicht, ErstelltVonId = a.Id
            };
            db.Bewertungen.Add(b);
            await db.SaveChangesAsync(ct);
            return b.Id;
        }, ct);
    }

    public async Task AendernAsync(Guid akteurId, Guid bewertungId, BewertungEingabe e, CancellationToken ct = default)
    {
        var (titel, bareme, gewicht) = Pruefe(e.Titel, e.Bareme, e.Gewicht);
        await dbFactory.TransaktionAsync(async db =>
        {
            var b = await db.Bewertungen.FirstOrDefaultAsync(x => x.Id == bewertungId, ct) ?? throw new FachFehler("Cette évaluation est introuvable.");
            var (_, k) = await zugriff.KlassenfachAsync(db, akteurId, b.KlassenfachId, schreiben: true, ct);
            var periode = await StrukturOffenAsync(db, k, b.PeriodeId, ct);
            PruefeDatum(e.Datum, periode);
            if ((b.Bareme != bareme || b.Gewicht != gewicht) && await db.Noten.AnyAsync(n => n.BewertungId == b.Id, ct))
                throw new FachFehler("Des notes sont déjà saisies : le barème et le poids ne peuvent plus changer (le titre, le type et la date oui).");
            b.Titel = titel;
            b.Art = e.Art;
            b.Datum = e.Datum;
            b.Bareme = bareme;
            b.Gewicht = gewicht;
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task LoeschenAsync(Guid akteurId, Guid bewertungId, CancellationToken ct = default)
    {
        await dbFactory.TransaktionAsync(async db =>
        {
            var b = await db.Bewertungen.FirstOrDefaultAsync(x => x.Id == bewertungId, ct) ?? throw new FachFehler("Cette évaluation est introuvable.");
            var (_, k) = await zugriff.KlassenfachAsync(db, akteurId, b.KlassenfachId, schreiben: true, ct);
            await StrukturOffenAsync(db, k, b.PeriodeId, ct);
            if (await db.Noten.AnyAsync(n => n.BewertungId == b.Id, ct))
                throw new FachFehler("Cette évaluation a déjà des notes (ou un historique) : elle ne peut plus être supprimée.");
            db.Bewertungen.Remove(b);
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    /// <summary>Legt je noch nicht genutzter Komponente der Periode eine Bewertung an (Vorschlag). Anzahl neu.</summary>
    public async Task<int> AusKomponentenAsync(Guid akteurId, Guid klassenfachId, Guid periodeId, CancellationToken ct = default)
    {
        return await dbFactory.TransaktionAsync(async db =>
        {
            var (a, k) = await zugriff.KlassenfachAsync(db, akteurId, klassenfachId, schreiben: true, ct);
            var periode = await StrukturOffenAsync(db, k, periodeId, ct);
            var genutzt = await db.Bewertungen.Where(b => b.KlassenfachId == k.Kf.Id && b.PeriodeId == periode.Id && b.KomponenteId != null)
                .Select(b => b.KomponenteId!.Value).ToListAsync(ct);
            var komponenten = await db.PeriodenKomponenten.Where(x => x.PeriodeId == periode.Id && !genutzt.Contains(x.Id)).OrderBy(x => x.Reihenfolge).ToListAsync(ct);
            if (komponenten.Count == 0)
                throw new FachFehler(genutzt.Count > 0 ? "Toutes les composantes de la période sont déjà créées." : "Aucune composante n'est définie pour cette période (Année scolaire › Composantes).");
            foreach (var x in komponenten)
                db.Bewertungen.Add(new Bewertung
                {
                    SchuleId = a.SchuleId, KlassenfachId = k.Kf.Id, PeriodeId = periode.Id, KomponenteId = x.Id, Titel = x.Titel, Art = x.Art,
                    Bareme = x.Bareme, Gewicht = x.Gewicht, ErstelltVonId = a.Id
                });
            await db.SaveChangesAsync(ct);
            return komponenten.Count;
        }, ct);
    }

    // ---------- Komponenten je Periode (Direktion) ----------

    /// <summary>Komponenten aller Perioden eines Jahres (lesen: Direktion und Lehrkräfte für Vorschläge).</summary>
    public async Task<IReadOnlyList<PeriodeMitKomponenten>> KomponentenAsync(Guid akteurId, Guid jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.PersonalAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        var perioden = await db.Perioden.AsNoTracking().Where(p => p.SchuljahrId == jahr.Id).OrderBy(p => p.Beginn).ThenBy(p => p.Nr).ToListAsync(ct);
        var ids = perioden.Select(p => p.Id).ToList();
        var komponenten = await db.PeriodenKomponenten.AsNoTracking().Where(x => ids.Contains(x.PeriodeId))
            .OrderBy(x => x.Reihenfolge).ThenBy(x => x.Titel)
            .Select(x => new KomponenteZeile(x.Id, x.PeriodeId, x.Titel, x.Art, x.Gewicht, x.Bareme, x.Reihenfolge, db.Bewertungen.Count(b => b.KomponenteId == x.Id)))
            .ToListAsync(ct);
        return perioden.Select(p => new PeriodeMitKomponenten(p.Id, p.Titel, p.ZaehltFuerJahr, p.Status, komponenten.Where(x => x.PeriodeId == p.Id).ToList())).ToList();
    }

    public async Task<Guid> KomponenteAnlegenAsync(Guid akteurId, Guid periodeId, KomponenteEingabe e, CancellationToken ct = default)
    {
        var (titel, bareme, gewicht) = Pruefe(e.Titel, e.Bareme, e.Gewicht, 80);
        return await dbFactory.TransaktionAsync(async db =>
        {
            var (a, periode) = await PeriodeFuerKomponentenAsync(db, akteurId, periodeId, ct);
            var vorhandene = await db.PeriodenKomponenten.Where(x => x.PeriodeId == periode.Id).ToListAsync(ct);
            if (vorhandene.Any(x => string.Equals(x.Titel, titel, StringComparison.OrdinalIgnoreCase)))
                throw new FachFehler($"La composante « {titel} » existe déjà dans cette période.", "titel");
            var k = new PeriodenKomponente
            {
                SchuleId = a.SchuleId, PeriodeId = periode.Id, Titel = titel, Art = e.Art, Gewicht = gewicht, Bareme = bareme,
                Reihenfolge = vorhandene.Count == 0 ? 1 : vorhandene.Max(x => x.Reihenfolge) + 1
            };
            db.PeriodenKomponenten.Add(k);
            await db.SaveChangesAsync(ct);
            return k.Id;
        }, ct);
    }

    /// <summary>Änderungen wirken nur auf künftige Vorschläge; bestehende Bewertungen behalten ihre Werte.</summary>
    public async Task KomponenteAendernAsync(Guid akteurId, Guid komponenteId, KomponenteEingabe e, CancellationToken ct = default)
    {
        var (titel, bareme, gewicht) = Pruefe(e.Titel, e.Bareme, e.Gewicht, 80);
        await dbFactory.TransaktionAsync(async db =>
        {
            var k = await db.PeriodenKomponenten.FirstOrDefaultAsync(x => x.Id == komponenteId, ct) ?? throw new FachFehler("Cette composante est introuvable.");
            await PeriodeFuerKomponentenAsync(db, akteurId, k.PeriodeId, ct);
            if (await db.PeriodenKomponenten.AnyAsync(x => x.PeriodeId == k.PeriodeId && x.Id != k.Id && x.Titel == titel, ct))
                throw new FachFehler($"La composante « {titel} » existe déjà dans cette période.", "titel");
            (k.Titel, k.Art, k.Bareme, k.Gewicht) = (titel, e.Art, bareme, gewicht);
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    /// <summary>Löschen: bereits angelegte Bewertungen bleiben (nur die Verknüpfung entfällt).</summary>
    public async Task KomponenteLoeschenAsync(Guid akteurId, Guid komponenteId, CancellationToken ct = default)
    {
        await dbFactory.TransaktionAsync(async db =>
        {
            var k = await db.PeriodenKomponenten.FirstOrDefaultAsync(x => x.Id == komponenteId, ct) ?? throw new FachFehler("Cette composante est introuvable.");
            await PeriodeFuerKomponentenAsync(db, akteurId, k.PeriodeId, ct);
            foreach (var b in await db.Bewertungen.Where(b => b.KomponenteId == k.Id).ToListAsync(ct)) b.KomponenteId = null;
            await db.SaveChangesAsync(ct);
            db.PeriodenKomponenten.Remove(k);
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    /// <summary>
    /// Modèle standard (Interrogation ×1, Devoir ×1, Composition ×2, sur 20) für alle Perioden des Jahres ohne Komponenten,
    /// bzw. Übernahme der Komponenten einer Periode in alle anderen Perioden ohne Komponenten. Anzahl versorgter Perioden.
    /// </summary>
    public async Task<int> KomponentenVerteilenAsync(Guid akteurId, Guid jahrId, Guid? vorlagePeriodeId, CancellationToken ct = default)
    {
        return await dbFactory.TransaktionAsync(async db =>
        {
            var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, ct: ct);
            var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
            OrgAbfragen.PruefeOffen(jahr);
            var perioden = await db.Perioden.Where(p => p.SchuljahrId == jahr.Id).Select(p => p.Id).ToListAsync(ct);
            var mitKomponenten = await db.PeriodenKomponenten.Where(x => perioden.Contains(x.PeriodeId)).Select(x => x.PeriodeId).Distinct().ToListAsync(ct);
            List<(string Titel, BewertungsArt Art, decimal Gewicht, decimal Bareme)> muster;
            if (vorlagePeriodeId is { } vp)
            {
                if (!perioden.Contains(vp)) throw new FachFehler("Cette période n'appartient pas à l'année.");
                muster = (await db.PeriodenKomponenten.Where(x => x.PeriodeId == vp).OrderBy(x => x.Reihenfolge).ToListAsync(ct))
                    .Select(x => (x.Titel, x.Art, x.Gewicht, x.Bareme)).ToList();
                if (muster.Count == 0) throw new FachFehler("Cette période n'a pas encore de composantes à recopier.");
            }
            else
            {
                muster = [("Interrogation", BewertungsArt.Interrogation, 1, 20), ("Devoir", BewertungsArt.Devoir, 1, 20), ("Composition", BewertungsArt.Composition, 2, 20)];
            }
            var ziele = perioden.Except(mitKomponenten).ToList();
            foreach (var p in ziele)
            {
                var nr = 0;
                foreach (var m in muster)
                    db.PeriodenKomponenten.Add(new PeriodenKomponente { SchuleId = a.SchuleId, PeriodeId = p, Titel = m.Titel, Art = m.Art, Gewicht = m.Gewicht, Bareme = m.Bareme, Reihenfolge = ++nr });
            }
            await db.SaveChangesAsync(ct);
            return ziele.Count;
        }, ct);
    }

    private async Task<(SchulAkteur A, Periode Periode)> PeriodeFuerKomponentenAsync(SkoulouDbContext db, Guid akteurId, Guid periodeId, CancellationToken ct)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, ct: ct);
        var periode = await db.Perioden.FirstOrDefaultAsync(p => p.Id == periodeId, ct) ?? throw new FachFehler("Cette période est introuvable.");
        a.PruefeSchule(periode.SchuleId);
        OrgAbfragen.PruefeOffen(await db.Schuljahre.FirstAsync(j => j.Id == periode.SchuljahrId, ct));
        return (a, periode);
    }

    private static async Task<Periode> StrukturOffenAsync(SkoulouDbContext db, KlassenfachKontext k, Guid periodeId, CancellationToken ct)
    {
        var periode = await NotenRechte.PeriodeAsync(db, k.Klasse, periodeId, ct);
        if (NotenSperren.Struktur(k.Jahr, periode, await NotenSperren.StatusAsync(db, k.Klasse.Id, periode.Id, ct)) is { } sperre) throw new FachFehler(sperre);
        return periode;
    }

    private static async Task<Guid?> KomponenteAsync(SkoulouDbContext db, Guid? komponenteId, Guid periodeId, CancellationToken ct)
    {
        if (komponenteId is not { } id) return null;
        return await db.PeriodenKomponenten.AnyAsync(x => x.Id == id && x.PeriodeId == periodeId, ct) ? id : null;
    }

    private static void PruefeDatum(DateOnly? datum, Periode periode)
    {
        if (datum is { } d && (d < periode.Beginn || d > periode.Ende))
            throw new FachFehler($"La date doit être comprise dans la période ({SkoulouKultur.Datum(periode.Beginn)} – {SkoulouKultur.Datum(periode.Ende)}).", "datum");
    }

    private static (string Titel, decimal Bareme, decimal Gewicht) Pruefe(string? titel, decimal bareme, decimal gewicht, int maxTitel = 120)
    {
        var t = NotenFormat.Text(titel, maxTitel, "Le titre") ?? throw new FachFehler("Indiquez le titre de l'évaluation.", "titel");
        if (bareme <= 0 || bareme > MaxBareme || decimal.Round(bareme, 2) != bareme)
            throw new FachFehler("Le barème doit être compris entre 0,01 et 1000 (deux décimales au plus).", "bareme");
        if (gewicht <= 0 || gewicht > MaxGewicht || decimal.Round(gewicht, 2) != gewicht)
            throw new FachFehler("Le poids doit être compris entre 0,01 et 100 (deux décimales au plus).", "gewicht");
        return (t, bareme, gewicht);
    }
}
