namespace ENote.WebApp.Services;

/// <summary>
/// Circuit-weiter Zustand der Besitzer-PIN-Sperre am gemeinsamen Tablet.
/// Nach der Anmeldung ist die Verwaltung standardmäßig GESPERRT (gesetzt eine
/// Besitzer-PIN ist konfiguriert), damit der Besitzer das nicht vergisst. Sie wird
/// erst durch die korrekte Besitzer-PIN freigeschaltet und beim Kassenabschluss
/// automatisch wieder gesperrt.
/// </summary>
public class BesitzerPinState
{
    public event Action? OnChange;

    /// <summary>true = Verwaltung freigeschaltet (Besitzer-PIN korrekt eingegeben).</summary>
    public bool VerwaltungFreigeschaltet { get; private set; }

    /// <summary>Freischaltet die Verwaltung nach korrekter Besitzer-PIN.</summary>
    public void Freischalten()
    {
        VerwaltungFreigeschaltet = true;
        OnChange?.Invoke();
    }

    /// <summary>Sperrt die Verwaltung wieder (manuell oder beim Kassenabschluss).</summary>
    public void Sperren()
    {
        VerwaltungFreigeschaltet = false;
        OnChange?.Invoke();
    }
}
