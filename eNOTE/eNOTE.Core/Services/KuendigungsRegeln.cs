namespace ENote.Core.Services;

/// <summary>
/// Kündigung und Kontolöschung (Entscheidungen 11.11.2026): Die Kündigung wirkt zum Ende der
/// laufenden, bereits berechneten Periode (in der Probezeit zum Probe-Ende) und ist bis dahin
/// – und auch danach – zurücknehmbar. Die Löschung hat 30 Tage Bedenkzeit, der Laden ist so lange
/// gesperrt; sie ist nur ohne offene Rechnung möglich.
/// </summary>
public static class KuendigungsRegeln
{
    public const int LoeschFristTage = 30;
    public const int GrundMax = 500;

    /// <summary>Zeitpunkt, zu dem eine heute ausgesprochene Kündigung wirkt.</summary>
    public static DateTime WirksamZum(DateTime jetztUtc, DateTime? probeBisUtc, IEnumerable<(DateTime VonUtc, DateTime BisUtc)> perioden)
    {
        if (probeBisUtc is DateTime probe && probe > jetztUtc) return VertragsRegeln.BeginnNachProbe(probe);
        var laufend = perioden.Where(p => p.VonUtc <= jetztUtc && p.BisUtc > jetztUtc).Select(p => (DateTime?)p.BisUtc).Max();
        return laufend ?? jetztUtc;
    }

    public static DateTime LoeschungAb(DateTime beantragtUtc) => beantragtUtc.AddDays(LoeschFristTage);

    /// <summary>Neue Perioden dürfen nur vor dem Kündigungszeitpunkt beginnen und nicht während einer beantragten Löschung.</summary>
    public static bool DarfPeriodeBeginnen(DateTime startUtc, DateTime? gekuendigtZumUtc, DateTime? loeschungBeantragtUtc)
        => loeschungBeantragtUtc is null && (gekuendigtZumUtc is not DateTime zum || startUtc < zum);
}
