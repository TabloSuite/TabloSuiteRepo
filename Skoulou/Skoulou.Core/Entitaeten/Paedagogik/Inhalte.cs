namespace Skoulou.Core.Entitaeten;

/// <summary>Leçon: gehört zu Stufe + Fach und überlebt Schuljahre.</summary>
public class Lecon : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid AutorId { get; set; }

    /// <summary>Gesetzt = « Saisi par la direction ».</summary>
    public Guid? ErfasstVonId { get; set; }

    public Guid FachId { get; set; }
    public Guid StufeId { get; set; }
    public string Titel { get; set; } = string.Empty;
    public string? Zusammenfassung { get; set; }
    public string? Thema { get; set; }
    public Guid? VideoMediumId { get; set; }
    public InhaltsStatus Status { get; set; }
    public List<LeconSeite> Seiten { get; set; } = [];
}

/// <summary>Seite einer Leçon (bereinigtes HTML).</summary>
public class LeconSeite : Entitaet
{
    public Guid LeconId { get; set; }
    public int Nr { get; set; }
    public string Html { get; set; } = string.Empty;
}

/// <summary>Examen mit Dauer und Bestehensgrenze.</summary>
public class Pruefung : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid AutorId { get; set; }
    public Guid? ErfasstVonId { get; set; }
    public Guid FachId { get; set; }
    public Guid StufeId { get; set; }
    public string Titel { get; set; } = string.Empty;
    public string? Beschreibung { get; set; }
    public int DauerMinuten { get; set; } = 60;

    /// <summary>Bestehensgrenze in Prozent.</summary>
    public decimal Bestehensgrenze { get; set; } = 50;

    public InhaltsStatus Status { get; set; }
}

/// <summary>Aufgabenblock einer Leçon oder eines Examens (genau eines von beiden).</summary>
public class AufgabenBlock : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid? LeconId { get; set; }
    public Guid? PruefungId { get; set; }
    public int Nr { get; set; }
    public string? Titel { get; set; }
    public string? Consigne { get; set; }

    /// <summary>Entfernt, aber mit beantworteten Fragen (bleibt für alte Versuche erhalten).</summary>
    public bool Archiviert { get; set; }
}

/// <summary>
/// Frage eines Blocks. Nach der ersten Abgabe unveränderlich: Bearbeiten erzeugt eine neue
/// Revision (alte wird archiviert, alte Versuche bleiben gültig).
/// </summary>
public class Frage : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid BlockId { get; set; }
    public int Nr { get; set; }
    public FragenTyp Typ { get; set; }
    public string TextHtml { get; set; } = string.Empty;
    public decimal Punkte { get; set; } = 1;
    public string? AudioText { get; set; }
    public string Sprache { get; set; } = "fr-FR";
    public List<string> AkzeptierteAntworten { get; set; } = [];

    /// <summary>Lücken als JSON-Liste von Listen akzeptierter Antworten.</summary>
    public string? LueckenJson { get; set; }

    public List<string> Wortbank { get; set; } = [];

    /// <summary>Akzente beim Textvergleich beachten (Standard: « é » = « e »).</summary>
    public bool AkzenteBeachten { get; set; }

    public int Revision { get; set; } = 1;
    public Guid? VorgaengerId { get; set; }
    public bool Archiviert { get; set; }
    public List<FrageOption> Optionen { get; set; } = [];
}

/// <summary>Antwortmöglichkeit einer Auswahlfrage.</summary>
public class FrageOption : Entitaet
{
    public Guid FrageId { get; set; }
    public int Nr { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool Richtig { get; set; }
}

/// <summary>Favorisierte Formel (LaTeX) eines Kontos.</summary>
public class FormelFavorit : Entitaet
{
    public Guid KontoId { get; set; }
    public string LaTeX { get; set; } = string.Empty;
    public string Bezeichnung { get; set; } = string.Empty;
}
