using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Organisation;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Organisation.Import;

/// <summary>Fehler einer Importzeile (Zeilennummer wie in Excel).</summary>
public sealed record ImportFehler(int Zeile, string Person, string Meldung);

/// <summary>Zeile der Vorschau: wer angelegt würde (Identifiant ggf. vorgeschlagen) oder warum nicht.</summary>
public sealed record ImportZeile(int Nr, string Name, string? Identifiant, bool Vorgeschlagen, string? Zusatz, bool Gueltig);

/// <summary>Vorschau eines Imports mit VOLLSTÄNDIGER Fehlerliste; gültige Zeilen können importiert werden.</summary>
public sealed record ImportVorschau(Guid Id, ImportArt Art, string Datei, string? Jahr, IReadOnlyList<ImportZeile> Zeilen, IReadOnlyList<ImportFehler> Fehler,
    DateOnly? JahrBeginn = null, DateOnly? JahrEnde = null, DateOnly? VorgabeBeginn = null)
{
    public int Gueltig => Zeilen.Count(z => z.Gueltig);
    public int Ungueltig => Zeilen.Count - Gueltig;
}

/// <summary>Ergebnis des Imports: angelegte Konten (Zugangsliste) und ausgelassene Zeilen.</summary>
public sealed record ImportErgebnis(int Angelegt, int Ausgelassen, ZugangsStapel Zugaenge);

