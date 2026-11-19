using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Organisation;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Organisation;

public sealed record PeriodeZeile(Guid Id, int Nr, string Titel, DateOnly Beginn, DateOnly Ende, decimal Jahresgewicht,
    bool ZaehltFuerJahr, PeriodenStatus Status, bool Benutzt);

public sealed record SchuljahrZeile(Guid Id, string Bezeichnung, DateOnly Beginn, DateOnly Ende, SchuljahrStatus Status,
    PeriodenArt PeriodenArt, long? PlattformMonatspreis, DateTime? AbgeschlossenAmUtc, string? WiedereroeffnetGrund,
    int AnzahlKlassen, int AnzahlEinschreibungen, IReadOnlyList<PeriodeZeile> Perioden)
{
    public Zeitraum Zeitraum => new(Beginn, Ende);
}

public sealed record SchuljahrEingabe(string? Bezeichnung, DateOnly? Beginn, DateOnly? Ende, PeriodenArt PeriodenArt, int AnzahlPerioden);

public sealed record PeriodeEingabe(string? Titel, DateOnly? Beginn, DateOnly? Ende, decimal Jahresgewicht, bool ZaehltFuerJahr);

/// <summary>
/// Schuljahre und Perioden (Bauplan §4.2): eindeutige Bezeichnung, keine Überlappung, höchstens ein
/// laufendes Jahr, Preis je Schüler und Monat beim Start eingefroren, Abschluss/Wiedereröffnung mit Audit.
/// </summary>
public sealed class SchuljahrService(IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit, TimeProvider zeit)
{
    public async Task<IReadOnlyList<SchuljahrZeile>> JahreAsync(Guid akteurId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var jahre = await db.Schuljahre.AsNoTracking().Where(j => j.SchuleId == a.SchuleId)
            .OrderByDescending(j => j.Beginn)
            .Select(j => new
            {
                j.Id, j.Bezeichnung, j.Beginn, j.Ende, j.Status, j.PeriodenArt, j.PlattformMonatspreis, j.AbgeschlossenAmUtc, j.WiedereroeffnetGrund,
                Klassen = db.Klassen.Count(k => k.SchuljahrId == j.Id && k.Aktiv),
                Einschreibungen = db.Einschreibungen.Count(e => e.SchuljahrId == j.Id && e.StorniertAmUtc == null && e.Status == EinschreibungsStatus.Active)
            }).ToListAsync(ct);
        var ids = jahre.Select(j => j.Id).ToList();
        var perioden = await PeriodenAbfrage(db, ids).ToListAsync(ct);
        return jahre.Select(j => new SchuljahrZeile(j.Id, j.Bezeichnung, j.Beginn, j.Ende, j.Status, j.PeriodenArt, j.PlattformMonatspreis,
                j.AbgeschlossenAmUtc, j.WiedereroeffnetGrund, j.Klassen, j.Einschreibungen,
                perioden.Where(p => p.SchuljahrId == j.Id).OrderBy(p => p.Zeile.Beginn).ThenBy(p => p.Zeile.Nr).Select(p => p.Zeile).ToList()))
            .ToList();
    }

    private sealed record PeriodeMitJahr(Guid SchuljahrId, PeriodeZeile Zeile);

    // Filter vor der Projektion: Eigenschaften eines per Konstruktor projizierten Records sind auf SQL Server nicht übersetzbar.
    private static IQueryable<PeriodeMitJahr> PeriodenAbfrage(SkoulouDbContext db, List<Guid> jahrIds)
        => db.Perioden.AsNoTracking().Where(p => jahrIds.Contains(p.SchuljahrId)).Select(p => new PeriodeMitJahr(p.SchuljahrId,
            new PeriodeZeile(p.Id, p.Nr, p.Titel, p.Beginn, p.Ende, p.Jahresgewicht, p.ZaehltFuerJahr, p.Status,
                db.Bewertungen.Any(b => b.PeriodeId == p.Id) || db.Klassen.Any(k => k.PeriodeId == p.Id) || db.GebuehrenPosten.Any(g => g.PeriodeId == p.Id)
                || db.PeriodenKomponenten.Any(k => k.PeriodeId == p.Id) || db.Plattformrechnungen.Any(r => r.PeriodeId == p.Id)
                || db.KlassenVeroeffentlichungen.Any(v => v.PeriodeId == p.Id) || db.Bulletins.Any(b => b.PeriodeId == p.Id))));

