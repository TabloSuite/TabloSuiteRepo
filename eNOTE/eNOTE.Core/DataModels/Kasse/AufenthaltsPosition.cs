namespace ENote.Core.DataModels.Kasse;

/// <summary>
/// Eine während der Anwesenheit erbrachte und schon fest erfasste Leistung (« Enregistrer sans encaisser »),
/// z. B. Patient bleibt mehrere Tage in der Praxis: jeden Tag werden die Leistungen erfasst, bezahlt wird am Ende.
/// Gehört zum offenen <see cref="KundenBesuch"/>. Name und Preis sind Kopien vom Zeitpunkt der Erfassung;
/// der Lagerbestand sinkt schon bei der Erfassung (Ware ist verbraucht), Umsatz und Kasse erst beim Encaissement.
/// Offen = weder bezahlt (<see cref="VerkaufId"/>) noch zurückgenommen (<see cref="ZurueckgenommenUtc"/>).
/// </summary>
public class AufenthaltsPosition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }
    public Guid BesuchId { get; set; }
    public Guid KundeId { get; set; }

    /// <summary>Leistung aus dem Katalog; null nur bei abgeschlossenen Zeilen, deren Leistung später gelöscht wurde.</summary>
    public Guid? LeistungId { get; set; }
    public string LeistungName { get; set; } = string.Empty;
    public decimal EinzelPreis { get; set; }
    public int Menge { get; set; } = 1;
    /// <summary>Standard-Artikel « Divers » mit frei eingegebenem Betrag.</summary>
    public bool FreierBetrag { get; set; }

    /// <summary>Tag der Leistung in Ortszeit des Ladens (für Bon und Übersicht « jour par jour »).</summary>
    public DateOnly ErbrachtAm { get; set; }
    public DateTime ErfasstUtc { get; set; }
    public Guid ErfasstVonId { get; set; }
    public string ErfasstVonName { get; set; } = string.Empty;

    /// <summary>Bei der Erfassung abgezogener Bestand (Leistung bzw. Basis-Leistung eines Sammel-Artikels) – für die Rückgabe ins Lager.</summary>
    public Guid? LagerLeistungId { get; set; }
    public decimal? LagerMenge { get; set; }

    /// <summary>Bezahlt mit diesem Verkauf (Nebenläufigkeits-Token: eine Zeile wird nie zweimal kassiert).</summary>
    public Guid? VerkaufId { get; set; }

    /// <summary>Zurückgenommen (Erfassungsfehler, nicht erbracht, Kunde ohne Zahlung gegangen …) – bleibt sichtbar, zählt nicht mehr.</summary>
    public DateTime? ZurueckgenommenUtc { get; set; }
    public string? ZurueckgenommenVonName { get; set; }
    public string? RuecknahmeGrund { get; set; }
    public bool InsLagerZurueck { get; set; }

    public decimal Summe => EinzelPreis * Menge;
    public bool IstOffen => VerkaufId is null && ZurueckgenommenUtc is null;
}
