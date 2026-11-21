namespace ENote.Core.DataModels.Abo;

public enum AboIntervall
{
    Monatlich = 0,
    DreiMonate = 1,
    Jaehrlich = 2,
    SechsMonate = 3
}

public enum AboZahlungStatus
{
    Offen = 0,
    Gemeldet = 1,
    Bestaetigt = 2,
    Abgelehnt = 3
}

/// <summary>Globaler Standard-Nutzungspreis je Intervall (vom SuperAdmin gepflegt).</summary>
public class PlattformPreis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AboIntervall Intervall { get; set; }
    public decimal Preis { get; set; }
    public bool IstAktuell { get; set; } = true;
    public DateTime GueltigAbUtc { get; set; } = ENote.Core.Services.AppUhr.UtcNow;
}

/// <summary>
/// Eine Abrechnungsperiode (Abo) eines Salons. Zahlung wird vom Besitzer gemeldet
/// und vom SuperAdmin bestätigt (Workflow analog SKOULOUAPP).
/// </summary>
public class NutzungsAbo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }
    public Salons.Salon Salon { get; set; } = null!;

    public AboIntervall Intervall { get; set; }
    public decimal PreisSnapshot { get; set; }
    public decimal? MonatsBasisSnapshot { get; set; }
    public decimal RabattProzentSnapshot { get; set; }
    public bool IndividuellerPreisSnapshot { get; set; }
    public bool IstTeilperiode { get; set; }
    public DateTime? FaelligUtc { get; set; }
    public DateTime? KassenSperreAbUtc { get; set; }
    public bool AutomatischErstellt { get; set; }

    public DateTime ZeitraumVonUtc { get; set; }
    public DateTime ZeitraumBisUtc { get; set; }

    public AboZahlungStatus ZahlungStatus { get; set; } = AboZahlungStatus.Offen;
    public DateTime? ZahlungGemeldetUtc { get; set; }
    public DateTime? ZahlungBestaetigtUtc { get; set; }
    public string? ZahlungsNotiz { get; set; }

    public DateTime CreatedAtUtc { get; set; } = ENote.Core.Services.AppUhr.UtcNow;

    // ---------- E-Mail-Benachrichtigungen (jeweils höchstens einmal) ----------
    /// <summary>Mail « Nouvelle facture » versendet.</summary>
    public DateTime? RechnungMailUtc { get; set; }
    /// <summary>Erinnerung kurz vor der Kassensperre versendet.</summary>
    public DateTime? ErinnerungMailUtc { get; set; }
    /// <summary>Hinweis « Caisse suspendue » versendet.</summary>
    public DateTime? SperrMailUtc { get; set; }
}
