namespace ENote.WebApp.Services;

/// <summary>
/// Meldet Änderungen an den anwesenden Kunden eines Ladens an alle offenen Ansichten
/// (Empfang, Behandlungsraum, Kasse) im selben Serverprozess. Ergänzt durch periodisches Neuladen.
/// </summary>
public sealed class AnwesenheitsSignal
{
    public event Action<Guid>? Geaendert;

    public void Melde(Guid salonId)
    {
        try { Geaendert?.Invoke(salonId); }
        catch { /* eine Ansicht darf die anderen nicht stören */ }
    }
}
