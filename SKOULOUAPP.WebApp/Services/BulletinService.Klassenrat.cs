using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.Core.Services;
using SkoulouApp.WebApp.Data;

namespace SkoulouApp.WebApp.Services;

/// <summary>Zeile der Klassenrat-Erfassung (Conseil de classe) je Schüler.</summary>
public sealed record KlassenratZeile(Guid JahresZuordnungID, string Schueler, decimal? Moyenne, int? Rang,
    KlassenratDistinktion Vorschlag, KlassenratDistinktion? Distinktion, KlassenratSanktion Sanktionen, string? Bemerkung,
    Jahresentscheidung Entscheidung);

public sealed record KlassenratEingabe(Guid JahresZuordnungID, KlassenratDistinktion? Distinktion, KlassenratSanktion Sanktionen,
    string? Bemerkung, Jahresentscheidung Entscheidung);

/// <summary>Berechnungen einer Klasse für eine Periode (Fach-/Gesamtmittel, Teilnoten, Ränge, Statistik).</summary>
internal sealed class BulletinRechner(BewertungsUebersicht daten, Guid klasseID)
{
    public List<BewertungsFachZeile> Faecher { get; } = daten.Faecher.Where(x => x.KlasseID == klasseID).ToList();
    public List<BewertungsSchuelerZeile> Schueler { get; } = daten.Schueler.Where(x => x.KlasseID == klasseID).ToList();

    public decimal? FachMittel(Guid periode, Guid schueler, Guid fach) => NotenBerechnung.FachMittel(daten.Bestandteile
        .Where(x => x.PeriodeID == periode).Select(x => new NotenAnteil(Punkte(x.Id, schueler, fach), x.MaximalPunkte, x.Gewicht)));

    private decimal? Punkte(Guid bestandteil, Guid schueler, Guid fach)
        => daten.Noten.SingleOrDefault(n => n.BestandteilID == bestandteil && n.JahresZuordnungID == schueler && n.FachBewertungID == fach)?.Punkte;

    /// <summary>Teilnote eines Bestandteils, auf 20 umgerechnet.</summary>
    public decimal? Teilnote(BewertungsBestandteil bestandteil, Guid schueler, Guid fach)
        => Punkte(bestandteil.Id, schueler, fach) is decimal punkte && bestandteil.MaximalPunkte > 0
            ? punkte * 20m / bestandteil.MaximalPunkte : null;

    public string? LehrerBemerkung(Guid periode, Guid schueler, Guid fach)
    {
        var bestandteile = daten.Bestandteile.Where(x => x.PeriodeID == periode).Select(x => x.Id).ToHashSet();
        return daten.Noten.Where(n => bestandteile.Contains(n.BestandteilID) && n.JahresZuordnungID == schueler && n.FachBewertungID == fach
                && !string.IsNullOrWhiteSpace(n.Bemerkung))
            .OrderByDescending(n => n.GeaendertAmUtc ?? n.ErstelltAmUtc).Select(n => n.Bemerkung!.Trim()).FirstOrDefault();
    }

    public List<BewertungsBestandteil> Bestandteile(Guid periode)
        => daten.Bestandteile.Where(x => x.PeriodeID == periode).OrderBy(x => x.ErstelltAmUtc).ToList();

    public decimal? Mittel(Guid periode, Guid schueler)
        => NotenBerechnung.GesamtMittel(Faecher.Select(x => (FachMittel(periode, schueler, x.Id), x.Koeffizient)));

    /// <summary>Perioden derselben Art (ohne Tests), deren Reihenfolge das Schuljahr bildet.</summary>
    public List<BewertungsPeriode> JahresPerioden(BewertungsPeriode periode)
        => daten.Perioden.Where(x => x.Art == periode.Art && x.Art != BewertungsArt.Test).OrderBy(x => x.Reihenfolge).ToList();

    /// <summary>Letzte Periode des Jahres (z. B. 3e trimestre, 2e semestre) → Jahresbilanz.</summary>
    public bool IstJahresabschluss(BewertungsPeriode periode)
    {
        var perioden = JahresPerioden(periode);
        if (perioden.Count == 0 || perioden[^1].Id != periode.Id) return false;
        // Trimester: nur mit allen drei Trimestern (bestehende Regel); Semester u. a.: mindestens zwei Perioden.
        return periode.Art == BewertungsArt.Trimester
            ? perioden.Select(x => x.Reihenfolge).Distinct().Count() == 3 && periode.Reihenfolge == 3
            : perioden.Count >= 2;
    }

