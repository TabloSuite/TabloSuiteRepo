using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.Core.Services;
using SkoulouApp.Core.Services.Storage;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.WebApp.Data;

namespace SkoulouApp.WebApp.Services;

public sealed partial class BulletinService(IDbContextFactory<ApplicationDbContext> factory, BewertungsService bewertungen, IBlobMediaLibraryService? medien = null)
{
    public async Task<List<BulletinVorlage>> VorlagenAsync(Guid user, Guid schule, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await DirektionAsync(db, user, schule, token);
        return await db.Set<BulletinVorlage>().AsNoTracking().Where(x => x.SchulID == schule).ToListAsync(token);
    }

    public async Task VorlageSpeichernAsync(Guid user, BulletinVorlage eingabe, CancellationToken token = default)
    {
        await VorlagenZuweisenAsync(user, eingabe, [eingabe.KlasseID], true, token);
    }

    public Task<int> VorlagenZuweisenAsync(Guid user, BulletinVorlage eingabe, IReadOnlyCollection<Guid> klassen,
        bool ueberschreiben, CancellationToken token = default)
    {
        var ids = klassen.Distinct().OrderBy(x => x).ToArray();
        return UnterrichtTransaktion.AusfuehrenAsync(factory, async db =>
        {
        await DirektionAsync(db, user, eingabe.SchulID, token);
        if (ids.Length == 0 || ids.Contains(Guid.Empty)
            || await db.Klassen.CountAsync(x => ids.Contains(x.Id) && x.SchoolId == eingabe.SchulID && !x.IstSystemNiveau, token) != ids.Length)
            throw new InvalidOperationException("Sélectionnez au moins une classe de cette école.");
        if (!ueberschreiben && await db.Set<BulletinVorlage>().AnyAsync(x => x.SchulID == eingabe.SchulID
            && ids.Contains(x.KlasseID) && x.Art == eingabe.Art, token))
            throw new InvalidOperationException("Certaines classes possèdent déjà un modèle de ce type. Confirmez leur remplacement ou modifiez la sélection. Aucun modèle n'a été modifié.");
        foreach (var id in ids) await VorlageSpeichernAsync(db, user, eingabe, id, token);
        await db.SaveChangesAsync(token);
        return ids.Length;
        }, token);
    }

    private static async Task VorlageSpeichernAsync(ApplicationDbContext db, Guid user, BulletinVorlage eingabe, Guid klasseID, CancellationToken token)
    {
        if (!Enum.IsDefined(eingabe.Art) || string.IsNullOrWhiteSpace(eingabe.Name) || eingabe.Name.Trim().Length > 120
            || !GueltigeBloecke(eingabe.BlockReihenfolge) || eingabe.SchriftGroesse is < 8 or > 12 || eingabe.SeitenRand is < 15 or > 50
            || eingabe.FussText?.Length > 300 || !Regex.IsMatch(eingabe.AkzentFarbe ?? "", "^#[0-9a-fA-F]{6}$")
            || !await db.Klassen.AnyAsync(x => x.Id == klasseID && x.SchoolId == eingabe.SchulID, token))
            throw new InvalidOperationException("Vérifiez la classe, le nom, la couleur et le texte du modèle.");
        var vorlage = await db.Set<BulletinVorlage>().SingleOrDefaultAsync(x => x.SchulID == eingabe.SchulID
            && x.KlasseID == klasseID && x.Art == eingabe.Art, token);
        if (vorlage is null)
        {
            vorlage = new BulletinVorlage { Id = Guid.NewGuid(), SchulID = eingabe.SchulID, KlasseID = klasseID, Art = eingabe.Art };
            db.Add(vorlage);
        }
        vorlage.Name = eingabe.Name.Trim(); vorlage.AkzentFarbe = eingabe.AkzentFarbe!;
        vorlage.ZeigeKoeffizienten = eingabe.ZeigeKoeffizienten; vorlage.ZeigeRang = eingabe.ZeigeRang;
        vorlage.ZeigeVorperioden = eingabe.ZeigeVorperioden; vorlage.ZeigeUnterschriften = eingabe.ZeigeUnterschriften;
        vorlage.FussText = eingabe.FussText?.Trim();
        vorlage.BlockReihenfolge = eingabe.BlockReihenfolge;
        vorlage.SchriftGroesse = eingabe.SchriftGroesse; vorlage.SeitenRand = eingabe.SeitenRand;
        vorlage.ZeigeLehrkraefte = eingabe.ZeigeLehrkraefte; vorlage.ZeigeAnwesenheit = eingabe.ZeigeAnwesenheit;
        foreach (var assetID in new[] { eingabe.LogoAssetID }.Where(x => x.HasValue))
            if (!await db.BlobMediaAssets.AnyAsync(x => x.Id == assetID && x.SchoolId == eingabe.SchulID && x.MediaType == BlobMediaType.Image, token))
                throw new InvalidOperationException("Le logo doit provenir de la bibliothèque de cette école.");
        vorlage.LogoAssetID = eingabe.LogoAssetID;
        vorlage.SiegelAssetID = null; // Legacy-Spalte bleibt erhalten, neue Dokumente verwenden echte Schulstempel.
    }

