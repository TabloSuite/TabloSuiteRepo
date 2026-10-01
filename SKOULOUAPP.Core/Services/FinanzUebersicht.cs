using HalloApp.Core.DataModels.Finanzen;

namespace HalloApp.Core.Services;

/// <summary>Filter- und Sortieroptionen für die Finanzübersicht.</summary>
public class FinanzFilter
{
    /// <summary>Schulfilter (für SuperAdmin optional; für SchulLeiter fest gesetzt).</summary>
    public Guid? SchulID { get; set; }

    /// <summary>Nur Buchungen ab diesem Wirkungsdatum (inkl.).</summary>
    public DateTime? Von { get; set; }

    /// <summary>Nur Buchungen bis zu diesem Wirkungsdatum (inkl.).</summary>
    public DateTime? Bis { get; set; }

    /// <summary>Nur Einnahmen oder nur Ausgaben; null = beide.</summary>
    public FinanzRichtung? Richtung { get; set; }

    /// <summary>Optionaler Kategoriefilter.</summary>
    public Guid? KategorieID { get; set; }

    /// <summary>Sortierschlüssel.</summary>
    public FinanzSortierung Sortierung { get; set; } = FinanzSortierung.WirkungsdatumAbsteigend;
}

/// <summary>Sortiermöglichkeiten der Finanzübersicht.</summary>
public enum FinanzSortierung
{
    WirkungsdatumAbsteigend = 0,
    WirkungsdatumAufsteigend = 1,
    BetragAbsteigend = 2,
    BetragAufsteigend = 3,
    ErstelltAmAbsteigend = 4
}

/// <summary>Eine Zeile der Finanzübersicht (projiziert für die Anzeige/PDF).</summary>
public class FinanzUebersichtZeile
{
    public Guid BuchungID { get; set; }
    public string Buchungsnummer { get; set; } = string.Empty;
    public DateTime Wirkungsdatum { get; set; }
    public DateTime ErstelltAmUtc { get; set; }
    public string SchulName { get; set; } = string.Empty;
    public string Kategorie { get; set; } = string.Empty;
    public FinanzSystemCode? KategorieSystemCode { get; set; }
    public FinanzRichtung Richtung { get; set; }
    public decimal Betrag { get; set; }
    public string? Belegnummer { get; set; }
    public bool HatBelegdatei { get; set; }
    public string? BelegDateiname { get; set; }
    public string? BeschreibungHtml { get; set; }
    public string ErfasstVon { get; set; } = string.Empty;
}

/// <summary>Aggregiertes Ergebnis der Finanzübersicht inkl. Summen.</summary>
public class FinanzUebersicht
{
    public List<FinanzUebersichtZeile> Zeilen { get; set; } = new();
    public decimal SummeEinnahmen { get; set; }
    public decimal SummeAusgaben { get; set; }
    public decimal Saldo => SummeEinnahmen - SummeAusgaben;
}

public sealed record FinanzBericht(string SchulName, FinanzFilter Filter, FinanzUebersicht Uebersicht);

public sealed record FinanzAbgleich(
    decimal SchulgebuehrenZahlungen,
    decimal SchulgebuehrenBuchungen,
    int SchulgebuehrenAbweichungen,
    decimal PlattformRechnungen,
    decimal PlattformBuchungenZuRechnungen,
    decimal BestaetigtePlattformZahlungen,
    int PlattformRechnungenMitAbweichung);

public enum FinanzSaldoStatus
{
    Verlust,
    Ausgeglichen,
    Gewinn
}

public class DirektorKontostandUebersicht
{
    public static FinanzSaldoStatus BestimmeSaldoStatus(decimal saldo)
        => saldo < 0m ? FinanzSaldoStatus.Verlust : saldo > 0m ? FinanzSaldoStatus.Gewinn : FinanzSaldoStatus.Ausgeglichen;

    public decimal IstKontostand { get; set; }
    public decimal SchulgebuehrenEingenommen { get; set; }
    public decimal SonstigeEinnahmen { get; set; }
    public decimal BetrieblicheAusgaben { get; set; }
    public decimal HalloAppAboEingenommen { get; set; }
    public decimal SchulleiterAboAnteil { get; set; }
    public decimal LehrerAboAnteil { get; set; }
    public decimal HalloAppAboAnteil { get; set; }
    public decimal AboVerteilungGesamt => SchulleiterAboAnteil + LehrerAboAnteil + HalloAppAboAnteil;
    public decimal VerfuegbarerKontostand => IstKontostand;
    public decimal KontostandMitNichtUeberwiesenenHalloAppAnteilen => IstKontostand + LehrerAboAnteil + HalloAppAboAnteil;
    public decimal WirklicherKontostandNachAboVerteilung => KontostandMitNichtUeberwiesenenHalloAppAnteilen - HalloAppAboAnteil;
    public decimal OffeneSchulgebuehrenAktiveKlassen { get; set; }
    public decimal OffeneAboGebuehrenAktiveKlassen { get; set; }
    public int OffeneAboAnzahlAktiveKlassen { get; set; }
    public decimal SollKontostand => IstKontostand + OffeneSchulgebuehrenAktiveKlassen + OffeneAboGebuehrenAktiveKlassen;
}

