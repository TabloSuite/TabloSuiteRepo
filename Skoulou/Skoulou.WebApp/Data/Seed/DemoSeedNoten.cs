using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Noten;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Data.Seed;

/// <summary>
/// Demo-Daten des Bereichs E5 Noten & Bulletins (nach der Grund-Demo-Schule; nur erfundene Daten): Komponenten für alle
/// Perioden, im 1er trimestre Évaluations + Noten (6e A vollständig, 6e B mit fehlenden Noten und einer entschuldigten
/// Abwesenheit, 5e A begonnen), Conseil der 6e A, veröffentlichte Bulletins der 6e A und ein berichtigtes Bulletin.
/// </summary>
public static class DemoSeedNoten
{
    private static readonly (string Titel, BewertungsArt Art, decimal Bareme, decimal Gewicht, DateOnly Datum)[] Bewertungen =
    [
        ("Interrogation 1", BewertungsArt.Interrogation, 10, 1, new(2026, 10, 2)),
        ("Devoir 1", BewertungsArt.Devoir, 20, 1, new(2026, 10, 23)),
        ("Composition", BewertungsArt.Composition, 20, 2, new(2026, 11, 10)),
    ];

    private static readonly string[] Bemerkungen =
    [
        "Excellent trimestre, élève sérieux et curieux.", "Bon trimestre, continuez ainsi.", "Des efforts à poursuivre en mathématiques.",
        "Travail régulier, participation à encourager.", "Trimestre juste : il faut travailler davantage.",
    ];

    /// <param name="dienste">Dienste der App (DbContextFactory, UserManager, Bereichsdienste …).</param>
    /// <param name="schuleId">Demo-Schule « Collège Démo Les Palmiers ».</param>
    /// <param name="neueKonten">Neu angelegte Demo-Konten (Passwörter landen nur in demo-konten.json).</param>
    public static async Task ErgaenzenAsync(IServiceProvider dienste, Guid schuleId, List<DemoKonto> neueKonten, CancellationToken ct)
    {
        var dbFactory = dienste.GetRequiredService<IDbContextFactory<SkoulouDbContext>>();
        Guid direktor, p1;
        Dictionary<string, Klasse> klassen;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var jahr = await db.Schuljahre.FirstOrDefaultAsync(j => j.SchuleId == schuleId && j.Status == SchuljahrStatus.Laufend, ct);
            if (jahr is null || await db.Bewertungen.AnyAsync(b => b.SchuleId == schuleId, ct)) return;

            direktor = await db.Users.Where(u => u.SchuleId == schuleId && u.Rolle == Rollen.Direktor).Select(u => u.Id).FirstAsync(ct);
            var perioden = await db.Perioden.Where(p => p.SchuljahrId == jahr.Id).OrderBy(p => p.Nr).ToListAsync(ct);
            p1 = perioden[0].Id;
            foreach (var p in perioden)
            {
                var nr = 0;
                foreach (var (titel, art, gewicht) in new[] { ("Interrogation", BewertungsArt.Interrogation, 1m), ("Devoir", BewertungsArt.Devoir, 1m), ("Composition", BewertungsArt.Composition, 2m) })
                    db.PeriodenKomponenten.Add(new PeriodenKomponente { SchuleId = schuleId, PeriodeId = p.Id, Titel = titel, Art = art, Gewicht = gewicht, Bareme = 20, Reihenfolge = ++nr });
            }
            await db.SaveChangesAsync(ct);

            klassen = await db.Klassen.Where(k => k.SchuljahrId == jahr.Id).ToDictionaryAsync(k => k.Name, ct);
            var komponenten = await db.PeriodenKomponenten.Where(k => k.PeriodeId == p1).ToDictionaryAsync(k => k.Art, ct);
            foreach (var (name, anzahl, luecken) in new[] { ("6e A", 3, false), ("6e B", 2, true), ("5e A", 1, false) })
            {
                if (!klassen.TryGetValue(name, out var klasse)) continue;
                var faecher = await (from kf in db.Klassenfaecher
                                     join fa in db.Faecher on kf.FachId equals fa.Id
                                     where kf.KlasseId == klasse.Id
                                     select new { kf.Id, fa.Name, Lehrer = db.Lehrauftraege.Where(l => l.KlassenfachId == kf.Id).Select(l => l.LehrerId).FirstOrDefault() }).ToListAsync(ct);
                var schueler = await db.Einschreibungen.Where(e => e.KlasseId == klasse.Id && e.Status == EinschreibungsStatus.Active && e.StorniertAmUtc == null)
                    .OrderBy(e => e.Id).Select(e => e.Id).ToListAsync(ct);
                var f = 0;
                foreach (var fach in faecher)
                {
                    f++;
                    var lehrer = fach.Lehrer == Guid.Empty ? direktor : fach.Lehrer;
                    foreach (var (titel, art, bareme, gewicht, datum) in Bewertungen.Take(anzahl))
                    {
                        var b = new Bewertung
                        {
                            SchuleId = schuleId, KlassenfachId = fach.Id, PeriodeId = p1, KomponenteId = komponenten.GetValueOrDefault(art)?.Id, Titel = titel, Art = art,
                            Datum = datum, Bareme = bareme, Gewicht = gewicht, ErstelltVonId = lehrer
                        };
                        db.Bewertungen.Add(b);
                        var s = 0;
                        foreach (var e in schueler)
                        {
                            s++;
                            if (luecken && f == 2 && art == BewertungsArt.Devoir && s <= 2) continue; // zwei fehlende Noten in 6e B
                            var (status, punkte) = luecken && f == 1 && art == BewertungsArt.Interrogation && s == 3
                                ? (NotenStatus.AbsentJustifie, (decimal?)null)
                                : (NotenStatus.Notee, Punkte(s, f, art, bareme));
                            var note = new Note { SchuleId = schuleId, BewertungId = b.Id, EinschreibungId = e, Status = status, Punkte = punkte, ErfasstVonId = lehrer };
                            db.Noten.Add(note);
                            db.NotenAenderungen.Add(new NotenAenderung
                            {
                                SchuleId = schuleId, NoteId = note.Id, NeuerStatus = status, NeuePunkte = punkte, GeaendertVonId = lehrer, ZeitUtc = DateTime.UtcNow
                            });
                        }
                    }
                }
                if (name == "6e A")
                {
                    var i = 0;
                    foreach (var e in schueler)
                    {
                        db.KlassenratEintraege.Add(new KlassenratEintrag
                        {
                            SchuleId = schuleId, PeriodeId = p1, EinschreibungId = e, Bemerkung = Bemerkungen[i % Bemerkungen.Length], ErfasstVonId = direktor,
                            Sanktionen = i == 3 ? Sanktionen.AvertissementConduite : Sanktionen.Keine
                        });
                        i++;
                    }
                }
            }
            await db.SaveChangesAsync(ct);
        }