/// <summary>
/// XLSX-Import für Schüler (mit Klasse), Lehrkräfte und Eltern (mit Matrikel der Kinder): Vorlage, Vorschau mit
/// vollständiger Fehlerliste (als Datei exportierbar), Import der gültigen Zeilen in einer Transaktion, danach
/// Zugangsliste. Willkommensmail nur an Konten mit Adresse (Bauplan §11.12).
/// </summary>
public sealed class ImportService(
    IDbContextFactory<SkoulouDbContext> dbFactory, Zugriff zugriff, AuditService audit, KontoFabrik fabrik, ZugangsAblage ablage,
    OrgMails mails, NummernkreisService nummern, TimeProvider zeit)
{
    /// <summary>Zwischengespeicherte Datei für die Bestätigung (nur für den Besitzer, 30 Minuten).</summary>
    private sealed record ImportSitzung(ImportArt Art, Guid? JahrId, string Datei, IReadOnlyList<RohZeile> Zeilen, IReadOnlyList<ImportFehler> Fehler);

    /// <summary>Geprüfte Zeile (nur intern, für die Anlage).</summary>
    private sealed record GepruefteZeile(int Nr, KontoDaten Konto, string? Identifiant, bool Vorgeschlagen, Guid? KlasseId, string? KlasseName,
        SchuelerService.GeprueftDaten? Schueler, bool Wiederholer, IReadOnlyList<(Guid Id, string Name)> Kinder, string Beziehung, List<string> Fehler);

    public async Task<byte[]> VorlageAsync(Guid akteurId, ImportArt art, Guid? jahrId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, schreiben: false, ct);
        var klassen = new List<string>();
        if (art == ImportArt.Eleves && jahrId is { } jid)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await OrgAbfragen.SchuljahrAsync(db, a, jid, ct);
            klassen = await db.Klassen.AsNoTracking().Where(k => k.SchuljahrId == jid && k.Aktiv).OrderBy(k => k.Name).Select(k => k.Name).ToListAsync(ct);
        }
        return ImportVorlage.Erzeuge(art, klassen);
    }

    /// <summary>Datei lesen und prüfen (noch nichts anlegen). Die Vorschau bleibt 30 Minuten für « Importer » gültig.</summary>
    public async Task<ImportVorschau> VorschauAsync(Guid akteurId, ImportArt art, Guid? jahrId, Stream datei, string? dateiname, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        var inhalt = await LeseAsync(datei, ct);
        var zeilen = ImportVorlage.Lese(inhalt, art);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var jahr = await JahrAsync(db, a, art, jahrId, ct);
        var geprueft = await PruefenAsync(db, a, art, jahr, zeilen, ct);
        var fehler = FehlerListe(geprueft);
        var name = Path.GetFileName(dateiname ?? "import.xlsx");
        var id = ablage.AblegenObjekt(a.Id, new ImportSitzung(art, jahr?.Id, name, zeilen, fehler));
        var vorgabe = jahr is null ? (DateOnly?)null : await EinschreibungService.StandardAsync(db, jahr, new SchulZeit(zeit).Heute, ct);
        return new ImportVorschau(id, art, name, jahr?.Bezeichnung, geprueft.Select(Zeile).ToList(), fehler, jahr?.Beginn, jahr?.Ende, vorgabe);
    }

    /// <summary>Fehlerliste der Vorschau als Excel-Datei.</summary>
    public async Task<byte[]> FehlerDateiAsync(Guid akteurId, Guid vorschauId, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, schreiben: false, ct);
        var sitzung = ablage.HolenObjekt<ImportSitzung>(vorschauId, a.Id) ?? throw new FachFehler(MeldungAbgelaufen);
        return ImportVorlage.FehlerDatei(sitzung.Fehler);
    }

    public const string MeldungAbgelaufen = "Cet aperçu a expiré : chargez à nouveau le fichier.";

    /// <summary>
    /// Gültige Zeilen anlegen (erneut geprüft, eine Transaktion); fehlerhafte Zeilen werden ausgelassen. Schüler mit Klasse
    /// sind ab <paramref name="beginn"/> eingeschrieben (Standard: Beginn des Schuljahres, <see cref="EinschreibungsBeginn"/>).
    /// </summary>
    public async Task<ImportErgebnis> ImportierenAsync(Guid akteurId, Guid vorschauId, DateOnly? beginn = null, CancellationToken ct = default)
    {
        var a = await zugriff.SchuleAsync(akteurId, Faehigkeit.KontenVerwalten, ct: ct);
        var sitzung = ablage.HolenObjekt<ImportSitzung>(vorschauId, a.Id) ?? throw new FachFehler(MeldungAbgelaufen);
        var heute = new SchulZeit(zeit).Heute;

        var (zugaenge, ausgelassen, schulName, empfaenger) = await dbFactory.TransaktionAsync(async db =>
        {
            var jahr = await JahrAsync(db, a, sitzung.Art, sitzung.JahrId, ct);
            var geprueft = await PruefenAsync(db, a, sitzung.Art, jahr, sitzung.Zeilen, ct);
            var gueltig = geprueft.Where(z => z.Fehler.Count == 0).ToList();
            if (gueltig.Count == 0) throw new FachFehler("Aucune ligne valide à importer : corrigez le fichier puis chargez-le à nouveau.");
            var schule = await db.Schulen.AsNoTracking().FirstAsync(s => s.Id == a.SchuleId, ct);
            var rolle = sitzung.Art switch { ImportArt.Eleves => Rollen.Schueler, ImportArt.Enseignants => Rollen.Lehrer, _ => Rollen.Eltern };
            var reserviert = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matrikelReserviert = new HashSet<string>(StringComparer.Ordinal);
            foreach (var z in gueltig)
            {
                if (z.Identifiant is not null && !z.Vorgeschlagen) reserviert.Add(z.Identifiant);
                if (z.Schueler?.Matrikel is { } m) matrikelReserviert.Add(m);
            }

            var liste = new List<Zugang>();
            var mailEmpfaenger = new List<MailEmpfaenger>();
            DateOnly? standardBeginn = null; // einmal je Import (Startphase der Schule, 21.11.2026)
            foreach (var z in gueltig)
            {
                var identifiant = z.Identifiant is not null && !z.Vorgeschlagen
                    ? z.Identifiant
                    : await fabrik.VorschlagAsync(db, z.Konto.Vorname, z.Konto.Nachname, reserviert, ct);
                reserviert.Add(identifiant);
                var (konto, passwort) = await fabrik.AnlegenAsync(db, z.Konto, identifiant, rolle, a.SchuleId, a.Id, ct: ct);
                string? zusatz = null;
                if (z.Schueler is { } s)
                {
                    var matrikel = s.Matrikel ?? await SchuelerService.NaechsteMatrikelAsync(db, nummern, schule, (jahr?.Beginn ?? heute).Year, matrikelReserviert, ct);
                    matrikelReserviert.Add(matrikel);
                    db.SchuelerProfile.Add(SchuelerService.NeuesProfil(konto.Id, a.SchuleId, matrikel, s));
                    if (z.KlasseId is { } klasseId && jahr is not null)
                    {
                        var von = beginn is { } angegeben
                            ? await EinschreibungService.BeginnAsync(db, jahr, angegeben, new SchulZeit(zeit).Heute, ct)
                            : standardBeginn ??= await EinschreibungService.StandardAsync(db, jahr, new SchulZeit(zeit).Heute, ct);
                        db.Einschreibungen.Add(new Einschreibung
                        {
                            SchuleId = a.SchuleId, SchuelerId = konto.Id, SchuljahrId = jahr.Id, KlasseId = klasseId, Von = von,
                            Status = EinschreibungsStatus.Active, IstWiederholer = z.Wiederholer
                        });
                    }
                    zusatz = z.KlasseName is null ? $"Matricule {matrikel}" : $"{z.KlasseName} · {matrikel}";
                }
                foreach (var (kindId, kindName) in z.Kinder)
                    db.ElternKinder.Add(new ElternKind { SchuleId = a.SchuleId, ElternId = konto.Id, SchuelerId = kindId, Beziehung = z.Beziehung });
                if (z.Kinder.Count > 0) zusatz = $"{z.Beziehung} de {string.Join(", ", z.Kinder.Select(k => k.Name))}";

                audit.Erfasse(db, a.Id, a.SchuleId, AuditAktionen.KontoAngelegt, nameof(Konto), konto.Id,
                    $"{Rollen.Bezeichnung(rolle)} {konto.VollerName} importé(e) (identifiant {identifiant}).");
                liste.Add(new Zugang(konto.Id, konto.VollerName, rolle, identifiant, passwort, zusatz));
                mailEmpfaenger.Add(new MailEmpfaenger(z.Konto.Email, z.Konto.Vorname, identifiant, rolle));
            }
            audit.Erfasse(db, a.Id, a.SchuleId, OrgAudit.ImportAusgefuehrt, nameof(Schule), a.SchuleId,
                $"Import de {liste.Count} {ImportVorlage.Bezeichnung(sitzung.Art)} depuis « {sitzung.Datei} » ({geprueft.Count - gueltig.Count} ligne(s) écartée(s)).");
            await db.SaveChangesAsync(ct);
            return (liste, geprueft.Count - gueltig.Count, schule.Name, mailEmpfaenger);
        }, ct, DbArbeit.KontoVersuche);

        ablage.Entfernen<ImportSitzung>(vorschauId);
        foreach (var m in empfaenger) await mails.WillkommenAsync(m, schulName, ct);
        return new ImportErgebnis(zugaenge.Count, ausgelassen, ablage.Ablegen(a.Id, a.SchuleId, zugaenge));
    }

    // ---------------- Prüfung ----------------

    private static async Task<byte[]> LeseAsync(Stream datei, CancellationToken ct)
    {
        using var puffer = new MemoryStream();
        var block = new byte[81920];
        int n;
        while ((n = await datei.ReadAsync(block, ct)) > 0)
        {
            puffer.Write(block, 0, n);
            if (puffer.Length > ImportVorlage.MaxBytes) throw new FachFehler("Le fichier est trop lourd (2 Mo au maximum).");
        }
        if (puffer.Length == 0) throw new FachFehler("Le fichier est vide.");
        var inhalt = puffer.ToArray();
        // XLSX = ZIP (« PK »); ein altes .xls oder CSV wird verständlich abgewiesen.
        if (inhalt.Length < 4 || inhalt[0] != 0x50 || inhalt[1] != 0x4B)
            throw new FachFehler("Seuls les fichiers Excel au format .xlsx sont acceptés (enregistrez votre fichier en .xlsx).");
        return inhalt;
    }

    private static async Task<Schuljahr?> JahrAsync(SkoulouDbContext db, SchulAkteur a, ImportArt art, Guid? jahrId, CancellationToken ct)
    {
        if (art != ImportArt.Eleves) return null;
        if (jahrId is not { } id) throw new FachFehler("Choisissez d'abord l'année scolaire (en haut de la page).");
        var jahr = await OrgAbfragen.SchuljahrAsync(db, a, id, ct);
        OrgAbfragen.PruefeOffen(jahr);
        return jahr;
    }

    private static ImportZeile Zeile(GepruefteZeile z)
    {
        var zusatz = z.KlasseName ?? (z.Kinder.Count > 0 ? string.Join(", ", z.Kinder.Select(k => k.Name)) : null);
        return new ImportZeile(z.Nr, $"{z.Konto.Vorname} {z.Konto.Nachname}".Trim(), z.Identifiant, z.Vorgeschlagen, zusatz, z.Fehler.Count == 0);
    }

    private static List<ImportFehler> FehlerListe(IEnumerable<GepruefteZeile> zeilen)
        => zeilen.SelectMany(z => z.Fehler.Select(f => new ImportFehler(z.Nr, $"{z.Konto.Vorname} {z.Konto.Nachname}".Trim(), f))).ToList();

    private async Task<List<GepruefteZeile>> PruefenAsync(SkoulouDbContext db, SchulAkteur a, ImportArt art, Schuljahr? jahr,
        IReadOnlyList<RohZeile> zeilen, CancellationToken ct)
    {
        var jahrId = jahr?.Id;
        var klassen = await db.Klassen.AsNoTracking().Where(k => jahrId != null && k.SchuljahrId == jahrId && k.Aktiv)
            .Select(k => new { k.Id, k.Name }).ToListAsync(ct);
        var klassenNachSchluessel = klassen.GroupBy(k => TextNormalisierung.Schluessel(k.Name)).ToDictionary(g => g.Key, g => g.First());

        var matrikelInDatei = zeilen.Select(z => MatrikelRegel.Normalisiere(z["matricule"])).OfType<string>().ToList();
        var kinderMatrikel = zeilen.SelectMany(z => Liste(z["matricules"])).Select(MatrikelRegel.Normalisiere).OfType<string>().ToList();
        var alleMatrikel = matrikelInDatei.Concat(kinderMatrikel).Distinct().ToList();
        var profile = await (from p in db.SchuelerProfile.AsNoTracking()
                             join k in db.Users on p.KontoId equals k.Id
                             where p.SchuleId == a.SchuleId && p.Matrikel != null && alleMatrikel.Contains(p.Matrikel!)
                             select new { p.Matrikel, k.Id, k.Vorname, k.Nachname, k.Status }).ToListAsync(ct);

        // Doppelte Schüler (Klicktest 21.11.2026: dieselbe Datei zweimal importiert bzw. zweimal dieselbe Zeile → doppelte Konten,
        // doppelt berechnet): gleicher Name in derselben Klasse (und gleiches Geburtsdatum, falls angegeben).
        var vorhandene = art == ImportArt.Eleves && jahrId is not null
            ? await (from e in db.Einschreibungen.AsNoTracking()
                     join k in db.Users on e.SchuelerId equals k.Id
                     where e.SchuljahrId == jahrId && e.Status == EinschreibungsStatus.Active && k.SchuleId == a.SchuleId
                     select new { k.Vorname, k.Nachname, e.KlasseId, k.Geburtsdatum }).ToListAsync(ct)
            : [];
        var personenInDatei = new Dictionary<string, int>(StringComparer.Ordinal);
        var identifiantsInDatei = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var matrikelGesehen = new Dictionary<string, int>(StringComparer.Ordinal);
        var ergebnis = new List<GepruefteZeile>(zeilen.Count);
        var reserviert = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Ausdrücklich angegebene Identifiants zuerst reservieren, damit Vorschläge ihnen nicht zuvorkommen.
        foreach (var z in zeilen)
            if (!string.IsNullOrWhiteSpace(z["identifiant"]) && IdentifiantRegel.Normalisiere(z["identifiant"]) is { Length: > 0 } id)
                reserviert.Add(id);

        foreach (var z in zeilen)
        {
            var fehler = new List<string>();
            var vorname = Pruefe(fehler, () => Eingabe.Pflicht(z["prenom"], 100, "le prénom")) ?? string.Empty;
            var nachname = Pruefe(fehler, () => Eingabe.Pflicht(z["nom"], 100, "le nom")) ?? string.Empty;
            var email = Pruefe(fehler, () => Eingabe.Email(z["email"]));
            var telefon = Pruefe(fehler, () => Eingabe.Telefon(z["telephone"]));
            var geschlecht = GeschlechtAus(z["sexe"], fehler);

            // Identifiant: angegeben (Format, eindeutig in DB und Datei) oder Vorschlag.
            string? identifiant = null;
            var vorgeschlagen = false;
            if (!string.IsNullOrWhiteSpace(z["identifiant"]))
            {
                identifiant = IdentifiantRegel.Normalisiere(z["identifiant"]);
                if (!IdentifiantRegel.IstGueltig(identifiant)) fehler.Add($"Identifiant « {z["identifiant"]} » : {IdentifiantRegel.MeldungFormat}");
                else if (identifiantsInDatei.TryGetValue(identifiant, out var andere)) fehler.Add($"L'identifiant « {identifiant} » figure déjà à la ligne {andere}.");
                else if (await fabrik.IstVergebenAsync(db, identifiant, null, ct)) fehler.Add($"L'identifiant « {identifiant} » est déjà utilisé.");
                identifiantsInDatei.TryAdd(identifiant, z.Nr);
            }
            else if (vorname.Length > 0 && nachname.Length > 0)
            {
                identifiant = await fabrik.VorschlagAsync(db, vorname, nachname, reserviert, ct);
                reserviert.Add(identifiant);
                vorgeschlagen = true;
            }

            var konto = new KontoDaten(vorname, nachname, identifiant, email, telefon, geschlecht);
            Guid? klasseId = null;
            string? klasseName = null;
            SchuelerService.GeprueftDaten? schueler = null;
            var wiederholer = false;
            var kinder = new List<(Guid, string)>();
            var beziehung = "Autre";

            if (art == ImportArt.Eleves)
            {
                if (!string.IsNullOrWhiteSpace(z["classe"]))
                {
                    if (klassenNachSchluessel.TryGetValue(TextNormalisierung.Schluessel(z["classe"]), out var k)) { klasseId = k.Id; klasseName = k.Name; }
                    else fehler.Add($"Classe « {z["classe"]} » introuvable en {jahr?.Bezeichnung} (voir l'onglet « Classes » du modèle).");
                }
                var geburt = Datum(z["naissance"], fehler);
                var matrikel = MatrikelRegel.Normalisiere(z["matricule"]);
                if (matrikel is not null)
                {
                    if (!MatrikelRegel.IstGueltig(matrikel)) fehler.Add($"Matricule « {z["matricule"]} » : {MatrikelRegel.MeldungFormat}");
                    else if (matrikelGesehen.TryGetValue(matrikel, out var andere)) fehler.Add($"Le matricule {matrikel} figure déjà à la ligne {andere}.");
                    else if (profile.Any(p => p.Matrikel == matrikel)) fehler.Add($"Le matricule {matrikel} est déjà attribué à un autre élève.");
                    matrikelGesehen.TryAdd(matrikel, z.Nr);
                }
                wiederholer = JaNein(z["redoublant"], fehler);
                schueler = new SchuelerService.GeprueftDaten(konto with { Geburtsdatum = geburt },
                    Pruefe(fehler, () => Eingabe.Text(z["lieu"], 150, "le lieu de naissance")),
                    Pruefe(fehler, () => Eingabe.Text(z["nationalite"], 80, "la nationalité")), false, false,
                    Pruefe(fehler, () => Eingabe.Telefon(z["telparent"])), Pruefe(fehler, () => Eingabe.Email(z["emailparent"])), matrikel);
                konto = schueler.Konto;
            }
            else if (art == ImportArt.Parents)
            {
                beziehung = Pruefe(fehler, () => ElternService.PruefeBeziehung(z["lien"])) ?? "Autre";
                foreach (var roh in Liste(z["matricules"]))
                {
                    var m = MatrikelRegel.Normalisiere(roh);
                    var kind = profile.FirstOrDefault(p => p.Matrikel == m);
                    if (kind is null) fehler.Add($"Aucun élève de l'établissement n'a le matricule « {roh} ».");
                    else if (kind.Status == KontoStatus.Archive) fehler.Add($"L'élève {kind.Vorname} {kind.Nachname} ({m}) est archivé(e).");
                    else if (kinder.All(k => k.Item1 != kind.Id)) kinder.Add((kind.Id, $"{kind.Vorname} {kind.Nachname}".Trim()));
                }
                if (kinder.Count > ElternService.MaxKinder) fehler.Add($"Un compte parent peut être lié à {ElternService.MaxKinder} enfants au maximum.");
            }
            if (art == ImportArt.Eleves && schueler is not null && fehler.Count == 0
                && string.IsNullOrWhiteSpace(z["identifiant"]) && string.IsNullOrWhiteSpace(z["matricule"]))
            {
                var name = TextNormalisierung.Schluessel($"{vorname} {nachname}");
                var geburt = schueler.Konto.Geburtsdatum;
                var person = $"{name}|{klasseId}|{geburt}";
                if (personenInDatei.TryGetValue(person, out var erste))
                    fehler.Add($"« {vorname} {nachname} » figure déjà à la ligne {erste} (même nom, même classe).");
                else if (klasseId is not null && vorhandene.Any(v => v.KlasseId == klasseId && TextNormalisierung.Schluessel($"{v.Vorname} {v.Nachname}") == name
                             && (geburt is null || v.Geburtsdatum is null || v.Geburtsdatum == geburt)))
                    fehler.Add($"Un élève « {vorname} {nachname} » est déjà inscrit en {klasseName} : s'il s'agit d'un homonyme, ajoutez-le avec « Nouvel élève ».");
                else personenInDatei.TryAdd(person, z.Nr);
            }
            ergebnis.Add(new GepruefteZeile(z.Nr, konto, identifiant, vorgeschlagen, klasseId, klasseName, schueler, wiederholer, kinder, beziehung, fehler));
        }
        return ergebnis;
    }

    private static T? Pruefe<T>(List<string> fehler, Func<T?> pruefung)
    {
        try
        {
            return pruefung();
        }
        catch (FachFehler f)
        {
            fehler.Add(f.Message);
            return default;
        }
    }

    private static IEnumerable<string> Liste(string? text)
        => string.IsNullOrWhiteSpace(text) ? [] : text.Split([',', ';', '|', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static Geschlecht GeschlechtAus(string? text, List<string> fehler)
    {
        switch (TextNormalisierung.Schluessel(text))
        {
            case "": return Skoulou.Core.Entitaeten.Geschlecht.Divers;
            case "f" or "fille" or "feminin" or "femme" or "w": return Skoulou.Core.Entitaeten.Geschlecht.Weiblich;
            case "m" or "g" or "h" or "garcon" or "masculin" or "homme": return Skoulou.Core.Entitaeten.Geschlecht.Maennlich;
            default:
                fehler.Add($"Sexe « {text} » : indiquez F ou M.");
                return Skoulou.Core.Entitaeten.Geschlecht.Divers;
        }
    }

    private static bool JaNein(string? text, List<string> fehler)
    {
        switch (TextNormalisierung.Schluessel(text))
        {
            case "" or "non" or "n" or "0" or "faux" or "no": return false;
            case "oui" or "o" or "x" or "1" or "vrai" or "yes": return true;
            default:
                fehler.Add($"Redoublant « {text} » : indiquez oui ou non.");
                return false;
        }
    }

    private static readonly string[] DatumsFormate = ["dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "yyyy-MM-dd", "dd/MM/yy"];

    private DateOnly? Datum(string? text, List<string> fehler)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // Excel liefert Datumswerte in Textspalten (Vorlage) oder nach « Coller les valeurs » als Seriennummer (z. B. 41396).
        if (int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var serie) && serie is > 0 and < 100_000)
            text = DateOnly.FromDateTime(DateTime.FromOADate(serie)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!DateOnly.TryParseExact(text.Trim(), DatumsFormate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            fehler.Add($"Date de naissance « {text} » : écrivez-la au format jj/mm/aaaa.");
            return null;
        }
        if (d.Year < 1900 || d > new SchulZeit(zeit).Heute)
        {
            fehler.Add($"La date de naissance {SkoulouKultur.Datum(d)} n'est pas plausible.");
            return null;
        }
        return d;
    }
}
