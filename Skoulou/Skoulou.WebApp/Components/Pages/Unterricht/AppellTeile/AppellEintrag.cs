using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.WebApp.Components.Pages.Unterricht.AppellTeile;

/// <summary>Bearbeitbare Zeile des Appells (wird beim Speichern zu <see cref="AppellEingabe"/>).</summary>
public sealed class AppellEintrag(AppellZeile z)
{
    public Guid EinschreibungId { get; } = z.EinschreibungId;
    public Guid SchuelerId { get; } = z.SchuelerId;
    public string Name { get; } = z.Name;
    public string? Matrikel { get; } = z.Matrikel;
    public AnwesenheitsStatus Status { get; set; } = z.Status;
    public int? Minuten { get; set; } = z.Minuten;
    public bool Entschuldigt { get; set; } = z.Entschuldigt;
    public string? Bemerkung { get; set; } = z.Bemerkung;

    public AppellEingabe AlsEingabe() => new(EinschreibungId, Status, Minuten, Entschuldigt, Bemerkung);
}
