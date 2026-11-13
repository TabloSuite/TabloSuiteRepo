namespace Skoulou.Core.Fehler;

/// <summary>Fachlicher Fehler mit französischer Meldung für die Oberfläche (z. B. Validierung).</summary>
public class FachFehler(string meldung, string? feld = null) : Exception(meldung)
{
    /// <summary>Betroffenes Feld (für die Anzeige am Feld), optional.</summary>
    public string? Feld { get; } = feld;
}

/// <summary>Fehlendes Recht: Seite zeigt einen Hinweis, Endpunkt antwortet 403.</summary>
public class KeinZugriffFehler(string meldung = KeinZugriffFehler.Standard) : Exception(meldung)
{
    public const string Standard = "Vous n'avez pas accès à cette fonction.";
}

/// <summary>Ergebnis einer Aktion ohne Ausnahme (Erfolg mit Wert oder Meldung).</summary>
public sealed record Ergebnis<T>(bool Erfolg, T? Wert, string? Meldung)
{
    public static Ergebnis<T> Ok(T wert) => new(true, wert, null);
    public static Ergebnis<T> Fehler(string meldung) => new(false, default, meldung);
}
