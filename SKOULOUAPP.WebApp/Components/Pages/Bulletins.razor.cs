using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.WebApp.Components.Shared;
using SkoulouApp.WebApp.Data;
using SkoulouApp.WebApp.Services;

namespace SkoulouApp.WebApp.Components.Pages;

public partial class Bulletins
{
    [Inject] private BulletinService Service { get; set; } = default!;
    [Inject] private BewertungsService Bewertungen { get; set; } = default!;
    [Inject] private UnterrichtsVerwaltungService Verwaltung { get; set; } = default!;
    [Inject] private IDbContextFactory<ApplicationDbContext> Factory { get; set; } = default!;
    [Inject] private AuthenticationStateProvider Auth { get; set; } = default!;
    [Inject] private ILogger<Bulletins> Logger { get; set; } = default!;
    [Inject] private BulletinPdfService Pdf { get; set; } = default!;
    [Inject] private AnwesenheitsService Anwesenheit { get; set; } = default!;
    [Inject] private SchulLogoService Logos { get; set; } = default!;
    private string? logoVorschau;
    private HashSet<Guid> vorlagenKlassen = [];
    private string klassenSuche = "";
    private bool vorlagenErsetzen;
    private IEnumerable<AuswahlEintrag> GefilterteVorlagenKlassen => (unterricht?.Klassen ?? [])
        .Where(x => x.Bezeichnung.Contains(klassenSuche, StringComparison.CurrentCultureIgnoreCase));
    private int VorhandeneVorlagen => modeles.Count(x => vorlagenKlassen.Contains(x.KlasseID) && (int)x.Art == art);
    private void VorlageKlasseWaehlen(Guid id, bool gewaehlt)
    {
        if (occupe) return;
        if (gewaehlt) vorlagenKlassen.Add(id); else vorlagenKlassen.Remove(id);
        vorlagenErsetzen = false;
    }
    private void VorlagenAuswahl(bool alle)
    {
        if (occupe) return;
        if (alle) foreach (var klasse in GefilterteVorlagenKlassen) vorlagenKlassen.Add(klasse.Id);
        else vorlagenKlassen.Clear();
        vorlagenErsetzen = false;
    }
    private Task MehrfachModeleAsync() => AktionAsync(async () =>
    {
        vorlage.SchulID = schulID; vorlage.Art = (BewertungsArt)art;
        var anzahl = await Service.VorlagenZuweisenAsync(userID, vorlage, vorlagenKlassen.ToArray(), vorlagenErsetzen, ComponentToken);
        modeles = await Service.VorlagenAsync(userID, schulID, ComponentToken);
        vorlagenErsetzen = false;
        VorschauVerwerfen();
        message = $"Le modèle a été appliqué à {anzahl} classe(s). Chaque classe conserve sa propre copie. Les bulletins publiés restent inchangés.";
    });
    private Task LogoHochladenAsync(Microsoft.AspNetCore.Components.Forms.InputFileChangeEventArgs args) => AktionAsync(async () =>
    {
        if (args.File.Size > SchulLogoPruefung.MaxBytes) throw new InvalidOperationException("Le logo ne doit pas dépasser 2 Mo.");
        await using var stream = args.File.OpenReadStream(SchulLogoPruefung.MaxBytes, ComponentToken);
        var bild = await Logos.HochladenAsync(userID, schulID, stream, ComponentToken);
        BildOptionen.Add(new(bild.Id, bild.DisplayName));
        vorlage.LogoAssetID = bild.Id;
        logoVorschau = bild.ImageUrl;
        VorschauVerwerfen();
        message = "Le logo a été importé. Enregistrez le modèle pour l'utiliser dans les prochains bulletins.";
    });
    private void LogoWaehlen(Guid? id) { vorlage.LogoAssetID = id; logoVorschau = id.HasValue ? $"/media-library/{id.Value}" : null; VorschauVerwerfen(); }
    private DateTime praesenzDatum = DateTime.Today;
    private int praesenzStatus, praesenzMinuten;
    private bool entschuldigt;
    private List<AnwesenheitsEintrag> praesenz = [];
    private static readonly List<ModernDropdownOption<int>> PraesenzOptionen = [new(0, "Présent"), new(1, "Absent"), new(2, "En retard")];
    private Task PraesenzLadenAsync() => AktionAsync(async () => { praesenz = await Anwesenheit.LadenAsync(userID, schulID, periodeID, ComponentToken); });
    private Task PraesenzSpeichernAsync() => AktionAsync(async () => {
        await Anwesenheit.SpeichernAsync(userID, periodeID, schuelerID, DateOnly.FromDateTime(praesenzDatum), (AnwesenheitsStatus)praesenzStatus,
            entschuldigt, praesenzMinuten, null, ComponentToken);
        praesenz = await Anwesenheit.LadenAsync(userID, schulID, periodeID, ComponentToken); message = "La présence a été enregistrée.";
    });
    private List<AuswahlEintrag> schulen = [];
    private UnterrichtsUebersicht? unterricht;
    private BewertungsUebersicht? bewertungen;
    private List<BulletinVorlage> modeles = [];
    private List<ModernDropdownOption<Guid?>> BildOptionen = [];
    private List<BulletinListenZeile> liste = [];
    private BulletinVorlage vorlage = new();
    private string? beispielPdf;

