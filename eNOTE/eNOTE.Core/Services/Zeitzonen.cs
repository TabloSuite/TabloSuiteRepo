using System.Collections.Concurrent;

namespace ENote.Core.Services;

/// <summary>
/// Ortszeit eines Ladens aus seinem Land (französischer Name wie in <see cref="DataModels.Salons.Laender"/>).
/// Gespeichert wird immer UTC; angezeigt und nach Tagen gruppiert wird in der Zeit des Ladens
/// (Togo UTC+0, Bénin/Cameroun UTC+1 …). Unbekanntes Land: Lomé (UTC+0), wie bisher auf dem Server.
/// </summary>
public static class Zeitzonen
{
    /// <summary>IANA-Zone + fester Versatz als Rückfall, falls das System die Zone nicht kennt (Afrika: keine Sommerzeit).</summary>
    private static readonly Dictionary<string, (string Iana, double Stunden)> Laender = new(StringComparer.OrdinalIgnoreCase)
    {
        // UTC+0
        ["Togo"] = ("Africa/Lome", 0), ["Burkina Faso"] = ("Africa/Ouagadougou", 0), ["Côte d'Ivoire"] = ("Africa/Abidjan", 0),
        ["Ghana"] = ("Africa/Accra", 0), ["Mali"] = ("Africa/Bamako", 0), ["Sénégal"] = ("Africa/Dakar", 0),
        ["Gambie"] = ("Africa/Banjul", 0), ["Guinée"] = ("Africa/Conakry", 0), ["Guinée-Bissau"] = ("Africa/Bissau", 0),
        ["Libéria"] = ("Africa/Monrovia", 0), ["Mauritanie"] = ("Africa/Nouakchott", 0), ["Sierra Leone"] = ("Africa/Freetown", 0),
        ["Sao Tomé-et-Principe"] = ("Africa/Sao_Tome", 0),
        // UTC−1
        ["Cap-Vert"] = ("Atlantic/Cape_Verde", -1),
        // UTC+1
        ["Bénin"] = ("Africa/Porto-Novo", 1), ["Niger"] = ("Africa/Niamey", 1), ["Nigeria"] = ("Africa/Lagos", 1),
        ["Cameroun"] = ("Africa/Douala", 1), ["Tchad"] = ("Africa/Ndjamena", 1), ["Gabon"] = ("Africa/Libreville", 1),
        ["Congo"] = ("Africa/Brazzaville", 1), ["RDC (Congo-Kinshasa)"] = ("Africa/Kinshasa", 1),
        ["République centrafricaine"] = ("Africa/Bangui", 1), ["Guinée équatoriale"] = ("Africa/Malabo", 1),
        ["Angola"] = ("Africa/Luanda", 1), ["Algérie"] = ("Africa/Algiers", 1), ["Tunisie"] = ("Africa/Tunis", 1),
        ["Maroc"] = ("Africa/Casablanca", 1),
        // UTC+2
        ["Burundi"] = ("Africa/Bujumbura", 2), ["Rwanda"] = ("Africa/Kigali", 2), ["Malawi"] = ("Africa/Blantyre", 2),
        ["Mozambique"] = ("Africa/Maputo", 2), ["Zambie"] = ("Africa/Lusaka", 2), ["Zimbabwe"] = ("Africa/Harare", 2),
        ["Botswana"] = ("Africa/Gaborone", 2), ["Namibie"] = ("Africa/Windhoek", 2), ["Afrique du Sud"] = ("Africa/Johannesburg", 2),
        ["Lesotho"] = ("Africa/Maseru", 2), ["Eswatini"] = ("Africa/Mbabane", 2), ["Égypte"] = ("Africa/Cairo", 2),
        ["Libye"] = ("Africa/Tripoli", 2), ["Soudan"] = ("Africa/Khartoum", 2), ["Soudan du Sud"] = ("Africa/Juba", 2),
        // UTC+3 / +4
        ["Kenya"] = ("Africa/Nairobi", 3), ["Ouganda"] = ("Africa/Kampala", 3), ["Tanzanie"] = ("Africa/Dar_es_Salaam", 3),
        ["Éthiopie"] = ("Africa/Addis_Ababa", 3), ["Érythrée"] = ("Africa/Asmara", 3), ["Somalie"] = ("Africa/Mogadishu", 3),
        ["Djibouti"] = ("Africa/Djibouti", 3), ["Comores"] = ("Indian/Comoro", 3), ["Madagascar"] = ("Indian/Antananarivo", 3),
        ["Maurice"] = ("Indian/Mauritius", 4), ["Seychelles"] = ("Indian/Mahe", 4),
        // Europa / Amerika (Sommerzeit über die Systemzone)
        ["France"] = ("Europe/Paris", 1), ["Belgique"] = ("Europe/Brussels", 1), ["Suisse"] = ("Europe/Zurich", 1),
        ["Luxembourg"] = ("Europe/Luxembourg", 1), ["Monaco"] = ("Europe/Monaco", 1), ["Allemagne"] = ("Europe/Berlin", 1),
        ["Italie"] = ("Europe/Rome", 1), ["Espagne"] = ("Europe/Madrid", 1), ["Royaume-Uni"] = ("Europe/London", 0),
        ["Canada"] = ("America/Toronto", -5), ["États-Unis"] = ("America/New_York", -5), ["Haïti"] = ("America/Port-au-Prince", -5),
    };

