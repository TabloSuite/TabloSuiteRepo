using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Noten;
using Skoulou.Core.Organisation;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Services.Noten;

/// <summary>
/// Erweiterungspunkt des Jahreswechsels für andere Bereiche (z. B. E6 Finanzen: offene Posten ins neue Jahr übertragen).
/// Läuft in derselben Transaktion nach dem Anlegen der neuen Einschreibungen und vor dem Abschluss des alten Jahres;
/// muss idempotent sein (ein abgebrochener Wechsel wird vollständig wiederholt). Registrierung: <c>services.AddScoped&lt;IJahreswechselTeilnehmer, …&gt;()</c>.
/// </summary>
public interface IJahreswechselTeilnehmer
{
    Task NachWechselAsync(SkoulouDbContext db, JahreswechselKontext kontext, CancellationToken ct);
}

/// <summary>Kontext für Teilnehmer: Schule, Akteur, altes/neues Jahr, neue Einschreibung je Schüler (nur in diesem Lauf angelegte).</summary>
public sealed record JahreswechselKontext(Guid SchuleId, Guid AkteurId, Guid AltesJahrId, Guid NeuesJahrId, IReadOnlyDictionary<Guid, Guid> NeueEinschreibungen);

/// <summary>Plan des Assistenten (änderbar in der Oberfläche).</summary>
public sealed class PassagePlan
{
    public string? Bezeichnung { get; set; }
    public DateOnly? Beginn { get; set; }
    public DateOnly? Ende { get; set; }
    public bool LehrauftraegeKopieren { get; set; } = true;
    public bool KomponentenKopieren { get; set; } = true;

    /// <summary>Alte Klasse → Name ihrer Kopie im neuen Jahr (gleiche Stufe; nimmt die Redoublants auf).</summary>
    public Dictionary<Guid, string> Namen { get; set; } = [];

    /// <summary>Alte Klasse → alte Klasse der Folgestufe, deren Kopie die « Admis » aufnimmt (null = verlassen die Schule).</summary>
    public Dictionary<Guid, Guid?> ZielAdmis { get; set; } = [];
}

public sealed record PassageSchueler(Guid EinschreibungId, string Name, JahresEntscheidungsArt Entscheidung, string? Ziel);

public sealed record PassageKlasse(Guid KlasseId, string Klasse, string? Stufe, string NeuerName, Guid? ZielAdmis, string? ZielAdmisName,
    IReadOnlyList<AuswahlEintrag> ZielOptionen, IReadOnlyList<PassageSchueler> Schueler)
{
    public int Anzahl(JahresEntscheidungsArt e) => Schueler.Count(s => s.Entscheidung == e);
    public int Sortants => Schueler.Count(s => s.Ziel is null && s.Entscheidung != JahresEntscheidungsArt.Offen);
}

public sealed record PassageVorschau(Guid AltesJahrId, string AltesJahr, SchuljahrStatus AltStatus, Guid? NeuesJahrId, string NeuesJahr,
    DateOnly NeuBeginn, DateOnly NeuEnde, int NeuePerioden, IReadOnlyList<PassageKlasse> Klassen, IReadOnlyList<string> Warnungen, bool BereitsAusgefuehrt)
{
    public int Offen => Klassen.Sum(k => k.Anzahl(JahresEntscheidungsArt.Offen));
    public int Eingeschrieben => Klassen.Sum(k => k.Schueler.Count(s => s.Ziel is not null));
    public int Sortants => Klassen.Sum(k => k.Sortants);
    public bool Bereit => !BereitsAusgefuehrt && Offen == 0 && Klassen.Count > 0;
}

public sealed record PassageErgebnis(Guid NeuesJahrId, string NeuesJahr, int NeueKlassen, int Einschreibungen, int Sortants);

