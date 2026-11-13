using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Paedagogik;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Paedagogik;

/// <summary>Filter der Korrekturliste.</summary>
public sealed record KorrekturFilter(bool NurOffene = true, Guid? KlasseId = null, string? Suche = null);

/// <summary>
/// Eine Korrektur-Warteschlange für Übungen und Examen (unabhängig von der Frist), Nachkorrektur mit
/// Protokoll, Ergebnisse je Zuweisung und Versuchsansicht. Lehrkraft: eigene Zuweisungen; Direktion:
/// alle Zuweisungen der Schule (korrigiert für die Lehrkraft).
/// </summary>
public sealed class KorrekturService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, TimeProvider zeit)
{
    private async Task<Akteur> KorrektorAsync(Guid akteurId, CancellationToken ct)
    {
        var akteur = await zugriff.LadeAkteurAsync(akteurId, ct);
        Zugriff.Pruefe(akteur, Faehigkeit.Korrigieren, schreiben: true);
        return akteur;
    }

    private static void PruefeZuweisung(Akteur akteur, Zuweisung z)
    {
        if (akteur.SchuleId != z.SchuleId) throw new KeinZugriffFehler(Zugriff.MeldungAndereSchule);
        if (!akteur.IstDirektion && z.LehrerId != akteur.Id)
            throw new KeinZugriffFehler("Vous ne pouvez corriger que les contenus que vous avez attribués.");
    }

    /// <summary>Zuweisungen, die der Akteur korrigieren darf.</summary>
    private static IQueryable<Zuweisung> Erlaubte(SkoulouDbContext db, Akteur akteur)
        => akteur.IstDirektion
            ? db.Zuweisungen.Where(z => z.SchuleId == akteur.SchuleId)
            : db.Zuweisungen.Where(z => z.SchuleId == akteur.SchuleId && z.LehrerId == akteur.Id);

    /// <summary>Anzahl offener Korrekturen (Startseite, Navigation).</summary>
    public async Task<int> OffeneAnzahlAsync(Guid akteurId, CancellationToken ct = default)
    {
        var akteur = await KorrektorAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var zuweisungen = Erlaubte(db, akteur).Select(z => z.Id);
        return await db.Antworten.CountAsync(a => a.Punkte == null && a.Korrektur == KorrekturArt.Manuelle
            && db.Versuche.Any(v => v.Id == a.VersuchId && zuweisungen.Contains(v.ZuweisungId)), ct);
    }

    /// <summary>Klassen, in denen es Zuweisungen des Akteurs gibt (Filter).</summary>
    public async Task<IReadOnlyList<AuswahlEintrag>> KlassenAsync(Guid akteurId, CancellationToken ct = default)
    {
        var akteur = await KorrektorAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var klassen = Erlaubte(db, akteur).Select(z => z.KlasseId).Distinct();
        return await db.Klassen.AsNoTracking().Where(k => klassen.Contains(k.Id)).OrderBy(k => k.Name)
            .Select(k => new AuswahlEintrag(k.Id, k.Name)).ToListAsync(ct);
    }