    /// <summary>Vorschau der aktuellen (auch ungespeicherten) Einstellungen mit einem fiktiven Schüler.</summary>
    private Task BeispielAsync() => AktionAsync(async () =>
    {
        vorlage.SchulID = schulID; vorlage.Art = (BewertungsArt)art;
        var beispiel = await Service.VorlagenBeispielAsync(userID, vorlage, vorlage.ZeigeJahresBilanz, ComponentToken);
        beispielPdf = "data:application/pdf;base64," + Convert.ToBase64String(Pdf.Erzeugen(beispiel));
    });
    private BulletinSnapshot? vorschau;
    private string? vorschauPdf;
    private void VorschauVerwerfen() { vorschau = null; vorschauPdf = null; }
    private void PeriodeWaehlen(Guid id) { AbschlussVerwerfen(); periodeID = id; praesenz = []; VorschauVerwerfen(); }
    private void SchuelerWaehlen(Guid id) { schuelerID = id; bemerkung = ""; grund = ""; VorschauVerwerfen(); }
    private static string BlockTitel(string block) => block switch { "Schueler" => "Identité de l'élève", "Noten" => "Tableau des notes",
        "Ergebnisse" => "Résultats et rang", "Bemerkung" => "Observation", _ => "Signatures" };
    private void VerschiebeBlock(int index, int delta)
    {
        var bloecke = vorlage.BlockReihenfolge.Split(',').ToList();
        if (occupe || index + delta < 0 || index + delta >= bloecke.Count) return;
        (bloecke[index], bloecke[index + delta]) = (bloecke[index + delta], bloecke[index]);
        vorlage.BlockReihenfolge = string.Join(',', bloecke);
    }
    private Guid userID, schulID, jahrID, periodeID, schuelerID;
    private Guid sammelKlasseID;
    private bool SammelDownloadMoeglich => !occupe && schulID != Guid.Empty && periodeID != Guid.Empty && sammelKlasseID != Guid.Empty;
    private string SammelDownloadUrl => $"/bulletins/sammelausgabe/{schulID}/{periodeID}/{sammelKlasseID}/pdf";
    private int art;
    private bool verwalter, occupe;
    private string message = "", bemerkung = "", grund = "";
    private static List<ModernDropdownOption<Guid>> Options(IEnumerable<AuswahlEintrag> items)
        => items.Select(x => new ModernDropdownOption<Guid>(x.Id, x.Bezeichnung)).ToList();
    private static readonly List<ModernDropdownOption<int>> Arten = [new(0, "Bulletin général"), new(1, "Trimestre"), new(2, "Examen blanc / test")];
    private List<ModernDropdownOption<Guid>> JahresOptionen => Options(unterricht?.Schuljahre.Select(x => new AuswahlEintrag(x.Id, x.Bezeichnung)) ?? []);
    private List<ModernDropdownOption<Guid>> PeriodenOptionen => Options(bewertungen?.Perioden.Select(x => new AuswahlEintrag(x.Id, x.Titel)) ?? []);
    private List<ModernDropdownOption<Guid>> SchuelerOptionen => Options(bewertungen?.Schueler.Select(x => new AuswahlEintrag(x.Id,
        x.Name + " · " + bewertungen.Faecher.FirstOrDefault(f => f.KlasseID == x.KlasseID)?.Klasse)) ?? []);
    protected override Task OnInitializedAsync() => AktionAsync(async () =>
    {
        var state = await Auth.GetAuthenticationStateAsync();
        if (!Guid.TryParse(state.User.FindFirstValue(ClaimTypes.NameIdentifier), out userID)) throw new UnauthorizedAccessException("Veuillez vous reconnecter.");
        verwalter = state.User.IsInRole(Rollen.SchulLeiter) || state.User.IsInRole(Rollen.Koordinator);
        await using var db = await Factory.CreateDbContextAsync(ComponentToken);
        schulen = await db.UserSchools.AsNoTracking().Where(x => x.AppUserId == userID && x.School.IsActive)
            .Select(x => new AuswahlEintrag(x.SchoolId, x.School.Name)).ToListAsync(ComponentToken);
        schulID = schulen.FirstOrDefault()?.Id ?? Guid.Empty;
        if (schulID != Guid.Empty) await LadenAsync();
    });
    private Task EcoleAsync(Guid id) => AktionAsync(async () => { vorlagenKlassen.Clear(); vorlagenErsetzen = false; klassenSuche = ""; schulID = id; jahrID = periodeID = schuelerID = sammelKlasseID = Guid.Empty; vorlage = new(); logoVorschau = null; praesenz = []; bemerkung = grund = ""; VorschauVerwerfen(); await LadenAsync(); });
    private Task JahrAsync(Guid id) => AktionAsync(async () => { jahrID = id; periodeID = schuelerID = Guid.Empty; praesenz = []; bemerkung = grund = ""; VorschauVerwerfen(); await BewertungenLadenAsync(); });
    private async Task LadenAsync()
    {
        AbschlussVerwerfen(); uebergaben = []; uebergabeAusgabeID = Guid.Empty; uebergabeVermerk = "";
        liste = await Service.ListeAsync(userID, schulID, ComponentToken);
        if (!verwalter) return;
        berichtigungen = await Service.BerichtigungenAsync(userID, schulID, ComponentToken);
        if (!berichtigungen.Any(x => x.Id == berichtigungID)) berichtigungID = Guid.Empty;
        unterricht = await Verwaltung.LadenAsync(userID, schulID, ComponentToken);
        modeles = await Service.VorlagenAsync(userID, schulID, ComponentToken);
        await using (var db = await Factory.CreateDbContextAsync(ComponentToken))
            BildOptionen = await db.BlobMediaAssets.AsNoTracking().Where(x => x.SchoolId == schulID
                && x.MediaType == Core.DataModels.Lessons.BlobMediaType.Image && (x.ContentType == "image/png" || x.ContentType == "image/jpeg"))
                .OrderBy(x => x.DisplayName).Select(x => new ModernDropdownOption<Guid?>(x.Id, x.DisplayName)).ToListAsync(ComponentToken);
        BildOptionen.Insert(0, new(null, "Sans image"));
        if (!unterricht.Schuljahre.Any(x => x.Id == jahrID)) jahrID = unterricht.Schuljahre.FirstOrDefault()?.Id ?? Guid.Empty;
        await BewertungenLadenAsync();
    }
    private async Task BewertungenLadenAsync()
    {
        AbschlussVerwerfen(); StammdatenVerwerfen(); berichtigungID = Guid.Empty; korrekturNoteID = Guid.Empty;
        bewertungen = jahrID == Guid.Empty ? null : await Bewertungen.LadenAsync(userID, schulID, jahrID, ComponentToken);
    }
    private void ChargerModele(BulletinVorlage m)
    {
        if (occupe) return;
        vorlagenKlassen.Clear(); vorlagenErsetzen = false;
        art = (int)m.Art;
        vorlage = new BulletinVorlage { SchulID = schulID, KlasseID = m.KlasseID, Art = m.Art, Name = m.Name,
            AkzentFarbe = m.AkzentFarbe, ZeigeKoeffizienten = m.ZeigeKoeffizienten, ZeigeRang = m.ZeigeRang,
            ZeigeVorperioden = m.ZeigeVorperioden, ZeigeUnterschriften = m.ZeigeUnterschriften, FussText = m.FussText, BlockReihenfolge = m.BlockReihenfolge,
            LogoAssetID = m.LogoAssetID, SchriftGroesse = m.SchriftGroesse, SeitenRand = m.SeitenRand,
            ZeigeLehrkraefte = m.ZeigeLehrkraefte, ZeigeAnwesenheit = m.ZeigeAnwesenheit,
            ZeigeTeilnoten = m.ZeigeTeilnoten, ZeigePunkte = m.ZeigePunkte, ZeigeFachRang = m.ZeigeFachRang, ZeigeAppreciation = m.ZeigeAppreciation,
            ZeigeSignaturSpalte = m.ZeigeSignaturSpalte, ZeigeFachGruppen = m.ZeigeFachGruppen, ZeigeKlassenStatistik = m.ZeigeKlassenStatistik,
            ZeigeMentionen = m.ZeigeMentionen, ZeigeJahresBilanz = m.ZeigeJahresBilanz, ZeigeFoto = m.ZeigeFoto, ZeigeNoteInWorten = m.ZeigeNoteInWorten };
        beispielPdf = null;
        LogoWaehlen(m.LogoAssetID);
    }
    private Task ModeleAsync() => AktionAsync(async () => { vorlage.SchulID = schulID; vorlage.Art = (BewertungsArt)art;
        await Service.VorlageSpeichernAsync(userID, vorlage, ComponentToken); modeles = await Service.VorlagenAsync(userID, schulID, ComponentToken); message = "Le modèle a été enregistré."; });
    private Task VorschauAsync() => AktionAsync(async () => { vorschau = await Service.VorschauAsync(userID, schulID, periodeID, schuelerID, bemerkung, ComponentToken);
        vorschauPdf = "data:application/pdf;base64," + Convert.ToBase64String(Pdf.Erzeugen(vorschau)); });
    private Task PublierAsync() => AktionAsync(async () => { await Service.VeroeffentlichenAsync(userID, schulID, periodeID, schuelerID, bemerkung, grund, ComponentToken); await LadenAsync(); message = "Le bulletin est publié."; });
    private Task RetirerAsync(Guid id) => AktionAsync(async () => { await Service.ZurueckziehenAsync(userID, id, grund, ComponentToken); await LadenAsync(); message = "La publication a été retirée. L'édition reste archivée."; });
    private async Task AktionAsync(Func<Task> action)
    {
        if (occupe) return; occupe = true; message = "";
        try { await action(); }
        catch (Exception ex) when (IstAbbruch(ex)) { }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { message = ex.Message; }
        catch (Exception ex) { Logger.LogError(ex, "Bulletinfehler für Schule {SchulID}", schulID); message = "Impossible de terminer l'opération. Veuillez réessayer."; }
        finally { occupe = false; }
    }
}
