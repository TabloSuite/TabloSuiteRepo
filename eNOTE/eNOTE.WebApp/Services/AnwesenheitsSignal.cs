namespace ENote.WebApp.Services;

/// <summary>
/// Meldet Änderungen an den anwesenden Kunden und an den Tickets « en attente » eines Ladens an alle offenen Ansichten
/// (Empfang, Behandlungsraum, Kasse) im selben Serverprozess. Ergänzt durch periodisches Neuladen.
/// Auch der Kassenabschluss meldet sich hier, damit eine zweite Kasse desselben Ladens ihre Ansicht nachzieht.
/// </summary>
public sealed class AnwesenheitsSignal
{
    public event Action<Guid>? Geaendert;

    /// <summary>
    /// Bargeld/Kassenstand eines Ladens hat sich geändert (Verkauf, Erstattung, Retrait, Einlage, Abschluss): nur die
    /// Kassen hören zu und laden « En caisse » bzw. einen offenen Abschlussdialog aus der Datenbank (W3).
    /// </summary>
    public event Action<Guid>? KasseGeaendert;

    /// <summary>
    /// Die offene Kasse eines Ladens wurde auf ein anderes Gerät geholt (« Reprendre la caisse ici »): das bisherige
    /// Gerät sperrt sich sofort (eine Kasse je Laden, an genau einem Gerät).
    /// </summary>
    public event Action<Guid>? KassenGeraetGewechselt;

    public void MeldeKassenGeraet(Guid salonId)
    {
        try { KassenGeraetGewechselt?.Invoke(salonId); }
        catch { /* eine Ansicht darf die anderen nicht stören */ }
    }

    public void Melde(Guid salonId)
    {
        try { Geaendert?.Invoke(salonId); }
        catch { /* eine Ansicht darf die anderen nicht stören */ }
    }

    public void MeldeKasse(Guid salonId)
    {
        try { KasseGeaendert?.Invoke(salonId); }
        catch { /* eine Ansicht darf die anderen nicht stören */ }
    }
}
