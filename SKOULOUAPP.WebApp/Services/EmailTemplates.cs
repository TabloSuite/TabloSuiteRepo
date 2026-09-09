namespace HalloApp.WebApp.Services;

/// <summary>
/// Einheitliche HTML-Vorlage für alle ausgehenden E-Mails.
/// Tabellenbasiertes Layout mit reinen Inline-Styles, damit die Darstellung
/// in allen gängigen Clients (Outlook, Gmail, web.de, GMX) funktioniert.
/// </summary>
internal static class EmailTemplates
{
    private const string BrandName = "SKOULOUAPP";
    private const string BrandClaim = "APPRENDRE. PROGRESSER. RÉUSSIR.";
    private const string BrandDescription = "L'espace d'apprentissage qui rapproche les écoles, les enseignants et les élèves.";
    private const string SiteUrl = "https://skoulouapp.com";
    private const string SiteLabel = "www.skoulouapp.com";
    private const string SupportEmail = "kontakt@skoulouapp.com";
    private const string Phone = "+49 176 61221600";
    private const string Address = "Lomé, Togo";

    // Markenfarben aus der Website-Navigation bzw. dem Footer.
    private const string ColorNavy = "#0b1b3a";
    private const string ColorBlue = "#1d4ed8";
    private const string ColorBlueLight = "#2563eb";
    private const string ColorAccent = "#38bdf8";
    private const string ColorAccentSoft = "#bae6fd";

    private const string ColorText = "#0f172a";
    private const string ColorMuted = "#64748b";
    private const string ColorBody = "#334155";
    private const string ColorBackground = "#eef4ff";
    private const string ColorSurface = "#ffffff";
    private const string ColorContentTint = "#f7faff";
    private const string ColorBorder = "#dbe6f8";

    private const string FontStack = "'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    /// <summary>
    /// Umschließt den Inhalt mit farbigem Header, leicht getöntem Inhaltsbereich
    /// und dunkelblauem Marken-Footer.
    /// </summary>
    /// <param name="heading">Überschrift im Inhaltsbereich.</param>
    /// <param name="innerHtml">Vorbereiteter HTML-Inhalt.</param>
    /// <param name="preheader">Optionaler Vorschautext für die Posteingangsansicht.</param>
    public static string Wrap(string heading, string innerHtml, string? preheader = null) =>
        $"""
        <!DOCTYPE html>
        <html lang="fr">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <meta name="color-scheme" content="light only">
            <meta name="supported-color-schemes" content="light only">
            <title>{heading}</title>
        </head>
        <body bgcolor="{ColorBackground}" style="margin:0;padding:0;width:100%;min-width:100%;background:{ColorBackground};font-family:{FontStack};color:{ColorText};-webkit-text-size-adjust:100%;-ms-text-size-adjust:100%;-webkit-font-smoothing:antialiased;">
            {BuildPreheader(preheader)}
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="{ColorBackground}" style="width:100%;background:{ColorBackground};">
                <tr>
                    <td align="center" style="padding:20px 10px;">
                        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="{ColorSurface}" style="width:100%;max-width:620px;background:{ColorSurface};border-radius:16px;overflow:hidden;">

                            <!-- Header: blaue Navigationsleiste mit Logo-Badge -->
                            <tr>
                                <td bgcolor="{ColorNavy}" style="background:{ColorNavy};background-image:linear-gradient(90deg,{ColorNavy} 0%,{ColorBlue} 100%);padding:18px 20px;">
                                    <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                                        <tr>
                                            <td style="padding-right:12px;" valign="middle">
                                                <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="38" height="38" style="width:38px;height:38px;background:{ColorAccent};border-radius:11px;">
                                                    <tr>
                                                        <td align="center" valign="middle" style="font-family:{FontStack};font-size:19px;font-weight:800;color:{ColorNavy};line-height:38px;">H</td>
                                                    </tr>
                                                </table>
                                            </td>
                                            <td valign="middle">
                                                <a href="{SiteUrl}" style="font-family:{FontStack};color:#ffffff;font-size:21px;font-weight:800;letter-spacing:.3px;text-decoration:none;">{BrandName}</a>
                                            </td>
                                        </tr>
                                    </table>
                                </td>
                            </tr>

                            <!-- Farbiger Übergangsstreifen -->
                            <tr>
                                <td bgcolor="{ColorAccent}" style="height:4px;line-height:4px;font-size:0;background:{ColorAccent};background-image:linear-gradient(90deg,{ColorAccent} 0%,{ColorBlueLight} 100%);">&nbsp;</td>
                            </tr>

                            <!-- Inhalt: leicht blau getönte Fläche -->
                            <tr>
                                <td bgcolor="{ColorContentTint}" style="background:{ColorContentTint};padding:26px 20px 24px;">
                                    <h1 style="margin:0 0 18px;font-family:{FontStack};font-size:22px;line-height:1.3;font-weight:800;color:{ColorNavy};">{heading}</h1>
                                    <div style="font-family:{FontStack};font-size:15px;line-height:1.75;color:{ColorBody};">{innerHtml}</div>
                                    <div style="margin-top:26px;border-top:1px solid {ColorBorder};padding-top:18px;font-family:{FontStack};font-size:14px;line-height:1.6;color:{ColorMuted};">
                                        Une question ? Écris-nous à
                                        <a href="mailto:{SupportEmail}" style="color:{ColorBlue};text-decoration:none;font-weight:600;">{SupportEmail}</a>.
                                    </div>
                                </td>
                            </tr>

                            <!-- Footer: dunkelblauer Markenbereich -->
                            <tr>
                                <td bgcolor="{ColorNavy}" style="background:{ColorNavy};background-image:linear-gradient(135deg,{ColorNavy} 0%,{ColorBlue} 100%);padding:24px 20px 22px;">
                                    <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%">
                                        <tr>
                                            <td width="52" valign="top" style="padding-right:14px;">
                                                <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="46" height="46" style="width:46px;height:46px;background:{ColorAccent};border-radius:13px;">
                                                    <tr>
                                                        <td align="center" valign="middle" style="font-family:{FontStack};font-size:22px;font-weight:800;color:{ColorNavy};line-height:46px;">H</td>
                                                    </tr>
                                                </table>
                                            </td>
                                            <td valign="top" style="font-family:{FontStack};">
                                                <div style="color:{ColorAccent};font-size:11px;font-weight:700;letter-spacing:1.4px;">{BrandClaim}</div>
                                                <div style="margin-top:5px;color:#ffffff;font-size:20px;font-weight:800;letter-spacing:.3px;">{BrandName}</div>
                                                <div style="margin-top:9px;color:{ColorAccentSoft};font-size:13px;line-height:1.6;">{BrandDescription}</div>
                                            </td>
                                        </tr>
                                    </table>

                                    <div style="margin-top:24px;font-family:{FontStack};color:{ColorAccent};font-size:11px;font-weight:700;letter-spacing:1.4px;">CONTACT</div>
                                    <div style="margin-top:10px;font-family:{FontStack};font-size:13px;line-height:1.9;color:#e2eefc;">
                                        <a href="{SiteUrl}" style="color:#e2eefc;text-decoration:none;">{SiteLabel}</a><br/>
                                        <a href="mailto:{SupportEmail}" style="color:#e2eefc;text-decoration:none;">{SupportEmail}</a><br/>
                                        {Phone}<br/>
                                        {Address}
                                    </div>

                                    <div style="margin-top:22px;border-top:1px solid rgba(255,255,255,.18);padding-top:16px;font-family:{FontStack};font-size:12px;line-height:1.8;color:#b6cdeb;">
                                        &copy; {DateTime.UtcNow.Year} {BrandName}. Tous droits réservés.<br/>
                                        Conçu avec passion pour mieux apprendre.<br/>
                                        Cet e-mail a été envoyé automatiquement, merci de ne pas y répondre directement.
                                    </div>
                                </td>
                            </tr>

                        </table>
                    </td>
                </tr>
            </table>
        </body>
        </html>
        """;

