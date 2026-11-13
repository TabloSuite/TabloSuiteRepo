using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Paedagogik;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Paedagogik;

/// <summary>
/// Gemeinsame Regeln für Versuche (Übung und Examen): Zugang des Schülers, Abschluss, Punkte,
/// Lösungsanzeige und materialisierter <see cref="LernStand"/>. Eine offene manuelle Korrektur ist
/// eine Antwort mit <c>Punkte == null</c> und <c>Korrektur == Manuelle</c>.
/// </summary>
internal static class VersuchsLogik
{
    public const string MeldungNichtZugewiesen = "Ce contenu n'est pas attribué à votre classe.";

    /// <summary>Zuweisung, die der Schüler sehen darf: aktuelle Klasse, aktiv, begonnen – oder (lesend) eine, in der er schon Versuche hat.</summary>
    public static async Task<(Zuweisung Zuweisung, bool Offen)> ZuweisungAsync(SkoulouDbContext db, Akteur schueler, Guid zuweisungId,
        DateTime jetzt, bool auchFruehere, CancellationToken ct)
    {
        var z = await db.Zuweisungen.AsNoTracking().FirstOrDefaultAsync(x => x.Id == zuweisungId, ct)
                ?? throw new FachFehler("Ce contenu est introuvable.");
        if (z.SchuleId != schueler.SchuleId) throw new KeinZugriffFehler(Zugriff.MeldungAndereSchule);
        var klasse = (await SchulAbfragen.AktuelleEinschreibungAsync(db, schueler.Id, ct))?.KlasseId;
        if (z.KlasseId == klasse && z.Status == ZuweisungsStatus.Active && z.AbUtc <= jetzt) return (z, true);
        if (auchFruehere && await db.Versuche.AnyAsync(v => v.ZuweisungId == z.Id && v.SchuelerId == schueler.Id, ct)) return (z, false);
        throw new KeinZugriffFehler(MeldungNichtZugewiesen);
    }

    public static bool IstOffeneKorrektur(Antwort a) => a.Punkte is null && a.Korrektur == KorrekturArt.Manuelle;

    /// <summary>Gibt einen Versuch ab: Examen-Entwürfe werden jetzt bewertet; Status nach offenen Korrekturen.</summary>
    public static async Task AbschliessenAsync(SkoulouDbContext db, Versuch v, DateTime jetzt, CancellationToken ct)
    {
        var antworten = await db.Antworten.Where(a => a.VersuchId == v.Id).ToListAsync(ct);
        var unbewertet = antworten.Where(a => a.Punkte is null && a.Korrektur == KorrekturArt.Auto).ToList();
        if (unbewertet.Count > 0)
        {
            var fragen = await FragenAbbildung.LadeAsync(db, unbewertet.Select(a => a.FrageId), ct);
            foreach (var a in unbewertet)
            {
                if (!fragen.TryGetValue(a.FrageId, out var f)) continue;
                var e = BewertungsEngine.Bewerte(FragenAbbildung.Definition(f), AntwortEingabe.AusJson(a.AntwortJson));
                a.Punkte = e.Punkte;
                a.Korrektur = e.BrauchtKorrektur ? KorrekturArt.Manuelle : KorrekturArt.Auto;
            }
        }
        v.AbgabeUtc ??= jetzt;
        Aufsummieren(v, antworten);
    }

    /// <summary>Punkte und (bei abgegebenen Versuchen) Status aus den Antworten.</summary>
    public static void Aufsummieren(Versuch v, IReadOnlyCollection<Antwort> antworten, bool abgegeben = true)
    {
        v.Punkte = antworten.Sum(a => a.Punkte ?? 0);
        if (!abgegeben) return;
        if (antworten.Any(IstOffeneKorrektur)) v.Status = VersuchsStatus.ACorriger;
        else if (antworten.Any(a => a.KorrigiertVonId is not null)) v.Status = VersuchsStatus.Corrige;
        else v.Status = VersuchsStatus.Soumis;
    }

    /// <summary>Stand einer Antwort für die Anzeige (Lösung nur, wenn erlaubt).</summary>
    public static AntwortStand Stand(Frage f, Antwort a, bool loesungSichtbar)
    {
        var eingabe = AntwortEingabe.AusJson(a.AntwortJson);
        var bewertung = BewertungsEngine.Bewerte(FragenAbbildung.Definition(f), eingabe);
        var status = IstOffeneKorrektur(a) ? BewertungsStatus.Manuell
            : a.KorrigiertVonId is not null ? StatusAusPunkten(a.Punkte ?? 0, f.Punkte)
            : bewertung.Status;
        return new AntwortStand(f.Id, eingabe, status, a.Punkte, f.Punkte, bewertung.RichtigeLuecken, a.Feedback,
            a.KorrigiertVonId is not null, loesungSichtbar ? FragenAbbildung.Loesung(f) : null);
    }