    /// <summary>Warteschlange: offene (oder bereits korrigierte) Antworten, älteste Abgabe zuerst.</summary>
    public async Task<(IReadOnlyList<KorrekturZeile> Eintraege, bool Mehr)> SucheAsync(Guid akteurId, KorrekturFilter filter,
        int ueberspringen = 0, int anzahl = 50, CancellationToken ct = default)
    {
        var akteur = await KorrektorAsync(akteurId, ct);
        anzahl = Math.Clamp(anzahl, 1, 200);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var zuweisungen = Erlaubte(db, akteur);
        if (filter.KlasseId is { } klasse) zuweisungen = zuweisungen.Where(z => z.KlasseId == klasse);

        var q = from a in db.Antworten
                join v in db.Versuche on a.VersuchId equals v.Id
                join z in zuweisungen on v.ZuweisungId equals z.Id
                join f in db.Fragen on a.FrageId equals f.Id
                join s in db.Users on v.SchuelerId equals s.Id
                select new { a, v, z, f, s };
        q = filter.NurOffene
            ? q.Where(x => x.a.Punkte == null && x.a.Korrektur == KorrekturArt.Manuelle)
            : q.Where(x => x.a.KorrigiertVonId != null);
        if (!string.IsNullOrWhiteSpace(filter.Suche))
        {
            var suche = filter.Suche.Trim();
            q = q.Where(x => (x.s.Vorname + " " + x.s.Nachname).Contains(suche)
                             || db.Lecons.Any(l => l.Id == x.z.LeconId && l.Titel.Contains(suche))
                             || db.Pruefungen.Any(p => p.Id == x.z.PruefungId && p.Titel.Contains(suche)));
        }
        q = filter.NurOffene
            ? q.OrderBy(x => x.v.AbgabeUtc ?? x.a.ErstelltAmUtc).ThenBy(x => x.f.Nr)
            : q.OrderByDescending(x => x.a.KorrigiertAmUtc);

        var roh = await q.Skip(Math.Max(0, ueberspringen)).Take(anzahl + 1)
            .Select(x => new
            {
                x.a.Id, VersuchId = x.v.Id, ZuweisungId = x.z.Id, Schueler = x.s.Vorname + " " + x.s.Nachname,
                Klasse = db.Klassen.Where(k => k.Id == x.z.KlasseId).Select(k => k.Name).FirstOrDefault() ?? "",
                Inhalt = x.z.LeconId != null
                    ? db.Lecons.Where(l => l.Id == x.z.LeconId).Select(l => l.Titel).FirstOrDefault()
                    : db.Pruefungen.Where(p => p.Id == x.z.PruefungId).Select(p => p.Titel).FirstOrDefault(),
                IstLecon = x.z.LeconId != null, x.f.Nr, x.f.TextHtml, Abgabe = x.v.AbgabeUtc ?? x.a.ErstelltAmUtc,
                x.a.Punkte, Max = x.f.Punkte, x.a.KorrigiertAmUtc
            }).ToListAsync(ct);
        var zeilen = roh.Take(anzahl).Select(x => new KorrekturZeile(x.Id, x.VersuchId, x.ZuweisungId, x.Schueler, x.Klasse, x.Inhalt ?? "",
            x.IstLecon ? InhaltsArt.Lecon : InhaltsArt.Pruefung, x.Nr, InhaltBereiniger.Kurztext(x.TextHtml), x.Abgabe, x.Punkte, x.Max,
            x.KorrigiertAmUtc)).ToList();
        return (zeilen, roh.Count > anzahl);
    }

    /// <summary>Eine Antwort zur Korrektur: Frage mit Lösung, Antwort des Schülers, Verlauf.</summary>
    public async Task<KorrekturDetail> DetailAsync(Guid akteurId, Guid antwortId, CancellationToken ct = default)
    {
        var akteur = await KorrektorAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var a = await db.Antworten.AsNoTracking().FirstOrDefaultAsync(x => x.Id == antwortId, ct) ?? throw new FachFehler("Réponse introuvable.");
        var v = await db.Versuche.AsNoTracking().FirstAsync(x => x.Id == a.VersuchId, ct);
        var z = await db.Zuweisungen.AsNoTracking().FirstAsync(x => x.Id == v.ZuweisungId, ct);
        PruefeZuweisung(akteur, z);
        var f = await db.Fragen.AsNoTracking().Include(x => x.Optionen).FirstAsync(x => x.Id == a.FrageId, ct);
        var nr = Math.Max(1, v.FragenIds.IndexOf(f.Id) + 1);
        var schueler = await db.Users.Where(k => k.Id == v.SchuelerId).Select(k => k.Vorname + " " + k.Nachname).FirstAsync(ct);
        var klasse = await db.Klassen.Where(k => k.Id == z.KlasseId).Select(k => k.Name).FirstOrDefaultAsync(ct) ?? "";
        var titel = z.LeconId != null
            ? await db.Lecons.Where(l => l.Id == z.LeconId).Select(l => l.Titel).FirstAsync(ct)
            : await db.Pruefungen.Where(p => p.Id == z.PruefungId).Select(p => p.Titel).FirstAsync(ct);
        var verlauf = await (from e in db.Set<KorrekturEintrag>().AsNoTracking()
                             where e.AntwortId == a.Id
                             orderby e.ZeitUtc descending
                             select new KorrekturVerlauf(e.ZeitUtc,
                                 db.Users.Where(k => k.Id == e.KorrigiertVonId).Select(k => k.Vorname + " " + k.Nachname).FirstOrDefault() ?? "",
                                 e.AltePunkte, e.NeuePunkte, e.Feedback)).ToListAsync(ct);
        return new KorrekturDetail(a.Id, schueler, klasse, titel, FragenAbbildung.Ansicht(f, nr), AntwortEingabe.AusJson(a.AntwortJson),
            a.Punkte, a.Feedback, verlauf);
    }

