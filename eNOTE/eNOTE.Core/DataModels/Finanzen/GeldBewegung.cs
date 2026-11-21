namespace ENote.Core.DataModels.Finanzen;

public enum GeldBewegungArt
{
    /// <summary>Besitzer nimmt Bargeld für sich (Retrait du propriétaire).</summary>
    EntnahmeBesitzer = 0,

    /// <summary>Geld kommt von außen (Hilfe, Apport, Darlehen) – nie Umsatz.</summary>
    Einlage = 1
}

public enum GeldOrt
{
    /// <summary>Bargeld in der Kassenschublade (wirkt auf den erwarteten Kassenbestand).</summary>
    Kasse = 0,

    /// <summary>Bank, Mobile Money o. Ä. – nur Finanzbuch.</summary>
    AusserhalbKasse = 1
}

/// <summary>Geldbewegung außerhalb von Verkäufen und Ausgaben (Entnahmen, Einlagen/Hilfen).</summary>
public class GeldBewegung
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }
    public GeldBewegungArt Art { get; set; }
    public GeldOrt Ort { get; set; }
    public decimal Betrag { get; set; }

    /// <summary>Bei Einlagen: Herkunft (z. B. „Aide familiale“, „Microcrédit“).</summary>
    public string? Herkunft { get; set; }
    public string? Notiz { get; set; }

    /// <summary>Bei Ort = Kasse: die offene Sitzung, deren Bestand sich ändert.</summary>
    public Guid? KassenSitzungId { get; set; }
    public Guid ErfasstVonId { get; set; }
    public string ErfasstVonName { get; set; } = string.Empty;
    public DateTime ZeitUtc { get; set; } = ENote.Core.Services.AppUhr.UtcNow;

    /// <summary>Wirkung auf den Kassenbestand bzw. Saldo: Einlage +, Entnahme −.</summary>
    public decimal Vorzeichenbetrag => Art == GeldBewegungArt.Einlage ? Betrag : -Betrag;
}