    private static Task<bool> PeriodeBenutztAsync(SkoulouDbContext db, Guid periodeId, CancellationToken ct)
        => db.Perioden.Where(p => p.Id == periodeId).AnyAsync(p =>
            db.Bewertungen.Any(b => b.PeriodeId == p.Id) || db.Klassen.Any(k => k.PeriodeId == p.Id) || db.GebuehrenPosten.Any(g => g.PeriodeId == p.Id)
            || db.PeriodenKomponenten.Any(k => k.PeriodeId == p.Id) || db.Plattformrechnungen.Any(r => r.PeriodeId == p.Id)
            || db.KlassenVeroeffentlichungen.Any(v => v.PeriodeId == p.Id) || db.Bulletins.Any(b => b.PeriodeId == p.Id), ct);

    /// <summary>
    /// Für die eigene Schule gültiger Preis je Schüler und Monat (abweichender Schulpreis, sonst Plattformpreis) – Preis-Hinweis
    /// beim Anlegen eines Jahres (JahrDialog) und auf der Seite « Année scolaire ».
    /// </summary>
    public async Task<long> MonatspreisAsync(Guid akteurId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await GueltigerMonatspreisAsync(db, a.SchuleId, ct);
    }

    private static async Task<long> GueltigerMonatspreisAsync(SkoulouDbContext db, Guid schuleId, CancellationToken ct)
    {
        var schulPreis = await db.Schulen.AsNoTracking().Where(s => s.Id == schuleId).Select(s => s.MonatspreisOverride).FirstOrDefaultAsync(ct);
        return schulPreis ?? await db.PlattformEinstellungen.AsNoTracking().Where(p => p.Id == PlattformEinstellung.EinzigeId)
            .Select(p => (long?)p.MonatspreisProSchueler).FirstOrDefaultAsync(ct) ?? new PlattformEinstellung().MonatspreisProSchueler;
    }

    /// <summary>Vorschlag für ein neues Jahr: nach dem letzten Jahr, sonst 1. September – 30. Juni.</summary>
    public async Task<SchuljahrEingabe> VorschlagAsync(Guid akteurId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, schreiben: false, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var letztes = await db.Schuljahre.AsNoTracking().Where(j => j.SchuleId == a.SchuleId).OrderByDescending(j => j.Ende)
            .Select(j => (DateOnly?)j.Ende).FirstOrDefaultAsync(ct);
        var heute = new SchulZeit(zeit).Heute;
        var startJahr = letztes is { } e ? (e.Month >= 8 ? e.Year + 1 : e.Year) : (heute.Month >= 7 ? heute.Year : heute.Year - 1);
        var beginn = new DateOnly(startJahr, 9, 1);
        if (letztes is { } l && beginn <= l) beginn = l.AddDays(1);
        var ende = new DateOnly(startJahr + 1, 6, 30);
        return new SchuljahrEingabe($"{startJahr}-{startJahr + 1}", beginn, ende, PeriodenArt.Trimestre, 3);
    }

