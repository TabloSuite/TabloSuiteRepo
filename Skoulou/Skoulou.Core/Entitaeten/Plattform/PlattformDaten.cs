namespace Skoulou.Core.Entitaeten;

/// <summary>Zähler eines Nummernkreises (je Schule bzw. global, Art und Jahr).</summary>
public class Nummernkreis : Entitaet
{
    public Guid? SchuleId { get; set; }
    public NummernArt Art { get; set; }
    public int Jahr { get; set; }
    public long Letzte { get; set; }
}

/// <summary>Annonce in der App (gecacht, mit Zielgruppe und Zeitraum).</summary>
public class Ankuendigung : Entitaet
{
    public string Titel { get; set; } = string.Empty;
    public string InhaltHtml { get; set; } = string.Empty;
    public AnkuendigungsStufe Stufe { get; set; }
    public ZielRollen Zielrollen { get; set; } = ZielRollen.Alle;
    public Guid? SchuleId { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndeUtc { get; set; }
    public Guid? ErstelltVonId { get; set; }
}

/// <summary>Nachricht der Messagerie (eingehend über Webhook/Kontakt, ausgehend als Antwort).</summary>
public class Nachricht : Entitaet
{
    public NachrichtRichtung Richtung { get; set; }
    public string Von { get; set; } = string.Empty;
    public string An { get; set; } = string.Empty;
    public string Betreff { get; set; } = string.Empty;

    /// <summary>Bereinigtes HTML.</summary>
    public string TextHtml { get; set; } = string.Empty;

    /// <summary>Automatisch über ein bekanntes Absenderkonto zugeordnet.</summary>
    public Guid? SchuleId { get; set; }

    public DateTime? GelesenAmUtc { get; set; }
    public Guid? AntwortAufId { get; set; }
    public Guid? GesendetVonId { get; set; }

    /// <summary>MessageID von Postmark (eindeutig, gegen Doppelzustellung).</summary>
    public string? PostmarkId { get; set; }
}

/// <summary>Outbox: jede Mail wird zuerst gespeichert und dann im Hintergrund versendet.</summary>
public class MailAusgang : Entitaet
{
    public string An { get; set; } = string.Empty;
    public string Betreff { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? AntwortAn { get; set; }
    public MailStatus Status { get; set; } = MailStatus.Offen;
    public int Versuche { get; set; }
    public DateTime NaechsterVersuchUtc { get; set; }
    public string? Fehler { get; set; }

    /// <summary>Anlass, z. B. « mot-de-passe-oublie » oder « contact ».</summary>
    public string? Bezug { get; set; }

    public DateTime? GesendetAmUtc { get; set; }
    public string? PostmarkId { get; set; }
}

/// <summary>Fehlerjournal (Serilog-Ereignisse ab Error mit Exception).</summary>
public class FehlerProtokoll : Entitaet
{
    public DateTime ZeitUtc { get; set; }
    public string Stufe { get; set; } = "Error";
    public string Nachricht { get; set; } = string.Empty;
    public string? Typ { get; set; }
    public string? Details { get; set; }
    public string? Pfad { get; set; }
    public Guid? KontoId { get; set; }
    public Guid? SchuleId { get; set; }
    public bool Bearbeitet { get; set; }
    public DateTime? BearbeitetAmUtc { get; set; }
    public Guid? BearbeitetVonId { get; set; }
}

/// <summary>Nachvollziehbarkeit sensibler Aktionen (nur anhängen, nie ändern).</summary>
public class AuditEintrag : Entitaet
{
    public DateTime ZeitUtc { get; set; }
    public Guid? AkteurId { get; set; }
    public Guid? SchuleId { get; set; }
    public string Aktion { get; set; } = string.Empty;
    public string ZielTyp { get; set; } = string.Empty;
    public Guid? ZielId { get; set; }
    public string Text { get; set; } = string.Empty;
}
