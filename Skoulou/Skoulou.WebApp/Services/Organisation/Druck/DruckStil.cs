using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Skoulou.WebApp.Services.Organisation.Druck;

/// <summary>Kopf einer Druckseite: Schulname (offizielle Bezeichnung) und Logo (bereits geprüft).</summary>
public sealed record SchulKopf(string Name, byte[]? Logo);

/// <summary>Gemeinsame Farben, Schriften und Bausteine der PDFs des Bereichs (Marke SKOULOU).</summary>
internal static class DruckStil
{
    public const string Primaer = "#1d4ed8";
    public const string PrimaerDunkel = "#12245a";
    public const string PrimaerZart = "#eaf1ff";
    public const string Akzent = "#0891b2";
    public const string Text = "#0f1a33";
    public const string Leise = "#56657f";
    public const string Rand = "#cbd5e3";
    public const string RandZart = "#e2e8f2";
    public const string Flaeche = "#f7f9fd";
    public const string Warn = "#b45309";
    public const string WarnZart = "#fdf3e3";

    /// <summary>Gut lesbare Festbreitenschrift für Identifiants und Passwörter (Rückfall: Standardschrift).</summary>
    public static readonly string[] Festbreite = ["Consolas", "Courier New", "DejaVu Sans Mono"];

    /// <summary>Bild nur übernehmen, wenn QuestPDF es lesen kann (sonst ohne Bild drucken statt abbrechen).</summary>
    public static byte[]? PruefeBild(byte[]? bild)
    {
        if (bild is null || bild.Length == 0) return null;
        try
        {
            Image.FromBinaryData(bild);
            return bild;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Seitenkopf: Logo, Schulname, Titel und Untertitel.</summary>
    public static void Kopf(IContainer container, SchulKopf schule, string titel, string untertitel)
    {
        container.PaddingBottom(4, Unit.Millimetre).BorderBottom(1).BorderColor(RandZart).PaddingBottom(3, Unit.Millimetre).Row(row =>
        {
            if (schule.Logo is { } logo)
            {
                row.ConstantItem(16, Unit.Millimetre).Height(16, Unit.Millimetre).AlignMiddle().Image(logo).FitArea();
                row.ConstantItem(4, Unit.Millimetre);
            }
            row.RelativeItem().AlignMiddle().Column(col =>
            {
                col.Item().Text(schule.Name).FontSize(10).SemiBold().FontColor(Leise);
                col.Item().Text(titel).FontSize(16).Bold().FontColor(PrimaerDunkel);
                col.Item().Text(untertitel).FontSize(9).FontColor(Leise);
            });
            row.ConstantItem(30, Unit.Millimetre).AlignRight().AlignMiddle().Text("SKOULOU").FontSize(11).Bold().FontColor(Primaer).LetterSpacing(0.08f);
        });
    }

    /// <summary>Seitenfuß « Page x / y ».</summary>
    public static void Fuss(IContainer container, string links)
    {
        container.PaddingTop(2, Unit.Millimetre).Row(row =>
        {
            row.RelativeItem().Text(links).FontSize(7.5f).FontColor(Leise);
            row.ConstantItem(30, Unit.Millimetre).AlignRight().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Leise));
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        });
    }

    /// <summary>Gestrichelte Schnittlinie (SVG), volle Breite.</summary>
    public static void Schnittlinie(IContainer container)
        => container.Height(2).Svg($"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 600 2" preserveAspectRatio="none"><line x1="0" y1="1" x2="600" y2="1" stroke="{Rand}" stroke-width="1.5" stroke-dasharray="6 4"/></svg>""").FitArea();

    /// <summary>Initialen in einem Kreis (wenn kein Foto vorhanden ist).</summary>
    public static void Initialen(IContainer container, string vorname, string nachname, float groesseMm)
    {
        var initialen = $"{Erster(vorname)}{Erster(nachname)}";
        container.Width(groesseMm, Unit.Millimetre).Height(groesseMm, Unit.Millimetre).Layers(l =>
        {
            l.Layer().Svg($"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><circle cx="50" cy="50" r="50" fill="{PrimaerZart}"/></svg>""").FitArea();
            l.PrimaryLayer().AlignCenter().AlignMiddle().Text(initialen).FontSize(groesseMm * 0.9f).Bold().FontColor(Primaer);
        });
    }

    private static string Erster(string text) => string.IsNullOrWhiteSpace(text) ? string.Empty : char.ToUpperInvariant(text.Trim()[0]).ToString();

    /// <summary>Gemeinsame Seiteneinstellungen.</summary>
    public static void Seite(PageDescriptor page, PageSize groesse, float randMm)
    {
        page.Size(groesse);
        page.Margin(randMm, Unit.Millimetre);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(t => t.FontSize(9.5f).FontColor(Text));
    }
}
