using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.WebApp.Components.Shared;
using SkoulouApp.WebApp.Services;

namespace SkoulouApp.WebApp.Components.Pages;

public partial class Bulletins
{
    private BulletinKlassenPruefung? klassenPruefung;
    private BerichtigungsPruefung? berichtigungsPruefung;
    private bool endgueltigBestaetigt, berichtigungBestaetigt;
    private List<BulletinBerichtigung> berichtigungen = [];
    private Guid berichtigungID, korrekturNoteID;
    private decimal korrekturPunkte;
    private string? korrekturBemerkung;
    private Guid stammdatenSchuelerID;
    private string stammdatenName = "";
    private string? stammdatenMatrikel, stammdatenGeburtsort;
    private DateTime? stammdatenGeburtsdatum;
    private bool stammdatenGeladen;
    private string uebergabeVermerk = "";
    private List<BulletinUebergabe> uebergaben = [];
    private Guid uebergabeAusgabeID;
    private void AbschlussVerwerfen()
    {
        klassenPruefung = null; berichtigungsPruefung = null;
        endgueltigBestaetigt = berichtigungBestaetigt = false;
    }
    private async Task AbschlussKlasse(Guid id) { sammelKlasseID = id; AbschlussVerwerfen(); await ListeLadenSicherAsync(); }
    private Task KlassePruefenAsync() => AktionAsync(async () =>
    {
        AbschlussVerwerfen();
        klassenPruefung = await Service.KlassePruefenAsync(userID, schulID, periodeID, sammelKlasseID, ComponentToken);
    });
    private Task KlassePublizierenAsync() => AktionAsync(async () =>
    {
        if (klassenPruefung is null) return;
        var anzahl = await Service.KlasseEndgueltigVeroeffentlichenAsync(userID, schulID, periodeID, sammelKlasseID,
            klassenPruefung.Hash, endgueltigBestaetigt, ComponentToken);
        AbschlussVerwerfen(); await LadenAsync(); message = $"Publication définitive : {anzahl} bulletins. Toute correction nécessite une rectification.";
    });
    private Task BerichtigungStartenAsync() => AktionAsync(async () =>
    {
        StammdatenVerwerfen();
        berichtigungID = await Service.BerichtigungStartenAsync(userID, schulID, periodeID, sammelKlasseID, grund, ComponentToken);
        berichtigungen = await Service.BerichtigungenAsync(userID, schulID, ComponentToken);
        AbschlussVerwerfen(); message = "Le brouillon de rectification est enregistré. Les originaux restent valides jusqu'à la confirmation finale.";
    });
    private void BerichtigungWaehlen(Guid id) { berichtigungID = id; korrekturNoteID = Guid.Empty; StammdatenVerwerfen(); AbschlussVerwerfen(); }
    private void StammdatenVerwerfen() { stammdatenSchuelerID = Guid.Empty; stammdatenGeladen = false; stammdatenName = ""; stammdatenMatrikel = stammdatenGeburtsort = null; stammdatenGeburtsdatum = null; }
    private List<ModernDropdownOption<Guid>> BerichtigungsSchueler => (bewertungen?.Schueler ?? [])
        .Where(x => x.KlasseID == berichtigungen.FirstOrDefault(b => b.Id == berichtigungID && b.SchuljahrID == jahrID)?.KlasseID)
        .Select(x => new ModernDropdownOption<Guid>(x.Id, x.Name)).ToList();
    private Task StammdatenWaehlenAsync(Guid id) => AktionAsync(async () =>
    {
        StammdatenVerwerfen(); AbschlussVerwerfen();
        if (id == Guid.Empty) return;
        var s = await Service.BerichtigungsStammdatenLadenAsync(userID, berichtigungID, id, ComponentToken);
        stammdatenSchuelerID = id; stammdatenName = s.Name; stammdatenMatrikel = s.Matrikelnummer;
        stammdatenGeburtsort = s.Geburtsort; stammdatenGeburtsdatum = s.Geburtsdatum?.ToDateTime(TimeOnly.MinValue); stammdatenGeladen = true;
    });
    private Task StammdatenSpeichernAsync() => AktionAsync(async () =>
    {
        if (!stammdatenGeladen) return;
        await Service.BerichtigungsStammdatenAsync(userID, berichtigungID,
            new(stammdatenSchuelerID, stammdatenName, stammdatenMatrikel, stammdatenGeburtsort,
                stammdatenGeburtsdatum is { } datum ? DateOnly.FromDateTime(datum) : null), ComponentToken);
        AbschlussVerwerfen(); message = "Identité enregistrée dans le brouillon uniquement. Calculez les bulletins concernés avant de confirmer.";
    });
    private List<ModernDropdownOption<Guid>> BerichtigungsNoten => (bewertungen?.Noten ?? [])
        .Where(x => bewertungen!.Schueler.Any(s => s.Id == x.JahresZuordnungID && s.KlasseID == berichtigungen.FirstOrDefault(b => b.Id == berichtigungID)?.KlasseID))
        .Select(x => new ModernDropdownOption<Guid>(x.Id, $"{bewertungen!.Schueler.First(s => s.Id == x.JahresZuordnungID).Name} · {bewertungen.Faecher.First(f => f.Id == x.FachBewertungID).Fach} · {bewertungen.Perioden.First(p => p.Id == bewertungen.Bestandteile.First(b => b.Id == x.BestandteilID).PeriodeID).Titel} · {bewertungen.Bestandteile.First(b => b.Id == x.BestandteilID).Bezeichnung} · {x.Punkte:0.##}"))
        .ToList();
    private Task KorrekturSpeichernAsync() => AktionAsync(async () =>
    {
        await Service.BerichtigungsNoteAsync(userID, berichtigungID, korrekturNoteID, korrekturPunkte, korrekturBemerkung, ComponentToken);
        AbschlussVerwerfen(); message = "Correction enregistrée dans le brouillon uniquement.";
    });
    private Task WirkungPruefenAsync() => AktionAsync(async () =>
    {
        AbschlussVerwerfen(); berichtigungsPruefung = await Service.BerichtigungPruefenAsync(userID, berichtigungID, ComponentToken);
    });
    private Task KorrekturFreigebenAsync() => AktionAsync(async () =>
    {
        if (berichtigungsPruefung is null) return;
        var anzahl = await Service.BerichtigungFreigebenAsync(userID, berichtigungID, berichtigungsPruefung.Hash, berichtigungBestaetigt, ComponentToken);
        berichtigungID = Guid.Empty; AbschlussVerwerfen(); await LadenAsync();
        message = $"{anzahl} bulletin(s) rectificatif(s) publié(s). Informez les familles et organisez l'échange des exemplaires signés ; consignez chaque contact et remise.";
    });
    private Task KorrekturAbbrechenAsync() => AktionAsync(async () =>
    {
        await Service.BerichtigungAbbrechenAsync(userID, berichtigungID, ComponentToken); berichtigungID = Guid.Empty;
        AbschlussVerwerfen(); await LadenAsync(); message = "Brouillon abandonné, aucune note ni publication modifiée.";
    });
    private Task UebergabeAsync(Guid id, bool abholung) => AktionAsync(async () =>
    {
        await Service.UebergabeSpeichernAsync(userID, id, abholung, uebergabeVermerk, ComponentToken);
        uebergabeAusgabeID = id; uebergaben = await Service.UebergabenAsync(userID, id, ComponentToken);
        uebergabeVermerk = ""; message = "Événement enregistré. Aucun message n'est envoyé automatiquement.";
    });
    private Task UebergabenLadenAsync(Guid id) => AktionAsync(async () => { uebergabeAusgabeID = id; uebergaben = await Service.UebergabenAsync(userID, id, ComponentToken); });
    private void ZeigeAbschlussVorschau(BulletinSnapshot snapshot) { vorschau = snapshot; vorschauPdf = "data:application/pdf;base64," + Convert.ToBase64String(Pdf.Erzeugen(snapshot with { PruefToken = null, DokumentNummer = "BROUILLON — NON VALIDE", DokumentDatumUtc = null })); }
}
