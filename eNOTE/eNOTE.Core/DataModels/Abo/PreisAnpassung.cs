namespace ENote.Core.DataModels.Abo;

/// <summary>
/// Geplante (oder sofortige) Änderung des Standard-Monatspreises. Bei einer Erhöhung steigen auch
/// alle individuellen Preise um denselben Betrag; kein Preis liegt unter dem Standard. Die zugehörigen
/// Preisstände (<see cref="MonatsPreisStand.PreisAnpassungId"/>) gelten ab <see cref="GueltigAbUtc"/>.
/// </summary>
public class PreisAnpassung
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public decimal AlterStandard { get; set; }
    public decimal NeuerStandard { get; set; }
    public DateTime GueltigAbUtc { get; set; }
    public DateTime ErstelltUtc { get; set; } = DateTime.UtcNow;
    public Guid? ErstelltVonId { get; set; }
    /// <summary>Anzahl Läden mit individuellem Preis, die mit angepasst wurden.</summary>
    public int AngepassteLaeden { get; set; }
    public int MailsGesendet { get; set; }
    public Guid? AnkuendigungId { get; set; }
    public bool IstAufgehoben { get; set; }
    /// <summary>Wann und von wem die geplante Änderung aufgehoben wurde (Verlauf in Administration › Tarifs).</summary>
    public DateTime? AufgehobenUtc { get; set; }
    public Guid? AufgehobenVonId { get; set; }
    /// <summary>Entwarnung an die Besitzer („la hausse annoncée n'aura pas lieu“): Banner in der App.</summary>
    public Guid? EntwarnungAnkuendigungId { get; set; }
    /// <summary>Anzahl der Entwarnungs-Mails (0 = nur in der App).</summary>
    public int EntwarnungMails { get; set; }
    /// <summary>Salon-Felder (IndividuellerMonatsPreis) sind auf den neuen Stand gebracht.</summary>
    public bool IstAngewendet { get; set; }
}