    public decimal? JahresMittel(BewertungsPeriode periode, Guid schueler)
        => NotenBerechnung.JahresMittel(JahresPerioden(periode).Select(x => new PeriodenAnteil(Mittel(x.Id, schueler), x.JahresGewicht, false)));

    public decimal? FachJahresMittel(BewertungsPeriode periode, Guid schueler, Guid fach)
        => NotenBerechnung.JahresMittel(JahresPerioden(periode).Select(x => new PeriodenAnteil(FachMittel(x.Id, schueler, fach), x.JahresGewicht, false)));

    /// <summary>Rang im Wettbewerbsverfahren (gleiche Moyenne = gleicher Rang); Schüler ohne Note zählen nicht.</summary>
    public static int? Rang(decimal? wert, IEnumerable<decimal?> alle)
        => wert is null ? null : 1 + alle.Count(x => x is not null && NotenBerechnung.Anzeige(x.Value) > NotenBerechnung.Anzeige(wert.Value));
}

public sealed partial class BulletinService
{
    /// <summary>Klassenrat einer Klasse und Periode: Moyenne, Rang, Vorschlag und gespeicherte Entscheidung je Schüler.</summary>
    public async Task<List<KlassenratZeile>> KlassenratAsync(Guid user, Guid schule, Guid periodeID, Guid klasseID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await DirektionAsync(db, user, schule, token);
        var periode = await db.Set<BewertungsPeriode>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == periodeID && x.SchulID == schule, token)
            ?? throw new InvalidOperationException("Cette période est introuvable.");
        var daten = await BewertungsService.LadenAsync(db, user, schule, periode.SchuljahrID, token);
        var rechner = new BulletinRechner(daten, klasseID);
        var mittel = rechner.Schueler.ToDictionary(x => x.Id, x => rechner.Mittel(periodeID, x.Id));
        var gespeichert = await db.Set<KlassenratEntscheidung>().AsNoTracking()
            .Where(x => x.PeriodeID == periodeID && x.SchulID == schule).ToDictionaryAsync(x => x.JahresZuordnungID, token);
        return rechner.Schueler.OrderBy(x => x.Name).Select(s =>
        {
            gespeichert.TryGetValue(s.Id, out var eintrag);
            var moyenne = mittel[s.Id];
            return new KlassenratZeile(s.Id, s.Name, moyenne, BulletinRechner.Rang(moyenne, mittel.Values), BulletinTexte.DistinktionVorschlag(moyenne),
                eintrag?.Distinktion, eintrag?.Sanktionen ?? KlassenratSanktion.Keine, eintrag?.Bemerkung, eintrag?.Entscheidung ?? Jahresentscheidung.Offen);
        }).ToList();
    }

    /// <summary>Speichert die Entscheidungen des Klassenrats. Bereits veröffentlichte Bulletins bleiben unverändert (Snapshot).</summary>
    public Task KlassenratSpeichernAsync(Guid user, Guid schule, Guid periodeID, IReadOnlyCollection<KlassenratEingabe> eingaben, CancellationToken token = default)
        => UnterrichtTransaktion.AusfuehrenAsync(factory, async db =>
        {
            await DirektionAsync(db, user, schule, token);
            if (!await db.Set<BewertungsPeriode>().AnyAsync(x => x.Id == periodeID && x.SchulID == schule, token))
                throw new InvalidOperationException("Cette période est introuvable.");
            if (eingaben.Any(e => e.Bemerkung?.Trim().Length > 300))
                throw new InvalidOperationException("L'appréciation du conseil est limitée à 300 caractères.");
            var ids = eingaben.Select(x => x.JahresZuordnungID).Distinct().ToList();
            var periode = await db.Set<BewertungsPeriode>().AsNoTracking().SingleAsync(x => x.Id == periodeID, token);
            if (await db.Set<SchuelerJahresZuordnung>().CountAsync(x => ids.Contains(x.Id) && x.SchulID == schule && x.SchuljahrID == periode.SchuljahrID, token) != ids.Count)
                throw new InvalidOperationException("Certains élèves n'appartiennent pas à cette année scolaire.");
            var vorhanden = await db.Set<KlassenratEntscheidung>().Where(x => x.PeriodeID == periodeID && ids.Contains(x.JahresZuordnungID)).ToListAsync(token);
            foreach (var eingabe in eingaben)
            {
                var eintrag = vorhanden.SingleOrDefault(x => x.JahresZuordnungID == eingabe.JahresZuordnungID);
                if (eintrag is null)
                {
                    eintrag = new KlassenratEntscheidung { Id = Guid.NewGuid(), SchulID = schule, PeriodeID = periodeID, JahresZuordnungID = eingabe.JahresZuordnungID };
                    db.Add(eintrag);
                }
                eintrag.Distinktion = eingabe.Distinktion;
                eintrag.Sanktionen = eingabe.Sanktionen;
                eintrag.Bemerkung = string.IsNullOrWhiteSpace(eingabe.Bemerkung) ? null : eingabe.Bemerkung.Trim();
                eintrag.Entscheidung = eingabe.Entscheidung;
                eintrag.BearbeitetVonID = user;
                eintrag.GeaendertAmUtc = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(token);
            return true;
        }, token);

    /// <summary>Ergänzt den Snapshot um die Felder des allgemeinen Layouts (Teilnoten, Ränge, Gruppen, Statistik, Klassenrat).</summary>
    private async Task<BulletinSnapshot> ErweiternAsync(ApplicationDbContext db, BulletinSnapshot basis, BulletinVorlage vorlage,
        BewertungsPeriode periode, SchuelerJahresZuordnung zuordnung, BewertungsUebersicht daten, Guid user, Guid schule, CancellationToken token)
    {
        var rechner = new BulletinRechner(daten, zuordnung.KlasseID);
        var schuelerIDs = rechner.Schueler.Select(x => x.Id).ToList();
        var jahresabschluss = rechner.IstJahresabschluss(periode);
        var bestandteile = rechner.Bestandteile(periode.Id);
        var fachIDs = await db.Set<FachBewertung>().AsNoTracking().Where(x => x.SchuljahrID == periode.SchuljahrID && x.KlasseID == zuordnung.KlasseID)
            .ToDictionaryAsync(x => x.Id, x => x.FachID, token);
        var faecherInfo = await db.Set<SkoulouApp.Core.DataModels.Schools.Fach>().AsNoTracking().Where(x => fachIDs.Values.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.Gruppe, x.Reihenfolge }, token);

        var zeilen = new List<BulletinFach>();
        foreach (var fach in rechner.Faecher
                     .OrderBy(f => faecherInfo.GetValueOrDefault(fachIDs.GetValueOrDefault(f.Id))?.Reihenfolge ?? 0).ThenBy(f => f.Fach))
        {
            var alt = basis.Faecher.FirstOrDefault(x => x.Fach == fach.Fach);
            var note = rechner.FachMittel(periode.Id, zuordnung.Id, fach.Id);
            var alleNoten = schuelerIDs.Select(s => rechner.FachMittel(periode.Id, s, fach.Id)).ToList();
            decimal? jahresNote = jahresabschluss ? rechner.FachJahresMittel(periode, zuordnung.Id, fach.Id) : null;
            int? jahresRang = jahresabschluss ? BulletinRechner.Rang(jahresNote, schuelerIDs.Select(s => rechner.FachJahresMittel(periode, s, fach.Id))) : null;
            zeilen.Add(new BulletinFach(fach.Fach, alt?.Lehrer ?? "", fach.Koeffizient, note,
                faecherInfo.GetValueOrDefault(fachIDs.GetValueOrDefault(fach.Id))?.Gruppe?.Trim(),
                bestandteile.Select(b => new BulletinTeilnote(b.Bezeichnung, rechner.Teilnote(b, zuordnung.Id, fach.Id))).ToList(),
                BulletinRechner.Rang(note, alleNoten),
                rechner.LehrerBemerkung(periode.Id, zuordnung.Id, fach.Id) ?? BulletinTexte.Appreciation(note),
                jahresNote, jahresRang));
        }

        var klassenMittel = schuelerIDs.Select(s => rechner.Mittel(periode.Id, s)).Where(x => x is not null).Select(x => x!.Value).ToList();
        int? jahresRangGesamt = null;
        decimal? jahresNoteGesamt = basis.JahresNote;
        if (jahresabschluss)
        {
            jahresNoteGesamt = rechner.JahresMittel(periode, zuordnung.Id);
            jahresRangGesamt = BulletinRechner.Rang(jahresNoteGesamt, schuelerIDs.Select(s => rechner.JahresMittel(periode, s)));
        }

        var rat = await db.Set<KlassenratEntscheidung>().AsNoTracking().SingleOrDefaultAsync(x => x.PeriodeID == periode.Id && x.JahresZuordnungID == zuordnung.Id, token);
        var distinktion = rat?.Distinktion ?? BulletinTexte.DistinktionVorschlag(basis.Durchschnitt);
        var school = await db.Schools.AsNoTracking().SingleAsync(x => x.Id == schule, token);
        var profil = await db.StudentProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.AppUserId == zuordnung.SchuelerID && x.SchoolId == schule, token);
        var foto = vorlage.ZeigeFoto && zuordnung.Schueler.ProfilBildAssetId is Guid fotoID ? await FotoAsync(db, user, schule, fotoID, token) : null;

        return basis with
        {
            Faecher = zeilen,
            JahresNote = jahresNoteGesamt,
            Layout = new BulletinLayout(vorlage.ZeigeTeilnoten, vorlage.ZeigePunkte, vorlage.ZeigeFachRang, vorlage.ZeigeAppreciation,
                vorlage.ZeigeSignaturSpalte, vorlage.ZeigeFachGruppen, vorlage.ZeigeKlassenStatistik, vorlage.ZeigeMentionen,
                vorlage.ZeigeJahresBilanz, vorlage.ZeigeFoto, vorlage.ZeigeNoteInWorten),
            Direction = school.DokumentDirection,
            Statut = school.DokumentStatut,
            Ort = school.City,
            Geschlecht = zuordnung.Schueler.Geschlecht switch { Geschlecht.Maennlich => "M", Geschlecht.Weiblich => "F", _ => null },
            Nationalitaet = profil?.Nationalitaet,
            IstInterne = profil?.IstInterne ?? false,
            IstAffecte = profil?.IstAffecte ?? false,
            Foto = foto,
            TeilnotenSpalten = bestandteile.Count > 1 ? bestandteile.Select(b => b.Bezeichnung).ToList() : [],
            KlassenMittel = klassenMittel.Count > 0 ? klassenMittel.Average() : null,
            KlassenMin = klassenMittel.Count > 0 ? klassenMittel.Min() : null,
            KlassenMax = klassenMittel.Count > 0 ? klassenMittel.Max() : null,
            Distinktion = BulletinTexte.DistinktionText(distinktion),
            Sanktionen = BulletinTexte.SanktionTexte(rat?.Sanktionen ?? KlassenratSanktion.Keine),
            RatsBemerkung = rat?.Bemerkung,
            Entscheidung = jahresabschluss ? BulletinTexte.EntscheidungText(rat?.Entscheidung ?? Jahresentscheidung.Offen) : null,
            JahresRang = jahresRangGesamt,
            IstJahresabschluss = jahresabschluss
        };
    }

    private async Task<byte[]?> FotoAsync(ApplicationDbContext db, Guid user, Guid schule, Guid assetID, CancellationToken token)
    {
        try { return await BildAsync(db, user, schule, assetID, token); }
        catch (InvalidOperationException) { return null; } // Fehlendes Foto blockiert kein Bulletin.
    }
}

public sealed partial class BulletinService
{
    /// <summary>Vorschau einer (auch ungespeicherten) Vorlage mit fiktivem Schüler und den Kopfdaten der Schule.</summary>
    public async Task<BulletinSnapshot> VorlagenBeispielAsync(Guid user, BulletinVorlage vorlage, bool jahresabschluss, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await DirektionAsync(db, user, vorlage.SchulID, token);
        var schule = await db.Schools.AsNoTracking().SingleAsync(x => x.Id == vorlage.SchulID, token);
        byte[]? logo = null;
        if (vorlage.LogoAssetID is Guid logoID)
        {
            try { logo = await BildAsync(db, user, vorlage.SchulID, logoID, token); }
            catch (InvalidOperationException) { logo = null; }
        }
        return BulletinBeispiel.FuerVorlage(vorlage, schule, logo, jahresabschluss);
    }
}
