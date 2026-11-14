namespace ENote.WebApp.Services;

/// <summary>
/// Meldet Änderungen an den anwesenden Kunden und an den Tickets « en attente » eines Ladens an alle offenen Ansichten
/// (Empfang, Behandlungsraum, Kasse) im selben Serverprozess. Ergänzt durch periodisches Neuladen.
/// Auch der Kassenabschluss meldet sich hier, damit eine zweite Kasse desselben Ladens ihre Ansicht nachzieht.
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
