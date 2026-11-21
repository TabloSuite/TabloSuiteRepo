namespace Skoulou.Core.Werkzeuge;

/// <summary>
/// Zentrale Uhr der App. In Produktion immer die Systemuhr (<see cref="TimeProvider.System"/>) – Verhalten wie
/// <c>DateTime.UtcNow</c>/<c>Now</c>/<c>Today</c>. Nur für Klicktests über Monate und Jahre wird <see cref="Provider"/>
/// vor dem Start durch eine verstellbare Uhr ersetzt (Startup-Hook außerhalb des Repositorys).
/// Deshalb in App-Code nie <c>DateTime.UtcNow</c> direkt, sondern diese Werte bzw. den <see cref="TimeProvider"/> aus DI
/// (der ist dieselbe Uhr) – sonst sehen Seiten und Dienste unterschiedliche Zeiten.
/// </summary>
public static class AppUhr
{
    public static TimeProvider Provider { get; set; } = TimeProvider.System;

    /// <summary>Wie <c>DateTime.UtcNow</c> (Kind = Utc).</summary>
    public static DateTime UtcNow => Provider.GetUtcNow().UtcDateTime;

    /// <summary>Wie <c>DateTimeOffset.UtcNow</c>.</summary>
    public static DateTimeOffset UtcNowOffset => Provider.GetUtcNow();

    /// <summary>Wie <c>DateTime.Now</c> (Ortszeit des Servers, Kind = Local).</summary>
    public static DateTime Now => DateTime.SpecifyKind(Provider.GetLocalNow().DateTime, DateTimeKind.Local);

    /// <summary>Wie <c>DateTime.Today</c> (Kind = Local).</summary>
    public static DateTime Today => Now.Date;
}
