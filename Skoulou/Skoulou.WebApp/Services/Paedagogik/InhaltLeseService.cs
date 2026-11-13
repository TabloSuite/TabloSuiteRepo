using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Paedagogik;

/// <summary>
/// Lesende Sichten eines Inhalts: Vorschau mit Lösungen für Lehrkraft/Direktion und Druckansicht
/// (Personal mit oder ohne Lösungen; Schüler nur Leçons, die ihnen zugewiesen sind, ohne Lösungen).
/// </summary>
public sealed class InhaltLeseService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, TimeProvider zeit)
{
    /// <summary>Vorschau (Lehrkraft: eigene; Direktion: alle der Schule).</summary>
    public async Task<InhaltAnsicht> AnsichtAsync(Guid akteurId, InhaltsArt art, Guid id, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.LeserAsync(zugriff, akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await LadeAsync(db, akteur, art, id, pruefen: true, ct);
    }

    private static async Task<InhaltAnsicht> LadeAsync(SkoulouDbContext db, Akteur akteur, InhaltsArt art, Guid id, bool pruefen, CancellationToken ct)
    {
        Guid schuleId, autorId, stufeId, fachId;
        Guid? erfasstVon, video = null;
        string titel;
        string? zusammenfassung = null, beschreibung = null;
        InhaltsStatus status;
        DateTime geaendert;
        int dauer = 0;
        decimal grenze = 0;
        IReadOnlyList<string> seiten = [];
        if (art == InhaltsArt.Lecon)
        {
            var l = await db.Lecons.AsNoTracking().Include(x => x.Seiten).FirstOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new FachFehler(InhaltsRechte.MeldungNichtGefunden);
            (schuleId, autorId, stufeId, fachId, erfasstVon, titel, status, geaendert) = (l.SchuleId, l.AutorId, l.StufeId, l.FachId, l.ErfasstVonId, l.Titel, l.Status, l.GeaendertAmUtc);
            zusammenfassung = l.Zusammenfassung;
            video = l.VideoMediumId;
            seiten = l.Seiten.OrderBy(s => s.Nr).Select(s => s.Html).ToList();
        }
        else
        {
            var p = await db.Pruefungen.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new FachFehler(InhaltsRechte.MeldungNichtGefunden);
            (schuleId, autorId, stufeId, fachId, erfasstVon, titel, status, geaendert) = (p.SchuleId, p.AutorId, p.StufeId, p.FachId, p.ErfasstVonId, p.Titel, p.Status, p.GeaendertAmUtc);
            beschreibung = p.Beschreibung;
            dauer = p.DauerMinuten;
            grenze = p.Bestehensgrenze;
        }
        if (pruefen) InhaltsRechte.PruefeInhalt(akteur, schuleId, autorId);

        var aktuelle = await FragenAbbildung.AktuelleAsync(db, art == InhaltsArt.Lecon ? id : null, art == InhaltsArt.Pruefung ? id : null, ct);
        var nr = 0;
        var bloecke = aktuelle.Select(a => new AnsichtBlock(a.Block.Id, a.Block.Nr, a.Block.Titel, a.Block.Consigne,
            a.Fragen.Select(f => FragenAbbildung.Ansicht(f, ++nr)).ToList())).ToList();
        var namen = await db.Users.Where(k => k.Id == autorId || k.Id == erfasstVon)
            .ToDictionaryAsync(k => k.Id, k => k.Vorname + " " + k.Nachname, ct);
        var stufe = await db.Stufen.Where(s => s.Id == stufeId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "";
        var fach = await db.Faecher.Where(f => f.Id == fachId).Select(f => f.Name).FirstOrDefaultAsync(ct) ?? "";
        var darf = akteur.SchuleId == schuleId && (akteur.IstDirektion || akteur.Id == autorId);
        var kopf = new InhaltKopf(id, art, titel, stufe, stufeId, fach, fachId, status, autorId, namen.GetValueOrDefault(autorId) ?? "",
            erfasstVon is { } e ? namen.GetValueOrDefault(e) : null, zusammenfassung, dauer, grenze,
            bloecke.Sum(b => b.Fragen.Count), bloecke.SelectMany(b => b.Fragen).Sum(f => f.Punkte), darf, geaendert);
        return new InhaltAnsicht(kopf, seiten, video, beschreibung, bloecke);
    }

    /// <summary>Druckdaten; Lösungen nur für Personal. Schüler: nur zugewiesene Leçons (über die Zuweisung).</summary>
    public async Task<DruckDaten> DruckAsync(Guid akteurId, InhaltsArt art, Guid id, bool mitLoesungen, Guid? zuweisungId, CancellationToken ct = default)
    {
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        string? klasse = null;
        InhaltAnsicht ansicht;
        if (akteur.Rolle == Rollen.Schueler)
        {
            Zugriff.Pruefe(akteur, Faehigkeit.Lernen);
            if (art != InhaltsArt.Lecon || zuweisungId is not { } zid) throw new KeinZugriffFehler(VersuchsLogik.MeldungNichtZugewiesen);
            var (z, _) = await VersuchsLogik.ZuweisungAsync(db, akteur, zid, zeit.GetUtcNow().UtcDateTime, auchFruehere: true, ct);
            if (z.LeconId != id) throw new KeinZugriffFehler(VersuchsLogik.MeldungNichtZugewiesen);
            klasse = await db.Klassen.Where(k => k.Id == z.KlasseId).Select(k => k.Name).FirstOrDefaultAsync(ct);
            ansicht = await LadeAsync(db, akteur, art, id, pruefen: false, ct);
            mitLoesungen = false;
        }
        else
        {
            Zugriff.Pruefe(akteur, Faehigkeit.InhalteErstellen);
            ansicht = await LadeAsync(db, akteur, art, id, pruefen: true, ct);
            if (zuweisungId is { } zid)
                klasse = await db.Zuweisungen.Where(z => z.Id == zid && (z.LeconId == id || z.PruefungId == id))
                    .Select(z => db.Klassen.Where(k => k.Id == z.KlasseId).Select(k => k.Name).FirstOrDefault()).FirstOrDefaultAsync(ct);
        }
        var schule = await db.Schulen.Where(s => s.Id == akteur.SchuleId).Select(s => s.OffizielleBezeichnung ?? s.Name).FirstOrDefaultAsync(ct) ?? "";
        var k = ansicht.Kopf;
        return new DruckDaten(schule, klasse, k.AutorName, k.Titel, k.Fach, k.Stufe, art, ansicht.Seiten, ansicht.Bloecke, mitLoesungen,
            k.DauerMinuten, k.MaxPunkte, ansicht.Beschreibung);
    }
}
