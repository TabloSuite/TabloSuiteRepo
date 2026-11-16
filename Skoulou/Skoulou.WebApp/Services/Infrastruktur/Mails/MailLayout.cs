using System.Net;
using System.Text.RegularExpressions;

namespace Skoulou.WebApp.Services.Infrastruktur.Mails;

/// <summary>
/// Gemeinsames HTML-Layout aller Mails: tabellenbasiert, nur Inline-Styles (Outlook, Gmail, web.de),
/// Kopf Navy→Blau mit Logo « S », Fußzeile mit Kontakt. Dazu Bausteine und eine Textfassung.
/// </summary>
public static partial class MailLayout
{
    public const string Marker = "<!-- SKOULOU-MAIL-LAYOUT -->";
    private const string Navy = "#0b1b3a";
    private const string Blau = "#1d4ed8";
    private const string BlauHell = "#2563eb";
    private const string Cyan = "#06b6d4";
    private const string CyanZart = "#bae6fd";
    // Logo « S » im eNOTE-Blau (Nutzerwunsch 16.11.): wie img/skoulou-logo.svg
    private const string LogoNavy = "#1b3384";
    private const string LogoBlau = "#2547b8";
    private const string LogoTuerkis = "#0f8f86";
    private const string Text = "#0f172a";
    private const string Leise = "#64748b";
    private const string Koerper = "#334155";
    private const string Hintergrund = "#eef4ff";
    private const string Tonung = "#f7faff";
    private const string Rand = "#dbe6f8";
    private const string Schrift = "'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    /// <summary>Umschließt den Inhalt mit Kopf, getöntem Inhaltsbereich und Marken-Fuß.</summary>
    public static string Umhuelle(string titel, string inhaltHtml, string? vorschau = null)
    {
        if (inhaltHtml.Contains(Marker, StringComparison.Ordinal)) return inhaltHtml;
        var t = WebUtility.HtmlEncode(titel);
        var vorschauHtml = string.IsNullOrWhiteSpace(vorschau) ? string.Empty
            : $"<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;\">{WebUtility.HtmlEncode(vorschau)}</div>";
        return $"""
            <!DOCTYPE html>
            {Marker}
            <html lang="fr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1.0">
            <meta name="color-scheme" content="light only"><title>{t}</title></head>
            <body bgcolor="{Hintergrund}" style="margin:0;padding:0;background:{Hintergrund};font-family:{Schrift};color:{Text};">
            {vorschauHtml}
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="{Hintergrund}"><tr><td align="center" style="padding:20px 10px;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#ffffff" style="max-width:620px;background:#ffffff;border-radius:16px;overflow:hidden;">
            <tr><td bgcolor="{Navy}" style="background:{Navy};background-image:linear-gradient(90deg,{Navy} 0%,{Blau} 100%);padding:18px 20px;">
              <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
                <td style="padding-right:12px;" valign="middle"><table role="presentation" cellpadding="0" cellspacing="0" border="0" width="38" height="38" style="width:38px;height:38px;background:{LogoBlau};background-image:linear-gradient(135deg,{LogoNavy} 0%,{LogoBlau} 62%,{LogoTuerkis} 135%);border:1px solid rgba(255,255,255,.35);border-radius:11px;"><tr><td align="center" valign="middle" style="font-family:{Schrift};font-size:19px;font-weight:800;color:#ffffff;line-height:38px;">S</td></tr></table></td>
                <td valign="middle"><a href="{SkoulouMarke.Website}" style="font-family:{Schrift};color:#ffffff;font-size:21px;font-weight:800;letter-spacing:.3px;text-decoration:none;">SKOULOU</a></td>
              </tr></table>
            </td></tr>
            <tr><td bgcolor="{Cyan}" style="height:4px;line-height:4px;font-size:0;background:{Cyan};background-image:linear-gradient(90deg,{Cyan} 0%,{BlauHell} 100%);">&nbsp;</td></tr>
            <tr><td bgcolor="{Tonung}" style="background:{Tonung};padding:26px 20px 24px;">
              <h1 style="margin:0 0 18px;font-family:{Schrift};font-size:22px;line-height:1.3;font-weight:800;color:{Navy};">{t}</h1>
              <div style="font-family:{Schrift};font-size:15px;line-height:1.75;color:{Koerper};">{inhaltHtml}</div>
              <div style="margin-top:26px;border-top:1px solid {Rand};padding-top:18px;font-family:{Schrift};font-size:14px;line-height:1.6;color:{Leise};">
                Une question ? Écrivez-nous à <a href="mailto:{SkoulouMarke.KontaktEmail}" style="color:{Blau};text-decoration:none;font-weight:600;">{SkoulouMarke.KontaktEmail}</a>.
              </div>
            </td></tr>
            <tr><td bgcolor="{Navy}" style="background:{Navy};background-image:linear-gradient(135deg,{Navy} 0%,{Blau} 100%);padding:24px 20px 22px;font-family:{Schrift};">
              <div style="color:{Cyan};font-size:11px;font-weight:700;letter-spacing:1.4px;">APPRENDRE. PROGRESSER. RÉUSSIR.</div>
              <div style="margin-top:5px;color:#ffffff;font-size:20px;font-weight:800;">SKOULOU</div>
              <div style="margin-top:9px;color:{CyanZart};font-size:13px;line-height:1.6;">La gestion scolaire qui rapproche la direction, les enseignants, les élèves et les parents.</div>
              <div style="margin-top:18px;font-size:13px;line-height:1.9;color:#e2eefc;">
                <a href="{SkoulouMarke.Website}" style="color:#e2eefc;text-decoration:none;">{SkoulouMarke.Domain}</a><br>
                <a href="mailto:{SkoulouMarke.KontaktEmail}" style="color:#e2eefc;text-decoration:none;">{SkoulouMarke.KontaktEmail}</a><br>{SkoulouMarke.Ort}
              </div>
              <div style="margin-top:18px;border-top:1px solid rgba(255,255,255,.18);padding-top:14px;font-size:12px;line-height:1.8;color:#b6cdeb;">
                &copy; {DateTime.UtcNow.Year} SKOULOU, une application TabloSuite.<br>
                Cet e-mail a été envoyé automatiquement, merci de ne pas y répondre directement.
              </div>
            </td></tr>
            </table></td></tr></table>
            </body></html>
            """;
    }

