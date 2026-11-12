namespace TabloSuiteHome.WebApp.Services;

/// <summary>Stand einer App auf der Firmenseite.</summary>
public enum AppStatus
{
    /// <summary>Live: Karte mit « Découvrir » und Anmeldung.</summary>
    Disponible,

    /// <summary>In Arbeit: Karte mit Hinweis « Bientôt disponible », ohne Links.</summary>
    Bientot,
}

/// <summary>
/// Eine App der TabloSuiteMappe, wie sie auf tablosuite.com erscheint. Neue App = neuer Eintrag in
/// appsettings.json (« TabloSuite:Apps ») plus Logo/Bild in wwwroot/img/apps – kein Code nötig.
/// </summary>
public sealed class TabloSuiteApp
{
    /// <summary>Kurzname für Anker und Bilder, z. B. « enote ».</summary>
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Für wen (eine Zeile), z. B. « Caisse et gestion pour salons, boutiques et cabinets ».</summary>
    public string Kategorie { get; set; } = string.Empty;
    public string Beschreibung { get; set; } = string.Empty;
    public List<string> Punkte { get; set; } = [];

    /// <summary>Adresse der App (https), z. B. https://enote.tablosuite.com.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Pfad der Registrierung relativ zur App-Adresse, z. B. « /auth/register ».</summary>
    public string? Registrierung { get; set; }
    public string RegistrierungText { get; set; } = "Inscrire mon établissement";

    /// <summary>Kurzer Zusatz unter den Knöpfen, z. B. « 3 mois d'essai offerts ».</summary>
    public string? Hinweis { get; set; }

    /// <summary>Logo und Bildschirmbild relativ zu wwwroot.</summary>
    public string Logo { get; set; } = string.Empty;
    public string Bild { get; set; } = string.Empty;
    public string BildAlt { get; set; } = string.Empty;

    /// <summary>Akzentfarbe der Karte (CSS-Farbe).</summary>
    public string Farbe { get; set; } = "#2f4ec9";
    public AppStatus Status { get; set; } = AppStatus.Disponible;
    public int Reihenfolge { get; set; }

    public bool IstVerfuegbar => Status == AppStatus.Disponible;
    public string Domain => Uri.TryCreate(Url, UriKind.Absolute, out var u) ? u.Host : Url;
    public string? RegistrierungUrl => string.IsNullOrWhiteSpace(Registrierung) ? null : Url.TrimEnd('/') + "/" + Registrierung.TrimStart('/');
}

/// <summary>Firmenangaben und App-Katalog (Abschnitt « TabloSuite » in appsettings.json).</summary>
public sealed class TabloSuiteOptionen
{
    public const string Abschnitt = "TabloSuite";

    public string KontaktEmail { get; set; } = "contact@tablosuite.com";
    public string? Telefon { get; set; }
    public string Ort { get; set; } = "Lomé, Togo";
    public List<TabloSuiteApp> Apps { get; set; } = [];

    /// <summary>Apps in Anzeigereihenfolge: verfügbare zuerst, dann « bientôt ».</summary>
    public IReadOnlyList<TabloSuiteApp> SortierteApps => Apps
        .OrderBy(a => a.Status)
        .ThenBy(a => a.Reihenfolge)
        .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>Prüft den Katalog beim Start; Fehler verhindern, dass eine kaputte Karte live geht.</summary>
    public IReadOnlyList<string> Pruefe()
    {
        var fehler = new List<string>();
        if (Apps.Count == 0) fehler.Add("Aucune application dans « TabloSuite:Apps ».");
        foreach (var dopplung in Apps.GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            fehler.Add($"Id en double : « {dopplung.Key} ».");
        foreach (var a in Apps)
        {
            var name = string.IsNullOrWhiteSpace(a.Name) ? a.Id : a.Name;
            if (string.IsNullOrWhiteSpace(a.Id) || !a.Id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
                fehler.Add($"« {name} » : Id invalide (minuscules, chiffres, tirets).");
            if (string.IsNullOrWhiteSpace(a.Name)) fehler.Add($"« {a.Id} » : nom manquant.");
            if (string.IsNullOrWhiteSpace(a.Kategorie) || string.IsNullOrWhiteSpace(a.Beschreibung))
                fehler.Add($"« {name} » : catégorie ou description manquante.");
            if (string.IsNullOrWhiteSpace(a.Logo)) fehler.Add($"« {name} » : logo manquant.");
            if (a.IstVerfuegbar && !(Uri.TryCreate(a.Url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps))
                fehler.Add($"« {name} » : adresse https manquante.");
        }
        return fehler;
    }
}
