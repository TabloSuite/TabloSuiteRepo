using System.Text;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Salons;
using QRCoder;

namespace ENote.WebApp.Services;

/// <summary>Rendert einen Kassenbon als schlanke HTML-Druckansicht, die sofort window.print() aufruft.</summary>
public static class BonDruckHtml
{
    public static string Erzeuge(Verkauf verkauf, Salon salon, string kassiererName, string? qrLinkZiel = null)
    {
        static string E(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);

        var qrHtml = string.Empty;
        if (!string.IsNullOrWhiteSpace(qrLinkZiel))
        {
            var qrBytes = ErzeugeQrCode(qrLinkZiel);
            if (qrBytes is not null)
            {
                qrHtml = $"<div class=\"c\" style=\"margin-top:8px;\"><img src=\"data:image/png;base64,{Convert.ToBase64String(qrBytes)}\" style=\"width:24mm;height:24mm;\" alt=\"QR\" /></div>";
            }
        }

        var pos = new StringBuilder();
        foreach (var p in verkauf.Positionen.OrderBy(p => p.ErbrachtAm is null).ThenBy(p => p.ErbrachtAm))
        {
            var rabatt = p.RabattBetrag > 0 ? $" <span class=\"rabatt\">(-{p.RabattBetrag:N0} F)</span>" : string.Empty;
            var tag = p.ErbrachtAm is DateOnly t ? $" · {ENote.Core.Services.Kasse.AufenthaltRegeln.Tag(t)}" : string.Empty;
            pos.AppendLine($"<tr><td>{p.Menge}× {E(p.LeistungName)}{tag}{rabatt}</td><td class=\"r\">{p.ZeilenSumme:N0} F</td></tr>");
        }

        var remise = verkauf.RabattSumme > 0
            ? $"<tr><td>Remise</td><td class=\"r rabatt\">-{verkauf.RabattSumme:N0} F</td></tr>"
            : string.Empty;

        var anteile = ENote.Core.Services.Kasse.KassenRechner.Anteile(verkauf);
        var zahlung = anteile.Count == 1
            ? E(ZahlungsTexte.Name(anteile[0]))
            : string.Join(" + ", anteile.Select(a => $"{E(ZahlungsTexte.Name(a))} {a.Betrag:N0} F"));
        var rueck = verkauf.Gegeben is not null
            ? $"<div class=\"meta\">Espèces reçues : {verkauf.Gegeben:N0} F &nbsp;·&nbsp; Monnaie : {verkauf.Rueckgeld:N0} F</div>"
            : string.Empty;

        var storno = verkauf.StornoVonVerkaufId is Guid origId
            ? $"<div class=\"c storno\">REMBOURSEMENT</div><div class=\"meta\">Vente d'origine n° {origId.ToString()[..8].ToUpperInvariant()}</div><div class=\"meta\">Motif : {E(verkauf.StornoGrund)}</div>"
            : string.Empty;
        var zahlLabel = verkauf.IstStorno ? "Remboursé en" : "Paiement";
        var fuss = verkauf.IstStorno ? "Document de remboursement – à conserver." : "Merci de votre visite !";

        var slogan = string.IsNullOrWhiteSpace(salon.Slogan) ? string.Empty : $"<div class=\"c slogan\">« {E(salon.Slogan)} »</div>";
        var adresse = string.IsNullOrWhiteSpace(salon.Adresse) ? E(salon.Standort) : E(salon.Adresse);
        var adresseHtml = string.IsNullOrWhiteSpace(adresse) ? string.Empty : $"<div class=\"c\">{adresse}</div>";
        var tel = string.IsNullOrWhiteSpace(salon.TelefonNummer) ? string.Empty : $"<div class=\"c\">Tél : {E(salon.TelefonNummer)}</div>";

        return $$"""
<!DOCTYPE html>
<html lang="fr">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>Reçu {{verkauf.Id.ToString()[..8].ToUpperInvariant()}}</title>
<style>
  :root { color-scheme: light; }
  * { box-sizing: border-box; }
  @page { size: 80mm 200mm; margin: 4mm; }
  html, body { width: 80mm; margin: 0; padding: 0; }
  body { font-family: Arial, system-ui, sans-serif; background: #fff; color: #000; font-size: 12px; }
  .bon { width: 72mm; margin: 0; padding: 2mm 1mm; }
  .c { text-align: center; }
  .name { font-size: 16px; font-weight: 700; }
  .slogan { font-style: italic; font-size: 10px; }
  .meta { font-size: 10px; }
  hr { border: none; border-top: 1px dashed #999; margin: 8px 0; }
  table { width: 100%; border-collapse: collapse; }
  td { padding: 1px 0; vertical-align: top; }
  td.r { text-align: right; white-space: nowrap; }
  .rabatt { color: #047857; }
  .total td { font-weight: 700; font-size: 14px; padding-top: 4px; }
  .storno { font-size: 15px; font-weight: 700; letter-spacing: .08em; border: 2px solid #000; padding: 3px 0; margin-bottom: 4px; }
  .foot { text-align: center; font-style: italic; margin-top: 10px; font-size: 11px; }
  @media print { .no-print { display: none; } }
  .no-print { width: 72mm; margin: 8px 0; text-align: center; }
  .no-print button { padding: 10px 18px; font-size: 14px; border: none; border-radius: 8px; background: #1e3a8a; color: #fff; cursor: pointer; }
</style>
</head>
<body>
  <div class="no-print"><button onclick="window.print()">🖨️ Imprimer</button></div>
  <div class="bon">
    <div class="c name">{{E(salon.Name)}}</div>
    {{slogan}}
    {{adresseHtml}}
    {{tel}}
    <hr />
    {{storno}}
    <div class="meta">Reçu n° {{verkauf.Id.ToString()[..8].ToUpperInvariant()}}</div>
    <div class="meta">{{ENote.Core.Services.Zeitzonen.ImLaden(verkauf.ZeitUtc, ENote.Core.Services.Zeitzonen.Fuer(salon.Land)):dd.MM.yyyy HH:mm}}</div>
    <div class="meta">Servi par : {{E(kassiererName)}}</div>
    {{(verkauf.Kunde is { } kunde ? $"<div class=\"meta\">Client : {E(kunde.Name)}</div>" : "")}}
    <hr />
    <table>
      <tbody>
        {{pos}}
        {{remise}}
        <tr class="total"><td>TOTAL</td><td class="r">{{verkauf.GesamtSumme:N0}} F</td></tr>
      </tbody>
    </table>
    <div class="meta" style="margin-top:6px;">{{zahlLabel}} : {{zahlung}}</div>
    {{rueck}}
    {{qrHtml}}
    <div class="foot">{{fuss}}</div>
  </div>
  <script>window.addEventListener('load', function () { setTimeout(function () { window.print(); }, 150); });</script>
</body>
</html>
""";
    }

    /// <summary>Erzeugt einen QR-Code als PNG-Bytes.</summary>
    private static byte[]? ErzeugeQrCode(string inhalt)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            using var daten = generator.CreateQrCode(inhalt, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(daten);
            return png.GetGraphic(20);
        }
        catch
        {
            return null; // QR darf den Bon nie verhindern.
        }
    }
}
