namespace Skoulou.Core.Entitaeten;

/// <summary>Art einer Ermäßigung.</summary>
public enum ErmaessigungsArt
{
    Montant = 0,
    Pourcentage = 1
}

/// <summary>Zahlungsweg.</summary>
public enum ZahlungsKanal
{
    Especes = 0,
    MobileMoney = 1,
    Virement = 2,
    Cheque = 3
}

/// <summary>Richtung im Kassenjournal.</summary>
public enum BuchungsRichtung
{
    Recette = 0,
    Depense = 1
}

/// <summary>Herkunft einer Buchung.</summary>
public enum BuchungsHerkunft
{
    Manuelle = 0,
    Paiement = 1,
    AnnulationPaiement = 2,
    FactureSkoulou = 3,
    Annulation = 4
}

/// <summary>Zustand einer Plattformrechnung.</summary>
public enum RechnungsStatus
{
    Ouverte = 0,
    PaiementDeclare = 1,
    Payee = 2,
    Contestee = 3,
    Annulee = 4
}
