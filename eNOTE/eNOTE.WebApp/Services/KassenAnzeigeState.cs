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
    /// <summary>
    /// Wer an DIESEM Gerät kassiert (« Qui encaisse ? », per PIN). Mehrere Geräte können in derselben offenen Kasse
    /// arbeiten, jedes mit eigener Person; jeder Bon wird dieser Person zugeordnet. null = noch niemand angemeldet.
    /// </summary>
    public Guid? AktiverKassiererId { get; private set; }

    /// <summary>Name der Person, die an diesem Gerät kassiert (bzw. bis zur PIN-Anmeldung die Person, die geöffnet hat).</summary>
    public string? KassiererName { get; private set; }

    /// <summary>true, wenn an diesem Gerät noch niemand per PIN angemeldet ist – dann wird vor dem Kassieren gefragt.</summary>
    public bool KassiererFehlt => KassenModusAktiv && AktiverKassiererId is null;
    public bool IstMinimiert { get; private set; }
    public decimal Startbestand { get; private set; }
    public decimal BarUmsatz { get; private set; }
    public int AnzahlVerkaeufe { get; private set; }

    /// <summary>true, solange eine Kassensitzung läuft (Kassen-Modus aktiv).</summary>
    public bool KassenModusAktiv => AktiveSitzungId is not null;

    /// <summary>Erwarteter Bargeldbestand: Startbestand + nur Bar-Einnahmen (Karte erhöht die Kasse nicht).</summary>
    public decimal ErwarteterBarBetrag => Startbestand + BarUmsatz;

    /// <param name="kassiererId">Person, die sich gerade per PIN angemeldet hat (Öffnen der Kasse). null beim Wiederaufnehmen
    /// einer offenen Kasse: läuft dieselbe Sitzung hier schon, bleibt die angemeldete Person, sonst muss sich jemand anmelden.</param>
    public void SitzungGestartet(Guid sitzungId, Guid salonId, string kassiererName, decimal startbestand = 0m, decimal barUmsatzBisher = 0m,
        int anzahlVerkaeufeBisher = 0, Guid? kassiererId = null)
    {
        if (kassiererId is not null)
        {
            AktiverKassiererId = kassiererId;
            KassiererName = kassiererName;
        }
        else if (AktiveSitzungId != sitzungId || AktiverKassiererId is null)
        {
            AktiverKassiererId = null;
            KassiererName = kassiererName;
        }
        AktiveSitzungId = sitzungId;
        SalonId = salonId;
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

    /// <summary>Person an diesem Gerät wechseln (« Changer de caissier », nach PIN-Prüfung).</summary>
    public void SetzeKassierer(Guid kassiererId, string name)
    {
        AktiverKassiererId = kassiererId;
        KassiererName = name;
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
        AktiverKassiererId = null;
        IstMinimiert = false;
        BarUmsatz = 0m;
        AnzahlVerkaeufe = 0;
        Startbestand = 0m;
        OnChange?.Invoke();
    }
}
