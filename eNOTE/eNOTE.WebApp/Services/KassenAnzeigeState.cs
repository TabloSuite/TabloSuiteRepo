namespace ENote.WebApp.Services;

/// <summary>
/// Circuit-weiter Zustand der Kassenanzeige: merkt sich die aktive Sitzung und
/// ob die Kassenansicht minimiert ist (schwebender Button am Tablet).
/// </summary>
public class KassenAnzeigeState
{
    public event Action? OnChange;

    public Guid? AktiveSitzungId { get; private set; }
    public Guid? SalonId { get; private set; }
    public string? KassiererName { get; private set; }
    public bool IstMinimiert { get; private set; }
    public decimal LaufenderUmsatz { get; private set; }
    public int AnzahlVerkaeufe { get; private set; }

    public void SitzungGestartet(Guid sitzungId, Guid salonId, string kassiererName)
    {
        AktiveSitzungId = sitzungId;
        SalonId = salonId;
        KassiererName = kassiererName;
        IstMinimiert = false;
        LaufenderUmsatz = 0m;
        AnzahlVerkaeufe = 0;
        OnChange?.Invoke();
    }

    public void VerkaufGebucht(decimal gesamtSumme)
    {
        LaufenderUmsatz += gesamtSumme;
        AnzahlVerkaeufe++;
        OnChange?.Invoke();
    }

    public void Minimieren()
    {
        IstMinimiert = true;
        OnChange?.Invoke();
    }

    public void Wiederherstellen()
    {
        IstMinimiert = false;
        OnChange?.Invoke();
    }

    public void SitzungBeendet()
    {
        AktiveSitzungId = null;
        SalonId = null;
        KassiererName = null;
        IstMinimiert = false;
        LaufenderUmsatz = 0m;
        AnzahlVerkaeufe = 0;
        OnChange?.Invoke();
    }
}
