namespace Skoulou.Core.Entitaeten;

/// <summary>Layout-Vorlage des Bulletins (je Schule, optional je Stufe).</summary>
public class BulletinVorlage : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }

    /// <summary>null = Standard der Schule.</summary>
    public Guid? StufeId { get; set; }

    public string Name { get; set; } = "Modèle standard";
    public string Farbe { get; set; } = "#1d4ed8";

    public bool ZeigeKoeffizienten { get; set; } = true;
    public bool ZeigeRang { get; set; } = true;
    public bool ZeigeVorperioden { get; set; } = true;
    public bool ZeigeUnterschriften { get; set; } = true;
    public bool ZeigeLehrkraefte { get; set; } = true;
    public bool ZeigeAnwesenheit { get; set; } = true;
    public bool ZeigeTeilnoten { get; set; }
    public bool ZeigePunkte { get; set; } = true;
    public bool ZeigeFachRang { get; set; }
    public bool ZeigeAppreciation { get; set; } = true;
    public bool ZeigeSignaturSpalte { get; set; }
    public bool ZeigeFachGruppen { get; set; }
    public bool ZeigeKlassenStatistik { get; set; } = true;
    public bool ZeigeMentionen { get; set; } = true;
    public bool ZeigeJahresBilanz { get; set; } = true;
    public bool ZeigeFoto { get; set; }
    public bool ZeigeNoteInWorten { get; set; }

    public int SchriftGroesse { get; set; } = 10;
    public int SeitenRand { get; set; } = 25;
    public string? Fusstext { get; set; }
}

/// <summary>Veröffentlichung der Bulletins einer Klasse in einer Periode.</summary>
public class KlassenVeroeffentlichung : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid KlasseId { get; set; }
    public Guid PeriodeId { get; set; }
    public VeroeffentlichungsStatus Status { get; set; }
    public DateTime VeroeffentlichtAmUtc { get; set; }
    public Guid VeroeffentlichtVonId { get; set; }
    public string? RectificationGrund { get; set; }
}

/// <summary>Veröffentlichtes Bulletin als eingefrorener Snapshot mit Prüf-Token (QR).</summary>
public class Bulletin : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid PeriodeId { get; set; }
    public Guid EinschreibungId { get; set; }

    /// <summary>Fortlaufende Nummer « BUL-2026-00042 ».</summary>
    public string Nummer { get; set; } = string.Empty;

    /// <summary>64 Hex-Zeichen für die öffentliche Prüfung.</summary>
    public string PruefToken { get; set; } = string.Empty;

    public BulletinStatus Status { get; set; }
    public Guid? ErsetztId { get; set; }
    public bool IstRectificatif { get; set; }
    public string? Grund { get; set; }

    /// <summary>Snapshot ohne Bildbytes; Logo/Foto als unveränderliche Medium-Referenz.</summary>
    public string SnapshotJson { get; set; } = "{}";

    public Guid? LogoMediumId { get; set; }
    public Guid? FotoMediumId { get; set; }
    public string Hash { get; set; } = string.Empty;
    public DateTime VeroeffentlichtAmUtc { get; set; }
}

/// <summary>Protokoll: Familie benachrichtigt bzw. Bulletin ausgehändigt.</summary>
public class BulletinUebergabe : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid BulletinId { get; set; }
    public UebergabeArt Art { get; set; }
    public string? Vermerk { get; set; }
    public Guid VonId { get; set; }
}