    public async Task<BulletinSnapshot> VorschauAsync(Guid user, Guid schule, Guid periodeID, Guid zuordnungID,
        string? bemerkung, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        return await SnapshotAsync(db, user, schule, periodeID, zuordnungID, bemerkung, token);
    }

    private async Task<BulletinSnapshot> SnapshotAsync(ApplicationDbContext db, Guid user, Guid schule,
        Guid periodeID, Guid zuordnungID, string? bemerkung, CancellationToken token, List<FachNote>? entwurf = null)
    {
        await DirektionAsync(db, user, schule, token);
        if (bemerkung?.Length > 1000) throw new InvalidOperationException("L'observation est limitée à 1 000 caractères.");
        var periode = await db.Set<BewertungsPeriode>().AsNoTracking().Include(x => x.Schuljahr)
            .SingleOrDefaultAsync(x => x.Id == periodeID && x.SchulID == schule, token)
            ?? throw new InvalidOperationException("Cette période est introuvable.");
        var zuordnung = await db.Set<SchuelerJahresZuordnung>().AsNoTracking().Include(x => x.Schueler).Include(x => x.Klasse)
            .SingleOrDefaultAsync(x => x.Id == zuordnungID && x.SchulID == schule && x.SchuljahrID == periode.SchuljahrID, token)
            ?? throw new InvalidOperationException("Cette inscription ne correspond pas à la période.");
        var vorlage = await db.Set<BulletinVorlage>().AsNoTracking().SingleOrDefaultAsync(x => x.SchulID == schule
            && x.KlasseID == zuordnung.KlasseID && x.Art == periode.Art, token)
            ?? throw new InvalidOperationException("Créez d'abord un modèle pour cette classe et ce type de bulletin.");
        var daten = await BewertungsService.LadenAsync(db, user, schule, periode.SchuljahrID, token);
        if (entwurf is not null) daten = daten with { Noten = entwurf };
        var faecher = daten.Faecher.Where(x => x.KlasseID == zuordnung.KlasseID).OrderBy(x => x.Fach).ToList();
        decimal? FachMittel(Guid periode, Guid schueler, Guid fach) => NotenBerechnung.FachMittel(daten.Bestandteile
            .Where(x => x.PeriodeID == periode).Select(x => new NotenAnteil(daten.Noten.SingleOrDefault(n => n.BestandteilID == x.Id
                && n.JahresZuordnungID == schueler && n.FachBewertungID == fach)?.Punkte, x.MaximalPunkte, x.Gewicht)));
        decimal? Mittel(Guid period, Guid schueler) => NotenBerechnung.GesamtMittel(faecher.Select(x => (FachMittel(period, schueler, x.Id), x.Koeffizient)));
        var durchschnitt = Mittel(periodeID, zuordnungID);
        var kohorte = daten.Schueler.Where(x => x.KlasseID == zuordnung.KlasseID).ToList();
        var mittelwerte = kohorte.Select(x => Mittel(periodeID, x.Id)).ToList();
        int? rang = durchschnitt is null || mittelwerte.Any(x => x is null) ? null
            : 1 + mittelwerte.Count(x => NotenBerechnung.Anzeige(x!.Value) > NotenBerechnung.Anzeige(durchschnitt.Value));
        var lehrer = await db.Set<UnterrichtsZuordnung>().AsNoTracking().Where(x => x.SchulID == schule
            && x.SchuljahrID == periode.SchuljahrID && x.KlasseID == zuordnung.KlasseID)
            .Select(x => new { x.FachID, x.Lehrer.FullName }).ToListAsync(token);
        var fachIDs = await db.Set<FachBewertung>().AsNoTracking().Where(x => x.SchuljahrID == periode.SchuljahrID && x.KlasseID == zuordnung.KlasseID)
            .ToDictionaryAsync(x => x.Id, x => x.FachID, token);
        var zeilen = faecher.Select(x => new BulletinFach(x.Fach, string.Join(", ", lehrer.Where(l => l.FachID == fachIDs[x.Id]).Select(l => l.FullName).Distinct()),
            x.Koeffizient, FachMittel(periodeID, zuordnungID, x.Id))).ToList();
        var trim = daten.Perioden.Where(x => x.Art == BewertungsArt.Trimester).OrderBy(x => x.Reihenfolge).ToList();
        decimal? jahresnote = periode.Art == BewertungsArt.Trimester && periode.Reihenfolge == 3 && trim.Select(x => x.Reihenfolge).Distinct().Count() == 3
            ? NotenBerechnung.JahresMittel(trim.Select(x => new PeriodenAnteil(Mittel(x.Id, zuordnungID), x.JahresGewicht, false))) : null;
        var vorher = daten.Perioden.Where(x => x.Art == periode.Art && x.Reihenfolge < periode.Reihenfolge && x.Art != BewertungsArt.Test)
            .OrderBy(x => x.Reihenfolge).Select(x => new BulletinVorperiode(x.Titel, Mittel(x.Id, zuordnungID))).ToList();
        var school = await db.Schools.AsNoTracking().SingleAsync(x => x.Id == schule, token);
        var profil = await db.StudentProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.AppUserId == zuordnung.SchuelerID && x.SchoolId == schule, token);
        var praesenz = await db.Set<AnwesenheitsEintrag>().AsNoTracking().Where(x => x.PeriodeID == periodeID && x.JahresZuordnungID == zuordnungID).ToListAsync(token);
        return new(school.OffizielleBezeichnung ?? school.Name, school.Slogan, school.DokumentAdresse, school.RepublikZeile, school.Ministerium,
            school.StaatsMotto, school.Zulassungsnummer, string.Join(" · ", new[] { school.DokumentTelefon, school.DokumentEmail }.Where(x => !string.IsNullOrWhiteSpace(x))),
            periode.Schuljahr.Bezeichnung, periode.Titel, zuordnung.Klasse.Code, zuordnung.Schueler.FullName, profil?.Matrikelnummer,
            profil?.Geburtsort, zuordnung.Schueler.GeburtsdatumUtc?.ToString("dd/MM/yyyy"), zuordnung.IstWiederholer,
            zeilen, durchschnitt, rang, kohorte.Count, vorher, jahresnote, bemerkung?.Trim() ?? "",
            vorlage.AkzentFarbe, vorlage.ZeigeKoeffizienten, vorlage.ZeigeRang, vorlage.ZeigeVorperioden, vorlage.ZeigeUnterschriften, vorlage.FussText, vorlage.BlockReihenfolge,
            await BildAsync(db, user, schule, vorlage.LogoAssetID, token), null,
            praesenz.Count, praesenz.Count(x => x.Status == AnwesenheitsStatus.Abwesend),
            praesenz.Count(x => x.Status == AnwesenheitsStatus.Abwesend && x.IstEntschuldigt),
            praesenz.Count(x => x.Status == AnwesenheitsStatus.Verspaetet), praesenz.Sum(x => x.VerspaetungsMinuten),
            vorlage.SchriftGroesse, vorlage.SeitenRand, vorlage.ZeigeLehrkraefte, vorlage.ZeigeAnwesenheit);
    }

    private async Task<byte[]?> BildAsync(ApplicationDbContext db, Guid user, Guid schule, Guid? id, CancellationToken token)
    {
        if (id is null) return null;
        if (medien is null || !await db.BlobMediaAssets.AnyAsync(x => x.Id == id && x.SchoolId == schule && x.MediaType == BlobMediaType.Image, token))
            throw new InvalidOperationException("Le logo ou le cachet n'est plus disponible dans cette école.");
        var roles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == user select r.Name!).ToListAsync(token);
        var download = await medien.OpenImageAsync(id.Value, user, roles, token)
            ?? throw new InvalidOperationException("Impossible de lire le logo ou le cachet.");
        await using var stream = download.Content;
        if (download.ContentType is not ("image/png" or "image/jpeg")) throw new InvalidOperationException("Utilisez une image PNG ou JPEG pour les documents.");
        using var buffer = new MemoryStream();
        var bloc = new byte[8192];
        int lu;
        while ((lu = await stream.ReadAsync(bloc, token)) > 0)
        {
            if (buffer.Length + lu > 2 * 1024 * 1024) throw new InvalidOperationException("Le logo et le cachet sont limités à 2 Mo chacun.");
            await buffer.WriteAsync(bloc.AsMemory(0, lu), token);
        }
        return buffer.ToArray();
    }

    public async Task<Guid> VeroeffentlichenAsync(Guid user, Guid schule, Guid periodeID, Guid zuordnungID, string? bemerkung,
        string grund, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(grund) || grund.Trim().Length > 300) throw new InvalidOperationException("Indiquez le motif de cette édition (300 caractères maximum).");
        return await UnterrichtTransaktion.AusfuehrenAsync(factory, async db =>
        {
        var snapshot = await SnapshotAsync(db, user, schule, periodeID, zuordnungID, bemerkung, token);
        var klasse = await db.Set<SchuelerJahresZuordnung>().Where(x => x.Id == zuordnungID).Select(x => x.KlasseID).SingleAsync(token);
        if (await db.Set<BulletinAbschluss>().AnyAsync(x => x.PeriodeID == periodeID && x.KlasseID == klasse, token))
            throw new InvalidOperationException("Cette classe est définitivement publiée. Utilisez une rectification.");
        if (snapshot.Faecher.Count == 0 || snapshot.Durchschnitt is null) throw new InvalidOperationException("Complétez toutes les notes avant de publier le bulletin.");
        await DirektionAsync(db, user, schule, token);
        var bisher = await db.Set<BulletinAusgabe>().Where(x => x.SchulID == schule && x.PeriodeID == periodeID && x.JahresZuordnungID == zuordnungID).ToListAsync(token);
        if (bisher.Any(x => x.IstVeroeffentlicht)) throw new InvalidOperationException("Retirez d'abord la publication précédente en indiquant le motif.");
        var ausgabe = new BulletinAusgabe { Id = Guid.NewGuid(), SchulID = schule, PeriodeID = periodeID, JahresZuordnungID = zuordnungID,
            Version = bisher.Select(x => x.Version).DefaultIfEmpty(0).Max() + 1, ErstelltVonID = user, IstVeroeffentlicht = true,
            AenderungsGrund = grund.Trim(), SnapshotJson = JsonSerializer.Serialize(snapshot) };
        db.Add(ausgabe);
        await db.SaveChangesAsync(token);
        return ausgabe.Id;
        }, token);
    }

    public async Task ZurueckziehenAsync(Guid user, Guid ausgabeID, string grund, CancellationToken token = default)
    {
        await UnterrichtTransaktion.AusfuehrenAsync(factory, async db =>
        {
        var ausgabe = await db.Set<BulletinAusgabe>().SingleOrDefaultAsync(x => x.Id == ausgabeID, token)
            ?? throw new InvalidOperationException("Ce bulletin est introuvable.");
        await DirektionAsync(db, user, ausgabe.SchulID, token);
        if (ausgabe.IstEndgueltig) throw new InvalidOperationException("Ce bulletin est définitif. Utilisez une rectification, sans retirer l'original.");
        if (string.IsNullOrWhiteSpace(grund) || grund.Length > 300) throw new InvalidOperationException("Indiquez le motif du retrait.");
        ausgabe.IstVeroeffentlicht = false;
        if (ausgabe.ZurueckgezogenAmUtc is not null) throw new InvalidOperationException("Cette édition a déjà été retirée.");
        ausgabe.RuecknahmeGrund = grund.Trim();
        ausgabe.ZurueckgezogenVonID = user;
        ausgabe.ZurueckgezogenAmUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(token);
        return true;
        }, token);
    }

    public async Task<List<BulletinListenZeile>> ListeAsync(Guid user, Guid schule, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await MitgliedAsync(db, user, schule, token);
        var direktion = await IstDirektionAsync(db, user, token);
        var schueler = await db.UserRoles.AnyAsync(x => x.UserId == user && db.Roles.Any(r => r.Id == x.RoleId && r.Name == Rollen.Schueler), token);
        return await db.Set<BulletinAusgabe>().AsNoTracking().Where(x => x.SchulID == schule && (direktion
            || (schueler && (x.IstVeroeffentlicht || x.ErsetztDurchID != null) && x.JahresZuordnung.SchuelerID == user)))
            .OrderByDescending(x => x.ErstelltAmUtc).Select(x => new BulletinListenZeile(x.Id, x.SchulID, x.Periode.Titel,
                x.JahresZuordnung.Schueler.FullName, x.Version, x.IstVeroeffentlicht, x.IstEndgueltig, x.ErsetztDurchID)).ToListAsync(token);
    }

    public async Task<BulletinSnapshot?> LesenAsync(Guid user, Guid ausgabeID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var ausgabe = await db.Set<BulletinAusgabe>().AsNoTracking().Include(x => x.JahresZuordnung).SingleOrDefaultAsync(x => x.Id == ausgabeID, token);
        if (ausgabe is null) return null;
        await MitgliedAsync(db, user, ausgabe.SchulID, token);
        var schueler = await db.UserRoles.AnyAsync(x => x.UserId == user
            && db.Roles.Any(r => r.Id == x.RoleId && r.Name == Rollen.Schueler), token);
        if (!await IstDirektionAsync(db, user, token) && (!schueler || (!ausgabe.IstVeroeffentlicht && ausgabe.ErsetztDurchID == null) || ausgabe.JahresZuordnung.SchuelerID != user))
            throw new UnauthorizedAccessException("Ce bulletin n'est pas accessible.");
        return JsonSerializer.Deserialize<BulletinSnapshot>(ausgabe.SnapshotJson);
    }

    public async Task<List<BulletinSnapshot>> SammelausgabeAsync(Guid user, Guid schule, Guid periode, Guid klasse, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await DirektionAsync(db, user, schule, token);
        var schuljahr = await db.Set<BewertungsPeriode>().AsNoTracking()
            .Where(x => x.Id == periode && x.SchulID == schule).Select(x => (Guid?)x.SchuljahrID).SingleOrDefaultAsync(token);
        if (schuljahr is null || !await db.Klassen.AnyAsync(x => x.Id == klasse && x.SchoolId == schule, token))
            throw new InvalidOperationException("Sélectionnez une classe et une période de cette école.");

        var snapshots = new List<BulletinSnapshot>();
        var schueler = new HashSet<Guid>();
        long zeichen = 0;
        // Nur gespeicherte Veröffentlichungen: keine Neuberechnung und kein implizites Publizieren.
        var ausgaben = db.Set<BulletinAusgabe>().AsNoTracking()
            .Where(x => x.SchulID == schule && x.PeriodeID == periode && x.IstVeroeffentlicht
                && x.JahresZuordnung.SchulID == schule && x.JahresZuordnung.SchuljahrID == schuljahr.Value
                && x.JahresZuordnung.KlasseID == klasse)
            .OrderBy(x => x.JahresZuordnungID).ThenByDescending(x => x.Version)
            .Select(x => new { x.JahresZuordnungID, x.SnapshotJson }).AsAsyncEnumerable();
        await foreach (var ausgabe in ausgaben.WithCancellation(token))
        {
            if (!schueler.Add(ausgabe.JahresZuordnungID)) continue;
            zeichen += ausgabe.SnapshotJson.Length;
            if (snapshots.Count >= 200 || zeichen > 32 * 1024 * 1024)
                throw new InvalidOperationException("Cette sélection est trop volumineuse (200 bulletins ou 32 Mo de données maximum). Téléchargez les bulletins individuellement.");
            snapshots.Add(JsonSerializer.Deserialize<BulletinSnapshot>(ausgabe.SnapshotJson)
                ?? throw new InvalidOperationException("Un bulletin enregistré est illisible. Aucun document n'a été exporté."));
        }
        if (snapshots.Count == 0)
            throw new InvalidOperationException("Aucun bulletin publié pour cette classe et cette période. Publiez d'abord les bulletins souhaités.");
        return snapshots.OrderBy(x => x.Schueler, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("fr-FR"), true))
            .ThenBy(x => x.Matrikelnummer, StringComparer.Ordinal).ToList();
    }

    private static bool GueltigeBloecke(string? reihenfolge)
    {
        var bloecke = reihenfolge?.Split(',') ?? [];
        return bloecke.Length == 5 && bloecke.Distinct().Count() == 5
            && bloecke.All(x => x is "Schueler" or "Noten" or "Ergebnisse" or "Bemerkung" or "Unterschriften");
    }

    private static Task<bool> IstDirektionAsync(ApplicationDbContext db, Guid user, CancellationToken token)
        => db.UserRoles.AnyAsync(x => x.UserId == user && db.Roles.Any(r => r.Id == x.RoleId && (r.Name == Rollen.SchulLeiter || r.Name == Rollen.Koordinator)), token);
    private static async Task MitgliedAsync(ApplicationDbContext db, Guid user, Guid schule, CancellationToken token)
    {
        if (!await new SkoulouApp.Core.Services.SchulZugangService(db).IstSchuleFreigegebenAsync(schule, token))
            throw new UnauthorizedAccessException(SkoulouApp.Core.Services.SchulZugangService.GesperrtHinweis);
        if (!await db.UserSchools.AnyAsync(x => x.AppUserId == user && x.SchoolId == schule && x.School.IsActive && !x.AppUser.IsBlocked, token))
            throw new UnauthorizedAccessException("Vous n'êtes pas autorisé pour cette école.");
    }
    private static async Task DirektionAsync(ApplicationDbContext db, Guid user, Guid schule, CancellationToken token)
    {
        await MitgliedAsync(db, user, schule, token);
        if (!await IstDirektionAsync(db, user, token)) throw new UnauthorizedAccessException("Cette action est réservée à la direction et aux coordinateurs.");
    }
}
