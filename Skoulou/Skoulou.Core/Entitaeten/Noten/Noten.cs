namespace Skoulou.Core.Entitaeten;

/// <summary>Schema-Vorlage einer Periode (Vorschlag beim Anlegen von Bewertungen).</summary>
public class PeriodenKomponente : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid PeriodeId { get; set; }
    public string Titel { get; set; } = string.Empty;
    public BewertungsArt Art { get; set; }
    public decimal Gewicht { get; set; } = 1;
    public decimal Bareme { get; set; } = 20;
    public int Reihenfolge { get; set; }
}

/// <summary>Bewertung (Évaluation) eines Klassenfachs in einer Periode.</summary>
public class Bewertung : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid KlassenfachId { get; set; }
    public Guid PeriodeId { get; set; }
    public Guid? KomponenteId { get; set; }
    public string Titel { get; set; } = string.Empty;
    public BewertungsArt Art { get; set; }
    public DateOnly? Datum { get; set; }
    public decimal Bareme { get; set; } = 20;
    public decimal Gewicht { get; set; } = 1;
    public Guid ErstelltVonId { get; set; }
}

/// <summary>Note eines Schülers (über die Einschreibung) in einer Bewertung.</summary>
public class Note : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid BewertungId { get; set; }
    public Guid EinschreibungId { get; set; }
    public decimal? Punkte { get; set; }
    public NotenStatus Status { get; set; }
    public Guid ErfasstVonId { get; set; }

    /// <summary>Optimistische Nebenläufigkeit (parallele Änderung).</summary>
    public byte[]? Version { get; set; }
}

/// <summary>Historie einer Note (nur anhängen).</summary>
public class NotenAenderung : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid NoteId { get; set; }
    public decimal? AltePunkte { get; set; }
    public NotenStatus? AlterStatus { get; set; }
    public decimal? NeuePunkte { get; set; }
    public NotenStatus NeuerStatus { get; set; }
    public Guid GeaendertVonId { get; set; }
    public DateTime ZeitUtc { get; set; }
    public string? Grund { get; set; }
}

/// <summary>Appréciation des Lehrers je Fach, Periode und Schüler.</summary>
public class FachBemerkung : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid KlassenfachId { get; set; }
    public Guid PeriodeId { get; set; }
    public Guid EinschreibungId { get; set; }
    public string Text { get; set; } = string.Empty;
    public Guid ErfasstVonId { get; set; }
}

/// <summary>Beschluss des Klassenrats je Periode und Schüler.</summary>
public class KlassenratEintrag : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid PeriodeId { get; set; }
    public Guid EinschreibungId { get; set; }

    /// <summary>null = Vorschlag aus der Moyenne.</summary>
    public Distinktion? Distinktion { get; set; }

    public Sanktionen Sanktionen { get; set; }
    public string? Bemerkung { get; set; }
    public Guid? ErfasstVonId { get; set; }
}

/// <summary>Entscheidung am Jahresende (letzte Einschreibung des Jahres).</summary>
public class Jahresentscheidung : Entitaet, ISchulgebunden
{
    public Guid SchuleId { get; set; }
    public Guid EinschreibungId { get; set; }
    public JahresEntscheidungsArt Entscheidung { get; set; }
    public Guid? ZielStufeId { get; set; }
    public string? Bemerkung { get; set; }
    public DateTime? AusgefuehrtAmUtc { get; set; }
}
