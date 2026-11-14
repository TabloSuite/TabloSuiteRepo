using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Noten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Noten;

public sealed record KlassenratZeile(Guid EinschreibungId, Guid SchuelerId, string Name, string? Matrikel, Mittel Moyenne, RangInfo Rang,
    Distinktion Vorschlag, Distinktion? Distinktion, Sanktionen Sanktionen, string? Bemerkung, int Fehlend,
    JahresEntscheidungsArt Entscheidung, Guid? ZielStufeId, string? EntscheidungBemerkung, bool EntscheidungAusgefuehrt,
    Guid? BulletinId, string? BulletinNummer, BulletinStatus? BulletinStatus, string Hash)
{
    public Distinktion Wirksam => Distinktion ?? Vorschlag;
}

public sealed record KlassenratUebersicht(Guid KlasseId, string Klasse, Guid PeriodeId, string Periode, bool IstLetztePeriode, int Effectif,
    KlassenStatistik Statistik, IReadOnlyList<KlassenratZeile> Zeilen, IReadOnlyList<FehlendeNote> Fehlend, VeroeffentlichungsStatus? Status,
    DateTime? VeroeffentlichtAmUtc, string? RectificationGrund, bool Direktion, string? ConseilSperre, string KlassenHash,
    IReadOnlyList<AuswahlEintrag> Stufen)
{
    public int NonClasses => Zeilen.Count(z => !z.Rang.Klassiert);
}

public sealed record ConseilEingabe(Distinktion? Distinktion, Sanktionen Sanktionen, string? Bemerkung);

public sealed record EntscheidungEingabe(JahresEntscheidungsArt Entscheidung, Guid? ZielStufeId, string? Bemerkung);

/// <summary>Auswahl für /bulletins: Klassen (Direktion alle, Professeur principal seine), Perioden des Jahres.</summary>
public sealed record BulletinAuswahl(IReadOnlyList<AuswahlEintrag> Klassen, IReadOnlyList<PeriodeAuswahl> Perioden, bool Direktion);

/// <summary>
/// Conseil de classe (Bauplan §4.3, §11.15): Übersicht je Klasse und Periode (Moyennes, Ränge, Vollständigkeit mit Liste
/// der fehlenden Noten), Beschlüsse (Distinktion, Sanktionen, Bemerkung) und Jahresentscheidungen in der letzten Periode.
/// Der Professeur principal bereitet vor; gesperrt nach der Veröffentlichung (außer Rectification).
/// </summary>
public sealed class KlassenratService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, TimeProvider zeit)
{
    public async Task<BulletinAuswahl> AuswahlAsync(Guid akteurId, Guid jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KlassenratErfassen, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
        var q = db.Klassen.AsNoTracking().Where(k => k.SchuljahrId == jahr.Id && k.Aktiv);
        if (!a.IstDirektion) q = q.Where(k => k.KlassenlehrerId == a.Id);
        var aktive = SchulAbfragen.AktiveEinschreibungen(db);
        var klassen = await q.OrderBy(k => k.Name).Select(k => new { k.Id, k.Name, Anzahl = aktive.Count(e => e.KlasseId == k.Id) }).ToListAsync(ct);
        return new BulletinAuswahl(klassen.Select(k => new AuswahlEintrag(k.Id, k.Name, $"{k.Anzahl} élève{(k.Anzahl > 1 ? "s" : "")}")).ToList(),
            await NotenService.PeriodenAsync(db, jahr, zeit, ct), a.IstDirektion);
    }

    public async Task<KlassenratUebersicht> UebersichtAsync(Guid akteurId, Guid klasseId, Guid periodeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (a, klasse, direktion) = await zugriff.KlasseFuerConseilAsync(db, akteurId, klasseId, ct);
        var periode = await NotenRechte.PeriodeAsync(db, klasse, periodeId, ct);
        var b = await BulletinBerechnung.LadeAsync(db, klasse, periode, ct);
        var pub = await db.KlassenVeroeffentlichungen.AsNoTracking().FirstOrDefaultAsync(v => v.KlasseId == klasse.Id && v.PeriodeId == periode.Id, ct);
        var ids = b.Kohorte.Select(k => k.EinschreibungId).ToList();
        var bulletins = await db.Bulletins.AsNoTracking().Where(x => x.PeriodeId == periode.Id && ids.Contains(x.EinschreibungId) && x.Status == BulletinStatus.Publie)
            .Select(x => new { x.Id, x.EinschreibungId, x.Nummer, x.Status }).ToListAsync(ct);
        var stufen = await db.Stufen.AsNoTracking().Where(s => s.SchuleId == a.SchuleId && s.Aktiv).OrderBy(s => s.Reihenfolge)
            .Select(s => new AuswahlEintrag(s.Id, s.Name, null)).ToListAsync(ct);
        var (inhalte, klassenHash) = b.AlleInhalte();
        var zeilen = inhalte.Select(x =>
        {
            var s = x.Schueler;
            var e = b.ErgebnisVon(s.SchuelerId);
            b.Conseil.TryGetValue(s.EinschreibungId, out var c);
            b.Entscheidungen.TryGetValue(s.EinschreibungId, out var je);
            var bul = bulletins.FirstOrDefault(y => y.EinschreibungId == s.EinschreibungId);
            return new KlassenratZeile(s.EinschreibungId, s.SchuelerId, s.Name, s.Matrikel, e.Moyenne, e.Rang, NotenTexte.DistinktionVorschlag(e.Moyenne.Wert),
                c?.Distinktion, c?.Sanktionen ?? Sanktionen.Keine, c?.Bemerkung, b.Fehlend.Count(f => f.EinschreibungId == s.EinschreibungId),
                je?.Entscheidung ?? JahresEntscheidungsArt.Offen, je?.ZielStufeId, je?.Bemerkung, je?.AusgefuehrtAmUtc is not null,
                bul?.Id, bul?.Nummer, bul?.Status, x.Hash);
        }).ToList();
        return new KlassenratUebersicht(klasse.Id, klasse.Name, periode.Id, periode.Titel, b.IstLetztePeriode, b.Kohorte.Count, b.Ergebnis.Statistik,
            zeilen, b.Fehlend, pub?.Status, pub?.VeroeffentlichtAmUtc, pub?.RectificationGrund, direktion,
            NotenSperren.Conseil(b.Jahr, periode, pub?.Status), klassenHash, stufen);
    }