    /// <summary>Absatz aus Klartext (kodiert).</summary>
    public static string Absatz(string text) => $"<p style=\"margin:0 0 14px;\">{WebUtility.HtmlEncode(text)}</p>";

    /// <summary>Großer Knopf mit Link.</summary>
    public static string Knopf(string url, string beschriftung)
        => $"""<p style="margin:22px 0;"><a href="{WebUtility.HtmlEncode(url)}" style="display:inline-block;padding:13px 22px;border-radius:10px;background:{Blau};color:#ffffff;font-weight:700;text-decoration:none;">{WebUtility.HtmlEncode(beschriftung)}</a></p>""";

    /// <summary>Hervorgehobener Kasten (Inhalt ist bereits HTML).</summary>
    public static string InfoKasten(string html)
        => $"""<div style="margin:16px 0;padding:14px 16px;border-radius:12px;background:#ffffff;border:1px solid {Rand};">{html}</div>""";

    [GeneratedRegex(@"<(style|head|title)[^>]*>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex KopfBloecke();

    [GeneratedRegex(@"<div style=""display:none[^""]*"">.*?</div>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Vorschau();

    [GeneratedRegex(@"<a [^>]*href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Links();

    [GeneratedRegex(@"<(br|/p|/div|/h1|/h2|/tr|/li)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Umbrueche();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    /// <summary>Textfassung (Spamfilter bewerten reine HTML-Mails schlechter).</summary>
    public static string AlsText(string html)
    {
        var text = KopfBloecke().Replace(html, string.Empty);
        text = Vorschau().Replace(text, string.Empty);
        text = Links().Replace(text, "$2 ($1)");
        text = Umbrueche().Replace(text, "\n");
        text = Tags().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        var zeilen = text.Split('\n').Select(z => z.Trim()).ToList();
        var ergebnis = new List<string>();
        foreach (var z in zeilen)
        {
            if (z.Length == 0 && (ergebnis.Count == 0 || ergebnis[^1].Length == 0)) continue;
            ergebnis.Add(z);
        }
        return string.Join('\n', ergebnis).Trim();
    }
}
