using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.WebApp.Data;

namespace SkoulouApp.WebApp.Services;

public sealed record BulletinKlassenPruefung(string Hash, List<BulletinSnapshot> Bulletins);
public sealed record NotenEntwurf(Guid NoteID, decimal Punkte, string? Bemerkung);
public sealed record BulletinWirkung(Guid AusgabeID, string Schueler, string Periode, decimal? Alt, decimal? Neu, int? AlterRang, int? NeuerRang, BulletinSnapshot Vorschau);
public sealed record BerichtigungsPruefung(string Hash, List<BulletinWirkung> Auswirkungen);

public sealed partial class BulletinService
{
    private static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    private static string Token() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static async Task<List<SchuelerJahresZuordnung>> KohorteAsync(ApplicationDbContext db, Guid schule, Guid periode, Guid klasse, CancellationToken token)
    {
        var p = await db.Set<BewertungsPeriode>().SingleOrDefaultAsync(x => x.Id == periode && x.SchulID == schule, token)
            ?? throw new InvalidOperationException("Sélectionnez une période de cette école.");
        if (!await db.Klassen.AnyAsync(x => x.Id == klasse && x.SchoolId == schule, token))
            throw new InvalidOperationException("Sélectionnez une classe de cette école.");
        var liste = await db.Set<SchuelerJahresZuordnung>().Where(x => x.SchulID == schule && x.SchuljahrID == p.SchuljahrID && x.KlasseID == klasse)
            .OrderBy(x => x.Id).ToListAsync(token);
        if (liste.Count == 0 || liste.Count > 200) throw new InvalidOperationException("La classe doit contenir entre 1 et 200 élèves inscrits à l'année scolaire.");
        return liste;
    }

    private async Task<BulletinKlassenPruefung> KlassenPruefungAsync(ApplicationDbContext db, Guid user, Guid schule, Guid periode, Guid klasse, CancellationToken token)
    {
        await DirektionAsync(db, user, schule, token);
        var kohorte = await KohorteAsync(db, schule, periode, klasse, token);
        if (await db.Set<BulletinAbschluss>().AnyAsync(x => x.PeriodeID == periode && x.KlasseID == klasse, token))
            throw new InvalidOperationException("Cette classe est déjà publiée définitivement pour cette période.");
        var liste = new List<BulletinSnapshot>();
        long taille = 0;
        foreach (var eleve in kohorte)
        {
            var ancien = await db.Set<BulletinAusgabe>().AsNoTracking().SingleOrDefaultAsync(x => x.PeriodeID == periode && x.JahresZuordnungID == eleve.Id && x.IstVeroeffentlicht, token);
            var observation = ancien is null ? null : JsonSerializer.Deserialize<BulletinSnapshot>(ancien.SnapshotJson)?.Bemerkung;
            var snapshot = await SnapshotAsync(db, user, schule, periode, eleve.Id, observation, token);
            if (snapshot.Durchschnitt is null || snapshot.Faecher.Count == 0) throw new InvalidOperationException($"Notes incomplètes : {snapshot.Schueler}. Complétez toute la classe avant de publier.");
            taille += JsonSerializer.Serialize(snapshot).Length;
            if (taille > 32 * 1024 * 1024) throw new InvalidOperationException("Les données de la classe dépassent la limite de 32 Mo.");
            liste.Add(snapshot);
        }
        return new(Hash(JsonSerializer.Serialize(liste)), liste);
    }

    public Task<BulletinKlassenPruefung> KlassePruefenAsync(Guid user, Guid schule, Guid periode, Guid klasse, CancellationToken token = default)
        => UnterrichtTransaktion.AusfuehrenAsync(factory, db => KlassenPruefungAsync(db, user, schule, periode, klasse, token), token);

    public Task<int> KlasseEndgueltigVeroeffentlichenAsync(Guid user, Guid schule, Guid periode, Guid klasse, string pruefHash, bool bestaetigt, CancellationToken token = default)
        => UnterrichtTransaktion.AusfuehrenAsync(factory, async db =>
        {
            if (!bestaetigt) throw new InvalidOperationException("Confirmez la publication définitive.");
            var pruefung = await KlassenPruefungAsync(db, user, schule, periode, klasse, token);
            if (pruefung.Hash != pruefHash) throw new InvalidOperationException("Les données ont changé. Vérifiez à nouveau toute la classe.");
            var kohorte = await KohorteAsync(db, schule, periode, klasse, token);
            for (var i = 0; i < kohorte.Count; i++)
            {
                var id = kohorte[i].Id;
                var anciennes = await db.Set<BulletinAusgabe>().Where(x => x.PeriodeID == periode && x.JahresZuordnungID == id).ToListAsync(token);
                var active = anciennes.SingleOrDefault(x => x.IstVeroeffentlicht);
                if (active is not null) { active.IstVeroeffentlicht = false; await db.SaveChangesAsync(token); }
                var neu = Ausgabe(schule, periode, id, anciennes.Select(x => x.Version).DefaultIfEmpty().Max() + 1, user, "Publication définitive", pruefung.Bulletins[i], active);
                db.Add(neu);
                if (active is not null) active.ErsetztDurchID = neu.Id;
            }
            db.Add(new BulletinAbschluss { Id = Guid.NewGuid(), SchulID = schule, PeriodeID = periode, KlasseID = klasse, ErstelltVonID = user });
            await db.SaveChangesAsync(token);
            return kohorte.Count;
        }, token);

