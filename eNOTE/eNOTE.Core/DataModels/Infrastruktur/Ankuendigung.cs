namespace ENote.Core.DataModels.Infrastruktur;

/// <summary>Art einer Ankündigung (bestimmt Symbol und Farbe des Banners).</summary>
public enum AnkuendigungArt { Nouveaute = 0, Maintenance = 1, Tarif = 2, Information = 3 }

/// <summary>Wer die Ankündigung in der App sieht.</summary>
public enum AnkuendigungZiel { Proprietaires = 0, ProprietairesEtCaissiers = 1, Tous = 2 }

/// <summary>
/// Ankündigung der Plattform (z. B. App-Update, Wartung, neuer Tarif). Erscheint als Banner oben
/// in der App, bis der Nutzer sie schließt oder sie abläuft.
/// </summary>
public class Ankuendigung
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AnkuendigungArt Art { get; set; }
    public AnkuendigungZiel Ziel { get; set; }
    public string Titel { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    /// <summary>Optionaler Link (App-intern, z. B. „/abonnement“, oder https://…).</summary>
    public string? LinkUrl { get; set; }
    public string? LinkText { get; set; }
    public DateTime ErstelltUtc { get; set; } = DateTime.UtcNow;
    public Guid? ErstelltVonId { get; set; }
    /// <summary>Ab wann das Banner erscheint (Standard: sofort).</summary>
    public DateTime SichtbarAbUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Danach verschwindet das Banner automatisch (leer = bis zur Deaktivierung).</summary>
    public DateTime? SichtbarBisUtc { get; set; }
    public bool IstAktiv { get; set; } = true;
    /// <summary>Geplante Aktualisierung: Beginn des Zeitfensters (UTC = heure de Lomé).</summary>
    public DateTime? WartungVonUtc { get; set; }
    /// <summary>Geplante Aktualisierung: Ende des Zeitfensters; danach verschwindet das Banner.</summary>
    public DateTime? WartungBisUtc { get; set; }
    /// <summary>Anzahl der E-Mails, die beim Veröffentlichen verschickt wurden (0 = nur in der App).</summary>
    public int MailsGesendet { get; set; }
}

/// <summary>Ein Nutzer hat die Ankündigung gesehen und geschlossen.</summary>
public class AnkuendigungGelesen
{
    public Guid AnkuendigungId { get; set; }
    public Guid UserId { get; set; }
    public DateTime GelesenUtc { get; set; } = DateTime.UtcNow;
}
