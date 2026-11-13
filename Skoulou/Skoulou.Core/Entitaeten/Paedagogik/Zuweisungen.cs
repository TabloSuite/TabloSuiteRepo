namespace Skoulou.Core.Entitaeten;

/// <summary>Zuweisung einer Leçon oder eines Examens an eine Klasse.</summary>
public class Zuweisung : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid? LeconId { get; set; }
    public Guid? PruefungId { get; set; }
    public Guid KlasseId { get; set; }
    public Guid LehrerId { get; set; }

    /// <summary>Veröffentlichung (Examen: Beginn des Fensters).</summary>
    public DateTime AbUtc { get; set; }

    public DateTime? FristUtc { get; set; }

    /// <summary>null = « illimité ».</summary>
    public int? MaxVersuche { get; set; }

    public VersuchsWertung Wertung { get; set; }
    public LoesungsSichtbarkeit LoesungenSichtbar { get; set; }
    public ZuweisungsStatus Status { get; set; }
}

/// <summary>Versuch eines Schülers zu einer Zuweisung.</summary>
public class Versuch : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid ZuweisungId { get; set; }
    public Guid SchuelerId { get; set; }
    public int Nr { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime? AbgabeUtc { get; set; }
    public DateTime? DeadlineUtc { get; set; }
    public decimal Punkte { get; set; }
    public decimal MaxPunkte { get; set; }
    public VersuchsStatus Status { get; set; }

    /// <summary>Beim Start eingefrorene Fragen (Revisionen) – spätere Änderungen betreffen nur neue Versuche.</summary>
    public List<Guid> FragenIds { get; set; } = [];
}

/// <summary>Antwort auf eine Frage innerhalb eines Versuchs.</summary>
public class Antwort : Entitaet
{
    public Guid VersuchId { get; set; }
    public Guid FrageId { get; set; }
    public string AntwortJson { get; set; } = "{}";
    public decimal? Punkte { get; set; }
    public KorrekturArt Korrektur { get; set; }
    public Guid? KorrigiertVonId { get; set; }
    public DateTime? KorrigiertAmUtc { get; set; }
    public string? Feedback { get; set; }
}

/// <summary>Protokoll jeder manuellen (Nach-)Korrektur einer Antwort (nur anhängen).</summary>
public class KorrekturEintrag : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid AntwortId { get; set; }
    public Guid VersuchId { get; set; }
    public decimal? AltePunkte { get; set; }
    public decimal NeuePunkte { get; set; }
    public string? Feedback { get; set; }
    public Guid KorrigiertVonId { get; set; }
    public DateTime ZeitUtc { get; set; }
}

/// <summary>Materialisierter Lernstand je Schüler und Zuweisung.</summary>
public class LernStand : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid SchuelerId { get; set; }
    public Guid ZuweisungId { get; set; }
    /// <summary>Punkte des gewerteten Versuchs (gemäß Versuchsregel der Zuweisung).</summary>
    public decimal BestePunkte { get; set; }

    public decimal MaxPunkte { get; set; }
    public decimal Prozent { get; set; }
    public bool Abgeschlossen { get; set; }
    public int AnzahlVersuche { get; set; }

    /// <summary>Der gewertete Versuch wartet noch auf eine manuelle Korrektur.</summary>
    public bool KorrekturOffen { get; set; }

    public DateTime? LetzteAktivitaetUtc { get; set; }
}