    private static BulletinAusgabe Ausgabe(Guid schule, Guid periode, Guid schueler, int version, Guid user, string grund, BulletinSnapshot snapshot, BulletinAusgabe? alt)
    {
        var id = Guid.NewGuid(); var pruefToken = Token(); var datum = DateTime.UtcNow;
        var nummer = $"B-{id:N}";
        return new BulletinAusgabe { Id = id, SchulID = schule, PeriodeID = periode, JahresZuordnungID = schueler, Version = version,
            IstVeroeffentlicht = true, IstEndgueltig = true, PruefToken = pruefToken, ErstelltVonID = user,
            ErsetztAusgabeID = alt?.Id, AenderungsGrund = grund, ErstelltAmUtc = datum,
            SnapshotJson = JsonSerializer.Serialize(snapshot with { DokumentNummer = nummer, DokumentDatumUtc = datum, PruefToken = pruefToken,
                ErsetztDokumentNummer = alt is null || !alt.IstEndgueltig ? null : $"B-{alt.Id:N}" }) };
    }

    public Task<Guid> BerichtigungStartenAsync(Guid user, Guid schule, Guid periode, Guid klasse, string grund, CancellationToken token = default)
        => UnterrichtTransaktion.AusfuehrenAsync(factory, async db =>
        {
            await DirektionAsync(db, user, schule, token);
            await KohorteAsync(db, schule, periode, klasse, token);
            if (string.IsNullOrWhiteSpace(grund) || grund.Trim().Length > 300) throw new InvalidOperationException("Indiquez le motif de la rectification (300 caractères maximum).");
            if (!await db.Set<BulletinAbschluss>().AnyAsync(x => x.PeriodeID == periode && x.KlasseID == klasse, token)) throw new InvalidOperationException("Publiez d'abord la classe définitivement.");
            var jahr = await db.Set<BewertungsPeriode>().Where(x => x.Id == periode).Select(x => x.SchuljahrID).SingleAsync(token);
            if (await db.Set<BulletinBerichtigung>().AnyAsync(x => x.SchulID == schule && x.SchuljahrID == jahr && x.KlasseID == klasse && x.FreigegebenAmUtc == null && x.AbgebrochenAmUtc == null, token))
                throw new InvalidOperationException("Une rectification est déjà en préparation pour cette classe et cette année.");
            var entwurf = new BulletinBerichtigung { Id = Guid.NewGuid(), SchulID = schule, SchuljahrID = jahr, KlasseID = klasse, ErstelltVonID = user, Grund = grund.Trim() };
            db.Add(entwurf); await db.SaveChangesAsync(token); return entwurf.Id;
        }, token);

    private static async Task<BulletinBerichtigung> EntwurfAsync(ApplicationDbContext db, Guid user, Guid id, CancellationToken token)
    {
        var e = await db.Set<BulletinBerichtigung>().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new InvalidOperationException("Rectification introuvable.");
        await DirektionAsync(db, user, e.SchulID, token);
        if (e.FreigegebenAmUtc != null || e.AbgebrochenAmUtc != null) throw new InvalidOperationException("Cette rectification est terminée.");
        return e;
    }

