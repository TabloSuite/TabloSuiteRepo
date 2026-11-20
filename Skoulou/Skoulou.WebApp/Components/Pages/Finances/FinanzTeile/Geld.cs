using Skoulou.Core.Entitaeten;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Services.Finanzen;

namespace Skoulou.WebApp.Components.Pages.Finances.FinanzTeile;

/// <summary>Anzeigeformate des Bereichs Finanzen (Beträge « 150.000 FCFA », Kanäle, Status).</summary>
public static class Geld
{
    /// <summary>Betrag wie überall in SKOULOU (Eingabefelder, PDF): « 150.000 FCFA ».</summary>
    public static string F(long betrag) => SkoulouKultur.Fcfa(betrag);

    /// <summary>Betrag mit Vorzeichen für das Journal.</summary>
    public static string Mit(BuchungsRichtung richtung, long betrag) => (richtung == BuchungsRichtung.Recette ? "+ " : "− ") + F(betrag);

    public static string Datum(DateOnly? datum) => datum is { } d ? SkoulouKultur.Datum(d) : "—";

    public static string Kanal(ZahlungsKanal kanal) => FinanzEingabe.KanalText(kanal);

    public static string Status(RechnungsStatus status) => FinanzEingabe.StatusText(status);

    public static string StatusArt(RechnungsStatus status) => FinanzEingabe.StatusArt(status);

    public static string KanalIcon(ZahlungsKanal kanal) => kanal switch
    {
        ZahlungsKanal.MobileMoney => "phone",
        ZahlungsKanal.Especes => "banknote",
        _ => "receipt"
    };

    /// <summary>Optionen der Zahlungswege.</summary>
    public static readonly IReadOnlyList<Shared.AuswahlOption<ZahlungsKanal>> Kanaele =
    [
        new(ZahlungsKanal.Especes, "Espèces", Icon: "banknote"), new(ZahlungsKanal.MobileMoney, "Mobile Money", Icon: "phone"),
        new(ZahlungsKanal.Virement, "Virement", Icon: "receipt"), new(ZahlungsKanal.Cheque, "Chèque", Icon: "edit"),
    ];
}
