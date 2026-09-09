using HalloApp.Core.DataModels.Schools;

namespace HalloApp.Core.Models.Billing;

/// <summary>
/// Aktueller (noch offener) Abrechnungsstand einer Schule für einen Zeitraum.
/// </summary>
public class AbrechnungsUebersicht
{
    public Guid SchulID { get; set; }
    public string SchulName { get; set; } = string.Empty;

    /// <summary>Beginn des offenen Zeitraums (Ende der letzten abgeschlossenen Periode oder Schulbeginn).</summary>
    public DateTime VonUtc { get; set; }

    /// <summary>Ende des offenen Zeitraums (aktueller Zeitpunkt).</summary>
    public DateTime BisUtc { get; set; }

    public int AnzahlHalloBasis { get; set; }
    public int AnzahlHalloPlus { get; set; }

    /// <summary>Offener Umsatz (FCFA), den die Schule dem Betreiber schuldet.</summary>
    public decimal OffenerUmsatz { get; set; }

    /// <summary>Anzahl der bezahlten, aber noch nicht abgerechneten Niveaus.</summary>
    public int AnzahlPositionen => AnzahlHalloBasis + AnzahlHalloPlus;
}

/// <summary>Eine von der Schule als überwiesen gemeldete Rechnung, die noch auf die finale Bankprüfung wartet.</summary>
public class OffeneSuperAdminBestaetigung
{
    public Guid AbrechnungsperiodeId { get; set; }
    public Guid SchulID { get; set; }
    public string SchulName { get; set; } = string.Empty;
    public decimal Umsatz { get; set; }
    public int AnzahlAbonnements { get; set; }
    public DateTime? ZahlungGemeldetAmUtc { get; set; }
    public string ZahlungGemeldetVon { get; set; } = "Direction de l'école";
    public string RechnungErstelltVon { get; set; } = "Administrateur";
    public bool WurdeVonProduktOwnerErstellt { get; set; }
}

/// <summary>Offene Plattformforderung aus einem bereits geschlossenen Klassenzyklus.</summary>
public class GeschlosseneKlasseOffeneAboForderung
{
    public Guid SchulID { get; set; }
    public string SchulName { get; set; } = string.Empty;
    public Guid KlasseId { get; set; }
    public string KlasseCode { get; set; } = string.Empty;
    public string KlasseTitel { get; set; } = string.Empty;
    public DateTime ZyklusEnde { get; set; }
    public int AnzahlAbonnements { get; set; }
    public decimal Plattformforderung { get; set; }
}

/// <summary>Überfällige Plattformforderung aus einer weiterhin aktiven Klasse.</summary>
public class AktiveKlasseUeberfaelligeAboForderung
{
    public Guid SchulID { get; set; }
    public string SchulName { get; set; } = string.Empty;
    public Guid KlasseId { get; set; }
    public string KlasseCode { get; set; } = string.Empty;
    public string KlasseTitel { get; set; } = string.Empty;
    public DateTime AeltesteZahlungAmUtc { get; set; }
    public int TageOffen { get; set; }
    public int AnzahlAbonnements { get; set; }
    public decimal Plattformforderung { get; set; }
}