/// <summary>Leichtgewichtige Auswahlliste (Id + Anzeigename).</summary>
public class FinanzAuswahl
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Zahlungsstand eines eingeschriebenen Schülers innerhalb eines Durchlaufs.</summary>
public class SchuelerZahlungsstand
{
    public Guid StudentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Soll { get; set; }
    public decimal Bezahlt { get; set; }
    public decimal Offen => Soll - Bezahlt > 0 ? Soll - Bezahlt : 0m;
    public bool VollBezahlt => Bezahlt >= Soll;
}

/// <summary>Übersicht eines Durchlaufs inklusive aller Schüler-Zahlungsstände.</summary>
public class DurchlaufUebersicht
{
    public Guid DurchlaufID { get; set; }
    public Guid KlasseId { get; set; }
    public string NiveauCode { get; set; } = string.Empty;
    public decimal NiveauPreis { get; set; }
    public DateTime StartDatum { get; set; }
    public DateTime? EndDatum { get; set; }
    public bool IstAktiv => EndDatum is null;
    public List<SchuelerZahlungsstand> Schueler { get; set; } = new();
    public decimal SummeSoll => Schueler.Sum(x => x.Soll);
    public decimal SummeBezahlt => Schueler.Sum(x => x.Bezahlt);
    public decimal SummeOffen => Schueler.Sum(x => x.Offen);
}

/// <summary>Klassenbezogener Finanzbericht für einen konkreten Kursdurchlauf.</summary>
public class KlassenFinanzbericht
{
    public Guid DurchlaufId { get; set; }
    public Guid KlasseId { get; set; }
    public string KlasseCode { get; set; } = string.Empty;
    public string KlasseTitel { get; set; } = string.Empty;
    public DateTime StartDatum { get; set; }
    public DateTime? EndDatum { get; set; }
    public bool IstAktiv => EndDatum is null;
    public decimal SchulgebuehrProSchueler { get; set; }
    public List<KlassenFinanzberichtSchueler> Schueler { get; set; } = new();

    public int SchuelerAnzahl => Schueler.Count;
    public int SchulgebuehrVollBezahltAnzahl => Schueler.Count(x => x.SchulgebuehrVollBezahlt);
    public int SchulgebuehrTeilbezahltAnzahl => Schueler.Count(x => x.SchulgebuehrBezahlt > 0m && !x.SchulgebuehrVollBezahlt);
    public int SchulgebuehrOffenAnzahl => Schueler.Count(x => x.SchulgebuehrBezahlt <= 0m);
    public decimal SchulgebuehrSoll => Schueler.Sum(x => x.SchulgebuehrSoll);
    public decimal SchulgebuehrBezahlt => Schueler.Sum(x => x.SchulgebuehrBezahlt);
    public decimal SchulgebuehrOffen => Schueler.Sum(x => x.SchulgebuehrOffen);
    public int HalloBasisAnzahl => Schueler.Count(x => x.AboTyp == HalloApp.Core.DataModels.Schools.AboTyp.HalloApp);
    public int HalloPlusAnzahl => 0;
    public int HalloAboBezahltAnzahl => Schueler.Count(x => x.HalloAboBezahlt);
    public int HalloAboOffenAnzahl => Schueler.Count(x => !x.HalloAboBezahlt);
    public decimal HalloAboSoll => Schueler.Sum(x => x.HalloAboPreis);
    public decimal HalloAboOffen => Schueler.Where(x => !x.HalloAboBezahlt).Sum(x => x.HalloAboPreis);
}

/// <summary>Zahlungs- und Abozustand eines Schülers im Klassenfinanzbericht.</summary>
public class KlassenFinanzberichtSchueler
{
    public Guid StudentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal SchulgebuehrSoll { get; set; }
    public decimal SchulgebuehrBezahlt { get; set; }
    public decimal SchulgebuehrOffen => Math.Max(0m, SchulgebuehrSoll - SchulgebuehrBezahlt);
    public bool SchulgebuehrVollBezahlt => SchulgebuehrBezahlt >= SchulgebuehrSoll;
    public HalloApp.Core.DataModels.Schools.AboTyp AboTyp { get; set; }
    public decimal HalloAboPreis { get; set; }
    public bool HalloAboBezahlt { get; set; }
}
