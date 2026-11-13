using ENote.Core.Services.Kunden;

namespace ENote.Core.Services.Kasse;

/// <summary>Regeln für schon erfasste, noch nicht bezahlte Leistungen eines anwesenden Kunden (« Compte de présence »).</summary>
public static class AufenthaltRegeln
{
    public const int GrundMax = 120;
    public const int GrundMin = 3;
    public const int ZeilenJeErfassungMax = 100;
    public const int OffeneZeilenMax = 300;
    public const int MengeMax = 999;

    /// <summary>Eigene Erfassung darf kurz danach ohne Freigabe des Besitzers zurückgenommen werden (Tippfehler).</summary>
    public static readonly TimeSpan SofortKorrektur = TimeSpan.FromMinutes(15);

    /// <summary>Schnellauswahl für den Grund der Rücknahme.</summary>
    public static readonly IReadOnlyList<string> GruendeVorschlaege = ["Erreur de saisie", "Prestation non réalisée", "Client parti sans payer"];

    /// <summary>Grund bereinigen und prüfen (Pflicht, eine Zeile, <see cref="GrundMin"/>–<see cref="GrundMax"/> Zeichen).</summary>
    public static string PruefeGrund(string? grund)
    {
        var g = KundenBesuchRegeln.Bereinige(grund).Replace('\n', ' ');
        if (g.Length < GrundMin) throw new ArgumentException("Indiquez le motif du retrait.");
        if (g.Length > GrundMax) throw new ArgumentException($"Motif : {GrundMax} caractères au maximum.");
        return g;
    }

    /// <summary>Kurzes Datum für Bon und Ticket, z. B. « 11/11 ».</summary>
    public static string Tag(DateOnly tag) => tag.ToString("dd'/'MM");

    /// <summary>Überschrift je Tag in der Übersicht, z. B. « Aujourd'hui », « Hier », « Lundi 10/11 ».</summary>
    public static string TagUeberschrift(DateOnly tag, DateOnly heute)
    {
        if (tag == heute) return "Aujourd'hui";
        if (tag == heute.AddDays(-1)) return "Hier";
        var name = System.Globalization.CultureInfo.GetCultureInfo("fr-FR").DateTimeFormat.GetDayName(tag.DayOfWeek);
        return $"{char.ToUpperInvariant(name[0])}{name[1..]} {Tag(tag)}";
    }
}
