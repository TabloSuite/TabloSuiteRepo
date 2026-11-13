using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Paedagogik;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Paedagogik;

/// <summary>
/// Examen aus Schülersicht: genau ein Versuch im Fenster (Ab…Frist), Zeit läuft serverseitig
/// (Deadline = min(Start + Dauer, Frist)), Antworten werden laufend gespeichert und bis Deadline + 30 s
/// angenommen; abgelaufene Versuche gibt der <see cref="PruefungsAbgabeDienst"/> ab. Bewertung bei der
/// Abgabe, Note sofort (ohne Einzelheiten), Korrektur nach der Frist.
/// </summary>
public sealed class PruefungsService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, TimeProvider zeit,
    ILogger<PruefungsService> logger)
{
    private DateTime Jetzt => zeit.GetUtcNow().UtcDateTime;

    /// <summary>Examen der aktuellen Klasse und eigene frühere Examen.</summary>
    public async Task<IReadOnlyList<PruefungsKarte>> ListeAsync(Guid schuelerId, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchuelerAsync(zugriff, schuelerId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var klasse = (await SchulAbfragen.AktuelleEinschreibungAsync(db, akteur.Id, ct))?.KlasseId;
        var jetzt = Jetzt;
        var roh = await (from z in db.Zuweisungen
                         where z.PruefungId != null && z.SchuleId == akteur.SchuleId
                               && ((z.KlasseId == klasse && z.Status == ZuweisungsStatus.Active)
                                   || db.Versuche.Any(v => v.ZuweisungId == z.Id && v.SchuelerId == akteur.Id))
                         join p in db.Pruefungen on z.PruefungId equals p.Id
                         select new
                         {
                             z, p.Titel, p.DauerMinuten, p.Bestehensgrenze,
                             Fach = db.Faecher.Where(f => f.Id == p.FachId).Select(f => f.Name).FirstOrDefault() ?? "",
                             Lehrer = db.Users.Where(k => k.Id == z.LehrerId).Select(k => k.Vorname + " " + k.Nachname).FirstOrDefault() ?? "",
                             Versuch = db.Versuche.Where(v => v.ZuweisungId == z.Id && v.SchuelerId == akteur.Id).OrderBy(v => v.Nr).FirstOrDefault(),
                             Max = db.Fragen.Where(f => !f.Archiviert && db.AufgabenBloecke.Any(b => b.Id == f.BlockId && b.PruefungId == p.Id && !b.Archiviert))
                                 .Sum(f => (decimal?)f.Punkte) ?? 0
                         }).ToListAsync(ct);
        return roh.Select(x => Karte(x.z, x.Titel, x.Fach, x.Lehrer, x.DauerMinuten, x.Bestehensgrenze, x.Versuch, x.Max, jetzt))
            .OrderBy(k => k.Zustand switch { PruefungsZustand.EnCours => 0, PruefungsZustand.Ouvert => 1, PruefungsZustand.AVenir => 2, _ => 3 })
            .ThenBy(k => k.Zustand is PruefungsZustand.Ouvert or PruefungsZustand.AVenir ? k.FristUtc : null)
            .ThenByDescending(k => k.FristUtc).ToList();
    }

    private static PruefungsKarte Karte(Zuweisung z, string titel, string fach, string lehrer, int dauer, decimal grenze, Versuch? v,
        decimal max, DateTime jetzt)
    {
        var zustand = v switch
        {
            { Status: VersuchsStatus.EnCours } when !PruefungsZeit.AutoAbgabeFaellig(jetzt, v.DeadlineUtc) => PruefungsZustand.EnCours,
            { Status: VersuchsStatus.EnCours } => PruefungsZustand.Envoye,
            { Status: VersuchsStatus.ACorriger or VersuchsStatus.Soumis } => z.FristUtc is { } f && jetzt >= f && v.Status == VersuchsStatus.Soumis
                ? PruefungsZustand.Corrige : PruefungsZustand.Envoye,
            { Status: VersuchsStatus.Corrige } => PruefungsZustand.Corrige,
            _ when jetzt < z.AbUtc => PruefungsZustand.AVenir,
            _ when PruefungsZeit.Startbar(jetzt, z.AbUtc, z.FristUtc) => PruefungsZustand.Ouvert,
            _ => PruefungsZustand.Manque
        };
        var abgegeben = v is not null && zustand is PruefungsZustand.Envoye or PruefungsZustand.Corrige && v.Status != VersuchsStatus.EnCours;
        var prozent = abgegeben ? ScoreRechner.Prozent(v!.Punkte, v.MaxPunkte) : null;
        var offen = v?.Status == VersuchsStatus.ACorriger;
        return new PruefungsKarte(z.Id, titel, fach, lehrer, dauer, z.AbUtc, z.FristUtc, zustand, v?.DeadlineUtc,
            abgegeben ? v!.Punkte : null, v?.MaxPunkte ?? max, prozent, abgegeben && !offen ? prozent >= grenze : null, offen);
    }

    /// <summary>Sitzung: vor dem Start nur die Karte, während des Versuchs die Fragen, danach die Note (Korrektur nach Frist).</summary>
    public async Task<PruefungsSitzung> OeffnenAsync(Guid schuelerId, Guid zuweisungId, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchuelerAsync(zugriff, schuelerId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var jetzt = Jetzt;
        var (z, _) = await VersuchsLogik.ZuweisungAsync(db, akteur, zuweisungId, jetzt, auchFruehere: true, ct);
        if (z.PruefungId is not { } pid) throw new FachFehler("Ce contenu n'est pas un examen.");

        var v = await db.Versuche.FirstOrDefaultAsync(x => x.ZuweisungId == z.Id && x.SchuelerId == akteur.Id, ct);
        if (v is { Status: VersuchsStatus.EnCours } && PruefungsZeit.AutoAbgabeFaellig(jetzt, v.DeadlineUtc))
        {
            await AbgebenInternAsync(db, z, v, jetzt, ct);
            v = await db.Versuche.AsNoTracking().FirstAsync(x => x.Id == v.Id, ct);
        }
        var p = await db.Pruefungen.AsNoTracking().FirstAsync(x => x.Id == pid, ct);
        var fach = await db.Faecher.Where(f => f.Id == p.FachId).Select(f => f.Name).FirstOrDefaultAsync(ct) ?? "";
        var lehrer = await db.Users.Where(k => k.Id == z.LehrerId).Select(k => k.Vorname + " " + k.Nachname).FirstOrDefaultAsync(ct) ?? "";
        var max = v?.MaxPunkte ?? (await FragenAbbildung.AktuelleAsync(db, null, pid, ct)).SelectMany(b => b.Fragen).Sum(f => f.Punkte);
        var karte = Karte(z, p.Titel, fach, lehrer, p.DauerMinuten, p.Bestehensgrenze, v, max, jetzt);

        var loesungen = v is not null && v.Status != VersuchsStatus.EnCours && z.FristUtc is { } frist && jetzt >= frist;
        IReadOnlyList<SchuelerBlock> bloecke = [];
        IReadOnlyList<AntwortStand> antworten = [];
        if (v is not null && (v.Status == VersuchsStatus.EnCours || loesungen))
        {
            var (b, fragen) = await LernService.FragenFuerAsync(db, z, v, ct);
            bloecke = b;
            if (loesungen)
            {
                // Durchsicht: auch unbeantwortete Fragen mit Lösung zeigen.
                var staende = (await LernService.AntwortStaendeAsync(db, z, v, fragen, jetzt, ct)).ToList();
                staende.AddRange(v.FragenIds.Where(id => fragen.ContainsKey(id) && staende.All(s => s.FrageId != id))
                    .Select(id => new AntwortStand(id, AntwortEingabe.Leer, BewertungsStatus.Leer, 0, fragen[id].Punkte, null, null, false,
                        FragenAbbildung.Loesung(fragen[id]))));
                antworten = staende;
            }
            else
            {
                antworten = await EntwuerfeAsync(db, v, fragen, ct);
            }
        }
        var rest = v is { Status: VersuchsStatus.EnCours, DeadlineUtc: { } d } ? (int)Math.Ceiling(PruefungsZeit.Restzeit(jetzt, d).TotalSeconds) : 0;
        return new PruefungsSitzung(karte, p.Beschreibung, p.Bestehensgrenze, v is null ? null : VersuchsLogik.Info(v), bloecke, antworten, rest, loesungen);
    }

    /// <summary>Gespeicherte Antworten während des Examens – ohne jede Bewertung.</summary>
    private static async Task<IReadOnlyList<AntwortStand>> EntwuerfeAsync(SkoulouDbContext db, Versuch v, Dictionary<Guid, Frage> fragen, CancellationToken ct)
        => (await db.Antworten.AsNoTracking().Where(a => a.VersuchId == v.Id).ToListAsync(ct))
            .Where(a => fragen.ContainsKey(a.FrageId))
            .Select(a => new AntwortStand(a.FrageId, AntwortEingabe.AusJson(a.AntwortJson), BewertungsStatus.Leer, null, fragen[a.FrageId].Punkte,
                null, null, false, null)).ToList();

    /// <summary>Startet das Examen (genau ein Versuch; doppelter Start liefert den bestehenden).</summary>
    public async Task<VersuchInfo> StartenAsync(Guid schuelerId, Guid zuweisungId, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchuelerAsync(zugriff, schuelerId, ct);
        var jetzt = Jetzt;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (z, _) = await VersuchsLogik.ZuweisungAsync(db, akteur, zuweisungId, jetzt, auchFruehere: false, ct);
        if (z.PruefungId is null) throw new FachFehler("Ce contenu n'est pas un examen.");
        if (await db.Versuche.AsNoTracking().FirstOrDefaultAsync(v => v.ZuweisungId == z.Id && v.SchuelerId == akteur.Id, ct) is { } vorhanden)
        {
            if (vorhanden.Status == VersuchsStatus.EnCours) return VersuchsLogik.Info(vorhanden);
            throw new FachFehler("Vous avez déjà passé cet examen.");
        }
        if (jetzt < z.AbUtc) throw new FachFehler("Cet examen n'est pas encore ouvert.");
        if (!PruefungsZeit.Startbar(jetzt, z.AbUtc, z.FristUtc)) throw new FachFehler("La date limite de cet examen est dépassée.");
        try
        {
            var v = await VersuchsLogik.NeuerVersuchAsync(db, z, akteur.Id, jetzt, ct);
            if (v.FragenIds.Count == 0) throw new FachFehler("Cet examen ne contient pas encore de questions.");
            await db.SaveChangesAsync(ct);
            await VersuchsLogik.LernStandAsync(db, z, akteur.Id, jetzt, ct);
            return VersuchsLogik.Info(v);
        }
        catch (DbUpdateException ex) when (DbTransaktion.IstEindeutigkeitsFehler(ex))
        {
            // Doppelklick: der andere Start hat gewonnen.
            await using var neu = await dbFactory.CreateDbContextAsync(ct);
            return VersuchsLogik.Info(await neu.Versuche.AsNoTracking().FirstAsync(v => v.ZuweisungId == z.Id && v.SchuelerId == akteur.Id, ct));
        }
    }

    /// <summary>Speichert eine Antwort während des Examens (bis Deadline + Karenz).</summary>
    public async Task AntwortSpeichernAsync(Guid schuelerId, Guid versuchId, Guid frageId, AntwortEingabe eingabe, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchuelerAsync(zugriff, schuelerId, ct);
        var jetzt = Jetzt;
        if ((eingabe.Text?.Length ?? 0) > 4000 || eingabe.Luecken.Any(l => (l?.Length ?? 0) > 500)) throw new FachFehler("La réponse est trop longue.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var v = await LaufenderVersuchAsync(db, akteur, versuchId, jetzt, ct);
        if (!PruefungsZeit.AnnahmeOffen(jetzt, v.DeadlineUtc)) throw new FachFehler("Le temps est écoulé : vos réponses ont été envoyées.");
        if (!v.FragenIds.Contains(frageId)) throw new FachFehler("Cette question ne fait pas partie de votre examen.");

        for (var versuch = 0; ; versuch++)
        {
            var a = await db.Antworten.FirstOrDefaultAsync(x => x.VersuchId == v.Id && x.FrageId == frageId, ct);
            if (a is null) db.Antworten.Add(a = new Antwort { VersuchId = v.Id, FrageId = frageId, Korrektur = KorrekturArt.Auto });
            a.AntwortJson = eingabe.AlsJson();
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException ex) when (versuch == 0 && DbTransaktion.IstEindeutigkeitsFehler(ex))
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>Gibt ab (idempotent: eine zweite Abgabe liefert das bestehende Ergebnis).</summary>
    public async Task<VersuchInfo> AbgebenAsync(Guid schuelerId, Guid versuchId, CancellationToken ct = default)
    {
        var akteur = await InhaltsRechte.SchuelerAsync(zugriff, schuelerId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var v = await db.Versuche.FirstOrDefaultAsync(x => x.Id == versuchId && x.SchuelerId == akteur.Id, ct)
                ?? throw new FachFehler("Cet examen est introuvable.");
        var z = await db.Zuweisungen.AsNoTracking().FirstAsync(x => x.Id == v.ZuweisungId, ct);
        if (z.PruefungId is null) throw new FachFehler("Ce contenu n'est pas un examen.");
        if (v.Status == VersuchsStatus.EnCours) await AbgebenInternAsync(db, z, v, Jetzt, ct);
        await using var frisch = await dbFactory.CreateDbContextAsync(ct);
        return VersuchsLogik.Info(await frisch.Versuche.AsNoTracking().FirstAsync(x => x.Id == versuchId, ct));
    }

    /// <summary>Hintergrund: alle abgelaufenen Versuche (Examen und Leçons mit Frist) abgeben. Liefert die Anzahl.</summary>
    public async Task<int> AbgelaufeneAbgebenAsync(CancellationToken ct = default)
    {
        var grenze = Jetzt - PruefungsZeit.Karenz;
        List<Guid> faellig;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            faellig = await db.Versuche.AsNoTracking()
                .Where(v => v.Status == VersuchsStatus.EnCours && v.DeadlineUtc != null && v.DeadlineUtc < grenze)
                .OrderBy(v => v.DeadlineUtc).Select(v => v.Id).Take(200).ToListAsync(ct);

        var anzahl = 0;
        foreach (var id in faellig)
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var v = await db.Versuche.FirstOrDefaultAsync(x => x.Id == id && x.Status == VersuchsStatus.EnCours, ct);
                if (v is null) continue;
                var z = await db.Zuweisungen.AsNoTracking().FirstAsync(x => x.Id == v.ZuweisungId, ct);
                if (await AbgebenInternAsync(db, z, v, v.DeadlineUtc!.Value, ct)) anzahl++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Automatische Abgabe von Versuch {Versuch} fehlgeschlagen.", id);
            }
        }
        return anzahl;
    }

    /// <summary>Abgabe mit optimistischer Sperre auf den Status: nur einer (Schüler oder Dienst) gewinnt.</summary>
    private static async Task<bool> AbgebenInternAsync(SkoulouDbContext db, Zuweisung z, Versuch v, DateTime zeitpunkt, CancellationToken ct)
    {
        await VersuchsLogik.AbschliessenAsync(db, v, zeitpunkt, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
        await VersuchsLogik.LernStandAsync(db, z, v.SchuelerId, zeitpunkt, ct);
        return true;
    }

    /// <summary>Laufender eigener Versuch; Rechteverlust (Klassenwechsel, Archiv, gesperrte Schule) sperrt auch ihn.</summary>
    private static async Task<Versuch> LaufenderVersuchAsync(SkoulouDbContext db, Akteur akteur, Guid versuchId, DateTime jetzt, CancellationToken ct)
    {
        var v = await db.Versuche.AsNoTracking().FirstOrDefaultAsync(x => x.Id == versuchId && x.SchuelerId == akteur.Id, ct)
                ?? throw new FachFehler("Cet examen est introuvable.");
        if (v.Status != VersuchsStatus.EnCours) throw new FachFehler("Vos réponses ont déjà été envoyées.");
        var (z, _) = await VersuchsLogik.ZuweisungAsync(db, akteur, v.ZuweisungId, jetzt, auchFruehere: false, ct);
        if (z.PruefungId is null) throw new KeinZugriffFehler(VersuchsLogik.MeldungNichtZugewiesen);
        return v;
    }
}