    private static readonly ConcurrentDictionary<string, TimeZoneInfo> Cache = new();

    /// <summary>Lomé (UTC+0): Standard für Läden ohne bekanntes Land.</summary>
    public static TimeZoneInfo Standard { get; } = Erzeuge("Africa/Lome", 0);

    /// <summary>Hat eNOTE eine Ortszeit für dieses Land hinterlegt?</summary>
    public static bool Kennt(string? land) => land is not null && Laender.ContainsKey(land.Trim());

    /// <summary>Kurzer Versatz-Text wie « UTC+1 » (für Hinweise in Formularen).</summary>
    public static string VersatzText(string? land, DateTime utc)
    {
        var v = Versatz(Fuer(land), utc);
        return v == TimeSpan.Zero ? "UTC+0" : $"UTC{(v < TimeSpan.Zero ? "−" : "+")}{Math.Abs(v.TotalHours):0.#}";
    }

    public static TimeZoneInfo Fuer(string? land)
        => land is not null && Laender.TryGetValue(land.Trim(), out var z) ? Cache.GetOrAdd(z.Iana, _ => Erzeuge(z.Iana, z.Stunden)) : Standard;

    private static TimeZoneInfo Erzeuge(string iana, double stunden)
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(iana, out var zone)) return zone;
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(iana, out var windows) && TimeZoneInfo.TryFindSystemTimeZoneById(windows, out zone)) return zone;
        return TimeZoneInfo.CreateCustomTimeZone(iana, TimeSpan.FromHours(stunden), iana, iana);
    }

    /// <summary>UTC → Ortszeit des Ladens.</summary>
    public static DateTime ImLaden(this DateTime utc, TimeZoneInfo zone)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    /// <summary>Ortszeit des Ladens → UTC.</summary>
    public static DateTime NachUtc(DateTime lokal, TimeZoneInfo zone)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(lokal, DateTimeKind.Unspecified), zone);

    /// <summary>Heutiges Datum im Laden.</summary>
    public static DateTime Heute(TimeZoneInfo zone, DateTime utcJetzt) => utcJetzt.ImLaden(zone).Date;

    /// <summary>Kalendertage des Ladens (von–bis einschließlich) als UTC-Bereich [von, bis].</summary>
    public static (DateTime VonUtc, DateTime BisUtc) TageInUtc(DateTime vonTag, DateTime bisTag, TimeZoneInfo zone)
        => (NachUtc(vonTag.Date, zone), NachUtc(bisTag.Date.AddDays(1), zone).AddTicks(-1));

    /// <summary>Versatz des Ladens zu UTC zum angegebenen Zeitpunkt (für das Gruppieren nach Tagen).</summary>
    public static TimeSpan Versatz(TimeZoneInfo zone, DateTime utc) => zone.GetUtcOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
}