    /// <summary>
    /// Zentrierter Call-to-Action-Button im Markenstil.
    /// </summary>
    public static string Button(string url, string label) =>
        $"""
        <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:28px auto;">
            <tr>
                <td align="center" bgcolor="{ColorBlue}" style="border-radius:9999px;background:{ColorBlue};background-image:linear-gradient(135deg,{ColorBlueLight} 0%,{ColorNavy} 100%);">
                    <a href="{url}" style="display:inline-block;padding:14px 34px;font-family:{FontStack};font-size:15px;font-weight:700;color:#ffffff;text-decoration:none;border-radius:9999px;">{label}</a>
                </td>
            </tr>
        </table>
        """;

    /// <summary>
    /// Hervorgehobener Infokasten für Zugangsdaten oder wichtige Hinweise.
    /// </summary>
    public static string InfoBox(string innerHtml) =>
        $"""
        <div style="margin:24px 0;padding:16px 20px;background:#e8f2ff;border-left:4px solid {ColorBlueLight};border-radius:10px;font-family:{FontStack};font-size:14px;line-height:1.7;color:{ColorBody};">{innerHtml}</div>
        """;

    /// <summary>
    /// Erzeugt aus HTML eine einfache Klartext-Variante.
    /// Ein vorhandener Text-Teil verbessert die Spam-Bewertung deutlich,
    /// weil reine HTML-Mails von vielen Filtern abgewertet werden.
    /// </summary>
    public static string ToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = System.Text.RegularExpressions.Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, "<br\\s*/?>|</p>|</div>|</tr>|</h[1-6]>", "\n",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text);
        text = System.Text.RegularExpressions.Regex.Replace(text, "[ \\t]+", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, "(\\s*\\n\\s*){2,}", "\n\n");

        return text.Trim();
    }

    private static string BuildPreheader(string? preheader)
        => string.IsNullOrWhiteSpace(preheader)
            ? string.Empty
            : $"""<div style="display:none;max-height:0;overflow:hidden;opacity:0;mso-hide:all;">{preheader}</div>""";
}