/// <summary>
/// Assistent « Passage à l'année suivante » (Bauplan §6, §11.17; Fachkonzept 04 §4.7/4.8, §7.1.2): Vorschlag je Klasse aus den
/// Jahresentscheidungen (Admis → Folgestufe, Redouble → gleiche Stufe, Exclu/Orientation/Partie → Austritt), neues Jahr mit
/// kopierter Periodenstruktur, neue Klassen (Namen änderbar), Fächer/Koeffizienten und optional Lehraufträge kopieren,
/// Einschreibungen in einer Transaktion, altes Jahr abschließen, Audit. Idempotent: Vorhandenes wird erkannt und nicht doppelt angelegt.
/// </summary>
public sealed class JahreswechselService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit, TimeProvider zeit,
    IEnumerable<IJahreswechselTeilnehmer> teilnehmer)
{
    private sealed record AltSchueler(Guid EinschreibungId, Guid SchuelerId, Guid KlasseId, string Name, JahresEntscheidungsArt Entscheidung, Guid? ZielStufeId);

    private sealed record AltKlasse(Guid Id, string Name, Guid? StufeId, string? Stufe, Guid? Nachfolger, int Reihenfolge);

    /// <summary>Vorschlag: nächstes Jahr (vorhandenes in Vorbereitung oder neu), gleiche Klassennamen, Admis in die Klasse der Folgestufe mit gleichem Suffix.</summary>
    public async Task<PassagePlan> PlanAsync(Guid akteurId, Guid altesJahrId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var alt = await OrgAbfragen.SchuljahrAsync(db, a, altesJahrId, ct);
        var neu = await FolgejahrAsync(db, alt, ct);
        var klassen = await KlassenAsync(db, alt.Id, ct);
        var plan = new PassagePlan
        {
            Bezeichnung = neu?.Bezeichnung ?? $"{alt.Beginn.Year + 1}-{alt.Ende.Year + 1}",
            Beginn = neu?.Beginn ?? alt.Beginn.AddYears(1),
            Ende = neu?.Ende ?? alt.Ende.AddYears(1)
        };
        foreach (var k in klassen)
        {
            plan.Namen[k.Id] = k.Name;
            plan.ZielAdmis[k.Id] = VorschlagZiel(k, klassen);
        }
        return plan;
    }

    private static Guid? VorschlagZiel(AltKlasse k, List<AltKlasse> klassen)
    {
        if (k.Nachfolger is not { } folge) return null;
        var kandidaten = klassen.Where(x => x.StufeId == folge).ToList();
        var suffix = k.Stufe is { } s && k.Name.StartsWith(s, StringComparison.OrdinalIgnoreCase) ? k.Name[s.Length..] : null;
        return (suffix is null ? null : kandidaten.FirstOrDefault(x => x.Stufe is { } fs && string.Equals(x.Name, fs + suffix, StringComparison.OrdinalIgnoreCase)))?.Id
               ?? kandidaten.FirstOrDefault()?.Id;
    }

    public async Task<PassageVorschau> VorschauAsync(Guid akteurId, Guid altesJahrId, PassagePlan plan, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var alt = await OrgAbfragen.SchuljahrAsync(db, a, altesJahrId, ct);
        return await RechneAsync(db, alt, plan, ct);
    }

    private async Task<PassageVorschau> RechneAsync(SkoulouDbContext db, Schuljahr alt, PassagePlan plan, CancellationToken ct)
    {
        var neu = await FolgejahrAsync(db, alt, ct);
        var klassen = await KlassenAsync(db, alt.Id, ct);
        var schueler = await SchuelerAsync(db, alt.Id, await LetztesPeriodenEndeAsync(db, alt, ct), ct);
        var warnungen = new List<string>();
        var perioden = await db.Perioden.AsNoTracking().Where(p => p.SchuljahrId == alt.Id).ToListAsync(ct);
        var letzte = perioden.Where(p => p.ZaehltFuerJahr).OrderByDescending(p => p.Ende).FirstOrDefault();
        if (letzte is not null)
        {
            var veroeffentlicht = await db.KlassenVeroeffentlichungen.Where(v => v.PeriodeId == letzte.Id).Select(v => v.KlasseId).ToListAsync(ct);
            // Klassen einer früheren Session haben für die letzte Periode nie Bulletins: nicht mitzählen.
            var andereSession = await db.Klassen.Where(k => k.SchuljahrId == alt.Id && k.PeriodeId != null && k.PeriodeId != letzte.Id).Select(k => k.Id).ToListAsync(ct);
            var ohne = klassen.Count(k => !veroeffentlicht.Contains(k.Id) && !andereSession.Contains(k.Id));
            if (ohne > 0) warnungen.Add($"{ohne} classe(s) n'ont pas encore de bulletins publiés pour « {letzte.Titel} ».");
        }
        var bereits = alt.Status == SchuljahrStatus.Abgeschlossen && schueler.Count == 0;
        var ergebnis = klassen.Select(k =>
        {
            var name = plan.Namen.GetValueOrDefault(k.Id) ?? k.Name;
            var ziel = plan.ZielAdmis.TryGetValue(k.Id, out var z) ? z : VorschlagZiel(k, klassen);
            var optionen = klassen.Where(x => x.StufeId is not null && x.StufeId == k.Nachfolger)
                .Select(x => new AuswahlEintrag(x.Id, plan.Namen.GetValueOrDefault(x.Id) ?? x.Name, x.Stufe)).ToList();
            var liste = schueler.Where(s => s.KlasseId == k.Id)
                .Select(s => new PassageSchueler(s.EinschreibungId, s.Name, s.Entscheidung, ZielName(s, k, ziel, klassen, plan))).ToList();
            return new PassageKlasse(k.Id, k.Name, k.Stufe, name, ziel, ziel is { } zid ? plan.Namen.GetValueOrDefault(zid) ?? klassen.FirstOrDefault(x => x.Id == zid)?.Name : null,
                optionen, liste);
        }).ToList();
        var anzahlPerioden = neu is null ? perioden.Count : await db.Perioden.CountAsync(p => p.SchuljahrId == neu.Id, ct);
        return new PassageVorschau(alt.Id, alt.Bezeichnung, alt.Status, neu?.Id, neu?.Bezeichnung ?? plan.Bezeichnung ?? string.Empty,
            neu?.Beginn ?? plan.Beginn ?? alt.Beginn.AddYears(1), neu?.Ende ?? plan.Ende ?? alt.Ende.AddYears(1), anzahlPerioden, ergebnis, warnungen, bereits);
    }

    /// <summary>Name der Zielklasse im neuen Jahr (null = verlässt die Schule bzw. noch offen).</summary>
    private static string? ZielName(AltSchueler s, AltKlasse k, Guid? zielAdmis, List<AltKlasse> klassen, PassagePlan plan)
    {
        string NameVon(Guid id) => plan.Namen.GetValueOrDefault(id) ?? klassen.First(x => x.Id == id).Name;
        switch (s.Entscheidung)
        {
            case JahresEntscheidungsArt.Redouble:
                return NameVon(k.Id);
            case JahresEntscheidungsArt.Admis:
                if (s.ZielStufeId is { } zs && zs != k.Nachfolger)
                    return klassen.FirstOrDefault(x => x.StufeId == zs) is { } andere ? NameVon(andere.Id) : null;
                return zielAdmis is { } z ? NameVon(z) : null;
            case JahresEntscheidungsArt.Orientation:
                return s.ZielStufeId is { } os && klassen.FirstOrDefault(x => x.StufeId == os) is { } ok ? NameVon(ok.Id) : null;
            default:
                return null;
        }
    }

    public async Task<PassageErgebnis> AusfuehrenAsync(Guid akteurId, Guid altesJahrId, PassagePlan plan, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, ct: ct);
        return await dbFactory.TransaktionAsync(async db =>
        {
            var alt = await OrgAbfragen.SchuljahrAsync(db, a, altesJahrId, ct);
            if (alt.Status == SchuljahrStatus.Vorbereitung) throw new FachFehler("Cette année n'a pas encore démarré : le passage se fait à la fin d'une année en cours.");
            var vorschau = await RechneAsync(db, alt, plan, ct);
            if (vorschau.BereitsAusgefuehrt) throw new FachFehler($"Le passage de l'année {alt.Bezeichnung} a déjà été effectué.");
            if (vorschau.Offen > 0)
                throw new FachFehler($"{vorschau.Offen} élève(s) n'ont pas encore de décision de fin d'année : renseignez-la (conseil de classe) ou appliquez « Admis » aux élèves sans décision.");
            var namen = vorschau.Klassen.Select(k => (k.KlasseId, Name: Pruefe(k.NeuerName))).ToList();
            if (namen.GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } doppelt)
                throw new FachFehler($"Le nom « {doppelt.Key} » est prévu pour plusieurs classes.");

            var (neu, neuePerioden) = await NeuesJahrAsync(db, a, alt, plan, ct);
            var altKlassen = await db.Klassen.Where(k => k.SchuljahrId == alt.Id).ToDictionaryAsync(k => k.Id, ct);
            var neueKlassen = await db.Klassen.Where(k => k.SchuljahrId == neu.Id).ToListAsync(ct);
            var zuordnung = new Dictionary<string, Klasse>(StringComparer.OrdinalIgnoreCase);
            // Session-Klassen (Sprachschulen) gehören im neuen Jahr zur Periode mit derselben Nr.
            var altePeriodenNr = await db.Perioden.Where(p => p.SchuljahrId == alt.Id).ToDictionaryAsync(p => p.Id, p => p.Nr, ct);
            Guid? NeueSession(Guid? altePeriode) => altePeriode is { } pid && altePeriodenNr.TryGetValue(pid, out var nr)
                ? neuePerioden.FirstOrDefault(p => p.Nr == nr)?.Id : null;
            var angelegt = 0;
            foreach (var (altId, name) in namen)
            {
                var quelle = altKlassen[altId];
                var ziel = neueKlassen.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));
                if (ziel is null)
                {
                    ziel = new Klasse
                    {
                        SchuleId = a.SchuleId, SchuljahrId = neu.Id, StufeId = quelle.StufeId, Name = name, Kapazitaet = quelle.Kapazitaet,
                        KlassenlehrerId = quelle.KlassenlehrerId, Raum = quelle.Raum, PeriodeId = NeueSession(quelle.PeriodeId)
                    };
                    db.Klassen.Add(ziel);
                    neueKlassen.Add(ziel);
                    angelegt++;
                }
                zuordnung[name] = ziel;
                await FaecherKopierenAsync(db, a.SchuleId, quelle.Id, ziel.Id, plan.LehrauftraegeKopieren, ct);
            }
            await db.SaveChangesAsync(ct);

            // Session-Klassen enden mit ihrer Session: wer dort ohne Wechsel/Austritt blieb, wird zum Session-Ende
            // abgeschlossen (steht nicht in der Liste, wird nicht übernommen, bleibt im alten Jahr nicht « active »).
            await SessionAbschluss.AbgelaufeneSchliessenAsync(db, db.Einschreibungen.Where(e => e.SchuljahrId == alt.Id),
                await LetztesPeriodenEndeAsync(db, alt, ct), ct);

            // Alte Einschreibungen schließen (vor dem Anlegen der neuen: höchstens eine aktive je Schüler).
            var jetzt = zeit.GetUtcNow().UtcDateTime;
            var plaene = vorschau.Klassen.SelectMany(k => k.Schueler).ToList();
            var ids = plaene.Select(p => p.EinschreibungId).ToList();
            var alte = await db.Einschreibungen.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
            var entscheidungen = await db.Jahresentscheidungen.Where(j => ids.Contains(j.EinschreibungId)).ToDictionaryAsync(j => j.EinschreibungId, ct);
            foreach (var p in plaene)
            {
                var e = alte[p.EinschreibungId];
                e.Bis ??= alt.Ende;
                if (p.Ziel is null)
                {
                    e.Status = EinschreibungsStatus.Partie;
                    e.Austrittsgrund ??= $"Fin d'année {alt.Bezeichnung} : {NotenTexte.Entscheidung(p.Entscheidung).ToLowerInvariant()}.";
                }
                else e.Status = EinschreibungsStatus.Terminee;
                entscheidungen[p.EinschreibungId].AusgefuehrtAmUtc = jetzt;
            }
            await db.SaveChangesAsync(ct);

            var schuelerIds = alte.Values.Select(e => e.SchuelerId).ToList();
            var schonImNeuenJahr = await db.Einschreibungen.Where(e => e.SchuljahrId == neu.Id && e.StorniertAmUtc == null && schuelerIds.Contains(e.SchuelerId))
                .Select(e => e.SchuelerId).ToListAsync(ct);
            var neueEinschreibungen = new Dictionary<Guid, Guid>();
            foreach (var p in plaene.Where(p => p.Ziel is not null))
            {
                var e = alte[p.EinschreibungId];
                if (schonImNeuenJahr.Contains(e.SchuelerId)) continue;
                var n = new Einschreibung
                {
                    SchuleId = a.SchuleId, SchuelerId = e.SchuelerId, SchuljahrId = neu.Id, KlasseId = zuordnung[p.Ziel!].Id, Von = neu.Beginn,
                    Status = EinschreibungsStatus.Active, IstWiederholer = p.Entscheidung == JahresEntscheidungsArt.Redouble
                };
                db.Einschreibungen.Add(n);
                neueEinschreibungen[e.SchuelerId] = n.Id;
            }
            await db.SaveChangesAsync(ct);

            if (plan.KomponentenKopieren) await KomponentenKopierenAsync(db, a.SchuleId, alt.Id, neuePerioden, ct);
            var kontext = new JahreswechselKontext(a.SchuleId, a.Id, alt.Id, neu.Id, neueEinschreibungen);
            foreach (var t in teilnehmer) await t.NachWechselAsync(db, kontext, ct);

            foreach (var p in await db.Perioden.Where(p => p.SchuljahrId == alt.Id).ToListAsync(ct)) p.Status = PeriodenStatus.Geschlossen;
            if (alt.Status != SchuljahrStatus.Abgeschlossen)
            {
                alt.Status = SchuljahrStatus.Abgeschlossen;
                alt.AbgeschlossenAmUtc = jetzt;
            }
            var sortants = plaene.Count(p => p.Ziel is null);
            audit.Erfasse(db, a.Id, a.SchuleId, AuditAktionen.Jahreswechsel, nameof(Schuljahr), alt.Id,
                $"Passage {alt.Bezeichnung} → {neu.Bezeichnung} : {angelegt} classe(s) créée(s), {neueEinschreibungen.Count} inscription(s), {sortants} départ(s).");
            await db.SaveChangesAsync(ct);
            return new PassageErgebnis(neu.Id, neu.Bezeichnung, angelegt, neueEinschreibungen.Count, sortants);
        }, ct);
    }

    /// <summary>« Admis » (bzw. die gewählte Entscheidung) für alle Schüler einer Klasse ohne Entscheidung. Anzahl gesetzt.</summary>
    public async Task<int> OffeneEntscheidenAsync(Guid akteurId, Guid klasseId, JahresEntscheidungsArt entscheidung, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, ct: ct);
        if (entscheidung == JahresEntscheidungsArt.Offen) throw new FachFehler("Choisissez une décision.");
        return await dbFactory.TransaktionAsync(async db =>
        {
            var klasse = await OrgAbfragen.KlasseAsync(db, a, klasseId, ct);
            OrgAbfragen.PruefeOffen(await db.Schuljahre.FirstAsync(j => j.Id == klasse.SchuljahrId, ct));
            var einschreibungen = await SchulAbfragen.AktiveEinschreibungen(db).Where(e => e.KlasseId == klasse.Id).Select(e => e.Id).ToListAsync(ct);
            var vorhandene = await db.Jahresentscheidungen.Where(j => einschreibungen.Contains(j.EinschreibungId)).ToListAsync(ct);
            var n = 0;
            foreach (var id in einschreibungen)
            {
                var j = vorhandene.FirstOrDefault(x => x.EinschreibungId == id);
                if (j is null)
                {
                    db.Jahresentscheidungen.Add(new Jahresentscheidung { SchuleId = a.SchuleId, EinschreibungId = id, Entscheidung = entscheidung });
                    n++;
                }
                else if (j.Entscheidung == JahresEntscheidungsArt.Offen && j.AusgefuehrtAmUtc is null)
                {
                    j.Entscheidung = entscheidung;
                    n++;
                }
            }
            await db.SaveChangesAsync(ct);
            return n;
        }, ct);
    }

    private static string Pruefe(string? name)
    {
        var n = NotenFormat.Text(name, 60, "Le nom de la classe") ?? throw new FachFehler("Chaque nouvelle classe a besoin d'un nom.");
        return n;
    }

    private static async Task<Schuljahr?> FolgejahrAsync(SkoulouDbContext db, Schuljahr alt, CancellationToken ct)
        => await db.Schuljahre.Where(j => j.SchuleId == alt.SchuleId && j.Beginn > alt.Ende && j.Status != SchuljahrStatus.Abgeschlossen)
            .OrderBy(j => j.Beginn).FirstOrDefaultAsync(ct);

    private static async Task<List<AltKlasse>> KlassenAsync(SkoulouDbContext db, Guid jahrId, CancellationToken ct)
    {
        var aktive = SchulAbfragen.AktiveEinschreibungen(db);
        var liste = await (from k in db.Klassen.AsNoTracking()
                           join s in db.Stufen on k.StufeId equals s.Id into st
                           from s in st.DefaultIfEmpty()
                           where k.SchuljahrId == jahrId && (k.Aktiv || aktive.Any(e => e.KlasseId == k.Id))
                           select new AltKlasse(k.Id, k.Name, k.StufeId, s == null ? null : s.Name, s == null ? null : s.NachfolgerId, s == null ? 999 : s.Reihenfolge))
            .ToListAsync(ct);
        return liste.OrderBy(k => k.Reihenfolge).ThenBy(k => k.Name).ToList();
    }

    /// <summary>Ende der letzten Periode des Jahres (ohne Perioden: Jahresende) – Session-Klassen früherer Perioden sind dann vorbei.</summary>
    private static async Task<DateOnly> LetztesPeriodenEndeAsync(SkoulouDbContext db, Schuljahr jahr, CancellationToken ct)
        => await db.Perioden.Where(p => p.SchuljahrId == jahr.Id).MaxAsync(p => (DateOnly?)p.Ende, ct) ?? jahr.Ende;

    /// <summary>
    /// Schüler des Jahresendes: laufende Einschreibungen am <paramref name="stichtag"/>. Wer in einer Session-Klasse ohne
    /// Wechsel/Austritt blieb, deren Session vorbei ist, gehört nicht dazu (braucht keine Entscheidung, wird nicht übernommen).
    /// </summary>
    private static async Task<List<AltSchueler>> SchuelerAsync(SkoulouDbContext db, Guid jahrId, DateOnly stichtag, CancellationToken ct)
    {
        var liste = await (from e in SchulAbfragen.LaufendeEinschreibungen(db, stichtag).AsNoTracking()
                           join u in db.Users on e.SchuelerId equals u.Id
                           join j in db.Jahresentscheidungen on e.Id equals j.EinschreibungId into je
                           from j in je.DefaultIfEmpty()
                           where e.SchuljahrId == jahrId
                           select new
                           {
                               e.Id, e.SchuelerId, e.KlasseId, u.Vorname, u.Nachname,
                               Entscheidung = j == null ? JahresEntscheidungsArt.Offen : j.Entscheidung, Ziel = j == null ? null : j.ZielStufeId
                           }).ToListAsync(ct);
        return liste.Select(x => new AltSchueler(x.Id, x.SchuelerId, x.KlasseId, NotenFormat.Name(x.Vorname, x.Nachname), x.Entscheidung, x.Ziel))
            .OrderBy(s => s.Name, StringComparer.Create(SkoulouKultur.Kultur, true)).ToList();
    }

    /// <summary>Vorhandenes Folgejahr (in Vorbereitung/laufend) oder neues mit derselben Periodenstruktur (Daten um die Jahre verschoben).</summary>
    private static async Task<(Schuljahr Jahr, List<Periode> NeuePerioden)> NeuesJahrAsync(SkoulouDbContext db, SchulAkteur a, Schuljahr alt, PassagePlan plan, CancellationToken ct)
    {
        if (await FolgejahrAsync(db, alt, ct) is { } vorhanden)
        {
            var perioden = await db.Perioden.Where(p => p.SchuljahrId == vorhanden.Id).ToListAsync(ct);
            return (await db.Schuljahre.FirstAsync(j => j.Id == vorhanden.Id, ct), perioden);
        }
        var bezeichnung = NotenFormat.Text(plan.Bezeichnung, 80, "Le nom de l'année") ?? throw new FachFehler("Indiquez le nom de la nouvelle année.");
        if (plan.Beginn is not { } beginn || plan.Ende is not { } ende || ende <= beginn || beginn <= alt.Ende)
            throw new FachFehler("Les dates de la nouvelle année sont invalides (elle commence après la fin de l'année actuelle).");
        var andere = await db.Schuljahre.AsNoTracking().Where(j => j.SchuleId == a.SchuleId).ToListAsync(ct);
        if (andere.Any(j => string.Equals(j.Bezeichnung, bezeichnung, StringComparison.OrdinalIgnoreCase)))
            throw new FachFehler($"L'année « {bezeichnung} » existe déjà.");
        if (andere.Any(j => new Zeitraum(j.Beginn, j.Ende).Ueberschneidet(new Zeitraum(beginn, ende))))
            throw new FachFehler("Ces dates chevauchent une autre année scolaire.");
        var neu = new Schuljahr
        {
            SchuleId = a.SchuleId, Bezeichnung = bezeichnung, Beginn = beginn, Ende = ende, Status = SchuljahrStatus.Vorbereitung, PeriodenArt = alt.PeriodenArt
        };
        db.Schuljahre.Add(neu);
        var jahre = beginn.Year - alt.Beginn.Year;
        var neue = new List<Periode>();
        foreach (var p in await db.Perioden.AsNoTracking().Where(p => p.SchuljahrId == alt.Id).OrderBy(p => p.Nr).ToListAsync(ct))
        {
            var von = Klemme(p.Beginn.AddYears(jahre), beginn, ende);
            var bis = Klemme(p.Ende.AddYears(jahre), von, ende);
            var np = new Periode
            {
                SchuleId = a.SchuleId, SchuljahrId = neu.Id, Nr = p.Nr, Titel = p.Titel, Beginn = von, Ende = bis,
                Jahresgewicht = p.Jahresgewicht, ZaehltFuerJahr = p.ZaehltFuerJahr
            };
            db.Perioden.Add(np);
            neue.Add(np);
        }
        await db.SaveChangesAsync(ct);
        return (neu, neue);
    }

    private static DateOnly Klemme(DateOnly d, DateOnly min, DateOnly max) => d < min ? min : d > max ? max : d;

    private static async Task FaecherKopierenAsync(SkoulouDbContext db, Guid schuleId, Guid quelleId, Guid zielId, bool lehrauftraege, CancellationToken ct)
    {
        var quelle = await db.Klassenfaecher.AsNoTracking().Where(kf => kf.KlasseId == quelleId).ToListAsync(ct);
        var ziel = await db.Klassenfaecher.Where(kf => kf.KlasseId == zielId).ToListAsync(ct);
        ziel.AddRange(db.Klassenfaecher.Local.Where(kf => kf.KlasseId == zielId && !ziel.Contains(kf)));
        var aktiveFaecher = await db.Faecher.Where(f => f.SchuleId == schuleId && f.Aktiv).Select(f => f.Id).ToListAsync(ct);
        var aktiveLehrer = await db.Users.Where(u => u.SchuleId == schuleId && u.Status == KontoStatus.Actif && OrgRollen.Lehrkraefte.Contains(u.Rolle))
            .Select(u => u.Id).ToListAsync(ct);
        foreach (var q in quelle.Where(q => aktiveFaecher.Contains(q.FachId)))
        {
            var kf = ziel.FirstOrDefault(z => z.FachId == q.FachId);
            if (kf is null)
            {
                // Wahlfach-Einstellung wandert mit; die Wahl der Schüler gilt nur für ein Schuljahr und wird neu getroffen.
                kf = new Klassenfach
                {
                    SchuleId = schuleId, KlasseId = zielId, FachId = q.FachId, Koeffizient = q.Koeffizient, Reihenfolge = q.Reihenfolge,
                    IstWahlfach = q.IstWahlfach, WahlfachRegel = q.WahlfachRegel
                };
                db.Klassenfaecher.Add(kf);
                ziel.Add(kf);
            }
            if (!lehrauftraege) continue;
            var vorhandene = await db.Lehrauftraege.Where(l => l.KlassenfachId == kf.Id).Select(l => l.LehrerId).ToListAsync(ct);
            vorhandene.AddRange(db.Lehrauftraege.Local.Where(l => l.KlassenfachId == kf.Id).Select(l => l.LehrerId));
            foreach (var l in await db.Lehrauftraege.AsNoTracking().Where(l => l.KlassenfachId == q.Id).ToListAsync(ct))
                if (aktiveLehrer.Contains(l.LehrerId) && !vorhandene.Contains(l.LehrerId))
                    db.Lehrauftraege.Add(new Lehrauftrag { SchuleId = schuleId, KlassenfachId = kf.Id, LehrerId = l.LehrerId, Wochenstunden = l.Wochenstunden });
        }
    }

    private static async Task KomponentenKopierenAsync(SkoulouDbContext db, Guid schuleId, Guid altesJahrId, List<Periode> neuePerioden, CancellationToken ct)
    {
        var alte = await (from k in db.PeriodenKomponenten.AsNoTracking()
                          join p in db.Perioden on k.PeriodeId equals p.Id
                          where p.SchuljahrId == altesJahrId
                          select new { p.Nr, k.Titel, k.Art, k.Gewicht, k.Bareme, k.Reihenfolge }).ToListAsync(ct);
        foreach (var p in neuePerioden)
        {
            if (await db.PeriodenKomponenten.AnyAsync(k => k.PeriodeId == p.Id, ct)) continue;
            foreach (var k in alte.Where(x => x.Nr == p.Nr))
                db.PeriodenKomponenten.Add(new PeriodenKomponente { SchuleId = schuleId, PeriodeId = p.Id, Titel = k.Titel, Art = k.Art, Gewicht = k.Gewicht, Bareme = k.Bareme, Reihenfolge = k.Reihenfolge });
        }
        await db.SaveChangesAsync(ct);
    }
}
