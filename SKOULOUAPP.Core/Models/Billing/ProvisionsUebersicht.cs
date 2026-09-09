namespace HalloApp.Core.Models.Billing;

/// <summary>
/// Aggregierte Provisions-Übersicht für einen Zeitraum bzw. eine Abrechnungsperiode.
/// Fasst die einzelnen <see cref="ProvisionsAnteil"/> nach Rolle zusammen.
/// </summary>
public class ProvisionsUebersicht
{
    public Guid SchulID { get; set; }
    public string SchulName { get; set; } = string.Empty;

    /// <summary>Abrechnungsperiode, auf die sich die Übersicht bezieht (null = offener Zeitraum).</summary>
    public Guid? PeriodeID { get; set; }

    public DateTime VonUtc { get; set; }
    public DateTime BisUtc { get; set; }

    /// <summary>Summe aller Schulleiter-Provisionen (FCFA).</summary>
    public decimal ProvisionSchulleiter { get; set; }

    /// <summary>Summe aller Lehrer-Provisionen (FCFA).</summary>
    public decimal ProvisionLehrer { get; set; }

    /// <summary>Summe aller Admin-/Betreiber-Anteile (FCFA).</summary>
    public decimal ProvisionAdmin { get; set; }

    /// <summary>Summe aller ProductOwner-Anteile (FCFA).</summary>
    public decimal ProvisionProductOwner { get; set; }

    /// <summary>Gesamter abgerechneter Umsatz (FCFA), Summe aller Anteile.</summary>
    public decimal Gesamtumsatz => ProvisionSchulleiter + ProvisionLehrer + ProvisionAdmin + ProvisionProductOwner;

    /// <summary>Anzahl der berücksichtigten bezahlten Freischaltungen.</summary>
    public int AnzahlPositionen { get; set; }

    /// <summary>Anzahl der berücksichtigten HALLOBASIS-Abos.</summary>
    public int AnzahlHalloBasis { get; set; }

    /// <summary>Anzahl der berücksichtigten HALLOPLUS-Abos.</summary>
    public int AnzahlHalloPlus { get; set; }

    /// <summary>Alle Einzelanteile, aus denen die Summen gebildet wurden.</summary>
    public List<ProvisionsAnteil> Anteile { get; set; } = new();

    /// <summary>Rollenbezogen sichtbarer Betrag für den aktuellen Benutzer.</summary>
    public decimal SichtbarerBetrag { get; set; }
}