    public async Task<List<BulletinBerichtigung>> BerichtigungenAsync(Guid user, Guid schule, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token); await DirektionAsync(db, user, schule, token);
        return await db.Set<BulletinBerichtigung>().AsNoTracking().Where(x => x.SchulID == schule && x.FreigegebenAmUtc == null && x.AbgebrochenAmUtc == null).ToListAsync(token);
    }

    public Task<bool> BerichtigungsNoteAsync(Guid user, Guid id, Guid noteID, decimal punkte, string? bemerkung, CancellationToken token = default)
        => UnterrichtTransaktion.AusfuehrenAsync(factory, async db =>
        {
            var e = await EntwurfAsync(db, user, id, token);
            var note = await db.Set<FachNote>().Include(x => x.Bestandteil).Include(x => x.JahresZuordnung).SingleOrDefaultAsync(x => x.Id == noteID, token);
            if (note is null || note.JahresZuordnung.SchulID != e.SchulID || note.JahresZuordnung.SchuljahrID != e.SchuljahrID || note.JahresZuordnung.KlasseID != e.KlasseID
                || punkte < 0 || punkte > note.Bestandteil.MaximalPunkte || decimal.Round(punkte, 2) != punkte || bemerkung?.Length > 300)
                throw new InvalidOperationException("Choisissez une note de cette classe et respectez son barème.");
            var liste = JsonSerializer.Deserialize<List<NotenEntwurf>>(e.EntwurfJson)!;
            liste.RemoveAll(x => x.NoteID == noteID); liste.Add(new(noteID, punkte, string.IsNullOrWhiteSpace(bemerkung) ? null : bemerkung.Trim()));
            e.EntwurfJson = JsonSerializer.Serialize(liste); e.VorschauHash = null;
            await db.SaveChangesAsync(token); return true;
        }, token);

    private async Task<BerichtigungsPruefung> WirkungAsync(ApplicationDbContext db, Guid user, BulletinBerichtigung e, CancellationToken token)
    {
        var entwuerfe = JsonSerializer.Deserialize<List<NotenEntwurf>>(e.EntwurfJson)!;
        var stammdaten = JsonSerializer.Deserialize<List<BulletinStammdatenEntwurf>>(e.StammdatenEntwurfJson)!;
        if (entwuerfe.Count == 0 && stammdaten.Count == 0) throw new InvalidOperationException("Ajoutez au moins une correction au brouillon.");
        foreach (var s in stammdaten) await StammdatenPruefenAsync(db, e, s, token);
        var daten = await BewertungsService.LadenAsync(db, user, e.SchulID, e.SchuljahrID, token);
        var originale = JsonSerializer.Serialize(daten.Noten.OrderBy(x => x.Id));
        foreach (var n in entwuerfe)
        {
            var note = daten.Noten.SingleOrDefault(x => x.Id == n.NoteID) ?? throw new InvalidOperationException("Une note du brouillon n'existe plus.");
            note.Punkte = n.Punkte; note.Bemerkung = n.Bemerkung;
        }
        var ausgaben = await db.Set<BulletinAusgabe>().AsNoTracking().Where(x => x.SchulID == e.SchulID && x.IstVeroeffentlicht
            && x.JahresZuordnung.SchuljahrID == e.SchuljahrID && x.JahresZuordnung.KlasseID == e.KlasseID).OrderBy(x => x.Id).ToListAsync(token);
        var wirkungen = new List<BulletinWirkung>();
        long taille = 0;
        foreach (var ausgabe in ausgaben)
        {
            var alt = JsonSerializer.Deserialize<BulletinSnapshot>(ausgabe.SnapshotJson)!;
            var berechnet = entwuerfe.Count == 0 ? alt : await SnapshotAsync(db, user, e.SchulID, ausgabe.PeriodeID, ausgabe.JahresZuordnungID, alt.Bemerkung, token, daten.Noten);
            // Bestehende Gestaltung/Identität bleibt erhalten; nur fachliche Notenfelder werden berichtigt.
            var neu = alt with { Faecher = berechnet.Faecher, Durchschnitt = berechnet.Durchschnitt, Rang = berechnet.Rang,
                Vorperioden = berechnet.Vorperioden, JahresNote = berechnet.JahresNote };
            var identitaet = stammdaten.SingleOrDefault(x => x.JahresZuordnungID == ausgabe.JahresZuordnungID);
            if (identitaet is not null) neu = StammdatenAnwenden(neu, identitaet);
            var direkt = entwuerfe.Any(n => daten.Noten.Any(note => note.Id == n.NoteID && note.JahresZuordnungID == ausgabe.JahresZuordnungID
                && daten.Bestandteile.Any(b => b.Id == note.BestandteilID && b.PeriodeID == ausgabe.PeriodeID)));
            if (direkt || JsonSerializer.Serialize(neu) != JsonSerializer.Serialize(alt))
            {
                taille += JsonSerializer.Serialize(neu).Length;
                if (wirkungen.Count >= 200 || taille > 32 * 1024 * 1024) throw new InvalidOperationException("La rectification dépasse la limite de 200 bulletins ou 32 Mo.");
                wirkungen.Add(new(ausgabe.Id, alt.Schueler, alt.Titel, alt.Durchschnitt, neu.Durchschnitt, alt.Rang, neu.Rang, neu));
            }
        }
        if (wirkungen.Count == 0) throw new InvalidOperationException("Aucun bulletin publié n'est concerné par ce brouillon.");
        return new(Hash(originale + e.EntwurfJson + e.StammdatenEntwurfJson + JsonSerializer.Serialize(wirkungen)), wirkungen);
    }

    public Task<BerichtigungsPruefung> BerichtigungPruefenAsync(Guid user, Guid id, CancellationToken token = default)
        => UnterrichtTransaktion.AusfuehrenAsync(factory, async db =>
        {
            var e = await EntwurfAsync(db, user, id, token); var pruefung = await WirkungAsync(db, user, e, token);
            e.VorschauHash = pruefung.Hash; await db.SaveChangesAsync(token); return pruefung;
        }, token);

    public Task<int> BerichtigungFreigebenAsync(Guid user, Guid id, string hash, bool bestaetigt, CancellationToken token = default)
        => UnterrichtTransaktion.AusfuehrenAsync(factory, async db =>
        {
            var e = await EntwurfAsync(db, user, id, token);
            var pruefung = await WirkungAsync(db, user, e, token);
            if (!bestaetigt || hash != pruefung.Hash || e.VorschauHash != hash) throw new InvalidOperationException("Vérifiez à nouveau les effets et confirmez la rectification définitive.");
            var name = await db.Users.Where(x => x.Id == user).Select(x => x.FullName).SingleAsync(token);
            foreach (var n in JsonSerializer.Deserialize<List<NotenEntwurf>>(e.EntwurfJson)!)
            {
                var note = await db.Set<FachNote>().SingleAsync(x => x.Id == n.NoteID, token);
                db.Add(new NotenAenderung { Id = Guid.NewGuid(), FachNoteID = note.Id, AltePunkte = note.Punkte, NeuePunkte = n.Punkte,
                    AlteBemerkung = note.Bemerkung, NeueBemerkung = n.Bemerkung, BearbeitetVonID = user, BearbeiterName = name });
                note.Punkte = n.Punkte; note.Bemerkung = n.Bemerkung; note.BearbeitetVonID = user; note.GeaendertAmUtc = DateTime.UtcNow;
            }
            foreach (var w in pruefung.Auswirkungen)
            {
                var alt = await db.Set<BulletinAusgabe>().SingleAsync(x => x.Id == w.AusgabeID, token);
                alt.IstVeroeffentlicht = false;
            }
            await db.SaveChangesAsync(token); // Gefilterten Unique-Index vor Einfügen freigeben, weiterhin dieselbe Transaktion.
            foreach (var w in pruefung.Auswirkungen)
            {
                var alt = await db.Set<BulletinAusgabe>().SingleAsync(x => x.Id == w.AusgabeID, token);
                var version = await db.Set<BulletinAusgabe>().Where(x => x.PeriodeID == alt.PeriodeID && x.JahresZuordnungID == alt.JahresZuordnungID).MaxAsync(x => x.Version, token) + 1;
                var neu = Ausgabe(e.SchulID, alt.PeriodeID, alt.JahresZuordnungID, version, user, e.Grund, w.Vorschau, alt);
                db.Add(neu); alt.ErsetztDurchID = neu.Id;
            }
            e.FreigegebenAmUtc = DateTime.UtcNow; e.FreigegebenVonID = user;
            await db.SaveChangesAsync(token); return pruefung.Auswirkungen.Count;
        }, token);

    public Task<bool> BerichtigungAbbrechenAsync(Guid user, Guid id, CancellationToken token = default)
        => UnterrichtTransaktion.AusfuehrenAsync(factory, async db => { var e = await EntwurfAsync(db, user, id, token); e.AbgebrochenAmUtc = DateTime.UtcNow; await db.SaveChangesAsync(token); return true; }, token);

    public async Task UebergabeSpeichernAsync(Guid user, Guid ausgabeID, bool abholung, string vermerk, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var ausgabe = await db.Set<BulletinAusgabe>().SingleOrDefaultAsync(x => x.Id == ausgabeID, token) ?? throw new InvalidOperationException("Bulletin introuvable.");
        await DirektionAsync(db, user, ausgabe.SchulID, token);
        if (string.IsNullOrWhiteSpace(vermerk) || vermerk.Length > 300) throw new InvalidOperationException("Précisez le destinataire et le mode de contact / remise (300 caractères maximum).");
        db.Add(new BulletinUebergabe { Id = Guid.NewGuid(), AusgabeID = ausgabeID, BearbeitetVonID = user, IstAbholung = abholung, Vermerk = vermerk.Trim() });
        await db.SaveChangesAsync(token);
    }

    public async Task<List<BulletinUebergabe>> UebergabenAsync(Guid user, Guid ausgabeID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var ausgabe = await db.Set<BulletinAusgabe>().SingleAsync(x => x.Id == ausgabeID, token); await DirektionAsync(db, user, ausgabe.SchulID, token);
        return await db.Set<BulletinUebergabe>().AsNoTracking().Where(x => x.AusgabeID == ausgabeID).OrderByDescending(x => x.ErstelltAmUtc).ToListAsync(token);
    }
}
