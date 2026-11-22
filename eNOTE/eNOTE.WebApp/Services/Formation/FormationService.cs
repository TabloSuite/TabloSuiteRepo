using System.Text.Json;
using ENote.Core.DataModels.Formation;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services.Formation;

/// <summary>
/// Formation der Responsables (Nutzerwunsch 17.11.2026): Lernpfad + Prüfung in Runden.
/// <list type="bullet">
/// <item>Eine Runde öffnet sich alle <see cref="FormationTeilnehmer.IntervallMonate"/> Monate (Standard 3) mit einer Frist (Standard 30 Tage).</item>
/// <item>Höchstens <see cref="FormationRunde.MaxVersuche"/> Versuche; nur der letzte abgegebene zählt. Nach jedem Versuch: Note + Fehler.</item>
/// <item>Jeder Versuch zieht neue Fragen aus dem Pool (gleichmäßig über die Lektionen, zuerst noch nicht gesehene).</item>
/// <item>Fristablauf: ein angefangener Versuch mit Antworten wird gewertet, ohne Versuch gilt die Runde als « manquée ».</item>
/// <item>Keine Sperre – der Administrateur entscheidet (Intervall, Pause, Frist, erlassen, Rolle entziehen).</item>
/// <item>Administrateure trainieren frei: ohne Frist, beliebig oft, nicht in der Übersicht.</item>
/// </list>
/// </summary>
public class FormationService(IDbContextFactory<ApplicationDbContext> dbFactory, TimeProvider? uhr = null, IReadOnlyList<Lektion>? katalog = null,
    IReadOnlyList<Lektion>? adminKatalog = null)
{
    private readonly TimeProvider zeit = uhr ?? ENote.Core.Services.AppUhr.Provider;
    /// <summary>Lernpfad der Responsables (Prüfungsrunden).</summary>
    public IReadOnlyList<Lektion> Lektionen { get; } = katalog ?? FormationKatalog.Lektionen;
    /// <summary>Lernpfad der Administrateure (nur freies Training, nur für SuperAdmin sichtbar).</summary>
    public IReadOnlyList<Lektion> AdminLektionen { get; } = adminKatalog ?? FormationKatalog.AdminLektionen;
    public IReadOnlyList<Lektion> Pfad(bool admin) => admin ? AdminLektionen : Lektionen;
    public bool IstAdminLektion(string? id) => AdminLektionen.Any(l => l.Id == id);

    /// <summary>Ende eines freien Trainings (« ohne Frist »).</summary>
    public static readonly DateTime OhneFrist = new(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Ab so vielen Tagen vor der Frist kommt die Erinnerung.</summary>
    public const int ErinnerungTageVorFrist = 7;
    /// <summary>« Excellent » ab dieser Note in den letzten drei Runden → Vorschlag, das Intervall zu verlängern.</summary>
    public const int SehrGutAbProzent = 90;

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

    /// <summary>Stand der Formation für die eigene Seite (Responsable oder Administrateur).</summary>
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

    /// <summary>Zeile der Übersicht für den Administrateur.</summary>
    public sealed record PoZeile(Guid PoId, string Name, string? Email, int IntervallMonate, DateTime? PausiertBisUtc, DateTime? NaechsteRundeAbUtc,
        RundeKurz? Offen, RundeKurz? LetzteGewertete, int Gelesen, int LektionenGesamt, bool Schwach, bool SehrGut, IReadOnlyList<RundeKurz> Verlauf);

    // ------------------------------------------------------------------ Regeln

    public async Task<FormationRegel> GetRegelAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await RegelAsync(db, ct);
    }

    private static async Task<FormationRegel> RegelAsync(ApplicationDbContext db, CancellationToken ct)
        => await db.FormationRegeln.AsNoTracking().FirstOrDefaultAsync(r => r.Id == 1, ct) ?? new FormationRegel();

    public async Task<string?> SpeichereRegelAsync(int grenze, int fragen, int fristTage, int intervall, CancellationToken ct = default)
    {
        if (grenze is < 10 or > 100) return "Le seuil de réussite doit être entre 10 et 100 %.";
        var maxFragen = Lektionen.Sum(l => l.Pruefung.Count);
        if (fragen < 5 || fragen > maxFragen) return $"Le nombre de questions doit être entre 5 et {maxFragen}.";
        if (fristTage is < 3 or > 120) return "Le délai doit être entre 3 et 120 jours.";
        if (intervall is < 1 or > 24) return "L'intervalle doit être entre 1 et 24 mois.";
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var regel = await db.FormationRegeln.FirstOrDefaultAsync(r => r.Id == 1, ct);
        if (regel is null) db.FormationRegeln.Add(regel = new FormationRegel());
        regel.BestehensGrenzeProzent = grenze;
        regel.FragenJePruefung = fragen;
        regel.FristTage = fristTage;
        regel.StandardIntervallMonate = intervall;
        regel.GeaendertUtc = Jetzt;
        await db.SaveChangesAsync(ct);
        return null;
    }

    // ------------------------------------------------------------------ Runden führen

    /// <summary>
    /// Bringt die Runden eines Responsable auf den aktuellen Stand (idempotent): abgelaufene Runde abschließen,
    /// fällige Runde öffnen. Wird beim Öffnen der Seiten und stündlich im Hintergrund aufgerufen.
    /// </summary>
    public async Task AktualisiereAsync(Guid poId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await AktualisiereAsync(db, poId, await RegelAsync(db, ct), ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Alle Responsables aktualisieren (Hintergrunddienst).</summary>
    public async Task AktualisiereAlleAsync(CancellationToken ct = default)
    {
        foreach (var poId in await PoIdsAsync(ct))
        {
            await AktualisiereAsync(poId, ct);
        }
    }

    private async Task<List<Guid>> PoIdsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await (from ur in db.UserRoles
                      join r in db.Roles on ur.RoleId equals r.Id
                      where r.Name == AppRollen.ProduktOwner
                      select ur.UserId).Distinct().ToListAsync(ct);
    }

    private async Task<FormationTeilnehmer> TeilnehmerAsync(ApplicationDbContext db, Guid poId, FormationRegel regel, CancellationToken ct)
    {
        var t = await db.FormationTeilnehmer.FirstOrDefaultAsync(x => x.ProduktOwnerId == poId, ct);
        if (t is not null) return t;
        // Neu: erste Runde sofort – mit der vollen Frist, um vorher den Lernpfad zu lesen.
        t = new FormationTeilnehmer { ProduktOwnerId = poId, IntervallMonate = regel.StandardIntervallMonate, NaechsteRundeAbUtc = Jetzt, ErstelltUtc = Jetzt };
        db.FormationTeilnehmer.Add(t);
        return t;
    }

    private async Task AktualisiereAsync(ApplicationDbContext db, Guid poId, FormationRegel regel, CancellationToken ct)
    {
        var t = await TeilnehmerAsync(db, poId, regel, ct);
        var offen = await db.FormationRunden.Include(r => r.Versuche)
            .FirstOrDefaultAsync(r => r.ProduktOwnerId == poId && !r.IstFrei && r.Status == FormationRundenStatus.Offen, ct);
        if (offen is not null && Jetzt > offen.FristUtc)
        {
            SchliesseAb(offen, t, wertetLaufenden: true);
            offen = null;
        }

        if (offen is null && !(t.PausiertBisUtc > Jetzt) && Jetzt >= t.NaechsteRundeAbUtc)
        {
            db.FormationRunden.Add(new FormationRunde
            {
                ProduktOwnerId = poId, GeoeffnetUtc = Jetzt, FristUtc = Jetzt.AddDays(regel.FristTage), Status = FormationRundenStatus.Offen,
                BestehensGrenzeProzent = regel.BestehensGrenzeProzent, FragenAnzahl = regel.FragenJePruefung
            });
        }
    }

    /// <summary>Schließt eine Runde mit dem letzten abgegebenen Versuch ab und plant die nächste.</summary>
    private void SchliesseAb(FormationRunde runde, FormationTeilnehmer? t, bool wertetLaufenden)
    {
        if (wertetLaufenden)
        {
            // Angefangen, aber nicht abgegeben (z. B. Netz weg): mit Antworten zählt er, ohne Antwort nicht.
            foreach (var v in runde.Versuche.Where(v => v.AbgegebenUtc is null).ToList())
            {
                if (LeseAntworten(v).Count > 0) Werte(v, runde);
                else runde.Versuche.Remove(v);
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

    public async Task MarkiereGelesenAsync(Guid userId, string lektionId, CancellationToken ct = default)
    {
        if (Lektionen.All(l => l.Id != lektionId) && !IstAdminLektion(lektionId)) return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.FormationLektionenGelesen.AnyAsync(g => g.ProduktOwnerId == userId && g.LektionId == lektionId, ct)) return;
        db.FormationLektionenGelesen.Add(new FormationLektionGelesen { ProduktOwnerId = userId, LektionId = lektionId, GelesenUtc = Jetzt });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { /* gleichzeitig auf zwei Geräten – schon gelesen */ }
    }

    // ------------------------------------------------------------------ Stand für die eigene Seite

    /// <param name="istFrei">true für Administrateure (freies Training).</param>
    public async Task<Stand> GetStandAsync(Guid userId, bool istFrei, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var regel = await RegelAsync(db, ct);
        FormationTeilnehmer? t = null;
        if (!istFrei)
        {
            await AktualisiereAsync(db, userId, regel, ct);
            await db.SaveChangesAsync(ct);
            t = await db.FormationTeilnehmer.AsNoTracking().FirstAsync(x => x.ProduktOwnerId == userId, ct);
        }

        var gelesen = (await db.FormationLektionenGelesen.AsNoTracking().Where(g => g.ProduktOwnerId == userId).Select(g => g.LektionId).ToListAsync(ct)).ToHashSet();
        var runden = await db.FormationRunden.AsNoTracking().Include(r => r.Versuche)
            .Where(r => r.ProduktOwnerId == userId && r.IstFrei == istFrei)
            .OrderByDescending(r => r.GeoeffnetUtc).Take(20).ToListAsync(ct);
        var offen = runden.FirstOrDefault(r => r.Status == FormationRundenStatus.Offen);
        // Freies Training: offen ist nur ein angefangener Versuch.
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

    /// <summary>
    /// Startet einen Versuch (oder liefert den angefangenen). Fehlertext, wenn keine Runde offen ist oder keine Versuche übrig sind.
    /// </summary>
    /// <param name="adminPfad">Nur im freien Training: Fragen aus dem Pfad der Administrateure statt der Responsables.</param>
    public async Task<(VersuchAnsicht? Versuch, string? Fehler)> StarteVersuchAsync(Guid userId, bool istFrei, CancellationToken ct = default, bool adminPfad = false)
    {
        if (!istFrei) adminPfad = false;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var regel = await RegelAsync(db, ct);
        FormationRunde? runde;
        if (istFrei)
        {
            runde = await db.FormationRunden.Include(r => r.Versuche)
                .FirstOrDefaultAsync(r => r.ProduktOwnerId == userId && r.IstFrei && r.Status == FormationRundenStatus.Offen, ct);
            // Angefangenes Training des anderen Pfads verwerfen (freies Training zählt nicht).
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
                    ProduktOwnerId = userId, IstFrei = true, GeoeffnetUtc = Jetzt, FristUtc = OhneFrist, Status = FormationRundenStatus.Offen,
                    BestehensGrenzeProzent = regel.BestehensGrenzeProzent, FragenAnzahl = regel.FragenJePruefung
                };
                db.FormationRunden.Add(runde);
            }
        }
        else
        {
            await AktualisiereAsync(db, userId, regel, ct);
            await db.SaveChangesAsync(ct);
            runde = await db.FormationRunden.Include(r => r.Versuche)
                .FirstOrDefaultAsync(r => r.ProduktOwnerId == userId && !r.IstFrei && r.Status == FormationRundenStatus.Offen, ct);
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
                // Zweites Gerät hat gleichzeitig gestartet: dessen Versuch weiterführen.
                await using var db2 = await dbFactory.CreateDbContextAsync(ct);
                var anderer = await db2.FormationVersuche.Include(v => v.Runde)
                    .FirstOrDefaultAsync(v => v.RundeId == runde.Id && v.AbgegebenUtc == null, ct);
                return anderer is null ? (null, "Veuillez réessayer.") : (Ansicht(anderer, anderer.Runde), null);
            }
        }

        return (Ansicht(laufend, runde), null);
    }

    /// <summary>Den angefangenen Versuch laden (nach Neuladen der Seite / anderem Gerät).</summary>
    public async Task<VersuchAnsicht?> GetVersuchAsync(Guid userId, Guid versuchId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var v = await db.FormationVersuche.AsNoTracking().Include(x => x.Runde)
            .FirstOrDefaultAsync(x => x.Id == versuchId && x.Runde.ProduktOwnerId == userId, ct);
        return v is null || v.AbgegebenUtc is not null || v.Runde.Status != FormationRundenStatus.Offen ? null : Ansicht(v, v.Runde);
    }

    /// <summary>Speichert die Antwort auf eine Frage sofort (Handy: nichts geht verloren).</summary>
    public async Task<string?> SpeichereAntwortAsync(Guid userId, Guid versuchId, string frageId, IReadOnlyCollection<int> auswahl, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var v = await db.FormationVersuche.Include(x => x.Runde).FirstOrDefaultAsync(x => x.Id == versuchId && x.Runde.ProduktOwnerId == userId, ct);
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

    /// <summary>Gibt den Versuch ab, rechnet die Note und zeigt die Fehler. Der dritte Versuch schließt die Runde.</summary>
    public async Task<(Ergebnis? Ergebnis, string? Fehler)> GibAbAsync(Guid userId, Guid versuchId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var v = await db.FormationVersuche.Include(x => x.Runde).ThenInclude(r => r.Versuche)
            .FirstOrDefaultAsync(x => x.Id == versuchId && x.Runde.ProduktOwnerId == userId, ct);
        if (v is null) return (null, "Essai introuvable.");
        if (v.AbgegebenUtc is null)
        {
            if (v.Runde.Status != FormationRundenStatus.Offen) return (null, "Cette session est déjà terminée.");
            Werte(v, v.Runde);
            // Frei: jeder Versuch ist eine eigene Übung. Sonst nach dem dritten Versuch Schluss.
            if (v.Runde.IstFrei || v.Runde.Versuche.Count(x => x.AbgegebenUtc is not null) >= FormationRunde.MaxVersuche)
            {
                var t = v.Runde.IstFrei ? null : await db.FormationTeilnehmer.FirstOrDefaultAsync(x => x.ProduktOwnerId == userId, ct);
                SchliesseAb(v.Runde, t, wertetLaufenden: false);
            }
            await db.SaveChangesAsync(ct);
        }
        return (ErgebnisVon(v, v.Runde), null);
    }

    /// <summary>Ergebnis eines abgegebenen Versuchs (auch später aus dem Verlauf).</summary>
    public async Task<Ergebnis?> GetErgebnisAsync(Guid userId, Guid versuchId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var v = await db.FormationVersuche.AsNoTrackingWithIdentityResolution().Include(x => x.Runde).ThenInclude(r => r.Versuche)
            .FirstOrDefaultAsync(x => x.Id == versuchId && x.Runde.ProduktOwnerId == userId, ct);
        return v?.AbgegebenUtc is null ? null : ErgebnisVon(v, v.Runde);
    }

    /// <summary>« Garder ce résultat » – die Runde mit dem letzten abgegebenen Versuch endgültig abschließen.</summary>
    public async Task<string?> BehalteErgebnisAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var runde = await db.FormationRunden.Include(r => r.Versuche)
            .FirstOrDefaultAsync(r => r.ProduktOwnerId == userId && !r.IstFrei && r.Status == FormationRundenStatus.Offen, ct);
        if (runde is null) return "Aucune session ouverte.";
        if (!runde.Versuche.Any(v => v.AbgegebenUtc is not null)) return "Terminez d'abord un essai.";
        // Ein angefangener, nicht abgegebener Versuch fällt weg.
        foreach (var offen in runde.Versuche.Where(v => v.AbgegebenUtc is null).ToList())
        {
            runde.Versuche.Remove(offen);
            db.FormationVersuche.Remove(offen);
        }
        var t = await db.FormationTeilnehmer.FirstOrDefaultAsync(x => x.ProduktOwnerId == userId, ct);
        SchliesseAb(runde, t, wertetLaufenden: false);
        await db.SaveChangesAsync(ct);
        return null;
    }

    // ------------------------------------------------------------------ Fragen ziehen und werten

    /// <summary>
    /// Zieht <paramref name="anzahl"/> Prüfungsfragen gleichmäßig über die Lektionen (Lektionen in zufälliger Folge, je Durchgang
    /// eine Frage je Lektion), zuerst Fragen, die in dieser Runde noch nicht vorkamen. Antworten in zufälliger Reihenfolge.
    /// </summary>
    public List<GezogeneFrage> ZieheFragen(int anzahl, IReadOnlySet<string> schonGesehen, IReadOnlyList<Lektion>? quelle = null)
    {
        var zufall = Random.Shared;
        var stapel = (quelle ?? Lektionen)
            .Where(l => l.Pruefung.Count > 0)
            .Select(l => new Queue<Frage>(l.Pruefung.OrderBy(f => schonGesehen.Contains(f.Id)).ThenBy(_ => zufall.Next())))
            .OrderBy(_ => zufall.Next())
            .ToList();
        var gezogen = new List<Frage>();
        while (gezogen.Count < anzahl && stapel.Any(s => s.Count > 0))
        {
            // Bei jedem Durchgang zuerst Lektionen mit noch ungesehenen Fragen.
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

    private (Frage Frage, Lektion Lektion)? Finde(string frageId)
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

    /// <summary>Gehört der Versuch zum Pfad der Administrateure? (an den gezogenen Fragen erkennbar – keine eigene Spalte nötig)</summary>
    private bool IstAdminVersuch(FormationVersuch v)
    {
        var erste = LeseFragen(v).FirstOrDefault()?.Id;
        return erste is not null && AdminLektionen.Any(l => l.Pruefung.Any(f => f.Id == erste));
    }

    /// <summary>Auswahl wird in Anzeige-Positionen gespeichert; zurück auf die Indizes des Katalogs.</summary>
    private static int[] OriginalIndizes(GezogeneFrage g, int[] anzeige) => anzeige.Where(i => i >= 0 && i < g.Reihenfolge.Length).Select(i => g.Reihenfolge[i]).ToArray();

    private void Werte(FormationVersuch v, FormationRunde runde)
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

    /// <summary>Übersicht aller Responsables (aktualisiert vorher ihre Runden).</summary>
    public async Task<List<PoZeile>> GetUebersichtAsync(CancellationToken ct = default)
    {
        await AktualisiereAlleAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var poIds = await PoIdsAsync(ct);
        var nutzer = await db.Users.AsNoTracking().Where(u => poIds.Contains(u.Id)).Select(u => new { u.Id, u.FullName, u.Email }).ToListAsync(ct);
        var teilnehmer = await db.FormationTeilnehmer.AsNoTracking().Where(t => poIds.Contains(t.ProduktOwnerId)).ToDictionaryAsync(t => t.ProduktOwnerId, ct);
        var runden = await db.FormationRunden.AsNoTracking().Include(r => r.Versuche)
            .Where(r => poIds.Contains(r.ProduktOwnerId) && !r.IstFrei).ToListAsync(ct);
        var gelesen = await db.FormationLektionenGelesen.AsNoTracking().Where(g => poIds.Contains(g.ProduktOwnerId))
            .GroupBy(g => g.ProduktOwnerId).Select(g => new { g.Key, Anzahl = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Anzahl, ct);

        return nutzer.Select(u =>
        {
            teilnehmer.TryGetValue(u.Id, out var t);
            var eigene = runden.Where(r => r.ProduktOwnerId == u.Id).OrderByDescending(r => r.GeoeffnetUtc).ToList();
            var gewertet = eigene.Where(r => r.Status is FormationRundenStatus.Bestanden or FormationRundenStatus.NichtBestanden or FormationRundenStatus.Verpasst).ToList();
            var schwach = gewertet.Count >= 2 && gewertet.Take(2).All(r => r.Status != FormationRundenStatus.Bestanden);
            var sehrGut = gewertet.Count >= 3 && gewertet.Take(3).All(r => r.Status == FormationRundenStatus.Bestanden && r.EndNoteProzent >= SehrGutAbProzent)
                          && (t?.IntervallMonate ?? 3) < 12;
            var offen = eigene.FirstOrDefault(r => r.Status == FormationRundenStatus.Offen);
            return new PoZeile(u.Id, u.FullName ?? "", u.Email, t?.IntervallMonate ?? 3, t?.PausiertBisUtc, t?.NaechsteRundeAbUtc,
                offen is null ? null : Kurz(offen), gewertet.Count > 0 ? Kurz(gewertet[0]) : null,
                gelesen.GetValueOrDefault(u.Id), Lektionen.Count, schwach, sehrGut, eigene.Select(Kurz).ToList());
        }).OrderByDescending(z => z.Schwach).ThenBy(z => z.Name).ToList();
    }

    /// <summary>Ergebnis eines Versuchs für den Administrateur (beliebiger Responsable).</summary>
    public async Task<Ergebnis?> GetErgebnisFuerAdminAsync(Guid versuchId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var v = await db.FormationVersuche.AsNoTrackingWithIdentityResolution().Include(x => x.Runde).ThenInclude(r => r.Versuche).FirstOrDefaultAsync(x => x.Id == versuchId, ct);
        return v?.AbgegebenUtc is null ? null : ErgebnisVon(v, v.Runde);
    }

    /// <summary>Name des Responsable zu einem Versuch (Überschrift der Ergebnisseite für den Administrateur).</summary>
    public async Task<string?> GetNameZuVersuchAsync(Guid versuchId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.FormationVersuche.AsNoTracking().Where(v => v.Id == versuchId).Select(v => v.Runde.ProduktOwner.FullName).FirstOrDefaultAsync(ct);
    }

    private async Task<(ApplicationDbContext Db, FormationTeilnehmer T, FormationRunde? Offen)> LadeFuerAdminAsync(Guid poId, CancellationToken ct)
    {
        var db = await dbFactory.CreateDbContextAsync(ct);
        var regel = await RegelAsync(db, ct);
        await AktualisiereAsync(db, poId, regel, ct);
        await db.SaveChangesAsync(ct);
        var t = await db.FormationTeilnehmer.FirstAsync(x => x.ProduktOwnerId == poId, ct);
        var offen = await db.FormationRunden.Include(r => r.Versuche)
            .FirstOrDefaultAsync(r => r.ProduktOwnerId == poId && !r.IstFrei && r.Status == FormationRundenStatus.Offen, ct);
        return (db, t, offen);
    }

    /// <summary>Intervall je Person (1–24 Monate). Ohne offene Runde verschiebt sich die nächste Runde entsprechend.</summary>
    public async Task<string?> SetzeIntervallAsync(Guid poId, int monate, CancellationToken ct = default)
    {
        if (monate is < 1 or > 24) return "L'intervalle doit être entre 1 et 24 mois.";
        var (db, t, offen) = await LadeFuerAdminAsync(poId, ct);
        await using (db)
        {
            if (offen is null)
            {
                var letzte = await db.FormationRunden.Where(r => r.ProduktOwnerId == poId && !r.IstFrei && r.AbgeschlossenUtc != null)
                    .MaxAsync(r => (DateTime?)r.AbgeschlossenUtc, ct);
                t.NaechsteRundeAbUtc = (letzte ?? Jetzt).AddMonths(monate);
            }
            t.IntervallMonate = monate;
            await db.SaveChangesAsync(ct);
        }
        return null;
    }

    /// <summary>
    /// Pause bis zu einem Datum (keine Runde bis dahin; eine offene Runde wird erlassen). <paramref name="bisUtc"/> = null beendet die Pause:
    /// die nächste Runde öffnet sich dann sofort.
    /// </summary>
    public async Task<string?> SetzePauseAsync(Guid poId, DateTime? bisUtc, Guid adminId, CancellationToken ct = default)
    {
        if (bisUtc is { } b && b <= Jetzt) return "Choisissez une date dans le futur.";
        var (db, t, offen) = await LadeFuerAdminAsync(poId, ct);
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
                // Pause beendet = wieder einsteigen: die nächste Runde öffnet sich sofort.
                if (offen is null && t.NaechsteRundeAbUtc > Jetzt) t.NaechsteRundeAbUtc = Jetzt;
                await AktualisiereAsync(db, poId, await RegelAsync(db, ct), ct);
                await db.SaveChangesAsync(ct);
            }
        }
        return null;
    }

    public async Task<string?> VerlaengereFristAsync(Guid poId, DateTime neueFristUtc, CancellationToken ct = default)
    {
        var (db, _, offen) = await LadeFuerAdminAsync(poId, ct);
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

    /// <summary>Die offene Runde erlassen (zählt nicht); die nächste Runde folgt nach dem Intervall.</summary>
    public async Task<string?> ErlasseRundeAsync(Guid poId, Guid adminId, string? grund, CancellationToken ct = default)
    {
        var (db, t, offen) = await LadeFuerAdminAsync(poId, ct);
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

    /// <summary>Sofort eine neue Runde öffnen (z. B. nach schwachen Ergebnissen). Beendet eine Pause.</summary>
    public async Task<string?> OeffneRundeJetztAsync(Guid poId, CancellationToken ct = default)
    {
        var (db, t, offen) = await LadeFuerAdminAsync(poId, ct);
        await using (db)
        {
            if (offen is not null) return "Une session est déjà ouverte.";
            t.PausiertBisUtc = null;
            t.NaechsteRundeAbUtc = Jetzt;
            await AktualisiereAsync(db, poId, await RegelAsync(db, ct), ct);
            await db.SaveChangesAsync(ct);
        }
        return null;
    }

    public sealed record LadenDesPo(Guid SalonId, string Name);
    public sealed record AndererPo(Guid Id, string Name);

    /// <summary>Läden eines Responsable und die übrigen Responsables (für die Neuzuweisung beim Entziehen der Rolle).</summary>
    public async Task<(IReadOnlyList<LadenDesPo> Laeden, IReadOnlyList<AndererPo> Andere)> GetNeuzuweisungAsync(Guid poId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var laeden = await db.ProduktOwnerSalons.AsNoTracking().Where(z => z.ProduktOwnerId == poId && z.GueltigBisUtc == null)
            .OrderBy(z => z.Salon.Name).Select(z => new LadenDesPo(z.SalonId, z.Salon.Name)).ToListAsync(ct);
        var ids = (await PoIdsAsync(ct)).Where(id => id != poId).ToList();
        var andere = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && !u.IsBlocked)
            .OrderBy(u => u.FullName).Select(u => new AndererPo(u.Id, u.FullName)).ToListAsync(ct);
        return (laeden, andere);
    }

    /// <summary>
    /// Entzieht die Rolle « Responsable » und weist die Läden im selben Schritt neu zu (Nutzer 17.11.: keine Lücke bei den Provisionen):
    /// <paramref name="neuzuweisung"/> = Laden → neuer Responsable (null = ohne Responsable). Die neue Zuweisung übernimmt Provisionssätze
    /// und Rechte der alten. Danach: Rolle weg, alle Anmeldungen enden, offene Runde erlassen; bisherige Provisionen bleiben.
    /// Liefert die Läden, die ohne Responsable bleiben.
    /// </summary>
    public async Task<(bool Erfolg, string? Fehler, IReadOnlyList<string> OhneResponsable)> EntzieheRolleAsync(Guid poId, Guid adminId,
        IReadOnlyDictionary<Guid, Guid?>? neuzuweisung = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rolle = await db.Roles.FirstOrDefaultAsync(r => r.Name == AppRollen.ProduktOwner, ct);
        var zuordnung = rolle is null ? null : await db.UserRoles.FirstOrDefaultAsync(ur => ur.UserId == poId && ur.RoleId == rolle.Id, ct);
        if (zuordnung is null) return (false, "Ce compte n'est pas responsable.", []);

        var gueltigeNeue = rolle is null ? [] : (await db.UserRoles.Where(ur => ur.RoleId == rolle.Id && ur.UserId != poId).Select(ur => ur.UserId).ToListAsync(ct)).ToHashSet();
        if (neuzuweisung is not null && neuzuweisung.Values.Any(v => v is { } id && !gueltigeNeue.Contains(id)))
            return (false, "Choisissez un autre responsable (actif) pour chaque établissement.", []);

        var zuweisungen = await db.ProduktOwnerSalons.Include(z => z.Salon).Where(z => z.ProduktOwnerId == poId && z.GueltigBisUtc == null).ToListAsync(ct);
        var ohne = new List<string>();
        foreach (var z in zuweisungen)
        {
            if (neuzuweisung is not null && neuzuweisung.TryGetValue(z.SalonId, out var neuerPo) && neuerPo is { } neuId)
            {
                db.ProduktOwnerSalons.Add(new ProduktOwnerSalon
                {
                    ProduktOwnerId = neuId, SalonId = z.SalonId, KannLadenUmsatzSehen = z.KannLadenUmsatzSehen, KannPlattformUmsatzSehen = z.KannPlattformUmsatzSehen,
                    ProzentProvision = z.ProzentProvision, FestbetragProvision = z.FestbetragProvision, CreatedAtUtc = Jetzt
                });
            }
            else ohne.Add(z.Salon.Name);
        }
        db.ProduktOwnerSalons.RemoveRange(zuweisungen);
        db.UserRoles.Remove(zuordnung);

        var konto = await db.Users.FirstAsync(u => u.Id == poId, ct);
        konto.ActiveSessionId = null;
        konto.UpdatedAtUtc = Jetzt;

        var t = await db.FormationTeilnehmer.FirstOrDefaultAsync(x => x.ProduktOwnerId == poId, ct);
        var offen = await db.FormationRunden.Include(r => r.Versuche)
            .FirstOrDefaultAsync(r => r.ProduktOwnerId == poId && !r.IstFrei && r.Status == FormationRundenStatus.Offen, ct);
        if (offen is not null && t is not null) Erlasse(offen, adminId, "Rôle de responsable retiré", t, null);

        await db.SaveChangesAsync(ct);
        return (true, null, ohne.OrderBy(n => n).ToList());
    }

    // ------------------------------------------------------------------ Benachrichtigungen (Hintergrunddienst)

    public sealed record Meldung(Guid RundeId, Guid PoId, string Art, DateTime FristUtc, int? Note, int Grenze, int Fragen = 0);

    /// <summary>
    /// Offene Benachrichtigungen: « ouverte » (neue Runde), « rappel » (≤ 7 Tage vor der Frist, noch nicht bestanden abgegeben),
    /// « resultat » (Runde « non réussie » oder « manquée » – an das Team). Nach dem Versand <see cref="MarkiereGemeldetAsync"/>.
    /// </summary>
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
                if (r.BenachrichtigtUtc is null) liste.Add(new(r.Id, r.ProduktOwnerId, "ouverte", r.FristUtc, null, r.BestehensGrenzeProzent, r.FragenAnzahl));
                // Nur vor Fristende: nach Ablauf (Runde noch nicht abgeschlossen, z. B. nach einer Betriebspause) keine
                // Erinnerung « se termine le … » mit einem Datum in der Vergangenheit – dann folgt « manquée ».
                else if (r.ErinnerungUtc is null && r.FristUtc <= grenze && r.FristUtc > Jetzt && r.Versuche.Count(v => v.AbgegebenUtc != null) < FormationRunde.MaxVersuche)
                {
                    // Schon bestanden abgegeben → keine Erinnerung nötig (zählt zur Frist ohnehin).
                    var note = r.Versuche.Where(v => v.AbgegebenUtc != null).MaxBy(v => v.Nummer)?.Prozent;
                    if (note is null || note < r.BestehensGrenzeProzent)
                        liste.Add(new(r.Id, r.ProduktOwnerId, "rappel", r.FristUtc, note, r.BestehensGrenzeProzent));
                }
            }
            else liste.Add(new(r.Id, r.ProduktOwnerId, "resultat", r.FristUtc, r.EndNoteProzent, r.BestehensGrenzeProzent));
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
