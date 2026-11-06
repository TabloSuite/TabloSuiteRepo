namespace ENote.Core.DataModels.Abo;

public class PoZahlungseingang
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProduktOwnerId { get; set; }
    public Guid NutzungsAboId { get; set; }
    public NutzungsAbo NutzungsAbo { get; set; } = null!;
    public string Besitzer { get; set; } = "";
    public string Laden { get; set; } = "";
    public DateTime VonUtc { get; set; }
    public DateTime BisUtc { get; set; }
    public DateTime EingangUtc { get; set; }
    public DateTime ErfasstUtc { get; set; }
    public decimal Betrag { get; set; }
    public decimal ProzentProvision { get; set; }
    public decimal FestProvision { get; set; }
    public decimal Provision { get; set; }
    public string Referenz { get; set; } = "";
    public Guid? AnforderungId { get; set; }
    public PoZahlungsanforderung? Anforderung { get; set; }
}

public class PoZahlungsanforderung
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SammellaufId { get; set; }
    public Guid ProduktOwnerId { get; set; }
    public DateTime StichtagUtc { get; set; }
    public DateTime FaelligUtc { get; set; }
    public DateTime ErstelltUtc { get; set; }
    public decimal Betrag { get; set; }
    public decimal VerrechneteProvision { get; set; }
    public decimal NettoBetrag => Betrag - VerrechneteProvision;
    public List<PoZahlungseingang> Positionen { get; set; } = [];
}

public enum PoBewegungsStatus { Gemeldet, Bestaetigt, Abgelehnt }

public class PoGeldbewegung
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProduktOwnerId { get; set; }
    public Guid? AnforderungId { get; set; }
    public PoZahlungsanforderung? Anforderung { get; set; }
    public bool IstProvision { get; set; }
    public decimal Betrag { get; set; }
    public DateTime ZahlungsdatumUtc { get; set; }
    public DateTime ErfasstUtc { get; set; }
    public string Referenz { get; set; } = "";
    public PoBewegungsStatus Status { get; set; }
}

public class PoAbrechnungsEreignis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProduktOwnerId { get; set; }
    public Guid BenutzerId { get; set; }
    public string Benutzer { get; set; } = "";
    public Guid VorgangId { get; set; }
    public DateTime ZeitpunktUtc { get; set; }
    public string Aktion { get; set; } = "";
    public decimal Betrag { get; set; }
    public string Notiz { get; set; } = "";
}
