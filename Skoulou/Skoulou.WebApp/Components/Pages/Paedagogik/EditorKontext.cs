namespace Skoulou.WebApp.Components.Pages.Paedagogik;

/// <summary>
/// Verbindet die Quill-Felder eines Editors mit den Dialogen der Seite (Mediathek, Formeln) und
/// erlaubt, vor dem Speichern alle Felder zu sichern.
/// </summary>
public sealed class EditorKontext
{
    private readonly HashSet<RichTextFeld> felder = [];

    public Func<RichTextFeld, Task>? BildWaehlen { get; set; }
    public Func<RichTextFeld, string?, Task>? FormelOeffnen { get; set; }

    public void Anmelden(RichTextFeld feld) => felder.Add(feld);
    public void Abmelden(RichTextFeld feld) => felder.Remove(feld);

    public Task AlleSichernAsync() => Task.WhenAll(felder.ToList().Select(f => f.SichernAsync()));
}