    /// <summary>Punkte und Rückmeldung setzen (auch Nachkorrektur); jede Änderung wird protokolliert.</summary>
    public async Task KorrigierenAsync(Guid akteurId, Guid antwortId, decimal punkte, string? feedback, CancellationToken ct = default)
    {
        var akteur = await KorrektorAsync(akteurId, ct);
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        feedback = string.IsNullOrWhiteSpace(feedback) ? null : feedback.Trim();
        if (feedback?.Length > 2000) throw new FachFehler("Le commentaire est trop long (2000 caractères au plus).", "Feedback");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        Zuweisung? zuweisung = null;
        Guid schuelerId = default;
        await DbTransaktion.AusfuehrenAsync(db, async () =>
        {
            var a = await db.Antworten.FirstOrDefaultAsync(x => x.Id == antwortId, ct) ?? throw new FachFehler("Réponse introuvable.");
            var v = await db.Versuche.FirstAsync(x => x.Id == a.VersuchId, ct);
            var z = await db.Zuweisungen.AsNoTracking().FirstAsync(x => x.Id == v.ZuweisungId, ct);
            PruefeZuweisung(akteur, z);
            if (z.PruefungId is not null && v.Status == VersuchsStatus.EnCours)
                throw new FachFehler("L'élève compose encore : corrigez après l'envoi de sa copie.");
            var max = await db.Fragen.Where(f => f.Id == a.FrageId).Select(f => f.Punkte).FirstAsync(ct);
            if (punkte < 0 || punkte > max)
                throw new FachFehler($"Les points doivent être compris entre 0 et {ScoreRechner.Punkte(max)}.", "Punkte");
            punkte = Math.Round(punkte, 2, MidpointRounding.AwayFromZero);

            db.Set<KorrekturEintrag>().Add(new KorrekturEintrag
            {
                SchuleId = z.SchuleId, AntwortId = a.Id, VersuchId = v.Id, AltePunkte = a.Punkte, NeuePunkte = punkte,
                Feedback = feedback, KorrigiertVonId = akteur.Id, ZeitUtc = jetzt
            });
            a.Punkte = punkte;
            a.Feedback = feedback;
            a.Korrektur = KorrekturArt.Manuelle;
            a.KorrigiertVonId = akteur.Id;
            a.KorrigiertAmUtc = jetzt;
            var alle = await db.Antworten.Where(x => x.VersuchId == v.Id).ToListAsync(ct);
            VersuchsLogik.Aufsummieren(v, alle, abgegeben: v.Status != VersuchsStatus.EnCours);
            await db.SaveChangesAsync(ct);
            zuweisung = z;
            schuelerId = v.SchuelerId;
        }, ct);
        await VersuchsLogik.LernStandAsync(db, zuweisung!, schuelerId, jetzt, ct, aktivitaet: false);
    }

