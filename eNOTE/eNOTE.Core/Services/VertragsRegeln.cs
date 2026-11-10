using ENote.Core.DataModels.Abo;

namespace ENote.Core.Services;

public static class VertragsRegeln
{
    public static readonly AboIntervall[] Intervalle = [AboIntervall.Monatlich, AboIntervall.DreiMonate, AboIntervall.SechsMonate, AboIntervall.Jaehrlich];
    public static decimal Rabatt(AboIntervall intervall) => intervall switch
    {
        AboIntervall.Monatlich => 0m, AboIntervall.DreiMonate => 5m,
        AboIntervall.SechsMonate => 7.5m, AboIntervall.Jaehrlich => 10m,
        _ => throw new ArgumentOutOfRangeException(nameof(intervall))
    };
    public static decimal Preis(decimal monatsBasis, AboIntervall intervall)
    {
        if (monatsBasis <= 0) throw new ArgumentOutOfRangeException(nameof(monatsBasis));
        return decimal.Round(monatsBasis * Monate(intervall) * (1 - Rabatt(intervall) / 100), 0, MidpointRounding.AwayFromZero);
    }
    public static DateTime SperreAb(DateTime faellig) => faellig.Date.AddDays(15);

    /// <summary>
    /// Vertragsbeginn nach der Probezeit: der erste Tag danach um 00:00. Probezeit-Enden werden als
    /// „bis einschließlich Tagesende“ gespeichert (z. B. 31.01. 23:59:59,9999999) → Beginn 01.02. 00:00;
    /// ein exakter Tagesbeginn (00:00) gilt bereits als Beginn.
    /// </summary>
    public static DateTime BeginnNachProbe(DateTime probeBis)
        => probeBis.TimeOfDay == TimeSpan.Zero ? probeBis : DateTime.SpecifyKind(probeBis.Date.AddDays(1), probeBis.Kind);

    /// <summary>Ein gewähltes Probezeit-Datum gilt einschließlich dieses Tages (Ende = letzter Tick des Tages).</summary>
    public static DateTime ProbeEndeEinschliesslich(DateTime tag)
        => DateTime.SpecifyKind(tag.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
    public static bool IstTeilperiode(DateTime start) => start.Day != 1 || start.TimeOfDay != TimeSpan.Zero;
    public static DateTime PeriodenEnde(DateTime start, AboIntervall intervall)
        => IstTeilperiode(start) ? new DateTime(start.Year, start.Month, 1, 0, 0, 0, start.Kind).AddMonths(1) : start.AddMonths(Monate(intervall));
    public static decimal Teilpreis(decimal basis, DateTime start)
        => decimal.Round(basis * (decimal)(PeriodenEnde(start, AboIntervall.Monatlich) - start).TotalDays
            / DateTime.DaysInMonth(start.Year, start.Month), 0, MidpointRounding.AwayFromZero);
    public static int Monate(AboIntervall intervall) => intervall switch
    {
        AboIntervall.Monatlich => 1, AboIntervall.DreiMonate => 3,
        AboIntervall.SechsMonate => 6, AboIntervall.Jaehrlich => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(intervall))
    };
    public static string Bezeichnung(AboIntervall intervall) => intervall switch
    {
        AboIntervall.Monatlich => "Mensuel", AboIntervall.DreiMonate => "Trimestriel",
        AboIntervall.SechsMonate => "Semestriel", AboIntervall.Jaehrlich => "Annuel", _ => "Non défini"
    };
    public static string ZahlungsText(AboZahlungStatus status) => status switch
    {
        AboZahlungStatus.Bestaetigt => "Payé", AboZahlungStatus.Gemeldet => "Non payé · paiement signalé",
        AboZahlungStatus.Abgelehnt => "Non payé · paiement refusé", _ => "Non payé"
    };
}
