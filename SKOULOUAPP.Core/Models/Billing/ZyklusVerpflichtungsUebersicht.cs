namespace SkoulouApp.Core.Models.Billing;

/// <summary>Vorläufige Teilnahmeverpflichtungen und tatsächliche offene Rechnungen getrennt.</summary>
public sealed class ZyklusVerpflichtungsUebersicht
{
    public List<LaufendeZyklusVerpflichtung> LaufendeZyklen { get; set; } = [];
    public decimal VorlaeufigerBetrag => LaufendeZyklen.Sum(x => x.Betrag);
    public int TeilnehmerAnzahl => LaufendeZyklen.Sum(x => x.TeilnehmerAnzahl);
    public decimal OffeneRechnungenBetrag { get; set; }
    public int OffeneRechnungenAnzahl { get; set; }
    public decimal ZahlungGemeldetBetrag { get; set; }
}

public sealed record LaufendeZyklusVerpflichtung(Guid DurchlaufID, string Klasse, DateTime StartDatum,
    decimal PreisProTeilnehmer, int TeilnehmerAnzahl, decimal Betrag);
