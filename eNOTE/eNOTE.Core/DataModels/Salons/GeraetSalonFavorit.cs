namespace ENote.Core.DataModels.Salons;

/// <summary>
/// Optionaler Favoriten-Laden für ein bestimmtes Gerät (Tablet). Über einen im
/// Browser-LocalStorage des Geräts erzeugten Schlüssel wird ein Salon als
/// Vorauswahl für den Kassenstart hinterlegt. Die fachliche Bindung eines
/// Verkaufs bleibt die Kassen-PIN; der Favorit ist nur eine Komfort-Vorauswahl.
/// </summary>
public class GeraetSalonFavorit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stabiler, zufälliger Schlüssel des Geräts (im LocalStorage erzeugt).</summary>
    public string GeraeteSchluessel { get; set; } = string.Empty;

    public Guid SalonId { get; set; }
    public Salon Salon { get; set; } = null!;

    /// <summary>Optionaler Geräte-Name, damit der Besitzer das Gerät erkennt (z. B. „Tablet Eingang“).</summary>
    public string? GeraeteName { get; set; }

    public DateTime CreatedAtUtc { get; set; } = ENote.Core.Services.AppUhr.UtcNow;
}