    /// <summary>Kopf einer Zuweisung für die Ergebnisseite.</summary>
    private static async Task<ZuweisungKopf> KopfAsync(SkoulouDbContext db, Zuweisung z, CancellationToken ct)
    {
        string titel, fach;
        decimal grenze = 0;
        var dauer = 0;
        if (z.LeconId is { } lid)
        {
            var l = await db.Lecons.AsNoTracking().Where(x => x.Id == lid).Select(x => new { x.Titel, x.FachId }).FirstAsync(ct);
            titel = l.Titel;
            fach = await db.Faecher.Where(f => f.Id == l.FachId).Select(f => f.Name).FirstOrDefaultAsync(ct) ?? "";
        }
        else
        {
            var p = await db.Pruefungen.AsNoTracking().FirstAsync(x => x.Id == z.PruefungId, ct);
            (titel, grenze, dauer) = (p.Titel, p.Bestehensgrenze, p.DauerMinuten);
            fach = await db.Faecher.Where(f => f.Id == p.FachId).Select(f => f.Name).FirstOrDefaultAsync(ct) ?? "";
        }
        var max = (await FragenAbbildung.AktuelleAsync(db, z.LeconId, z.PruefungId, ct)).SelectMany(b => b.Fragen).Sum(f => f.Punkte);
        var klasse = await db.Klassen.Where(k => k.Id == z.KlasseId).Select(k => k.Name).FirstOrDefaultAsync(ct) ?? "";
        return new ZuweisungKopf(z.Id, z.LeconId is not null ? InhaltsArt.Lecon : InhaltsArt.Pruefung, z.LeconId ?? z.PruefungId!.Value,
            titel, klasse, fach, z.AbUtc, z.FristUtc, z.MaxVersuche, z.Wertung, z.LoesungenSichtbar, z.Status, max, grenze, dauer);
    }

    /// <summary>Wer hat was gemacht: Schüler der Klasse (und frühere mit Versuchen) mit gewertetem Ergebnis.</summary>
    public async Task<ZuweisungErgebnisse> ErgebnisseAsync(Guid akteurId, Guid zuweisungId, CancellationToken ct = default)
    {
        var akteur = await KorrektorAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var z = await db.Zuweisungen.AsNoTracking().FirstOrDefaultAsync(x => x.Id == zuweisungId, ct) ?? throw new FachFehler("Cette attribution est introuvable.");
        PruefeZuweisung(akteur, z);
        var kopf = await KopfAsync(db, z, ct);

        var inKlasse = await SchulAbfragen.AktiveEinschreibungen(db).Where(e => e.KlasseId == z.KlasseId).Select(e => e.SchuelerId).ToListAsync(ct);
        var mitVersuch = await db.Versuche.Where(v => v.ZuweisungId == z.Id).Select(v => v.SchuelerId).Distinct().ToListAsync(ct);
        var alle = inKlasse.Union(mitVersuch).ToList();
        var namen = await db.Users.AsNoTracking().Where(k => alle.Contains(k.Id))
            .Select(k => new { k.Id, k.Vorname, k.Nachname }).ToListAsync(ct);
        var staende = await db.LernStaende.AsNoTracking().Where(s => s.ZuweisungId == z.Id).ToDictionaryAsync(s => s.SchuelerId, ct);
        var versuche = await db.Versuche.AsNoTracking().Where(v => v.ZuweisungId == z.Id)
            .Select(v => new VersuchsWertMitId(v.Id, v.SchuelerId, v.Nr, v.Punkte, v.MaxPunkte, v.Status)).ToListAsync(ct);

        var zeilen = namen.OrderBy(n => n.Nachname).ThenBy(n => n.Vorname).Select(n =>
        {
            var eigene = versuche.Where(v => v.SchuelerId == n.Id).ToList();
            var gewertet = VersuchsRegel.Gewertet(eigene.Where(v => v.Status != VersuchsStatus.EnCours)
                .Select(v => new VersuchsWert(v.Nr, v.Punkte, v.MaxPunkte, v.Status == VersuchsStatus.ACorriger)), z.Wertung);
            var gewerteterId = gewertet is null ? (Guid?)null : eigene.First(v => v.Nr == gewertet.Nr).Id;
            staende.TryGetValue(n.Id, out var stand);
            var fortschritt = eigene.Any(v => v.Status == VersuchsStatus.ACorriger) ? SchuelerFortschritt.ACorriger
                : stand?.Abgeschlossen == true ? SchuelerFortschritt.Termine
                : eigene.Count > 0 ? SchuelerFortschritt.EnCours
                : SchuelerFortschritt.PasCommence;
            return new SchuelerErgebnisZeile(n.Id, $"{n.Nachname} {n.Vorname}", fortschritt, eigene.Count, gewertet?.Punkte ?? 0,
                gewertet?.MaxPunkte ?? kopf.MaxPunkte, gewertet is null ? null : ScoreRechner.Prozent(gewertet.Punkte, gewertet.MaxPunkte),
                stand?.LetzteAktivitaetUtc, gewerteterId, inKlasse.Contains(n.Id));
        }).ToList();
        return new ZuweisungErgebnisse(kopf, zeilen);
    }