    public async Task<Guid> JahrAnlegenAsync(Guid akteurId, SchuljahrEingabe e, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, ct: ct);
        var (bezeichnung, zeitraum) = PruefeJahr(e.Bezeichnung, e.Beginn, e.Ende);
        var vorschlaege = PeriodenPlan.Erzeuge(e.PeriodenArt, e.AnzahlPerioden, zeitraum.Beginn, zeitraum.Ende);
        return await dbFactory.TransaktionAsync(async db =>
        {
            await PruefeEindeutigAsync(db, a.SchuleId, null, bezeichnung, zeitraum, ct);
            var jahr = new Schuljahr
            {
                SchuleId = a.SchuleId, Bezeichnung = bezeichnung, Beginn = zeitraum.Beginn, Ende = zeitraum.Ende,
                Status = SchuljahrStatus.Vorbereitung, PeriodenArt = e.PeriodenArt
            };
            db.Schuljahre.Add(jahr);
            foreach (var v in vorschlaege)
                db.Perioden.Add(new Periode { SchuleId = a.SchuleId, SchuljahrId = jahr.Id, Nr = v.Nr, Titel = v.Titel, Beginn = v.Beginn, Ende = v.Ende });
            audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.SchuljahrAngelegt, nameof(Schuljahr), jahr.Id,
                $"Année {bezeichnung} créée ({zeitraum}, {PeriodenPlan.Bezeichnung(e.PeriodenArt, vorschlaege.Count).ToLowerInvariant()}).");
            await db.SaveChangesAsync(ct);
            return jahr.Id;
        }, ct);
    }

    public async Task JahrAendernAsync(Guid akteurId, Guid jahrId, string? bezeichnungEingabe, DateOnly? beginn, DateOnly? ende, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, ct: ct);
        var (bezeichnung, zeitraum) = PruefeJahr(bezeichnungEingabe, beginn, ende);
        await dbFactory.TransaktionAsync(async db =>
        {
            var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
            OrgAbfragen.PruefeOffen(jahr);
            await PruefeEindeutigAsync(db, a.SchuleId, jahr.Id, bezeichnung, zeitraum, ct);
            var perioden = await db.Perioden.Where(p => p.SchuljahrId == jahr.Id).ToListAsync(ct);
            if (PeriodenPlan.Pruefe(zeitraum, perioden.Select(p => (p.Titel, new Zeitraum(p.Beginn, p.Ende), p.ZaehltFuerJahr))) is { } fehler)
                throw new FachFehler(fehler + " Ajustez d'abord les périodes.");
            if (await db.Einschreibungen.AnyAsync(x => x.SchuljahrId == jahr.Id && x.StorniertAmUtc == null && (x.Von < zeitraum.Beginn || x.Von > zeitraum.Ende), ct))
                throw new FachFehler("Des inscriptions commencent en dehors de ces dates : ajustez-les d'abord.");
            jahr.Bezeichnung = bezeichnung;
            jahr.Beginn = zeitraum.Beginn;
            jahr.Ende = zeitraum.Ende;
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    /// <summary>Leeres Jahr in Vorbereitung löschen (Fehleingabe).</summary>
    public async Task JahrLoeschenAsync(Guid akteurId, Guid jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, ct: ct);
        await dbFactory.TransaktionAsync(async db =>
        {
            var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
            if (jahr.Status != SchuljahrStatus.Vorbereitung) throw new FachFehler("Seule une année en préparation peut être supprimée.");
            if (await db.Klassen.AnyAsync(k => k.SchuljahrId == jahr.Id, ct) || await db.Einschreibungen.AnyAsync(x => x.SchuljahrId == jahr.Id, ct)
                || await db.GebuehrenPosten.AnyAsync(g => g.SchuljahrId == jahr.Id, ct))
                throw new FachFehler("Cette année contient déjà des classes, des inscriptions ou des frais : elle ne peut plus être supprimée.");
            var perioden = await db.Perioden.Where(p => p.SchuljahrId == jahr.Id).ToListAsync(ct);
            foreach (var p in perioden)
                if (await PeriodeBenutztAsync(db, p.Id, ct)) throw new FachFehler($"La période « {p.Titel} » est déjà utilisée.");
            db.Perioden.RemoveRange(perioden);
            db.Schuljahre.Remove(jahr);
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    /// <summary>Periodenstruktur neu erzeugen (nur in Vorbereitung und solange keine Periode benutzt ist).</summary>
    public async Task StrukturNeuAsync(Guid akteurId, Guid jahrId, PeriodenArt art, int anzahl, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, ct: ct);
        await dbFactory.TransaktionAsync(async db =>
        {
            var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
            if (jahr.Status != SchuljahrStatus.Vorbereitung)
                throw new FachFehler("La structure des périodes ne peut être changée qu'avant le démarrage de l'année. Modifiez plutôt les dates des périodes.");
            var alte = await db.Perioden.Where(p => p.SchuljahrId == jahr.Id).ToListAsync(ct);
            foreach (var p in alte)
                if (await PeriodeBenutztAsync(db, p.Id, ct)) throw new FachFehler($"La période « {p.Titel} » est déjà utilisée : la structure ne peut plus être changée.");
            var neue = PeriodenPlan.Erzeuge(art, anzahl, jahr.Beginn, jahr.Ende);
            db.Perioden.RemoveRange(alte);
            await db.SaveChangesAsync(ct);
            foreach (var v in neue)
                db.Perioden.Add(new Periode { SchuleId = a.SchuleId, SchuljahrId = jahr.Id, Nr = v.Nr, Titel = v.Titel, Beginn = v.Beginn, Ende = v.Ende });
            jahr.PeriodenArt = art;
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task<Guid> PeriodeHinzufuegenAsync(Guid akteurId, Guid jahrId, PeriodeEingabe e, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, ct: ct);
        var (titel, zeitraum, gewicht) = PruefePeriode(e);
        return await dbFactory.TransaktionAsync(async db =>
        {
            var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
            OrgAbfragen.PruefeOffen(jahr);
            var perioden = await db.Perioden.Where(p => p.SchuljahrId == jahr.Id).ToListAsync(ct);
            if (perioden.Count >= PeriodenPlan.MaxPerioden) throw new FachFehler($"Une année compte au plus {PeriodenPlan.MaxPerioden} périodes.");
            if (e.ZaehltFuerJahr && jahr.Status != SchuljahrStatus.Vorbereitung)
                throw new FachFehler("Une période comptant pour l'année ne peut être ajoutée qu'avant le démarrage de l'année (facturation). Vous pouvez ajouter un examen blanc.");
            PruefeTitelFrei(perioden, titel, null);
            var periode = new Periode
            {
                SchuleId = a.SchuleId, SchuljahrId = jahr.Id, Nr = perioden.Count == 0 ? 1 : perioden.Max(p => p.Nr) + 1, Titel = titel,
                Beginn = zeitraum.Beginn, Ende = zeitraum.Ende, Jahresgewicht = e.ZaehltFuerJahr ? gewicht : 0, ZaehltFuerJahr = e.ZaehltFuerJahr
            };
            PruefeStruktur(jahr, [.. perioden, periode]);
            db.Perioden.Add(periode);
            await db.SaveChangesAsync(ct);
            return periode.Id;
        }, ct);
    }

    public async Task PeriodeAendernAsync(Guid akteurId, Guid periodeId, PeriodeEingabe e, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, ct: ct);
        var (titel, zeitraum, gewicht) = PruefePeriode(e);
        await dbFactory.TransaktionAsync(async db =>
        {
            var periode = await db.Perioden.FirstOrDefaultAsync(p => p.Id == periodeId, ct) ?? throw new FachFehler("Cette période est introuvable.");
            a.PruefeSchule(periode.SchuleId);
            var jahr = await db.Schuljahre.FirstAsync(j => j.Id == periode.SchuljahrId, ct);
            OrgAbfragen.PruefeOffen(jahr);
            if (periode.ZaehltFuerJahr != e.ZaehltFuerJahr && jahr.Status != SchuljahrStatus.Vorbereitung)
                throw new FachFehler("Après le démarrage de l'année, une période ne peut plus changer entre « compte pour l'année » et « examen blanc » (facturation).");
            var perioden = await db.Perioden.Where(p => p.SchuljahrId == jahr.Id).ToListAsync(ct);
            PruefeTitelFrei(perioden, titel, periode.Id);
            periode.Titel = titel;
            periode.Beginn = zeitraum.Beginn;
            periode.Ende = zeitraum.Ende;
            periode.ZaehltFuerJahr = e.ZaehltFuerJahr;
            periode.Jahresgewicht = e.ZaehltFuerJahr ? gewicht : 0;
            PruefeStruktur(jahr, perioden);
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task PeriodeLoeschenAsync(Guid akteurId, Guid periodeId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, ct: ct);
        await dbFactory.TransaktionAsync(async db =>
        {
            var periode = await db.Perioden.FirstOrDefaultAsync(p => p.Id == periodeId, ct) ?? throw new FachFehler("Cette période est introuvable.");
            a.PruefeSchule(periode.SchuleId);
            var jahr = await db.Schuljahre.FirstAsync(j => j.Id == periode.SchuljahrId, ct);
            OrgAbfragen.PruefeOffen(jahr);
            if (periode.ZaehltFuerJahr && jahr.Status != SchuljahrStatus.Vorbereitung)
                throw new FachFehler("Après le démarrage de l'année, seules les périodes « examen blanc » peuvent être supprimées.");
            if (await PeriodeBenutztAsync(db, periode.Id, ct)) throw new FachFehler("Cette période est déjà utilisée (évaluations, classes, factures ou bulletins).");
            if (periode.ZaehltFuerJahr && !await db.Perioden.AnyAsync(p => p.SchuljahrId == jahr.Id && p.Id != periode.Id && p.ZaehltFuerJahr, ct))
                throw new FachFehler("L'année doit garder au moins une période qui compte pour l'année.");
            db.Perioden.Remove(periode);
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task PeriodeStatusAsync(Guid akteurId, Guid periodeId, bool geschlossen, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, ct: ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var periode = await db.Perioden.FirstOrDefaultAsync(p => p.Id == periodeId, ct) ?? throw new FachFehler("Cette période est introuvable.");
        a.PruefeSchule(periode.SchuleId);
        var jahr = await db.Schuljahre.FirstAsync(j => j.Id == periode.SchuljahrId, ct);
        OrgAbfragen.PruefeOffen(jahr);
        var neu = geschlossen ? PeriodenStatus.Geschlossen : PeriodenStatus.Offen;
        if (periode.Status == neu) return;
        periode.Status = neu;
        audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.PeriodeStatus, nameof(Periode), periode.Id,
            $"{periode.Titel} ({jahr.Bezeichnung}) {(geschlossen ? "fermée" : "rouverte")}.");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Jahr starten: höchstens ein laufendes Jahr; der Preis je Schüler und Monat wird eingefroren.</summary>
    public async Task JahrStartenAsync(Guid akteurId, Guid jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, ct: ct);
        await dbFactory.TransaktionAsync(async db =>
        {
            var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
            if (jahr.Status != SchuljahrStatus.Vorbereitung) throw new FachFehler("Cette année n'est pas en préparation.");
            var laufend = await db.Schuljahre.Where(j => j.SchuleId == a.SchuleId && j.Status == SchuljahrStatus.Laufend && j.Id != jahr.Id)
                .Select(j => j.Bezeichnung).FirstOrDefaultAsync(ct);
            if (laufend is not null)
                throw new FachFehler($"L'année {laufend} est encore en cours : clôturez-la (passage d'année) avant de démarrer {jahr.Bezeichnung}.");
            if (!await db.Perioden.AnyAsync(p => p.SchuljahrId == jahr.Id && p.ZaehltFuerJahr, ct))
                throw new FachFehler("Ajoutez au moins une période qui compte pour l'année avant de démarrer.");
            // Schulpreis, sonst Plattformpreis (ohne Einstellungszeile der Standard 500 je Schüler und Monat).
            jahr.PlattformMonatspreis = await GueltigerMonatspreisAsync(db, a.SchuleId, ct);
            jahr.Status = SchuljahrStatus.Laufend;
            audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.SchuljahrGestartet, nameof(Schuljahr), jahr.Id,
                $"Année {jahr.Bezeichnung} démarrée ; prix par élève et par mois figé à {SkoulouKultur.Fcfa(jahr.PlattformMonatspreis.Value)}.");
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    /// <summary>Jahr abschließen (Direktor und Koordinator, Bauplan §11.3): alle Perioden werden geschlossen.</summary>
    public async Task JahrAbschliessenAsync(Guid akteurId, Guid jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, ct: ct);
        await dbFactory.TransaktionAsync(async db =>
        {
            var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
            if (jahr.Status != SchuljahrStatus.Laufend) throw new FachFehler("Seule l'année en cours peut être clôturée.");
            foreach (var p in await db.Perioden.Where(p => p.SchuljahrId == jahr.Id).ToListAsync(ct)) p.Status = PeriodenStatus.Geschlossen;
            jahr.Status = SchuljahrStatus.Abgeschlossen;
            jahr.AbgeschlossenAmUtc = zeit.GetUtcNow().UtcDateTime;
            audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.SchuljahrAbgeschlossen, nameof(Schuljahr), jahr.Id, $"Année {jahr.Bezeichnung} clôturée.");
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    /// <summary>Wiedereröffnen nur durch den Direktor, mit Grund, solange kein anderes Jahr läuft (§11.17).</summary>
    public async Task JahrWiedereroeffnenAsync(Guid akteurId, Guid jahrId, string? grund, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.SchuljahrVerwalten, ct: ct);
        if (a.Rolle != Rollen.Direktor) throw new KeinZugriffFehler("Seul le directeur peut rouvrir une année clôturée.");
        var text = Eingabe.Pflicht(grund, 500, "le motif de la réouverture", "grund");
        await dbFactory.TransaktionAsync(async db =>
        {
            var jahr = await OrgAbfragen.SchuljahrAsync(db, a, jahrId, ct);
            if (jahr.Status != SchuljahrStatus.Abgeschlossen) throw new FachFehler("Cette année n'est pas clôturée.");
            var laufend = await db.Schuljahre.Where(j => j.SchuleId == a.SchuleId && j.Status == SchuljahrStatus.Laufend)
                .Select(j => j.Bezeichnung).FirstOrDefaultAsync(ct);
            if (laufend is not null) throw new FachFehler($"L'année {laufend} est en cours : une année clôturée ne peut plus être rouverte.");
            jahr.Status = SchuljahrStatus.Laufend;
            jahr.AbgeschlossenAmUtc = null;
            jahr.WiedereroeffnetGrund = text;
            audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.SchuljahrWiedereroeffnet, nameof(Schuljahr), jahr.Id, $"Année {jahr.Bezeichnung} rouverte : {text}");
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    private static (string Bezeichnung, Zeitraum Zeitraum) PruefeJahr(string? bezeichnung, DateOnly? beginn, DateOnly? ende)
    {
        var b = Eingabe.Pflicht(bezeichnung, 80, "le nom de l'année (ex. 2026-2027)", "bezeichnung");
        if (beginn is null) throw new FachFehler("Indiquez la date de début.", "beginn");
        if (ende is null) throw new FachFehler("Indiquez la date de fin.", "ende");
        if (ende <= beginn) throw new FachFehler("La date de fin doit être postérieure à la date de début.", "ende");
        if (ende.Value.DayNumber - beginn.Value.DayNumber > 400) throw new FachFehler("Une année scolaire dure au plus 13 mois.", "ende");
        return (b, new Zeitraum(beginn.Value, ende.Value));
    }

    private static async Task PruefeEindeutigAsync(SkoulouDbContext db, Guid schuleId, Guid? ausser, string bezeichnung, Zeitraum z, CancellationToken ct)
    {
        var andere = await db.Schuljahre.AsNoTracking().Where(j => j.SchuleId == schuleId && j.Id != ausser)
            .Select(j => new { j.Bezeichnung, j.Beginn, j.Ende }).ToListAsync(ct);
        if (andere.Any(j => string.Equals(j.Bezeichnung, bezeichnung, StringComparison.OrdinalIgnoreCase)))
            throw new FachFehler($"L'année « {bezeichnung} » existe déjà.", "bezeichnung");
        if (andere.FirstOrDefault(j => new Zeitraum(j.Beginn, j.Ende).Ueberschneidet(z)) is { } konflikt)
            throw new FachFehler($"Ces dates chevauchent l'année {konflikt.Bezeichnung}.", "beginn");
    }

    private static (string Titel, Zeitraum Zeitraum, decimal Gewicht) PruefePeriode(PeriodeEingabe e)
    {
        var titel = Eingabe.Pflicht(e.Titel, 80, "le titre de la période", "titel");
        if (e.Beginn is null || e.Ende is null) throw new FachFehler("Indiquez les dates de début et de fin.", "beginn");
        if (e.Ende < e.Beginn) throw new FachFehler("La date de fin précède la date de début.", "ende");
        if (e.ZaehltFuerJahr && (e.Jahresgewicht <= 0 || e.Jahresgewicht > 100))
            throw new FachFehler("Le poids dans la moyenne annuelle doit être compris entre 0,01 et 100.", "gewicht");
        return (titel, new Zeitraum(e.Beginn.Value, e.Ende.Value), Math.Round(e.Jahresgewicht, 2, MidpointRounding.AwayFromZero));
    }

    private static void PruefeTitelFrei(IEnumerable<Periode> perioden, string titel, Guid? ausser)
    {
        if (perioden.Any(p => p.Id != ausser && string.Equals(p.Titel, titel, StringComparison.OrdinalIgnoreCase)))
            throw new FachFehler($"Une période « {titel} » existe déjà dans cette année.", "titel");
    }

    private static void PruefeStruktur(Schuljahr jahr, IEnumerable<Periode> perioden)
    {
        if (PeriodenPlan.Pruefe(new Zeitraum(jahr.Beginn, jahr.Ende), perioden.Select(p => (p.Titel, new Zeitraum(p.Beginn, p.Ende), p.ZaehltFuerJahr))) is { } fehler)
            throw new FachFehler(fehler);
    }
}
