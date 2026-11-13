namespace Skoulou.Core.Werkzeuge;

/// <summary>
/// Zeit einer Schule: gespeichert wird in UTC, angezeigt und eingegeben in der Zeitzone der Schule
/// (Standard « Africa/Lome »). Kalenderdaten sind <see cref="DateOnly"/>.
/// </summary>
public sealed class SchulZeit
{
    public const string StandardZone = "Africa/Lome";

    private readonly TimeProvider zeit;

    public SchulZeit(TimeProvider zeit, string? zonenId = null)
    {
        this.zeit = zeit;
        Zone = FindeZone(zonenId);
    }

    public TimeZoneInfo Zone { get; }

    /// <summary>Zeitzone nach IANA-Id; unbekannt oder leer → Africa/Lome, notfalls UTC.</summary>
    public static TimeZoneInfo FindeZone(string? zonenId)
    {
        foreach (var id in new[] { zonenId, StandardZone })
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    public DateTime JetztUtc => zeit.GetUtcNow().UtcDateTime;

    /// <summary>Lokale Uhrzeit der Schule.</summary>
    public DateTime JetztLokal => ZuLokal(JetztUtc);

    /// <summary>Heutiges Datum in der Schule.</summary>
    public DateOnly Heute => DateOnly.FromDateTime(JetztLokal);

    /// <summary>UTC → lokale Zeit der Schule.</summary>
    public DateTime ZuLokal(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>Lokale Zeit der Schule → UTC.</summary>
    public DateTime ZuUtc(DateTime lokal)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(lokal, DateTimeKind.Unspecified), Zone);

    /// <summary>Kalendertag (lokal) eines UTC-Zeitpunkts.</summary>
    public DateOnly TagVon(DateTime utc) => DateOnly.FromDateTime(ZuLokal(utc));

    /// <summary>Beginn eines lokalen Tages in UTC (inklusive).</summary>
    public DateTime TagesbeginnUtc(DateOnly tag) => ZuUtc(tag.ToDateTime(TimeOnly.MinValue));

    /// <summary>Beginn des Folgetages in UTC (exklusive Obergrenze).</summary>
    public DateTime TagesendeUtc(DateOnly tag) => ZuUtc(tag.AddDays(1).ToDateTime(TimeOnly.MinValue));

    /// <summary>Anzeige « 12/11/2026 à 08:30 » in lokaler Zeit.</summary>
    public string AlsText(DateTime utc)
    {
        var lokal = ZuLokal(utc);
        return $"{lokal.ToString("dd/MM/yyyy", SkoulouKultur.Kultur)} à {lokal.ToString("HH:mm", SkoulouKultur.Kultur)}";
    }
}
