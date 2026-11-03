namespace ENote.Core.DataModels.Identity;

/// <summary>
/// Fachliche Rollen der Anwendung. Die Sichtbare Bezeichnung von
/// <see cref="SuperAdmin"/> in der Oberfläche ist immer „Administrateur“.
/// </summary>
public static class AppRollen
{
    public const string SuperAdmin = "SuperAdmin";
    public const string ProduktOwner = "ProduktOwner";
    public const string Besitzer = "Besitzer";
    public const string Kassierer = "Kassierer";

    public static readonly string[] Alle = [SuperAdmin, ProduktOwner, Besitzer, Kassierer];
}
