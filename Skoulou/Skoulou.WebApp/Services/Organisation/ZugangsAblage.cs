using Microsoft.Extensions.Caching.Memory;

namespace Skoulou.WebApp.Services.Organisation;

/// <summary>Stapel neuer Zugangsdaten (Anlage, Import, Zurücksetzen) für Anzeige und Zugangsliste-PDF.</summary>
public sealed record ZugangsStapel(Guid Id, Guid BesitzerId, Guid SchuleId, DateTime ErstelltUtc, IReadOnlyList<Zugang> Zugaenge);

/// <summary>
/// Kurzzeit-Ablage im Arbeitsspeicher (30 Minuten) für Zugangsdaten und erzeugte Dateien:
/// Startpasswörter werden nie gespeichert – nur so lange gehalten, bis die Direktion die Liste gedruckt hat.
/// Zugriff nur für das Konto, das den Stapel erzeugt hat.
/// </summary>
public sealed class ZugangsAblage(IMemoryCache cache, TimeProvider zeit)
{
    public static readonly TimeSpan Dauer = TimeSpan.FromMinutes(30);

    private static string Schluessel(string art, Guid id) => $"org:{art}:{id:N}";

    public ZugangsStapel Ablegen(Guid besitzerId, Guid schuleId, IReadOnlyList<Zugang> zugaenge)
    {
        var stapel = new ZugangsStapel(Guid.NewGuid(), besitzerId, schuleId, zeit.GetUtcNow().UtcDateTime, zugaenge);
        cache.Set(Schluessel("acces", stapel.Id), stapel, Dauer);
        return stapel;
    }

    /// <summary>Stapel nur für seinen Besitzer; abgelaufen oder fremd → null.</summary>
    public ZugangsStapel? Holen(Guid stapelId, Guid besitzerId)
        => cache.TryGetValue(Schluessel("acces", stapelId), out ZugangsStapel? s) && s is not null && s.BesitzerId == besitzerId ? s : null;

    /// <summary>Beliebiges Objekt (z. B. Import-Vorschau) für den Besitzer ablegen.</summary>
    public Guid AblegenObjekt<T>(Guid besitzerId, T wert) where T : class
    {
        var id = Guid.NewGuid();
        cache.Set(Schluessel(typeof(T).Name, id), (besitzerId, (object)wert), Dauer);
        return id;
    }

    public T? HolenObjekt<T>(Guid id, Guid besitzerId) where T : class
        => cache.TryGetValue(Schluessel(typeof(T).Name, id), out (Guid Besitzer, object Wert) e) && e.Besitzer == besitzerId ? e.Wert as T : null;

    public void Entfernen<T>(Guid id) => cache.Remove(Schluessel(typeof(T).Name, id));
}
