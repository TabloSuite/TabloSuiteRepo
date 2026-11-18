namespace Skoulou.Core.Entitaeten;

/// <summary>Schule = Mandant. Status steuert Anmeldung und Registrierung.</summary>
public class Schule : Entitaet
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Name ohne Akzente/Groß-Klein/Leerzeichen-Varianten (eindeutig).</summary>
    public string NameNormalisiert { get; set; } = string.Empty;

    public string Ort { get; set; } = string.Empty;
    public string Land { get; set; } = "Togo";
    public SchulStatus Status { get; set; } = SchulStatus.EnAttente;
    public string? StatusGrund { get; set; }
    public string Zeitzone { get; set; } = "Africa/Lome";

    /// <summary>Betreuender Responsable SKOULOU.</summary>
    public Guid? ResponsableId { get; set; }

    /// <summary>Abweichender Jahrespreis je Schüler (FCFA); null = Plattformpreis.</summary>
    public long? JahrespreisOverride { get; set; }

    /// <summary>Abweichender Anteil des Responsable in %; null = Plattformstandard.</summary>
    public decimal? ResponsableProzent { get; set; }
    /// <summary>
    /// Probezeit (18.11.2026): so viele Abrechnungsperioden zu Beginn des ersten Schuljahres stellt SKOULOU nicht in Rechnung
    /// (0–4, höchstens die Periodenzahl des Jahres). Änderbar durch Admin und Responsable, solange die Schule keine Rechnung hat.
    /// Standard 0 (Nutzer 18.11.2026: keine automatische Probezeit – der Admin entscheidet bei jeder neuen Schule).
    /// </summary>
    public int EssaiPerioden { get; set; }

    // Dokumentdaten (Bulletin, Quittung, Karten)
    public string? OffizielleBezeichnung { get; set; }
    public string? Slogan { get; set; }
    public string? Adresse { get; set; }
    public string? Telefon { get; set; }
    public string? Email { get; set; }
    public string? Zulassungsnummer { get; set; }
    public string? Ministerium { get; set; }
    public string? RepublikZeile { get; set; }
    public string? StaatsMotto { get; set; }
    public string? DirectionRegionale { get; set; }
    public string? Statut { get; set; }

    public Guid? LogoMediumId { get; set; }

    /// <summary>Präfix der automatischen Matrikel « {Präfix}{JJ}{0001} ».</summary>
    public string? MatrikelPraefix { get; set; }

    /// <summary>Unentschuldigt fehlend = 0 Punkte (sonst zählt die Note nicht).</summary>
    public bool AbsenzOhneEntschuldigungZaehltNull { get; set; } = true;

    /// <summary>Eltern sehen Noten live (sonst nur veröffentlichte Bulletins).</summary>
    public bool ElternSehenNotenLive { get; set; }
}

/// <summary>Plattformweite Einstellungen (genau eine Zeile).</summary>
public class PlattformEinstellung : Entitaet
{
    /// <summary>Feste Id der einzigen Zeile.</summary>
    public static readonly Guid EinzigeId = new("5c0a1e00-0000-4000-8000-000000000001");

    public long JahrespreisProSchueler { get; set; } = 3_600;
    public int ZahlungszielTage { get; set; } = 30;
    public decimal ResponsableProzentStandard { get; set; }

    /// <summary>Gültige Version der Nutzungsbedingungen; Änderung erzwingt erneute Zustimmung.</summary>
    public string AgbVersion { get; set; } = string.Empty;
}