    public async Task ConseilSpeichernAsync(Guid akteurId, Guid periodeId, Guid einschreibungId, ConseilEingabe e, CancellationToken ct = default)
    {
        var bemerkung = NotenFormat.Text(e.Bemerkung, 300, "L'appréciation du conseil");
        await dbFactory.TransaktionAsync(async db =>
        {
            var (a, klasse, periode) = await KohorteAsync(db, akteurId, periodeId, einschreibungId, ct);
            var jahr = await db.Schuljahre.AsNoTracking().FirstAsync(j => j.Id == klasse.SchuljahrId, ct);
            if (NotenSperren.Conseil(jahr, periode, await NotenSperren.StatusAsync(db, klasse.Id, periode.Id, ct)) is { } sperre) throw new FachFehler(sperre);
            var eintrag = await db.KlassenratEintraege.FirstOrDefaultAsync(k => k.PeriodeId == periode.Id && k.EinschreibungId == einschreibungId, ct);
            var leer = e.Distinktion is null && e.Sanktionen == Sanktionen.Keine && bemerkung is null;
            if (leer)
            {
                if (eintrag is not null) db.KlassenratEintraege.Remove(eintrag);
            }
            else
            {
                if (eintrag is null)
                {
                    eintrag = new KlassenratEintrag { SchuleId = a.SchuleId, PeriodeId = periode.Id, EinschreibungId = einschreibungId };
                    db.KlassenratEintraege.Add(eintrag);
                }
                eintrag.Distinktion = e.Distinktion;
                eintrag.Sanktionen = e.Sanktionen;
                eintrag.Bemerkung = bemerkung;
                eintrag.ErfasstVonId = a.Id;
            }
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task EntscheidungSpeichernAsync(Guid akteurId, Guid periodeId, Guid einschreibungId, EntscheidungEingabe e, CancellationToken ct = default)
    {
        var bemerkung = NotenFormat.Text(e.Bemerkung, 300, "La remarque");
        await dbFactory.TransaktionAsync(async db =>
        {
            var (a, klasse, periode) = await KohorteAsync(db, akteurId, periodeId, einschreibungId, ct);
            var jahr = await db.Schuljahre.AsNoTracking().FirstAsync(j => j.Id == klasse.SchuljahrId, ct);
            if (jahr.Status == SchuljahrStatus.Abgeschlossen) throw new FachFehler($"L'année scolaire {jahr.Bezeichnung} est clôturée.");
            if (e.ZielStufeId is { } ziel && !await db.Stufen.AnyAsync(s => s.Id == ziel && s.SchuleId == a.SchuleId, ct))
                throw new FachFehler("Ce niveau est introuvable.");
            var eintrag = await db.Jahresentscheidungen.FirstOrDefaultAsync(j => j.EinschreibungId == einschreibungId, ct);
            if (eintrag?.AusgefuehrtAmUtc is not null) throw new FachFehler("Cette décision a déjà été appliquée lors du passage d'année.");
            if (eintrag is null)
            {
                eintrag = new Jahresentscheidung { SchuleId = a.SchuleId, EinschreibungId = einschreibungId };
                db.Jahresentscheidungen.Add(eintrag);
            }
            eintrag.Entscheidung = e.Entscheidung;
            eintrag.ZielStufeId = e.Entscheidung == JahresEntscheidungsArt.Admis || e.Entscheidung == JahresEntscheidungsArt.Orientation ? e.ZielStufeId : null;
            eintrag.Bemerkung = bemerkung;
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    /// <summary>Einschreibung muss zur Kohorte der Klasse am Periodenende gehören.</summary>
    private async Task<(SchulAkteur A, Klasse Klasse, Periode Periode)> KohorteAsync(SkoulouDbContext db, Guid akteurId, Guid periodeId, Guid einschreibungId, CancellationToken ct)
    {
        var e = await db.Einschreibungen.AsNoTracking().FirstOrDefaultAsync(x => x.Id == einschreibungId && x.StorniertAmUtc == null, ct)
                ?? throw new FachFehler("Cette inscription est introuvable.");
        var (a, klasse, _) = await zugriff.KlasseFuerConseilAsync(db, akteurId, e.KlasseId, ct);
        var periode = await NotenRechte.PeriodeAsync(db, klasse, periodeId, ct);
        if (e.Von > periode.Ende || (e.Bis is { } bis && bis < periode.Ende))
            throw new FachFehler("Cet élève ne fait pas partie de la classe à la fin de la période.");
        return (a, klasse, periode);
    }
}
