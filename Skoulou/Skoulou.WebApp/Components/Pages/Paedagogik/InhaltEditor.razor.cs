using Microsoft.AspNetCore.Components;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Components.Shared;
using Skoulou.WebApp.Services.Paedagogik;

namespace Skoulou.WebApp.Components.Pages.Paedagogik;

public partial class InhaltEditor
{
    [Parameter] public InhaltsArt Art { get; set; }
    [Parameter] public Guid? Id { get; set; }

    /// <summary>Direktion: Lehrkraft, für die neu erfasst wird (« Saisir pour un enseignant »).</summary>
    [Parameter] public Guid? Pour { get; set; }

    [Inject] private InhaltService Inhalte { get; set; } = default!;

    private InhaltFormular? f;
    private IReadOnlyList<AuswahlOption<string>> kombiOptionen = [];
    private readonly EditorKontext kontext = new();
    private string? fehlerFeld;

    private bool medienOffen, formelOffen;
    private MedienArt medienArt;
    private RichTextFeld? zielFeld;
    private string? formelAnfang;

    private static readonly IReadOnlyList<AuswahlOption<InhaltsStatus>> statusOptionen =
        [new(InhaltsStatus.Brouillon, "Brouillon"), new(InhaltsStatus.Prete, "Prêt")];

    private string Basis => Art == InhaltsArt.Lecon ? "/enseignant/lecons" : "/enseignant/examens";
    private string ZurueckHref => f?.Id is { } id ? $"{Basis}/{id}" : Basis;

    private string Kopftitel => f?.Id is null
        ? (Art == InhaltsArt.Lecon ? "Nouvelle leçon" : "Nouvel examen")
        : $"Modifier « {f.Titel} »";

    private string? StufeFachSchluessel => f?.StufeId is { } s && f.FachId is { } fa ? $"{s}|{fa}" : null;

    private string? FeldFehler(string feld) => fehlerFeld == feld ? Fehlermeldung : null;

    protected override async Task InitialisierenAsync()
    {
        kontext.BildWaehlen = feld => { OeffneMedien(MedienArt.Bild, feld); return InvokeAsync(StateHasChanged); };
        kontext.FormelOeffnen = (feld, latex) =>
        {
            zielFeld = feld;
            formelAnfang = latex;
            formelOffen = true;
            return InvokeAsync(StateHasChanged);
        };

        var formular = Id is { } id ? await Inhalte.LadeAsync(Ich.KontoId, Art, id) : await Inhalte.NeuAsync(Ich.KontoId, Art, Pour);
        var optionen = await Inhalte.OptionenAsync(Ich.KontoId, formular.AutorId);
        var kombis = optionen.Kombinationen.Select(k => new AuswahlOption<string>($"{k.StufeId}|{k.FachId}", $"{k.Stufe} · {k.Fach}")).ToList();
        if (formular.StufeId is { } s && formular.FachId is { } fa && kombis.All(k => k.Wert != $"{s}|{fa}"))
            kombis.Insert(0, new AuswahlOption<string>($"{s}|{fa}", "Niveau et matière actuels"));
        kombiOptionen = kombis;
        f = formular;
    }

    private void StufeFachAsync(string? schluessel)
    {
        if (f is null || schluessel?.Split('|') is not [var s, var fa]) return;
        f.StufeId = Guid.Parse(s);
        f.FachId = Guid.Parse(fa);
    }

    private void OeffneMedien(MedienArt art, RichTextFeld? feld)
    {
        medienArt = art;
        zielFeld = feld;
        medienOffen = true;
    }

    private async Task MediumGewaehltAsync(MediumZeile m)
    {
        medienOffen = false;
        if (m.Art == MedienArt.Video && f is not null)
        {
            f.VideoMediumId = m.Id;
            f.VideoName = m.Dateiname;
        }
        else if (zielFeld is not null)
        {
            await zielFeld.BildEinfuegenAsync(m.Url);
        }
    }

    private async Task FormelAsync(string latex)
    {
        formelOffen = false;
        if (zielFeld is not null) await zielFeld.FormelEinfuegenAsync(latex);
    }

    private void BlockHinzufuegen()
        => f!.Bloecke.Add(new BlockFormular { Titel = null, Fragen = [FrageFormular.Neu()] });

    private void BlockVerschieben(int index, int richtung)
    {
        var ziel = index + richtung;
        if (ziel < 0 || ziel >= f!.Bloecke.Count) return;
        (f.Bloecke[index], f.Bloecke[ziel]) = (f.Bloecke[ziel], f.Bloecke[index]);
    }

    private async Task SpeichernAsync()
    {
        if (f is null) return;
        await kontext.AlleSichernAsync();
        var neu = f.Id is null;
        fehlerFeld = null;
        try
        {
            var id = await Inhalte.SpeichernAsync(Ich.KontoId, f);
            Fehlermeldung = null;
            Toast.ZeigeErfolg(Art == InhaltsArt.Lecon ? "La leçon est enregistrée." : "L'examen est enregistré.");
            if (neu) Navigation.NavigateTo($"{Basis}/{id}/modifier", replace: true);
            else f = await Inhalte.LadeAsync(Ich.KontoId, Art, id);
        }
        catch (FachFehler ex)
        {
            Fehlermeldung = ex.Message;
            fehlerFeld = ex.Feld;
            Toast.ZeigeFehler(ex.Message);
        }
        catch (KeinZugriffFehler ex)
        {
            Fehlermeldung = ex.Message;
            Toast.ZeigeFehler(ex.Message);
        }
    }
}