        // Veröffentlichung der 6e A und ein berichtigtes Bulletin – über den Kern des Dienstes (dieselben Regeln, ohne Mails).
        if (!klassen.TryGetValue("6e A", out var sechsA)) return;
        var akteur = new SchulAkteur(new Akteur(direktor, Rollen.Direktor, schuleId, Core.Rechte.KoordinatorRechte.Keine, KontoStatus.Actif, SchulStatus.Active, true, "Démo"),
            Core.Rechte.Zugriffsart.Ja, schuleId);
        var bulletins = dienste.GetRequiredService<BulletinService>();
        await bulletins.VeroeffentlichenKernAsync(akteur, sechsA.Id, p1, null, trotzNichtKlassierter: true, mitMails: false, ct);
        await bulletins.RectificationOeffnenKernAsync(akteur, sechsA.Id, p1, "Appréciation du conseil corrigée à la demande du professeur principal.", ct);
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var eintrag = await db.KlassenratEintraege.Where(k => k.PeriodeId == p1 && db.Einschreibungen.Any(e => e.Id == k.EinschreibungId && e.KlasseId == sechsA.Id))
                .OrderBy(k => k.EinschreibungId).Skip(1).FirstAsync(ct);
            eintrag.Bemerkung = "Très bon trimestre, félicitations pour les progrès en anglais.";
            await db.SaveChangesAsync(ct);
        }
        await bulletins.RectificationVeroeffentlichenKernAsync(akteur, sechsA.Id, p1, null, mitMails: false, ct);

    }

    /// <summary>Gut verteilte, wiederholbare Noten (je Schüler ein Niveau, je Fach eine kleine Abweichung).</summary>
    private static decimal Punkte(int schueler, int fach, BewertungsArt art, decimal bareme)
    {
        var niveau = 7.5m + (schueler * 37 % 11);                 // 7,5 … 17,5
        var abweichung = ((schueler * 3 + fach * 5 + (int)art * 7) % 9 - 4) * 0.5m; // −2 … +2
        var aufZwanzig = Math.Clamp(niveau + abweichung, 3, 19.5m);
        return Math.Round(aufZwanzig * bareme / 20 * 2, MidpointRounding.AwayFromZero) / 2;
    }
}