    public static BewertungsStatus StatusAusPunkten(decimal punkte, decimal max)
        => punkte >= max ? BewertungsStatus.Richtig : punkte > 0 ? BewertungsStatus.Teilweise : BewertungsStatus.Falsch;

    public static VersuchInfo Info(Versuch v) => new(v.Id, v.Nr, v.Status, v.Punkte, v.MaxPunkte, v.StartUtc, v.AbgabeUtc, v.DeadlineUtc);

    /// <summary>Neuer Versuch mit eingefrorenen aktuellen Fragen.</summary>
    public static async Task<Versuch> NeuerVersuchAsync(SkoulouDbContext db, Zuweisung z, Guid schuelerId, DateTime jetzt, CancellationToken ct)
    {
        var aktuelle = await FragenAbbildung.AktuelleAsync(db, z.LeconId, z.PruefungId, ct);
        var fragen = aktuelle.SelectMany(a => a.Fragen).ToList();
        var nr = (await db.Versuche.Where(v => v.ZuweisungId == z.Id && v.SchuelerId == schuelerId).MaxAsync(v => (int?)v.Nr, ct) ?? 0) + 1;
        var dauer = z.PruefungId is { } pid ? await db.Pruefungen.Where(p => p.Id == pid).Select(p => p.DauerMinuten).FirstAsync(ct) : 0;
        var v = new Versuch
        {
            SchuleId = z.SchuleId, ZuweisungId = z.Id, SchuelerId = schuelerId, Nr = nr, StartUtc = jetzt, Status = VersuchsStatus.EnCours,
            FragenIds = fragen.Select(f => f.Id).ToList(), MaxPunkte = fragen.Sum(f => f.Punkte),
            DeadlineUtc = z.PruefungId is not null ? PruefungsZeit.Deadline(jetzt, dauer, z.FristUtc) : z.FristUtc
        };
        db.Versuche.Add(v);
        return v;
    }

    /// <summary>Berechnet den LernStand (Schüler × Zuweisung) neu – nach jedem Speichern eines Versuchs.</summary>
    public static async Task LernStandAsync(SkoulouDbContext db, Zuweisung z, Guid schuelerId, DateTime jetzt, CancellationToken ct,
        bool aktivitaet = true)
    {
        for (var versuch = 0; ; versuch++)
        {
            var versuche = await db.Versuche.AsNoTracking().Where(v => v.ZuweisungId == z.Id && v.SchuelerId == schuelerId).ToListAsync(ct);
            var gewertet = VersuchsRegel.Gewertet(versuche.Where(v => v.Status != VersuchsStatus.EnCours)
                .Select(v => new VersuchsWert(v.Nr, v.Punkte, v.MaxPunkte, v.Status == VersuchsStatus.ACorriger)), z.Wertung);
            var stand = await db.LernStaende.FirstOrDefaultAsync(s => s.ZuweisungId == z.Id && s.SchuelerId == schuelerId, ct);
            if (stand is null)
            {
                stand = new LernStand { SchuleId = z.SchuleId, ZuweisungId = z.Id, SchuelerId = schuelerId };
                db.LernStaende.Add(stand);
            }
            stand.AnzahlVersuche = versuche.Count;
            stand.Abgeschlossen = gewertet is not null;
            stand.BestePunkte = gewertet?.Punkte ?? 0;
            stand.MaxPunkte = gewertet?.MaxPunkte ?? versuche.OrderBy(v => v.Nr).LastOrDefault()?.MaxPunkte ?? 0;
            stand.Prozent = gewertet is null ? 0 : ScoreRechner.Prozent(gewertet.Punkte, gewertet.MaxPunkte) ?? 0;
            stand.KorrekturOffen = gewertet?.KorrekturOffen ?? false;
            if (aktivitaet || stand.LetzteAktivitaetUtc is null) stand.LetzteAktivitaetUtc = jetzt;
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException ex) when (versuch == 0 && DbTransaktion.IstEindeutigkeitsFehler(ex))
            {
                // Gleichzeitig angelegt – neu laden und aktualisieren.
                db.ChangeTracker.Clear();
            }
        }
    }
}
