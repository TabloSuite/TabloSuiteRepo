namespace ENote.WebApp.Services;

/// <summary>
/// Circuit-weiter Zustand der Kassenanzeige: merkt sich die aktive Sitzung und
/// ob die Kassenansicht minimiert ist (schwebender Button am Tablet).
/// Steuert zugleich den Kassen-Modus (nur Kasse erlaubt, solange eine Sitzung läuft).
/// </summary>
public class KassenAnzeigeState
{
    private readonly BesitzerPinState besitzerPinState;

    public KassenAnzeigeState(BesitzerPinState besitzerPinState)
        => this.besitzerPinState = besitzerPinState;

    public event Action? OnChange;

    public Guid? AktiveSitzungId { get; private set; }
    public Guid? SalonId { get; private set; }
    public string? KassiererName { get; private set; }
    public bool IstMinimiert { get; private set; }
    public decimal Startbestand { get; private set; }
    public decimal BarUmsatz { get; private set; }
    public int AnzahlVerkaeufe { get; private set; }

    /// <summary>true, solange eine Kassensitzung läuft (Kassen-Modus aktiv).</summary>
    public bool KassenModusAktiv => AktiveSitzungId is not null;

    /// <summary>Erwarteter Bargeldbestand: Startbestand + nur Bar-Einnahmen (Karte erhöht die Kasse nicht).</summary>
    public decimal ErwarteterBarBetrag => Startbestand + BarUmsatz;

    public void SitzungGestartet(Guid sitzungId, Guid salonId, string kassiererName, decimal startbestand = 0m, decimal barUmsatzBisher = 0m, int anzahlVerkaeufeBisher = 0)
    {
        AktiveSitzungId = sitzungId;
        SalonId = salonId;
        KassiererName = kassiererName;
        IstMinimiert = false;
        BarUmsatz = barUmsatzBisher;
        AnzahlVerkaeufe = anzahlVerkaeufeBisher;
        Startbestand = startbestand;
        OnChange?.Invoke();
    }

    public void VerkaufGebucht(decimal barBetrag)
    {
        BarUmsatz += barBetrag;
        AnzahlVerkaeufe++;
        OnChange?.Invoke();
    }

    /// <summary>Erwarteten Bargeldbestand aus der Datenbank übernehmen (alle Geräte des Ladens, Retraits, Erstattungen).</summary>
    public void SetzeBarStand(decimal erwartet)
    {
        var neu = erwartet - Startbestand;
        if (neu == BarUmsatz) return;
        BarUmsatz = neu;
        OnChange?.Invoke();
    }

    /// <summary>Bargeld-Einlage (+) oder -Entnahme (−) in der Schublade, ohne Verkauf.</summary>
    public void BargeldBewegt(decimal betrag)
    {
        BarUmsatz += betrag;
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
        BarUmsatz = 0m;
        AnzahlVerkaeufe = 0;
        Startbestand = 0m;
        OnChange?.Invoke();
    }
}