    private sealed record VersuchsWertMitId(Guid Id, Guid SchuelerId, int Nr, decimal Punkte, decimal MaxPunkte, VersuchsStatus Status);

    /// <summary>Versuche eines Schülers zu einer Zuweisung (Nr, Status, Punkte) für die Auswahl in der Ansicht.</summary>
    public async Task<IReadOnlyList<VersuchInfo>> VersucheAsync(Guid akteurId, Guid zuweisungId, Guid schuelerId, CancellationToken ct = default)
    {
        var akteur = await KorrektorAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var z = await db.Zuweisungen.AsNoTracking().FirstOrDefaultAsync(x => x.Id == zuweisungId, ct) ?? throw new FachFehler("Cette attribution est introuvable.");
        PruefeZuweisung(akteur, z);
        return (await db.Versuche.AsNoTracking().Where(v => v.ZuweisungId == z.Id && v.SchuelerId == schuelerId).OrderBy(v => v.Nr).ToListAsync(ct))
            .Select(VersuchsLogik.Info).ToList();
    }

    /// <summary>Ein Versuch mit allen Antworten, Lösungen und Korrekturen (Lehrkraft/Direktion).</summary>
    public async Task<VersuchsDetail> VersuchAsync(Guid akteurId, Guid versuchId, CancellationToken ct = default)
    {
        var akteur = await KorrektorAsync(akteurId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var v = await db.Versuche.AsNoTracking().FirstOrDefaultAsync(x => x.Id == versuchId, ct) ?? throw new FachFehler("Cet essai est introuvable.");
        var z = await db.Zuweisungen.AsNoTracking().FirstAsync(x => x.Id == v.ZuweisungId, ct);
        PruefeZuweisung(akteur, z);
        var kopf = await KopfAsync(db, z, ct);
        var fragen = await FragenAbbildung.LadeAsync(db, v.FragenIds, ct);
        var antworten = await db.Antworten.AsNoTracking().Where(a => a.VersuchId == v.Id).ToDictionaryAsync(a => a.FrageId, ct);
        var korrektoren = antworten.Values.Where(a => a.KorrigiertVonId != null).Select(a => a.KorrigiertVonId!.Value).Distinct().ToList();
        var namen = await db.Users.Where(k => korrektoren.Contains(k.Id)).ToDictionaryAsync(k => k.Id, k => k.Vorname + " " + k.Nachname, ct);
        var nr = 0;
        var zeilen = v.FragenIds.Where(fragen.ContainsKey).Select(id =>
        {
            var f = fragen[id];
            antworten.TryGetValue(id, out var a);
            var eingabe = AntwortEingabe.AusJson(a?.AntwortJson);
            var status = a is null ? BewertungsStatus.Leer
                : VersuchsLogik.IstOffeneKorrektur(a) ? BewertungsStatus.Manuell
                : a.KorrigiertVonId is not null ? VersuchsLogik.StatusAusPunkten(a.Punkte ?? 0, f.Punkte)
                : v.Status == VersuchsStatus.EnCours && z.PruefungId is not null ? BewertungsStatus.Leer
                : BewertungsEngine.Bewerte(FragenAbbildung.Definition(f), eingabe).Status;
            return new VersuchsAntwortZeile(FragenAbbildung.Ansicht(f, ++nr), a?.Id, eingabe, a?.Punkte, status, a?.Feedback,
                a?.KorrigiertVonId is not null, a?.KorrigiertVonId is { } k ? namen.GetValueOrDefault(k) : null);
        }).ToList();
        var schueler = await db.Users.Where(k => k.Id == v.SchuelerId).Select(k => k.Vorname + " " + k.Nachname).FirstAsync(ct);
        return new VersuchsDetail(v.Id, schueler, kopf.Titel, v.Nr, v.Status, v.Punkte, v.MaxPunkte, v.StartUtc, v.AbgabeUtc, zeilen);
    }
}
