using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.WebApp.Services.Formation;

/// <summary>
/// Formation der Responsables SKOULOU (Nutzerwunsch 17.11.2026, gleich wie eNOTE): Lernpfad + Prüfung in Runden.
/// Runde alle <see cref="FormationTeilnehmer.IntervallMonate"/> Monate (Standard 3), Frist 30 Tage, höchstens 3 Versuche, nur der
/// letzte zählt; nach jedem Versuch Note + Fehler. Keine Sperre – der Administrateur entscheidet (Intervall, Pause, Frist, erlassen,
/// neue Runde, « Retirer les écoles et bloquer »). Administrateure trainieren frei (ohne Frist, ohne Grenze) in beiden Pfaden.
/// Fehler kommen als französischer Text zurück (null = in Ordnung).
/// </summary>
public class FormationService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit, SitzungsPruefung sitzungen,
    TimeProvider? uhr = null, IReadOnlyList<LernLektion>? katalog = null, IReadOnlyList<LernLektion>? adminKatalog = null)
{
    private readonly TimeProvider zeit = uhr ?? TimeProvider.System;

    /// <summary>Lernpfad der Responsables (Prüfungsrunden).</summary>
    public IReadOnlyList<LernLektion> Lektionen { get; } = katalog ?? FormationKatalog.Lektionen;
    /// <summary>Lernpfad der Administrateure (nur freies Training).</summary>
    public IReadOnlyList<LernLektion> AdminLektionen { get; } = adminKatalog ?? FormationKatalog.AdminLektionen;
    public IReadOnlyList<LernLektion> Pfad(bool admin) => admin ? AdminLektionen : Lektionen;
    public bool IstAdminLektion(string? id) => AdminLektionen.Any(l => l.Id == id);

    public static readonly DateTime OhneFrist = new(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);
    public const int ErinnerungTageVorFrist = 7;
    public const int SehrGutAbProzent = 90;
    public const string AuditRolleEntzogen = "formation.responsable-retire";

    private DateTime Jetzt => zeit.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ Datentypen für die Seiten

    public sealed record GezogeneFrage(string Id, int[] Reihenfolge);
    public sealed record VersuchKurz(Guid Id, int Nummer, DateTime BegonnenUtc, DateTime? AbgegebenUtc, int? Prozent, int? Richtige, int FragenAnzahl);

    public sealed record RundeKurz(Guid Id, DateTime GeoeffnetUtc, DateTime FristUtc, DateTime? AbgeschlossenUtc, FormationRundenStatus Status,
        int? EndNoteProzent, int Grenze, int FragenAnzahl, bool IstFrei, string? ErlassenGrund, IReadOnlyList<VersuchKurz> Versuche, bool AdminPfad = false)
    {
        public VersuchKurz? Laufend => Versuche.FirstOrDefault(v => v.AbgegebenUtc is null);
        public VersuchKurz? LetzterAbgegeben => Versuche.Where(v => v.AbgegebenUtc is not null).MaxBy(v => v.Nummer);
        public int Abgegeben => Versuche.Count(v => v.AbgegebenUtc is not null);
        public int Uebrig => IstFrei ? int.MaxValue : Math.Max(0, FormationRunde.MaxVersuche - Versuche.Count + (Laufend is null ? 0 : 1));
    }

    public sealed record Stand(bool IstFrei, IReadOnlySet<string> Gelesen, RundeKurz? Offen, IReadOnlyList<RundeKurz> Verlauf,
        DateTime? NaechsteRundeAbUtc, DateTime? PausiertBisUtc, int IntervallMonate, int Grenze, int FragenJePruefung);

    public sealed record AntwortAnzeige(int Index, string Text);
    public sealed record FrageAnzeige(int Nr, string Id, string Text, bool Mehrfach, IReadOnlyList<AntwortAnzeige> Antworten, IReadOnlyList<int> Auswahl);
    public sealed record VersuchAnsicht(Guid Id, Guid RundeId, int Nummer, bool IstFrei, DateTime FristUtc, IReadOnlyList<FrageAnzeige> Fragen, bool AdminPfad = false)
    {
        public int Beantwortet => Fragen.Count(f => f.Auswahl.Count > 0);
    }

    public sealed record FrageErgebnis(int Nr, string Text, bool Richtig, IReadOnlyList<string> DeineAntworten, IReadOnlyList<string> RichtigeAntworten,
        string Erklaerung, string? LektionId, string? LektionTitel);
    public sealed record Ergebnis(Guid VersuchId, int Nummer, int Prozent, int Richtige, int Gesamt, int Grenze, bool Bestanden, bool IstFrei,
        bool RundeAbgeschlossen, int VersucheUebrig, DateTime FristUtc, IReadOnlyList<FrageErgebnis> Fragen, bool AdminPfad = false);

    public sealed record PoZeile(Guid KontoId, string Name, string? Email, int IntervallMonate, DateTime? PausiertBisUtc, DateTime? NaechsteRundeAbUtc,
        RundeKurz? Offen, RundeKurz? LetzteGewertete, int Gelesen, int LektionenGesamt, bool Schwach, bool SehrGut, IReadOnlyList<RundeKurz> Verlauf,
        int Schulen);

    public sealed record Meldung(Guid RundeId, Guid KontoId, string Art, DateTime FristUtc, int? Note, int Grenze, int Fragen = 0);

    // ------------------------------------------------------------------ Regeln

    public async Task<FormationRegel> GetRegelAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await RegelAsync(db, ct);
    }

    private static async Task<FormationRegel> RegelAsync(SkoulouDbContext db, CancellationToken ct)
        => await db.FormationRegeln.AsNoTracking().OrderBy(r => r.ErstelltAmUtc).FirstOrDefaultAsync(ct) ?? new FormationRegel();

    public async Task<string?> SpeichereRegelAsync(Guid adminId, int grenze, int fragen, int fristTage, int intervall, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(adminId, ct);
        if (grenze is < 10 or > 100) return "Le seuil de réussite doit être entre 10 et 100 %.";
        var maxFragen = Lektionen.Sum(l => l.Pruefung.Count);
        if (fragen < 5 || fragen > maxFragen) return $"Le nombre de questions doit être entre 5 et {maxFragen}.";
        if (fristTage is < 3 or > 120) return "Le délai doit être entre 3 et 120 jours.";
        if (intervall is < 1 or > 24) return "L'intervalle doit être entre 1 et 24 mois.";
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var regel = await db.FormationRegeln.OrderBy(r => r.ErstelltAmUtc).FirstOrDefaultAsync(ct);
        if (regel is null) db.FormationRegeln.Add(regel = new FormationRegel());
        regel.BestehensGrenzeProzent = grenze;
        regel.FragenJePruefung = fragen;
        regel.FristTage = fristTage;
        regel.StandardIntervallMonate = intervall;
        await db.SaveChangesAsync(ct);
        return null;
    }

    // ------------------------------------------------------------------ Wer nimmt teil?

    /// <summary>Aktive Responsables (gesperrte und archivierte nehmen nicht teil).</summary>
    private static IQueryable<Konto> AktiveResponsables(SkoulouDbContext db)
        => db.Users.Where(k => k.Rolle == Rollen.Responsable && k.Status == KontoStatus.Actif);

    /// <summary>
    /// Prüft die Person der eigenen Seite: Responsable (aktiv) für echte Runden, SuperAdmin für das freie Training.
    /// Liefert true = freies Training.
    /// </summary>
    public async Task<bool> PruefeTeilnehmerAsync(Guid kontoId, CancellationToken ct = default)
    {
        var akteur = await zugriff.LadeAkteurAsync(kontoId, ct);
        if (akteur.Status != KontoStatus.Actif) throw new Skoulou.Core.Fehler.KeinZugriffFehler(Zugriff.MeldungKontoInaktiv);
        return akteur.Rolle switch
        {
            Rollen.Responsable => false,
            Rollen.SuperAdmin => true,
            _ => throw new Skoulou.Core.Fehler.KeinZugriffFehler()
        };
    }

    // ------------------------------------------------------------------ Runden führen

    public async Task AktualisiereAsync(Guid kontoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await AktualisiereAsync(db, kontoId, await RegelAsync(db, ct), ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Alle aktiven Responsables aktualisieren (Hintergrunddienst).</summary>
    public async Task AktualisiereAlleAsync(CancellationToken ct = default)
    {
        List<Guid> ids;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            ids = await AktiveResponsables(db).Select(k => k.Id).ToListAsync(ct);
        foreach (var id in ids) await AktualisiereAsync(id, ct);
    }

    private async Task<FormationTeilnehmer> TeilnehmerAsync(SkoulouDbContext db, Guid kontoId, FormationRegel regel, CancellationToken ct)
    {
        var t = await db.FormationTeilnehmer.FirstOrDefaultAsync(x => x.KontoId == kontoId, ct);
        if (t is not null) return t;
        t = new FormationTeilnehmer { KontoId = kontoId, IntervallMonate = regel.StandardIntervallMonate, NaechsteRundeAbUtc = Jetzt };
        db.FormationTeilnehmer.Add(t);
        return t;
    }

    private async Task AktualisiereAsync(SkoulouDbContext db, Guid kontoId, FormationRegel regel, CancellationToken ct)
    {
        var t = await TeilnehmerAsync(db, kontoId, regel, ct);
        var offen = await db.FormationRunden.Include(r => r.Versuche)
            .FirstOrDefaultAsync(r => r.KontoId == kontoId && !r.IstFrei && r.Status == FormationRundenStatus.Offen, ct);
        if (offen is not null && Jetzt > offen.FristUtc)
        {
            SchliesseAb(db, offen, t, wertetLaufenden: true);
            offen = null;
        }
        if (offen is null && !(t.PausiertBisUtc > Jetzt) && Jetzt >= t.NaechsteRundeAbUtc)
        {
            db.FormationRunden.Add(new FormationRunde
            {
                KontoId = kontoId, GeoeffnetUtc = Jetzt, FristUtc = Jetzt.AddDays(regel.FristTage), Status = FormationRundenStatus.Offen,
                BestehensGrenzeProzent = regel.BestehensGrenzeProzent, FragenAnzahl = regel.FragenJePruefung
            });
        }
    }

    private void SchliesseAb(SkoulouDbContext db, FormationRunde runde, FormationTeilnehmer? t, bool wertetLaufenden)
    {
        if (wertetLaufenden)
        {
            foreach (var v in runde.Versuche.Where(v => v.AbgegebenUtc is null).ToList())
            {
                if (LeseAntworten(v).Count > 0) Werte(v);
                else { runde.Versuche.Remove(v); db.FormationVersuche.Remove(v); }
            }
        }
        var letzter = runde.Versuche.Where(v => v.AbgegebenUtc is not null).MaxBy(v => v.Nummer);
        runde.AbgeschlossenUtc = Jetzt;
        runde.EndNoteProzent = letzter?.Prozent;
        runde.Status = letzter is null ? FormationRundenStatus.Verpasst
            : letzter.Prozent >= runde.BestehensGrenzeProzent ? FormationRundenStatus.Bestanden : FormationRundenStatus.NichtBestanden;
        if (t is not null && !runde.IstFrei) t.NaechsteRundeAbUtc = Jetzt.AddMonths(t.IntervallMonate);
    }

    // ------------------------------------------------------------------ Lernpfad

    public async Task MarkiereGelesenAsync(Guid kontoId, string lektionId, CancellationToken ct = default)
    {
        var frei = await PruefeTeilnehmerAsync(kontoId, ct);
        if (Lektionen.All(l => l.Id != lektionId) && !(frei && IstAdminLektion(lektionId))) return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.FormationLektionenGelesen.AnyAsync(g => g.KontoId == kontoId && g.LektionId == lektionId, ct)) return;
        db.FormationLektionenGelesen.Add(new FormationLektionGelesen { KontoId = kontoId, LektionId = lektionId, GelesenUtc = Jetzt });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { /* gleichzeitig auf zwei Geräten */ }
    }

    // ------------------------------------------------------------------ Stand für die eigene Seite

    public async Task<Stand> GetStandAsync(Guid kontoId, CancellationToken ct = default)
    {
        var istFrei = await PruefeTeilnehmerAsync(kontoId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var regel = await RegelAsync(db, ct);
        FormationTeilnehmer? t = null;
        if (!istFrei)
        {
            await AktualisiereAsync(db, kontoId, regel, ct);
            await db.SaveChangesAsync(ct);
            t = await db.FormationTeilnehmer.AsNoTracking().FirstAsync(x => x.KontoId == kontoId, ct);
        }
        var gelesen = (await db.FormationLektionenGelesen.AsNoTracking().Where(g => g.KontoId == kontoId).Select(g => g.LektionId).ToListAsync(ct)).ToHashSet();
        var runden = await db.FormationRunden.AsNoTracking().Include(r => r.Versuche)
            .Where(r => r.KontoId == kontoId && r.IstFrei == istFrei)
            .OrderByDescending(r => r.GeoeffnetUtc).Take(20).ToListAsync(ct);
        var offen = runden.FirstOrDefault(r => r.Status == FormationRundenStatus.Offen);
        return new Stand(istFrei, gelesen, offen is null ? null : Kurz(offen),
            runden.Where(r => r.Status != FormationRundenStatus.Offen).Select(Kurz).ToList(),
            t?.NaechsteRundeAbUtc, t?.PausiertBisUtc, t?.IntervallMonate ?? regel.StandardIntervallMonate,
            offen?.BestehensGrenzeProzent ?? regel.BestehensGrenzeProzent, offen?.FragenAnzahl ?? regel.FragenJePruefung);
    }

    private RundeKurz Kurz(FormationRunde r) => new(r.Id, r.GeoeffnetUtc, r.FristUtc, r.AbgeschlossenUtc, r.Status, r.EndNoteProzent,
        r.BestehensGrenzeProzent, r.FragenAnzahl, r.IstFrei, r.ErlassenGrund,
        r.Versuche.OrderBy(v => v.Nummer).Select(v => new VersuchKurz(v.Id, v.Nummer, v.BegonnenUtc, v.AbgegebenUtc, v.Prozent, v.Richtige, v.FragenAnzahl)).ToList(),
        r.Versuche.Any(IstAdminVersuch));

    // ------------------------------------------------------------------ Versuch

    /// <param name="adminPfad">Nur im freien Training (Administrateur): Fragen aus dem Pfad der Administrateure.</param>
    public async Task<(VersuchAnsicht? Versuch, string? Fehler)> StarteVersuchAsync(Guid kontoId, bool adminPfad = false, CancellationToken ct = default)
    {
        var istFrei = await PruefeTeilnehmerAsync(kontoId, ct);
        if (!istFrei) adminPfad = false;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var regel = await RegelAsync(db, ct);
        FormationRunde? runde;
        if (istFrei)
        {
            runde = await db.FormationRunden.Include(r => r.Versuche)
                .FirstOrDefaultAsync(r => r.KontoId == kontoId && r.IstFrei && r.Status == FormationRundenStatus.Offen, ct);
            if (runde is not null && runde.Versuche.Any(v => IstAdminVersuch(v) != adminPfad))
            {
                db.FormationRunden.Remove(runde);
                await db.SaveChangesAsync(ct);
                runde = null;
            }
            if (runde is null)
            {
                runde = new FormationRunde
                {
                    KontoId = kontoId, IstFrei = true, GeoeffnetUtc = Jetzt, FristUtc = OhneFrist, Status = FormationRundenStatus.Offen,
                    BestehensGrenzeProzent = regel.BestehensGrenzeProzent, FragenAnzahl = regel.FragenJePruefung
                };
                db.FormationRunden.Add(runde);
            }
        }
        else
        {
            await AktualisiereAsync(db, kontoId, regel, ct);
            await db.SaveChangesAsync(ct);
            runde = await db.FormationRunden.Include(r => r.Versuche)
                .FirstOrDefaultAsync(r => r.KontoId == kontoId && !r.IstFrei && r.Status == FormationRundenStatus.Offen, ct);
            if (runde is null) return (null, "Aucune session d'évaluation n'est ouverte pour le moment.");
        }

        var laufend = runde.Versuche.FirstOrDefault(v => v.AbgegebenUtc is null);
        if (laufend is null)
        {
            if (!runde.IstFrei && runde.Versuche.Count >= FormationRunde.MaxVersuche)
                return (null, "Vous avez utilisé vos trois essais pour cette session.");
            var gesehen = runde.Versuche.SelectMany(v => LeseFragen(v).Select(f => f.Id)).ToHashSet();
            var fragen = ZieheFragen(runde.FragenAnzahl, gesehen, Pfad(adminPfad));
            laufend = new FormationVersuch
            {
                RundeId = runde.Id, Nummer = runde.Versuche.Count + 1, BegonnenUtc = Jetzt,
                FragenJson = JsonSerializer.Serialize(fragen), FragenAnzahl = fragen.Count
            };
            db.FormationVersuche.Add(laufend);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                await using var db2 = await dbFactory.CreateDbContextAsync(ct);
                var anderer = await db2.FormationVersuche.Include(v => v.Runde)
                    .FirstOrDefaultAsync(v => v.RundeId == runde.Id && v.AbgegebenUtc == null, ct);
                return anderer is null ? (null, "Veuillez réessayer.") : (Ansicht(anderer, anderer.Runde), null);
            }
        }
        return (Ansicht(laufend, runde), null);
    }

    public async Task<VersuchAnsicht?> GetVersuchAsync(Guid kontoId, Guid versuchId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var v = await db.FormationVersuche.AsNoTracking().Include(x => x.Runde)
            .FirstOrDefaultAsync(x => x.Id == versuchId && x.Runde.KontoId == kontoId, ct);
        return v is null || v.AbgegebenUtc is not null || v.Runde.Status != FormationRundenStatus.Offen ? null : Ansicht(v, v.Runde);
    }

    public async Task<string?> SpeichereAntwortAsync(Guid kontoId, Guid versuchId, string frageId, IReadOnlyCollection<int> auswahl, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var v = await db.FormationVersuche.Include(x => x.Runde).FirstOrDefaultAsync(x => x.Id == versuchId && x.Runde.KontoId == kontoId, ct);
        if (v is null || v.AbgegebenUtc is not null || v.Runde.Status != FormationRundenStatus.Offen) return "Cet essai est déjà terminé.";
        if (Jetzt > v.Runde.FristUtc) return "Le délai de cette session est dépassé.";
        var gezogen = LeseFragen(v).FirstOrDefault(f => f.Id == frageId);
        if (gezogen is null) return "Question inconnue.";
        var antworten = LeseAntworten(v);
        antworten[frageId] = auswahl.Where(i => i >= 0 && i < gezogen.Reihenfolge.Length).Distinct().OrderBy(i => i).ToArray();
        v.AntwortenJson = JsonSerializer.Serialize(antworten);
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<(Ergebnis? Ergebnis, string? Fehler)> GibAbAsync(Guid kontoId, Guid versuchId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var v = await db.FormationVersuche.Include(x => x.Runde).ThenInclude(r => r.Versuche)
            .FirstOrDefaultAsync(x => x.Id == versuchId && x.Runde.KontoId == kontoId, ct);
        if (v is null) return (null, "Essai introuvable.");
        if (v.AbgegebenUtc is null)
        {
            if (v.Runde.Status != FormationRundenStatus.Offen) return (null, "Cette session est déjà terminée.");
            Werte(v);
            if (v.Runde.IstFrei || v.Runde.Versuche.Count(x => x.AbgegebenUtc is not null) >= FormationRunde.MaxVersuche)
            {
                var t = v.Runde.IstFrei ? null : await db.FormationTeilnehmer.FirstOrDefaultAsync(x => x.KontoId == kontoId, ct);
                SchliesseAb(db, v.Runde, t, wertetLaufenden: false);
            }
            await db.SaveChangesAsync(ct);
        }
        return (ErgebnisVon(v, v.Runde), null);
    }

    public async Task<Ergebnis?> GetErgebnisAsync(Guid kontoId, Guid versuchId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var v = await db.FormationVersuche.AsNoTrackingWithIdentityResolution().Include(x => x.Runde).ThenInclude(r => r.Versuche)
            .FirstOrDefaultAsync(x => x.Id == versuchId && x.Runde.KontoId == kontoId, ct);
        return v?.AbgegebenUtc is null ? null : ErgebnisVon(v, v.Runde);
    }

    public async Task<string?> BehalteErgebnisAsync(Guid kontoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var runde = await db.FormationRunden.Include(r => r.Versuche)
            .FirstOrDefaultAsync(r => r.KontoId == kontoId && !r.IstFrei && r.Status == FormationRundenStatus.Offen, ct);
        if (runde is null) return "Aucune session ouverte.";
        if (!runde.Versuche.Any(v => v.AbgegebenUtc is not null)) return "Terminez d'abord un essai.";
        foreach (var offen in runde.Versuche.Where(v => v.AbgegebenUtc is null).ToList())
        {
            runde.Versuche.Remove(offen);
            db.FormationVersuche.Remove(offen);
        }
        var t = await db.FormationTeilnehmer.FirstOrDefaultAsync(x => x.KontoId == kontoId, ct);
        SchliesseAb(db, runde, t, wertetLaufenden: false);
        await db.SaveChangesAsync(ct);
        return null;
    }

    // ------------------------------------------------------------------ Fragen ziehen und werten

    public List<GezogeneFrage> ZieheFragen(int anzahl, IReadOnlySet<string> schonGesehen, IReadOnlyList<LernLektion>? quelle = null)
    {
        var zufall = Random.Shared;
        var stapel = (quelle ?? Lektionen)
            .Where(l => l.Pruefung.Count > 0)
            .Select(l => new Queue<LernFrage>(l.Pruefung.OrderBy(f => schonGesehen.Contains(f.Id)).ThenBy(_ => zufall.Next())))
            .OrderBy(_ => zufall.Next())
            .ToList();
        var gezogen = new List<LernFrage>();
        while (gezogen.Count < anzahl && stapel.Any(s => s.Count > 0))
        {
            foreach (var s in stapel.Where(s => s.Count > 0).OrderBy(s => schonGesehen.Contains(s.Peek().Id)).ToList())
            {
                if (gezogen.Count >= anzahl) break;
                gezogen.Add(s.Dequeue());
            }
        }
        return gezogen.OrderBy(_ => zufall.Next())
            .Select(f => new GezogeneFrage(f.Id, Enumerable.Range(0, f.Antworten.Count).OrderBy(_ => zufall.Next()).ToArray()))
            .ToList();
    }

    private (LernFrage Frage, LernLektion Lektion)? Finde(string frageId)
    {
        foreach (var l in Lektionen.Concat(AdminLektionen))
        {
            var f = l.Pruefung.FirstOrDefault(x => x.Id == frageId);
            if (f is not null) return (f, l);
        }
        return null;
    }

    private static List<GezogeneFrage> LeseFragen(FormationVersuch v) => JsonSerializer.Deserialize<List<GezogeneFrage>>(v.FragenJson) ?? [];
    private static Dictionary<string, int[]> LeseAntworten(FormationVersuch v) => JsonSerializer.Deserialize<Dictionary<string, int[]>>(v.AntwortenJson) ?? [];

    private bool IstAdminVersuch(FormationVersuch v)
    {
        var erste = LeseFragen(v).FirstOrDefault()?.Id;
        return erste is not null && AdminLektionen.Any(l => l.Pruefung.Any(f => f.Id == erste));
    }

    private static int[] OriginalIndizes(GezogeneFrage g, int[] anzeige) => anzeige.Where(i => i >= 0 && i < g.Reihenfolge.Length).Select(i => g.Reihenfolge[i]).ToArray();

    private void Werte(FormationVersuch v)
    {
        var antworten = LeseAntworten(v);
        var gewertet = LeseFragen(v).Select(g => (g, Finde(g.Id))).Where(x => x.Item2 is not null).ToList();
        var richtige = gewertet.Count(x => x.Item2!.Value.Frage.IstRichtig(antworten.TryGetValue(x.g.Id, out var a) ? OriginalIndizes(x.g, a) : null));
        v.Richtige = richtige;
        v.FragenAnzahl = gewertet.Count;
        v.Prozent = gewertet.Count == 0 ? 0 : (int)Math.Round(100.0 * richtige / gewertet.Count, MidpointRounding.AwayFromZero);
        v.AbgegebenUtc = Jetzt;
    }

    private VersuchAnsicht Ansicht(FormationVersuch v, FormationRunde runde)
    {
        var antworten = LeseAntworten(v);
        var fragen = new List<FrageAnzeige>();
        var nr = 0;
        foreach (var g in LeseFragen(v))
        {
            if (Finde(g.Id) is not { } treffer) continue;
            nr++;
            fragen.Add(new FrageAnzeige(nr, g.Id, treffer.Frage.Text, treffer.Frage.Mehrfach,
                g.Reihenfolge.Select((orig, pos) => new AntwortAnzeige(pos, treffer.Frage.Antworten[orig])).ToList(),
                antworten.TryGetValue(g.Id, out var a) ? a : []));
        }
        return new VersuchAnsicht(v.Id, runde.Id, v.Nummer, runde.IstFrei, runde.FristUtc, fragen, IstAdminVersuch(v));
    }

    private Ergebnis ErgebnisVon(FormationVersuch v, FormationRunde runde)
    {
        var antworten = LeseAntworten(v);
        var liste = new List<FrageErgebnis>();
        var nr = 0;
        foreach (var g in LeseFragen(v))
        {
            if (Finde(g.Id) is not { } t) continue;
            nr++;
            var original = antworten.TryGetValue(g.Id, out var a) ? OriginalIndizes(g, a) : [];
            liste.Add(new FrageErgebnis(nr, t.Frage.Text, t.Frage.IstRichtig(original),
                original.OrderBy(i => i).Select(i => t.Frage.Antworten[i]).ToList(),
                t.Frage.Richtig.OrderBy(i => i).Select(i => t.Frage.Antworten[i]).ToList(),
                t.Frage.Erklaerung, t.Lektion.Id, t.Lektion.Titel));
        }
        var uebrig = runde.IstFrei ? int.MaxValue : Math.Max(0, FormationRunde.MaxVersuche - runde.Versuche.Count);
        return new Ergebnis(v.Id, v.Nummer, v.Prozent ?? 0, v.Richtige ?? 0, v.FragenAnzahl, runde.BestehensGrenzeProzent,
            (v.Prozent ?? 0) >= runde.BestehensGrenzeProzent, runde.IstFrei, runde.Status != FormationRundenStatus.Offen,
            runde.Status == FormationRundenStatus.Offen ? uebrig : 0, runde.FristUtc, liste, IstAdminVersuch(v));
    }

    // ------------------------------------------------------------------ Administrateur

    public async Task<List<PoZeile>> GetUebersichtAsync(Guid adminId, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(adminId, ct);
        await AktualisiereAlleAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var konten = await db.Users.AsNoTracking().Where(k => k.Rolle == Rollen.Responsable && k.Status == KontoStatus.Actif)
            .Select(k => new { k.Id, k.Vorname, k.Nachname, k.Email }).ToListAsync(ct);
        var ids = konten.Select(k => k.Id).ToList();
        var teilnehmer = await db.FormationTeilnehmer.AsNoTracking().Where(t => ids.Contains(t.KontoId)).ToDictionaryAsync(t => t.KontoId, ct);
        var runden = await db.FormationRunden.AsNoTracking().Include(r => r.Versuche).Where(r => ids.Contains(r.KontoId) && !r.IstFrei).ToListAsync(ct);
        var gelesen = await db.FormationLektionenGelesen.AsNoTracking().Where(g => ids.Contains(g.KontoId))
            .GroupBy(g => g.KontoId).Select(g => new { g.Key, Anzahl = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Anzahl, ct);
        var schulen = await db.Schulen.AsNoTracking().Where(s => s.ResponsableId != null && ids.Contains(s.ResponsableId.Value))
            .GroupBy(s => s.ResponsableId!.Value).Select(g => new { g.Key, Anzahl = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Anzahl, ct);
        var poLektionen = Lektionen.Select(l => l.Id).ToHashSet();

        return konten.Select(k =>
        {
            teilnehmer.TryGetValue(k.Id, out var t);
            var eigene = runden.Where(r => r.KontoId == k.Id).OrderByDescending(r => r.GeoeffnetUtc).ToList();
            var gewertet = eigene.Where(r => r.Status is FormationRundenStatus.Bestanden or FormationRundenStatus.NichtBestanden or FormationRundenStatus.Verpasst).ToList();
            var schwach = gewertet.Count >= 2 && gewertet.Take(2).All(r => r.Status != FormationRundenStatus.Bestanden);
            var sehrGut = gewertet.Count >= 3 && gewertet.Take(3).All(r => r.Status == FormationRundenStatus.Bestanden && r.EndNoteProzent >= SehrGutAbProzent)
                          && (t?.IntervallMonate ?? 3) < 12;
            var offen = eigene.FirstOrDefault(r => r.Status == FormationRundenStatus.Offen);
            return new PoZeile(k.Id, $"{k.Vorname} {k.Nachname}".Trim(), k.Email, t?.IntervallMonate ?? 3, t?.PausiertBisUtc, t?.NaechsteRundeAbUtc,
                offen is null ? null : Kurz(offen), gewertet.Count > 0 ? Kurz(gewertet[0]) : null,
                Math.Min(gelesen.GetValueOrDefault(k.Id), poLektionen.Count), Lektionen.Count, schwach, sehrGut, eigene.Select(Kurz).ToList(),
                schulen.GetValueOrDefault(k.Id));
        }).OrderByDescending(z => z.Schwach).ThenBy(z => z.Name).ToList();
    }

    public async Task<(Ergebnis? Ergebnis, string? Name)> GetErgebnisFuerAdminAsync(Guid adminId, Guid versuchId, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(adminId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var v = await db.FormationVersuche.AsNoTrackingWithIdentityResolution().Include(x => x.Runde).ThenInclude(r => r.Versuche)
            .FirstOrDefaultAsync(x => x.Id == versuchId, ct);
        if (v?.AbgegebenUtc is null) return (null, null);
        var name = await db.Users.AsNoTracking().Where(k => k.Id == v.Runde.KontoId).Select(k => k.Vorname + " " + k.Nachname).FirstOrDefaultAsync(ct);
        return (ErgebnisVon(v, v.Runde), name);
    }

    private async Task<(SkoulouDbContext Db, FormationTeilnehmer T, FormationRunde? Offen)> LadeFuerAdminAsync(Guid adminId, Guid kontoId, CancellationToken ct)
    {
        await zugriff.AdminAsync(adminId, ct);
        var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Users.AnyAsync(k => k.Id == kontoId && k.Rolle == Rollen.Responsable, ct))
        {
            await db.DisposeAsync();
            throw new Skoulou.Core.Fehler.FachFehler("Ce compte n'est pas un responsable.");
        }
        await AktualisiereAsync(db, kontoId, await RegelAsync(db, ct), ct);
        await db.SaveChangesAsync(ct);
        var t = await db.FormationTeilnehmer.FirstAsync(x => x.KontoId == kontoId, ct);
        var offen = await db.FormationRunden.Include(r => r.Versuche)
            .FirstOrDefaultAsync(r => r.KontoId == kontoId && !r.IstFrei && r.Status == FormationRundenStatus.Offen, ct);
        return (db, t, offen);
    }

    public async Task<string?> SetzeIntervallAsync(Guid adminId, Guid kontoId, int monate, CancellationToken ct = default)
    {
        if (monate is < 1 or > 24) return "L'intervalle doit être entre 1 et 24 mois.";
        var (db, t, offen) = await LadeFuerAdminAsync(adminId, kontoId, ct);
        await using (db)
        {
            if (offen is null)
            {
                var letzte = await db.FormationRunden.Where(r => r.KontoId == kontoId && !r.IstFrei && r.AbgeschlossenUtc != null)
                    .MaxAsync(r => (DateTime?)r.AbgeschlossenUtc, ct);
                t.NaechsteRundeAbUtc = (letzte ?? Jetzt).AddMonths(monate);
            }
            t.IntervallMonate = monate;
            await db.SaveChangesAsync(ct);
        }
        return null;
    }

    public async Task<string?> SetzePauseAsync(Guid adminId, Guid kontoId, DateTime? bisUtc, CancellationToken ct = default)
    {
        if (bisUtc is { } b && b <= Jetzt) return "Choisissez une date dans le futur.";
        var (db, t, offen) = await LadeFuerAdminAsync(adminId, kontoId, ct);
        await using (db)
        {
            t.PausiertBisUtc = bisUtc;
            if (bisUtc is { } bis)
            {
                if (offen is not null) Erlasse(offen, adminId, $"Pause jusqu'au {bis:dd.MM.yyyy}", t, null);
                if (t.NaechsteRundeAbUtc < bis) t.NaechsteRundeAbUtc = bis;
            }
            await db.SaveChangesAsync(ct);
            if (bisUtc is null)
            {
                if (offen is null && t.NaechsteRundeAbUtc > Jetzt) t.NaechsteRundeAbUtc = Jetzt;
                await AktualisiereAsync(db, kontoId, await RegelAsync(db, ct), ct);
                await db.SaveChangesAsync(ct);
            }
        }
        return null;
    }

    public async Task<string?> VerlaengereFristAsync(Guid adminId, Guid kontoId, DateTime neueFristUtc, CancellationToken ct = default)
    {
        var (db, _, offen) = await LadeFuerAdminAsync(adminId, kontoId, ct);
        await using (db)
        {
            if (offen is null) return "Aucune session ouverte.";
            if (neueFristUtc <= Jetzt || neueFristUtc <= offen.FristUtc) return "La nouvelle date doit être après le délai actuel.";
            offen.FristUtc = neueFristUtc;
            offen.ErinnerungUtc = null;
            await db.SaveChangesAsync(ct);
        }
        return null;
    }

    public async Task<string?> ErlasseRundeAsync(Guid adminId, Guid kontoId, string? grund, CancellationToken ct = default)
    {
        var (db, t, offen) = await LadeFuerAdminAsync(adminId, kontoId, ct);
        await using (db)
        {
            if (offen is null) return "Aucune session ouverte.";
            Erlasse(offen, adminId, string.IsNullOrWhiteSpace(grund) ? "Dispensé par l'administrateur" : grund.Trim(), t, Jetzt.AddMonths(t.IntervallMonate));
            await db.SaveChangesAsync(ct);
        }
        return null;
    }

    private void Erlasse(FormationRunde runde, Guid adminId, string grund, FormationTeilnehmer t, DateTime? naechste)
    {
        runde.Status = FormationRundenStatus.Erlassen;
        runde.AbgeschlossenUtc = Jetzt;
        runde.ErlassenVonId = adminId;
        runde.ErlassenGrund = grund.Length > 500 ? grund[..500] : grund;
        runde.EndNoteProzent = runde.Versuche.Where(v => v.AbgegebenUtc is not null).MaxBy(v => v.Nummer)?.Prozent;
        if (naechste is not null) t.NaechsteRundeAbUtc = naechste.Value;
    }

    public async Task<string?> OeffneRundeJetztAsync(Guid adminId, Guid kontoId, CancellationToken ct = default)
    {
        var (db, t, offen) = await LadeFuerAdminAsync(adminId, kontoId, ct);
        await using (db)
        {
            if (offen is not null) return "Une session est déjà ouverte.";
            t.PausiertBisUtc = null;
            t.NaechsteRundeAbUtc = Jetzt;
            await AktualisiereAsync(db, kontoId, await RegelAsync(db, ct), ct);
            await db.SaveChangesAsync(ct);
        }
        return null;
    }

    public sealed record SchuleDesPo(Guid SchuleId, string Name);
    public sealed record AndererPo(Guid Id, string Name);

    /// <summary>Schulen eines Responsable und die übrigen aktiven Responsables (Neuzuweisung beim Entziehen der Rolle).</summary>
    public async Task<(IReadOnlyList<SchuleDesPo> Schulen, IReadOnlyList<AndererPo> Andere)> GetNeuzuweisungAsync(Guid adminId, Guid kontoId, CancellationToken ct = default)
    {
        await zugriff.AdminAsync(adminId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var schulen = await db.Schulen.AsNoTracking().Where(s => s.ResponsableId == kontoId).OrderBy(s => s.Name)
            .Select(s => new SchuleDesPo(s.Id, s.Name)).ToListAsync(ct);
        var andere = await AktiveResponsables(db).Where(k => k.Id != kontoId).OrderBy(k => k.Vorname).ThenBy(k => k.Nachname)
            .Select(k => new AndererPo(k.Id, k.Vorname + " " + k.Nachname)).ToListAsync(ct);
        return (schulen, andere);
    }

    /// <summary>
    /// SKOULOU kennt genau eine Rolle je Konto: « Retirer le rôle » = Schulen im selben Schritt neu zuweisen (Nutzer 17.11.: keine Lücke
    /// bei den Parts) oder ohne Responsable lassen, Konto sperren (alle Sitzungen enden), offene Runde erlassen, Audit.
    /// Bereits entstandene Parts bleiben. « Débloquer » unter Responsables macht die Sperre rückgängig.
    /// Liefert die Schulen, die ohne Responsable bleiben.
    /// </summary>
    public async Task<(bool Erfolg, string? Fehler, IReadOnlyList<string> OhneResponsable)> EntzieheRolleAsync(Guid adminId, Guid kontoId,
        IReadOnlyDictionary<Guid, Guid?>? neuzuweisung = null, CancellationToken ct = default)
    {
        var admin = await zugriff.AdminAsync(adminId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var konto = await db.Users.FirstOrDefaultAsync(k => k.Id == kontoId, ct);
        if (konto is null || konto.Rolle != Rollen.Responsable) return (false, "Ce compte n'est pas un responsable.", []);
        if (konto.Status != KontoStatus.Actif) return (false, "Ce compte est déjà bloqué ou archivé.", []);
        var gueltig = (await AktiveResponsables(db).Where(k => k.Id != kontoId).Select(k => k.Id).ToListAsync(ct)).ToHashSet();
        if (neuzuweisung is not null && neuzuweisung.Values.Any(v => v is { } id && !gueltig.Contains(id)))
            return (false, "Choisissez un autre responsable actif pour chaque école.", []);

        var schulen = await db.Schulen.Where(s => s.ResponsableId == kontoId).ToListAsync(ct);
        var ohne = new List<string>();
        var neuNamen = new List<string>();
        foreach (var s in schulen)
        {
            Guid? neu = neuzuweisung is not null && neuzuweisung.TryGetValue(s.Id, out var n) ? n : null;
            s.ResponsableId = neu;
            if (neu is null) ohne.Add(s.Name); else neuNamen.Add(s.Name);
        }
        konto.Status = KontoStatus.Bloque;
        konto.AktiveSitzungId = Guid.NewGuid();

        var t = await db.FormationTeilnehmer.FirstOrDefaultAsync(x => x.KontoId == kontoId, ct);
        var offen = await db.FormationRunden.Include(r => r.Versuche)
            .FirstOrDefaultAsync(r => r.KontoId == kontoId && !r.IstFrei && r.Status == FormationRundenStatus.Offen, ct);
        if (offen is not null && t is not null) Erlasse(offen, admin.Id, "Rôle de responsable retiré", t, null);

        audit.Erfasse(db, admin.Id, null, AuditRolleEntzogen, nameof(Konto), konto.Id,
            $"Responsable {konto.VollerName} : rôle retiré (formation), compte bloqué. Écoles réattribuées : {(neuNamen.Count == 0 ? "aucune" : string.Join(", ", neuNamen))} ; sans responsable : {(ohne.Count == 0 ? "aucune" : string.Join(", ", ohne))}.");
        await db.SaveChangesAsync(ct);
        sitzungen.Vergessen(kontoId);
        return (true, null, ohne.OrderBy(x => x).ToList());
    }

    // ------------------------------------------------------------------ Benachrichtigungen

    public async Task<List<Meldung>> GetOffeneMeldungenAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var grenze = Jetzt.AddDays(ErinnerungTageVorFrist);
        var runden = await db.FormationRunden.AsNoTracking().Include(r => r.Versuche).Where(r => !r.IstFrei && (
                (r.Status == FormationRundenStatus.Offen && (r.BenachrichtigtUtc == null || (r.ErinnerungUtc == null && r.FristUtc <= grenze)))
                || ((r.Status == FormationRundenStatus.NichtBestanden || r.Status == FormationRundenStatus.Verpasst) && r.AdminGemeldetUtc == null)))
            .ToListAsync(ct);
        var liste = new List<Meldung>();
        foreach (var r in runden)
        {
            if (r.Status == FormationRundenStatus.Offen)
            {
                if (r.BenachrichtigtUtc is null) liste.Add(new(r.Id, r.KontoId, "ouverte", r.FristUtc, null, r.BestehensGrenzeProzent, r.FragenAnzahl));
                else if (r.ErinnerungUtc is null && r.FristUtc <= grenze && r.Versuche.Count(v => v.AbgegebenUtc != null) < FormationRunde.MaxVersuche)
                {
                    var note = r.Versuche.Where(v => v.AbgegebenUtc != null).MaxBy(v => v.Nummer)?.Prozent;
                    if (note is null || note < r.BestehensGrenzeProzent)
                        liste.Add(new(r.Id, r.KontoId, "rappel", r.FristUtc, note, r.BestehensGrenzeProzent));
                }
            }
            else liste.Add(new(r.Id, r.KontoId, "resultat", r.FristUtc, r.EndNoteProzent, r.BestehensGrenzeProzent));
        }
        return liste;
    }

    public async Task MarkiereGemeldetAsync(Guid rundeId, string art, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var r = await db.FormationRunden.FirstOrDefaultAsync(x => x.Id == rundeId, ct);
        if (r is null) return;
        switch (art)
        {
            case "ouverte": r.BenachrichtigtUtc = Jetzt; break;
            case "rappel": r.ErinnerungUtc = Jetzt; break;
            case "resultat": r.AdminGemeldetUtc = Jetzt; break;
        }
        await db.SaveChangesAsync(ct);
    }
}
